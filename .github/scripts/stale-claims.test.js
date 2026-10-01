const assert = require('node:assert/strict');
const { test } = require('node:test');
const releaseStaleClaims = require('./stale-claims.js');

const Now = Date.parse('2026-10-01T15:00:00Z');
const hoursAgo = hours => new Date(Now - hours * 60 * 60 * 1000).toISOString();

// A small fake of the GitHub API: issues and pull requests share numbers, as on GitHub.
function world({ issues = [], prs = [], comments = {}, events = {}, commits = {} }) {
  const records = new Map();
  for (const issue of issues) records.set(issue.number, { pull_request: undefined, state: 'open', ...issue });
  for (const pr of prs) records.set(pr.number, { state: 'open', draft: false, body: '', ...pr, pull_request: {} });
  const posted = [];
  const github = {
    rest: {
      pulls: { list: 'pulls.list' },
      issues: {
        listForRepo: 'issues.listForRepo',
        listComments: 'issues.listComments',
        listEvents: 'issues.listEvents',
        addLabels: async ({ issue_number, labels }) => {
          const record = records.get(issue_number);
          record.labels = [...new Set([...record.labels, ...labels])];
        },
        removeLabel: async ({ issue_number, name }) => {
          const record = records.get(issue_number);
          if (!record.labels.includes(name)) throw Object.assign(new Error('missing'), { status: 404 });
          record.labels = record.labels.filter(label => label !== name);
        },
        createComment: async ({ issue_number, body }) => posted.push({ number: issue_number, body }),
      },
      repos: {
        getCommit: async ({ ref }) => {
          if (!(ref in commits)) throw Object.assign(new Error('missing'), { status: 404 });
          return { data: { commit: { committer: { date: commits[ref] } } } };
        },
      },
    },
    paginate: async (method, params) => {
      if (method === 'pulls.list') return [...records.values()].filter(r => r.pull_request && r.state === 'open');
      if (method === 'issues.listForRepo') {
        return [...records.values()].filter(r => r.state === 'open' && r.labels.includes(params.labels));
      }
      if (method === 'issues.listComments') return comments[params.issue_number] ?? [];
      if (method === 'issues.listEvents') return events[params.issue_number] ?? [];
      throw new Error(`unexpected ${method}`);
    },
  };
  const run = (dryRun = false) => releaseStaleClaims({
    github, context: { repo: { owner: 'compoodment', repo: 'ClankerWorld' } }, core: { info() {} }, now: Now, dryRun,
  });
  return { records, posted, run };
}

const claimed = (number, hours) => ({ [number]: [{ event: 'labeled', label: { name: 'status:in-progress' }, created_at: hoursAgo(hours) }] });

test('an issue claim with no work for 4 hours goes back to the queue', async () => {
  const state = world({
    issues: [{ number: 1, labels: ['type:feature', 'priority:p2', 'status:in-progress'] }],
    events: claimed(1, 5),
    comments: { 1: [{ body: 'Working on this.', created_at: hoursAgo(5) }] },
  });
  await state.run();
  assert.deepEqual(state.records.get(1).labels, ['type:feature', 'priority:p2', 'status:needs-pr']);
  assert.equal(state.posted.length, 1);
  assert.match(state.posted[0].body, /Claim released/);
  assert.match(state.posted[0].body, /No pushed work was found/);
});

test('a recent commit on a linked draft keeps the claim', async () => {
  const state = world({
    issues: [{ number: 1, labels: ['status:in-progress', 'status:has-pr'] }],
    prs: [{ number: 9, draft: true, body: 'Closes #1', labels: [], head: { sha: 'abc', ref: 'codex/1-work' } }],
    events: claimed(1, 10),
    commits: { abc: hoursAgo(1) },
  });
  await state.run();
  assert.ok(state.records.get(1).labels.includes('status:in-progress'));
  assert.equal(state.posted.length, 0);
});

test('a recent comment, or a commit on a branch named in a comment, keeps the claim', async () => {
  const state = world({
    issues: [
      { number: 1, labels: ['status:in-progress'] },
      { number: 2, labels: ['status:in-progress'] },
    ],
    events: { ...claimed(1, 9), ...claimed(2, 9) },
    comments: {
      1: [{ body: 'Still running the full suite.', created_at: hoursAgo(1) }],
      2: [{ body: 'Working on branch `codex/2-fix`.', created_at: hoursAgo(9) }],
    },
    commits: { 'codex/2-fix': hoursAgo(2) },
  });
  await state.run();
  assert.ok(state.records.get(1).labels.includes('status:in-progress'));
  assert.ok(state.records.get(2).labels.includes('status:in-progress'));
});

test('an abandoned draft is named so the next agent continues it, and gets a note', async () => {
  const state = world({
    issues: [{ number: 1, labels: ['status:in-progress', 'status:has-pr'] }],
    prs: [{ number: 9, draft: true, body: 'Closes #1', labels: [], head: { sha: 'abc', ref: 'codex/1-work' } }],
    events: claimed(1, 11),
    commits: { abc: hoursAgo(11) },
  });
  await state.run();
  assert.deepEqual(new Set(state.records.get(1).labels), new Set(['status:has-pr', 'status:needs-pr']));
  assert.match(state.posted.find(c => c.number === 1).body, /Continue draft #9 \(`codex\/1-work`\)/);
  assert.ok(state.posted.some(c => c.number === 9));
});

test('blocked issues lose the stale claim but do not join the queue; owner waits keep it', async () => {
  const state = world({
    issues: [
      { number: 1, labels: ['status:in-progress', 'status:blocked'] },
      { number: 2, labels: ['status:in-progress', 'status:needs-decision'] },
      { number: 3, labels: ['status:in-progress'] },
    ],
    prs: [{ number: 9, draft: true, body: 'Refs #3', labels: ['status:needs-decision'], head: { sha: 'abc', ref: 'codex/3' } }],
    events: { ...claimed(1, 6), ...claimed(2, 30), ...claimed(3, 30) },
  });
  await state.run();
  assert.deepEqual(state.records.get(1).labels, ['status:blocked']);
  assert.ok(state.records.get(2).labels.includes('status:in-progress'));
  assert.ok(state.records.get(3).labels.includes('status:in-progress'));
});

test("the script's own comments never count as work, and a dry run changes nothing", async () => {
  const stale = () => world({
    issues: [{ number: 1, labels: ['status:in-progress'] }],
    events: claimed(1, 8),
    comments: { 1: [{ body: '<!-- claim-check -->\nClaim released earlier.', created_at: hoursAgo(1) }] },
  });
  const dry = stale();
  await dry.run(true);
  assert.deepEqual(dry.records.get(1).labels, ['status:in-progress']);
  assert.equal(dry.posted.length, 0);
  const real = stale();
  await real.run();
  assert.deepEqual(real.records.get(1).labels, ['status:needs-pr']);
});

test('review claims are released after 2 hours without a commit or comment', async () => {
  const reviewing = (number, hours) => ({ [number]: [{ event: 'labeled', label: { name: 'status:reviewing' }, created_at: hoursAgo(hours) }] });
  const state = world({
    prs: [
      { number: 7, labels: ['status:reviewing', 'status:needs-review'], head: { sha: 'old', ref: 'a' } },
      { number: 8, labels: ['status:reviewing', 'status:needs-review'], head: { sha: 'new', ref: 'b' } },
    ],
    events: { ...reviewing(7, 3), ...reviewing(8, 3) },
    commits: { old: hoursAgo(5), new: hoursAgo(1) },
  });
  await state.run();
  assert.deepEqual(state.records.get(7).labels, ['status:needs-review']);
  assert.ok(state.records.get(8).labels.includes('status:reviewing'));
  assert.equal(state.posted.length, 1);
  assert.match(state.posted[0].body, /Review claim released/);
});

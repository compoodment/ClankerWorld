const assert = require('node:assert/strict');
const { test } = require('node:test');
const releaseStaleClaims = require('./stale-claims.js');

const Now = Date.parse('2026-10-01T15:00:00Z');
const hoursAgo = hours => new Date(Now - hours * 60 * 60 * 1000).toISOString();

// A small fake of the GitHub API: issues and pull requests share numbers, as on GitHub.
function world({ issues = [], prs = [], comments = {}, events = {}, commits = {}, beforeCommit = () => {},
  beforeRemove = () => {}, beforeAdd = () => {}, beforeComment = () => {} }) {
  const records = new Map();
  for (const issue of issues) records.set(issue.number, { pull_request: undefined, state: 'open', ...issue });
  for (const pr of prs) records.set(pr.number, { state: 'open', draft: false, body: '', ...pr, pull_request: {} });
  const posted = [];
  const github = {
    rest: {
      pulls: { list: 'pulls.list', get: async ({ pull_number }) => ({ data: structuredClone(records.get(pull_number)) }) },
      issues: {
        listForRepo: 'issues.listForRepo',
        listComments: 'issues.listComments',
        listEvents: 'issues.listEvents',
        get: async ({ issue_number }) => ({ data: structuredClone(records.get(issue_number)) }),
        addLabels: async ({ issue_number, labels }) => {
          const record = records.get(issue_number);
          beforeAdd({ issue_number, labels, record, records, comments, events });
          for (const name of labels.filter(name => !record.labels.includes(name))) {
            (events[issue_number] ??= []).push({ event: 'labeled', label: { name }, created_at: hoursAgo(0) });
            record.updated_at = hoursAgo(0);
          }
          record.labels = [...new Set([...record.labels, ...labels])];
        },
        removeLabel: async ({ issue_number, name }) => {
          const record = records.get(issue_number);
          beforeRemove({ issue_number, name, record, records, comments, events });
          if (!record.labels.includes(name)) throw Object.assign(new Error('missing'), { status: 404 });
          record.labels = record.labels.filter(label => label !== name);
          (events[issue_number] ??= []).push({ event: 'unlabeled', label: { name }, created_at: hoursAgo(0) });
          record.updated_at = hoursAgo(0);
        },
        createComment: async ({ issue_number, body }) => {
          beforeComment({ issue_number, body, records, comments, events });
          posted.push({ number: issue_number, body });
          (comments[issue_number] ??= []).push({ id: posted.length, body, created_at: hoursAgo(0), updated_at: hoursAgo(0) });
          records.get(issue_number).updated_at = hoursAgo(0);
        },
      },
      repos: {
        getCommit: async ({ ref }) => {
          beforeCommit({ ref, records, comments, events });
          if (!(ref in commits)) throw Object.assign(new Error('missing'), { status: 404 });
          return { data: { commit: { committer: { date: commits[ref] } } } };
        },
      },
    },
    paginate: async (method, params) => {
      if (method === 'pulls.list') return structuredClone([...records.values()].filter(r => r.pull_request && r.state === 'open'));
      if (method === 'issues.listForRepo') {
        return structuredClone([...records.values()].filter(r => r.state === 'open' && r.labels.includes(params.labels)));
      }
      if (method === 'issues.listComments') return structuredClone(comments[params.issue_number] ?? []);
      if (method === 'issues.listEvents') return structuredClone(events[params.issue_number] ?? []);
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

test('owner-wait review claims remain claimed', async () => {
  const state = world({
    prs: [{ number: 9, labels: ['status:reviewing', 'status:needs-decision'], head: { sha: 'old', ref: 'codex/9' } }],
    events: { 9: [{ event: 'labeled', label: { name: 'status:reviewing' }, created_at: hoursAgo(6) }] },
    commits: { old: hoursAgo(8) },
  });
  await state.run();
  assert.ok(state.records.get(9).labels.includes('status:reviewing'));
  assert.equal(state.posted.length, 0);
});

test('a freshly opened draft keeps an issue claim even when its commit is older', async () => {
  const state = world({
    issues: [{ number: 1, labels: ['status:in-progress', 'status:has-pr'] }],
    prs: [{ number: 9, draft: true, body: 'Closes #1', labels: [], created_at: hoursAgo(0.1),
      head: { sha: 'old', ref: 'codex/1' } }],
    events: claimed(1, 8), commits: { old: hoursAgo(8) },
  });
  await state.run();
  assert.ok(state.records.get(1).labels.includes('status:in-progress'));
  assert.equal(state.posted.length, 0);
});

test('recent PR activity keeps an older-commit claim but release comments do not', async () => {
  for (const ownComment of [false, true]) {
    const state = world({
      issues: [{ number: 1, labels: ['status:in-progress', 'status:has-pr'] }],
      prs: [{ number: 9, draft: true, body: 'Closes #1', labels: [], created_at: hoursAgo(10),
        updated_at: hoursAgo(0.1), head: { sha: 'old', ref: 'codex/1' } }],
      events: claimed(1, 8), commits: { old: hoursAgo(8) },
      comments: { 9: ownComment ? [{ body: '<!-- claim-check -->\nEarlier cleanup.', created_at: hoursAgo(0.1) }] : [] },
    });
    await state.run();
    assert.equal(state.records.get(1).labels.includes('status:in-progress'), !ownComment);
  }
});

test('fresh progress during a commit lookup prevents releasing the claim', async () => {
  let changed = false;
  const state = world({
    issues: [{ number: 1, labels: ['status:in-progress'] }],
    prs: [{ number: 9, draft: true, body: 'Refs #1', labels: [], head: { sha: 'old', ref: 'codex/1' } }],
    events: claimed(1, 8), commits: { old: hoursAgo(8) },
    beforeCommit({ comments }) {
      if (!changed) { changed = true; comments[1] = [{ body: 'Fresh progress.', created_at: hoursAgo(0) }]; }
    },
  });
  await state.run();
  assert.ok(state.records.get(1).labels.includes('status:in-progress'));
  assert.equal(state.posted.length, 0);
});

test('a reclaim immediately before removal survives cleanup without a queue label', async () => {
  let reclaimed = false;
  const state = world({
    issues: [{ number: 1, labels: ['status:in-progress'] }], events: claimed(1, 8),
    beforeRemove({ issue_number, name, events }) {
      if (issue_number === 1 && name === 'status:in-progress' && !reclaimed) {
        reclaimed = true;
        events[1].push({ event: 'labeled', label: { name: 'status:in-progress' }, created_at: hoursAgo(0) });
      }
    },
  });
  await state.run();
  assert.deepEqual(state.records.get(1).labels, ['status:in-progress']);
  assert.equal(state.posted.length, 0);
});

test('ready handoff during cleanup cannot restore needs-pr', async () => {
  let handedOff = false;
  const state = world({
    issues: [{ number: 1, labels: ['status:in-progress', 'status:has-pr'] }],
    prs: [{ number: 9, draft: true, body: 'Closes #1', labels: [], head: { sha: 'old', ref: 'codex/1' } }],
    events: claimed(1, 8), commits: { old: hoursAgo(8) },
    beforeRemove({ issue_number, name, records }) {
      if (issue_number === 1 && name === 'status:in-progress' && !handedOff) {
        handedOff = true; records.get(9).draft = false; records.get(1).labels = ['status:has-pr'];
      }
    },
  });
  await state.run();
  assert.deepEqual(state.records.get(1).labels, ['status:has-pr']);
  assert.equal(state.posted.length, 0);
});

test('a reclaim during the queue write keeps its claim and removes needs-pr', async () => {
  let reclaimed = false;
  const state = world({
    issues: [{ number: 1, labels: ['status:in-progress'] }], events: claimed(1, 8),
    beforeAdd({ issue_number, labels, record, events }) {
      if (issue_number === 1 && labels.includes('status:needs-pr') && !reclaimed) {
        reclaimed = true; record.labels = ['status:in-progress'];
        events[1].push({ event: 'labeled', label: { name: 'status:in-progress' }, created_at: hoursAgo(0) });
      }
    },
  });
  await state.run();
  assert.deepEqual(state.records.get(1).labels, ['status:in-progress']);
  assert.equal(state.posted.length, 0);
});

test('the latest named branch is checked instead of five historical branches', async () => {
  const state = world({
    issues: [{ number: 1, labels: ['status:in-progress'] }], events: claimed(1, 8),
    comments: { 1: [...Array.from({ length: 5 }, (_, index) => ({
      body: `Old branch \`codex/old-${index}\`.`, created_at: hoursAgo(10),
    })), { body: 'Current branch `codex/current`.', created_at: hoursAgo(8) }] },
    commits: { 'codex/current': hoursAgo(1) },
  });
  await state.run();
  assert.ok(state.records.get(1).labels.includes('status:in-progress'));
});

test('manual cleanup is restricted to main and checks out main explicitly', () => {
  const { readFileSync } = require('node:fs');
  const workflow = readFileSync(`${__dirname}/../workflows/stale-claims.yml`, 'utf8');
  assert.match(workflow, /release:\s*\n\s+if:\s+github\.ref == 'refs\/heads\/main'/);
  assert.match(workflow, /uses: actions\/checkout@v5\s*\n\s+with:\s*\n\s+ref: main\s*\n\s+persist-credentials: false/);
});

test('fresh review claims and new heads survive a cleanup already in flight', async () => {
  for (const change of ['claim', 'head']) {
    let changed = false;
    const mutate = ({ events, records }) => {
      if (changed) return;
      changed = true;
      if (change === 'claim') events[9].push({ event: 'labeled', label: { name: 'status:reviewing' }, created_at: hoursAgo(0) });
      else records.get(9).head.sha = 'replacement';
    };
    const state = world({
      prs: [{ number: 9, labels: ['status:reviewing', 'status:needs-review'], head: { sha: 'old', ref: 'codex/9' } }],
      events: { 9: [{ event: 'labeled', label: { name: 'status:reviewing' }, created_at: hoursAgo(8) }] },
      commits: { old: hoursAgo(8), replacement: hoursAgo(8) },
      ...(change === 'claim' ? { beforeRemove: mutate } : { beforeCommit: mutate }),
    });
    await state.run();
    assert.ok(state.records.get(9).labels.includes('status:reviewing'));
    assert.equal(state.posted.length, 0);
  }
});

test('a ready handoff during the queue write removes the obsolete queue entry', async () => {
  let changed = false;
  const state = world({
    issues: [{ number: 1, labels: ['status:in-progress', 'status:has-pr'] }],
    prs: [{ number: 9, draft: true, body: 'Closes #1', labels: [], head: { sha: 'old', ref: 'codex/1' } }],
    events: claimed(1, 8), commits: { old: hoursAgo(8) },
    beforeAdd({ issue_number, labels, record, records }) {
      if (issue_number === 1 && labels.includes('status:needs-pr') && !changed) {
        changed = true; records.get(9).draft = false; record.labels = ['status:has-pr'];
      }
    },
  });
  await state.run();
  assert.deepEqual(state.records.get(1).labels, ['status:has-pr']);
  assert.equal(state.posted.length, 0);
});

test('closing the issue during cleanup cannot recreate a work queue entry', async () => {
  const state = world({
    issues: [{ number: 1, labels: ['status:in-progress'] }], events: claimed(1, 8),
    beforeRemove({ issue_number, name, record }) {
      if (issue_number === 1 && name === 'status:in-progress') record.state = 'closed';
    },
  });
  await state.run();
  assert.deepEqual(state.records.get(1).labels, []);
  assert.equal(state.posted.length, 0);
});

test('a late draft-label write cannot erase a released draft from the queue', async () => {
  const state = world({
    issues: [{ number: 1, labels: ['status:in-progress', 'status:has-pr'] }],
    prs: [{ number: 9, draft: true, body: 'Closes #1', labels: [], head: { sha: 'old', ref: 'codex/1' } }],
    events: claimed(1, 8), commits: { old: hoursAgo(8) },
    beforeComment({ issue_number, records }) {
      if (issue_number === 1) records.get(1).labels = ['status:has-pr'];
    },
  });
  await state.run();
  assert.deepEqual(new Set(state.records.get(1).labels), new Set(['status:has-pr', 'status:needs-pr']));
  const notes = state.posted.length;
  await state.run();
  assert.equal(state.posted.length, notes);
});

test('edited progress comments count while automated review notes remain ignored', async () => {
  const state = world({
    issues: [{ number: 1, labels: ['status:in-progress'] }], events: claimed(1, 8),
    prs: [{ number: 9, labels: ['status:reviewing', 'status:needs-review'], updated_at: hoursAgo(0.1),
      head: { sha: 'old', ref: 'codex/9' } }],
    comments: {
      1: [{ body: 'Updated progress.', created_at: hoursAgo(8), updated_at: hoursAgo(0.1) }],
      9: [{ body: '<!-- claim-check -->\nEarlier review note.', created_at: hoursAgo(0.1) }],
    },
    commits: { old: hoursAgo(8) },
    beforeRemove() {},
  });
  await state.run();
  assert.ok(state.records.get(1).labels.includes('status:in-progress'));
  assert.ok(!state.records.get(9).labels.includes('status:reviewing'));
});

test('cleanup label writes do not restore a stale review claim or repeat its note', async () => {
  const state = world({
    prs: [{ number: 9, labels: ['status:reviewing', 'status:needs-review'], created_at: hoursAgo(10),
      updated_at: hoursAgo(8), head: { sha: 'old', ref: 'codex/9' } }],
    events: { 9: [{ event: 'labeled', label: { name: 'status:reviewing' }, created_at: hoursAgo(8) }] },
    commits: { old: hoursAgo(8) },
  });
  await state.run();
  assert.deepEqual(state.records.get(9).labels, ['status:needs-review']);
  assert.equal(state.records.get(9).updated_at, hoursAgo(0));
  assert.ok(state.posted.some(note => note.number === 9 && /Review claim released/.test(note.body)));
  const notes = state.posted.length;
  await state.run();
  assert.deepEqual(state.records.get(9).labels, ['status:needs-review']);
  assert.equal(state.posted.length, notes);
});

test('label-only PR updates do not freshen an abandoned draft but later activity does', async () => {
  for (const laterActivity of [false, true]) {
    const state = world({
      issues: [{ number: 1, labels: ['status:in-progress', 'status:has-pr'] }],
      prs: [{ number: 9, draft: true, body: 'Closes #1', labels: ['type:feature'], created_at: hoursAgo(10),
        updated_at: hoursAgo(laterActivity ? 0.1 : 0.2), head: { sha: 'old', ref: 'codex/1' } }],
      events: { ...claimed(1, 8), 9: [{ event: 'labeled', label: { name: 'type:feature' }, created_at: hoursAgo(0.2) }] },
      commits: { old: hoursAgo(8) },
    });
    await state.run();
    assert.equal(state.records.get(1).labels.includes('status:in-progress'), laterActivity);
    assert.equal(state.records.get(1).labels.includes('status:needs-pr'), !laterActivity);
  }
});

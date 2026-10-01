const assert = require('node:assert/strict');
const { test } = require('node:test');
const labelPullRequest = require('./pr-labels.js');

function scenario({ live = {}, event = {}, action = 'edited', changes, files = ['src/ClankerWorld.Simulation/Kernel/InventoryFixture.cs'],
  issueLabels = ['priority:p2', 'status:needs-pr', 'status:in-progress'], otherPrs = [], beforeRemove = () => {} } = {}) {
  const pr = {
    number: 25, state: 'open', draft: false, merged: false,
    body: '- [x] Bug fix\nCloses #4', labels: ['status:reviewing'], ...live,
  };
  const issue = { number: 4, state: 'open', labels: [...issueLabels] };
  const records = new Map([[25, pr], [4, issue]]);
  let reads = 0;
  const github = {
    rest: {
      pulls: {
        get: async ({ pull_number }) => {
          assert.equal(pull_number, 25);
          reads++;
          return { data: structuredClone(pr) };
        },
        listFiles: 'files', list: 'pulls',
      },
      issues: {
        get: async ({ issue_number }) => ({ data: structuredClone(records.get(issue_number)) }),
        addLabels: async ({ issue_number, labels }) => {
          const record = records.get(issue_number);
          record.labels = [...new Set([...record.labels, ...labels])];
        },
        removeLabel: async ({ issue_number, name }) => {
          const record = records.get(issue_number);
          beforeRemove({ issue_number, name, record, otherPrs });
          record.labels = record.labels.filter(label => label !== name);
        },
      },
    },
    paginate: async method => method === 'files' ? files.map(filename => ({ filename })) : otherPrs,
  };
  const context = {
    repo: { owner: 'compoodment', repo: 'ClankerWorld' },
    payload: { action, pull_request: { ...structuredClone(pr), ...event }, changes },
  };
  return { pr, issue, reads: () => reads, run: () => labelPullRequest({ github, context, core: { info() {} } }) };
}

test('queued draft edit preserves a live ready PR review claim and clears its linked issue claim', async () => {
  const state = scenario({ event: { draft: true, labels: [] } });
  await state.run();
  assert.equal(state.reads(), 1);
  assert.ok(state.pr.labels.includes('status:reviewing'));
  assert.ok(state.pr.labels.includes('status:needs-review'));
  assert.ok(state.issue.labels.includes('status:has-pr'));
  assert.ok(!state.issue.labels.includes('status:in-progress'));
  assert.ok(!state.issue.labels.includes('status:needs-pr'));
});

test('stale ready event cleans up a live closed PR and releases its open issue', async () => {
  const state = scenario({
    action: 'ready_for_review', event: { state: 'open', draft: false },
    live: { state: 'closed', labels: ['status:needs-review', 'status:reviewing'] },
    issueLabels: ['priority:p2', 'status:has-pr'],
  });
  await state.run();
  assert.deepEqual(state.pr.labels, []);
  assert.deepEqual(state.issue.labels, ['priority:p2', 'status:needs-pr']);
});

test('stale close event does not release a live reopened PR or its reviewer claim', async () => {
  const state = scenario({ action: 'closed', event: { state: 'closed', merged: true } });
  await state.run();
  assert.ok(state.pr.labels.includes('status:reviewing'));
  assert.ok(state.pr.labels.includes('status:needs-review'));
  assert.ok(state.issue.labels.includes('status:has-pr'));
});

test('live draft cleans review labels without clearing its linked issue work claim', async () => {
  const state = scenario({
    action: 'ready_for_review', event: { draft: false },
    live: { draft: true, labels: ['status:needs-review', 'status:reviewing'] },
  });
  await state.run();
  assert.ok(!state.pr.labels.includes('status:needs-review'));
  assert.ok(!state.pr.labels.includes('status:reviewing'));
  assert.ok(state.issue.labels.includes('status:in-progress'));
  assert.ok(state.issue.labels.includes('status:has-pr'));
});

test('current closed event preserves another open PR and an independent issue claim', async () => {
  const state = scenario({
    action: 'closed', live: { state: 'closed' },
    issueLabels: ['priority:p1', 'status:has-pr', 'status:in-progress'],
    otherPrs: [{ number: 26, body: 'Fixes #4' }],
  });
  await state.run();
  assert.deepEqual(state.pr.labels, []);
  assert.deepEqual(state.issue.labels, ['priority:p1', 'status:has-pr', 'status:in-progress']);
});

test('area, type and priority use current files and body; stale edits cannot release a current link', async () => {
  const state = scenario({
    action: 'ready_for_review', event: { body: 'Refs #4', labels: ['type:feature', 'priority:p3'] },
    live: { labels: ['type:feature', 'priority:p3', 'area:world', 'status:reviewing'] },
    changes: { body: { from: 'Closes #4' } },
    files: ['.github/scripts/pr-labels.js'],
  });
  await state.run();
  assert.deepEqual(new Set(state.pr.labels), new Set([
    'area:tooling', 'type:bug', 'priority:p1', 'status:reviewing', 'status:needs-review',
  ]));
  assert.ok(state.issue.labels.includes('status:has-pr'));
});

test('dropping a closing reference in the current body releases the issue even if the payload retained it', async () => {
  const state = scenario({
    live: { body: '- [x] Bug fix\nRefs #4' }, event: { body: 'Closes #4' },
    changes: { body: { from: 'Closes #4' } }, issueLabels: ['priority:p2', 'status:has-pr'],
  });
  await state.run();
  assert.deepEqual(state.issue.labels, ['priority:p2', 'status:needs-pr']);
});

test('cleanup restores an issue link acquired by another PR after the initial check', async () => {
  const state = scenario({
    action: 'closed', live: { state: 'closed' },
    issueLabels: ['priority:p2', 'status:has-pr', 'status:in-progress'],
    beforeRemove({ issue_number, name, record, otherPrs }) {
      if (issue_number === 4 && name === 'status:has-pr') {
        otherPrs.push({ number: 26, body: 'Closes #4', draft: true });
        record.labels = [...new Set([...record.labels, 'status:has-pr'])];
      }
    },
  });
  await state.run();
  assert.deepEqual(state.pr.labels, []);
  assert.deepEqual(new Set(state.issue.labels), new Set(['priority:p2', 'status:has-pr', 'status:in-progress']));
});

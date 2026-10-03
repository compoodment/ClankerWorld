const assert = require('node:assert/strict');
const { test } = require('node:test');
const labelPullRequest = require('./pr-labels.js');
const { closingIssueNumbers, referencedIssueNumbers, typeLabel } = labelPullRequest;

const Bot = { login: 'github-actions[bot]' };
const Person = { login: 'compoodment' };
const labeled = (name, actor = Bot) => ({ event: 'labeled', label: { name }, actor });
const unlabeled = (name, actor = Bot) => ({ event: 'unlabeled', label: { name }, actor });

function scenario({ live = {}, event = {}, action = 'edited', changes, files = ['src/ClankerWorld.Simulation/Kernel/InventoryFixture.cs'],
  issueLabels = ['priority:p2', 'status:needs-pr', 'status:in-progress'], otherPrs = [], otherIssues = {},
  compareFiles = [], compareError = null, events, recordEvents = true, beforeRemove = () => {}, beforeAdd = () => {} } = {}) {
  const pr = {
    number: 25, state: 'open', draft: false, merged: false,
    body: '- [x] Bug fix\nCloses #4', labels: ['status:reviewing'],
    base: { ref: 'main', repo: { default_branch: 'main' } }, head: { sha: 'head-sha' }, ...live,
  };
  const issue = { number: 4, state: 'open', labels: [...issueLabels] };
  const records = new Map([[25, pr], [4, issue]]);
  for (const [number, labels] of Object.entries(otherIssues)) {
    records.set(Number(number), { number: Number(number), state: 'open', labels: [...labels] });
  }
  // Labels already on the PR were added by this workflow unless a test says otherwise.
  const prEvents = events ?? pr.labels.map(name => labeled(name));
  const compares = [];
  const warnings = [];
  const removals = [];
  let changedFiles = files;
  let reads = 0;
  let eventReads = 0;
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
      repos: {
        compareCommitsWithBasehead: async ({ basehead }) => {
          compares.push(basehead);
          if (compareError) throw compareError;
          return { data: { files: compareFiles.map(filename => ({ filename })) } };
        },
      },
      issues: {
        get: async ({ issue_number }) => {
          if (!records.has(issue_number)) throw Object.assign(new Error('Not Found'), { status: 404 });
          return { data: structuredClone(records.get(issue_number)) };
        },
        addLabels: async ({ issue_number, labels }) => {
          const record = records.get(issue_number);
          beforeAdd({ issue_number, labels, record, pr, issue });
          if (issue_number === 25 && recordEvents) {
            prEvents.push(...labels.filter(name => !record.labels.includes(name)).map(name => labeled(name)));
          }
          record.labels = [...new Set([...record.labels, ...labels])];
        },
        removeLabel: async ({ issue_number, name }) => {
          const record = records.get(issue_number);
          beforeRemove({ issue_number, name, record, otherPrs, pr, issue });
          removals.push({ issue_number, name });
          if (issue_number === 25 && recordEvents && record.labels.includes(name)) prEvents.push(unlabeled(name));
          record.labels = record.labels.filter(label => label !== name);
        },
        listEvents: 'events',
      },
    },
    paginate: async method => {
      if (method === 'files') return changedFiles.map(filename => ({ filename }));
      if (method === 'events') {
        eventReads++;
        return structuredClone(prEvents);
      }
      return otherPrs;
    },
  };
  const context = {
    repo: { owner: 'compoodment', repo: 'ClankerWorld' },
    payload: { action, pull_request: { ...structuredClone(pr), ...event }, changes },
  };
  return {
    pr, issue, records, compares, warnings, removals, reads: () => reads, eventReads: () => eventReads,
    run: () => labelPullRequest({ github, context, core: { info() {}, warning: message => warnings.push(message) } }),
    // The next event on the same PR, such as a push after its base changed.
    next({ action: nextAction, changes: nextChanges, files: nextFiles } = {}) {
      context.payload = { action: nextAction, pull_request: structuredClone(pr), changes: nextChanges };
      if (nextFiles) changedFiles = nextFiles;
    },
  };
}

test('queued draft edit preserves a live ready PR review claim and clears its linked issue claim', async () => {
  const state = scenario({ event: { draft: true, labels: [] } });
  await state.run();
  assert.equal(state.reads(), 2);
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
    'area:tooling', 'type:bug', 'priority:p0', 'status:reviewing', 'status:needs-review',
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

test('a PR closing during an open handler cannot finish with review or linked-issue labels restored', async () => {
  const state = scenario({
    beforeAdd({ issue_number, labels, pr }) {
      if (issue_number === 25 && labels.includes('status:needs-review')) pr.state = 'closed';
    },
  });
  await state.run();
  assert.ok(!state.pr.labels.includes('status:needs-review'));
  assert.ok(!state.pr.labels.includes('status:reviewing'));
  assert.deepEqual(state.issue.labels, ['priority:p2', 'status:needs-pr']);
});

test('a PR reopening during cleanup retains its new reviewer claim and current issue link', async () => {
  const state = scenario({
    action: 'closed', live: { state: 'closed', labels: ['status:needs-review'] },
    issueLabels: ['priority:p2', 'status:has-pr'],
    beforeRemove({ issue_number, name, pr }) {
      if (issue_number === 25 && name === 'status:needs-review') {
        pr.state = 'open';
        pr.labels.push('status:reviewing');
      }
    },
  });
  await state.run();
  assert.ok(state.pr.labels.includes('status:needs-review'));
  assert.ok(state.pr.labels.includes('status:reviewing'));
  assert.deepEqual(state.issue.labels, ['priority:p2', 'status:has-pr']);
});

for (const action of ['opened', 'edited', 'synchronize']) {
  for (const alreadyLinked of [false, true]) {
    test(`a released draft stays findable through ${action}, already linked: ${alreadyLinked}`, async () => {
      const state = scenario({ action, live: { draft: true, labels: [] },
        issueLabels: ['priority:p2', 'status:needs-pr', ...(alreadyLinked ? ['status:has-pr'] : [])] });
      for (let pass = 0; pass < 2; pass++) {
        await state.run();
        assert.deepEqual(new Set(state.issue.labels), new Set(['priority:p2', 'status:has-pr', 'status:needs-pr']));
      }
    });
  }
}

test('reclaimed drafts and ready handoffs clear the abandoned queue entry', async () => {
  for (const alreadyLinked of [false, true]) {
    for (const draft of [true, false]) {
      const state = scenario({ live: { draft, labels: [] },
        issueLabels: ['status:needs-pr', ...(alreadyLinked ? ['status:has-pr'] : []), ...(draft ? ['status:in-progress'] : [])] });
      for (let pass = 0; pass < 2; pass++) {
        await state.run();
        assert.deepEqual(new Set(state.issue.labels), new Set(['status:has-pr', ...(draft ? ['status:in-progress'] : [])]));
      }
    }
  }
});

test('a late abandoned-draft reconciliation cannot queue an issue held by another ready PR', async () => {
  for (const alreadyLinked of [false, true]) {
    const state = scenario({ live: { draft: true, labels: [] },
      issueLabels: ['status:needs-pr', ...(alreadyLinked ? ['status:has-pr'] : [])],
      otherPrs: [{ number: 26, state: 'open', draft: false, body: 'Closes #4' }] });
    for (let pass = 0; pass < 2; pass++) {
      await state.run();
      assert.deepEqual(state.issue.labels, ['status:has-pr']);
    }
  }
});

test('released draft reconciliation respects current issue and PR holds before and after first labeling', async () => {
  for (const alreadyLinked of [false, true]) {
    for (const hold of ['status:in-progress', 'status:blocked', 'status:needs-decision', 'status:parked', 'type:decision', 'owner-task']) {
      const state = scenario({ live: { draft: true, labels: [] },
        issueLabels: ['status:needs-pr', hold, ...(alreadyLinked ? ['status:has-pr'] : [])] });
      for (let pass = 0; pass < 2; pass++) {
        await state.run();
        assert.deepEqual(new Set(state.issue.labels), new Set([hold, 'status:has-pr']), hold);
      }
    }
    for (const hold of ['status:blocked', 'status:needs-decision']) {
      const state = scenario({ live: { draft: true, labels: [hold] },
        issueLabels: ['status:needs-pr', ...(alreadyLinked ? ['status:has-pr'] : [])] });
      for (let pass = 0; pass < 2; pass++) {
        await state.run();
        assert.deepEqual(state.issue.labels, ['status:has-pr'], hold);
        assert.ok(state.pr.labels.includes(hold));
      }
    }
  }
});

const priorities = labels => labels.filter(name => name.startsWith('priority:')).sort();
const areas = labels => labels.filter(name => name.startsWith('area:')).sort();
const stacked = { ref: 'codex/1-lower', repo: { default_branch: 'main' } };
const cognition = 'src/ClankerWorld.Simulation/Cognition/AgentMind.cs';

test('a stacked PR is judged only by the files that also differ from main', async () => {
  const state = scenario({
    action: 'opened', live: { base: stacked, labels: [] },
    files: ['AGENTS.md', '.github/scripts/stale-claims.js', cognition], compareFiles: [cognition, 'src/Other.cs'],
  });
  await state.run();
  assert.deepEqual(state.compares, ['main...head-sha']);
  assert.deepEqual(new Set(state.pr.labels), new Set(['area:agents', 'type:bug', 'status:needs-review', 'priority:p2']));
});

test('the docs-only type fallback ignores files that only differ from a stale base', async () => {
  const state = scenario({
    live: { base: stacked, body: 'Closes #4', labels: [] },
    files: ['.github/scripts/stale-claims.js', 'docs/playing.md'], compareFiles: ['docs/playing.md'],
  });
  await state.run();
  assert.ok(state.pr.labels.includes('type:docs'));
  assert.deepEqual(priorities(state.pr.labels), ['priority:p2']);
});

test('a workflow P0 this workflow added goes once the PR no longer changes workflow files on main', async () => {
  const state = scenario({
    action: 'synchronize', live: { base: stacked, labels: ['priority:p0'] },
    files: ['CONTRIBUTING.md', cognition], compareFiles: [cognition],
  });
  await state.run();
  assert.deepEqual(priorities(state.pr.labels), ['priority:p2']);
});

test('a P0 set by hand stays on a stacked PR that changes no workflow files on main', async () => {
  const state = scenario({
    action: 'synchronize', live: { base: stacked, labels: ['priority:p0'] },
    events: [labeled('priority:p0', Person)],
    files: ['CONTRIBUTING.md', cognition], compareFiles: [cognition],
  });
  await state.run();
  assert.deepEqual(priorities(state.pr.labels), ['priority:p0']);
});

test('a PR based on main is not compared again and still gets P0 for workflow files', async () => {
  const state = scenario({ action: 'synchronize', live: { labels: [] }, files: ['AGENTS.md'] });
  await state.run();
  assert.deepEqual(state.compares, []);
  assert.deepEqual(priorities(state.pr.labels), ['priority:p0']);
});

test('a comparison cut short at 300 files keeps the PR\'s own file list', async () => {
  const state = scenario({
    action: 'synchronize', live: { base: stacked, labels: [] },
    files: ['AGENTS.md'], compareFiles: Array.from({ length: 300 }, (_, i) => `src/File${i}.cs`),
  });
  await state.run();
  assert.deepEqual(priorities(state.pr.labels), ['priority:p0']);
});

test('a failed comparison with main falls back to the PR\'s own files and still writes every label', async () => {
  for (const status of [404, 500]) {
    const state = scenario({
      action: 'ready_for_review', live: { base: stacked, labels: [] },
      files: ['.github/scripts/pr-labels.js'], compareFiles: [],
      compareError: Object.assign(new Error('Server Error'), { status }),
    });
    await state.run();
    assert.deepEqual(state.compares, ['main...head-sha'], status);
    assert.deepEqual(new Set(state.pr.labels), new Set([
      'area:tooling', 'type:bug', 'status:needs-review', 'priority:p0',
    ]), status);
    assert.deepEqual(state.issue.labels, ['priority:p2', 'status:has-pr'], status);
    assert.equal(state.warnings.length, 1, status);
    assert.match(state.warnings[0], new RegExp(`Could not compare with main \\(${status}\\)`));
  }
});

test('changing the base branch works out the areas again; other edits leave them', async () => {
  for (const [changes, expected] of [
    [{ base: { ref: { from: 'codex/1-lower' } } }, ['area:agents']],
    [{ body: { from: '- [x] Bug fix\nCloses #4' } }, ['area:tooling']],
  ]) {
    const state = scenario({ live: { labels: ['area:tooling'] }, changes, files: [cognition] });
    await state.run();
    assert.deepEqual(areas(state.pr.labels), expected);
  }
});

test('a push after a stacked PR moves to main works out its areas again from its own files', async () => {
  const upper = [1, 2, 3].map(i => `src/ClankerWorld.Simulation/Cognition/Mind${i}.cs`);
  const lower = Array.from({ length: 10 }, (_, i) => `src/ClankerWorld.Simulation/World/Terrain${i}.cs`);
  // The lower PR was squash-merged and GitHub moved this one to main, but its
  // file list still holds the lower PR's files until main is merged in.
  const state = scenario({
    changes: { base: { ref: { from: 'codex/1-lower' }, sha: { from: 'lower-sha' } } },
    live: { labels: [] }, files: [...upper, ...lower],
  });
  await state.run();
  assert.deepEqual(areas(state.pr.labels), ['area:world']);
  state.next({ action: 'synchronize', files: upper });
  await state.run();
  assert.deepEqual(areas(state.pr.labels), ['area:agents']);
});

test('a push that leaves the areas as they are does not read the label history', async () => {
  const state = scenario({ action: 'synchronize', live: { labels: ['area:world', 'priority:p2'] }, issueLabels: ['priority:p2'] });
  await state.run();
  assert.deepEqual(areas(state.pr.labels), ['area:world']);
  assert.equal(state.eventReads(), 0);
});

test('a closing PR sent back to draft puts its unheld issue back in the queue', async () => {
  const state = scenario({
    action: 'converted_to_draft', live: { draft: true, labels: [] },
    issueLabels: ['priority:p2', 'status:has-pr'],
  });
  await state.run();
  await state.run();
  assert.deepEqual(new Set(state.issue.labels), new Set(['priority:p2', 'status:has-pr', 'status:needs-pr']));
});

test('a draft PR whose issue is held, waiting or not PR work leaves the issue out of the queue', async () => {
  for (const held of ['status:in-progress', 'status:blocked', 'status:needs-decision', 'status:parked', 'type:decision', 'owner-task']) {
    const state = scenario({
      action: 'converted_to_draft', live: { draft: true, labels: [] },
      issueLabels: ['priority:p2', 'status:has-pr', held],
    });
    await state.run();
    assert.deepEqual(new Set(state.issue.labels), new Set(['priority:p2', 'status:has-pr', held]), held);
  }
});

test('a draft PR waiting on the owner or blocked leaves its issue out of the queue', async () => {
  for (const held of ['status:needs-decision', 'status:blocked']) {
    const state = scenario({
      action: 'converted_to_draft', live: { draft: true, labels: [held] },
      issueLabels: ['priority:p2', 'status:has-pr'],
    });
    await state.run();
    assert.deepEqual(state.issue.labels, ['priority:p2', 'status:has-pr'], held);
  }
});

test('a hold added after the PR went back to draft takes its issue out of the queue at the next push or edit', async () => {
  for (const [held, action] of [['status:needs-decision', 'synchronize'], ['status:blocked', 'edited']]) {
    const state = scenario({
      action: 'converted_to_draft', live: { draft: true, labels: [] },
      issueLabels: ['priority:p2', 'status:has-pr'],
    });
    await state.run();
    assert.deepEqual(new Set(state.issue.labels), new Set(['priority:p2', 'status:has-pr', 'status:needs-pr']), held);
    // Adding the label does not run the workflow; the next push or edit does.
    state.pr.labels.push(held);
    state.next({ action });
    await state.run();
    assert.deepEqual(state.issue.labels, ['priority:p2', 'status:has-pr'], held);
  }
});

test('a PR sent back to draft does not queue an issue another ready PR closes, even briefly', async () => {
  const state = scenario({
    action: 'converted_to_draft', live: { draft: true, labels: [] },
    issueLabels: ['priority:p2', 'status:has-pr'],
    otherPrs: [{ number: 26, state: 'open', draft: false, body: 'Closes #4' }],
    beforeAdd({ issue_number, labels }) {
      assert.ok(!(issue_number === 4 && labels.includes('status:needs-pr')), 'status:needs-pr was added');
    },
  });
  await state.run();
  assert.deepEqual(state.issue.labels, ['priority:p2', 'status:has-pr']);
});

test('a hand-back undone by a concurrent ready-state run is put back when that run sees the draft', async () => {
  // An edit event's run read the PR as ready; the reviewer then converted it to
  // draft, and that run queued the issue before this run removed the label.
  const state = scenario({
    action: 'edited', live: { draft: false, labels: [] },
    issueLabels: ['priority:p2', 'status:has-pr', 'status:needs-pr'],
    beforeRemove: ({ name, pr }) => { if (name === 'status:needs-pr') pr.draft = true; },
  });
  await state.run();
  assert.deepEqual(new Set(state.issue.labels), new Set(['priority:p2', 'status:has-pr', 'status:needs-pr']));
});

test('other events on a draft do not queue an issue that only has status:has-pr', async () => {
  for (const action of ['edited', 'synchronize']) {
    const state = scenario({ action, live: { draft: true, labels: [] }, issueLabels: ['priority:p2', 'status:has-pr'] });
    await state.run();
    assert.deepEqual(state.issue.labels, ['priority:p2', 'status:has-pr'], action);
  }
});

test('a retired or hand-made status label does not stop a released issue going back to the queue', async () => {
  const state = scenario({
    action: 'closed', live: { state: 'closed', labels: [] },
    issueLabels: ['priority:p2', 'status:has-pr', 'status:needs-playtest'],
  });
  await state.run();
  assert.deepEqual(state.issue.labels, ['priority:p2', 'status:needs-playtest', 'status:needs-pr']);
});

test('a holding status still keeps a released issue out of the queue', async () => {
  for (const held of ['status:blocked', 'status:parked', 'status:needs-decision']) {
    const state = scenario({
      action: 'closed', live: { state: 'closed', labels: [] },
      issueLabels: ['priority:p2', 'status:has-pr', held],
    });
    await state.run();
    assert.deepEqual(state.issue.labels, ['priority:p2', held], held);
  }
});

test('a closed unmerged PR releases Decision and owner-task issues without queueing them', async () => {
  for (const kind of ['type:decision', 'owner-task']) {
    const state = scenario({
      action: 'closed', live: { state: 'closed', labels: [] },
      issueLabels: [kind, 'priority:p2', 'status:has-pr'],
    });
    await state.run();
    assert.deepEqual(state.issue.labels, [kind, 'priority:p2'], kind);
  }
});

test('dropping the closing keyword for an owner task releases it without queueing it', async () => {
  const state = scenario({
    live: { body: '- [x] Bug fix\nRefs #4' }, event: { body: 'Closes #4' },
    changes: { body: { from: 'Closes #4' } }, issueLabels: ['owner-task', 'priority:p2', 'status:has-pr'],
  });
  await state.run();
  assert.deepEqual(state.issue.labels, ['owner-task', 'priority:p2']);
});

test('only a keyword directly before a number closes an issue', () => {
  const repo = 'compoodment/ClankerWorld';
  assert.deepEqual([...closingIssueNumbers('- Closes (each issue this completes): #12', repo)], []);
  assert.deepEqual([...closingIssueNumbers('- Closes (each issue this completes): Closes #12, closes #13', repo)], [12, 13]);
  assert.deepEqual([...closingIssueNumbers('Fixes #12 and #13', repo)], [12]);
  assert.deepEqual([...closingIssueNumbers('- Closes #630: see #500', repo)], [630]);
  assert.deepEqual([...closingIssueNumbers('Resolves other/repo#9, resolves compoodment/clankerworld#10', repo)], [10]);
});

test('Refs reads every issue in a list after it', () => {
  const repo = 'compoodment/ClankerWorld';
  const refs = body => [...referencedIssueNumbers(body, repo)];
  assert.deepEqual(refs('Refs #7. Closes #4'), [7]);
  assert.deepEqual(refs('- Refs #7, #8 (and what remains)'), [7, 8]);
  assert.deepEqual(refs('- Refs #598, #599 and #602: resident limits'), [598, 599, 602]);
  assert.deepEqual(refs('- Refs #562 and #563 for the shared part'), [562, 563]);
  assert.deepEqual(refs('Refs: #7, #8'), [7, 8]);
  assert.deepEqual(refs('Refs #7,#8, and #9 & #10'), [7, 8, 9, 10]);
  assert.deepEqual(refs('- Refs #666, the continuity rule implemented by prerequisite PR #689'), [666]);
  // A note after the list's colon is not more references.
  assert.deepEqual(refs('- Refs #666: the continuity rule from prerequisite PR #689'), [666]);
  assert.deepEqual(refs('- Refs #  (and what remains)'), []);
  assert.deepEqual(refs('- Refs #  (and what remains): #12, #13'), [12, 13]);
  assert.deepEqual(refs('- Refs #  (and what remains): #666, the continuity rule from prerequisite PR #689'), [666]);
  assert.deepEqual(refs('- Refs #  (and what remains): none; overlaps #700'), []);
});

test('Refs still reads the older template line, and ignores other repositories, comments and code', () => {
  const repo = 'compoodment/ClankerWorld';
  const refs = body => [...referencedIssueNumbers(body, repo)];
  assert.deepEqual(refs('- Refs (related or partly completed issues, and what remains): #7, #8'), [7, 8]);
  assert.deepEqual(refs('- Refs (related or partly completed issues, and what remains): #7 for the rest, other/repo#9'), [7]);
  assert.deepEqual(refs('Refs other/repo#9, #10 and compoodment/clankerworld#11'), [10, 11]);
  assert.deepEqual(refs('Refs other/repo#9'), []);
  assert.deepEqual(refs('<!-- Refs #9 --> `Refs #10`'), []);
  assert.deepEqual(refs('Refs #7, `#8`\n```\nRefs #9, #10\n```'), [7]);
  assert.deepEqual(refs('- Refs (and what remains): see PR#5 and notes.md#6'), []);
});

test('the claim cleanup reads Refs with the same parser', () => {
  const staleClaims = require('./stale-claims.js');
  assert.equal(staleClaims.referencedIssueNumbers, referencedIssueNumbers);
});

test('Refactor or tooling sets type:tooling, and the first ticked box wins', async () => {
  assert.equal(typeLabel('- [x] Refactor or tooling'), 'type:tooling');
  assert.equal(typeLabel('- [x] Documentation\n- [x] Refactor or tooling'), 'type:docs');
  assert.equal(typeLabel('- [x] Bug fix\n- [x] Refactor or tooling'), 'type:bug');
  assert.equal(typeLabel('- [ ] Refactor or tooling'), null);
  const state = scenario({ live: { body: '- [x] Refactor or tooling\nCloses #4', labels: ['type:feature'] } });
  await state.run();
  assert.ok(state.pr.labels.includes('type:tooling'));
  assert.ok(!state.pr.labels.includes('type:feature'));
});

test('areas changed by hand survive opening, reopening, ready for review and pushes', async () => {
  for (const action of ['opened', 'reopened', 'ready_for_review', 'synchronize']) {
    const added = scenario({
      action, live: { labels: ['area:interface'] },
      events: [labeled('area:world'), unlabeled('area:world', Person), labeled('area:interface', Person)],
    });
    await added.run();
    assert.deepEqual(areas(added.pr.labels), ['area:interface'], action);
    const removed = scenario({
      action, live: { labels: [] }, events: [labeled('area:world'), unlabeled('area:world', Person)],
    });
    await removed.run();
    assert.deepEqual(areas(removed.pr.labels), [], action);
  }
});

test('areas stay as they are when no area rule matches the changed files', async () => {
  const state = scenario({ action: 'ready_for_review', live: { labels: ['area:world'] }, files: ['README.md', 'changes/4-fix.md'] });
  await state.run();
  assert.deepEqual(areas(state.pr.labels), ['area:world']);
});

test('process files map to an area, but changelog and playtest files do not', async () => {
  const { areaLabels } = labelPullRequest;
  for (const file of ['CONTRIBUTING.md', 'AGENTS.md', 'CLAUDE.md', 'docs/development/build-and-test.md', 'docs/development/releasing.md']) {
    assert.deepEqual(areaLabels([file]), ['area:tooling'], file);
  }
  assert.deepEqual(areaLabels(['docs/development/private-server-deployment.md']), ['area:server']);
  assert.deepEqual(areaLabels([cognition, 'changes/4-fix.md', 'playtest/4-fix.md', 'docs/what-works.md']), ['area:agents']);
  const state = scenario({ action: 'opened', live: { labels: [] }, files: ['CONTRIBUTING.md'] });
  await state.run();
  assert.deepEqual(areas(state.pr.labels), ['area:tooling']);
});

test('a Refs-only PR takes the priority of the issues it refers to', async () => {
  const state = scenario({
    live: { body: '- [x] Bug fix\nRefs #7, refs #8', labels: [] },
    otherIssues: { 7: ['priority:p3'], 8: ['priority:p1'] },
  });
  await state.run();
  assert.deepEqual(priorities(state.pr.labels), ['priority:p1']);
  assert.deepEqual(state.issue.labels, ['priority:p2', 'status:needs-pr', 'status:in-progress']);
  assert.deepEqual(state.records.get(8).labels, ['priority:p1']);
});

test('the closing issues\' priority wins over Refs, which counts only when they have none', async () => {
  for (const [issueLabels, expected] of [
    [['priority:p3', 'status:in-progress'], 'priority:p3'],
    [['status:in-progress'], 'priority:p1'],
  ]) {
    const state = scenario({
      live: { body: '- [x] Bug fix\nCloses #4\nRefs #7', labels: [] },
      issueLabels, otherIssues: { 7: ['priority:p1'] },
    });
    await state.run();
    assert.deepEqual(priorities(state.pr.labels), [expected]);
  }
});

test('a PR with no issue and no workflow files has its stale bot priority reset to P2', async () => {
  const state = scenario({ live: { body: '- [x] Bug fix', labels: ['priority:p0'] } });
  await state.run();
  assert.deepEqual(priorities(state.pr.labels), ['priority:p2']);
});

test('a priority set by hand is never removed, and wins only when it is higher', async () => {
  const higher = scenario({ live: { labels: ['priority:p1'] }, events: [labeled('priority:p1', Person)] });
  await higher.run();
  assert.deepEqual(priorities(higher.pr.labels), ['priority:p1']);
  const lower = scenario({ live: { labels: ['priority:p3'] }, events: [labeled('priority:p3', Person)] });
  await lower.run();
  assert.deepEqual(priorities(lower.pr.labels), ['priority:p2', 'priority:p3']);
  const relabeled = scenario({
    live: { labels: ['priority:p0'] },
    events: [labeled('priority:p0'), unlabeled('priority:p0', Person), labeled('priority:p0', Person)],
  });
  await relabeled.run();
  assert.deepEqual(priorities(relabeled.pr.labels), ['priority:p0']);
  // A lower priority this workflow set earlier goes once a higher one is set by hand.
  const overruled = scenario({
    live: { labels: ['priority:p1', 'priority:p2'] },
    events: [labeled('priority:p2'), labeled('priority:p1', Person)],
  });
  await overruled.run();
  assert.deepEqual(priorities(overruled.pr.labels), ['priority:p1']);
});

test('a priority added earlier in the same run counts as the workflow\'s even before its event shows', async () => {
  const state = scenario({
    live: { labels: [] }, recordEvents: false,
    otherIssues: { 7: ['priority:p1'] },
    beforeAdd({ issue_number, labels, pr }) {
      if (issue_number === 25 && labels.includes('priority:p2')) pr.body = '- [x] Bug fix\nRefs #7';
    },
  });
  await state.run();
  assert.deepEqual(priorities(state.pr.labels), ['priority:p1']);
});

test('closing a PR or sending it back to draft ends its merge turn', async () => {
  const closed = scenario({
    action: 'closed', live: { state: 'closed', labels: ['status:needs-review', 'status:reviewing', 'status:merging'] },
    issueLabels: ['priority:p2', 'status:has-pr'],
  });
  await closed.run();
  assert.deepEqual(closed.pr.labels, []);
  const draft = scenario({
    action: 'converted_to_draft', live: { draft: true, labels: ['status:needs-review', 'status:reviewing', 'status:merging'] },
  });
  await draft.run();
  assert.ok(!draft.pr.labels.some(name => ['status:needs-review', 'status:reviewing', 'status:merging'].includes(name)));
});

test('a push to a draft without review labels reads the PR once to clear them and removes nothing', async () => {
  const state = scenario({ action: 'synchronize', live: { draft: true, labels: [] }, issueLabels: ['priority:p2', 'status:has-pr'] });
  await state.run();
  // The run's own read, one read to clear review labels and the final check.
  assert.equal(state.reads(), 3);
  assert.deepEqual(state.removals.filter(removal => removal.issue_number === 25), []);
});

test('sending a PR back to draft removes only the review labels it has', async () => {
  const state = scenario({ action: 'converted_to_draft', live: { draft: true, labels: ['status:reviewing'] } });
  await state.run();
  assert.deepEqual(state.removals.filter(removal => removal.issue_number === 25).map(removal => removal.name), ['status:reviewing']);
  assert.ok(!state.pr.labels.includes('status:reviewing'));
});

test('a PR marked ready while its review labels are being cleared keeps the rest', async () => {
  const state = scenario({
    action: 'converted_to_draft', live: { draft: true, labels: ['status:needs-review', 'status:reviewing'] },
    beforeRemove({ issue_number, name, pr }) {
      if (issue_number === 25 && name === 'status:needs-review') pr.draft = false;
    },
  });
  await state.run();
  assert.ok(state.pr.labels.includes('status:reviewing'));
  assert.ok(state.pr.labels.includes('status:needs-review'));
});

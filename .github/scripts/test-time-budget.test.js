const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { test } = require('node:test');
const checkTestTimeBudget = require('./test-time-budget.js');
const { BudgetSeconds, Marker, Title, Labels } = checkTestTimeBudget;

const Now = new Date('2026-10-12T05:23:00Z');

function duration(seconds) {
  const pad = value => String(Math.floor(value)).padStart(2, '0');
  return `${pad(seconds / 3600)}:${pad((seconds % 3600) / 60)}:${pad(seconds % 60)}`;
}

// Main's timings, one folder per run as main-timings.sh leaves them. `runs` maps a run ID to
// seconds per test.
function timings(t, runs) {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'test-time-budget-'));
  t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
  for (const [id, times] of Object.entries(runs)) {
    const entries = Object.entries(times);
    const tests = entries.map(([name], i) => {
      const dot = name.lastIndexOf('.');
      return `<UnitTest id="t${i}"><TestMethod className="${name.slice(0, dot)}" name="${name.slice(dot + 1)}" /></UnitTest>`;
    });
    const results = entries.map(([, seconds], i) => `<UnitTestResult testId="t${i}" duration="${duration(seconds)}" />`);
    fs.mkdirSync(path.join(dir, id, 'test-timings-1'), { recursive: true });
    fs.writeFileSync(path.join(dir, id, 'test-timings-1', 'timings.trx'),
      `<TestRun><Results>${results.join('')}</Results><TestDefinitions>${tests.join('')}</TestDefinitions></TestRun>`);
  }
  return dir;
}

// Three runs whose tests add up to `total` seconds at their medians; the middle run is the median.
function runsTotalling(total) {
  const scaled = factor => ({
    'ClankerWorld.Simulation.Tests.HugeTests.Slowest': Math.round(total * 0.6 * factor),
    'ClankerWorld.Simulation.Tests.HugeTests.Second': Math.round(total * 0.3 * factor),
    'ClankerWorld.Simulation.Tests.SmallTests.Quick': Math.round(total * 0.1 * factor),
  });
  return { 3001: scaled(0.8), 3002: scaled(1), 3003: scaled(1.3) };
}

// A small fake of the GitHub API's issues; pull requests share the list, as on GitHub.
function world(issues = []) {
  const records = issues.map(issue => ({ state: 'open', labels: [], body: '', ...issue }));
  const writes = [];
  const github = {
    rest: {
      issues: {
        listForRepo: 'issues.listForRepo',
        create: async params => {
          writes.push(['create', params]);
          const number = 900 + records.length;
          records.push({ number, state: 'open', ...params, labels: params.labels.map(name => ({ name })) });
          return { data: { number } };
        },
        update: async params => {
          writes.push(['update', params]);
          Object.assign(records.find(record => record.number === params.issue_number), params);
        },
        createComment: async params => { writes.push(['comment', params]); },
      },
    },
    paginate: async (method, params) => {
      assert.equal(method, 'issues.listForRepo');
      assert.equal(params.state, 'open');
      return structuredClone(records.filter(record => record.state === 'open'));
    },
  };
  const logs = [];
  const core = { info: text => logs.push(text), warning: text => logs.push(`warning: ${text}`) };
  const context = { repo: { owner: 'compoodment', repo: 'ClankerWorld' } };
  const run = (dir, dryRun = false) => checkTestTimeBudget({ github, context, core, dir, now: Now, dryRun });
  return { records, writes, logs, run };
}

test('over the budget, one issue opens with the slowest tests and the labels the queue needs', async t => {
  const { writes, run } = world([{ number: 5, body: 'Unrelated issue' }]);
  const result = await run(timings(t, runsTotalling(BudgetSeconds + 600)));
  assert.deepEqual(result, { action: 'opened', number: 901 });
  assert.equal(writes.length, 1);
  const [kind, params] = writes[0];
  assert.equal(kind, 'create');
  assert.equal(params.title, Title);
  assert.deepEqual(params.labels, ['type:tooling', 'area:tooling', 'priority:p2', 'status:needs-pr']);
  assert.ok(params.body.startsWith(Marker));
  assert.match(params.body, /Main's 3 tests now take \*\*9,600 s\*\* in total, over the budget of \*\*9,000 s\*\*/);
  assert.match(params.body, /median over main's last 3 green CI runs, the newest of them \[run 3003\]\(https:\/\/github\.com\/compoodment\/ClankerWorld\/actions\/runs\/3003\)/);
  assert.match(params.body, /\| HugeTests\.Slowest \| 5760 \|\n\| HugeTests\.Second \| 2880 \|/);
  assert.match(params.body, /\| HugeTests \| 2 \| 8640 \|/);
  assert.match(params.body, /closes the issue once main is 8,550 s or less, unless someone holds it\. Last checked 2026-10-12\./);
});

test('while main stays over the budget, the open issue is refreshed instead of opened again', async t => {
  const { records, writes, run } = world([
    { number: 7, body: `${Marker}\nOld numbers`, labels: [{ name: 'status:in-progress' }] },
    // A pull request quoting the marker is not the issue.
    { number: 8, body: `${Marker} quoted`, pull_request: {} },
  ]);
  const result = await run(timings(t, runsTotalling(BudgetSeconds + 600)));
  assert.deepEqual(result, { action: 'updated', number: 7 });
  assert.deepEqual(writes.map(([kind]) => kind), ['update']);
  assert.equal(writes[0][1].issue_number, 7);
  assert.match(records[0].body, /9,600 s/);
});

test('well under the budget, an unclaimed issue is closed as completed with the new total', async t => {
  const { records, writes, run } = world([{ number: 7, body: `${Marker}\nOld numbers` }]);
  const result = await run(timings(t, runsTotalling(BudgetSeconds * 0.9)));
  assert.deepEqual(result, { action: 'closed', number: 7 });
  assert.deepEqual(writes.map(([kind]) => kind), ['comment', 'update']);
  assert.match(writes[0][1].body, /back under the budget: 8,100 s of 9,000 s/);
  assert.deepEqual([records[0].state, records[0].state_reason], ['closed', 'completed']);
});

test('a held issue, a total just under the budget or no issue at all changes nothing', async t => {
  const held = world([{ number: 7, body: Marker, labels: [{ name: 'status:in-progress' }] }]);
  assert.deepEqual(await held.run(timings(t, runsTotalling(BudgetSeconds * 0.5))), { action: 'none' });
  assert.deepEqual(held.writes, []);
  // Between the budget and a twentieth under it, the issue stays as it is.
  const near = world([{ number: 7, body: Marker }]);
  assert.deepEqual(await near.run(timings(t, runsTotalling(BudgetSeconds * 0.97))), { action: 'none' });
  assert.deepEqual(near.writes, []);
  const none = world();
  assert.deepEqual(await none.run(timings(t, runsTotalling(BudgetSeconds * 0.5))), { action: 'none' });
});

test('with too few runs, or in a dry run, nothing is written', async t => {
  const few = world();
  const runs = runsTotalling(BudgetSeconds * 2);
  delete runs[3001];
  assert.deepEqual(await few.run(timings(t, runs)), { action: 'none' });
  assert.deepEqual(few.writes, []);
  assert.match(few.logs.join('\n'), /warning: Only 2 of main's green runs had test timings; at least 3 are needed\./);
  const dry = world([{ number: 7, body: Marker }]);
  assert.deepEqual(await dry.run(timings(t, runsTotalling(BudgetSeconds * 2)), true), { action: 'updated', number: 7 });
  assert.deepEqual(dry.writes, []);
  assert.match(dry.logs.join('\n'), /Would refresh #7\./);
});

test('the workflow runs weekly on main with main\'s timings and copy of the script', () => {
  const workflow = fs.readFileSync(path.join(__dirname, '..', 'workflows', 'test-time-budget.yml'), 'utf8');
  assert.match(workflow, /schedule:\n\s+- cron: '\d+ \d+ \* \* 1'/);
  assert.match(workflow, /workflow_dispatch:/);
  assert.match(workflow, /issues: write/);
  assert.match(workflow, /actions: read/);
  assert.match(workflow, /if: github\.ref == 'refs\/heads\/main'/);
  assert.match(workflow, /ref: main/);
  assert.match(workflow, /main-timings\.sh "\$RUNNER_TEMP\/main" 5/);
  assert.match(workflow, /require\('\.\/\.github\/scripts\/test-time-budget\.js'\)/);
});

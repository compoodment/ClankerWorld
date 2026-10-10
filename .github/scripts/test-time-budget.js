// Keeps one issue open while main's tests take longer in total than the budget below, so slow creep
// gets noticed even when no single pull request makes tests much slower (test-times in ci.yml warns
// only about one change at a time). Run weekly by .github/workflows/test-time-budget.yml through
// actions/github-script, on main's timings and main's copy of this script.
//
// - The total is the sum of each test's median over main's latest green runs (main-timings.sh), so
//   one slow runner can't open or close the issue.
// - Over the budget: open the issue with the slowest tests, or refresh the one already open.
// - At least a twentieth under the budget: close the issue, unless someone holds it
//   (status:in-progress); their pull request closes it instead. Between the two, nothing changes,
//   so a total near the budget doesn't open and close an issue every week.
// - With timings from fewer than MinRuns runs, do nothing.
// To change the budget, edit BudgetSeconds in a pull request that says why.
const { readMainTimings } = require('./ci-plan.js');

// About a fifth above main's 7,650 seconds in October 2026, after #946 made Town decisions faster.
const BudgetSeconds = 9000;
const CloseBelow = 0.95;
const MinRuns = 3;
const Marker = '<!-- test-time-budget -->';
const Title = "Main's tests take longer than the test-time budget";
const Labels = ['type:tooling', 'area:tooling', 'priority:p2', 'status:needs-pr'];
const InProgress = 'status:in-progress';

function labelNames(labels) {
  return (labels ?? []).map(label => (typeof label === 'string' ? label : label.name));
}

const seconds = value => Math.round(value).toLocaleString('en-US');

// The issue text: the total against the budget, what to do, and the slowest tests and classes.
function issueBody({ times, runs, ids }, { repoUrl, now = new Date() }) {
  const total = Object.values(times).reduce((sum, value) => sum + value, 0);
  const tests = Object.entries(times).sort((a, b) => b[1] - a[1] || (a[0] < b[0] ? -1 : 1));
  const classes = new Map();
  for (const [name, value] of tests) {
    const owner = name.slice(0, name.lastIndexOf('.'));
    const entry = classes.get(owner) ?? { count: 0, seconds: 0 };
    classes.set(owner, { count: entry.count + 1, seconds: entry.seconds + value });
  }
  const heaviest = [...classes].sort((a, b) => b[1].seconds - a[1].seconds || (a[0] < b[0] ? -1 : 1)).slice(0, 10);
  const short = name => name.replace(/^ClankerWorld\.Simulation\.Tests\./, '');
  const newest = ids.map(Number).sort((a, b) => b - a)[0];
  const docs = `${repoUrl}/blob/main/docs/development/build-and-test.md#how-ci-runs`;
  return [
    Marker,
    `Main's ${tests.length.toLocaleString('en-US')} tests now take **${seconds(total)} s** in total, over the budget of ` +
      `**${seconds(BudgetSeconds)} s** in \`.github/scripts/test-time-budget.js\`. Each test counts at its median ` +
      `over main's last ${runs} green CI runs, the newest of them [run ${newest}](${repoUrl}/actions/runs/${newest}).`,
    '',
    'Every merge waits for CI, so slower tests slow every session down. Take tests from the lists below and make ' +
      'them faster without losing what they check: start close to the moment the test checks, in the smallest ' +
      `world that shows the behavior ([how CI runs](${docs})), and use the test-audit skill to judge what each ` +
      'test must keep. If the time is really needed, raise the budget in a pull request that says why.',
    '',
    `A weekly check refreshes this description while main stays over the budget, and closes the issue once main ` +
      `is ${seconds(BudgetSeconds * CloseBelow)} s or less, unless someone holds it. ` +
      `Last checked ${now.toISOString().slice(0, 10)}.`,
    '',
    '### Slowest tests',
    '',
    '| Test | Seconds |', '| --- | --- |',
    ...tests.slice(0, 20).map(([name, value]) => `| ${short(name)} | ${Math.round(value)} |`),
    '',
    '### Slowest classes',
    '',
    '| Class | Tests | Seconds |', '| --- | --- | --- |',
    ...heaviest.map(([name, entry]) => `| ${short(name)} | ${entry.count} | ${Math.round(entry.seconds)} |`),
    '',
  ].join('\n');
}

async function checkTestTimeBudget({ github, context, core, dir, now = new Date(), dryRun = false }) {
  const repo = context.repo;
  const repoUrl = `https://github.com/${repo.owner}/${repo.repo}`;
  const timings = readMainTimings(dir);
  if (timings.runs < MinRuns) {
    core.warning(`Only ${timings.runs} of main's green runs had test timings; at least ${MinRuns} are needed.`);
    return { action: 'none' };
  }
  const total = Object.values(timings.times).reduce((sum, value) => sum + value, 0);
  core.info(`Main's tests take ${seconds(total)} s against a budget of ${seconds(BudgetSeconds)} s, ` +
    `from ${timings.runs} runs.`);
  const open = (await github.paginate(github.rest.issues.listForRepo, { ...repo, state: 'open', per_page: 100 }))
    .filter(issue => !issue.pull_request && (issue.body ?? '').includes(Marker))
    .sort((a, b) => a.number - b.number)[0];

  if (total > BudgetSeconds) {
    const body = issueBody(timings, { repoUrl, now });
    if (open) {
      core.info(`${dryRun ? 'Would refresh' : 'Refreshing'} #${open.number}.`);
      if (!dryRun) await github.rest.issues.update({ ...repo, issue_number: open.number, body });
      return { action: 'updated', number: open.number };
    }
    core.info(`${dryRun ? 'Would open' : 'Opening'} an issue.`);
    if (dryRun) return { action: 'opened' };
    const { data } = await github.rest.issues.create({ ...repo, title: Title, body, labels: Labels });
    return { action: 'opened', number: data.number };
  }
  if (!open || total > BudgetSeconds * CloseBelow) return { action: 'none' };
  if (labelNames(open.labels).includes(InProgress)) {
    core.info(`#${open.number} is under the budget again, but someone holds it; their pull request closes it.`);
    return { action: 'none' };
  }
  core.info(`${dryRun ? 'Would close' : 'Closing'} #${open.number}.`);
  if (dryRun) return { action: 'closed', number: open.number };
  await github.rest.issues.createComment({
    ...repo, issue_number: open.number,
    body: `Main's tests are back under the budget: ${seconds(total)} s of ${seconds(BudgetSeconds)} s, ` +
      `each test at its median over main's last ${timings.runs} green runs. Closing; the weekly check opens a ` +
      'new issue if they pass it again.',
  });
  await github.rest.issues.update({ ...repo, issue_number: open.number, state: 'closed', state_reason: 'completed' });
  return { action: 'closed', number: open.number };
}

module.exports = checkTestTimeBudget;
Object.assign(module.exports, { BudgetSeconds, CloseBelow, MinRuns, Marker, Title, Labels, issueBody });

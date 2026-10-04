// Decides what the Verify workflow (.github/workflows/ci.yml) runs:
//   node ci-plan.js scope < changed-files   prints code= and tests=: whether to run the code checks and the test suite
//   node ci-plan.js matrix                  prints shards=[1,2,...], one entry per test job
//   node ci-plan.js plan <timings-dir>      prints filter_1=... and so on, one dotnet test --filter per test job
//   node ci-plan.js filter <shard>          prints the fixed fallback filter for one test job
//   node ci-plan.js slow <results-dir>      lists a job's slowest tests and warns about very slow ones
// Every test runs in exactly one job: a test belongs to the first shard with an entry that names it,
// and the last shard runs everything no entry names, so new or renamed tests always run.
const fs = require('fs');
const path = require('path');
const { isDocumentation } = require('./pr-labels.js');

// A runner's cores; xUnit runs that many test classes at once, and a class's tests one after another.
const Cores = 4;
// CI warns about a single test slower than this, because no run can finish before its slowest test.
const SlowTestSeconds = 300;

// The fallback split, used when the latest main run's timings can't be read. "Class" names a whole
// test class, apart from tests an earlier shard already names; "Class.Method" names one test method,
// or several that start with that text. Normally `plan` splits the tests from measured timings.
const PinnedShards = [
  [
    'SettlementParenthoodTests.ContinuityDeadlineWithoutARequest',
    'ConcreteMealTests',
    'ToolProgressionRuntimeTests',
    'SpoiledHouseholdDeliveryTests',
  ],
  [
    'SettlementParenthoodTests.ChildCanGrowIntoAWorkingAdult',
    'PersonalEquipmentTests',
    'FoodRoutingTests',
    'WorkstationSourceReserveTests',
  ],
  [
    'SettlementParenthoodTests.OutsideHouseholdRelativeGetsFirstOffer',
    'BusinessTradeTests',
    'SettlementSurvivalTests',
    'HouseRelocationRuntimeTests',
  ],
  [
    'RestaurantBusinessPipelineTests',
    'BuildingExpansionTests',
    'OrnamentProductionTests',
    'PotteryContentTests',
  ],
  [
    'SettlementParenthoodTests.ContinuityResumesPostponedAcceptance',
    'SettlementParenthoodTests.ContinuityCoupleMayPostpone',
    'FarmFieldTests',
    'OrnamentTradeTests',
    'DamagedCheckpointEntryTests',
  ],
];

const EntryPattern = /^[A-Za-z_]\w*(\.[A-Za-z_]\w*)?$/;
// Full names from timings may hold namespaces, nested-class "+" and generic "`"; nothing a filter
// treats specially. A name with anything else is left out of the plan and runs in the last shard.
const FullNamePattern = /^[A-Za-z_][\w.+`]*$/;

// The text a fixed entry must contain in a test's fully qualified name. The leading dot stops
// "FarmFieldTests" matching "OtherFarmFieldTests"; a class entry also ends in a dot.
function namePart(entry) {
  if (!EntryPattern.test(entry)) throw new Error(`Test shard entry "${entry}" must be Class or Class.Method.`);
  return entry.includes('.') ? `.${entry}` : `.${entry}.`;
}

// One filter clause. A fixed entry is a short name from PinnedShards; a planned entry is
// { class: 'Namespace.Class' }, matching the whole class, or { method: 'Namespace.Class.Method' },
// matching exactly that method and every case of it.
function clause(entry, negate) {
  if (typeof entry === 'string') return `FullyQualifiedName${negate ? '!~' : '~'}${namePart(entry)}`;
  if (entry.class) return `FullyQualifiedName${negate ? '!~' : '~'}${entry.class}.`;
  return `FullyQualifiedName${negate ? '!=' : '='}${entry.method}`;
}

function shardCount(shards = PinnedShards) {
  return shards.length + 1;
}

// `shards` lists the named entries of every shard but the last, which runs everything else.
function testFilter(shard, shards = PinnedShards) {
  if (!Number.isInteger(shard) || shard < 1 || shard > shardCount(shards)) {
    throw new Error(`Test shard must be a whole number from 1 to ${shardCount(shards)}.`);
  }
  const excluded = shards.slice(0, shard - 1).flat().map(entry => clause(entry, true));
  if (shard > shards.length) return excluded.join('&');
  const named = shards[shard - 1].map(entry => clause(entry, false)).join('|');
  return [`(${named})`, ...excluded].join('&');
}

function decodeXml(text) {
  const entities = { lt: '<', gt: '>', quot: '"', apos: "'", amp: '&' };
  return text.replace(/&(lt|gt|quot|apos|amp);/g, (_, name) => entities[name]);
}

function attributes(tag) {
  const values = {};
  for (const [, key, value] of tag.matchAll(/(\w+)="([^"]*)"/g)) values[key] = decodeXml(value);
  return values;
}

function durationSeconds(duration) {
  const parts = /^(\d+):(\d+):(\d+(?:\.\d+)?)$/.exec(duration ?? '');
  return parts ? Number(parts[1]) * 3600 + Number(parts[2]) * 60 + Number(parts[3]) : 0;
}

// Seconds per test method, from a trx file; the cases of one method are added together.
function parseTrx(xml) {
  const names = new Map();
  for (const [, open, body] of xml.matchAll(/<UnitTest\b([^>]*)>([\s\S]*?)<\/UnitTest>/g)) {
    const id = attributes(open).id;
    const method = /<TestMethod\b([^>]*?)\/?>/.exec(body);
    if (!id || !method) continue;
    const { className, name } = attributes(method[1]);
    if (className && name) names.set(id, `${className}.${name}`);
  }
  const times = {};
  for (const [, tag] of xml.matchAll(/<UnitTestResult\b([^>]*?)\/?>/g)) {
    const { testId, duration } = attributes(tag);
    const name = names.get(testId);
    if (name) times[name] = (times[name] ?? 0) + durationSeconds(duration);
  }
  return times;
}

function trxFiles(dir) {
  if (!dir || !fs.existsSync(dir)) return [];
  return fs.readdirSync(dir, { withFileTypes: true }).flatMap(item => {
    const full = path.join(dir, item.name);
    if (item.isDirectory()) return trxFiles(full);
    return item.name.endsWith('.trx') ? [full] : [];
  });
}

function readTimings(dir) {
  const times = {};
  for (const file of trxFiles(dir)) {
    for (const [name, seconds] of Object.entries(parseTrx(fs.readFileSync(file, 'utf8')))) {
      times[name] = (times[name] ?? 0) + seconds;
    }
  }
  return times;
}

// A job's estimated time. xUnit runs `cores` classes at once and a class's tests one after another,
// and it may start any class last, so the estimate is (total - longest class) / cores + longest class.
function jobSeconds(job, cores) {
  return (job.total - job.longest) / cores + job.longest;
}

function withUnit(job, unit) {
  const chains = new Map(job.chains);
  const chain = (chains.get(unit.owner) ?? 0) + unit.seconds;
  chains.set(unit.owner, chain);
  return { total: job.total + unit.seconds, longest: Math.max(job.longest, chain), chains, entries: [...job.entries, unit.entry] };
}

// Splits measured tests between `count` jobs: a class longer than the ideal job is split into its
// methods, then each class or method, longest first, goes to the job that would finish soonest with
// it. Returns the named entries of every job but the last, which also runs any test the timings
// don't know yet, and each job's estimated minutes.
function planShards(times, count = shardCount(), cores = Cores) {
  const classes = new Map();
  for (const [name, seconds] of Object.entries(times)) {
    const dot = name.lastIndexOf('.');
    if (dot <= 0 || !FullNamePattern.test(name)) continue;
    const owner = name.slice(0, dot);
    if (!classes.has(owner)) classes.set(owner, []);
    classes.get(owner).push({ name, seconds });
  }
  const all = [...classes.values()].flat();
  if (all.length === 0) return null;
  const total = all.reduce((sum, test) => sum + test.seconds, 0);
  const longest = Math.max(...all.map(test => test.seconds));
  const ideal = Math.max(longest, total / (count * cores));

  const units = [];
  for (const [owner, tests] of classes) {
    const seconds = tests.reduce((sum, test) => sum + test.seconds, 0);
    if (seconds > ideal && tests.length > 1) {
      for (const test of tests) units.push({ owner, entry: { method: test.name }, key: test.name, seconds: test.seconds });
    } else {
      units.push({ owner, entry: { class: owner }, key: owner, seconds });
    }
  }
  units.sort((a, b) => b.seconds - a.seconds || (a.key < b.key ? -1 : a.key > b.key ? 1 : 0));

  const jobs = Array.from({ length: count }, () => ({ total: 0, longest: 0, chains: new Map(), entries: [] }));
  for (const unit of units) {
    let best = 0;
    for (let index = 1; index < count; index++) {
      if (jobSeconds(withUnit(jobs[index], unit), cores) < jobSeconds(withUnit(jobs[best], unit), cores) - 1e-6) best = index;
    }
    jobs[best] = withUnit(jobs[best], unit);
  }
  return {
    named: jobs.slice(0, count - 1).map(job => job.entries),
    minutes: jobs.map(job => jobSeconds(job, cores) / 60),
  };
}

// The filters for every test job, from the timings in `dir`, or the fixed split when there are none.
function planFilters(dir) {
  const times = readTimings(dir);
  const plan = planShards(times);
  const shards = plan ? plan.named : PinnedShards;
  const lines = [`source=${plan ? 'timings' : 'fixed'}`];
  for (let shard = 1; shard <= shardCount(shards); shard++) lines.push(`filter_${shard}=${testFilter(shard, shards)}`);
  const note = plan
    ? `Split ${Object.keys(times).length} measured tests; estimated minutes per job: ${plan.minutes.map(m => m.toFixed(1)).join(', ')}.`
    : 'No timings from main could be read; using the fixed split.';
  return { lines, note };
}

// A job's slowest tests as a Markdown table for the run summary, and a warning for each test
// slower than `limit` seconds.
function slowReport(times, limit = SlowTestSeconds, top = 10) {
  const sorted = Object.entries(times).sort((a, b) => b[1] - a[1]);
  const rows = sorted.slice(0, top).map(([name, seconds]) => `| ${name} | ${Math.round(seconds)} |`);
  const summary = ['### Slowest tests in this job', '', '| Test | Seconds |', '| --- | --- |', ...rows, ''].join('\n');
  const warnings = sorted.filter(([, seconds]) => seconds > limit).map(([name, seconds]) =>
    `::warning title=Slow test::${name} took ${Math.round(seconds)} seconds. Every CI run waits for its slowest test; ` +
    `aim for under a minute (docs/development/build-and-test.md#how-ci-runs).`);
  return { summary, warnings };
}

// The test project, and the client folder it compiles a few files from.
const TestProject = 'tests/ClankerWorld.Simulation.Tests/ClankerWorld.Simulation.Tests.csproj';
const ClientFolder = 'src/ClankerWorld.GodotClient/';

// The repository paths of the files the test project compiles from outside its folder, or null
// when that can't be known (a wildcard include), so every client file counts as compiled.
function compiledFiles(projectText) {
  const files = new Set();
  for (const [, include] of projectText.matchAll(/<Compile\s+Include="([^"]+)"/g)) {
    if (/[*?]/.test(include)) return null;
    files.add(path.posix.normalize(path.posix.join(path.posix.dirname(TestProject), include.replaceAll('\\', '/'))));
  }
  return files;
}

function readCompiledFiles() {
  try {
    return compiledFiles(fs.readFileSync(TestProject, 'utf8'));
  } catch {
    return null;
  }
}

// What a change needs. Main always runs everything, and an empty or unreadable list counts as
// everything. A change made only of documentation skips the code checks and the test suite. A
// change made only of documentation and Godot client files the test project does not compile
// runs the code checks but skips the test suite: those files can't change a test result.
function planScope(files, compiled = readCompiledFiles()) {
  const changed = files.map(file => file.trim()).filter(file => file !== '');
  if (changed.length === 0) return { code: true, tests: true };
  const clientOnly = file => compiled !== null && file.startsWith(ClientFolder) && !compiled.has(file);
  return {
    code: !changed.every(isDocumentation),
    tests: !changed.every(file => isDocumentation(file) || clientOnly(file)),
  };
}

function main(args, input) {
  const [command, value] = args;
  if (command === 'scope') {
    const { code, tests } = planScope(input.split('\n'));
    return `code=${code}\ntests=${tests}`;
  }
  if (command === 'matrix') return `shards=${JSON.stringify(Array.from({ length: shardCount() }, (_, i) => i + 1))}`;
  if (command === 'filter') return testFilter(Number(value));
  throw new Error('Usage: ci-plan.js scope | matrix | plan <timings-dir> | filter <shard> | slow <results-dir>');
}

if (require.main === module) {
  const [command, value] = process.argv.slice(2);
  if (command === 'plan') {
    const { lines, note } = planFilters(value);
    console.error(note);
    console.log(lines.join('\n'));
  } else if (command === 'slow') {
    const { summary, warnings } = slowReport(readTimings(value));
    if (process.env.GITHUB_STEP_SUMMARY) fs.appendFileSync(process.env.GITHUB_STEP_SUMMARY, `${summary}\n`);
    else console.log(summary);
    for (const warning of warnings) console.log(warning);
  } else {
    const input = command === 'scope' ? fs.readFileSync(0, 'utf8') : '';
    console.log(main(process.argv.slice(2), input));
  }
}

module.exports = {
  PinnedShards, Cores, SlowTestSeconds, namePart, clause, shardCount, testFilter, parseTrx, readTimings,
  planShards, planFilters, slowReport, compiledFiles, planScope, main,
};

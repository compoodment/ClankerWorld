const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { test } = require('node:test');
const {
  PinnedShards, namePart, shardCount, testFilter, parseTrx, readTimings, planShards, planFilters, slowReport,
  compareTimings, compiledFiles, planScope, main,
} = require('./ci-plan.js');

// The second shard names all of BigTests, but the first shard's BigTests.LongMethod stays there.
const Shards = [['SlowTests', 'BigTests.LongMethod'], ['OtherTests', 'BigTests']];

// Applies a dotnet test filter the way VSTest does for the forms ci-plan.js writes: terms joined
// by & (all), each a FullyQualifiedName clause (~ contains, = equals, ! negates) or a parenthesised
// | (any) group. `name` is a test's fully qualified name without its case arguments.
function matches(filter, name) {
  const clause = text => {
    const [, negated, operator, part] = text.match(/^FullyQualifiedName(!?)([~=])(.+)$/);
    const hit = operator === '~' ? name.includes(part) : name === part;
    return hit !== (negated === '!');
  };
  const term = text => text.startsWith('(') ? text.slice(1, -1).split('|').some(clause) : clause(text);
  return filter.split('&').every(term);
}

// The shard a test runs in under `named`, the planned entries of every shard but the last.
function shardsRunning(named, name) {
  const hits = [];
  for (let shard = 1; shard <= named.length + 1; shard++) if (matches(testFilter(shard, named), name)) hits.push(shard);
  return hits;
}

function trx(results) {
  const tests = results.map(([id, className, name]) =>
    `<UnitTest name="${className}.${name}" storage="x.dll" id="${id}"><Execution id="e${id}" />` +
    `<TestMethod codeBase="x.dll" adapterTypeName="executor://xunit" className="${className}" name="${name}" /></UnitTest>`);
  const runs = results.flatMap(([id, className, name, ...durations]) => durations.map((duration, i) =>
    `<UnitTestResult executionId="r${id}${i}" testId="${id}" testName="${className}.${name}(case: ${i})" ` +
    `computerName="ci" duration="${duration}" outcome="Passed" />`));
  return `<?xml version="1.0"?><TestRun><Results>${runs.join('')}</Results><TestDefinitions>${tests.join('')}</TestDefinitions></TestRun>`;
}

const Names = [
  'ClankerWorld.Simulation.Tests.SlowTests.First',
  'ClankerWorld.Simulation.Tests.SlowTests.Second',
  'ClankerWorld.Simulation.Tests.BigTests.LongMethod',
  'ClankerWorld.Simulation.Tests.BigTests.LongMethodWithCases',
  'ClankerWorld.Simulation.Tests.BigTests.ShortMethod',
  'ClankerWorld.Simulation.Tests.OtherTests.Anything',
  'ClankerWorld.Simulation.Tests.NotSlowTests.Lookalike',
  'ClankerWorld.Simulation.Tests.SlowTestsHelpers.Lookalike',
  'ClankerWorld.Simulation.Tests.NewTests.AddedLater',
];

test('every test runs in exactly one shard, and unnamed tests run in the last one', () => {
  for (const name of Names) {
    const hits = [];
    for (let shard = 1; shard <= shardCount(Shards); shard++) {
      if (matches(testFilter(shard, Shards), name)) hits.push(shard);
    }
    assert.equal(hits.length, 1, `${name} ran in shards ${hits.join(', ') || 'none'}`);
  }
  assert.ok(matches(testFilter(3, Shards), 'ClankerWorld.Simulation.Tests.NewTests.AddedLater'));
});

test('a class entry matches only that class, and an earlier shard keeps the methods it names', () => {
  const shardOf = name => [1, 2, 3].filter(shard => matches(testFilter(shard, Shards), name));
  assert.deepEqual(Names.filter(name => shardOf(name)[0] === 1), [
    'ClankerWorld.Simulation.Tests.SlowTests.First',
    'ClankerWorld.Simulation.Tests.SlowTests.Second',
    'ClankerWorld.Simulation.Tests.BigTests.LongMethod',
    'ClankerWorld.Simulation.Tests.BigTests.LongMethodWithCases',
  ]);
  assert.deepEqual(Names.filter(name => shardOf(name)[0] === 2), [
    'ClankerWorld.Simulation.Tests.BigTests.ShortMethod',
    'ClankerWorld.Simulation.Tests.OtherTests.Anything',
  ]);
  assert.equal(namePart('SlowTests'), '.SlowTests.');
  assert.equal(namePart('BigTests.LongMethod'), '.BigTests.LongMethod');
});

test('filters use only the forms dotnet test accepts', () => {
  assert.equal(testFilter(1, Shards), '(FullyQualifiedName~.SlowTests.|FullyQualifiedName~.BigTests.LongMethod)');
  assert.equal(testFilter(2, Shards),
    '(FullyQualifiedName~.OtherTests.|FullyQualifiedName~.BigTests.)' +
    '&FullyQualifiedName!~.SlowTests.&FullyQualifiedName!~.BigTests.LongMethod');
  assert.equal(testFilter(3, Shards),
    'FullyQualifiedName!~.SlowTests.&FullyQualifiedName!~.BigTests.LongMethod' +
    '&FullyQualifiedName!~.OtherTests.&FullyQualifiedName!~.BigTests.');
});

test('a shard outside the matrix or a malformed entry is refused', () => {
  assert.throws(() => testFilter(0, Shards), /from 1 to 3/);
  assert.throws(() => testFilter(4, Shards), /from 1 to 3/);
  assert.throws(() => testFilter(Number('x'), Shards), /whole number/);
  assert.throws(() => testFilter(1, [['Bad|Entry']]), /Class or Class.Method/);
  assert.throws(() => testFilter(1, [['Too.Many.Parts']]), /Class or Class.Method/);
});

test('the checked-in shards are well formed, name each entry once and hold at most four classes', () => {
  for (const shard of PinnedShards) {
    assert.ok(new Set(shard.map(entry => entry.split('.')[0])).size <= 4, `too many classes in ${shard.join(', ')}`);
  }
  const entries = PinnedShards.flat();
  assert.equal(new Set(entries).size, entries.length);
  for (let shard = 1; shard <= shardCount(); shard++) assert.ok(testFilter(shard).length > 0);
  const all = Array.from({ length: PinnedShards.length + 1 }, (_, i) => i + 1);
  assert.equal(main(['matrix'], ''), `shards=${JSON.stringify(all)}`);
});

test('a trx file gives seconds per method, adding up the cases of one method', () => {
  const times = parseTrx(trx([
    ['a', 'Ns.FarmTests', 'Grows', '00:00:01.5000000', '00:00:02.5000000'],
    ['b', 'Ns.FarmTests', 'Harvests', '00:02:03.0000000'],
    ['c', 'Ns.Outer+InnerTests', 'Nested&amp;Odd', '01:00:00'],
  ]));
  assert.deepEqual(times, { 'Ns.FarmTests.Grows': 4, 'Ns.FarmTests.Harvests': 123, 'Ns.Outer+InnerTests.Nested&Odd': 3600 });
  assert.deepEqual(parseTrx('<TestRun></TestRun>'), {});
});

test('timings are read from every trx file under a folder', () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'ci-plan-'));
  fs.mkdirSync(path.join(dir, 'test-timings-1'));
  fs.mkdirSync(path.join(dir, 'test-timings-2'));
  fs.writeFileSync(path.join(dir, 'test-timings-1', 'timings.trx'), trx([['a', 'Ns.ATests', 'One', '00:00:05']]));
  fs.writeFileSync(path.join(dir, 'test-timings-2', 'timings.trx'), trx([['b', 'Ns.BTests', 'Two', '00:00:07']]));
  fs.writeFileSync(path.join(dir, 'notes.txt'), 'not a trx file');
  assert.deepEqual(readTimings(dir), { 'Ns.ATests.One': 5, 'Ns.BTests.Two': 7 });
  assert.deepEqual(readTimings(path.join(dir, 'missing')), {});
  assert.deepEqual(readTimings(undefined), {});
});

// A long class, a very slow method in another class and many quick classes.
function measured() {
  const times = {
    'Ns.HugeTests.Slowest': 720, 'Ns.HugeTests.Quick': 5,
    'Ns.FamilyTests.A': 260, 'Ns.FamilyTests.B': 200, 'Ns.FamilyTests.C': 130, 'Ns.FamilyTests.D': 60,
    'Ns.FarmTests.Grow': 200, 'Ns.FarmTests.Store': 140,
  };
  for (let i = 0; i < 60; i++) times[`Ns.Quick${i}Tests.Runs`] = 20 + (i % 7) * 10;
  return times;
}

test('a planned split runs every measured and every new test in exactly one job', () => {
  const plan = planShards(measured());
  assert.equal(plan.named.length, shardCount() - 1);
  const names = [...Object.keys(measured()), 'Ns.HugeTests.AddedToday', 'Ns.FamilyTests.AddedToday', 'Ns.FarmTests.AddedToday',
    'Ns.BrandNewTests.Anything', 'Ns.FarmTestsHelpers.Lookalike'];
  for (const name of names) assert.equal(shardsRunning(plan.named, name).length, 1, `${name} ran in ${shardsRunning(plan.named, name)}`);
  assert.deepEqual(shardsRunning(plan.named, 'Ns.BrandNewTests.Anything'), [shardCount()]);
});

test('a class longer than the ideal job is split by method, and the slowest test gets room to itself', () => {
  const plan = planShards(measured());
  const entries = plan.named.flat();
  assert.ok(entries.some(entry => entry.method === 'Ns.HugeTests.Slowest'));
  assert.ok(!entries.some(entry => entry.class === 'Ns.HugeTests'));
  assert.ok(entries.some(entry => entry.class === 'Ns.Quick0Tests') || shardsRunning(plan.named, 'Ns.Quick0Tests.Runs')[0] === shardCount());
  const shardOfSlowest = shardsRunning(plan.named, 'Ns.HugeTests.Slowest')[0];
  // The 12-minute test sets the slowest job, and no other job is estimated past it.
  assert.ok(Math.max(...plan.minutes) <= 12.5, plan.minutes.join(', '));
  assert.equal(plan.minutes[shardOfSlowest - 1], Math.max(...plan.minutes));
});

test('the split is the same every time, and filters use only the forms dotnet test accepts', () => {
  assert.deepEqual(planShards(measured()), planShards(measured()));
  const plan = planShards({ 'Ns.ATests.One': 400, 'Ns.ATests.Two': 300, 'Ns.BTests.Three': 100 }, 2, 1);
  for (let shard = 1; shard <= 2; shard++) {
    for (const part of testFilter(shard, plan.named).replace(/[()]/g, '').split(/[&|]/)) {
      assert.match(part, /^FullyQualifiedName!?[~=]Ns\.[\w.]+$/);
    }
  }
  assert.equal(planShards({}), null);
  assert.equal(planShards({ 'Bad Name.With Spaces': 5 }), null);
});

test('without timings from main the fixed split is used', () => {
  const fixed = planFilters(path.join(os.tmpdir(), 'no-such-timings-folder'));
  assert.equal(fixed.lines[0], 'source=fixed');
  assert.equal(fixed.lines[1], `filter_1=${testFilter(1)}`);
  assert.equal(fixed.lines.length, shardCount() + 1);
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'ci-plan-'));
  fs.writeFileSync(path.join(dir, 'timings.trx'), trx([['a', 'Ns.ATests', 'One', '00:01:00'], ['b', 'Ns.BTests', 'Two', '00:02:00']]));
  const planned = planFilters(dir);
  assert.equal(planned.lines[0], 'source=timings');
  assert.match(planned.note, /2 measured tests/);
  assert.ok(planned.lines.slice(1).every((line, i) => line.startsWith(`filter_${i + 1}=`) && !line.includes('\n')));
});

test('a job lists its slowest tests and warns only about very slow ones', () => {
  const report = slowReport({ 'Ns.ATests.Slow': 723.4, 'Ns.BTests.Fine': 30, 'Ns.CTests.Quick': 0.5 }, 300, 2);
  assert.match(report.summary, /\| Ns\.ATests\.Slow \| 723 \|/);
  assert.match(report.summary, /\| Ns\.BTests\.Fine \| 30 \|/);
  assert.doesNotMatch(report.summary, /CTests/);
  assert.deepEqual(report.warnings.length, 1);
  assert.match(report.warnings[0], /^::warning title=Slow test::Ns\.ATests\.Slow took 723 seconds\./);
});

test('a test much slower than on main is flagged, but ordinary runner noise is not', () => {
  const main = { 'Ns.ATests.Slower': 30, 'Ns.BTests.Noisy': 100, 'Ns.CTests.Tiny': 1, 'Ns.DTests.Gone': 50 };
  const run = { 'Ns.ATests.Slower': 75, 'Ns.BTests.Noisy': 125, 'Ns.CTests.Tiny': 6, 'Ns.ETests.New': 400 };
  const report = compareTimings(main, run);
  // Only tests both runs have count: 131 s on main, 206 s here.
  assert.match(report.summary, /The 3 tests both runs have took 206 s here and 131 s in main's latest green run \(\+57%\)/);
  assert.match(report.summary, /\| Ns\.ATests\.Slower \| 30 \| 75 \| \+150% \|/);
  assert.doesNotMatch(report.summary, /ETests|DTests/);
  // Twice as slow and 20 seconds more: flagged. 25% slower, or 5 seconds more: not.
  assert.deepEqual(report.slower.map(change => change.name), ['Ns.ATests.Slower']);
  assert.equal(report.warnings.length, 1);
  assert.match(report.warnings[0], /^::warning title=Test got slower::Ns\.ATests\.Slower took 75 seconds, 2\.5 times its 30 seconds/);
});

test('the whole suite growing a lot is flagged even when no single test doubles', () => {
  const main = Object.fromEntries(Array.from({ length: 40 }, (_, i) => [`Ns.T${i}Tests.Runs`, 10]));
  const run = Object.fromEntries(Object.keys(main).map(name => [name, 15]));
  const report = compareTimings(main, run);
  assert.deepEqual(report.slower, []);
  assert.equal(report.warnings.length, 1);
  assert.match(report.warnings[0], /^::warning title=Tests got slower::The tests both runs have took 600 seconds, \+50% on main's/);
  // A small suite growing by the same share adds too few seconds to matter.
  assert.deepEqual(compareTimings({ 'Ns.ATests.One': 10 }, { 'Ns.ATests.One': 15 }).warnings, []);
});

test('without main timings the comparison says so and warns about nothing', () => {
  const report = compareTimings({}, { 'Ns.ATests.One': 10 });
  assert.match(report.summary, /No timings from main's latest green run/);
  assert.deepEqual(report.warnings, []);
});

test('the workflow compares test times after the test jobs, without blocking a merge', () => {
  const workflow = fs.readFileSync(path.join(__dirname, '..', 'workflows', 'ci.yml'), 'utf8');
  const job = workflow.slice(workflow.indexOf('\n  test-times:'), workflow.indexOf('\n  verify:'));
  assert.match(job, /needs: tests/);
  assert.match(job, /needs\.tests\.result == 'success'/);
  assert.match(job, /pattern: test-timings-\*/);
  assert.match(job, /ci-plan\.js compare "\$RUNNER_TEMP\/main" "\$RUNNER_TEMP\/this-run"/);
  const verify = workflow.slice(workflow.indexOf('\n  verify:'), workflow.indexOf('\n  windows-documentation:'));
  assert.doesNotMatch(verify, /test-times/);
});

test('the workflow passes each job its planned filter', () => {
  const workflow = fs.readFileSync(path.join(__dirname, '..', 'workflows', 'ci.yml'), 'utf8');
  for (let shard = 1; shard <= shardCount(); shard++) {
    assert.match(workflow, new RegExp(`filter-${shard}: \\$\\{\\{ steps\\.split\\.outputs\\.filter_${shard} \\}\\}`));
  }
  assert.match(workflow, /needs\.scope\.outputs\[format\('filter-\{0\}', matrix\.shard\)\]/);
});

test('only a change made entirely of documentation skips the code checks and the test suite', () => {
  const none = new Set();
  const plan = files => planScope(files, none);
  assert.deepEqual(plan(['docs/playing.md', 'changes/12-fix.md', 'docs/development/assets/map.png']),
    { code: false, tests: false });
  assert.deepEqual(plan(['README.md', 'src/ClankerWorld.Simulation/World.cs']), { code: true, tests: true });
  assert.deepEqual(plan(['.github/workflows/ci.yml']), { code: true, tests: true });
  assert.deepEqual(plan(['tests/ClankerWorld.Simulation.Tests/Documentation/DocumentationTests.cs']),
    { code: true, tests: true });
  assert.deepEqual(plan([]), { code: true, tests: true });
  assert.deepEqual(plan(['', '  ']), { code: true, tests: true });
});

test('a change to client files the tests do not compile skips only the test suite', () => {
  const compiled = new Set(['src/ClankerWorld.GodotClient/Protocol/WorldObservationProtocol.cs']);
  const plan = files => planScope(files, compiled);
  assert.deepEqual(plan(['src/ClankerWorld.GodotClient/UI/AgentPanel.cs', 'src/ClankerWorld.GodotClient/Main.tscn',
    'playtest/700-agent-panel.md']), { code: true, tests: false });
  // A compiled client file, or anything outside the client folder, needs the test suite.
  assert.deepEqual(plan(['src/ClankerWorld.GodotClient/UI/AgentPanel.cs',
    'src/ClankerWorld.GodotClient/Protocol/WorldObservationProtocol.cs']), { code: true, tests: true });
  assert.deepEqual(plan(['src/ClankerWorld.GodotClient/UI/AgentPanel.cs', 'src/ClankerWorld.Viewer/Program.cs']),
    { code: true, tests: true });
  assert.deepEqual(plan(['src/ClankerWorld.GodotClient/UI/AgentPanel.cs',
    'tests/ClankerWorld.Simulation.Tests/ClankerWorld.Simulation.Tests.csproj']), { code: true, tests: true });
  assert.deepEqual(plan(['src/ClankerWorld.GodotClientExtras/Tool.cs']), { code: true, tests: true });
  // When the compiled files can't be known, every client file counts as compiled.
  assert.deepEqual(planScope(['src/ClankerWorld.GodotClient/UI/AgentPanel.cs'], null), { code: true, tests: true });
});

test('the compiled client files come from the test project', () => {
  const project = `<Project><ItemGroup>
    <Compile Include="..\\..\\src\\ClankerWorld.GodotClient\\UI\\Theme\\GameUiText.cs" Link="GodotClient\\UI\\Theme\\GameUiText.cs" />
  </ItemGroup></Project>`;
  assert.deepEqual([...compiledFiles(project)], ['src/ClankerWorld.GodotClient/UI/Theme/GameUiText.cs']);
  assert.equal(compiledFiles('<Compile Include="..\\..\\src\\ClankerWorld.GodotClient\\**\\*.cs" />'), null);

  // The real project compiles a few client files, and each one exists.
  const root = path.join(__dirname, '..', '..');
  const real = compiledFiles(fs.readFileSync(
    path.join(root, 'tests', 'ClankerWorld.Simulation.Tests', 'ClankerWorld.Simulation.Tests.csproj'), 'utf8'));
  assert.ok(real.size > 0);
  for (const file of real) {
    assert.ok(file.startsWith('src/ClankerWorld.GodotClient/'), file);
    assert.ok(fs.existsSync(path.join(root, file)), file);
  }
});

test('scope prints whether to run the code checks and the test suite', () => {
  assert.equal(main(['scope'], 'docs/playing.md\nCONTRIBUTING.md\n'), 'code=false\ntests=false');
  assert.equal(main(['scope'], 'docs/playing.md\nglobal.json\n'), 'code=true\ntests=true');
  assert.equal(main(['scope'], 'src/ClankerWorld.GodotClient/Main.tscn\n'), 'code=true\ntests=false');
  assert.equal(main(['scope'], 'src/ClankerWorld.GodotClient/UI/Theme/GameUiText.cs\n'), 'code=true\ntests=true');
});

test('the workflow runs the test suite only when scope asks for it', () => {
  const workflow = fs.readFileSync(path.join(__dirname, '..', 'workflows', 'ci.yml'), 'utf8');
  assert.match(workflow, /tests: \$\{\{ steps\.plan\.outputs\.tests \}\}/);
  const job = name => workflow.slice(workflow.indexOf(`\n  ${name}:\n`)).split(/\n  [a-z-]+:\n/)[1];
  // Anything but an explicit false runs the test suite, so a missing decision can't skip it.
  assert.match(job('tests'), /if: needs\.scope\.outputs\.tests != 'false'/);
  assert.match(job('windows-provider-storage'), /if: needs\.scope\.outputs\.tests != 'false'/);
  assert.match(job('verify'), /if \[ "\$TESTS_NEEDED" = false \]; then test "\$TESTS" = skipped; else test "\$TESTS" = success; fi/);
  assert.doesNotMatch(workflow, /outputs\.tests == 'true'/);
});

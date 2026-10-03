const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { test } = require('node:test');
const {
  PinnedShards, namePart, shardCount, testFilter, parseTrx, readTimings, planShards, planFilters, slowReport,
  changesCode, main,
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

test('the workflow passes each job its planned filter', () => {
  const workflow = fs.readFileSync(path.join(__dirname, '..', 'workflows', 'ci.yml'), 'utf8');
  for (let shard = 1; shard <= shardCount(); shard++) {
    assert.match(workflow, new RegExp(`filter-${shard}: \\$\\{\\{ steps\\.split\\.outputs\\.filter_${shard} \\}\\}`));
  }
  assert.match(workflow, /needs\.scope\.outputs\[format\('filter-\{0\}', matrix\.shard\)\]/);
});

test('only a change made entirely of documentation skips the code checks', () => {
  assert.equal(changesCode(['docs/playing.md', 'changes/12-fix.md', 'docs/development/assets/map.png']), false);
  assert.equal(changesCode(['README.md', 'src/ClankerWorld.Simulation/World.cs']), true);
  assert.equal(changesCode(['.github/workflows/ci.yml']), true);
  assert.equal(changesCode(['tests/ClankerWorld.Simulation.Tests/Documentation/DocumentationTests.cs']), true);
  assert.equal(changesCode([]), true);
  assert.equal(changesCode(['', '  ']), true);
  assert.equal(main(['scope'], 'docs/playing.md\nCONTRIBUTING.md\n'), 'code=false');
  assert.equal(main(['scope'], 'docs/playing.md\nglobal.json\n'), 'code=true');
});

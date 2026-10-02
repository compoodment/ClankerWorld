const assert = require('node:assert/strict');
const { test } = require('node:test');
const { PinnedShards, namePart, shardCount, testFilter, changesCode, main } = require('./ci-plan.js');

const Shards = [['SlowTests', 'BigTests.LongMethod'], ['OtherTests']];

// Applies a dotnet test filter the way VSTest does for the forms ci-plan.js writes:
// clauses joined by | (any) or & (all), each FullyQualifiedName~text or !~text.
function matches(filter, name) {
  const clause = text => {
    const [, negated, part] = text.match(/^FullyQualifiedName(!?)~(.+)$/);
    return name.includes(part) !== (negated === '!');
  };
  return filter.includes('&') ? filter.split('&').every(clause) : filter.split('|').some(clause);
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

test('a class entry matches only that class, and a method entry splits a class across shards', () => {
  const first = Names.filter(name => matches(testFilter(1, Shards), name));
  assert.deepEqual(first, [
    'ClankerWorld.Simulation.Tests.SlowTests.First',
    'ClankerWorld.Simulation.Tests.SlowTests.Second',
    'ClankerWorld.Simulation.Tests.BigTests.LongMethod',
    'ClankerWorld.Simulation.Tests.BigTests.LongMethodWithCases',
  ]);
  assert.ok(matches(testFilter(3, Shards), 'ClankerWorld.Simulation.Tests.BigTests.ShortMethod'));
  assert.equal(namePart('SlowTests'), '.SlowTests.');
  assert.equal(namePart('BigTests.LongMethod'), '.BigTests.LongMethod');
});

test('filters use only the forms dotnet test accepts', () => {
  assert.equal(testFilter(1, Shards), 'FullyQualifiedName~.SlowTests.|FullyQualifiedName~.BigTests.LongMethod');
  assert.equal(testFilter(2, Shards), 'FullyQualifiedName~.OtherTests.');
  assert.equal(testFilter(3, Shards),
    'FullyQualifiedName!~.SlowTests.&FullyQualifiedName!~.BigTests.LongMethod&FullyQualifiedName!~.OtherTests.');
});

test('a shard outside the matrix or a malformed entry is refused', () => {
  assert.throws(() => testFilter(0, Shards), /from 1 to 3/);
  assert.throws(() => testFilter(4, Shards), /from 1 to 3/);
  assert.throws(() => testFilter(Number('x'), Shards), /whole number/);
  assert.throws(() => testFilter(1, [['Bad|Entry']]), /Class or Class.Method/);
  assert.throws(() => testFilter(1, [['Too.Many.Parts']]), /Class or Class.Method/);
});

test('the checked-in shards are well formed and name each entry once', () => {
  const entries = PinnedShards.flat();
  assert.equal(new Set(entries).size, entries.length);
  for (let shard = 1; shard <= shardCount(); shard++) assert.ok(testFilter(shard).length > 0);
  const all = Array.from({ length: PinnedShards.length + 1 }, (_, i) => i + 1);
  assert.equal(main(['matrix'], ''), `shards=${JSON.stringify(all)}`);
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

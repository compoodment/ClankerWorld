// Decides what the Verify workflow (.github/workflows/ci.yml) runs:
//   node ci-plan.js scope < changed-files   prints code=true, or code=false for a documentation-only change
//   node ci-plan.js matrix                  prints shards=[1,2,...], one entry per test job
//   node ci-plan.js filter <shard>          prints the dotnet test --filter for that test job
// Every test runs in exactly one job: each pinned shard runs the tests its entries name, and the
// last shard runs everything no entry names, so new or renamed tests always run somewhere.
const { isDocumentation } = require('./pr-labels.js');

// The slowest tests and classes, measured with the trx logger (docs/development/build-and-test.md),
// spread so each job takes about as long as the slowest single test. "Class" names a whole test
// class; "Class.Method" names one test method, or several that start with that text. Tests of one
// class run one after another inside a job, so a class's slowest methods are split across jobs.
// A name that no longer matches only makes the jobs less even: its tests move to the last shard.
const PinnedShards = [
  [
    'SettlementParenthoodTests.ContinuityDeadlineWithoutARequest',
    'FoodRoutingTests',
    'SettlementSurvivalTests',
    'DamagedCheckpointEntryTests',
    'TailorContentTests',
  ],
  [
    'SettlementParenthoodTests.ChildCanGrowIntoAWorkingAdult',
    'BuildingExpansionTests',
    'HouseholdBuildingUseCoverageTests',
    'PotteryContentTests',
    'StoredFuelRoutingTests',
  ],
  [
    'FarmFieldTests.PhysicalCropCycleSurvivesReload',
    'PersonalEquipmentTests',
    'BusinessTradeTests',
    'SettlementParenthoodTests.ContinuityResumesPostponedAcceptance',
  ],
];

const EntryPattern = /^[A-Za-z_]\w*(\.[A-Za-z_]\w*)?$/;

// The text each entry must contain in a test's fully qualified name. The leading dot stops
// "FarmFieldTests" matching "OtherFarmFieldTests"; a class entry also ends in a dot.
function namePart(entry) {
  if (!EntryPattern.test(entry)) throw new Error(`Test shard entry "${entry}" must be Class or Class.Method.`);
  return entry.includes('.') ? `.${entry}` : `.${entry}.`;
}

function shardCount(shards = PinnedShards) {
  return shards.length + 1;
}

function testFilter(shard, shards = PinnedShards) {
  if (!Number.isInteger(shard) || shard < 1 || shard > shardCount(shards)) {
    throw new Error(`Test shard must be a whole number from 1 to ${shardCount(shards)}.`);
  }
  if (shard <= shards.length) {
    return shards[shard - 1].map(entry => `FullyQualifiedName~${namePart(entry)}`).join('|');
  }
  return shards.flat().map(entry => `FullyQualifiedName!~${namePart(entry)}`).join('&');
}

// Main always runs everything; a pull request skips the code checks only when every changed
// file is documentation. An empty or unreadable list counts as code.
function changesCode(files) {
  const changed = files.map(file => file.trim()).filter(file => file !== '');
  return changed.length === 0 || !changed.every(isDocumentation);
}

function main(args, input) {
  const [command, value] = args;
  if (command === 'scope') return `code=${changesCode(input.split('\n'))}`;
  if (command === 'matrix') return `shards=${JSON.stringify(Array.from({ length: shardCount() }, (_, i) => i + 1))}`;
  if (command === 'filter') return testFilter(Number(value));
  throw new Error('Usage: ci-plan.js scope | matrix | filter <shard>');
}

if (require.main === module) {
  const input = process.argv[2] === 'scope' ? require('fs').readFileSync(0, 'utf8') : '';
  console.log(main(process.argv.slice(2), input));
}

module.exports = { PinnedShards, namePart, shardCount, testFilter, changesCode, main };

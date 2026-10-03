using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class SettlementParenthoodTests
{
    [Fact]
    public async Task ChildCanGrowIntoAWorkingAdultThroughSavedSettlementLife()
    {
        var directory = Directory.CreateTempSubdirectory("clankerworld-generation-");
        PrivateWorldRuntime? world = null;
        try
        {
            var observations = new System.Collections.Concurrent.ConcurrentDictionary<string, string>();
            var state = await PreparedState();
            world = PrivateWorldRuntime.Restore(state, _ => new GenerationProvider(observations));
            world.Pause();
            world.SetLifePace(1_460);
            world.Resume();
            var file = new PrivateWorldStateFile(Path.Combine(directory.FullName, "world.json"), _ => new GenerationProvider(observations));
            string? childId = null;
            var careSeen = false;
            var restarts = 0;
            for (var tick = 0; tick < 12_000; tick++)
            {
                var step = await world.AdvanceOneTickAsync();
                careSeen |= step.Events.Any(item => item.Kind == "child_cared_for");
                if (childId is null && world.Society.Births.Count > 0) childId = world.Society.Births[0].ChildId;
                if (childId is not null)
                {
                    var child = world.Society.GetInhabitant(childId);
                    Assert.True(child.Status == SocietyInhabitantStatus.Active,
                        $"Child died at tick {world.WorldTick}, age {world.Society.AgeAt(child, world.WorldTick)}, cause {child.DeathCause}.");
                    var physical = world.Inhabitants.Single(person => person.InhabitantId == childId);
                    if (child.AgeBand == SocietyAgeBand.Adult && physical.Skills?.Count > 0)
                        break;
                }
                if (tick % 256 == 0) file.Save(world);
                if (tick is 2_000 or 5_000)
                {
                    world.Pause();
                    file.Save(world);
                    world.Dispose();
                    world = file.LoadOrCreate(state.WorldSeed);
                    Assert.True(world.Society.IsPaused);
                    world.Resume();
                    restarts++;
                }
            }
            Assert.NotNull(childId);
            var grown = world.Society.GetInhabitant(childId);
            Assert.Equal(SocietyAgeBand.Adult, grown.AgeBand);
            Assert.Equal(SocietyWorkRole.Unassigned, grown.CurrentRole);
            Assert.True(world.Inhabitants.Single(person => person.InhabitantId == childId).Skills?.Count > 0,
                "Teaching state=" + System.Text.Json.JsonSerializer.Serialize(world.Inhabitants.Select(person => new
                {
                    person.InhabitantId,
                    person.Lesson,
                    person.Skills,
                    person.Position,
                    person.HungerBasisPoints,
                    person.Survival,
                    person.Project,
                    Choices = observations.GetValueOrDefault(person.InhabitantId),
                })));
            // The finite tiny map can have no work left when the child grows
            // up. Give this adult a real paid-input workstation task rather
            // than relying on the retired zero-input crop job.
            world.Pause();
            var adultState = world.ExportState();
            // Keep this finite paid-input task independent of unstarted
            // building plans left over from the preceding years of family life.
            adultState = adultState with
            {
                Inhabitants = adultState.Inhabitants.Select(person => person with
                {
                    Project = person.InhabitantId != childId && person.Project?.JobId is null ? null : person.Project,
                    // The scripted work phase replaces the preceding years of
                    // family choices; reconsider even an unchanged idle context.
                    LastDecisionContext = null,
                }).ToArray(),
            };
            var household = grown.HouseholdId!;
            var inventory = InventoryFixture.AddLot(adultState.Society.Society.Inventory,
                "adult-work-wood", "wood", household, 13);
            adultState = FarmFieldTests.WithInventory(adultState, inventory);
            world.Dispose();
            var tools = adultState.WorldContent!.Recipes.Single(recipe => recipe.LocalId == "tools");
            var storeIds = adultState.WorldSimulation!.Buildings.Where(building => building.HouseholdId == household &&
                    adultState.WorldContent.Buildings.Any(definition => definition.CanonicalId == building.DefinitionId &&
                        definition.Tags.Contains("store", StringComparer.Ordinal)))
                .Select(building => building.InstanceId).ToHashSet(StringComparer.Ordinal);
            var workFile = new PrivateWorldStateFile(Path.Combine(directory.FullName, "world.json"),
                _ => new AdultWorkProvider(childId, tools.CanonicalId, observations, storeIds));
            world = PrivateWorldRuntime.Restore(adultState, _ => new AdultWorkProvider(childId, tools.CanonicalId, observations, storeIds));
            var workshop = world.WorldContent.Buildings.Single(building => building.LocalId == "workshop");
            if (!world.WorldSimulation.Buildings.Any(building => building.DefinitionId == workshop.CanonicalId))
            {
                Assert.Contains(adultState.Map.Tiles.Where(tile => adultState.Map.IsBuildable(tile.Position)),
                    tile => world.PlaceBuilding("adult-workshop", workshop.CanonicalId, tile.Position).Applied);
            }
            var savedWhileWorking = false;
            world.Resume();
            for (var tick = 0; tick < 300 && !world.WorldSimulation.ProductionJobs.Any(job => job.WorkerId == childId &&
                     job.RecipeId == tools.CanonicalId && job.StartedTick >= adultState.Society.Society.WorldTick &&
                     job.State == WorldProductionJobState.Completed); tick++)
            {
                Assert.True((await world.AdvanceOneTickAsync()).Advanced);
                if (!savedWhileWorking && world.WorldSimulation.ProductionJobs.Any(job => job.WorkerId == childId &&
                        job.RecipeId == tools.CanonicalId && job.StartedTick >= adultState.Society.Society.WorldTick &&
                        job.State == WorldProductionJobState.Running))
                {
                    world.Pause();
                    workFile.Save(world);
                    world.Dispose();
                    world = workFile.LoadOrCreate(adultState.WorldSeed);
                    world.Resume();
                    savedWhileWorking = true;
                }
            }
            Assert.True(world.Inhabitants.Single(person => person.InhabitantId == childId).Project is { Stage: "completed" },
                $"Role={grown.CurrentRole}; Choices={observations.GetValueOrDefault(childId)}; " +
                "Physical=" + System.Text.Json.JsonSerializer.Serialize(world.Inhabitants.Single(person => person.InhabitantId == childId)) + "; " +
                "Stock=" + string.Join(',', world.Society.Inventory.Lots.GroupBy(lot => lot.ItemKind).Select(group => group.Key + "=" + group.Sum(lot => lot.Quantity))) +
                "; Owners=" + string.Join(',', world.Society.Inventory.Lots.Where(lot => lot.ItemKind == "wood")
                    .Select(lot => lot.OwnerId + "=" + lot.Quantity + "@" + lot.StorageBuildingId)) +
                "; Projects=" + System.Text.Json.JsonSerializer.Serialize(world.Inhabitants.Select(person => new { person.InhabitantId, person.Project })) +
                "; Sources=" + string.Join(',', world.WorldSystems.Ecology.Resources.Select(resource => resource.Kind + "=" + resource.Quantity)) +
                "; Positions=" + string.Join(',', world.Inhabitants.Select(person => person.InhabitantId + "@" + person.Position)) +
                "; Workstations=" + string.Join(',', world.WorldSimulation.Buildings.Where(building =>
                    building.DefinitionId == workshop.CanonicalId).Select(building => building.InstanceId + "@" + building.Position)));
            Assert.True(savedWhileWorking);
            Assert.Contains(world.ExportState().Events, item => item.Kind == "inhabitant_moved" &&
                item.WorldTick >= adultState.Society.Society.WorldTick &&
                !item.Detail.StartsWith(childId + ":", StringComparison.Ordinal) &&
                (item.Detail.EndsWith(":explore", StringComparison.Ordinal) ||
                 item.Detail.EndsWith(":explore_return", StringComparison.Ordinal)));
            var completed = Assert.Single(world.WorldSimulation.ProductionJobs, job => job.WorkerId == childId &&
                job.RecipeId == tools.CanonicalId && job.StartedTick >= adultState.Society.Society.WorldTick &&
                job.State == WorldProductionJobState.Completed);
            var consumed = world.Society.Inventory.Reservations.Where(reservation => completed.InputReservationIds.Contains(reservation.Id)).ToArray();
            Assert.All(consumed, reservation => Assert.Equal(InventoryReservationState.Completed, reservation.State));
            Assert.Equal(3, consumed.Sum(reservation => reservation.Quantity));
            Assert.Contains(world.Society.Inventory.Lots, lot => lot.Id.StartsWith(completed.JobId + ":output:", StringComparison.Ordinal) &&
                lot.ItemKind == "tool" && lot.Quantity == 1);
            Assert.True(careSeen);
            Assert.Equal(2, restarts);
            Assert.NotNull(world.ExportState().HistoryArchiveHead);
        }
        finally
        {
            world?.Dispose();
            directory.Delete(recursive: true);
        }
    }

    private sealed class AdultWorkProvider(string actor, string recipe,
        System.Collections.Concurrent.ConcurrentDictionary<string, string> observations,
        HashSet<string> storeIds) : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var observation = request.Observation;
            observations[observation.InhabitantId] = string.Join(',', observation.Candidates.Select(item => item.Id));
            // Finish stock already promised to a Store during the preceding family life.
            // This policy still excludes starting a new Store collection.
            var routine = observation.Candidates.Where(item => item.Id is "consume_food" or "collect_shared_food" or
                "seek_food" or "harvest_food" or "safe_idle" ||
                item.Id == "haul_household_stock" && item.DestinationId is { } destination && storeIds.Contains(destination) ||
                // A body frozen in the tiny map's passage can strand the adult
                // despite a free workstation. Other residents move normally.
                observation.InhabitantId != actor && item.Id == "explore");
            var candidates = observation.InhabitantId == actor && observation.HungerBasisPoints >= 3_500 &&
                observation.Candidates.FirstOrDefault(item => item.Id == "build:recipe:" + recipe) is { } work
                ? new[] { work } : routine.ToArray();
            return new DeterministicDecisionProvider().DecideAsync(request with
            {
                Observation = observation with { Candidates = candidates },
            }, cancellationToken);
        }
    }

    private sealed class GenerationProvider(System.Collections.Concurrent.ConcurrentDictionary<string, string> observations) : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var candidates = request.Observation.Candidates;
            observations[request.Observation.InhabitantId] = string.Join(',', candidates.Select(item => item.Id));
            CognitionCandidate? chosen = null;
            if (request.Observation.HungerBasisPoints >= 3_500)
            {
                chosen = candidates.FirstOrDefault(item => item.Id.StartsWith("care:", StringComparison.Ordinal));
                if (request.Observation.WorldTick < 1_000)
                    chosen ??= candidates.FirstOrDefault(item => item.Id.StartsWith("parent_accept:", StringComparison.Ordinal) ||
                        item.Id.StartsWith("parent_propose:", StringComparison.Ordinal));
            }
            var allowed = chosen is null ? candidates.Where(item => !item.Id.StartsWith("parent_", StringComparison.Ordinal)).ToArray() : [chosen];
            return new DeterministicDecisionProvider().DecideAsync(request with
            {
                Observation = request.Observation with { Candidates = allowed },
            }, cancellationToken);
        }
    }
}

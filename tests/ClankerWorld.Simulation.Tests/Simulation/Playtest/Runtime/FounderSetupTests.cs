using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class FounderSetupTests
{
    [Fact]
    public void FounderCanMoveBeforeTimeStartsWithoutChangingIdentityOrMembership()
    {
        using var world = new PrivateWorldRuntime("founder-move", startPace: WorldStartPace.FounderSetup);
        var positions = new[] { new GridPoint(0, 0), new GridPoint(1, 2), new GridPoint(2, 2), new GridPoint(3, 2) };
        var ids = positions.Select(_ => "founder:" + Guid.NewGuid().ToString("N")).ToArray();
        for (var index = 0; index < positions.Length; index++)
            world.PlaceFounder(ids[index], positions[index]);
        var map = world.ExportState().Map;
        var destination = map.Tiles.Select(tile => tile.Position).First(point =>
            map.IsBuildable(point) &&
            !map.CampObjects.Any(item => item.Position == point) &&
            !map.Resources.Any(item => item.Position == point) &&
            !positions.Contains(point));

        Assert.Throws<ArgumentException>(() => world.MoveFounder(ids[0], positions[1]));
        Assert.True(world.MoveFounder(ids[0], destination));
        Assert.False(world.MoveFounder(ids[0], destination));
        Assert.Equal("household:camp-alpha", world.Society.GetInhabitant(ids[0]).HouseholdId);
        Assert.Contains(ids[0], world.Towns.Single().ResidentIds);
        Assert.Equal(destination, world.Inhabitants.Single(item => item.InhabitantId == ids[0]).Position);

        using var restored = PrivateWorldRuntime.Restore(
            PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        Assert.Equal(destination, restored.Inhabitants.Single(item => item.InhabitantId == ids[0]).Position);
        Assert.Equal(ids, restored.FounderSetup!.FounderIds);
        restored.StartWorld();
        Assert.Throws<InvalidOperationException>(() => restored.MoveFounder(ids[0], positions[0]));
    }

    [Fact]
    public void LastFounderPlacementCanBeUndoneAndReplacedWithoutDisturbingHouseholds()
    {
        using var world = new PrivateWorldRuntime("founder-undo", startPace: WorldStartPace.FounderSetup);
        var positions = new[] { new GridPoint(0, 0), new GridPoint(1, 2), new GridPoint(2, 2), new GridPoint(3, 2) };
        var ids = positions.Select(_ => "founder:" + Guid.NewGuid().ToString("N")).ToArray();
        for (var index = 0; index < ids.Length; index++) world.PlaceFounder(ids[index], positions[index]);

        Assert.Throws<ArgumentException>(() => world.UndoLastFounder(ids[0]));
        Assert.Equal(4, world.FounderSetup!.FounderIds.Count);
        Assert.Equal(3, world.UndoLastFounder(ids[3]));
        Assert.DoesNotContain(world.Inhabitants, person => person.InhabitantId == ids[3]);
        Assert.DoesNotContain(world.Society.Inhabitants, person => person.Id == ids[3]);
        Assert.DoesNotContain(ids[3], world.Towns.Single().ResidentIds);
        Assert.Single(world.Society.GetHousehold("household:camp-beta").MemberIds);
        Assert.Throws<ArgumentException>(() => world.UndoLastFounder(ids[3]));

        using var restored = PrivateWorldRuntime.Restore(
            PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        Assert.Equal(ids.Take(3), restored.FounderSetup!.FounderIds);
        var replacement = "founder:" + Guid.NewGuid().ToString("N");
        Assert.Equal("household:camp-beta", restored.PlaceFounder(replacement, positions[3]));
        Assert.Equal(2, restored.Society.GetHousehold("household:camp-beta").MemberIds.Count);
        restored.StartWorld();
        Assert.Throws<InvalidOperationException>(() => restored.UndoLastFounder(replacement));

        Assert.Equal(2, world.UndoLastFounder(ids[2]));
        Assert.Equal(1, world.UndoLastFounder(ids[1]));
        Assert.Equal(0, world.UndoLastFounder(ids[0]));
        Assert.Empty(world.Inhabitants);
        Assert.Empty(world.Towns.Single().ResidentIds);
        Assert.Empty(world.Society.GetHousehold("household:camp-alpha").MemberIds);
        Assert.Empty(world.Society.GetHousehold("household:camp-beta").MemberIds);
    }

    [Fact]
    public async Task TownAdultWithoutHouseholdCannotSpendFoundingStockAndOwnsPersonalProduction()
    {
        using var seed = new PrivateWorldRuntime("town-adult-personal-stock", startPace: WorldStartPace.FounderSetup);
        foreach (var position in new[]
                 {
                     new GridPoint(0, 0), new GridPoint(1, 2), new GridPoint(2, 2), new GridPoint(3, 2),
                 })
            seed.PlaceFounder("founder:" + Guid.NewGuid().ToString("N"), position);
        seed.StartWorld();
        Assert.True(seed.StageStarterContent());
        for (var tick = 0; tick < 8; tick++)
            Assert.True((await seed.AdvanceOneTickAsync()).Advanced);

        var state = seed.ExportState();
        var workshop = seed.WorldContent.Buildings.Single(building => building.LocalId == "workshop");
        var tools = seed.WorldContent.Recipes.Single(recipe => recipe.LocalId == "tools");
        var site = state.Map.Tiles.Select(tile => tile.Position).First(point =>
            state.Map.IsBuildable(point) &&
            TownBorderRules.IsWithinOrAdjacent(seed.Towns.Single(), point, workshop.Width, workshop.Height) &&
            !state.Map.CampObjects.Any(item => item.Position == point) &&
            !state.Map.Resources.Any(item => item.Position == point) &&
            !state.Inhabitants.Any(person => person.Position == point));
        var building = seed.PlaceBuilding("town-workshop", workshop.CanonicalId, site);
        Assert.True(building.Applied, building.Failure);
        var agentId = "agent:" + Guid.NewGuid().ToString("N");
        Assert.Null(seed.AddAgent(agentId, site));
        Assert.Contains(seed.Society.Inventory.Lots, lot =>
            lot.OwnerId == "household:camp-alpha" && lot.ItemKind == "wood" && lot.Quantity >= 3);
        var borrowed = seed.StartProduction(tools.CanonicalId, "town-workshop", agentId);
        Assert.False(borrowed.Applied);

        state = seed.ExportState();
        state = state with
        {
            Society = state.Society with
            {
                Society = state.Society.Society with
                {
                    Inventory = InventoryFixture.AddLot(state.Society.Society.Inventory,
                        "wood:personal-builder", "wood", agentId, 3),
                },
            },
        };
        using var stocked = PrivateWorldRuntime.Restore(state);
        var started = stocked.StartProduction(tools.CanonicalId, "town-workshop", agentId);
        Assert.True(started.Applied, started.Failure);
        for (var tick = 0; tick < tools.DurationTicks; tick++)
            Assert.True((await stocked.AdvanceOneTickAsync()).Advanced);
        var output = stocked.Society.Inventory.Lots.Single(lot => lot.Id == started.JobId + ":output:00");
        Assert.Equal(agentId, output.OwnerId);
    }

    [Fact]
    public async Task AddedAdultOnRecordedHouseholdPropertyJoinsThatHouseholdAndEnclosingTown()
    {
        using var world = new PrivateWorldRuntime("new-agent-house-property", startPace: WorldStartPace.FounderSetup);
        foreach (var position in new[]
                 {
                     new GridPoint(0, 0), new GridPoint(1, 2), new GridPoint(2, 2), new GridPoint(3, 2),
                 })
            world.PlaceFounder("founder:" + Guid.NewGuid().ToString("N"), position);
        world.StartWorld();
        Assert.True(world.StageStarterContent());
        for (var tick = 0; tick < 8; tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);

        var state = world.ExportState();
        var house = world.WorldContent.Buildings.Single(building => building.LocalId == "house-1x1");
        var site = state.Map.Tiles.Select(tile => tile.Position).First(point =>
            state.Map.IsBuildable(point) &&
            TownBorderRules.IsWithinOrAdjacent(world.Towns.Single(), point, house.Width, house.Height) &&
            !state.Map.CampObjects.Any(item => item.Position == point) &&
            !state.Map.Resources.Any(item => item.Position == point) &&
            !state.Inhabitants.Any(person => person.Position == point));
        var placed = world.PlaceBuilding("starter-house-alpha", house.CanonicalId, site, "household:camp-alpha");
        Assert.True(placed.Applied, placed.Failure);

        var agentId = "agent:" + Guid.NewGuid().ToString("N");
        var householdCount = world.Society.Households.Count;
        Assert.Equal("household:camp-alpha", world.AddAgent(agentId, site));
        Assert.Equal(householdCount, world.Society.Households.Count);
        Assert.Contains(agentId, world.Society.GetHousehold("household:camp-alpha").MemberIds);
        Assert.Contains(agentId, world.Towns.Single().ResidentIds);
        var secondAgentId = "agent:" + Guid.NewGuid().ToString("N");
        Assert.Equal("household:camp-alpha", world.AddAgent(secondAgentId, site));
        Assert.Equal(householdCount, world.Society.Households.Count);
        Assert.Equal(site, world.Inhabitants.Single(item => item.InhabitantId == secondAgentId).Position);

        using var restored = PrivateWorldRuntime.Restore(
            PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        Assert.Equal("household:camp-alpha", restored.Society.GetInhabitant(agentId).HouseholdId);
        Assert.Equal("household:camp-alpha", restored.Society.GetInhabitant(secondAgentId).HouseholdId);
        Assert.Contains(agentId, restored.Towns.Single().ResidentIds);
        Assert.Contains(secondAgentId, restored.Towns.Single().ResidentIds);

        var capture = new CandidateCaptureProvider();
        var proposed = world.ExportState();
        proposed = proposed with
        {
            Inhabitants = proposed.Inhabitants.Select(person => person.InhabitantId == agentId
                ? person with { Aspiration = "build a home", HungerBasisPoints = 9_000 }
                : person).ToArray(),
        };
        using var observing = PrivateWorldRuntime.Restore(proposed,
            id => id == agentId ? capture : new CandidateCaptureProvider());
        Assert.True((await observing.AdvanceOneTickAsync()).Advanced);
        Assert.Contains(capture.CandidateIds, id => id.StartsWith("build:building:", StringComparison.Ordinal));
        Assert.DoesNotContain(capture.CandidateIds, id => id.Contains(house.CanonicalId, StringComparison.Ordinal));
    }

    [Fact]
    public async Task EmptyBaseCampPersistsFounderProgressAndOnlyStartsOnExplicitCommand()
    {
        var directory = Directory.CreateTempSubdirectory("clankerworld-founders-");
        try
        {
            var file = new PrivateWorldStateFile(Path.Combine(directory.FullName, "world.json"),
                newWorldPace: WorldStartPace.FounderSetup);
            using (var created = file.LoadOrCreate("new-camp"))
            {
                Assert.Empty(created.Inhabitants);
                Assert.Empty(created.Society.Inhabitants);
                Assert.Equal(2, created.Society.Households.Count);
                Assert.False(created.FounderSetup!.Started);
                Assert.True(created.Society.IsPaused);
                Assert.Equal(2, created.ExportState().Map.CampObjects.Count(item => item.Kind == "shelter"));
                Assert.DoesNotContain(created.ExportState().Map.CampObjects, item => item.Kind == "founder");
                Assert.Throws<InvalidOperationException>(created.Resume);
                Assert.Throws<InvalidOperationException>(created.StartWorld);
                Assert.Throws<InvalidOperationException>(() => created.AddAgent(
                    "agent:" + Guid.NewGuid().ToString("N"), new GridPoint(4, 2)));
                created.PlaceFounder("founder:" + Guid.NewGuid().ToString("N"), new GridPoint(0, 0));
                created.PlaceFounder("founder:" + Guid.NewGuid().ToString("N"), new GridPoint(1, 2));
                file.Save(created);
            }

            using var resumedSetup = file.LoadOrCreate("new-camp");
            Assert.Equal(2, resumedSetup.FounderSetup!.FounderIds.Count);
            Assert.True(resumedSetup.Society.IsPaused);
            Assert.Equal(2, resumedSetup.Society.Households.Single(item => item.Id == "household:camp-alpha").MemberIds.Count);
            resumedSetup.PlaceFounder("founder:" + Guid.NewGuid().ToString("N"), new GridPoint(2, 2));
            resumedSetup.PlaceFounder("founder:" + Guid.NewGuid().ToString("N"), new GridPoint(3, 2));
            Assert.Equal(2, resumedSetup.Society.Households.Single(item => item.Id == "household:camp-beta").MemberIds.Count);
            Assert.True(resumedSetup.Society.IsPaused);
            resumedSetup.StartWorld();
            Assert.False(resumedSetup.Society.IsPaused);
            Assert.True(resumedSetup.FounderSetup.Started);
            var tick = await resumedSetup.AdvanceOneTickAsync();
            Assert.True(tick.Advanced);
            var map = resumedSetup.ExportState().Map;
            var position = map.Tiles.Select(tile => tile.Position).First(point =>
                map.IsBuildable(point) &&
                resumedSetup.Towns.Single().BorderTiles.Contains(point) &&
                !map.CampObjects.Any(item => item.Position == point) &&
                !map.Resources.Any(item => item.Position == point) &&
                !resumedSetup.Inhabitants.Any(item => item.Position == point));
            var agentId = "agent:" + Guid.NewGuid().ToString("N");
            var householdCount = resumedSetup.Society.Households.Count;
            var householdId = resumedSetup.AddAgent(agentId, position);
            Assert.Null(householdId);
            Assert.Equal(householdCount, resumedSetup.Society.Households.Count);
            Assert.Null(resumedSetup.Society.GetInhabitant(agentId).HouseholdId);
            Assert.Contains(agentId, resumedSetup.Towns.Single().ResidentIds);
            var outside = map.Tiles.Select(tile => tile.Position).First(point =>
                map.IsBuildable(point) &&
                !resumedSetup.Towns.Single().BorderTiles.Contains(point) &&
                !map.CampObjects.Any(item => item.Position == point) &&
                !map.Resources.Any(item => item.Position == point) &&
                !resumedSetup.Inhabitants.Any(item => item.Position == point));
            var independentId = "agent:" + Guid.NewGuid().ToString("N");
            var independentHouseholdId = resumedSetup.AddAgent(independentId, outside);
            Assert.Equal("household:" + independentId, independentHouseholdId);
            Assert.DoesNotContain(independentId, resumedSetup.Towns.Single().ResidentIds);
            Assert.Equal(4, resumedSetup.Society.Inhabitants.Count(item => item.Id.StartsWith("founder:", StringComparison.Ordinal)));
            Assert.Throws<ArgumentException>(() => resumedSetup.AddAgent(agentId, new GridPoint(5, 2)));
            Assert.True(resumedSetup.RenameAgent(agentId, "Nova"));
            Assert.False(resumedSetup.RenameAgent(agentId, "Nova"));
            Assert.Throws<ArgumentException>(() => resumedSetup.RenameAgent(agentId, "  "));
            Assert.Equal(position, resumedSetup.Inhabitants.Single(item => item.InhabitantId == agentId).Position);
            Assert.True((await resumedSetup.AdvanceOneTickAsync()).Advanced);
            var positionAfterTick = resumedSetup.Inhabitants.Single(item => item.InhabitantId == agentId).Position;
            file.Save(resumedSetup);
            using var reloaded = file.LoadOrCreate("new-camp");
            Assert.Null(reloaded.Society.GetInhabitant(agentId).HouseholdId);
            Assert.Equal(independentHouseholdId, reloaded.Society.GetInhabitant(independentId).HouseholdId);
            Assert.Equal("Nova", reloaded.Society.GetInhabitant(agentId).Name);
            Assert.Equal(positionAfterTick, reloaded.Inhabitants.Single(item => item.InhabitantId == agentId).Position);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private sealed class CandidateCaptureProvider : IDecisionProvider
    {
        public IReadOnlyList<string> CandidateIds { get; private set; } = [];
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;

        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            CandidateIds = request.Observation.Candidates.Select(candidate => candidate.Id).ToArray();
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId,
                request.Observation.InhabitantId, Kind, ProviderEpoch, request.Observation.RunEpoch,
                request.Observation.DecisionGeneration, request.Observation.ObservationDigest,
                "safe_idle", 1, request.Observation.Candidates.ToDictionary(candidate => candidate.Id,
                    candidate => candidate.Id == "safe_idle" ? 1d : 0d)));
        }
    }
}

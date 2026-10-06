using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class TownPublicInteractionTests
{
    [Theory]
    [InlineData("store_town_resources")]
    [InlineData("collect_tool:wooden_hammer")]
    public async Task AStationaryResidentDoesNotBlockActualWarehouseStockTransfers(string action)
    {
        using var generated = NormalPathWorld.CreateGenerated("town-public-stock", _ => new Choice("safe_idle"));
        var state = generated.ExportState();
        var town = state.Towns![0];
        var warehouse = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == "first-town-warehouse");
        var actor = town.ResidentIds[0];
        var blocker = town.ResidentIds[1];
        var start = state.Map.FootNeighbors(warehouse.Position).First(point =>
            state.Map.IsBuildable(point) && state.Inhabitants.All(person => person.Position != point));
        var inventory = state.Society.Society.Inventory;
        if (action == "store_town_resources")
            inventory = InventoryFixture.AddLot(inventory, "public-wood", "wood", actor, 8);
        else
        {
            inventory = inventory with { Lots = inventory.Lots.Where(lot => lot.ItemKind != "wooden_hammer").ToArray() };
            inventory = InventoryFixture.AddLot(inventory, "public-hammer", "wooden_hammer", town.Id, 1,
                storageBuildingId: warehouse.InstanceId);
        }
        state = FarmFieldTests.WithInventory(state, inventory) with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { Position = start, HungerBasisPoints = 9_000, LastDecisionContext = null }
                : person.InhabitantId == blocker
                    ? person with { Position = warehouse.Position, HungerBasisPoints = 9_000, LastDecisionContext = null }
                    : person).ToArray(),
        };
        using var world = PrivateWorldRuntime.Restore(state, id => new Choice(id == actor ? action : "safe_idle"));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(
            PrivateWorldRuntimeCodec.Encode(world.ExportState())), id => new Choice(id == actor ? action : "safe_idle"));
        var eventKind = action == "store_town_resources" ? "town_resources_stored" : "equipment_collected";
        for (var tick = 0; tick < 30 && !world.ExportState().Events.Any(item => item.Kind == eventKind); tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        }
        Assert.Contains(world.ExportState().Events, item => item.Kind == eventKind && item.Detail.StartsWith(actor + ":", StringComparison.Ordinal));
        Assert.Equal(warehouse.Position, world.Inhabitants.Single(person => person.InhabitantId == blocker).Position);
        Assert.Equal(warehouse.Position, world.Inhabitants.Single(person => person.InhabitantId == actor).Position);
        if (action == "store_town_resources")
        {
            Assert.Equal(4, world.Society.Inventory.GetLot("public-wood").Quantity);
            Assert.Equal(4, world.Society.Inventory.Lots.Where(lot => lot.OwnerId == town.Id &&
                lot.ItemKind == "wood" && lot.StorageBuildingId == warehouse.InstanceId).Sum(lot => lot.Quantity));
        }
        else
        {
            var hammer = world.Society.Inventory.GetLot("public-hammer");
            Assert.Equal(actor, hammer.OwnerId);
            Assert.True(PersonalEquipmentRules.IsCarried(hammer, actor));
        }
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        world.Validate();
    }

    [Fact]
    public async Task AStationaryResidentDoesNotHideTheApprovedHallFromOtherWorkers()
    {
        using var scenario = await TownProjectScenario.ApprovedAsync(initialTownStock: true);
        var state = scenario.World.ExportState();
        var blocker = state.Towns![0].ResidentIds[^1];
        var site = scenario.Project.Plan.Site;
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == blocker
                ? person with { Position = site, HungerBasisPoints = 9_000, LastDecisionContext = null }
                : person).ToArray(),
        };
        scenario.Policy.Supply = true;
        using var world = PrivateWorldRuntime.Restore(state, id => id == blocker
            ? new Choice("safe_idle") : scenario.Policy.CreateProvider(id));
        for (var tick = 0; tick < 80 && world.Towns[0].Projects[0].Deliveries.Count == 0; tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.NotEmpty(world.Towns[0].Projects[0].Deliveries);
        Assert.Equal(site, world.Inhabitants.Single(person => person.InhabitantId == blocker).Position);
        Assert.Contains(world.ExportState().Events, item => item.Kind == "town_project_material_picked_up");
        Assert.Equal(44, TownProjectScenario.MaterialQuantity(world.Society.Inventory, state.Towns[0].Id));
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(
            PrivateWorldRuntimeCodec.Encode(world.ExportState())), id => id == blocker
                ? new Choice("safe_idle") : scenario.Policy.CreateProvider(id));
        for (var tick = 0; tick < 160 && world.Towns[0].Projects[0].Stage != "completed"; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
            Assert.Equal(site, world.Inhabitants.Single(person => person.InhabitantId == blocker).Position);
        }
        var completed = world.Towns[0].Projects[0];
        Assert.Equal("completed", completed.Stage);
        Assert.Contains(world.WorldSimulation.Buildings, building => building.InstanceId == completed.CompletedBuildingId);
        Assert.Equal(8, TownProjectScenario.MaterialQuantity(world.Society.Inventory, state.Towns[0].Id));
        Assert.Equal(InventoryReservationState.Reserved, world.Society.Inventory.GetReservation("unrelated-wood").State);
        Assert.Equal(InventoryReservationState.Reserved, world.Society.Inventory.GetReservation("unrelated-stone").State);
        Assert.Contains(world.ExportState().Events, item => item.Kind == "town_project_worked");
        world.Validate();
    }

    private sealed class Choice(string action) : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            var observation = request.Observation;
            var selected = observation.Candidates.FirstOrDefault(candidate => candidate.Id == action)
                ?? observation.Candidates.Single(candidate => candidate.Id == "safe_idle");
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, observation.InhabitantId,
                Kind, ProviderEpoch, observation.RunEpoch, observation.DecisionGeneration, observation.ObservationDigest,
                selected.Id, 1, observation.Candidates.ToDictionary(candidate => candidate.Id,
                    candidate => candidate.Id == selected.Id ? 1d : 0d, StringComparer.Ordinal)));
        }
    }
}

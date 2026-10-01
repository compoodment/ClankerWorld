using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class FarmGrainCapacityTests
{
    [Fact]
    public async Task FullFarmhouseLeavesGrainInItsFieldAndPickupTakesOnlyTheRoomAvailableAcrossReplay()
    {
        using var generated = NormalPathWorld.CreateGenerated("probe-a", _ => new Goal());
        var state = generated.ExportState();
        var farmhouse = generated.WorldSimulation.Buildings.Single(building => building.InstanceId == "first-town-farmhouse");
        var actor = generated.Society.Inhabitants.First(person => person.HouseholdId == farmhouse.HouseholdId).Id;
        var footprints = generated.WorldSimulation.Buildings.SelectMany(building => WorldContentSimulationRules.Footprint(
            generated.WorldContent.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId), building.Position)).ToHashSet();
        var point = generated.Towns.Single().BorderTiles.OrderBy(tile => state.Map.FootDistance(tile, farmhouse.Position))
            .First(tile => LandFertilityRules.IsFarmable(state.Map, tile) && !footprints.Contains(tile) &&
                !generated.RoadTiles.Contains(tile) && !state.Map.Resources.Any(resource => resource.Position == tile) &&
                !state.Inhabitants.Any(person => person.Position == tile));
        var recipe = generated.WorldContent.Recipes.Single(item => item.LocalId == "universal-grain-field");
        state = FarmTestFields.Prepare(state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor ? person with { Position = point } : person).ToArray(),
        }, actor, point, recipe);
        var capacity = BuildingStorageRules.Capacity(generated.WorldContent.Buildings.Single(definition => definition.CanonicalId == farmhouse.DefinitionId), farmhouse)!.Value;
        var inventory = state.Society.Society.Inventory;
        inventory = InventoryFixture.AddLot(inventory, "full-farmhouse-stock", "stone", farmhouse.HouseholdId!,
            checked((int)(capacity - inventory.Lots.Where(lot => lot.StorageBuildingId == farmhouse.InstanceId).Sum(lot => lot.Quantity))), storageBuildingId: farmhouse.InstanceId);
        state = WithInventory(state, inventory);
        using var growing = Load(state);
        var planted = growing.StartProduction(recipe.CanonicalId, WorldBuildSiteRules.FieldSiteId(point), actor);
        Assert.True(planted.Applied, planted.Failure);
        for (var tick = 0; tick < recipe.DurationTicks; tick++) Assert.True((await growing.AdvanceOneTickAsync()).Advanced);
        await FarmTestFields.Harvest(growing, actor, point);
        var grainId = planted.JobId + ":output:00";
        var harvested = growing.Society.Inventory.GetLot(grainId).Quantity;
        Assert.True(harvested > 3);
        using var full = Load(FreshChoice(growing.ExportState(), actor), actor, allowDelivery: false);
        for (var tick = 0; tick < 4; tick++) Assert.True((await full.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(farmhouse.HouseholdId, full.Society.Inventory.GetLot(grainId).OwnerId);
        Assert.Equal(new InventoryGroundPosition(point.X, point.Y), full.Society.Inventory.GetLot(grainId).GroundPosition);
        Assert.Equal(harvested, full.Society.Inventory.GetLot(grainId).Quantity);
        Assert.DoesNotContain(full.Society.Inventory.Lots, lot => lot.OwnerId == actor && lot.DeliveryBuildingId == farmhouse.InstanceId);
        inventory = InventoryFixture.Reserve(full.Society.Inventory, "farmhouse-used-stock", farmhouse.HouseholdId!, "full-farmhouse-stock", 3, "earlier-work", full.WorldTick + 100);
        inventory = InventoryFixture.ConsumeReservation(inventory, "farmhouse-used-stock");
        using var pickup = Load(FreshChoice(WithInventory(full.ExportState(), inventory), actor), actor);
        Assert.Equal(capacity - 3, pickup.Society.Inventory.Lots.Where(lot => lot.StorageBuildingId == farmhouse.InstanceId).Sum(lot => (long)lot.Quantity));
        for (var tick = 0; tick < 8 && !pickup.Society.Inventory.Lots.Any(lot => lot.OwnerId == actor && lot.DeliveryBuildingId == farmhouse.InstanceId); tick++)
            Assert.True((await pickup.AdvanceOneTickAsync()).Advanced);
        Assert.True(pickup.Society.Inventory.Lots.Any(lot => lot.OwnerId == actor && lot.DeliveryBuildingId == farmhouse.InstanceId),
            "Actor=" + System.Text.Json.JsonSerializer.Serialize(pickup.Inhabitants.Single(person => person.InhabitantId == actor)) +
            "; events=" + string.Join(';', pickup.ExportState().Events.TakeLast(10)));
        var carried = Assert.Single(pickup.Society.Inventory.Lots, lot => lot.OwnerId == actor && lot.DeliveryBuildingId == farmhouse.InstanceId);
        Assert.Equal("grain", carried.ItemKind);
        Assert.Equal(3, carried.Quantity);
        Assert.Null(carried.GroundPosition);
        Assert.Equal(harvested - 3, pickup.Society.Inventory.GetLot(grainId).Quantity);
        using var replay = Load(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(pickup.ExportState())), actor);
        for (var tick = 0; tick < 32 && pickup.Society.Inventory.GetLot(carried.Id).OwnerId == actor; tick++)
        {
            Assert.True((await pickup.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
        }
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(pickup.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        Assert.Equal(farmhouse.HouseholdId, pickup.Society.Inventory.GetLot(carried.Id).OwnerId);
        Assert.Equal(farmhouse.InstanceId, pickup.Society.Inventory.GetLot(carried.Id).StorageBuildingId);
        Assert.Equal(capacity, pickup.Society.Inventory.Lots.Where(lot => lot.StorageBuildingId == farmhouse.InstanceId).Sum(lot => (long)lot.Quantity));
        Assert.Equal(new InventoryGroundPosition(point.X, point.Y), pickup.Society.Inventory.GetLot(grainId).GroundPosition);
        pickup.Validate();
    }

    private static PrivateWorldRuntimeState WithInventory(PrivateWorldRuntimeState state, InventoryCheckpoint inventory) =>
        state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } };

    private static PrivateWorldRuntimeState FreshChoice(PrivateWorldRuntimeState state, string actor) => state with
    {
        Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor ? person with { LastDecisionContext = null } : person).ToArray(),
        Society = state.Society with
        {
            Cognition = state.Society.Cognition with
            {
                Runtimes = state.Society.Cognition.Runtimes.Select(runtime => runtime.InhabitantId == actor ? runtime with { CurrentIntention = null } : runtime).ToArray(),
            }
        },
    };

    private static PrivateWorldRuntime Load(PrivateWorldRuntimeState state, string? actor = null, bool allowDelivery = true) =>
        PrivateWorldRuntime.Restore(state, id => new Goal(id == actor, allowDelivery));

    private sealed class Goal(bool hauling = false, bool allowDelivery = true) : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var selected = hauling ? request.Observation.Candidates.Where(candidate => candidate.Id == "haul_farm_grain" ||
                allowDelivery && candidate.Id == "haul_household_stock")
                .OrderBy(candidate => candidate.DeterministicPriority).FirstOrDefault() : null;
            return new DeterministicDecisionProvider().DecideAsync(request with
            {
                Observation = request.Observation with { Candidates = [selected ?? request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle")] },
            }, cancellationToken);
        }
    }
}

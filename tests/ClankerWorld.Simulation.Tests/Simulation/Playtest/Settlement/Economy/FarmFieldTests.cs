using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class FarmFieldTests
{
    [Theory]
    [InlineData("grain", "grain_seed")]
    [InlineData("potatoes", "potatoes")]
    [InlineData("cultivated_greens", "cultivated_green_seed")]
    public async Task PhysicalCropCycleSurvivesReloadAndLeavesOwnedHarvestAndPlantingReserveOnTheTile(string crop, string seedKind)
    {
        var (state, actor, household, point) = PreparedFarmer("field-cycle-" + crop);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "carried-planting", seedKind, actor, 2);
        state = WithInventory(state, inventory);
        using var tilling = Restore(state);
        Assert.True(tilling.StartFieldWork(actor, point, FarmWorkKind.Till).Accepted);
        await Advance(tilling, 3);
        Assert.Equal(FarmFieldStage.Preparing, Assert.Single(tilling.Fields).Stage);
        using var prepared = Reload(tilling);
        await Advance(prepared, 5);
        Assert.Equal(FarmFieldStage.Prepared, Assert.Single(prepared.Fields).Stage);
        Assert.True(prepared.StartFieldWork(actor, point, FarmWorkKind.Plant, crop, "carried-planting").Accepted);
        await Advance(prepared, 2);
        using var planted = Reload(prepared);
        await Advance(planted, 2);
        Assert.Equal(FarmFieldStage.Planted, Assert.Single(planted.Fields).Stage);
        Assert.Equal(1, planted.Society.Inventory.GetLot("carried-planting").Quantity);
        using var growing = Reload(planted);
        await Advance(growing, 1);
        Assert.Equal(FarmFieldStage.Growing, Assert.Single(growing.Fields).Stage);
        Assert.True(growing.StartFieldWork(actor, point, FarmWorkKind.Tend).Accepted);
        await Advance(growing, 4);
        var ticksToReady = Assert.Single(growing.Fields).ReadyTick - growing.WorldTick;
        await Advance(growing, checked((int)Math.Max(1, ticksToReady)));
        Assert.Equal(FarmFieldStage.Ready, Assert.Single(growing.Fields).Stage);
        using var harvesting = Reload(growing);
        Assert.True(harvesting.StartFieldWork(actor, point, FarmWorkKind.Harvest).Accepted);
        await Advance(harvesting, 2);
        using var harvested = Reload(harvesting);
        await Advance(harvested, 2);
        var field = Assert.Single(harvested.Fields);
        Assert.Equal(FarmFieldStage.Harvested, field.Stage);
        Assert.Equal(1, field.Cycle);
        var lots = harvested.Society.Inventory.Lots.Where(lot => lot.Id.StartsWith("field-", StringComparison.Ordinal)).ToArray();
        Assert.All(lots, lot =>
        {
            Assert.Equal(household, lot.OwnerId);
            Assert.Equal(new InventoryGroundPosition(point.X, point.Y), lot.GroundPosition);
            Assert.Null(lot.StorageBuildingId);
            Assert.Null(lot.DeliveryBuildingId);
        });
        Assert.InRange(Assert.Single(lots, lot => lot.ItemKind == crop).Quantity, 4, 9);
        var reserve = Assert.Single(harvested.Society.Inventory.Reservations, item => item.Id == field.ReplantingReservationId);
        Assert.Equal(InventoryReservationState.Reserved, reserve.State);
        Assert.Equal(1, reserve.Quantity);
        Assert.Equal(seedKind, harvested.Society.Inventory.GetLot(reserve.LotId).ItemKind);
        var visible = new OwnerWorldObservationStore(harvested).GetSnapshot();
        Assert.Equal("harvested", Assert.Single(visible.Fields).Stage);
        Assert.All(visible.GroundStocks.Where(stock => stock.Position.X == point.X && stock.Position.Y == point.Y),
            stock => Assert.Equal(household, stock.OwnerId));
        using var final = Reload(harvested);
        await Advance(final, 2);
        Assert.Equal(lots.Select(lot => (lot.Id, lot.ItemKind, lot.OwnerId, lot.Quantity, lot.GroundPosition, lot.StorageBuildingId)),
            final.Society.Inventory.Lots.Where(lot => lot.Id.StartsWith("field-", StringComparison.Ordinal))
                .Select(lot => (lot.Id, lot.ItemKind, lot.OwnerId, lot.Quantity, lot.GroundPosition, lot.StorageBuildingId)));
        Assert.Equal(field, Assert.Single(final.Fields));
    }

    [Fact]
    public async Task RefusedOrInterruptedWorkCannotConsumeRemoteOrForeignSeedsOrDuplicateAField()
    {
        var (state, actor, household, point) = PreparedFarmer("field-refusals");
        var foreign = state.Inhabitants.First(person => person.InhabitantId != actor).InhabitantId;
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "remote-seed", "grain_seed", household, 2);
        inventory = InventoryFixture.AddLot(inventory, "foreign-seed", "grain_seed", foreign, 2);
        inventory = InventoryFixture.AddLot(inventory, "own-seed", "grain_seed", actor, 2);
        using var world = Restore(WithInventory(state, inventory));
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False(world.StartFieldWork(actor, new(point.X + 1, point.Y), FarmWorkKind.Till).Accepted);
        Assert.False(world.StartFieldWork(foreign, point, FarmWorkKind.Till).Accepted);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        Assert.True(world.StartFieldWork(actor, point, FarmWorkKind.Till).Accepted);
        await Advance(world, 8);
        before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False(world.StartFieldWork(actor, point, FarmWorkKind.Till).Accepted);
        Assert.False(world.StartFieldWork(actor, point, FarmWorkKind.Plant, "grain", "remote-seed").Accepted);
        Assert.False(world.StartFieldWork(actor, point, FarmWorkKind.Plant, "grain", "foreign-seed").Accepted);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        Assert.True(world.StartFieldWork(actor, point, FarmWorkKind.Plant, "grain", "own-seed").Accepted);
        await Advance(world, 2);
        state = world.ExportState() with
        {
            Inhabitants = world.ExportState().Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { Position = AwayFromField(state) } : person).ToArray(),
        };
        using var interrupted = Restore(state);
        await Advance(interrupted, 1);
        Assert.Equal(FarmFieldStage.Prepared, Assert.Single(interrupted.Fields).Stage);
        Assert.Null(Assert.Single(interrupted.Fields).Work);
        Assert.Equal(2, interrupted.Society.Inventory.GetLot("own-seed").Quantity);
        Assert.Contains(interrupted.Society.Inventory.Reservations, item =>
            item.Purpose == "field_planting" && item.State == InventoryReservationState.Released);
        using var reloaded = Reload(interrupted);
        await Advance(reloaded, 2);
        Assert.Equal(2, reloaded.Society.Inventory.GetLot("own-seed").Quantity);
        Assert.Equal(FarmFieldStage.Prepared, Assert.Single(reloaded.Fields).Stage);
    }

    private static GridPoint AwayFromField(PrivateWorldRuntimeState state) => state.Map.Tiles
        .First(tile => state.Map.IsBuildable(tile.Position) && !state.Inhabitants.Any(person => person.Position == tile.Position)).Position;

    [Fact]
    public async Task HarvestIsPickedUpWhereItLiesAndStorageCapacityIncludesCarriedDeliveriesAcrossReload()
    {
        var (state, actor, household, point) = PreparedFarmer("field-hauling-capacity");
        const string farmhouse = "first-town-farmhouse";
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "nearly-full-farm", "grain", household, 94,
            storageBuildingId: farmhouse);
        inventory = InventoryFixture.AddLot(inventory, "ground-grain", "grain", household, 9,
            groundPosition: new(point.X, point.Y));
        using var collecting = PrivateWorldRuntime.Restore(WithInventory(state, inventory),
            id => new ChooseProvider(id == actor ? "haul_farm_grain" : "safe_idle"));
        for (var tick = 0; tick < 12 && !collecting.Society.Inventory.Lots.Any(lot => lot.OwnerId == actor && lot.DeliveryBuildingId == farmhouse); tick++)
            Assert.True((await collecting.AdvanceOneTickAsync()).Advanced);
        var carried = Assert.Single(collecting.Society.Inventory.Lots, lot => lot.OwnerId == actor && lot.DeliveryBuildingId == farmhouse);
        Assert.Equal(2, carried.Quantity);
        Assert.Null(carried.GroundPosition);
        Assert.Null(carried.StorageBuildingId);
        Assert.Equal(7, collecting.Society.Inventory.GetLot("ground-grain").Quantity);
        Assert.Equal(new InventoryGroundPosition(point.X, point.Y), collecting.Society.Inventory.GetLot("ground-grain").GroundPosition);
        Assert.Equal(94, new OwnerWorldObservationStore(collecting).GetSnapshot().PlacedBuildings.Single(item => item.InstanceId == farmhouse)
            .StoredItems!.Single(item => item.Kind == "grain").Quantity);
        using var delivering = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(collecting.ExportState())),
            id => new ChooseProvider(id == actor ? "haul_household_stock" : "safe_idle"));
        for (var tick = 0; tick < 40 && delivering.Society.Inventory.GetLot(carried.Id).StorageBuildingId != farmhouse; tick++)
            Assert.True((await delivering.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(farmhouse, delivering.Society.Inventory.GetLot(carried.Id).StorageBuildingId);
        Assert.Equal(96, delivering.Society.Inventory.Lots.Where(lot => lot.StorageBuildingId == farmhouse).Sum(lot => lot.Quantity));
        Assert.Equal(103, delivering.Society.Inventory.Lots.Where(lot => lot.Id is "nearly-full-farm" or "ground-grain" || lot.ProvenanceLotId == "ground-grain")
            .Sum(lot => lot.Quantity));
        delivering.Validate();
    }

    internal static (PrivateWorldRuntimeState State, string Actor, string Household, GridPoint Point) PreparedFarmer(string seed)
    {
        var state = GeographyGeneratorTests.StartedGeneratedWorld(new(seed, WorldSizePreset.Small));
        var farmhouse = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == "first-town-farmhouse");
        var household = farmhouse.HouseholdId!;
        var actor = state.Society.Society.Inhabitants.First(person => person.HouseholdId == household).Id;
        using var world = Restore(state);
        var occupied = world.WorldSimulation.Buildings.SelectMany(building => WorldContentSimulationRules.Footprint(
            world.WorldContent.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId), building.Position))
            .Concat(state.Map.Resources.Select(resource => resource.Position)).Concat(world.RoadTiles)
            .Concat(state.Map.CampObjects.Select(item => item.Position)).ToHashSet();
        var fertility = new LandFertility(state.Map, seed);
        var point = state.Map.Tiles.Select(tile => tile.Position).Where(point => fertility.CanFarm(point) &&
            !occupied.Contains(point) && !state.Inhabitants.Any(person => person.Position == point))
            .OrderBy(point => state.Map.FootDistance(farmhouse.Position, point)).First();
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "carried-hoe", "wooden_hoe", actor, 1);
        state = WithInventory(state, inventory) with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { Position = point, HungerBasisPoints = 10_000 } : person).ToArray(),
        };
        return (state, actor, household, point);
    }

    internal static PrivateWorldRuntimeState WithInventory(PrivateWorldRuntimeState state, InventoryCheckpoint inventory) => state with
    {
        Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
    };
    internal static PrivateWorldRuntime Restore(PrivateWorldRuntimeState state) => PrivateWorldRuntime.Restore(state, _ => new IdleProvider());
    private static PrivateWorldRuntime Reload(PrivateWorldRuntime world) => Restore(PrivateWorldRuntimeCodec.Decode(
        PrivateWorldRuntimeCodec.Encode(world.ExportState())));
    private static async Task Advance(PrivateWorldRuntime world, int count)
    {
        for (var tick = 0; tick < count; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        world.Validate();
    }
    private sealed class IdleProvider : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default) =>
            new DeterministicDecisionProvider().DecideAsync(request with
            {
                Observation = request.Observation with { Candidates = [request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle")] },
            }, cancellationToken);
    }
    private sealed class ChooseProvider(string id) : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default) =>
            new DeterministicDecisionProvider().DecideAsync(request with
            {
                Observation = request.Observation with { Candidates = [request.Observation.Candidates.FirstOrDefault(candidate => candidate.Id == id)
                    ?? request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle")] },
            }, cancellationToken);
    }
}

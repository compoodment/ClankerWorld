using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class PotteryWaterTests
{
    private static readonly string[] Heirs = ["alice", "bob", "carol"];
    [Fact]
    public void InheritanceKeepsAFilledVesselWholeWhenThereAreSeveralHeirs()
    {
        var config = new SocietyConfig(TicksPerWorldDay: 1, DaysPerWorldYear: 100, EstateEscrowDays: 2,
            BaseNaturalMortalityBasisPoints: 0);
        var society = SocietyFixture.CreateGenesis("vessel-estate",
            Heirs.Select(id => SocietyFixture.CreateFounder(id, id, config: config)),
            [Lot("estate-jug", "water_jug", "alice", 1, capacity: 8),
                Lot("estate-water", "water", "alice", 8, container: "estate-jug")], config);
        society = SocietyFixture.CreateHousehold(society, "home", "Home", Heirs).Checkpoint;
        society = SocietyFixture.Kill(society, "alice", SocietyDeathCause.Accident).Checkpoint;
        society = SocietyFixture.AdvanceTo(society, 2).Checkpoint;
        Assert.Equal("bob", society.Inventory.GetLot("estate-jug").OwnerId);
        Assert.Equal("bob", society.Inventory.GetLot("estate-water").OwnerId);
        Assert.Equal("estate-jug", society.Inventory.GetLot("estate-water").ContainerLotId);
        Assert.Equal(8, society.Inventory.GetLot("estate-water").Quantity);
        InventoryFixture.ValidatePortableContainers(society.Inventory.Lots);
    }

    [Fact]
    public void FilledVesselsTransferAndBarterWithTheirRealContentsAndKeepTheirIdentity()
    {
        var inventory = InventoryFixture.CreateGenesis([
            Lot("jug", "water_jug", "alpha", 1, capacity: 8),
            Lot("water", "water", "alpha", 8, container: "jug"),
            Lot("wood", "wood", "beta", 2),
        ]);
        Assert.Equal(9, InventoryFixture.TransferLoadQuantity(inventory, "jug", 1));
        var stored = InventoryFixture.Transfer(inventory, "store", "alpha", "household", "jug", 1,
            "store", "house");
        Assert.All(stored.Lots.Where(lot => lot.Id is "jug" or "water"), lot =>
        {
            Assert.Equal("household", lot.OwnerId);
            Assert.Equal("house", lot.StorageBuildingId);
        });
        var collected = InventoryFixture.Transfer(stored, "collect", "household", "alpha", "jug", 1, "collect");
        var offer = InventoryFixture.CreateDirectBarterOffer(collected,
            new("jug-sale", 1, "alpha", "beta", "jug", 1, "wood", 2, 20));
        Assert.Throws<InvalidOperationException>(() => InventoryFixture.Reserve(offer, "steal-water", "alpha",
            "water", 1, "cook", 20));
        offer = InventoryFixture.AcceptDirectBarterOffer(offer, "jug-sale", 1, "alpha");
        var sold = InventoryFixture.AcceptDirectBarterOffer(offer, "jug-sale", 1, "beta");
        Assert.Equal("beta", sold.GetLot("jug").OwnerId);
        Assert.Equal("beta", sold.GetLot("water").OwnerId);
        Assert.Equal(8, sold.GetLot("water").Quantity);
        Assert.Equal("jug", sold.GetLot("water").ContainerLotId);
        Assert.Null(sold.GetLot("water").StorageBuildingId);
        Assert.Equal("alpha", sold.GetLot("wood").OwnerId);
        Assert.Equal(InventoryDigest.State(sold), InventoryDigest.State(
            InventoryCheckpointCodec.Decode(InventoryCheckpointCodec.Encode(sold))));
    }

    [Fact]
    public void PotCapacityAndReservationsPreserveStockAndOnlyContainedFoodKeepsLonger()
    {
        var inventory = InventoryFixture.CreateGenesis([
            Lot("pot", "storage_pot", "alpha", 1, capacity: 8),
            Lot("berries", "berries", "alpha", 10),
            Lot("outside", "berries", "alpha", 2),
        ]);
        var potted = InventoryFixture.StoreInContainer(inventory, "fill", "alpha", "berries", 8, "pot");
        var contents = potted.Lots.Single(lot => lot.ContainerLotId == "pot");
        Assert.Equal("berries", contents.ProvenanceLotId);
        Assert.Equal(2, potted.GetLot("berries").Quantity);
        Assert.Equal(0, InventoryFixture.ContainerRoom(potted, "pot"));
        Assert.Throws<InvalidOperationException>(() => InventoryFixture.StoreInContainer(potted, "overfill",
            "alpha", "outside", 1, "pot"));
        var reserved = InventoryFixture.Reserve(potted, "meal", "alpha", contents.Id, 2, "cook", 20);
        Assert.Throws<InvalidOperationException>(() => InventoryFixture.Transfer(reserved, "move", "alpha",
            "beta", "pot", 1, "gift"));
        var consumed = InventoryFixture.ConsumeReservation(reserved, "meal");
        Assert.Equal(6, consumed.GetLot(contents.Id).Quantity);
        Assert.Equal(2, InventoryFixture.ContainerRoom(consumed, "pot"));
        Assert.Equal("alpha", consumed.GetLot("pot").OwnerId);
        var decayed = InventoryFixture.ProcessSpoilage(consumed, 10, 100,
            new HashSet<string> { "berries" }, protectedContainerIds: new HashSet<string> { "pot" });
        Assert.Equal(9_500, decayed.GetLot(contents.Id).FreshnessBasisPoints);
        Assert.Equal(9_000, decayed.GetLot("outside").FreshnessBasisPoints);
        Assert.Equal(9_000, decayed.GetLot("berries").FreshnessBasisPoints);
        var restored = InventoryCheckpointCodec.Decode(InventoryCheckpointCodec.Encode(decayed));
        Assert.Equal(InventoryDigest.State(restored), InventoryDigest.State(InventoryFixture.ProcessSpoilage(restored,
            10, 100, new HashSet<string> { "berries" }, protectedContainerIds: new HashSet<string> { "pot" })));
    }

    [Fact]
    public async Task CollectionRequiresFreshWaterPresenceWorkAndKeepsTheJugAfterWaterIsConsumedAcrossReload()
    {
        using var initial = NormalPathWorld.CreateGenerated("water-work", _ => new ActionCoverageRecorder(true));
        var state = initial.ExportState();
        var actor = state.Inhabitants[0].InhabitantId;
        var shore = FindShore(state.Map, ocean: false);
        var ocean = FindShore(state.Map, ocean: true);
        var jug = Lot("collection-jug", "water_jug", actor, 1, capacity: 8);
        state = WithInventory(state, InventoryFixture.AddLot(state.Society.Society.Inventory, jug.Id, jug.ItemKind,
            actor, 1, containerCapacity: 8));
        state = MoveActor(state, actor, shore);
        var world = PrivateWorldRuntime.Restore(state, _ => new ActionCoverageRecorder(true));
        try
        {
            Assert.False(world.FillWaterJug(actor, jug.Id, ocean).Applied);
            Assert.False(world.FillWaterJug(state.Inhabitants[1].InhabitantId, jug.Id, shore).Applied);
            Assert.True(world.FillWaterJug(actor, jug.Id, shore).Applied);
            Assert.False(world.FillWaterJug(actor, jug.Id, shore).Applied);
            Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.ItemKind == "water");
            for (var work = 1; work < 3; work++)
            {
                await world.AdvanceOneTickAsync();
                Assert.True(world.FillWaterJug(actor, jug.Id, shore).Applied);
            }
            var saved = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState()));
            world.Dispose();
            world = PrivateWorldRuntime.Restore(saved, _ => new ActionCoverageRecorder(true));
            Assert.Equal(3, world.Inhabitants.Single(person => person.InhabitantId == actor).WaterWork!.WorkDone);
            for (var work = 3; work < VesselRules.FillingWorkTicks; work++)
            {
                await world.AdvanceOneTickAsync();
                var result = world.FillWaterJug(actor, jug.Id, shore);
                Assert.True(result.Applied, result.Failure);
                Assert.Equal(work == VesselRules.FillingWorkTicks - 1, result.Completed);
            }
            var water = world.Society.Inventory.Lots.Single(lot => lot.ContainerLotId == jug.Id);
            Assert.Equal(8, water.Quantity);
            Assert.False(world.FillWaterJug(actor, jug.Id, shore).Applied);
            Assert.Throws<InvalidOperationException>(() => InventoryFixture.Transfer(world.Society.Inventory,
                "bare-water", actor, "other", water.Id, 1, "transfer"));
            var used = InventoryFixture.Reserve(world.Society.Inventory, "cook-water", actor, water.Id, 8, "cook", 100);
            used = InventoryFixture.ConsumeReservation(used, "cook-water");
            Assert.Equal(1, used.GetLot(jug.Id).Quantity);
            Assert.Equal(8, InventoryFixture.ContainerRoom(used, jug.Id));
            saved = WithInventory(world.ExportState(), used);
            world.Dispose();
            world = PrivateWorldRuntime.Restore(saved, _ => new ActionCoverageRecorder(true));
            await world.AdvanceOneTickAsync();
            Assert.True(world.FillWaterJug(actor, jug.Id, shore).Applied);
            Assert.Equal(1, world.Inhabitants.Single(person => person.InhabitantId == actor).WaterWork!.WorkDone);
        }
        finally { world.Dispose(); }
    }

    [Fact]
    public async Task OrdinaryHouseholdChoicesGatherClayMakeAJugAndDeliverFreshWater()
    {
        var provider = new PotteryChoices();
        using var world = NormalPathWorld.CreateGenerated("pottery-flow", _ => provider);
        for (var tick = 0; tick < 1_200 && !world.ExportState().Events.Any(item => item.Kind == "water_delivered"); tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var state = world.ExportState();
        Assert.Contains(state.Events, item => item.Kind == "material_gathered" && item.Detail.Contains(":clay:", StringComparison.Ordinal));
        Assert.Contains(state.Events, item => item.Kind == "water_collected");
        Assert.Contains(state.Events, item => item.Kind == "water_delivered");
        var water = world.Society.Inventory.Lots.First(lot => lot.ItemKind == "water" && lot.StorageBuildingId is not null);
        var vessel = world.Society.Inventory.GetLot(water.ContainerLotId!);
        Assert.Equal(water.OwnerId, vessel.OwnerId);
        Assert.Equal(water.StorageBuildingId, vessel.StorageBuildingId);
        Assert.Equal("water_jug", vessel.ItemKind);
        Assert.Equal(8, vessel.ContainerCapacity);
        var projected = new OwnerWorldObservationStore(world).GetSnapshot().PlacedBuildings!
            .Single(building => building.InstanceId == water.StorageBuildingId).StoredItems!
            .Single(item => item.Kind == "water_jug");
        Assert.True(projected.ContainerCapacity >= 8);
        Assert.Equal(8, projected.Contents!.Single(item => item.Kind == "water").Quantity);
        var restored = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state));
        using var loaded = PrivateWorldRuntime.Restore(restored, _ => provider);
        loaded.Validate();
    }

    [Fact]
    public void ForgedOrOrphanedContentsAreRefusedByCurrentSaveValidation()
    {
        using var world = NormalPathWorld.CreateGenerated("pottery-invalid", _ => new ActionCoverageRecorder(true));
        var state = world.ExportState();
        var actor = state.Inhabitants[0].InhabitantId;
        var inventory = state.Society.Society.Inventory;
        var forged = inventory with { Lots = inventory.Lots.Append(Lot("orphan", "water", actor, 1, container: "missing")).ToArray() };
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(WithInventory(state, forged)));
        forged = inventory with { Lots = inventory.Lots.Append(Lot("bare-water", "water", actor, 1)).ToArray() };
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(WithInventory(state, forged)));
    }

    private static InventoryLot Lot(string id, string kind, string owner, int quantity,
        int capacity = 0, string? container = null) => new(id, kind, owner, quantity, 10_000, 10_000, 0,
            ContainerLotId: container, ContainerCapacity: capacity);

    private static PrivateWorldRuntimeState WithInventory(PrivateWorldRuntimeState state, InventoryCheckpoint inventory) =>
        state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } };

    private static PrivateWorldRuntimeState MoveActor(PrivateWorldRuntimeState state, string actor, GridPoint position) =>
        state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor ? person with { Position = position }
            : person.Position == position ? person with { Position = state.Inhabitants.Single(item => item.InhabitantId == actor).Position } : person).ToArray()
        };

    private static GridPoint FindShore(SeededMap map, bool ocean) => map.Tiles.First(tile =>
        map.IsPassable(tile.Position) && map.HydrologyAt(tile.Position) == ClankerWorld.Simulation.World.WaterKind.Land &&
        new[] { new GridPoint(tile.Position.X - 1, tile.Position.Y), new GridPoint(tile.Position.X + 1, tile.Position.Y),
            new GridPoint(tile.Position.X, tile.Position.Y - 1), new GridPoint(tile.Position.X, tile.Position.Y + 1) }
            .Any(point => point.X >= 0 && point.X < map.Width && point.Y >= 0 && point.Y < map.Height &&
                (ocean ? map.HydrologyAt(point) == ClankerWorld.Simulation.World.WaterKind.Ocean :
                    map.HydrologyAt(point) is ClankerWorld.Simulation.World.WaterKind.River or ClankerWorld.Simulation.World.WaterKind.Lake))).Position;

    private sealed class PotteryChoices : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            var choice = request.Observation.Candidates.Where(candidate => candidate.Id is "consume_food" or
                    "seek_food" or "harvest_food" or "collect_shared_food" or "haul_household_stock" or
                    "pottery_supply" or "water_collect_jug" or "water_fill" or "water_deliver" or "pot_store_food" ||
                    candidate.Id.StartsWith("build:recipe:", StringComparison.Ordinal) && candidate.Description.Contains("pot", StringComparison.OrdinalIgnoreCase) ||
                    candidate.Description.Contains("water jug", StringComparison.OrdinalIgnoreCase))
                .OrderBy(candidate => candidate.DeterministicPriority).ThenBy(candidate => candidate.Id, StringComparer.Ordinal)
                .FirstOrDefault() ?? request.Observation.Candidates.Single(item => item.Id == "safe_idle");
            return new DeterministicDecisionProvider().DecideAsync(request with
            { Observation = request.Observation with { Candidates = [choice] } }, cancellationToken);
        }
    }
}

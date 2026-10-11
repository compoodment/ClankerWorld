using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class AnimalPipelineTests
{
    [Theory]
    [InlineData("ground", false, false)]
    [InlineData("personal", false, false)]
    [InlineData("borrowed", false, false)]
    [InlineData("personal", true, false)]
    [InlineData("personal", false, true)]
    public async Task YardSupplyPreservesTheChosenLoadAndLeftoverProperty(string sourceMode, bool jug, bool distant)
    {
        var (state, actor, home, yard) = CreateYard("animal-yard-quantity-audit");
        var day = state.WorldSystems!.Config.TicksPerDay;
        var hen = new AnimalState("quantity-hen", "Moss", "chicken", "female",
            -(long)AnimalRules.Definition("chicken").AdultDays * day, yard.Position,
            "household:" + home, home, yard.InstanceId, CareUntilTick: state.Society.Society.WorldTick + day);
        var inventory = state.Society.Society.Inventory with
        {
            Lots = state.Society.Society.Inventory.Lots.Where(lot => lot.OwnerId != actor &&
                (lot.OwnerId != home || !AnimalRules.IsFeed(lot.ItemKind))).ToArray(),
        };
        inventory = InventoryFixture.AddLot(inventory, "quantity-yard-feed", "grain", home, 2, storageBuildingId: yard.InstanceId);
        var sourceOwner = sourceMode == "personal" ? actor : home;
        inventory = InventoryFixture.AddLot(inventory, "quantity-source-feed", jug ? "water_jug" : "grain", sourceOwner, jug ? 1 : 4,
            groundPosition: sourceMode == "ground" ? new InventoryGroundPosition(yard.Position.X, yard.Position.Y) : null);
        if (jug)
            inventory = InventoryFixture.AddLot(inventory, "quantity-source-water", "fresh_water", sourceOwner, 4, containerLotId: "quantity-source-feed");
        if (sourceMode == "borrowed")
            inventory = InventoryFixture.Relocate(inventory, "controlled-borrowed-feed", "quantity-source-feed", sourceOwner, 4, carrierId: actor);
        var start = distant ? state.Map.Tiles.Where(tile => state.Map.FootDistance(tile.Position, yard.Position) == 3 &&
            state.Map.IsReachableOnFoot(tile.Position, yard.Position) &&
            !state.Inhabitants.Any(person => person.InhabitantId != actor && person.Position == tile.Position)).First().Position : yard.Position;
        state = At(state, actor, start, inventory, [hen]) with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { Position = start, Equipment = null, LastDecisionContext = null, HungerBasisPoints = 9_500 }
                : person).ToArray(),
        };
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)), id =>
            id == actor ? new AnimalChooser("animal:supply", DecisionProviderKind.LargeLanguageModel) : new AnimalChooser());
        await Until(world, () => world.ExportState().AnimalWorld.SupplyTrips.Count == 1);
        var tripState = world.ExportState();
        var trip = Assert.Single(tripState.AnimalWorld.SupplyTrips);
        Assert.Equal(1, world.Society.Inventory.GetLot(trip.LotId).Quantity);
        Assert.Equal(jug ? 9 : 13, world.DestinationRoom(yard.InstanceId));
        Assert.Equal(14, world.DestinationRoom(yard.InstanceId, world.Society.Inventory.GetLot(trip.LotId)));
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(tripState), PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        if (!jug)
        {
            var remainder = world.Society.Inventory.GetLot("quantity-source-feed");
            Assert.Equal((sourceOwner, 3), (remainder.OwnerId, remainder.Quantity));
        }
        var tripBytes = PrivateWorldRuntimeCodec.Encode(tripState);
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(tripBytes), id =>
            id == actor ? new AnimalChooser("animal:supply", DecisionProviderKind.LargeLanguageModel) : new AnimalChooser());
        Assert.Equal(tripBytes, PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        Assert.False((await world.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(tripBytes, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        for (var tick = 0; tick < 20 && !world.ExportState().Events.Any(item => item.Kind == "animal_yard_supplied"); tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        }
        Assert.Single(world.ExportState().Events, item => item.Kind == "animal_yard_supplied");
        world.Validate();
        var final = world.ExportState();
        var bytes = PrivateWorldRuntimeCodec.Encode(final);
        using var loaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(loaded.ExportState()));
        var stored = final.Society.Society.Inventory.Lots.Where(lot => lot.StorageBuildingId == yard.InstanceId && lot.ItemKind == "grain").Sum(lot => lot.Quantity);
        Assert.Equal(jug ? 2 : 3, stored);
        var source = Assert.Single(final.Society.Society.Inventory.Lots, lot => lot.Id == "quantity-source-feed");
        if (jug)
        {
            Assert.Equal((home, 1, yard.InstanceId), (source.OwnerId, source.Quantity, source.StorageBuildingId));
            var water = final.Society.Society.Inventory.GetLot("quantity-source-water");
            Assert.Equal((home, 4, source.Id, yard.InstanceId), (water.OwnerId, water.Quantity, water.ContainerLotId, water.StorageBuildingId));
            Assert.Equal(10_000, water.FreshnessBasisPoints);
        }
        else
        {
            Assert.Equal((sourceOwner, 3), (source.OwnerId, source.Quantity));
            Assert.Null(source.StorageBuildingId);
            if (sourceMode == "ground") Assert.Equal(new InventoryGroundPosition(yard.Position.X, yard.Position.Y), source.GroundPosition);
            else Assert.True(PersonalEquipmentRules.IsCarried(source, actor));
            Assert.Equal(6, final.Society.Society.Inventory.Lots.Where(lot => lot.ItemKind == "grain" &&
                (lot.Id == "quantity-yard-feed" || lot.Id.StartsWith("quantity-source-feed", StringComparison.Ordinal))).Sum(lot => lot.Quantity));
        }
        Assert.Empty(final.AnimalWorld.SupplyTrips);
    }
}

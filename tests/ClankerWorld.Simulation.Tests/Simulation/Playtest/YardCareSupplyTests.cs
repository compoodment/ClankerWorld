using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class AnimalPipelineTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task YardCareFetchesStockBeforeApproachingAnotherTile(bool carryInputs, bool careAtStock)
    {
        var (state, actor, home, yard) = CreateYard("animal-horse-cargo");
        var tiles = WorldContentSimulationRules.Footprint(state.WorldContent!.Buildings.Single(definition => definition.CanonicalId == yard.DefinitionId), yard)
            .Where(point => !state.Inhabitants.Any(person => person.InhabitantId != actor && person.Position == point)).ToArray();
        Assert.Contains(yard.Position, tiles);
        var horsePosition = tiles.First(point => point != yard.Position);
        var cowPosition = tiles.First(point => point != yard.Position && point != horsePosition);
        var sheepPosition = careAtStock ? cowPosition : yard.Position;
        if (careAtStock) cowPosition = yard.Position;
        var day = state.WorldSystems!.Config.TicksPerDay;
        var horse = new AnimalState("supply-ride-horse", "Moss", "horse", "female", -(long)AnimalRules.Definition("horse").AdultDays * day,
            horsePosition, "household:" + home, home, yard.InstanceId, CareUntilTick: state.Society.Society.WorldTick + day);
        var sheep = new AnimalState("supply-stock-sheep", "Juniper", "sheep", "female", -(long)AnimalRules.Definition("sheep").AdultDays * day,
            sheepPosition, "household:" + home, home, yard.InstanceId, CareUntilTick: state.Society.Society.WorldTick + day,
            ProductProgressTicks: AnimalRules.Definition("sheep").ProductDays * day - 1);
        var cow = new AnimalState("supply-care-cow", "Fern", "cow", "female", -(long)AnimalRules.Definition("cow").AdultDays * day,
            cowPosition, "household:" + home, home, yard.InstanceId, CareUntilTick: state.Society.Society.WorldTick + 3,
            ProductProgressTicks: AnimalRules.Definition("cow").ProductDays * day - 1);
        var inventory = state.Society.Society.Inventory;
        inventory = inventory with
        {
            Lots = inventory.Lots.Where(lot => lot.OwnerId != actor &&
            (lot.OwnerId != home || !AnimalRules.IsFeed(lot.ItemKind) && lot.ItemKind != "fresh_water")).ToArray()
        };
        inventory = InventoryFixture.AddLot(inventory, "supply-ride-saddle", "saddle", home, 1, groundPosition: new(horsePosition.X, horsePosition.Y));
        inventory = InventoryFixture.AddLot(inventory, "supply-yard-feed", "grain", home, 4, storageBuildingId: yard.InstanceId);
        inventory = InventoryFixture.AddLot(inventory, "supply-yard-jug", "water_jug", home, 1, storageBuildingId: yard.InstanceId);
        inventory = InventoryFixture.AddLot(inventory, "supply-yard-water", "fresh_water", home, 4,
            storageBuildingId: yard.InstanceId, containerLotId: "supply-yard-jug");
        if (carryInputs)
        {
            inventory = InventoryFixture.AddLot(inventory, "supply-personal-feed", "grain", actor, 2);
            inventory = InventoryFixture.AddLot(inventory, "supply-personal-jug", "water_jug", actor, 1);
            inventory = InventoryFixture.AddLot(inventory, "supply-personal-water", "fresh_water", actor, 2,
                containerLotId: "supply-personal-jug");
        }
        state = At(state, actor, horsePosition, inventory, [horse, sheep, cow]);
        state = state with { Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor ? person with { Equipment = null } : person).ToArray() };
        using var preparing = RestoreYardCareSupply(state, actor);
        preparing.SubmitInstruction(new("supply-saddle", "owner", actor, OwnerInstructionKind.MustDo, "saddle Moss"));
        await Until(preparing, () => preparing.Animals.Single(animal => animal.Id == horse.Id).SaddleLotId is not null);
        preparing.SubmitInstruction(new("supply-mount", "owner", actor, OwnerInstructionKind.MustDo, "mount Moss"));
        await Until(preparing, () => preparing.Animals.Single(animal => animal.Id == horse.Id).RiderId == actor);
        preparing.SubmitInstruction(new("supply-dismount", "owner", actor, OwnerInstructionKind.MustDo, "dismount Moss"));
        await Until(preparing, () => preparing.Animals.Single(animal => animal.Id == horse.Id).RiderId is null);
        var instruction = preparing.SubmitInstruction(new("supply-care", "owner", actor, OwnerInstructionKind.MustDo, "care for Fern"));
        var bytes = PrivateWorldRuntimeCodec.Encode(preparing.ExportState());
        using var world = RestoreYardCareSupply(PrivateWorldRuntimeCodec.Decode(bytes), actor);
        using var replay = RestoreYardCareSupply(PrivateWorldRuntimeCodec.Decode(bytes), actor);
        Assert.False((await world.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        var sawSupplyTrip = false;
        var sawCareApproach = false;
        byte[]? reloadedNext = null;
        for (var tick = 0; tick < 12 && world.ExportState().Instructions!.Single(item => item.InstructionId == instruction.InstructionId).Order!.Status != "finished"; tick++)
        {
            var previousEvent = world.ExportState().Events[^1].EventId;
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
            var current = world.ExportState();
            var currentBytes = PrivateWorldRuntimeCodec.Encode(current);
            Assert.Equal(currentBytes, PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
            if (reloadedNext is not null)
            {
                Assert.Equal(reloadedNext, currentBytes);
                reloadedNext = null;
            }
            if (!carryInputs && !careAtStock && current.Events.Any(item => item.EventId > previousEvent &&
                item.Kind == "inhabitant_moved" && item.Detail.StartsWith(actor + ":", StringComparison.Ordinal) &&
                item.Detail.EndsWith(":animal_care", StringComparison.Ordinal)))
            {
                sawCareApproach = true;
                Assert.Equal(2, current.Society.Society.Inventory.Lots.Where(lot =>
                    lot.Id.StartsWith("supply-yard-feed", StringComparison.Ordinal) &&
                    PersonalEquipmentRules.IsPhysicallyCarried(current.Society.Society.Inventory, lot, actor)).Sum(lot => lot.Quantity));
                Assert.Equal(actor, current.Society.Society.Inventory.GetLot("supply-yard-jug").CarrierId);
                Assert.Equal(4, current.Society.Society.Inventory.GetLot("supply-yard-water").Quantity);
            }
            if (!sawSupplyTrip && current.AnimalWorld.SupplyTrips.Any(trip => trip.ActorId == actor))
            {
                sawSupplyTrip = true;
                using var savedTrip = RestoreYardCareSupply(PrivateWorldRuntimeCodec.Decode(currentBytes), actor);
                Assert.True((await savedTrip.AdvanceOneTickAsync()).Advanced);
                reloadedNext = PrivateWorldRuntimeCodec.Encode(savedTrip.ExportState());
            }
        }
        var order = world.ExportState().Instructions!.Single(item => item.InstructionId == instruction.InstructionId).Order!;
        Assert.Equal(("finished", 1), (order.Status, order.CompletedUnits));
        Assert.True(world.Animals.Single(animal => animal.Id == cow.Id).CareUntilTick > world.WorldTick);
        Assert.Null(world.Animals.Single(animal => animal.Id == horse.Id).RiderId);
        Assert.Equal(horsePosition, world.Animals.Single(animal => animal.Id == horse.Id).Position);
        Assert.Equal(sheepPosition, world.Animals.Single(animal => animal.Id == sheep.Id).Position);
        Assert.Equal(carryInputs ? 4 : 2, world.Society.Inventory.Lots.Where(lot => lot.Id.StartsWith("supply-yard-feed", StringComparison.Ordinal)).Sum(lot => lot.Quantity));
        Assert.Equal((home, carryInputs ? 4 : 2), (world.Society.Inventory.GetLot("supply-yard-water").OwnerId, world.Society.Inventory.GetLot("supply-yard-water").Quantity));
        Assert.Equal((home, carryInputs || careAtStock ? null : actor), (world.Society.Inventory.GetLot("supply-yard-jug").OwnerId, world.Society.Inventory.GetLot("supply-yard-jug").CarrierId));
        Assert.NotNull(world.Animals.Single(animal => animal.Id == sheep.Id).ReadyProductLotId);
        Assert.NotNull(world.Animals.Single(animal => animal.Id == cow.Id).ReadyProductLotId);
        Assert.Empty(world.ExportState().AnimalWorld.SupplyTrips);
        if (!carryInputs && !careAtStock)
        {
            Assert.True(sawSupplyTrip);
            Assert.True(sawCareApproach);
            Assert.Null(reloadedNext);
        }
        using var loaded = RestoreYardCareSupply(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())), actor);
        loaded.Validate();
        world.Validate();
    }

    private static PrivateWorldRuntime RestoreYardCareSupply(PrivateWorldRuntimeState state, string actor) =>
        PrivateWorldRuntime.Restore(state, id => id == actor ? new AnimalChooser("animal_order") : new AnimalChooser());
}

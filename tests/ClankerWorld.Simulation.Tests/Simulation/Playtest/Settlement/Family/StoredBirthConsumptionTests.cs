using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class SettlementParenthoodTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BirthConsumesReservedFamilyFoodInPlaceDespiteFullCargoAndKeepsBrokenVesselControl(bool broken)
    {
        var state = PrivateWorldRuntimeCodec.Decode(await FoodReadinessCheckpoint.Value);
        var parent = state.Inhabitants.Single(person => person.Parenthood is { Stage: "preparing" });
        var caregiver = parent.Parenthood!.PrimaryCaregiverId!;
        state = FoodReadinessStock(state, caregiver, enoughFood: true);
        var household = state.Society.Society.GetInhabitant(caregiver).HouseholdId!;
        var house = state.WorldSimulation!.Buildings.Single(building => building.HouseholdId == household &&
            building.InstanceId.StartsWith("first-town-house", StringComparison.Ordinal));
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "readiness-stored-pot", "storage_pot", household, 1,
            storageBuildingId: house.InstanceId);
        // Explicit starting location: the existing two-portion reservation stays
        // on its food, and prevents moving the family while allowing in-place use.
        inventory = inventory with
        {
            Lots = inventory.Lots.Select(lot => lot.Id == "readiness-food" ? lot with
            { StorageBuildingId = house.InstanceId, ContainerLotId = "readiness-stored-pot" } : lot).ToArray(),
        };
        if (broken) inventory = inventory with
        {
            Lots = inventory.Lots.Select(lot => lot.Id == "readiness-stored-pot" ? lot with { ConditionBasisPoints = 0 } : lot).ToArray(),
        };
        var away = state.Map.Tiles.First(tile => state.Map.IsPassable(tile.Position) &&
            state.Map.FootDistance(tile.Position, house.Position) > 3 &&
            state.Inhabitants.All(person => person.Position != tile.Position)).Position;
        state = state with
        {
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == caregiver
                ? person with { Position = away } : person).ToArray(),
        };
        using var world = PrivateWorldRuntime.Restore(state, _ => new ParentProvider("safe_idle"));
        var person = state.Inhabitants.Single(person => person.InhabitantId == caregiver);
        Assert.Equal(0, PersonalEquipmentRules.FreeCapacity(inventory, caregiver, person.Equipment));
        Assert.NotEqual(house.Position, person.Position);
        var readiness = PrivateWorldRuntime.ParenthoodFoodReadiness(world.Society, caregiver, state.Towns!);
        Assert.Equal(broken ? 0 : 12, readiness.AvailablePortions);
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(before), _ => new ParentProvider("safe_idle"));
        Assert.False((await world.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        world.Resume();
        replay.Resume();
        for (var tick = 0; tick < 3; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        }
        Assert.Equal(broken ? 0 : 1, world.Society.Births.Count);
        var retained = world.Society.Inventory.GetLot("readiness-food");
        Assert.Equal((household, house.InstanceId, "readiness-stored-pot", broken ? 14 : 10),
            (retained.OwnerId, retained.StorageBuildingId, retained.ContainerLotId, retained.Quantity));
        Assert.Equal(2, world.Society.Inventory.GetReservation("readiness-reservation").Quantity);
        world.Validate();
        var final = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var loaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(final));
        Assert.Equal(final, PrivateWorldRuntimeCodec.Encode(loaded.ExportState()));
    }
}

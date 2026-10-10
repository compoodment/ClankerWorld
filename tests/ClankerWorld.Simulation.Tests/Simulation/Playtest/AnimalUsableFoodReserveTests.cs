using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class AnimalPipelineTests
{
    [Theory]
    [InlineData(0, 4, false, false)]
    [InlineData(10_000, 4, false, true)]
    [InlineData(0, 0, false, false)]
    [InlineData(0, 4, true, true)]
    public async Task AnimalCareProtectsUsableMealsInsteadOfBrokenPotContents(int potCondition, int reserve, bool grain, bool canCare)
    {
        var (prepared, actor, household, yard) = CreateYard("animal-broken-family-food-reserve");
        var members = prepared.Society.Society.GetHousehold(household).MemberIds;
        Assert.Equal(2, members.Count);
        var house = prepared.WorldSimulation!.Buildings.First(building => building.HouseholdId == household &&
            prepared.WorldContent!.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId).Tags.Contains("house"));
        var inventory = prepared.Society.Society.Inventory with
        {
            Lots = prepared.Society.Society.Inventory.Lots.Where(lot =>
                !InventoryContainerRules.IsFood(lot.ItemKind) || lot.OwnerId != household && !members.Contains(lot.OwnerId)).ToArray(),
        };
        inventory = InventoryFixture.AddLot(inventory, "family-pot", "storage_pot", household, 1,
            storageBuildingId: house.InstanceId);
        if (reserve > 0)
            inventory = InventoryFixture.AddLot(inventory, "pot-berries", "berries", household, reserve,
                storageBuildingId: house.InstanceId, containerLotId: "family-pot");
        inventory = InventoryFixture.AddLot(inventory, "care-greens", "cultivated_greens", actor, 2);
        if (grain) inventory = InventoryFixture.AddLot(inventory, "care-grain", "grain", actor, 2);
        inventory = InventoryFixture.AddLot(inventory, "care-jug", "water_jug", actor, 1);
        inventory = InventoryFixture.AddLot(inventory, "care-water", "fresh_water", actor, 2, containerLotId: "care-jug");
        inventory = inventory with
        { Lots = inventory.Lots.Select(lot => lot.Id == "family-pot" ? lot with { ConditionBasisPoints = potCondition } : lot).ToArray() };
        var cow = new AnimalState("reserve-cow", "Moss", "cow", "female",
            -(long)AnimalRules.Definition("cow").AdultDays * prepared.WorldSystems!.Config.TicksPerDay,
            yard.Position, "household:" + household, household, yard.InstanceId);
        var state = At(prepared, actor, yard.Position, inventory, [cow]) with
        { JevEnabled = false, RoutineHelper = RoutineHelperSettings.Off };
        var initial = PrivateWorldRuntimeCodec.Encode(state);
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(initial),
            id => id == actor ? new AnimalChooser("animal:care:") : new AnimalChooser());
        Assert.Equal(initial, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        for (var tick = 0; tick < 4; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var saved = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved),
            id => id == actor ? new AnimalChooser("animal:care:") : new AnimalChooser());
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        for (var tick = 4; tick < 8; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
        }
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        Assert.Equal(canCare, world.Animals.Single().CareUntilTick > world.WorldTick);
        Assert.Equal(canCare ? 1 : 0, world.ExportState().Events.Count(item => item.Kind == "animal_cared"));
        Assert.Equal(canCare && !grain ? 0 : 2, world.Society.Inventory.Lots.Where(lot => lot.Id == "care-greens").Sum(lot => lot.Quantity));
        Assert.Equal(canCare ? 0 : 2, world.Society.Inventory.Lots.Where(lot => lot.Id == "care-water").Sum(lot => lot.Quantity));
        Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.Id == "care-grain");
        Assert.Equal(reserve, world.Society.Inventory.Lots.Where(lot => lot.Id == "pot-berries").Sum(lot => lot.Quantity));
        Assert.Equal((household, house.InstanceId, potCondition), (world.Society.Inventory.GetLot("family-pot").OwnerId,
            world.Society.Inventory.GetLot("family-pot").StorageBuildingId, world.Society.Inventory.GetLot("family-pot").ConditionBasisPoints));
        Assert.Equal((actor, 1), (world.Society.Inventory.GetLot("care-jug").OwnerId, world.Society.Inventory.GetLot("care-jug").Quantity));
        world.Validate();
    }
}

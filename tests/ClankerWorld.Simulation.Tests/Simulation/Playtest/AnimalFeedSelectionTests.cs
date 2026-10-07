using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class AnimalPipelineTests
{
    [Theory]
    [InlineData(false, 2, true)]
    [InlineData(true, 2, true)]
    [InlineData(false, 1, true)]
    [InlineData(true, 1, true)]
    [InlineData(false, 0, false)]
    [InlineData(true, 0, false)]
    public async Task CareSelectsReserveSafeFeedAcrossSplitLotsAndReload(bool splitGreens, int grain, bool canCare)
    {
        var (prepared, actor, household, yard) = CreateYard("animal-feed-selection");
        var members = prepared.Society.Society.GetHousehold(household).MemberIds;
        Assert.Equal(2, members.Count);
        var house = prepared.WorldSimulation!.Buildings.First(building => building.HouseholdId == household &&
            prepared.WorldContent!.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId).Tags.Contains("house"));
        var inventory = prepared.Society.Society.Inventory with
        {
            Lots = prepared.Society.Society.Inventory.Lots.Where(lot =>
                !InventoryContainerRules.IsFood(lot.ItemKind) || lot.OwnerId != household && !members.Contains(lot.OwnerId)).ToArray(),
        };
        inventory = InventoryFixture.AddLot(inventory, "house-reserve-berries", "berries", household, 3,
            storageBuildingId: house.InstanceId);
        inventory = InventoryFixture.AddLot(inventory, "a-care-greens", "cultivated_greens", actor, splitGreens ? 1 : 2);
        if (splitGreens)
            inventory = InventoryFixture.AddLot(inventory, "b-care-greens", "cultivated_greens", actor, 1);
        if (grain > 0)
            inventory = InventoryFixture.AddLot(inventory, "z-care-grain", "grain", actor, grain);
        inventory = InventoryFixture.AddLot(inventory, "care-jug", "water_jug", actor, 1);
        inventory = InventoryFixture.AddLot(inventory, "care-water", "fresh_water", actor, 2, containerLotId: "care-jug");
        var cow = new AnimalState("reserve-cow", "Moss", "cow", "female",
            -(long)AnimalRules.Definition("cow").AdultDays * prepared.WorldSystems!.Config.TicksPerDay,
            yard.Position, "household:" + household, household, yard.InstanceId);
        var state = At(prepared, actor, yard.Position, inventory, [cow]);
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)),
            id => id == actor ? new AnimalChooser("animal:care:") : new AnimalChooser());
        for (var tick = 0; tick < 6; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes),
            id => id == actor ? new AnimalChooser("animal:care:") : new AnimalChooser());
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        for (var tick = 6; tick < 12; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
        }
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        Assert.Equal(canCare, world.Animals.Single().CareUntilTick > world.WorldTick);
        Assert.Equal(canCare ? 1 : 0, world.ExportState().Events.Count(item => item.Kind == "animal_cared"));
        var final = world.Society.Inventory;
        Assert.Equal(canCare ? 0 : grain, final.Lots.Where(lot => lot.Id == "z-care-grain").Sum(lot => lot.Quantity));
        Assert.Equal(canCare ? grain : 2, final.Lots.Where(lot => lot.OwnerId == actor &&
            lot.ItemKind == "cultivated_greens").Sum(lot => lot.Quantity));
        Assert.Equal(canCare ? 0 : 2, final.Lots.Where(lot => lot.Id == "care-water").Sum(lot => lot.Quantity));
        Assert.Equal((actor, "water_jug", 1), (final.GetLot("care-jug").OwnerId,
            final.GetLot("care-jug").ItemKind, final.GetLot("care-jug").Quantity));
        Assert.Equal((household, house.InstanceId, 3), (final.GetLot("house-reserve-berries").OwnerId,
            final.GetLot("house-reserve-berries").StorageBuildingId, final.GetLot("house-reserve-berries").Quantity));
        world.Validate();
    }

    [Fact]
    public async Task PermittedOutsiderCombinesSpareGreensWithoutSpendingEitherHouseholdsReserveAcrossReload()
    {
        var (prepared, _, animalHousehold, yard) = CreateYard("animal-two-family-feed");
        var actor = prepared.Society.Society.Inhabitants.First(person => person.Status == SocietyInhabitantStatus.Active &&
            person.AgeBand == SocietyAgeBand.Adult && person.HouseholdId != animalHousehold).Id;
        var actorHousehold = prepared.Society.Society.GetInhabitant(actor).HouseholdId!;
        var households = new[] { animalHousehold, actorHousehold };
        var members = households.SelectMany(id => prepared.Society.Society.GetHousehold(id).MemberIds).ToArray();
        Assert.All(households, id => Assert.Equal(2, prepared.Society.Society.GetHousehold(id).MemberIds.Count));
        var inventory = prepared.Society.Society.Inventory with
        {
            Lots = prepared.Society.Society.Inventory.Lots.Where(lot => !InventoryContainerRules.IsFood(lot.ItemKind) ||
                !households.Contains(lot.OwnerId) && !members.Contains(lot.OwnerId)).ToArray(),
        };
        foreach (var household in households)
        {
            var house = prepared.WorldSimulation!.Buildings.First(building => building.HouseholdId == household &&
                prepared.WorldContent!.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId).Tags.Contains("house"));
            inventory = InventoryFixture.AddLot(inventory, household + "-reserve", "berries", household, 3,
                storageBuildingId: house.InstanceId);
        }
        inventory = InventoryFixture.AddLot(inventory, "a-yard-greens", "wild_greens", animalHousehold, 2,
            storageBuildingId: yard.InstanceId);
        inventory = InventoryFixture.AddLot(inventory, "b-carried-greens", "wild_greens", actor, 2);
        inventory = InventoryFixture.AddLot(inventory, "care-jug", "water_jug", actor, 1);
        inventory = InventoryFixture.AddLot(inventory, "care-water", "fresh_water", actor, 2, containerLotId: "care-jug");
        var cow = new AnimalState("shared-care-cow", "Moss", "cow", "female",
            -(long)AnimalRules.Definition("cow").AdultDays * prepared.WorldSystems!.Config.TicksPerDay,
            yard.Position, "household:" + animalHousehold, animalHousehold, yard.InstanceId)
        { CarePermissions = [actor] };
        using var world = PrivateWorldRuntime.Restore(At(prepared, actor, yard.Position, inventory, [cow]),
            id => id == actor ? new AnimalChooser("animal:care:") : new AnimalChooser());
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes),
            id => id == actor ? new AnimalChooser("animal:care:") : new AnimalChooser());
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        for (var tick = 0; tick < 12; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
        }
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        Assert.True(world.Animals.Single().CareUntilTick > world.WorldTick);
        Assert.Equal((animalHousehold, yard.InstanceId, 1), (world.Society.Inventory.GetLot("a-yard-greens").OwnerId,
            world.Society.Inventory.GetLot("a-yard-greens").StorageBuildingId, world.Society.Inventory.GetLot("a-yard-greens").Quantity));
        Assert.Equal((actor, 1), (world.Society.Inventory.GetLot("b-carried-greens").OwnerId,
            world.Society.Inventory.GetLot("b-carried-greens").Quantity));
        Assert.All(households, id => Assert.Equal(3, world.Society.Inventory.GetLot(id + "-reserve").Quantity));
        Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.Id == "care-water");
        Assert.Equal(1, world.Society.Inventory.GetLot("care-jug").Quantity);
        world.Validate();
    }
}

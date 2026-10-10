using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed class SwimmingCartRepairReturnTests
{
    private static readonly Lazy<Task<byte[]>> Fixture = new(async () =>
    {
        using var generated = NormalPathWorld.CreateGenerated("cart-cargo-orders", _ => new ActionCoverageRecorder(chooseIdle: true));
        Assert.True((await generated.AdvanceOneTickAsync()).Advanced);
        return PrivateWorldRuntimeCodec.Encode(SettlementWeatherTestFixture.WithWeather(generated.ExportState(), WeatherKind.Clear));
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RepairSupplyPickupRequiresALoadedReturnToItsSelectedCartButKeepsFootCollection(bool footReturn)
    {
        var state = PrivateWorldRuntimeCodec.Decode(await Fixture.Value);
        var house = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == FoodCapacityTestFixture.House);
        var actor = state.Society.Society.Inhabitants.First(person => person.HouseholdId == house.HouseholdId).Id;
        Assert.True(state.Map.IsPassable(house.Position));
        Assert.DoesNotContain(state.Inhabitants, person => person.InhabitantId != actor && person.Position == house.Position);
        var remote = state.Map.Resources.OrderBy(resource => state.Map.FootDistance(resource.Position, house.Position))
            .First(resource => state.Map.IsPassable(resource.Position) &&
                !state.Map.IsReachableOnFoot(resource.Position, house.Position) &&
                SwimmingRules.IsReachable(state.Map, resource.Position, house.Position) &&
                state.Inhabitants.All(person => person.Position != resource.Position)).Position;
        var cartPosition = footReturn ? house.Position : remote;
        Assert.Equal(footReturn, state.Map.IsReachableOnFoot(house.Position, cartPosition));
        Assert.True(SwimmingRules.IsReachable(state.Map, house.Position, cartPosition));
        var inventory = state.Society.Society.Inventory with
        {
            Lots = state.Society.Society.Inventory.Lots.Where(lot => lot.OwnerId != actor && lot.ItemKind != "rope").ToArray(),
        };
        inventory = InventoryFixture.AddLot(inventory, "swim-repair-wood", "wood", actor, 1);
        inventory = InventoryFixture.AddLot(inventory, "swim-repair-fitting", "iron_fittings", actor, 1);
        inventory = InventoryFixture.AddLot(inventory, "swim-repair-extra", "stone", actor, 2);
        inventory = InventoryFixture.AddLot(inventory, "swim-repair-rope", "rope", house.HouseholdId!, 1,
            groundPosition: new(house.Position.X, house.Position.Y));
        inventory = InventoryFixture.AddLot(inventory, "swim-repair-cart", "handcart", actor, 1,
            groundPosition: new(cartPosition.X, cartPosition.Y));
        inventory = InventoryFixture.AddLot(inventory, "swim-repair-cargo", "stone", actor, 4,
            containerLotId: "swim-repair-cart", conditionBasisPoints: 2_500);
        inventory = InventoryFixture.WearSingleUnit(inventory, "swim-repair-cart", 10_000);
        Assert.Equal(SwimmingRules.MaximumCarriedUnits, PersonalEquipmentRules.CarriedQuantity(inventory, actor, null));
        Assert.True(PersonalEquipmentRules.Capacity(inventory, actor, null) > SwimmingRules.MaximumCarriedUnits);
        state = FarmFieldTests.WithInventory(state, inventory) with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor ? person with
            {
                Position = house.Position,
                HungerBasisPoints = 10_000,
                Survival = new(10_000),
                Equipment = null,
                Project = null,
                Exploration = null,
                LastDecisionContext = null,
                TravelCooldownTicks = 0,
            } : person).ToArray(),
        };
        using var world = PrivateWorldRuntime.Restore(state, _ => new ActionCoverageRecorder(chooseIdle: true));
        world.SubmitInstruction(new("swim-repair-order", "owner:test", actor, OwnerInstructionKind.MustDo,
            "Repair cart swim-repair-cart"));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(footReturn ? actor : house.HouseholdId, world.Society.Inventory.GetLot("swim-repair-rope").OwnerId);
        Assert.Equal(footReturn ? 5 : 4, PersonalEquipmentRules.CarriedQuantity(world.Society.Inventory, actor, null));
        var order = Assert.Single(world.ExportState().Instructions!).Order!;
        Assert.Equal("swim-repair-cart", order.TargetCartLotId);
        Assert.Equal(0, order.CompletedUnits);
        Assert.Null(order.LastEffectId);
        if (!footReturn) Assert.Equal("blocked", order.Status);
        Assert.Equal(0, world.Society.Inventory.GetLot("swim-repair-cart").ConditionBasisPoints);
        Assert.Equal(cartPosition, new GridPoint(world.Society.Inventory.GetLot("swim-repair-cart").GroundPosition!.Value.X,
            world.Society.Inventory.GetLot("swim-repair-cart").GroundPosition!.Value.Y));
        Assert.Equal(4, world.Society.Inventory.GetLot("swim-repair-cargo").Quantity);
        Assert.Equal("swim-repair-cart", world.Society.Inventory.GetLot("swim-repair-cargo").ContainerLotId);
        Assert.DoesNotContain(world.Society.Inventory.Reservations, reservation => reservation.Id.StartsWith("cart-repair:", StringComparison.Ordinal));
        var saved = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False((await world.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        using var loaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved), _ => new ActionCoverageRecorder(chooseIdle: true));
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(loaded.ExportState()));
        world.Validate();
        loaded.Validate();
    }
}

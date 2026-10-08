using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class PrivateWorldConstructionOrderTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task ABlockedWarehouseCannotHideReachableConstructionWood(bool townStock, bool openApproach)
    {
        var setup = await Baseline.Value;
        var state = Decode(setup);
        var actor = setup.Actor;
        var warehouse = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == "first-town-warehouse");
        var approaches = state.Map.FootNeighbors(warehouse.Position).Where(state.Map.IsPassable)
            .OrderBy(point => point.X == warehouse.Position.X + 1 && point.Y == warehouse.Position.Y ? 0 : 1)
            .ThenBy(point => point.Y).ThenBy(point => point.X).ToArray();
        Assert.Equal(8, approaches.Length);
        Assert.DoesNotContain(setup.Site, approaches);
        var inventory = InventoryFixture.Reserve(state.Society.Society.Inventory, "construction-drain", Alpha,
            "construction-wood", 10, "fixture_work", long.MaxValue);
        inventory = InventoryFixture.ConsumeReservation(inventory, "construction-drain");
        inventory = InventoryFixture.AddLot(inventory, "construction-gather-axe", "crude_wooden_axe", actor, 1);
        if (townStock) inventory = InventoryFixture.AddLot(inventory, "blocked-town-wood", "wood", warehouse.TownId!, 10,
            storageBuildingId: warehouse.InstanceId);
        var blockers = state.Inhabitants.Where(person => person.InhabitantId != actor).ToArray();
        var positions = blockers.Select((person, index) => (person.InhabitantId, Position: approaches[index]))
            .ToDictionary(item => item.InhabitantId, item => item.Position, StringComparer.Ordinal);
        if (openApproach) positions[blockers[0].InhabitantId] = state.Map.FootNeighbors(setup.Site).First(point =>
            point != warehouse.Position && !approaches.Contains(point) && point != setup.Site);
        var day = state.WorldSystems!.Config.TicksPerDay;
        var horses = approaches.Skip(3).Select((point, index) => new AnimalState("warehouse-blocker-" + index, "Ash",
            "horse", "female", -(long)AnimalRules.Definition("horse").AdultDays * day,
            point, "warehouse-blockers", CareUntilTick: state.Society.Society.WorldTick + day,
            WildFedUntilTick: state.Society.Society.WorldTick + day, WildWaterUntilTick: state.Society.Society.WorldTick + day)).ToArray();
        state = WithInventory(state, inventory) with
        {
            AnimalWorld = new(true, horses, []),
            Inhabitants = state.Inhabitants.Select(person => person with
            {
                Position = person.InhabitantId == actor ? setup.Site : positions[person.InhabitantId],
                HungerBasisPoints = 10_000,
                LastDecisionContext = null,
                Project = null
            }).ToArray()
        };
        var occupied = positions.Values.Concat(horses.Select(animal => animal.Position)).ToHashSet();
        using (var routes = new UnoccupiedRouteSearch(state.Map, setup.Site, occupied, state.Map.FootStepCost))
            Assert.Equal(openApproach, routes.RouteTo(warehouse.Position, 0).Count > 0);
        var bytes = PrivateWorldRuntimeCodec.Encode(state);
        using var world = Restore(PrivateWorldRuntimeCodec.Decode(bytes));
        var receipt = Submit(world, actor, "reachable-clinic", BuildClinic(setup.Site));
        using var replay = Reload(world);
        bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False((await world.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        for (var tick = 0; tick < 100 && Order(world, receipt).Status != "finished"; tick++)
            await TickTogether(world, replay);
        Assert.Equal(("finished", 1), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
        var clinic = Assert.Single(world.WorldSimulation.Buildings, building => building.DefinitionId == setup.DefinitionId);
        Assert.Equal((Alpha, setup.Site), (clinic.HouseholdId, clinic.Position));
        Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.Id == "construction-stone");
        Assert.Equal(townStock && !openApproach ? 10 : 0, world.Society.Inventory.Lots
            .Where(lot => lot.OwnerId == warehouse.TownId && lot.ItemKind == "wood" && lot.StorageBuildingId == warehouse.InstanceId)
            .Sum(lot => lot.Quantity));
        var events = world.ExportState().Events;
        Assert.Equal(!townStock || !openApproach, events.Any(item => item.Kind == "material_gathered"));
        Assert.Equal(townStock && openApproach, events.Any(item => item.Kind == "town_resource_collected"));
        Assert.All(positions, item => Assert.Equal(item.Value, Person(world, item.Key).Position));
        Assert.All(horses, animal => Assert.Equal(animal.Position, world.Animals.Single(item => item.Id == animal.Id).Position));
        using var reload = Reload(world);
        reload.Validate();
        world.Validate();
    }
}

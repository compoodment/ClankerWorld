using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class RestaurantBusinessPipelineTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task VisitingCustomersCannotCollectResidentOnlyWarehouseEquipment(bool visiting)
    {
        var fixture = CreateFixture("porridge", kitchenReady: true);
        var state = fixture.State;
        var warehouse = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == "first-town-warehouse");
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory,
            "visiting-customer-warehouse-basket", "basket", warehouse.TownId!, 1, storageBuildingId: warehouse.InstanceId);
        state = WithInventory(state, inventory) with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == fixture.Customer
                ? person with { Position = warehouse.Position, HungerBasisPoints = 9_000 } : person).ToArray(),
        };
        Assert.DoesNotContain(state.Inhabitants, person => person.InhabitantId != fixture.Customer && person.Position == warehouse.Position);
        if (visiting) state = WithForeignTownResident(state, fixture.Customer);
        var choices = new PipelineProvider("equip_carry_aid");
        using var world = PrivateWorldRuntime.Restore(state, id => id == fixture.Customer ? choices : new PipelineProvider("safe_idle"));
        await Advance(world, 20);
        var basket = world.Society.Inventory.GetLot("visiting-customer-warehouse-basket");
        Assert.Equal(visiting ? warehouse.TownId : fixture.Customer, basket.OwnerId);
        Assert.Equal(visiting ? warehouse.InstanceId : null, basket.StorageBuildingId);
        Assert.Equal(!visiting, choices.SeenObservations.SelectMany(observation => observation.Candidates)
            .Any(candidate => candidate.Id == "equip_carry_aid"));
        world.Validate();
    }

    private static PrivateWorldRuntimeState WithForeignTownResident(PrivateWorldRuntimeState state, string actor)
    {
        var first = state.Towns!.Single();
        var residents = first.ResidentIds.Where(id => id != actor).ToArray();
        var adults = residents.Where(id => state.Society.Society.GetInhabitant(id).AgeBand is SocietyAgeBand.Adult or SocietyAgeBand.Elder);
        var site = state.Map.Tiles.Select(tile => tile.Position)
            .First(point => state.Map.IsBuildable(point) && !first.BorderTiles.Contains(point));
        return state with
        {
            Towns = [first with
            {
                ResidentIds = residents,
                Governance = TownGovernanceRules.Advance(first.Governance!, first.Id, state.WorldSeed,
                    adults, state.Society.Society.WorldTick, state.WorldSystems!.Config.TicksPerDay),
            }, new("town:visiting-resident", "Visitor Home", "founded", state.Society.Society.WorldTick,
                [actor], [], [site], site, TownGovernanceState.Create([actor]), TownGovernmentState.Create())],
        };
    }
}

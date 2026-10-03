using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class PrivateWorldFireOrderTests
{
    [Fact]
    public async Task AHouseBuiltAndBoundInOneTickCannotBeReplacedUntilItsIdentityTickHasPassed()
    {
        var state = Prepared(fuel: 0);
        var adults = state.Society.Society.Inhabitants.Where(person =>
                person.HouseholdId == Alpha && person.AgeBand == SocietyAgeBand.Adult)
            .OrderBy(person => person.Id, StringComparer.Ordinal).ToArray();
        Assert.True(adults.Length >= 2);
        var builder = adults[0].Id;
        var seeker = adults[1].Id;
        var original = House(state);
        var definition = state.WorldContent!.Buildings.Single(item => item.CanonicalId == original.DefinitionId);
        var cost = Assert.Single(definition.BuildCosts);
        Assert.Equal(("wood", 8), (cost.ResourceId, cost.Amount));
        state = ShelterOrderTestFixture.At(state, builder, original.Position);
        state = ShelterOrderTestFixture.At(state, seeker,
            ShelterOrderTestFixture.Approach(state, original.Position, distance: 4));
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory,
            "ordinary-house-payment", "wood", builder, cost.Amount);
        inventory = InventoryFixture.AddLot(inventory, "seeker-firewood", "wood", seeker, 2);
        inventory = InventoryFixture.AddLot(inventory, "later-house-payment", "wood", Alpha, cost.Amount);
        using (var removed = Restore(WithInventory(state, inventory)))
        {
            Assert.True(removed.RemoveBuilding(original.InstanceId, original.TownId, Alpha).Applied);
            state = removed.ExportState();
        }
        // A valid near-completion ordinary project still performs the last work tick and actual paid placement.
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == builder
                ? person with
                {
                    Project = new SettlementProject(TownConstructionCandidateIds.Building(definition.CanonicalId, original.Position),
                        definition.DisplayName, state.Society.Society.WorldTick, "working", WorkDone: 9,
                        LastTransitionTick: state.Society.Society.WorldTick),
                }
                : person).ToArray(),
        };
        using var world = Restore(state);
        var receipt = Submit(world, seeker, "same-tick-house-fire", AtHouse(original));
        await Tick(world);
        Assert.Equal(10, world.Inhabitants.Single(person => person.InhabitantId == builder).Project!.WorkDone);
        Assert.Null(Order(world, receipt).ShelterBinding);
        Assert.DoesNotContain(world.WorldSimulation.Buildings, building => building.HouseholdId == Alpha &&
            building.DefinitionId == definition.CanonicalId);
        await Tick(world);
        var built = Assert.Single(world.WorldSimulation.Buildings, building => building.HouseholdId == Alpha &&
            building.DefinitionId == definition.CanonicalId);
        var binding = Assert.IsType<OwnerShelterBinding>(Order(world, receipt).ShelterBinding);
        Assert.Equal((world.WorldTick, built.InstanceId, built.PlacedTick),
            (built.PlacedTick, binding.BuildingInstanceId, binding.BuildingPlacedTick!.Value));
        Assert.Equal(("completed", (string?)null),
            (world.Inhabitants.Single(person => person.InhabitantId == builder).Project!.Stage,
                world.Inhabitants.Single(person => person.InhabitantId == builder).Project!.OrderInstructionId));
        Assert.Empty(world.WorldSimulation.ConstructionReceipts ?? []);
        Assert.Contains(world.ExportState().Events, item => item.Kind == "build_completed" &&
            item.Detail.StartsWith(built.InstanceId + ":", StringComparison.Ordinal));
        var payment = Assert.Single(world.Society.Inventory.Reservations, reservation => reservation.LotId == "ordinary-house-payment");
        Assert.Equal((builder, cost.Amount, InventoryReservationState.Completed), (payment.OwnerId, payment.Quantity, payment.State));
        Assert.Equal(0, Wood(world, "ordinary-house-payment"));
        Assert.Equal(0, Order(world, receipt).CompletedUnits);
        Assert.Null(Order(world, receipt).ShelterCompletion);
        Assert.Empty(world.ExportState().Survival!.Fires);
        using (var admitted = Reload(world)) Assert.Equal(binding, Order(admitted, receipt).ShelterBinding);

        // Construction and the later actor's binding really share this tick; a same-tick replacement would match every field.
        Assert.True(world.RemoveBuilding(built.InstanceId, built.TownId, Alpha).Applied);
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        var sameTick = world.PlaceBuilding(built.InstanceId, built.DefinitionId, built.Position, Alpha);
        Assert.False(sameTick.Applied);
        Assert.Contains("reserved", sameTick.Failure!, StringComparison.Ordinal);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        Assert.Equal(cost.Amount, Wood(world, "later-house-payment"));

        // Ordinary builders reuse their deterministic ID: a different placement tick must remain legal.
        await Tick(world);
        var later = world.PlaceBuilding(built.InstanceId, built.DefinitionId, built.Position, Alpha);
        Assert.True(later.Applied, later.Failure);
        var replacement = world.WorldSimulation.Buildings.Single(building => building.InstanceId == built.InstanceId);
        Assert.Equal(built.PlacedTick + 1, replacement.PlacedTick);
        Assert.Equal(0, Wood(world, "later-house-payment"));
        using var replay = Reload(world);
        await TickTogether(world, replay);
        Assert.Equal(("blocked", 0, binding), (Order(world, receipt).Status,
            Order(world, receipt).CompletedUnits, Order(world, receipt).ShelterBinding));
        Assert.Null(Order(world, receipt).ShelterCompletion);
        Assert.Empty(world.ExportState().Survival!.Fires);
        Assert.Equal(2, Wood(world, "seeker-firewood"));
    }
}

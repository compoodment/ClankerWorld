using System.Collections.Concurrent;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class HouseholdDepartureTests
{
    private const string Alpha = "household:camp-alpha";
    private const string Beta = "household:camp-beta";

    [Fact]
    public async Task VoluntaryDeparturePreservesPropertyTownAndOnceOnlyPhysicalAllowanceAcrossReload()
    {
        var provider = new Choices();
        using var initial = NormalPathWorld.CreateGenerated("departure-normal", _ => provider);
        initial.Pause();
        var actor = initial.Society.GetHousehold(Alpha).MemberIds[0];
        var house = initial.WorldSimulation.Buildings.Single(building => building.InstanceId == "first-town-house-a");
        var state = initial.ExportState();
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "personal-coat", "padded_coat", actor, 1,
            storageBuildingId: house.InstanceId);
        inventory = InventoryFixture.AddLot(inventory, "borrowed-axe", "wooden_axe", Alpha, 1);
        inventory = InventoryFixture.Relocate(inventory, "borrow", "borrowed-axe", Alpha, 1, actor);
        state = WithInventory(state, inventory);
        using var world = PrivateWorldRuntime.Restore(state, _ => provider);
        provider.Wanted[actor] = "household_leave";
        var town = world.Towns.Single().ResidentIds.ToArray();
        world.Resume();
        await AdvanceUntil(world, () => world.Society.GetInhabitant(actor).HouseholdId is null);
        provider.Wanted[actor] = "safe_idle";
        var departure = Assert.Single(world.Inhabitants.Single(person => person.InhabitantId == actor).Departures!);
        Assert.InRange(departure.AllowancePortions, 0, 2);
        Assert.Equal("voluntary", departure.Cause);
        Assert.Equal(actor, world.Society.Inventory.GetLot("personal-coat").OwnerId);
        Assert.Equal(house.InstanceId, world.Society.Inventory.GetLot("personal-coat").StorageBuildingId);
        Assert.Equal(Alpha, world.Society.Inventory.GetLot("borrowed-axe").OwnerId);
        Assert.Equal(actor, world.Society.Inventory.GetLot("borrowed-axe").CarrierId);
        Assert.Equal(town, world.Towns.Single().ResidentIds);
        Assert.Equal(Alpha, world.WorldSimulation.Buildings.Single(item => item.InstanceId == house.InstanceId).HouseholdId);
        Assert.All(departure.AllowanceLotIds, id =>
        {
            var lot = world.Society.Inventory.GetLot(id);
            Assert.Equal(actor, lot.OwnerId);
            Assert.False(PersonalEquipmentRules.IsCarried(lot, actor));
        });
        world.Pause();
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), _ => provider);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        Assert.False(restored.DisplaceAdult(actor));
        Assert.Single(restored.Inhabitants.Single(person => person.InhabitantId == actor).Departures!);
        provider.Wanted[actor] = "household_collect:personal-coat";
        restored.Resume();
        await AdvanceUntil(restored, () => PersonalEquipmentRules.IsCarried(restored.Society.Inventory.GetLot("personal-coat"), actor), 180);
        Assert.Null(restored.Society.GetInhabitant(actor).HouseholdId);
        Assert.Equal(actor, restored.Society.Inventory.GetLot("personal-coat").OwnerId);
        provider.Wanted[actor] = "household_return:borrowed-axe";
        await AdvanceUntil(restored, () => restored.Society.Inventory.GetLot("borrowed-axe").StorageBuildingId == house.InstanceId, 180);
        Assert.Equal(Alpha, restored.Society.Inventory.GetLot("borrowed-axe").OwnerId);
        Assert.Single(restored.Inhabitants.Single(person => person.InhabitantId == actor).Departures!);
    }

    [Fact]
    public void DisplacementDoesNotInventFoodOrTouchReservationsAndLastAdultPropertySurvives()
    {
        using var initial = NormalPathWorld.CreateGenerated("departure-reserved", _ => new Choices());
        initial.Pause();
        var state = initial.ExportState();
        var inventory = state.Society.Society.Inventory;
        inventory = inventory with { Lots = inventory.Lots.Where(lot => lot.OwnerId != Alpha || lot.ItemKind != "food").ToArray() };
        inventory = InventoryFixture.AddLot(inventory, "departure-food", "food", Alpha, 3);
        inventory = InventoryFixture.Reserve(inventory, "care-food", Alpha, "departure-food", 2, "care", long.MaxValue);
        state = WithInventory(state, inventory);
        using var world = PrivateWorldRuntime.Restore(state);
        var members = world.Society.GetHousehold(Alpha).MemberIds.ToArray();
        Assert.True(world.DisplaceAdult(members[0]));
        var first = Assert.Single(world.Inhabitants.Single(person => person.InhabitantId == members[0]).Departures!);
        Assert.Equal(1, first.AllowancePortions);
        Assert.Equal(2, world.Society.Inventory.GetLot("departure-food").Quantity);
        Assert.Equal(InventoryReservationState.Reserved, world.Society.Inventory.GetReservation("care-food").State);
        Assert.True(world.DisplaceAdult(members[1]));
        Assert.Empty(world.Society.GetHousehold(Alpha).MemberIds);
        Assert.Equal(0, Assert.Single(world.Inhabitants.Single(person => person.InhabitantId == members[1]).Departures!).AllowancePortions);
        Assert.Contains(world.WorldSimulation.Buildings, item => item.HouseholdId == Alpha);
        Assert.Equal(Alpha, world.Society.Inventory.GetLot("departure-food").OwnerId);
        Assert.False(world.DisplaceAdult(members[1]));
        Assert.Equal(2, world.Society.Inventory.GetLot("departure-food").Quantity);
    }

    [Fact]
    public async Task HomelessAdultChecksExistingHomesBeforeSoloFormationWithoutFreeResources()
    {
        var provider = new Choices();
        using var initial = NormalPathWorld.CreateGenerated("departure-solo", _ => provider);
        var actor = initial.Society.GetHousehold(Alpha).MemberIds[0];
        Assert.True(initial.DisplaceAdult(actor));
        provider.Wanted[actor] = "household_found";
        await AdvanceUntil(initial, () => provider.Offered.ContainsKey(actor));
        Assert.DoesNotContain("household_found", provider.Offered[actor]);
        Assert.Contains(provider.Offered[actor], item => item.StartsWith("household_ask:", StringComparison.Ordinal));
        initial.Pause();
        var state = initial.ExportState();
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { Housing = new(Refusals: [new(Alpha, state.Society.Society.WorldTick), new(Beta, state.Society.Society.WorldTick)]) }
                : person).ToArray(),
        };
        var beforeLots = state.Society.Society.Inventory.Lots.ToArray();
        var beforeBuildings = state.WorldSimulation!.Buildings.ToArray();
        using var world = PrivateWorldRuntime.Restore(state, _ => provider);
        world.Resume();
        await AdvanceUntil(world, () => world.Society.GetInhabitant(actor).HouseholdId is { } home && home != Alpha && home != Beta);
        var solo = world.Society.GetHousehold(world.Society.GetInhabitant(actor).HouseholdId!);
        Assert.Equal([actor], solo.MemberIds);
        Assert.DoesNotContain(world.WorldSimulation.Buildings, item => item.HouseholdId == solo.Id);
        Assert.Equal(beforeBuildings.Length, world.WorldSimulation.Buildings.Count);
        Assert.Equal(beforeLots.Sum(lot => lot.Quantity), world.Society.Inventory.Lots.Sum(lot => lot.Quantity));
        Assert.NotNull(world.Inhabitants.Single(person => person.InhabitantId == actor).Housing?.Blocker);
    }

    [Fact]
    public void SoleCaregiverCannotBeDisplacedAndVoluntaryExitMovesCompleteGroupWithoutTeleporting()
    {
        using var initial = NormalPathWorld.CreateGenerated("departure-care", _ => new Choices());
        initial.Pause();
        var state = initial.ExportState();
        var actor = initial.Society.GetHousehold(Alpha).MemberIds[0];
        var other = initial.Society.GetHousehold(Alpha).MemberIds[1];
        var checkpoint = state.Society.Society;
        var child = other; // A younger dependent in the same physical checkpoint, with unchanged identity and position.
        checkpoint = checkpoint with
        {
            Inhabitants = checkpoint.Inhabitants.Select(person => person.Id == child
                ? person with
                {
                    AgeBand = SocietyAgeBand.Infant,
                    BirthTick = checkpoint.WorldTick,
                    BirthLifeTick = null,
                    LastLifecycleYearChecked = 0,
                    PrimaryCaregiverId = actor,
                    DomesticFamilyUnitId = checkpoint.GetInhabitant(actor).DomesticFamilyUnitId
                } : person).ToArray(),
        };
        // Exercise the independent society move contract without modifying any physical position.
        var position = state.Inhabitants.Single(person => person.InhabitantId == child).Position;
        var left = SocietyFixture.LeaveHousehold(checkpoint, actor).Checkpoint;
        Assert.Null(left.GetInhabitant(actor).HouseholdId);
        Assert.Null(left.GetInhabitant(child).HouseholdId);
        Assert.Equal(actor, left.GetInhabitant(child).PrimaryCaregiverId);
        Assert.Equal(position, state.Inhabitants.Single(person => person.InhabitantId == child).Position);
        Assert.Equal(2, SocietyFixture.MovingCareGroup(left, actor).Count);
        Assert.Equal(checkpoint.Relationships.Count, left.Relationships.Count);
    }

    private static PrivateWorldRuntimeState WithInventory(PrivateWorldRuntimeState state, InventoryCheckpoint inventory) => state with
    {
        Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
    };

    private static async Task AdvanceUntil(PrivateWorldRuntime world, Func<bool> ready, int maxTicks = 80)
    {
        for (var tick = 0; tick < maxTicks && !ready(); tick++) await world.AdvanceOneTickAsync();
        Assert.True(ready(), $"Expected household state was not reached within {maxTicks} ticks.");
    }

    private sealed class Choices : IDecisionProvider
    {
        public ConcurrentDictionary<string, string> Wanted { get; } = new(StringComparer.Ordinal);
        public ConcurrentDictionary<string, string[]> Offered { get; } = new(StringComparer.Ordinal);
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var observation = request.Observation;
            Offered[observation.InhabitantId] = observation.Candidates.Select(item => item.Id).ToArray();
            var wanted = Wanted.GetValueOrDefault(observation.InhabitantId, "safe_idle");
            var selected = observation.Candidates.FirstOrDefault(item => item.Id.StartsWith(wanted, StringComparison.Ordinal))
                ?? observation.Candidates.Single(item => item.Id == "safe_idle");
            return new DeterministicDecisionProvider().DecideAsync(request with { Observation = observation with { Candidates = [selected] } }, cancellationToken);
        }
    }
}

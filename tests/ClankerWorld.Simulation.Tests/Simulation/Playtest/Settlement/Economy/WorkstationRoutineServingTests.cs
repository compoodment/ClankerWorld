using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class WorkstationRoutineServingTests
{
    [Fact]
    public async Task WorkstationSupplyKeepsOneRoutineServingButMovesSurplusBeforeNaturalConsumptionAcrossReplay()
    {
        var (state, actor) = Prepared("routine-serving-supply", 4_100);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "b-surplus-food", "wild_greens", actor, 1);
        state = FarmFieldTests.WithInventory(state, inventory);
        using var world = PrivateWorldRuntime.Restore(state, id => new MealAndSupply(id == actor));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(actor, world.Society.Inventory.GetLot("a-routine-serving").OwnerId);
        Assert.Null(world.Society.Inventory.GetLot("a-routine-serving").StorageBuildingId);
        var supplied = world.Society.Inventory.GetLot("b-surplus-food");
        Assert.Equal("household:camp-alpha", supplied.OwnerId);
        Assert.Equal("first-town-house-a", supplied.StorageBuildingId);
        Assert.Equal(4, world.Society.Inventory.Lots.Where(lot => lot.ItemKind == "wild_greens" &&
            lot.StorageBuildingId == "first-town-house-a").Sum(lot => lot.Quantity));
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())),
            id => new MealAndSupply(id == actor));
        for (var tick = 0; tick < 64 && !Consumed(world, actor); tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
        }
        Assert.True(Consumed(world, actor));
        Assert.True(world.Inhabitants.Single(person => person.InhabitantId == actor).HungerBasisPoints > 4_100);
        Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.Id == "a-routine-serving");
        Assert.Single(world.ExportState().Events, item => item.Kind == "workstation_supplied");
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "household_food_collected");
        Assert.Equal(4, world.Society.Inventory.Lots.Where(lot => lot.ItemKind == "wild_greens" &&
            lot.StorageBuildingId == "first-town-house-a").Sum(lot => lot.Quantity));
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
    }

    [Fact]
    public async Task AFullAgentCanSupplyItsLastEdibleIngredient()
    {
        var (state, actor) = Prepared("full-serving-supply", 5_000);
        using var world = PrivateWorldRuntime.Restore(state, id => new MealAndSupply(id == actor));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var supplied = world.Society.Inventory.GetLot("a-routine-serving");
        Assert.Equal("household:camp-alpha", supplied.OwnerId);
        Assert.Equal("first-town-house-a", supplied.StorageBuildingId);
        Assert.Single(world.ExportState().Events, item => item.Kind == "workstation_supplied");
        Assert.False(Consumed(world, actor));
    }

    private static bool Consumed(PrivateWorldRuntime world, string actor) => world.Society.Inventory.Reservations.Any(reservation =>
        reservation.LotId == "a-routine-serving" && reservation.OwnerId == actor && reservation.Quantity == 1 &&
        reservation.Purpose == "direct_consumption" && reservation.State == InventoryReservationState.Completed);

    private static (PrivateWorldRuntimeState State, string Actor) Prepared(string seed, int fullness)
    {
        using var generated = NormalPathWorld.CreateGenerated(seed, _ => new MealAndSupply(false));
        var state = FarmFieldTests.FeedHouseholdFromAvailableStock(generated.ExportState(), "household:camp-alpha");
        var actor = state.Society.Society.Inhabitants.First(person => person.HouseholdId == "household:camp-alpha").Id;
        var home = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == "first-town-house-a");
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "home-greens", "wild_greens", "household:camp-alpha", 3,
            storageBuildingId: home.InstanceId);
        inventory = InventoryFixture.AddLot(inventory, "a-routine-serving", "wild_greens", actor, 1);
        state = FarmFieldTests.WithInventory(state, inventory) with
        {
            Survival = new SettlementSurvivalState(0, []),
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { Position = home.Position, HungerBasisPoints = fullness, Survival = new SurvivalCondition() } : person).ToArray(),
        };
        return (state, actor);
    }

    private sealed class MealAndSupply(bool active) : IDecisionProvider
    {
        private readonly DeterministicDecisionProvider chooser = new();
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var candidates = request.Observation.Candidates.Where(candidate => active && candidate.Id is
                "consume_food" or "collect_shared_food" or "supply_workstation:wild_greens").ToArray();
            if (candidates.Length == 0) candidates = [request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle")];
            return chooser.DecideAsync(request with { Observation = request.Observation with { Candidates = candidates } }, cancellationToken);
        }
    }
}

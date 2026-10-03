using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class OrchardSeedFoodRecoveryTests(OrchardSeedCargoFixture fixture)
    : IClassFixture<OrchardSeedCargoFixture>
{
    [Fact]
    public async Task ActualHarvestSeedsMakeRoomForFoodOnlyWhenTheyReachStorage()
    {
        var state = fixture.CreateState();
        var actor = fixture.Actor;
        var household = fixture.Household;
        var originalClaims = state.Society.Society.Inventory.Reservations
            .Where(item => item.OwnerId == actor && item.Purpose == "orchard_replanting" &&
                item.State == InventoryReservationState.Reserved).ToArray();
        Assert.Equal(5, originalClaims.Length);
        var choices = new Choices(["consume_food", "make_room_for_food", "harvest_food", "seek_food"]);
        var world = Restore(state, actor, choices);
        try
        {
            var start = world.Inhabitants.Single(person => person.InhabitantId == actor).Position;
            await AdvanceUntil(world, () => world.Inhabitants.Single(person => person.InhabitantId == actor).Position != start, 12);
            Assert.Contains("make_room_for_food", choices.Offered);
            Assert.Empty(Setdowns(world, actor));
            Assert.All(originalClaims, claim => Assert.Equal(claim, world.Society.Inventory.GetReservation(claim.Id)));
            Assert.Equal(5, CarriedSeeds(world.Society.Inventory, actor));

            // Loading while walking must retain the claims until a physical setdown commits.
            world.Validate();
            var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
            world.Dispose();
            world = Restore(PrivateWorldRuntimeCodec.Decode(bytes), actor, choices);
            await AdvanceUntil(world, () => Setdowns(world, actor).Length > 0, 60);
            var inventory = world.Society.Inventory;
            var stored = Assert.Single(Setdowns(world, actor));
            Assert.StartsWith(actor + ":orchard_seed:1:", stored.Detail);
            Assert.Single(originalClaims, claim => inventory.GetReservation(claim.Id).State == InventoryReservationState.Released);
            Assert.Equal(4, originalClaims.Count(claim => inventory.GetReservation(claim.Id).State == InventoryReservationState.Reserved));
            Assert.Equal(4, CarriedSeeds(inventory, actor));
            Assert.Equal(1, inventory.Lots.Where(lot => lot.OwnerId == household && lot.ItemKind == "orchard_seed").Sum(lot => lot.Quantity));
            Assert.Equal(5, inventory.Lots.Where(lot => lot.ItemKind == "orchard_seed").Sum(lot => lot.Quantity));

            var fullness = world.Inhabitants.Single(person => person.InhabitantId == actor).HungerBasisPoints;
            await AdvanceUntil(world, () => world.ExportState().Events.Any(item => item.Kind == "food_consumed" &&
                item.Detail == actor), 60);
            Assert.Contains(world.ExportState().Events, item => item.Kind == "food_harvested" &&
                item.Detail.StartsWith(actor + ":", StringComparison.Ordinal));
            Assert.True(world.Inhabitants.Single(person => person.InhabitantId == actor).HungerBasisPoints > fullness);
            Assert.Equal(4, CarriedSeeds(world.Society.Inventory, actor));
            world.Validate();
        }
        finally { world.Dispose(); }
    }

    [Theory]
    [InlineData(5, 4)]
    [InlineData(4, 4)]
    public async Task AGroupedSeedLotKeepsEveryReservationUnitNotNeededForFood(int reserved, int remaining)
    {
        var state = fixture.CreateState();
        var actor = fixture.Actor;
        var inventory = state.Society.Society.Inventory;
        var seedIds = inventory.Lots.Where(lot => lot.OwnerId == actor && lot.ItemKind == "orchard_seed")
            .Select(lot => lot.Id).ToHashSet(StringComparer.Ordinal);
        // A grouped valid lot exercises partial reservation release independently of harvest lot size.
        inventory = inventory with
        {
            Lots = inventory.Lots.Where(lot => !seedIds.Contains(lot.Id)).ToArray(),
            Reservations = inventory.Reservations.Where(item => !seedIds.Contains(item.LotId)).ToArray(),
        };
        inventory = InventoryFixture.AddLot(inventory, "grouped-orchard-seeds", "orchard_seed", actor, 5);
        inventory = InventoryFixture.Reserve(inventory, "orchard-replant:grouped-orchard-seeds", actor,
            "grouped-orchard-seeds", reserved, "orchard_replanting", long.MaxValue);
        state = state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } };
        using var world = Restore(state, actor, new Choices(["make_room_for_food"]));
        await AdvanceUntil(world, () => Setdowns(world, actor).Length > 0, 60);
        var after = world.Society.Inventory;
        Assert.Equal(4, after.GetLot("grouped-orchard-seeds").Quantity);
        Assert.Equal(remaining, after.GetReservation("orchard-replant:grouped-orchard-seeds").Quantity);
        Assert.Equal(InventoryReservationState.Reserved, after.GetReservation("orchard-replant:grouped-orchard-seeds").State);
        Assert.Equal(5, after.Lots.Where(lot => lot.ItemKind == "orchard_seed").Sum(lot => lot.Quantity));
        var stored = Assert.Single(after.Lots, lot => lot.OwnerId == fixture.Household && lot.ItemKind == "orchard_seed");
        Assert.Equal(1, stored.Quantity);
        Assert.True(stored.StorageBuildingId is not null || stored.GroundPosition is not null);
        using var loaded = Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())), actor, new Choices([]));
        loaded.Validate();
        Assert.Equal(after.GetReservation("orchard-replant:grouped-orchard-seeds"),
            loaded.Society.Inventory.GetReservation("orchard-replant:grouped-orchard-seeds"));
    }

    private static int CarriedSeeds(InventoryCheckpoint inventory, string actor) => inventory.Lots
        .Where(lot => lot.ItemKind == "orchard_seed" && PersonalEquipmentRules.IsCarried(lot, actor)).Sum(lot => lot.Quantity);

    private static PlaytestWorldEvent[] Setdowns(PrivateWorldRuntime world, string actor) => world.ExportState().Events
        .Where(item => item.Kind == "spare_cargo_stored" && item.Detail.StartsWith(actor + ":", StringComparison.Ordinal)).ToArray();

    private static PrivateWorldRuntime Restore(PrivateWorldRuntimeState state, string actor, Choices choices) =>
        PrivateWorldRuntime.Restore(state, id => id == actor ? choices : new Choices([]));

    private static async Task AdvanceUntil(PrivateWorldRuntime world, Func<bool> done, int maximumTicks)
    {
        for (var tick = 0; tick < maximumTicks && !done(); tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.True(done(), $"Expected recovery step did not finish within {maximumTicks} ticks.");
    }

    private sealed class Choices(string[] actions) : IDecisionProvider
    {
        public HashSet<string> Offered { get; } = new(StringComparer.Ordinal);
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;

        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            Offered.UnionWith(request.Observation.Candidates.Select(candidate => candidate.Id));
            var selected = actions.Select(action => request.Observation.Candidates.FirstOrDefault(candidate => candidate.Id == action))
                .FirstOrDefault(candidate => candidate is not null) ?? request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle");
            return new DeterministicDecisionProvider().DecideAsync(request with
            {
                Observation = request.Observation with { Candidates = [selected] },
            }, cancellationToken);
        }
    }
}

using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
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
            using var replay = Restore(PrivateWorldRuntimeCodec.Decode(bytes), actor,
                new Choices(["consume_food", "make_room_for_food", "harvest_food", "seek_food"]));
            await AdvanceUntil(world, () => Setdowns(world, actor).Length > 0, 60, replay);
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
                item.Detail == actor), 60, replay);
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
        // A grouped valid lot exercises partial reservation release independently of harvest lot size.
        state = WithGroupedSeeds(state, actor, 5, reserved);
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

    [Fact]
    public async Task OrdinaryCargoMakesRoomBeforeAnyOrchardReservationIsReleased()
    {
        var actor = fixture.Actor;
        var state = WithGroupedSeeds(fixture.CreateState(), actor, 4, 4);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory,
            "food-recovery-spare-stone", "stone", actor, 1);
        state = WithInventory(state, inventory);
        using var world = Restore(state, actor, new Choices(["make_room_for_food"]));
        await AdvanceUntil(world, () => Setdowns(world, actor).Length > 0, 60);

        Assert.StartsWith(actor + ":stone:1:", Assert.Single(Setdowns(world, actor)).Detail);
        Assert.Equal(4, CarriedSeeds(world.Society.Inventory, actor));
        Assert.Equal(inventory.Reservations, world.Society.Inventory.Reservations);
        var stored = world.Society.Inventory.GetLot("food-recovery-spare-stone");
        Assert.Equal(fixture.Household, stored.OwnerId);
        Assert.Equal(1, stored.Quantity);
        Assert.True(stored.StorageBuildingId is not null || stored.GroundPosition is not null);
        world.Validate();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnrelatedAndMixedWorkReservationsStayProtected(bool mixed)
    {
        var actor = fixture.Actor;
        var state = WithGroupedSeeds(fixture.CreateState(), actor, 5, mixed ? 4 : 5);
        var inventory = state.Society.Society.Inventory;
        inventory = mixed
            ? InventoryFixture.Reserve(inventory, "other-seed-work", actor, "grouped-orchard-seeds", 1,
                "other_work", long.MaxValue)
            : inventory with
            {
                Reservations = inventory.Reservations.Select(item => item.LotId == "grouped-orchard-seeds"
                    ? item with { Purpose = "other_work" } : item).ToArray(),
            };
        state = WithInventory(state, inventory);
        var choices = new Choices(["make_room_for_food"]);
        using var world = Restore(state, actor, choices);
        await AdvanceUntil(world, () => choices.Offered.Count > 0, 12);

        Assert.DoesNotContain("make_room_for_food", choices.Offered);
        Assert.Empty(Setdowns(world, actor));
        Assert.Equal(inventory.Reservations, world.Society.Inventory.Reservations);
        Assert.Equal(5, CarriedSeeds(world.Society.Inventory, actor));
        world.Validate();
    }

    [Fact]
    public async Task AFedHarvesterKeepsItsSeedsReservedForPlanting()
    {
        var actor = fixture.Actor;
        var state = fixture.CreateState();
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { HungerBasisPoints = 9_000 } : person).ToArray(),
        };
        var choices = new Choices(["make_room_for_food"]);
        using var world = Restore(state, actor, choices);
        await AdvanceUntil(world, () => choices.Offered.Count > 0, 12);

        Assert.Contains("plant_orchard", choices.Offered);
        Assert.DoesNotContain("make_room_for_food", choices.Offered);
        Assert.Empty(Setdowns(world, actor));
        Assert.Equal(state.Society.Society.Inventory.Reservations, world.Society.Inventory.Reservations);
        Assert.Equal(5, CarriedSeeds(world.Society.Inventory, actor));
        world.Validate();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AHouseholdServingThatAlreadyFitsLeavesEverySeedReserved(bool inPot)
    {
        var actor = fixture.Actor;
        var state = fixture.CreateState();
        var house = state.WorldSimulation!.Buildings.Single(building => building.HouseholdId == fixture.Household &&
            state.WorldContent!.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId).Tags.Contains("house"));
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "orchard-household-serving", "food",
            fixture.Household, 1, storageBuildingId: house.InstanceId);
        if (inPot)
        {
            inventory = InventoryFixture.AddLot(inventory, "orchard-serving-pot", InventoryContainerRules.StoragePot,
                fixture.Household, 1, storageBuildingId: house.InstanceId);
            inventory = InventoryFixture.PutIntoContainer(inventory, "orchard-serving-pot-fill", fixture.Household,
                "orchard-serving-pot", "orchard-household-serving", 1);
        }
        var claims = inventory.Reservations.Where(item => item.Purpose == "orchard_replanting").ToArray();
        var choices = new Choices(["consume_food", "collect_shared_food", "make_room_for_food"]);
        using var world = Restore(WithInventory(state, inventory), actor, choices);
        var fullness = world.Inhabitants.Single(person => person.InhabitantId == actor).HungerBasisPoints;
        await AdvanceUntil(world, () => world.ExportState().Events.Any(item => item.Kind == "food_consumed" &&
            item.Detail == actor), 60);

        Assert.DoesNotContain("make_room_for_food", choices.Offered);
        Assert.Empty(Setdowns(world, actor));
        Assert.All(claims, claim => Assert.Equal(claim, world.Society.Inventory.GetReservation(claim.Id)));
        Assert.Equal(5, CarriedSeeds(world.Society.Inventory, actor));
        Assert.True(world.Inhabitants.Single(person => person.InhabitantId == actor).HungerBasisPoints > fullness);
        if (inPot)
            Assert.Contains(world.Society.Inventory.Events, item => item.Kind == "container_contents_taken");
        world.Validate();
    }

    [Fact]
    public async Task UnreachableHouseAndCampCannotReleaseOrRelocateSeeds()
    {
        var actor = fixture.Actor;
        var state = fixture.CreateState();
        var house = state.WorldSimulation!.Buildings.Single(building => building.HouseholdId == fixture.Household &&
            state.WorldContent!.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId).Tags.Contains("house"));
        var camp = state.Map.CampObjects.FirstOrDefault(item => item.Id == "storage")?.Position ??
            state.WorldSimulation.Buildings.Single(building => building.InstanceId == "first-town-warehouse").Position;
        var occupied = state.WorldSimulation.Buildings.SelectMany(building => WorldContentSimulationRules.Footprint(
                state.WorldContent!.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId), building))
            .Concat(state.Inhabitants.Where(person => person.InhabitantId != actor).Select(person => person.Position))
            .Concat(state.Map.Resources.Select(resource => resource.Position))
            .Concat(state.Map.CampObjects.Select(item => item.Position)).ToHashSet();
        var isolated = state.Map.Resources.Where(resource => resource.Kind == "food" && resource.TreeKind is null &&
                state.WorldSystems!.Ecology.GetResource(resource.Id).Quantity > 0)
            .SelectMany(resource => state.Map.FootNeighbors(resource.Position))
            .Where(point => !occupied.Contains(point) && state.Map.FootDistance(point, camp) > 1 &&
                !state.Map.IsReachableOnFoot(point, house.Position) && !state.Map.IsReachableOnFoot(point, camp)).ToArray();
        Assert.NotEmpty(isolated);
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { Position = isolated[0], TravelCooldownTicks = 0, LastDecisionContext = null } : person).ToArray(),
        };
        var choices = new Choices(["make_room_for_food"]);
        using var world = Restore(state, actor, choices);
        await AdvanceUntil(world, () => choices.Offered.Count > 0, 12);

        Assert.DoesNotContain("make_room_for_food", choices.Offered);
        Assert.Empty(Setdowns(world, actor));
        Assert.Equal(state.Society.Society.Inventory.Reservations, world.Society.Inventory.Reservations);
        Assert.Equal(5, CarriedSeeds(world.Society.Inventory, actor));
        Assert.Equal(isolated[0], world.Inhabitants.Single(person => person.InhabitantId == actor).Position);
        world.Validate();
    }

    private static PrivateWorldRuntimeState WithGroupedSeeds(PrivateWorldRuntimeState state, string actor, int quantity, int reserved)
    {
        var inventory = state.Society.Society.Inventory;
        var seedIds = inventory.Lots.Where(lot => lot.OwnerId == actor && lot.ItemKind == "orchard_seed")
            .Select(lot => lot.Id).ToHashSet(StringComparer.Ordinal);
        inventory = inventory with
        {
            Lots = inventory.Lots.Where(lot => !seedIds.Contains(lot.Id)).ToArray(),
            Reservations = inventory.Reservations.Where(item => !seedIds.Contains(item.LotId)).ToArray(),
        };
        inventory = InventoryFixture.AddLot(inventory, "grouped-orchard-seeds", "orchard_seed", actor, quantity);
        inventory = InventoryFixture.Reserve(inventory, "orchard-replant:grouped-orchard-seeds", actor,
            "grouped-orchard-seeds", reserved, "orchard_replanting", long.MaxValue);
        return WithInventory(state, inventory);
    }

    private static PrivateWorldRuntimeState WithInventory(PrivateWorldRuntimeState state, InventoryCheckpoint inventory) =>
        state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } };

    private static int CarriedSeeds(InventoryCheckpoint inventory, string actor) => inventory.Lots
        .Where(lot => lot.ItemKind == "orchard_seed" && PersonalEquipmentRules.IsCarried(lot, actor)).Sum(lot => lot.Quantity);

    private static PlaytestWorldEvent[] Setdowns(PrivateWorldRuntime world, string actor) => world.ExportState().Events
        .Where(item => item.Kind == "spare_cargo_stored" && item.Detail.StartsWith(actor + ":", StringComparison.Ordinal)).ToArray();

    private static PrivateWorldRuntime Restore(PrivateWorldRuntimeState state, string actor, Choices choices) =>
        PrivateWorldRuntime.Restore(state, id => id == actor ? choices : new Choices([]));

    private static async Task AdvanceUntil(PrivateWorldRuntime world, Func<bool> done, int maximumTicks,
        PrivateWorldRuntime? replay = null)
    {
        for (var tick = 0; tick < maximumTicks && !done(); tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            if (replay is not null) Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
        }
        Assert.True(done(), $"Expected recovery step did not finish within {maximumTicks} ticks.");
        if (replay is not null)
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
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

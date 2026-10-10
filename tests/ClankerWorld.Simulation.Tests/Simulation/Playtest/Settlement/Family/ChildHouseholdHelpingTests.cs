using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Tests;

public sealed class ChildHouseholdHelpingTests
{
    private static readonly Lazy<Task<byte[]>> Generated = new(async () =>
    {
        using var world = NormalPathWorld.CreateGenerated("child-helping-973eb221", _ => new ActionCoverageRecorder(chooseIdle: true));
        for (var tick = 0; tick < 5; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        return PrivateWorldRuntimeCodec.Encode(world.ExportState());
    });

    [Theory]
    [InlineData("food", "berries", 4)]
    [InlineData("wood", "wood", 1)]
    public async Task BornChildGathersARealSmallLoadAndPhysicallyDeliversItAcrossRollbackAndReplay(string kind, string itemKind, int quantity)
    {
        var (state, child, house, source) = await Prepared(kind);
        var provider = new HelpingProvider("child_gather:" + kind + ":");
        using var world = Restore(state, child, provider);
        var baseline = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        var refused = false;
        for (var tick = 0; tick < 12 && !refused; tick++)
        {
            baseline = PrivateWorldRuntimeCodec.Encode(world.ExportState());
            provider.Chosen.Clear();
            refused = !(await world.AdvanceOneTickAsync(() => !provider.Chosen.Any(candidate =>
                candidate.StartsWith("child_gather:" + kind + ":", StringComparison.Ordinal)))).Advanced;
        }
        Assert.True(refused, string.Join("\n", provider.Seen));
        Assert.Equal(baseline, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        Assert.Contains("child_gather:" + kind + ":" + source.Id, provider.Seen);
        var ecologyBefore = world.WorldSystems.Ecology.GetResource(source.Id).Quantity;
        var household = world.Society.GetInhabitant(child).HouseholdId!;
        var storedBefore = Stored(world, household, house, itemKind);
        using var replay = Restore(PrivateWorldRuntimeCodec.Decode(baseline), child, new HelpingProvider("child_gather:" + kind + ":"));
        await StepPair(world, replay);
        var cargo = Assert.Single(world.Society.Inventory.Lots, lot => lot.OwnerId == child && lot.DeliveryBuildingId == house);
        Assert.Equal((itemKind, quantity), (cargo.ItemKind, cargo.Quantity));
        Assert.Equal(ecologyBefore - 1, world.WorldSystems.Ecology.GetResource(source.Id).Quantity);
        Assert.Equal(storedBefore, Stored(world, household, house, itemKind));
        Assert.Equal(source.Position, world.Inhabitants.Single(person => person.InhabitantId == child).Position);
        var midTrip = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var resumed = Restore(PrivateWorldRuntimeCodec.Decode(midTrip), child, new HelpingProvider("child_gather:" + kind + ":"));
        Assert.Equal(midTrip, PrivateWorldRuntimeCodec.Encode(resumed.ExportState()));
        provider.Gather = false;
        for (var tick = 0; tick < 35 && !world.ExportState().Events.Any(item => item.Kind == "child_delivered_household"); tick++)
            await StepPair(world, resumed);
        Assert.Single(world.ExportState().Events, item => item.Kind == "child_delivered_household");
        Assert.Equal(storedBefore + quantity, Stored(world, household, house, itemKind));
        Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.OwnerId == child && lot.DeliveryBuildingId == house);
        Assert.Equal(state.WorldSimulation!.Buildings.Single(item => item.InstanceId == house).Position,
            world.Inhabitants.Single(person => person.InhabitantId == child).Position);
        Assert.Null(world.Inhabitants.Single(person => person.InhabitantId == child).Project);
        Assert.Equal(SocietyWorkRole.Unassigned, world.Society.GetInhabitant(child).CurrentRole);
        world.Validate();
    }

    [Theory]
    [InlineData("food")]
    [InlineData("wood")]
    public async Task ChildCarriesOnlyUnreservedHouseholdStockWithinItsSmallTotalLoad(string kind)
    {
        var (state, child, house, source) = await Prepared("food");
        var checkpoint = state.Society.Society;
        var household = checkpoint.GetInhabitant(child).HouseholdId!;
        var inventory = InventoryFixture.AddLot(checkpoint.Inventory, "child-ground-stock", kind, household, 9,
            groundPosition: new(source.Position.X, source.Position.Y));
        inventory = InventoryFixture.Reserve(inventory, "child-stock-held", household, "child-ground-stock", 7,
            "other_work", checkpoint.WorldTick + 100);
        inventory = InventoryFixture.AddLot(inventory, "child-personal-ballast", "stone", child, 3);
        state = state with { Society = state.Society with { Society = checkpoint with { Inventory = inventory } } };
        var provider = new HelpingProvider("child_carry:child-ground-stock");
        using var world = Restore(state, child, provider);
        var storedBefore = Stored(world, household, house, kind);
        for (var tick = 0; tick < 12 && !world.Society.Inventory.Lots.Any(lot => lot.OwnerId == child && lot.DeliveryBuildingId == house); tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var cargo = Assert.Single(world.Society.Inventory.Lots, lot => lot.OwnerId == child && lot.DeliveryBuildingId == house);
        Assert.Equal((kind, 1), (cargo.ItemKind, cargo.Quantity));
        Assert.Equal(8, world.Society.Inventory.GetLot("child-ground-stock").Quantity);
        Assert.Equal(3, world.Society.Inventory.GetLot("child-personal-ballast").Quantity);
        var saved = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var replay = Restore(PrivateWorldRuntimeCodec.Decode(saved), child, new HelpingProvider("safe_idle"));
        provider.Gather = false;
        for (var tick = 0; tick < 35 && !world.ExportState().Events.Any(item => item.Kind == "child_delivered_household"); tick++)
            await StepPair(world, replay);
        Assert.Single(world.ExportState().Events, item => item.Kind == "child_delivered_household");
        Assert.Equal(storedBefore + 1, Stored(world, household, house, kind));
        Assert.Equal(7, Assert.Single(world.Society.Inventory.Reservations, item => item.Id == "child-stock-held").Quantity);
        Assert.Equal(InventoryReservationState.Reserved, Assert.Single(world.Society.Inventory.Reservations,
            item => item.Id == "child-stock-held").State);
        world.Validate();
    }

    [Fact]
    public async Task FullHouseKeepsAnAlreadyCollectedChildDeliveryUntilRoomReturns()
    {
        var (state, child, houseId, source) = await Prepared("food");
        var checkpoint = state.Society.Society;
        var household = checkpoint.GetInhabitant(child).HouseholdId!;
        var inventory = InventoryFixture.AddLot(checkpoint.Inventory, "child-waiting-stock", "wood", household, 4,
            groundPosition: new(source.Position.X, source.Position.Y));
        state = state with { Society = state.Society with { Society = checkpoint with { Inventory = inventory } } };
        using var collecting = Restore(state, child, new HelpingProvider("child_carry:child-waiting-stock"));
        for (var tick = 0; tick < 12 && !collecting.Society.Inventory.Lots.Any(lot => lot.OwnerId == child && lot.DeliveryBuildingId == houseId); tick++)
            Assert.True((await collecting.AdvanceOneTickAsync()).Advanced);
        var cargo = Assert.Single(collecting.Society.Inventory.Lots, lot => lot.OwnerId == child && lot.DeliveryBuildingId == houseId);
        state = collecting.ExportState();
        var house = state.WorldSimulation!.Buildings.Single(item => item.InstanceId == houseId);
        var definition = state.WorldContent!.Buildings.Single(item => item.CanonicalId == house.DefinitionId);
        var room = BuildingStorageRules.Capacity(definition, house)!.Value -
            state.Society.Society.Inventory.Lots.Where(lot => lot.StorageBuildingId == houseId).Sum(lot => lot.Quantity);
        inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "child-full-house-ballast", "stone", household, room,
            storageBuildingId: houseId);
        state = state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } };
        var provider = new HelpingProvider("child_carry:");
        using var blocked = Restore(state, child, provider);
        for (var tick = 0; tick < 8; tick++) Assert.True((await blocked.AdvanceOneTickAsync()).Advanced);
        var retained = blocked.Society.Inventory.GetLot(cargo.Id);
        Assert.Equal((cargo.OwnerId, cargo.Quantity, cargo.DeliveryBuildingId), (retained.OwnerId, retained.Quantity, retained.DeliveryBuildingId));
        Assert.DoesNotContain(blocked.ExportState().Events, item => item.Kind == "child_delivered_household");
        Assert.DoesNotContain(provider.Seen, candidate => candidate.StartsWith("child_carry:", StringComparison.Ordinal) ||
            candidate.StartsWith("child_gather:", StringComparison.Ordinal));
        state = blocked.ExportState();
        inventory = InventoryFixture.Reserve(state.Society.Society.Inventory, "free-child-delivery-room", household,
            "child-full-house-ballast", cargo.Quantity, "test_consumption", blocked.WorldTick + 100);
        inventory = InventoryFixture.ConsumeReservation(inventory, "free-child-delivery-room");
        state = state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } };
        using var resumed = Restore(state, child, new HelpingProvider("child_carry:"));
        using var replay = Restore(state, child, new HelpingProvider("child_carry:"));
        for (var tick = 0; tick < 35 && !resumed.ExportState().Events.Any(item => item.Kind == "child_delivered_household"); tick++)
            await StepPair(resumed, replay);
        Assert.Single(resumed.ExportState().Events, item => item.Kind == "child_delivered_household");
        Assert.Equal(4, Stored(resumed, household, houseId, "wood"));
        Assert.Equal(4, cargo.Quantity);
        resumed.Validate();
    }

    [Theory]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    public async Task HelpingTripsCountMovementStepsAndRefuseTheNinthStep(int steps)
    {
        var (state, child, houseId, _) = await Prepared("food");
        var house = state.WorldSimulation!.Buildings.Single(item => item.InstanceId == houseId);
        var routeMethod = typeof(PrivateWorldRuntime).GetMethod("FindUnoccupiedRoute",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        // Locate a real native route at the boundary; do not add terrain, a
        // House, permission or a delivery. The ninth-step route stays within
        // the home radius so its refusal exercises the separate trip limit.
        using var locating = Restore(state, child, new HelpingProvider("safe_idle"));
        var route = state.Map.Tiles.Where(tile => state.Map.IsPassable(tile.Position) &&
                state.Map.FootDistance(tile.Position, house.Position) <= 8 &&
                !state.Inhabitants.Any(person => person.InhabitantId != child && person.Position == tile.Position))
            .OrderBy(tile => tile.Position.Y).ThenBy(tile => tile.Position.X)
            .Select(tile => (IReadOnlyList<GridPoint>)routeMethod.Invoke(locating,
                [child, tile.Position, house.Position, 0, true, 0])!)
            .FirstOrDefault(path => path.Count == steps + 1 &&
                path.All(point => state.Map.FootDistance(point, house.Position) <= 8));
        Assert.NotNull(route);
        Assert.Equal(house.Position, route[^1]);
        var point = route[0];
        var household = state.Society.Society.GetInhabitant(child).HouseholdId!;
        const string stockId = "000-child-boundary-stock";
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, stockId, "wood", household, 1,
            groundPosition: new(point.X, point.Y));
        state = state with
        {
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == child
                ? person with { Position = point } : person).ToArray(),
        };
        var provider = new HelpingProvider("child_carry:" + stockId);
        using var world = Restore(state, child, provider);
        var storedBefore = Stored(world, household, houseId, "wood");
        for (var tick = 0; tick < 12 && !world.Society.Inventory.Lots.Any(lot =>
                 lot.OwnerId == child && lot.DeliveryBuildingId == houseId); tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        if (steps == 9)
        {
            Assert.DoesNotContain("child_carry:" + stockId, provider.Seen);
            var retained = world.Society.Inventory.GetLot(stockId);
            Assert.Equal((household, 1, new InventoryGroundPosition(point.X, point.Y)),
                (retained.OwnerId, retained.Quantity, retained.GroundPosition));
            Assert.Equal(storedBefore, Stored(world, household, houseId, "wood"));
            Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "child_collected_household" ||
                item.Kind == "child_delivered_household");
            world.Validate();
            var saved = PrivateWorldRuntimeCodec.Encode(world.ExportState());
            using var reloaded = Restore(PrivateWorldRuntimeCodec.Decode(saved), child, new HelpingProvider("safe_idle"));
            Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(reloaded.ExportState()));
            return;
        }
        Assert.Contains("child_carry:" + stockId, provider.Seen);
        var cargo = Assert.Single(world.Society.Inventory.Lots, lot => lot.OwnerId == child && lot.DeliveryBuildingId == houseId);
        Assert.Equal(("wood", 1), (cargo.ItemKind, cargo.Quantity));
        Assert.Equal(storedBefore, Stored(world, household, houseId, "wood"));
        var midTrip = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var replay = Restore(PrivateWorldRuntimeCodec.Decode(midTrip), child, new HelpingProvider("safe_idle"));
        Assert.Equal(midTrip, PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        provider.Gather = false;
        for (var tick = 0; tick < 80 && !world.ExportState().Events.Any(item => item.Kind == "child_delivered_household"); tick++)
            await StepPair(world, replay);
        Assert.Single(world.ExportState().Events, item => item.Kind == "child_delivered_household");
        Assert.Equal(storedBefore + 1, Stored(world, household, houseId, "wood"));
        Assert.Equal(house.Position, world.Inhabitants.Single(person => person.InhabitantId == child).Position);
        Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.OwnerId == child && lot.DeliveryBuildingId == houseId);
        world.Validate();
    }

    [Theory]
    [InlineData("food")]
    [InlineData("wood")]
    [InlineData("carry")]
    public async Task InfantMakesNoHelpingDecisionAndCannotGatherOrCarry(string task)
    {
        var (state, child, house, source) = await Prepared(task == "wood" ? "wood" : "food");
        var checkpoint = state.Society.Society;
        var infantBirth = checkpoint.LifeTickAt(checkpoint.WorldTick);
        checkpoint = checkpoint with
        {
            Inhabitants = checkpoint.Inhabitants.Select(person => person.Id == child
            ? person with
            {
                BirthTick = infantBirth,
                BirthLifeTick = checkpoint.LifeClock is null ? null : infantBirth,
                AgeBand = SocietyAgeBand.Infant,
                LastLifecycleYearChecked = 0
            } : person).ToArray()
        };
        var inventory = InventoryFixture.AddLot(checkpoint.Inventory, "infant-help-stock", "wood",
            checkpoint.GetInhabitant(child).HouseholdId!, 4, groundPosition: new(source.Position.X, source.Position.Y));
        state = state with { Society = state.Society with { Society = checkpoint with { Inventory = inventory } } };
        var wanted = task == "carry" ? "child_carry:infant-help-stock" : "child_gather:" + task + ":" + source.Id;
        var provider = new HelpingProvider(wanted, forge: true);
        using var world = Restore(state, child, provider);
        var ecologyBefore = world.WorldSystems.Ecology.GetResource(source.Id).Quantity;
        for (var tick = 0; tick < 8; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Empty(provider.Seen);
        Assert.Equal(ecologyBefore, world.WorldSystems.Ecology.GetResource(source.Id).Quantity);
        Assert.Equal(4, world.Society.Inventory.GetLot("infant-help-stock").Quantity);
        Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.OwnerId == child && lot.DeliveryBuildingId == house);
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "child_collected_household" ||
            item.Kind == "child_delivered_household");
        world.Validate();
    }

    [Fact]
    public async Task ForgedChildChoiceCannotTakeForeignReservedHeavyOrDistantStock()
    {
        var (state, child, house, source) = await Prepared("food");
        var checkpoint = state.Society.Society;
        var household = checkpoint.GetInhabitant(child).HouseholdId!;
        var foreign = checkpoint.Households.First(item => item.Id != household).Id;
        var far = state.Map.Tiles.First(tile => state.Map.IsPassable(tile.Position) &&
            state.Map.FootDistance(tile.Position, source.Position) > 16).Position;
        var inventory = checkpoint.Inventory;
        foreach (var (id, kind, owner, point) in new[]
        {
            ("foreign", "wood", foreign, source.Position),
            ("reserved", "wood", household, source.Position),
            ("heavy", "iron_ore", household, source.Position),
            ("distant", "wood", household, far),
        })
            inventory = InventoryFixture.AddLot(inventory, "child-denied-" + id, kind, owner, 4,
                groundPosition: new(point.X, point.Y));
        inventory = InventoryFixture.Reserve(inventory, "child-denied-hold", household, "child-denied-reserved", 4,
            "other_work", checkpoint.WorldTick + 100);
        state = state with { Society = state.Society with { Society = checkpoint with { Inventory = inventory } } };
        var provider = new HelpingProvider("child_carry:child-denied-foreign", forge: true);
        using var world = Restore(state, child, provider);
        var admissions = new List<SocietyCognitionDispatchResult>();
        for (var tick = 0; tick < 8; tick++)
            admissions.AddRange((await world.AdvanceOneTickAsync()).Decisions.Where(item => item.InhabitantId == child));
        Assert.NotEmpty(admissions);
        Assert.All(admissions, decision =>
        {
            Assert.True(decision.Admission.FellBack);
            Assert.Equal("candidate_not_legal", decision.Admission.Outcome);
            Assert.Equal("safe_idle", decision.Admission.Intention?.CandidateId);
        });
        Assert.DoesNotContain(provider.Seen, candidate => candidate.StartsWith("child_carry:child-denied-", StringComparison.Ordinal));
        foreach (var lot in inventory.Lots.Where(lot => lot.Id.StartsWith("child-denied-", StringComparison.Ordinal)))
        {
            var retained = world.Society.Inventory.GetLot(lot.Id);
            Assert.Equal((lot.OwnerId, lot.Quantity, lot.GroundPosition), (retained.OwnerId, retained.Quantity, retained.GroundPosition));
        }
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "child_collected_household");
        Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.OwnerId == child && lot.DeliveryBuildingId == house);

        // Existing carried goods count even when ordinary adult carrying space remains.
        inventory = InventoryFixture.AddLot(inventory, "child-full-load", "stone", child, 4);
        inventory = InventoryFixture.AddLot(inventory, "child-nearby-allowed", "wood", household, 4,
            groundPosition: new(source.Position.X, source.Position.Y));
        state = state with { Society = state.Society with { Society = checkpoint with { Inventory = inventory } } };
        var fullProvider = new HelpingProvider("child_carry:child-nearby-allowed", forge: true);
        using var full = Restore(state, child, fullProvider);
        for (var tick = 0; tick < 8; tick++) Assert.True((await full.AdvanceOneTickAsync()).Advanced);
        Assert.NotEmpty(fullProvider.Seen);
        Assert.DoesNotContain(fullProvider.Seen, candidate => candidate.StartsWith("child_carry:", StringComparison.Ordinal) ||
            candidate.StartsWith("child_gather:", StringComparison.Ordinal));
        Assert.Equal(4, full.Society.Inventory.GetLot("child-nearby-allowed").Quantity);
        Assert.Equal(4, full.Society.Inventory.GetLot("child-full-load").Quantity);
        full.Validate();
    }

    private static async Task<(PrivateWorldRuntimeState State, string Child, string House, MapResource Source)> Prepared(string kind)
    {
        var state = PrivateWorldRuntimeCodec.Decode(await Generated.Value);
        var choices = state.WorldSimulation!.Buildings.Where(building => building.HouseholdId is not null &&
                building.InstanceId.StartsWith("first-town-house-", StringComparison.Ordinal))
            .SelectMany(house => state.Map.Resources.Where(source => source.TreeKind is null &&
                (kind == "food" ? source.Kind is "food" or "fruit" : source.Kind == "wood" || source.NaturalObjectKind == "fallen_wood") &&
                state.Map.FootDistance(house.Position, source.Position) is > 0 and <= 8)
                .Select(source => (House: house, Source: source)))
            .OrderBy(item => state.Map.FootDistance(item.House.Position, item.Source.Position)).ToArray();
        Assert.True(choices.Length > 0, string.Join("\n", state.Map.Resources.Where(source => source.Id.StartsWith("settlement-", StringComparison.Ordinal)).Select(source => $"{source.Id} {source.Kind} {source.Position}")));
        var (house, source) = choices[0];
        var checkpoint = state.Society.Society;
        var household = checkpoint.Households.Single(item => item.Id == house.HouseholdId);
        var parents = household.MemberIds.Take(2).ToArray();
        checkpoint = SocietyFixture.ProposeRelationship(checkpoint, new("help-partnership", 1,
            SocietyRelationshipType.Partnership, parents[0], parents[1], checkpoint.WorldTick)).Checkpoint;
        checkpoint = SocietyFixture.AcceptRelationship(checkpoint, "help-partnership", 1, parents[1]).Checkpoint;
        checkpoint = ChosenBirthNameTestFixture.NameParent(checkpoint, parents[0]);
        checkpoint = checkpoint with { Inventory = InventoryFixture.AddLot(checkpoint.Inventory, "help-birth-food", "food", household.Id, 4) };
        var birth = SocietyFixture.CommitBirth(checkpoint, new($"family:{parents[0]}:{checkpoint.WorldTick}", 1,
            parents[0], parents[1], household.Id, household.MemberIds, parents, "help-birth-food", 4,
            checkpoint.WorldTick, ChildName: ChosenBirthNameTestFixture.ChildName(checkpoint, parents[0], "Helper"),
            PrimaryCaregiverId: parents[0]));
        var child = Assert.IsType<string>(birth.CreatedId);
        checkpoint = birth.Checkpoint;
        var age = Assert.IsType<SocietyDayLifecycle>(checkpoint.Config.DayLifecycle).ChildStartDay;
        var born = checkpoint.LifeTickAt(checkpoint.WorldTick) - age * checkpoint.Config.TicksPerLifecycleAge;
        checkpoint = checkpoint with
        {
            Inhabitants = checkpoint.Inhabitants.Select(person => person.Id == child
            ? person with
            {
                BirthTick = born,
                BirthLifeTick = checkpoint.LifeClock is null ? null : born,
                AgeBand = SocietyAgeBand.Child,
                LastLifecycleYearChecked = age
            } : person).ToArray()
        };
        state = state with
        {
            JevEnabled = false,
            RoutineHelper = RoutineHelperSettings.Off,
            Society = state.Society with { Society = checkpoint },
            Inhabitants = state.Inhabitants.Append(new(child, source.Position, 10_000, 0, "curious", "help at home")).ToArray(),
            Towns = state.Towns!.Select(town => town.ResidentIds.Contains(parents[0]) ? town with
            { ResidentIds = town.ResidentIds.Append(child).Order(StringComparer.Ordinal).ToArray() } : town).ToArray(),
        };
        return (state, child, house.InstanceId, source);
    }

    private static int Stored(PrivateWorldRuntime world, string household, string house, string kind) =>
        world.Society.Inventory.Lots.Where(lot => lot.OwnerId == household && lot.StorageBuildingId == house && lot.ItemKind == kind)
            .Sum(lot => lot.Quantity);

    private static PrivateWorldRuntime Restore(PrivateWorldRuntimeState state, string child, HelpingProvider provider) =>
        PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)),
            id => id == child ? provider : new ActionCoverageRecorder(chooseIdle: true));

    private static async Task StepPair(PrivateWorldRuntime world, PrivateWorldRuntime replay)
    {
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
    }

    private sealed class HelpingProvider(string wanted, bool forge = false) : IDecisionProvider
    {
        public List<string> Seen { get; } = [];
        public List<string> Chosen { get; } = [];
        public bool Gather { get; set; } = true;
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            Seen.AddRange(request.Observation.Candidates.Select(item => item.Id));
            var delivery = request.Observation.Candidates.FirstOrDefault(item => item.Id.StartsWith("child_carry:", StringComparison.Ordinal) &&
                request.Observation.Candidates.All(candidate => !candidate.Id.StartsWith("child_gather:", StringComparison.Ordinal)));
            var selected = forge ? wanted : delivery?.Id ?? (Gather ? request.Observation.Candidates.FirstOrDefault(item =>
                item.Id.StartsWith(wanted, StringComparison.Ordinal))?.Id : null) ?? "safe_idle";
            Chosen.Add(selected);
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, request.Observation.InhabitantId,
                Kind, ProviderEpoch, request.Observation.RunEpoch, request.Observation.DecisionGeneration,
                request.Observation.ObservationDigest, selected, 1, request.Observation.Candidates.ToDictionary(item => item.Id,
                    item => item.Id == selected ? 1d : 0d, StringComparer.Ordinal)));
        }
    }
}

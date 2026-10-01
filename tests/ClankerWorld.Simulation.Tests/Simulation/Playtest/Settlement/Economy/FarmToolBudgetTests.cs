using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed class FarmToolBudgetTests
{
    [Fact]
    public async Task PaidHoeRepairEnablesTheNextActualCropCycleAcrossReload()
    {
        var (state, actor, household, point) = FarmFieldTests.PreparedFarmer("field-paid-replant-repair");
        state = FarmFieldTests.FeedHouseholdFromAvailableStock(state, household);
        state = WithOwnedSmith(state, household, point);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "paid-repair-wood", "wood", household, 2,
            storageBuildingId: "repair-smith");
        inventory = InventoryFixture.AddLot(inventory, "paid-repair-seed", "grain_seed", actor, 1);
        using var setup = FarmFieldTests.Restore(FarmFieldTests.WithInventory(state, inventory));
        Assert.True(setup.StartFieldWork(actor, point, FarmWorkKind.Till).Accepted);
        await Advance(setup, 8);
        Assert.True(setup.StartFieldWork(actor, point, FarmWorkKind.Plant, FarmFieldRules.Grain, "paid-repair-seed").Accepted);
        await Advance(setup, 5);
        Assert.True(setup.StartFieldWork(actor, point, FarmWorkKind.Tend).Accepted);
        await Advance(setup, 4);
        Assert.Equal(2_000, setup.Society.Inventory.GetLot("carried-hoe").ConditionBasisPoints);
        var provider = new RepairCycleProvider(actor, point);
        using var first = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(setup.ExportState())), _ => provider);
        for (var tick = 0; tick < 60 && first.Society.Inventory.GetLot("carried-hoe").ConditionBasisPoints != 7_000; tick++)
            Assert.True((await first.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(7_000, first.Society.Inventory.GetLot("carried-hoe").ConditionBasisPoints);
        Assert.Equal(1, first.Society.Inventory.GetLot("paid-repair-wood").Quantity);
        for (var tick = 0; tick < first.WorldSystems.Config.TicksPerDay && !first.Fields.Any(field => field.Cycle == 1); tick++)
            Assert.True((await first.AdvanceOneTickAsync()).Advanced);
        var harvest = Assert.Single(first.Fields);
        Assert.Equal(1, harvest.Cycle);
        Assert.Equal(5_000, first.Society.Inventory.GetLot("carried-hoe").ConditionBasisPoints);
        var plantingReserve = first.Society.Inventory.GetReservation(harvest.ReplantingReservationId!);
        Assert.Equal(1, plantingReserve.Quantity);
        Assert.Equal(InventoryReservationState.Reserved, plantingReserve.State);
        var harvestedSeed = first.Society.Inventory.GetLot(plantingReserve.LotId);
        Assert.Equal("grain_seed", harvestedSeed.ItemKind);
        Assert.Equal(household, harvestedSeed.OwnerId);
        Assert.Equal(new InventoryGroundPosition(point.X, point.Y), harvestedSeed.GroundPosition);
        var saved = FreshChoices(first.ExportState(), actor);
        var observer = new ActionCoverageRecorder(chooseIdle: true);
        using (var observed = PrivateWorldRuntime.Restore(saved, _ => observer))
        {
            Assert.True((await observed.AdvanceOneTickAsync()).Advanced);
            Assert.DoesNotContain(observer.OfferedByAgent[actor].Keys, candidate => candidate.StartsWith("farm:Plant:", StringComparison.Ordinal));
            Assert.Contains("repair_tool:carried-hoe", observer.OfferedByAgent[actor].Keys);
            Assert.Equal(5_000, observed.Society.Inventory.GetLot("carried-hoe").ConditionBasisPoints);
            Assert.Equal(1, observed.Society.Inventory.GetLot("paid-repair-wood").Quantity);
        }
        var bytes = PrivateWorldRuntimeCodec.Encode(saved);
        using var second = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), _ => new RepairCycleProvider(actor, point));
        for (var tick = 0; tick < 60 && second.Society.Inventory.GetLot("carried-hoe").ConditionBasisPoints != 10_000; tick++)
            Assert.True((await second.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(10_000, second.Society.Inventory.GetLot("carried-hoe").ConditionBasisPoints);
        Assert.DoesNotContain(second.Society.Inventory.Lots, lot => lot.Id == "paid-repair-wood");
        var repairs = second.Society.Inventory.Reservations.Where(reservation => reservation.Purpose == "tool_repair").ToArray();
        Assert.Equal(2, repairs.Length);
        Assert.All(repairs, reservation =>
        {
            Assert.Equal(household, reservation.OwnerId);
            Assert.Equal("paid-repair-wood", reservation.LotId);
            Assert.Equal(1, reservation.Quantity);
            Assert.Equal(InventoryReservationState.Completed, reservation.State);
        });
        string? replantedSeedId = null;
        for (var tick = 0; tick < second.WorldSystems.Config.TicksPerDay && !second.Fields.Any(field => field.Cycle == 2); tick++)
        {
            Assert.True((await second.AdvanceOneTickAsync()).Advanced);
            replantedSeedId ??= second.Society.Inventory.Lots.FirstOrDefault(lot => lot.OwnerId == actor &&
                lot.ProvenanceLotId == harvestedSeed.Id)?.Id;
        }
        var repeated = Assert.Single(second.Fields);
        Assert.Equal(2, repeated.Cycle);
        Assert.Equal(4_000, second.Society.Inventory.GetLot("carried-hoe").ConditionBasisPoints);
        Assert.Equal(InventoryReservationState.Released, second.Society.Inventory.GetReservation(plantingReserve.Id).State);
        Assert.NotNull(replantedSeedId);
        Assert.Contains(second.Society.Inventory.Reservations, reservation => reservation.Purpose == "field_planting" &&
            reservation.LotId == replantedSeedId && reservation.State == InventoryReservationState.Completed);
        var crop = second.Society.Inventory.GetLot(FarmFieldRules.FieldId(point) + ":harvest:2:crop");
        Assert.Equal(FarmFieldRules.Grain, crop.ItemKind);
        Assert.Equal(household, crop.OwnerId);
        Assert.Equal(new InventoryGroundPosition(point.X, point.Y), crop.GroundPosition);
        var nextReserve = second.Society.Inventory.GetReservation(repeated.ReplantingReservationId!);
        Assert.Equal(1, nextReserve.Quantity);
        Assert.Equal(InventoryReservationState.Reserved, nextReserve.State);
        var finalBytes = PrivateWorldRuntimeCodec.Encode(second.ExportState());
        using var reloaded = FarmFieldTests.Restore(PrivateWorldRuntimeCodec.Decode(finalBytes));
        Assert.Equal(finalBytes, PrivateWorldRuntimeCodec.Encode(reloaded.ExportState()));
    }

    [Theory]
    [InlineData(5_000, true, true, true, true, true)]
    [InlineData(6_000, true, true, true, true, false)]
    [InlineData(5_000, false, true, true, true, false)]
    [InlineData(5_000, true, false, true, true, false)]
    [InlineData(5_000, true, true, false, true, false)]
    [InlineData(5_000, true, true, true, false, false)]
    public async Task EarlyHoeRepairNeedsActualPlantingDemandAndOwnedOnSiteMaterials(int condition, bool seed,
        bool foodShortage, bool materialOnSite, bool ownedSmith, bool offered)
    {
        var (state, actor, household, point) = FarmFieldTests.PreparedFarmer("field-early-repair-boundary");
        if (foodShortage) state = FarmFieldTests.FeedHouseholdFromAvailableStock(state, household);
        // Prepare the real field before placing the Smith: its generated Road
        // must respect this actual field instead of occupying a future fixture tile.
        using (var preparing = FarmFieldTests.Restore(state))
        {
            Assert.True(preparing.StartFieldWork(actor, point, FarmWorkKind.Till).Accepted);
            await Advance(preparing, 8);
            Assert.Equal(FarmFieldStage.Prepared, Assert.Single(preparing.Fields).Stage);
            state = preparing.ExportState();
        }
        if (ownedSmith) state = WithOwnedSmith(state, household, point);
        var inventory = InventoryFixture.ChangeCondition(state.Society.Society.Inventory, "carried-hoe", actor,
            condition - state.Society.Society.Inventory.GetLot("carried-hoe").ConditionBasisPoints, "fixture-replant-repair");
        if (!seed) inventory = inventory with
        {
            Lots = inventory.Lots.Where(lot => lot.OwnerId != household ||
                !new[] { "grain_seed", "cultivated_green_seed", "potatoes" }.Contains(lot.ItemKind, StringComparer.Ordinal)).ToArray(),
        };
        inventory = InventoryFixture.AddLot(inventory, "boundary-repair-wood", "wood", household, 1,
            storageBuildingId: materialOnSite && ownedSmith ? "repair-smith" : "first-town-house-a");
        state = FreshChoices(FarmFieldTests.WithInventory(state, inventory), actor);
        var recorder = new ActionCoverageRecorder(chooseIdle: true);
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)), _ => recorder);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(offered, recorder.OfferedByAgent[actor].Keys.Contains("repair_tool:carried-hoe", StringComparer.Ordinal));
        Assert.Equal(condition, world.Society.Inventory.GetLot("carried-hoe").ConditionBasisPoints);
        Assert.Equal(1, world.Society.Inventory.GetLot("boundary-repair-wood").Quantity);
        Assert.DoesNotContain(world.Society.Inventory.Reservations, reservation => reservation.Purpose == "tool_repair");
    }

    [Fact]
    public async Task RemainingWoodenHoeStrokesReachTheRealHarvestAcrossReloadInsteadOfStartingAnotherField()
    {
        var (state, actor, household, point) = FarmFieldTests.PreparedFarmer("field-tool-budget");
        state = FarmFieldTests.FeedHouseholdFromAvailableStock(state, household);
        state = FarmFieldTests.WithInventory(state, InventoryFixture.AddLot(state.Society.Society.Inventory,
            "budget-planting", "cultivated_green_seed", actor, 1));
        using var setup = FarmFieldTests.Restore(state);
        Assert.True(setup.StartFieldWork(actor, point, FarmWorkKind.Till).Accepted);
        await Advance(setup, 8);
        Assert.Equal(6_000, setup.Society.Inventory.GetLot("carried-hoe").ConditionBasisPoints);
        Assert.True(setup.StartFieldWork(actor, point, FarmWorkKind.Plant, FarmFieldRules.Greens, "budget-planting").Accepted);
        await Advance(setup, 5);
        Assert.True(setup.StartFieldWork(actor, point, FarmWorkKind.Tend).Accepted);
        await Advance(setup, 4);
        var growing = Assert.Single(setup.Fields);
        Assert.True(growing.Tended);
        Assert.Equal(FarmFieldStage.Growing, growing.Stage);
        Assert.Equal(2_000, setup.Society.Inventory.GetLot("carried-hoe").ConditionBasisPoints);

        var bytes = PrivateWorldRuntimeCodec.Encode(setup.ExportState());
        var provider = new FarmOnlyProvider(actor);
        using var working = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), _ => provider);
        for (var tick = 0; tick <= growing.ReadyTick - setup.WorldTick + 5 && !working.Fields.Any(field => field.Cycle > 0); tick++)
            Assert.True((await working.AdvanceOneTickAsync()).Advanced);
        var harvested = Assert.Single(working.Fields);
        Assert.Equal(1, harvested.Cycle);
        Assert.Equal(FarmFieldStage.Harvested, harvested.Stage);
        Assert.Equal(0, working.Society.Inventory.GetLot("carried-hoe").ConditionBasisPoints);
        Assert.DoesNotContain(provider.BeforeHarvest, candidate => candidate.StartsWith("farm:Till:", StringComparison.Ordinal));
        Assert.Contains(working.ExportState().Events, item => item.Kind == "field_harvested");
        var crop = Assert.Single(working.Society.Inventory.Lots, lot => lot.Id == FarmFieldRules.FieldId(point) + ":harvest:1:crop");
        Assert.Equal(household, crop.OwnerId);
        Assert.Equal(FarmFieldRules.Greens, crop.ItemKind);
        Assert.Equal(new InventoryGroundPosition(point.X, point.Y), crop.GroundPosition);
        Assert.True(crop.Quantity > 0);
        var finalBytes = PrivateWorldRuntimeCodec.Encode(working.ExportState());
        using var reloaded = FarmFieldTests.Restore(PrivateWorldRuntimeCodec.Decode(finalBytes));
        Assert.Equal(finalBytes, PrivateWorldRuntimeCodec.Encode(reloaded.ExportState()));
    }

    [Theory]
    [InlineData("wooden_hoe", 2_000, false, false)]
    [InlineData("wooden_hoe", 6_000, false, true)]
    [InlineData("iron_hoe", 2_000, false, true)]
    [InlineData("wooden_hoe", 4_000, true, true)]
    public async Task AnotherFieldNeedsEnoughActualCarriedToolWorkAfterTheGrowingCrop(string hoeKind, int condition,
        bool sickle, bool offered)
    {
        var (state, actor, household, point) = FarmFieldTests.PreparedFarmer("field-tool-budget-control");
        state = FarmFieldTests.FeedHouseholdFromAvailableStock(state, household);
        var inventory = state.Society.Society.Inventory;
        inventory = inventory with
        {
            Lots = inventory.Lots.Select(lot => lot.Id == "carried-hoe"
                ? lot with { ItemKind = hoeKind, ConditionBasisPoints = condition } : lot).ToArray(),
        };
        if (sickle) inventory = InventoryFixture.AddLot(inventory, "budget-sickle", "sickle", actor, 1);
        state = FarmFieldTests.WithInventory(state, inventory) with
        {
            Fields = [new(point, household, FarmFieldStage.Growing, FarmFieldRules.Greens, ReadyTick: 100, Tended: true)],
        };
        var recorder = new ActionCoverageRecorder(chooseIdle: true);
        using var world = PrivateWorldRuntime.Restore(state, _ => recorder);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(offered, recorder.OfferedByAgent[actor].Keys.Any(candidate => candidate.StartsWith("farm:Till:", StringComparison.Ordinal)));
        Assert.Equal(condition, world.Society.Inventory.GetLot("carried-hoe").ConditionBasisPoints);
        Assert.Equal(FarmFieldStage.Growing, Assert.Single(world.Fields).Stage);
        if (hoeKind == "wooden_hoe" && condition == 6_000)
        {
            var choices = new FarmOnlyProvider(actor);
            using var preparing = PrivateWorldRuntime.Restore(state, _ => choices);
            for (var tick = 0; tick < 20 && !preparing.Fields.Any(field => field.Position != point && field.Stage == FarmFieldStage.Prepared); tick++)
                Assert.True((await preparing.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(FarmFieldStage.Prepared, Assert.Single(preparing.Fields, field => field.Position != point).Stage);
            Assert.Equal(2_000, preparing.Society.Inventory.GetLot("carried-hoe").ConditionBasisPoints);
            Assert.True((await preparing.AdvanceOneTickAsync()).Advanced);
            Assert.DoesNotContain(choices.BeforeHarvest, candidate => candidate.StartsWith("farm:Plant:", StringComparison.Ordinal));
            Assert.Equal(FarmFieldStage.Growing, preparing.Fields.Single(field => field.Position == point).Stage);
        }
    }

    [Theory]
    [InlineData(FarmFieldStage.Prepared, 4_000, false)]
    [InlineData(FarmFieldStage.Prepared, 6_000, true)]
    [InlineData(FarmFieldStage.Harvested, 4_000, false)]
    [InlineData(FarmFieldStage.Harvested, 6_000, true)]
    public async Task PlantingNeedsEnoughHoeWorkForTendingAndHarvestInsteadOfBeatingRepair(FarmFieldStage stage,
        int condition, bool offered)
    {
        var (state, actor, household, point) = FarmFieldTests.PreparedFarmer("field-replant-tool-budget");
        state = FarmFieldTests.FeedHouseholdFromAvailableStock(state, household);
        var inventory = state.Society.Society.Inventory;
        inventory = inventory with
        {
            Lots = inventory.Lots.Select(lot => lot.Id == "carried-hoe"
                ? lot with { ConditionBasisPoints = condition } : lot).ToArray(),
        };
        using (var clock = FarmFieldTests.Restore(FarmFieldTests.WithInventory(state, inventory)))
        {
            Assert.True((await clock.AdvanceOneTickAsync()).Advanced);
            state = clock.ExportState();
        }
        state = state with
        {
            Fields = [new(point, household, stage, stage == FarmFieldStage.Harvested ? FarmFieldRules.Greens : null,
                ReadyTick: stage == FarmFieldStage.Harvested ? 1 : 0, Tended: stage == FarmFieldStage.Harvested,
                Cycle: stage == FarmFieldStage.Harvested ? 1 : 0)],
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { LastDecisionContext = null } : person).ToArray(),
        };
        var recorder = new ActionCoverageRecorder(chooseIdle: true);
        using var world = PrivateWorldRuntime.Restore(state, _ => recorder);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(offered, recorder.OfferedByAgent[actor].Keys.Any(candidate => candidate.StartsWith("farm:Plant:", StringComparison.Ordinal)));
        Assert.Equal(condition, world.Society.Inventory.GetLot("carried-hoe").ConditionBasisPoints);
        var next = world.ExportState();
        // Ask for a fresh ordinary observation on both sides of the save boundary.
        next = next with
        {
            Inhabitants = next.Inhabitants.Select(person => person.InhabitantId == actor
            ? person with { LastDecisionContext = null } : person).ToArray()
        };
        var bytes = PrivateWorldRuntimeCodec.Encode(next);
        var replay = new ActionCoverageRecorder(chooseIdle: true);
        using var reloaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), _ => replay);
        Assert.True((await reloaded.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(offered, replay.OfferedByAgent[actor].Keys.Any(candidate => candidate.StartsWith("farm:Plant:", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData(7_500, false)]
    [InlineData(8_000, true)]
    public async Task OneLastSickleStrokeCannotCoverTwoDifferentHarvestJobs(int hoeCondition, bool offered)
    {
        var (state, actor, household, _) = FarmFieldTests.PreparedFarmer("field-partial-sickle-budget");
        state = FarmFieldTests.FeedHouseholdFromAvailableStock(state, household);
        using (var clock = FarmFieldTests.Restore(state))
        {
            await Advance(clock, 1);
            state = clock.ExportState();
        }
        var farmhouse = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == "first-town-farmhouse");
        var occupied = state.Map.Resources.Select(resource => resource.Position).Concat(state.Map.CampObjects.Select(item => item.Position))
            .Concat(state.RoadTiles!).Concat(state.WorldSimulation.Buildings.SelectMany(building =>
                WorldContentSimulationRules.Footprint(state.WorldContent!.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId), building)))
            .ToHashSet();
        var fertility = new LandFertility(state.Map, state.WorldSeed);
        var town = state.Towns!.Single(item => item.Id == farmhouse.TownId);
        var points = state.Map.Tiles.Select(tile => tile.Position).Where(point => fertility.CanFarm(point) &&
                !occupied.Contains(point) && TownBorderRules.IsWithinOrAdjacent(town, point, 1, 1) &&
                !state.Inhabitants.Any(person => person.Position == point))
            .OrderBy(point => state.Map.FootDistance(farmhouse.Position, point)).Take(3)
            .OrderBy(point => point.Y).ThenBy(point => point.X).ToArray();
        Assert.Equal(3, points.Length);
        var other = state.Society.Society.Inhabitants.First(person => person.HouseholdId == household && person.Id != actor).Id;
        var inventory = InventoryFixture.ChangeCondition(state.Society.Society.Inventory, "carried-hoe", actor,
            hoeCondition - 10_000, "fixture-farm-budget");
        inventory = InventoryFixture.AddLot(inventory, "last-stroke-sickle", "sickle", actor, 1, conditionBasisPoints: 125);
        inventory = InventoryFixture.AddLot(inventory, "other-worker-hoe", "wooden_hoe", other, 1);
        state = FarmFieldTests.WithInventory(state, inventory) with
        {
            Fields = [new(points[0], household, FarmFieldStage.Ready, FarmFieldRules.Grain, ReadyTick: 1, Tended: true,
                    Work: new(other, FarmWorkKind.Harvest, 1, state.Society.Society.WorldTick)),
                new(points[1], household, FarmFieldStage.Ready, FarmFieldRules.Grain, ReadyTick: 1, Tended: true),
                new(points[2], household, FarmFieldStage.Prepared)],
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { Position = points[2], LastDecisionContext = null }
                : person.InhabitantId == other ? person with { Position = points[0] } : person).ToArray(),
        };
        var recorder = new ActionCoverageRecorder(chooseIdle: true);
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)), _ => recorder);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(offered, recorder.OfferedByAgent[actor].Keys.Any(candidate =>
            candidate.StartsWith($"farm:Plant:{points[2].X}:{points[2].Y}:", StringComparison.Ordinal)));
        Assert.Equal(125, world.Society.Inventory.GetLot("last-stroke-sickle").ConditionBasisPoints);
        Assert.Equal(hoeCondition, world.Society.Inventory.GetLot("carried-hoe").ConditionBasisPoints);
    }

    private static async Task Advance(PrivateWorldRuntime world, int count)
    {
        for (var tick = 0; tick < count; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
    }

    private static PrivateWorldRuntimeState WithOwnedSmith(PrivateWorldRuntimeState state, string household, GridPoint field)
    {
        var house = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == "first-town-house-a");
        var smith = state.WorldContent!.Buildings.Single(building => building.LocalId == "blacksmith-1x2");
        var inventory = state.Society.Society.Inventory;
        foreach (var cost in smith.BuildCosts)
            inventory = InventoryFixture.AddLot(inventory, "smith-build:" + cost.ResourceId, cost.ResourceId, household,
                cost.Amount, storageBuildingId: house.InstanceId);
        using var building = FarmFieldTests.Restore(FarmFieldTests.WithInventory(state, inventory));
        var farmhouse = building.WorldSimulation.Buildings.Single(item => item.InstanceId == "first-town-farmhouse");
        Assert.Contains(Enumerable.Range(-3, 7).SelectMany(dy => Enumerable.Range(-3, 7)
                .Select(dx => new GridPoint(farmhouse.Position.X + dx, farmhouse.Position.Y + dy))),
            site => !WorldContentSimulationRules.Footprint(smith, site).Contains(field) &&
                building.PlaceBuilding("repair-smith", smith.CanonicalId, site, household).Applied);
        return building.ExportState();
    }

    private static PrivateWorldRuntimeState FreshChoices(PrivateWorldRuntimeState state, string actor) => state with
    {
        Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
            ? person with { LastDecisionContext = null } : person).ToArray(),
        Society = state.Society with
        {
            Cognition = state.Society.Cognition with
            {
                Runtimes = state.Society.Cognition.Runtimes.Select(runtime => runtime.InhabitantId == actor
                    ? runtime with { CurrentIntention = null } : runtime).ToArray(),
            },
        },
    };

    private sealed class RepairCycleProvider(string actor, GridPoint point) : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var candidates = request.Observation.InhabitantId == actor
                ? request.Observation.Candidates.Where(candidate => candidate.Id.StartsWith("repair_tool:", StringComparison.Ordinal) ||
                    candidate.Id.StartsWith($"farm:Plant:{point.X}:{point.Y}:grain", StringComparison.Ordinal) ||
                    candidate.Id == $"farm:Tend:{point.X}:{point.Y}:-" || candidate.Id == $"farm:Harvest:{point.X}:{point.Y}:-" ||
                    candidate.Id == "safe_idle").ToArray()
                : [request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle")];
            return new DeterministicDecisionProvider().DecideAsync(request with
            {
                Observation = request.Observation with { Candidates = candidates },
            }, cancellationToken);
        }
    }

    private sealed class FarmOnlyProvider(string actor) : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public HashSet<string> BeforeHarvest { get; } = new(StringComparer.Ordinal);
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var candidates = request.Observation.InhabitantId == actor
                ? request.Observation.Candidates.Where(candidate => candidate.Id.StartsWith("farm:", StringComparison.Ordinal) || candidate.Id == "safe_idle").ToArray()
                : [request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle")];
            if (request.Observation.InhabitantId == actor)
                foreach (var candidate in candidates) BeforeHarvest.Add(candidate.Id);
            return new DeterministicDecisionProvider().DecideAsync(request with
            {
                Observation = request.Observation with { Candidates = candidates },
            }, cancellationToken);
        }
    }
}

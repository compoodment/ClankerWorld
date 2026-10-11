using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Viewer.Observation;
using System.Collections.Concurrent;

namespace ClankerWorld.Simulation.Tests;

public sealed class FarmFieldTests
{
    private static readonly (string Crop, string SeedKind)[] SeedCrops =
        [("grain", "grain_seed"), ("cultivated_greens", "cultivated_green_seed")];

    [Fact]
    public async Task OrdinaryChooserExpandsAndReplantsGeneratedFieldsThroughARealFoodShortageAndReload()
    {
        var (state, _, household, _) = PreparedFarmer("field-normal-cycle");
        // The Town has eaten its starter rations. Keep all real planting
        // stock and let ordinary choices arrange every field operation.
        state = FeedFarmTownFromAvailableStock(state, household);
        // Feeding four Town residents requires more field work than feeding
        // this household alone. Supply a physical spare for normal pickup;
        // the original hoes keep their normal wear and can break.
        state = WithInventory(state, InventoryFixture.AddLot(state.Society.Society.Inventory,
            "replanting-spare-hoe", "wooden_hoe", household, 1, storageBuildingId: "first-town-farmhouse"));
        var initialSeeds = SeedCrops.ToDictionary(item => item.SeedKind,
            item => state.Society.Society.Inventory.Lots.Where(lot => lot.ItemKind == item.SeedKind).Sum(lot => lot.Quantity));
        using var first = PrivateWorldRuntime.Restore(state, _ => new DeterministicDecisionProvider());
        for (var tick = 0; tick < 1_800 && !first.Fields.Any(field => field.HouseholdId == household && field.Cycle > 0); tick++)
            Assert.True((await first.AdvanceOneTickAsync()).Advanced);
        Assert.Contains(first.Fields, field => field.HouseholdId == household && field.Cycle > 0);
        Assert.Contains(first.ExportState().Events, item => item.Kind == "field_prepared");
        Assert.Contains(first.ExportState().Events, item => item.Kind == "field_tended");
        // Eating the available produce creates a real inventory shortage. The
        // separately reserved planting stock must remain available for recovery.
        state = FeedFarmTownFromAvailableStock(first.ExportState(), household);
        var second = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)),
            _ => new DeterministicDecisionProvider());
        try
        {
            for (var tick = 0; tick < 1_800 && !second.Fields.Any(field => field.HouseholdId == household && field.Cycle >= 2); tick++)
            {
                var step = await second.AdvanceOneTickAsync();
                Assert.True(step.Advanced);
                if (!step.Events.Any(item => item.Kind == "field_harvested") ||
                    second.Fields.Any(field => field.HouseholdId == household && field.Cycle >= 2)) continue;
                // Other fields may finish while this one is being replanted.
                // Keep the demand scenario going by accounting for that produce
                // too; leave every reserved planting unit intact.
                state = FeedFarmTownFromAvailableStock(second.ExportState(), household);
                second.Dispose();
                second = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)),
                    _ => new DeterministicDecisionProvider());
            }
            Assert.True(second.Fields.Any(field => field.HouseholdId == household && field.Cycle >= 2), string.Join("\n",
                second.Fields.Select(field => $"{field.Position} {field.Crop} {field.Stage} cycle {field.Cycle}")
                    .Concat(second.ExportState().Events.TakeLast(30).Select(item => $"{item.Kind}: {item.Detail}"))));
            var repeated = second.Fields.First(field => field.HouseholdId == household && field.Cycle >= 2);
            Assert.Equal(FarmFieldStage.Harvested, repeated.Stage);
            Assert.NotNull(repeated.ReplantingReservationId);
            Assert.Contains(second.Society.Inventory.Reservations, reservation => reservation.Id == repeated.ReplantingReservationId &&
                reservation.Quantity == 1 && reservation.State == InventoryReservationState.Reserved);
            var events = second.ExportState().Events;
            Assert.True(events.Count(item => item.Kind == "field_planted") >= 2);
            foreach (var (crop, seedKind) in SeedCrops)
            {
                var planted = events.Count(item => item.Kind == "field_planted" && item.Detail.EndsWith(":" + crop, StringComparison.Ordinal));
                var harvested = events.Count(item => item.Kind == "field_harvested" && item.Detail.EndsWith(":" + crop, StringComparison.Ordinal));
                Assert.Equal(initialSeeds[seedKind] + harvested * 2 - planted,
                    second.Society.Inventory.Lots.Where(lot => lot.ItemKind == seedKind).Sum(lot => lot.Quantity));
            }
            Assert.Contains(second.Fields, field => field.HouseholdId == household && field.Position != repeated.Position);
            var snapshot = new OwnerWorldObservationStore(second).GetSnapshot();
            Assert.Contains(snapshot.Fields!, field => field.HouseholdId == household);
            using var restored = Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(second.ExportState())));
            Assert.Equal(second.Fields, restored.Fields);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(second.ExportState()), PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        }
        finally { second.Dispose(); }
    }

    [Fact]
    public void CurrentAlphaCutoffPreservesCurrentFieldsAndGroundHarvestLots()
    {
        var (state, _, household, point) = PreparedFarmer("field-schema");
        var preceding = Assert.Throws<InvalidDataException>(() => Restore(state with { SchemaVersion = 33, Fields = null }));
        Assert.Contains($"minimum supported schema {PrivateWorldRuntime.StateSchemaVersion}", preceding.Message,
            StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => Restore(state with { Fields = null }));
        var fieldState = state with { Fields = [new(point, household, FarmFieldStage.Prepared)] };
        Assert.Throws<InvalidDataException>(() => Restore(fieldState with { SchemaVersion = 33 }));
        using var fieldsRestored = Restore(fieldState);
        Assert.Single(fieldsRestored.Fields);

        var groundState = WithInventory(state, InventoryFixture.AddLot(state.Society.Society.Inventory,
            "field-ground-schema", "grain", household, 1, groundPosition: new(point.X, point.Y)));
        Assert.Throws<InvalidDataException>(() => Restore(groundState with { SchemaVersion = 33 }));
        using var groundRestored = Restore(groundState);
        Assert.Equal(new InventoryGroundPosition(point.X, point.Y), groundRestored.Society.Inventory.GetLot("field-ground-schema").GroundPosition);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("unknown-field-worker")]
    public void SavedFieldWorkWithAnInvalidWorkerIsRefusedAsInvalidCheckpointData(string? workerId)
    {
        var (state, actor, _, point) = PreparedFarmer("field-invalid-worker-restore");
        using var world = Restore(state);
        Assert.True(world.StartFieldWork(actor, point, FarmWorkKind.Till).Accepted);
        var active = world.ExportState();
        var healthyBytes = PrivateWorldRuntimeCodec.Encode(active);
        var field = Assert.Single(active.Fields!);
        Assert.NotNull(field.Work);
        var damaged = active with
        {
            Fields = [field with { Work = field.Work with { WorkerId = workerId! } }],
        };

        Assert.Throws<InvalidDataException>(() => Restore(damaged));

        Assert.Equal(healthyBytes, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        using var restored = Restore(PrivateWorldRuntimeCodec.Decode(healthyBytes));
        Assert.Equal(active.Fields, restored.Fields);
        Assert.Equal(healthyBytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }

    internal static PrivateWorldRuntimeState FeedFarmTownFromAvailableStock(PrivateWorldRuntimeState state, string household)
    {
        var farmhouse = state.WorldSimulation!.Buildings.First(building => building.HouseholdId == household &&
            state.WorldContent!.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId)
                .Tags.Contains("farmhouse", StringComparer.Ordinal));
        var town = state.Towns!.Single(item => item.Id == farmhouse.TownId);
        var residents = state.Society.Society.Inhabitants.Where(person => person.Status == SocietyInhabitantStatus.Active &&
            town.ResidentIds.Contains(person.Id, StringComparer.Ordinal)).ToArray();
        // Consume the Town's ready food through the existing reservation boundary;
        // raw crops, planting reserves and another Town's stock stay in place.
        foreach (var owner in residents.Select(person => person.Id)
                     .Concat(residents.Select(person => person.HouseholdId).OfType<string>()).Distinct(StringComparer.Ordinal))
            state = FeedHouseholdFromAvailableStock(state, owner);
        return state;
    }

    internal static PrivateWorldRuntimeState FeedHouseholdFromAvailableStock(PrivateWorldRuntimeState state, string household)
    {
        var inventory = state.Society.Society.Inventory;
        foreach (var lot in inventory.Lots.Where(lot => lot.OwnerId == household &&
            lot.ItemKind is "food" or "berries" or "wild_greens" or "cultivated_greens" or "fruit" or
                "simple_meal" or "porridge" or "berry_porridge" or "fruit_porridge" or "bread" or "stew" or "restaurant_meal").ToArray())
        {
            var available = lot.FreshnessBasisPoints == 0 || lot.ConditionBasisPoints == 0 ? 0 : lot.Quantity - inventory.Reservations
                .Where(reservation => reservation.LotId == lot.Id && reservation.State is
                    InventoryReservationState.Reserved or InventoryReservationState.PartiallyConsumed or InventoryReservationState.Committed)
                .Sum(reservation => reservation.Quantity);
            if (available == 0) continue;
            var reservationId = $"test-household-meal:{inventory.WorldTick}:{lot.Id}";
            inventory = InventoryFixture.ConsumeReservation(InventoryFixture.Reserve(inventory, reservationId, household,
                lot.Id, available, "household_meals", checked(inventory.WorldTick + 1)), reservationId);
        }
        return WithInventory(state, inventory);
    }

    [Fact]
    public async Task RawCropReservesDoNotSuppressFreshFoodPlantingAcrossReload()
    {
        var (state, actor, household, point) = PreparedFarmer("field-raw-stock-shortage");
        state = FeedFarmTownFromAvailableStock(state, household);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "raw-reserve-grain", "grain", household, 40,
            storageBuildingId: "first-town-farmhouse");
        inventory = InventoryFixture.AddLot(inventory, "raw-reserve-potatoes", "potatoes", household, 40);
        state = WithInventory(state, inventory) with { Fields = [new(point, household, FarmFieldStage.Prepared)] };
        // Only the farmer acts. Other founders walking over the field or
        // hauling the green seed would test their timing, not the crop choice.
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)),
            id => id == actor ? new DeterministicDecisionProvider() : new IdleProvider());
        for (var tick = 0; tick < 120 && !world.Fields.Any(field => field.HouseholdId == household &&
                 field.Crop == FarmFieldRules.Greens); tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Contains(world.ExportState().Events, item => item.Kind == "field_planted" &&
            item.Detail.EndsWith(":" + FarmFieldRules.Greens, StringComparison.Ordinal));
        Assert.Equal(40, world.Society.Inventory.GetLot("raw-reserve-grain").Quantity);
        Assert.Equal(40, world.Society.Inventory.GetLot("raw-reserve-potatoes").Quantity);
        using var restored = Reload(world);
        Assert.Contains(restored.Fields, field => field.HouseholdId == household && field.Crop == FarmFieldRules.Greens);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TemporarilyOccupiedFieldKeepsItsPlantingChoiceAndSiblingDoesNotReplaceIt(bool pausedBuild)
    {
        var (state, actor, household, point) = PreparedFarmer("field-claim-through-occupancy");
        state = FeedFarmTownFromAvailableStock(state, household);
        var sibling = state.Society.Society.Inhabitants.First(person => person.HouseholdId == household &&
            person.AgeBand == SocietyAgeBand.Adult && person.Id != actor).Id;
        var siblingOrigin = state.Inhabitants.Single(person => person.InhabitantId == sibling).Position;
        Assert.NotEqual(point, siblingOrigin);
        Assert.True(state.Map.IsReachableOnFoot(siblingOrigin, point));
        var occupied = state.Map.Resources.Select(resource => resource.Position)
            .Concat(state.Map.CampObjects.Select(item => item.Position)).ToHashSet();
        var siblingAway = state.Map.Tiles.Select(tile => tile.Position)
            .First(position => position != point && position != siblingOrigin && state.Map.IsBuildable(position) &&
                !occupied.Contains(position) && !state.Inhabitants.Any(person =>
                    person.Position == position && person.InhabitantId != actor && person.InhabitantId != sibling) &&
                state.Map.IsReachableOnFoot(position, point));
        var candidateId = $"farm:Plant:{point.X}:{point.Y}:{FarmFieldRules.Greens}";
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory,
            "field-claim-greens-seed", FarmFieldRules.PlantingItem(FarmFieldRules.Greens), actor, 1);
        inventory = InventoryFixture.AddLot(inventory, "field-claim-sibling-hoe", FarmFieldRules.Hoe, sibling, 1);
        var savedPlan = pausedBuild ? new SettlementProject(
            "build:recipe:" + state.WorldContent!.Recipes.Single(item => item.LocalId == "wooden-axe").CanonicalId,
            "Wooden axe", state.Society.Society.WorldTick, "paused", 4,
            "Materials for this work are unavailable. Choose another task for now.",
            LastTransitionTick: state.Society.Society.WorldTick, RequiresFreshChoice: true) : null;
        state = state with
        {
            Society = state.Society with
            {
                Society = state.Society.Society with { Inventory = inventory },
                Cognition = state.Society.Cognition with
                {
                    Queue = [],
                    Runtimes = state.Society.Cognition.Runtimes.Select(runtime => runtime with
                    {
                        CurrentIntention = null,
                    }).ToArray(),
                },
            },
            Fields = [new(point, household, FarmFieldStage.Prepared)],
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with
                {
                    Position = siblingOrigin,
                    HungerBasisPoints = 10_000,
                    LastDecisionContext = null,
                    Project = savedPlan
                }
                : person.InhabitantId == sibling
                    ? person with { Position = siblingAway, HungerBasisPoints = 10_000, LastDecisionContext = null }
                    : person).ToArray(),
        };
        var firstChoice = new RecordingFieldChoiceProvider(actor, candidateId);
        using var choosing = PrivateWorldRuntime.Restore(state, _ => firstChoice);
        Assert.True((await choosing.AdvanceOneTickAsync()).Advanced);
        var actorCandidates = firstChoice.Requests.Where(request => request.InhabitantId == actor)
            .SelectMany(request => request.CandidateIds).ToArray();
        Assert.True(actorCandidates.Contains(candidateId, StringComparer.Ordinal),
            $"Actor candidates: {string.Join(",", actorCandidates)}");
        Assert.Contains(candidateId, firstChoice.SelectedCandidateIds);
        var actorRuntime = choosing.ExportState().Society.Cognition.Runtimes.Single(item => item.InhabitantId == actor);
        Assert.Equal(candidateId, actorRuntime.CurrentIntention?.CandidateId);
        Assert.Contains(choosing.Society.Inventory.Lots, lot => lot.OwnerId == actor &&
            lot.ItemKind == FarmFieldRules.PlantingItem(FarmFieldRules.Greens) && lot.Quantity == 1);

        state = choosing.ExportState() with
        {
            Inhabitants = choosing.ExportState().Inhabitants.Select(person => person.InhabitantId == sibling
                    ? person with { Position = point, LastDecisionContext = null }
                    : person).ToArray(),
        };
        state = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state));
        var secondChoice = new RecordingFieldChoiceProvider(actor, candidateId);
        using var contested = PrivateWorldRuntime.Restore(state, _ => secondChoice);
        Assert.Equal(candidateId, contested.ExportState().Society.Cognition.Runtimes
            .Single(item => item.InhabitantId == actor).CurrentIntention?.CandidateId);
        Assert.True((await contested.AdvanceOneTickAsync()).Advanced);
        var siblingRequest = Assert.Single(secondChoice.Requests, request => request.InhabitantId == sibling);
        Assert.True(!siblingRequest.CandidateIds.Any(id => id.StartsWith(
                $"farm:Plant:{point.X}:{point.Y}:", StringComparison.Ordinal)),
            $"Sibling candidates: {string.Join(",", siblingRequest.CandidateIds)}");
        Assert.Equal(candidateId, contested.ExportState().Society.Cognition.Runtimes
            .Single(item => item.InhabitantId == actor).CurrentIntention?.CandidateId);
        Assert.Contains(contested.Fields, field => field.Position == point && field.Stage == FarmFieldStage.Prepared);
        Assert.Equal(savedPlan, contested.Inhabitants.Single(person => person.InhabitantId == actor).Project);
        contested.Validate();
    }

    [Fact]
    public async Task FreshChoicePausedBuildCanYieldToFieldWorkWithoutLosingItsSavedPlan()
    {
        var (state, actor, household, point) = PreparedFarmer("field-fresh-choice-paused-project");
        state = FeedFarmTownFromAvailableStock(state, household);
        var recipe = state.WorldContent!.Recipes.Single(item => item.LocalId == "wooden-axe");
        var project = new SettlementProject("build:recipe:" + recipe.CanonicalId, recipe.DisplayName,
            state.Society.Society.WorldTick, "paused", 4,
            "Materials for this work are unavailable. Choose another task for now.",
            LastTransitionTick: state.Society.Society.WorldTick, RequiresFreshChoice: true);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "fresh-choice-greens-seed",
            FarmFieldRules.PlantingItem(FarmFieldRules.Greens), actor, 1);
        state = WithInventory(state, inventory) with
        {
            Fields = [new(point, household, FarmFieldStage.Prepared)],
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { Project = project, LastDecisionContext = null }
                : person).ToArray(),
        };
        var candidateId = $"farm:Plant:{point.X}:{point.Y}:{FarmFieldRules.Greens}";
        var provider = new RecordingFieldChoiceProvider(actor, candidateId);
        using var world = PrivateWorldRuntime.Restore(state, id => id == actor ? provider : new IdleProvider());

        Assert.True((await world.AdvanceOneTickAsync()).Advanced);

        Assert.Contains(candidateId, provider.SelectedCandidateIds);
        var field = Assert.Single(world.Fields);
        Assert.Equal(FarmWorkKind.Plant, field.Work?.Kind);
        Assert.Equal(actor, field.Work?.WorkerId);
        var paused = world.Inhabitants.Single(item => item.InhabitantId == actor).Project;
        Assert.Equal(project, paused);
        world.Validate();
    }

    [Theory]
    [InlineData("grain", "grain_seed")]
    [InlineData("cultivated_greens", "cultivated_green_seed")]
    public async Task PlantingKeepsThePreviousReserveUntilItCompletesThenReleasesItAcrossReload(string crop, string seedKind)
    {
        var (state, actor, household, point) = await ReadyFarmer("field-crop-change");
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "old-planting-stock", "grain_seed", household, 2,
            groundPosition: new(point.X, point.Y));
        inventory = InventoryFixture.Reserve(inventory, "old-replanting", household, "old-planting-stock", 1,
            "field_replanting", long.MaxValue);
        inventory = InventoryFixture.AddLot(inventory, "new-crop-seed", seedKind, actor, 2);
        state = WithInventory(state, inventory) with
        {
            Fields = [state.Fields!.Single() with { Stage = FarmFieldStage.Harvested, Cycle = 1,
                ReplantingReservationId = "old-replanting" }],
        };
        using var starting = Restore(state);
        Assert.True(starting.StartFieldWork(actor, point, FarmWorkKind.Plant, crop, "new-crop-seed").Accepted);
        await Advance(starting, 2);
        Assert.Equal(InventoryReservationState.Reserved, starting.Society.Inventory.GetReservation("old-replanting").State);
        using var completing = Reload(starting);
        await Advance(completing, 2);
        Assert.Equal(crop, Assert.Single(completing.Fields).Crop);
        Assert.Null(Assert.Single(completing.Fields).ReplantingReservationId);
        Assert.Equal(2, completing.Society.Inventory.GetLot("old-planting-stock").Quantity);
        Assert.Equal(1, completing.Society.Inventory.GetLot("new-crop-seed").Quantity);
        Assert.DoesNotContain(completing.Society.Inventory.Reservations, reservation => reservation.Id == "old-replanting" &&
            reservation.State is InventoryReservationState.Reserved or InventoryReservationState.PartiallyConsumed);
        Assert.Contains(completing.ExportState().Events, item => item.Kind == "field_planted" &&
            item.Detail.EndsWith(":" + crop, StringComparison.Ordinal));
        using var restored = Reload(completing);
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(completing.ExportState()), PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }

    [Theory]
    [InlineData(SocietyAgeBand.Infant, false)]
    [InlineData(SocietyAgeBand.Elder, true)]
    public async Task FieldWorkRespectsAgeBeforeAndAfterReload(SocietyAgeBand age, bool allowed)
    {
        var (state, actor, _, point) = PreparedFarmer("field-age");
        var society = state.Society.Society;
        var years = age switch
        {
            SocietyAgeBand.Infant => 0,
            SocietyAgeBand.Child => society.Config.DayLifecycle?.ChildStartDay ?? society.Config.InfantYears,
            SocietyAgeBand.Adult => society.Config.DayLifecycle?.AdultStartDay ?? society.Config.AdultYears,
            _ => society.Config.DayLifecycle?.ElderStartDay ?? society.Config.ElderYears,
        };
        var birth = society.LifeTickAt(society.WorldTick) - years * society.Config.TicksPerLifecycleAge;
        state = state with
        {
            Towns = state.Towns!.Select(town => town with
            {
                Governance = TownGovernanceRules.Advance(town.Governance!, town.Id, state.WorldSeed,
                    town.ResidentIds.Where(id => id != actor || allowed), society.WorldTick, state.WorldSystems!.Config.TicksPerDay),
            }).ToArray(),
            Society = state.Society with
            {
                Society = society with
                {
                    Inhabitants = society.Inhabitants.Select(person => person.Id == actor ? person with
                    {
                        AgeBand = age,
                        BirthTick = birth,
                        BirthLifeTick = society.LifeClock is null ? null : birth,
                        LastLifecycleYearChecked = years,
                        CurrentRole = SocietyWorkRole.Unassigned,
                    } : person).ToArray(),
                },
            },
        };
        using var world = Restore(state);
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.Equal(allowed, world.StartFieldWork(actor, point, FarmWorkKind.Till).Accepted);
        if (!allowed)
        {
            Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
            using var reloaded = Reload(world);
            Assert.False(reloaded.StartFieldWork(actor, point, FarmWorkKind.Till).Accepted);
            Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(reloaded.ExportState()));
        }
        else
        {
            await Advance(world, 3);
            using var reloaded = Reload(world);
            await Advance(reloaded, 5);
            Assert.Equal(FarmFieldStage.Prepared, Assert.Single(reloaded.Fields).Stage);
        }
    }

    [Fact]
    public async Task IronHoeWorksFasterThanWoodAndSavedWorkKeepsItsSelectedTool()
    {
        var (woodState, actor, _, point) = PreparedFarmer("field-wood-hoe-speed");
        using var wooden = Restore(woodState);
        Assert.True(wooden.StartFieldWork(actor, point, FarmWorkKind.Till).Accepted);
        Assert.Equal("carried-hoe", Assert.Single(wooden.Fields).Work!.HoeLotId);
        await Advance(wooden, 1);
        Assert.Equal(6, Assert.Single(wooden.Fields).Work!.RemainingTicks);
        Assert.Equal(9_000, wooden.Society.Inventory.GetLot("carried-hoe").ConditionBasisPoints);
        using var woodReload = Reload(wooden);
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(wooden.ExportState()),
            PrivateWorldRuntimeCodec.Encode(woodReload.ExportState()));
        await Advance(woodReload, 3);
        Assert.Equal(FarmFieldStage.Prepared, Assert.Single(woodReload.Fields).Stage);
        Assert.Equal(6_000, woodReload.Society.Inventory.GetLot("carried-hoe").ConditionBasisPoints);

        var (ironState, ironActor, _, ironPoint) = PreparedFarmer("field-iron-hoe-speed");
        var inventory = ironState.Society.Society.Inventory with
        {
            Lots = ironState.Society.Society.Inventory.Lots.Where(lot => lot.Id != "carried-hoe").ToArray(),
        };
        inventory = InventoryFixture.AddLot(inventory, "carried-iron-hoe", "iron_hoe", ironActor, 1);
        ironState = WithInventory(ironState, inventory);
        using var iron = Restore(ironState);
        Assert.True(iron.StartFieldWork(ironActor, ironPoint, FarmWorkKind.Till).Accepted);
        Assert.Equal("carried-iron-hoe", Assert.Single(iron.Fields).Work!.HoeLotId);
        await Advance(iron, 1);
        Assert.Equal(5, Assert.Single(iron.Fields).Work!.RemainingTicks);
        await Advance(iron, 2);
        Assert.Equal(FarmFieldStage.Prepared, Assert.Single(iron.Fields).Stage);
        Assert.Equal(7_000, iron.Society.Inventory.GetLot("carried-iron-hoe").ConditionBasisPoints);
    }

    [Fact]
    public async Task SickleTiersSpeedHarvestAndWearOnlyAsSavedFieldWorkProgresses()
    {
        var (state, actor, household, point) = await ReadyFarmer("field-sickle-speed");
        var woodenInventory = InventoryFixture.AddLot(state.Society.Society.Inventory,
            "carried-wooden-sickle", "wooden_sickle", actor, 1);
        using var wooden = Restore(WithInventory(state, woodenInventory));
        Assert.True(wooden.StartFieldWork(actor, point, FarmWorkKind.Harvest).Accepted);
        Assert.Equal("carried-wooden-sickle", Assert.Single(wooden.Fields).Work!.SickleLotId);
        await Advance(wooden, 1);
        Assert.Equal(2, Assert.Single(wooden.Fields).Work!.RemainingTicks);
        Assert.Equal(8_000, wooden.Society.Inventory.GetLot("carried-wooden-sickle").ConditionBasisPoints);
        using var resumedWooden = Reload(wooden);
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(wooden.ExportState()),
            PrivateWorldRuntimeCodec.Encode(resumedWooden.ExportState()));
        await Advance(resumedWooden, 1);
        Assert.Equal(FarmFieldStage.Harvested, Assert.Single(resumedWooden.Fields).Stage);
        Assert.Equal(6_000, resumedWooden.Society.Inventory.GetLot("carried-wooden-sickle").ConditionBasisPoints);
        Assert.Contains(resumedWooden.Society.Inventory.Lots, lot => lot.OwnerId == household && lot.ItemKind == "grain" &&
            lot.GroundPosition == new InventoryGroundPosition(point.X, point.Y));

        var (ironState, ironActor, _, ironPoint) = await ReadyFarmer("field-iron-sickle-speed");
        var ironInventory = InventoryFixture.AddLot(ironState.Society.Society.Inventory,
            "carried-iron-sickle", "iron_sickle", ironActor, 1);
        using var iron = Restore(WithInventory(ironState, ironInventory));
        Assert.True(iron.StartFieldWork(ironActor, ironPoint, FarmWorkKind.Harvest).Accepted);
        Assert.Equal("carried-iron-sickle", Assert.Single(iron.Fields).Work!.SickleLotId);
        await Advance(iron, 1);
        Assert.Equal(FarmFieldStage.Harvested, Assert.Single(iron.Fields).Stage);
        Assert.Equal(9_000, iron.Society.Inventory.GetLot("carried-iron-sickle").ConditionBasisPoints);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ToolBreakingDuringUnfinishedFieldWorkLeavesAReloadableCheckpoint(bool harvest)
    {
        var (state, actor, _, point) = harvest
            ? await ReadyFarmer("field-tool-break-harvest")
            : PreparedFarmer("field-tool-break-till");
        var toolId = harvest ? "breaking-sickle" : "carried-hoe";
        var inventory = state.Society.Society.Inventory;
        inventory = harvest
            ? InventoryFixture.AddLot(inventory, toolId, "wooden_sickle", actor, 1,
                conditionBasisPoints: 1_000)
            : inventory with
            {
                Lots = inventory.Lots.Select(lot => lot.Id == toolId
                    ? lot with { ConditionBasisPoints = 500 } : lot).ToArray(),
            };
        using var working = Restore(WithInventory(state, inventory));
        var beforeLots = working.Society.Inventory.Lots;
        Assert.True(working.StartFieldWork(actor, point,
            harvest ? FarmWorkKind.Harvest : FarmWorkKind.Till).Accepted);
        await Advance(working, 1);

        var tool = working.Society.Inventory.GetLot(toolId);
        Assert.Equal((actor, 1, 0), (tool.OwnerId, tool.Quantity, tool.ConditionBasisPoints));
        Assert.Null(tool.StorageBuildingId);
        Assert.Null(tool.DeliveryBuildingId);
        Assert.Null(tool.GroundPosition);
        if (harvest)
        {
            var field = Assert.Single(working.Fields);
            Assert.Equal(FarmFieldStage.Ready, field.Stage);
            Assert.Null(field.Work);
            Assert.DoesNotContain(working.ExportState().Events, item => item.Kind == "field_harvested");
        }
        else
        {
            Assert.Empty(working.Fields);
            Assert.DoesNotContain(working.ExportState().Events, item => item.Kind == "field_prepared");
        }
        Assert.Equal(beforeLots.Select(lot => (lot.Id, lot.Quantity)),
            working.Society.Inventory.Lots.Select(lot => (lot.Id, lot.Quantity)));
        var bytes = PrivateWorldRuntimeCodec.Encode(working.ExportState());
        using var reloaded = Restore(PrivateWorldRuntimeCodec.Decode(bytes));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(reloaded.ExportState()));
        await Advance(reloaded, 1);
        Assert.Equal(0, reloaded.Society.Inventory.GetLot(toolId).ConditionBasisPoints);
        Assert.All(reloaded.Fields, field => Assert.Null(field.Work));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task DeathCancelsUnfinishedFieldWorkBeforeItCompletesAndKeepsCompletedHarvest(bool harvest, bool completed)
    {
        var (state, actor, household, point) = await ReadyFarmer("field-death");
        if (!harvest) state = state with { Fields = [new(point, household, FarmFieldStage.Prepared)] };
        state = WithInventory(state, InventoryFixture.AddLot(state.Society.Society.Inventory, "death-planting", "grain_seed", actor, 2));
        using var working = Restore(state);
        Assert.True(working.StartFieldWork(actor, point, harvest ? FarmWorkKind.Harvest : FarmWorkKind.Plant,
            harvest ? null : "grain", harvest ? null : "death-planting").Accepted);
        await Advance(working, completed ? 4 : 3);
        var saved = working.ExportState();
        var outputs = saved.Society.Society.Inventory.Lots.Where(lot => lot.Id.StartsWith("field-", StringComparison.Ordinal))
            .Select(lot => (lot.Id, lot.OwnerId, lot.Quantity, lot.GroundPosition)).ToArray();
        var plantingTotal = saved.Society.Society.Inventory.Lots.Where(lot => lot.ItemKind == "grain_seed").Sum(lot => lot.Quantity);
        saved = DyingWorker(saved, actor);
        using var dying = Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(saved)));
        await Advance(dying, 1);
        Assert.Equal(SocietyInhabitantStatus.Dead, dying.Society.GetInhabitant(actor).Status);
        var field = Assert.Single(dying.Fields);
        Assert.Null(field.Work);
        Assert.Equal(completed ? FarmFieldStage.Harvested : harvest ? FarmFieldStage.Ready : FarmFieldStage.Prepared, field.Stage);
        Assert.Equal(completed ? 1 : 0, field.Cycle);
        Assert.Equal(outputs, dying.Society.Inventory.Lots.Where(lot => lot.Id.StartsWith("field-", StringComparison.Ordinal))
            .Select(lot => (lot.Id, lot.OwnerId, lot.Quantity, lot.GroundPosition)).ToArray());
        Assert.Equal(plantingTotal, dying.Society.Inventory.Lots.Where(lot => lot.ItemKind == "grain_seed").Sum(lot => lot.Quantity));
        if (!harvest)
            Assert.DoesNotContain(dying.Society.Inventory.Reservations, reservation => reservation.LotId == "death-planting" &&
                reservation.State is InventoryReservationState.Reserved or InventoryReservationState.PartiallyConsumed);
        using var reloaded = Reload(dying);
        await Advance(reloaded, 2);
        Assert.Equal(field, Assert.Single(reloaded.Fields));
    }

    private static PrivateWorldRuntimeState DyingWorker(PrivateWorldRuntimeState state, string actor)
    {
        var society = state.Society.Society;
        var nextLifeTick = society.Config.TicksPerLifecycleAge - 1;
        var delta = nextLifeTick - society.LifeTickAt(society.WorldTick);
        var years = society.Config.DayLifecycle!.MaximumDay - 1;
        return state with
        {
            Society = state.Society with
            {
                Society = society with
                {
                    LifeClock = new SocietyLifeClock(society.LifeClock?.Rate ?? 1, society.WorldTick, nextLifeTick),
                    Inhabitants = society.Inhabitants.Select(person => person.Id == actor ? person with
                    {
                        BirthLifeTick = nextLifeTick + 1 - (years + 1) * society.Config.TicksPerLifecycleAge,
                        AgeBand = SocietyAgeBand.Elder,
                        LastLifecycleYearChecked = years,
                    } : person with { BirthLifeTick = (person.BirthLifeTick ?? person.BirthTick) + delta }).ToArray(),
                },
            },
        };
    }

    internal static async Task<(PrivateWorldRuntimeState State, string Actor, string Household, GridPoint Point)> ReadyFarmer(string seed)
    {
        var (state, actor, household, point) = PreparedFarmer(seed);
        using var world = Restore(state);
        await Advance(world, 1);
        var readyState = world.ExportState();
        var occupied = world.WorldSimulation.Buildings.SelectMany(building => WorldContentSimulationRules.Footprint(
            world.WorldContent.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId), building))
            .Concat(readyState.Map.Resources.Select(resource => resource.Position))
            .Concat(world.RoadTiles).Concat(readyState.Map.CampObjects.Select(item => item.Position)).ToHashSet();
        var fertility = new LandFertility(readyState.Map, readyState.WorldSeed);
        var readyPoint = readyState.Map.Tiles.Select(tile => tile.Position)
            .Where(candidate => fertility.CanFarm(candidate) && !occupied.Contains(candidate) &&
                !readyState.Inhabitants.Any(person => person.InhabitantId != actor && person.Position == candidate))
            .OrderBy(candidate => readyState.Map.FootDistance(point, candidate)).First();
        return (readyState with
        {
            Fields = [new(readyPoint, household, FarmFieldStage.Ready, "grain", ReadyTick: 1, Tended: true)],
            Inhabitants = readyState.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { Position = readyPoint, LastDecisionContext = null, TravelCooldownTicks = 0 }
                : person).ToArray(),
        }, actor, household, readyPoint);
    }

    [Theory]
    [InlineData("grain", "grain_seed")]
    [InlineData("potatoes", "potatoes")]
    [InlineData("cultivated_greens", "cultivated_green_seed")]
    public async Task PhysicalCropCycleSurvivesReloadAndLeavesOwnedHarvestAndPlantingReserveOnTheTile(string crop, string seedKind)
    {
        var (state, actor, household, point) = await CompactFarmer("field-cycle-" + crop);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "carried-planting", seedKind, actor, 2);
        state = WithInventory(state, inventory);
        using var tilling = Restore(state);
        Assert.True(tilling.StartFieldWork(actor, point, FarmWorkKind.Till).Accepted);
        await Advance(tilling, 3);
        Assert.Equal(FarmFieldStage.Preparing, Assert.Single(tilling.Fields).Stage);
        using var prepared = Reload(tilling);
        await Advance(prepared, 1);
        Assert.Equal(FarmFieldStage.Prepared, Assert.Single(prepared.Fields).Stage);
        Assert.Contains(prepared.Inhabitants.Single(person => person.InhabitantId == actor).Skills!,
            skill => skill.Kind == SettlementSkillKind.Farming);
        Assert.True(prepared.StartFieldWork(actor, point, FarmWorkKind.Plant, crop, "carried-planting").Accepted);
        Assert.Equal(3, Assert.Single(prepared.Fields).Work!.RemainingTicks);
        await Advance(prepared, 2);
        using var planted = Reload(prepared);
        await Advance(planted, 1);
        Assert.Equal(FarmFieldStage.Planted, Assert.Single(planted.Fields).Stage);
        Assert.Equal(1, planted.Society.Inventory.GetLot("carried-planting").Quantity);
        using var growing = Reload(planted);
        await Advance(growing, 1);
        Assert.Equal(FarmFieldStage.Growing, Assert.Single(growing.Fields).Stage);
        Assert.True(growing.StartFieldWork(actor, point, FarmWorkKind.Tend).Accepted);
        await Advance(growing, 2);
        Assert.True(Assert.Single(growing.Fields).Tended);
        var ticksToReady = Assert.Single(growing.Fields).ReadyTick - growing.WorldTick;
        await Advance(growing, checked((int)Math.Max(1, ticksToReady)));
        Assert.Equal(FarmFieldStage.Ready, Assert.Single(growing.Fields).Stage);
        using var harvesting = Reload(growing);
        Assert.True(harvesting.StartFieldWork(actor, point, FarmWorkKind.Harvest).Accepted);
        await Advance(harvesting, 2);
        using var harvested = Reload(harvesting);
        await Advance(harvested, 1);
        var field = Assert.Single(harvested.Fields);
        Assert.Equal(FarmFieldStage.Harvested, field.Stage);
        Assert.Equal(1, field.Cycle);
        var lots = harvested.Society.Inventory.Lots.Where(lot => lot.Id.StartsWith("field-", StringComparison.Ordinal)).ToArray();
        Assert.All(lots, lot =>
        {
            Assert.Equal(household, lot.OwnerId);
            Assert.Equal(new InventoryGroundPosition(point.X, point.Y), lot.GroundPosition);
            Assert.Null(lot.StorageBuildingId);
            Assert.Null(lot.DeliveryBuildingId);
        });
        Assert.InRange(Assert.Single(lots, lot => lot.ItemKind == crop).Quantity, 2, 11);
        var reserve = Assert.Single(harvested.Society.Inventory.Reservations, item => item.Id == field.ReplantingReservationId);
        Assert.Equal(InventoryReservationState.Reserved, reserve.State);
        Assert.Equal(1, reserve.Quantity);
        Assert.Equal(seedKind, harvested.Society.Inventory.GetLot(reserve.LotId).ItemKind);
        var visible = new OwnerWorldObservationStore(harvested).GetSnapshot();
        Assert.Equal("harvested", Assert.Single(visible.Fields).Stage);
        Assert.All(visible.GroundStocks.Where(stock => stock.Position.X == point.X && stock.Position.Y == point.Y),
            stock => Assert.Equal(household, stock.OwnerId));
        using var final = Reload(harvested);
        await Advance(final, 2);
        Assert.Equal(lots.Select(lot => (lot.Id, lot.ItemKind, lot.OwnerId, lot.Quantity, lot.GroundPosition, lot.StorageBuildingId)),
            final.Society.Inventory.Lots.Where(lot => lot.Id.StartsWith("field-", StringComparison.Ordinal))
                .Select(lot => (lot.Id, lot.ItemKind, lot.OwnerId, lot.Quantity, lot.GroundPosition, lot.StorageBuildingId)));
        Assert.Equal(field, Assert.Single(final.Fields));
    }

    [Fact]
    public async Task RefusedOrInterruptedWorkCannotConsumeRemoteOrForeignSeedsOrDuplicateAField()
    {
        var (state, actor, household, point) = PreparedFarmer("field-refusals");
        var foreign = state.Inhabitants.First(person => person.InhabitantId != actor).InhabitantId;
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "remote-seed", "grain_seed", household, 2);
        inventory = InventoryFixture.AddLot(inventory, "foreign-seed", "grain_seed", foreign, 2);
        inventory = InventoryFixture.AddLot(inventory, "own-seed", "grain_seed", actor, 2);
        using var world = Restore(WithInventory(state, inventory));
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False(world.StartFieldWork(actor, new(point.X + 1, point.Y), FarmWorkKind.Till).Accepted);
        Assert.False(world.StartFieldWork(foreign, point, FarmWorkKind.Till).Accepted);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        Assert.True(world.StartFieldWork(actor, point, FarmWorkKind.Till).Accepted);
        await Advance(world, 8);
        before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False(world.StartFieldWork(actor, point, FarmWorkKind.Till).Accepted);
        Assert.False(world.StartFieldWork(actor, point, FarmWorkKind.Plant, "grain", "remote-seed").Accepted);
        Assert.False(world.StartFieldWork(actor, point, FarmWorkKind.Plant, "grain", "foreign-seed").Accepted);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        Assert.True(world.StartFieldWork(actor, point, FarmWorkKind.Plant, "grain", "own-seed").Accepted);
        await Advance(world, 2);
        state = world.ExportState() with
        {
            Inhabitants = world.ExportState().Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { Position = AwayFromField(state) } : person).ToArray(),
        };
        using var interrupted = Restore(state);
        await Advance(interrupted, 1);
        Assert.Equal(FarmFieldStage.Prepared, Assert.Single(interrupted.Fields).Stage);
        Assert.Null(Assert.Single(interrupted.Fields).Work);
        Assert.Equal(2, interrupted.Society.Inventory.GetLot("own-seed").Quantity);
        Assert.Contains(interrupted.Society.Inventory.Reservations, item =>
            item.Purpose == "field_planting" && item.State == InventoryReservationState.Released);
        using var reloaded = Reload(interrupted);
        await Advance(reloaded, 2);
        Assert.Equal(2, reloaded.Society.Inventory.GetLot("own-seed").Quantity);
        Assert.Equal(FarmFieldStage.Prepared, Assert.Single(reloaded.Fields).Stage);
    }

    private static GridPoint AwayFromField(PrivateWorldRuntimeState state) => state.Map.Tiles
        .First(tile => state.Map.IsBuildable(tile.Position) && !state.Inhabitants.Any(person => person.Position == tile.Position)).Position;

    [Fact]
    public async Task OrdinaryChooserHarvestsReadyCropsOntoTheFieldEvenWhenBothFarmStoresAreFull()
    {
        var (state, actor, household, point) = await ReadyFarmer("field-full-stores");
        var inventory = state.Society.Society.Inventory;
        var silo = state.WorldContent!.Buildings.Single(item => item.LocalId == "silo-1x1");
        var house = state.WorldSimulation!.Buildings.First(building => building.HouseholdId == household &&
            state.WorldContent.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId).Tags.Contains("house"));
        foreach (var cost in silo.BuildCosts)
            inventory = InventoryFixture.AddLot(inventory, "silo-material:" + cost.ResourceId, cost.ResourceId, household,
                cost.Amount, storageBuildingId: house.InstanceId);
        using var setup = Restore(WithInventory(state, inventory));
        var farmhouse = setup.WorldSimulation.Buildings.Single(building => building.InstanceId == "first-town-farmhouse");
        Assert.Contains(Enumerable.Range(-2, 5).SelectMany(dy => Enumerable.Range(-2, 5)
            .Select(dx => new GridPoint(farmhouse.Position.X + dx, farmhouse.Position.Y + dy)))
, tile => setup.PlaceBuilding("full-stores-silo", silo.CanonicalId, tile, household).Applied);
        state = setup.ExportState();
        inventory = state.Society.Society.Inventory;
        var stores = state.WorldSimulation!.Buildings.Where(building => building.HouseholdId == household &&
            building.InstanceId is "first-town-farmhouse" or "full-stores-silo").ToArray();
        Assert.Equal(2, stores.Length);
        foreach (var store in stores)
            inventory = InventoryFixture.AddLot(inventory, "full:" + store.InstanceId, "grain", household,
                store.InstanceId == "full-stores-silo" ? 64 : 96, storageBuildingId: store.InstanceId);
        var harvest = $"farm:Harvest:{point.X}:{point.Y}:-";
        using var world = PrivateWorldRuntime.Restore(WithInventory(state, inventory),
            id => new ChooseProvider(id == actor ? harvest : "safe_idle"));
        for (var tick = 0; tick < 12 && world.Fields.Single().Stage != FarmFieldStage.Harvested; tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var field = Assert.Single(world.Fields);
        Assert.Equal(FarmFieldStage.Harvested, field.Stage);
        var output = world.Society.Inventory.Lots.Where(lot => lot.Id.StartsWith("field-", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, output.Length);
        Assert.All(output, lot =>
        {
            Assert.Equal(household, lot.OwnerId);
            Assert.Equal(new InventoryGroundPosition(point.X, point.Y), lot.GroundPosition);
            Assert.Null(lot.StorageBuildingId);
            Assert.Null(lot.DeliveryBuildingId);
        });
        Assert.Contains(output, lot => lot.ItemKind == "grain" && lot.Quantity > 0);
        var reserve = world.Society.Inventory.GetReservation(field.ReplantingReservationId!);
        Assert.Equal(InventoryReservationState.Reserved, reserve.State);
        Assert.Equal(1, reserve.Quantity);
        Assert.Equal("grain_seed", world.Society.Inventory.GetLot(reserve.LotId).ItemKind);
        Assert.All(stores, store => Assert.Equal(store.InstanceId == "full-stores-silo" ? 64 : 96,
            world.Society.Inventory.Lots.Where(lot => lot.StorageBuildingId == store.InstanceId).Sum(lot => lot.Quantity)));
        using var restored = Reload(world);
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }

    [Fact]
    public async Task HarvestIsPickedUpWhereItLiesAndStorageCapacityIncludesCarriedDeliveriesAcrossReload()
    {
        var (state, actor, household, point) = PreparedFarmer("field-hauling-capacity");
        const string farmhouse = "first-town-farmhouse";
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "nearly-full-farm", "grain", household, 94,
            storageBuildingId: farmhouse);
        inventory = InventoryFixture.AddLot(inventory, "ground-grain", "grain", household, 9,
            groundPosition: new(point.X, point.Y));
        using var collecting = PrivateWorldRuntime.Restore(WithInventory(state, inventory),
            id => new ChooseProvider(id == actor ? "haul_farm_grain" : "safe_idle"));
        for (var tick = 0; tick < 12 && !collecting.Society.Inventory.Lots.Any(lot => lot.OwnerId == actor && lot.DeliveryBuildingId == farmhouse); tick++)
            Assert.True((await collecting.AdvanceOneTickAsync()).Advanced);
        var carried = Assert.Single(collecting.Society.Inventory.Lots, lot => lot.OwnerId == actor && lot.DeliveryBuildingId == farmhouse);
        Assert.Equal(2, carried.Quantity);
        Assert.Null(carried.GroundPosition);
        Assert.Null(carried.StorageBuildingId);
        Assert.Equal(7, collecting.Society.Inventory.GetLot("ground-grain").Quantity);
        Assert.Equal(new InventoryGroundPosition(point.X, point.Y), collecting.Society.Inventory.GetLot("ground-grain").GroundPosition);
        Assert.Equal(94, new OwnerWorldObservationStore(collecting).GetSnapshot().PlacedBuildings.Single(item => item.InstanceId == farmhouse)
            .StoredItems!.Single(item => item.Kind == "grain").Quantity);
        using var delivering = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(collecting.ExportState())),
            id => new ChooseProvider(id == actor ? "haul_household_stock" : "safe_idle"));
        for (var tick = 0; tick < 40 && delivering.Society.Inventory.GetLot(carried.Id).StorageBuildingId != farmhouse; tick++)
            Assert.True((await delivering.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(farmhouse, delivering.Society.Inventory.GetLot(carried.Id).StorageBuildingId);
        Assert.Equal(96, delivering.Society.Inventory.Lots.Where(lot => lot.StorageBuildingId == farmhouse).Sum(lot => lot.Quantity));
        Assert.Equal(103, delivering.Society.Inventory.Lots.Where(lot => lot.Id is "nearly-full-farm" or "ground-grain" || lot.ProvenanceLotId == "ground-grain")
            .Sum(lot => lot.Quantity));
        delivering.Validate();
    }

    [Theory]
    [InlineData("cultivated_greens")]
    public async Task ReadyFoodIsCarriedFromItsGroundTileToFiniteHouseStorageAcrossReload(string kind)
    {
        var (state, actor, household, point) = PreparedFarmer("field-ready-food-hauling");
        var house = state.WorldSimulation!.Buildings.First(building => building.HouseholdId == household &&
            state.WorldContent!.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId).Tags.Contains("house"));
        var capacity = BuildingStorageRules.Capacity(state.WorldContent!.Buildings.Single(
            definition => definition.CanonicalId == house.DefinitionId), house)!.Value;
        var inventory = state.Society.Society.Inventory;
        var stored = inventory.Lots.Where(lot => lot.StorageBuildingId == house.InstanceId).Sum(lot => lot.Quantity);
        inventory = InventoryFixture.AddLot(inventory, "house-capacity-filler", "wood", household,
            capacity - stored - 2, storageBuildingId: house.InstanceId);
        inventory = InventoryFixture.AddLot(inventory, "ready-ground-food", kind, household, 9,
            groundPosition: new(point.X, point.Y));
        inventory = InventoryFixture.Reserve(InventoryFixture.AddLot(inventory, "protected-ground-seed", "grain_seed", household, 1,
            groundPosition: new(point.X, point.Y)), "protected-replanting-seed", household, "protected-ground-seed", 1,
            "field_replanting", long.MaxValue);
        using var collecting = PrivateWorldRuntime.Restore(WithInventory(state, inventory),
            id => id == actor ? HaulProvider() : new IdleProvider());
        for (var tick = 0; tick < 20 && !collecting.Society.Inventory.Lots.Any(lot => lot.ProvenanceLotId == "ready-ground-food"); tick++)
            Assert.True((await collecting.AdvanceOneTickAsync()).Advanced);
        var carried = Assert.Single(collecting.Society.Inventory.Lots, lot => lot.ProvenanceLotId == "ready-ground-food");
        Assert.Equal(actor, carried.OwnerId);
        Assert.Equal(house.InstanceId, carried.DeliveryBuildingId);
        Assert.Equal(2, carried.Quantity);
        Assert.Null(carried.GroundPosition);
        Assert.Equal(7, collecting.Society.Inventory.GetLot("ready-ground-food").Quantity);
        Assert.DoesNotContain(collecting.Society.Inventory.Lots, lot => lot.ItemKind == kind &&
            lot.DeliveryBuildingId == "first-town-farmhouse");
        using var delivering = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(collecting.ExportState())),
            id => id == actor ? HaulProvider() : new IdleProvider());
        for (var tick = 0; tick < 80 && delivering.Society.Inventory.GetLot(carried.Id).StorageBuildingId != house.InstanceId; tick++)
            Assert.True((await delivering.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(house.InstanceId, delivering.Society.Inventory.GetLot(carried.Id).StorageBuildingId);
        Assert.Equal(capacity, delivering.Society.Inventory.Lots.Where(lot => lot.StorageBuildingId == house.InstanceId).Sum(lot => lot.Quantity));
        Assert.Equal(9, delivering.Society.Inventory.Lots.Where(lot => lot.Id == "ready-ground-food" || lot.ProvenanceLotId == "ready-ground-food").Sum(lot => lot.Quantity));
        Assert.Equal(1, delivering.Society.Inventory.GetLot("protected-ground-seed").Quantity);
        Assert.Equal(InventoryReservationState.Reserved, delivering.Society.Inventory.GetReservation("protected-replanting-seed").State);
        delivering.Validate();
    }

    [Fact]
    public async Task PotatoesInAStoragePotAreNotOfferedAsPlantingStock()
    {
        var (state, actor, household, point) = PreparedFarmer("potted-potato-stock");
        state = FeedFarmTownFromAvailableStock(state, household);
        var potatoKind = FarmFieldRules.PlantingItem(FarmFieldRules.Potatoes);
        var inventory = state.Society.Society.Inventory with
        {
            Lots = state.Society.Society.Inventory.Lots.Where(lot => lot.ItemKind != potatoKind).ToArray(),
        };
        // Contents move only with their vessel, so planting cannot take them.
        inventory = InventoryFixture.AddLot(inventory, "potato-pot", InventoryContainerRules.StoragePot, household, 1);
        inventory = InventoryFixture.AddLot(inventory, "potted-potatoes", potatoKind, household, 3,
            containerLotId: "potato-pot");
        state = WithInventory(state, inventory) with { Fields = [new(point, household, FarmFieldStage.Prepared)] };
        var candidateId = $"farm:Plant:{point.X}:{point.Y}:{FarmFieldRules.Potatoes}";
        var provider = new RecordingFieldChoiceProvider(actor, candidateId);
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)),
            _ => provider);
        await Advance(world, 10);
        Assert.Contains(provider.Requests, request => request.InhabitantId == actor);
        Assert.DoesNotContain(provider.Requests, request => request.CandidateIds.Contains(candidateId));
        Assert.DoesNotContain(world.Fields, field => field.Position == point && field.Crop == FarmFieldRules.Potatoes);
        Assert.Equal("potato-pot", world.Society.Inventory.GetLot("potted-potatoes").ContainerLotId);
        Assert.Equal(3, world.Society.Inventory.GetLot("potted-potatoes").Quantity);
    }

    internal static (PrivateWorldRuntimeState State, string Actor, string Household, GridPoint Point) PreparedFarmer(string seed)
    {
        var state = GeographyGeneratorTests.StartedGeneratedWorld(new(seed, WorldSizePreset.Small));
        var farmhouse = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == "first-town-farmhouse");
        var household = farmhouse.HouseholdId!;
        var actor = state.Society.Society.Inhabitants.First(person => person.HouseholdId == household).Id;
        using var world = Restore(state);
        var occupied = world.WorldSimulation.Buildings.SelectMany(building => WorldContentSimulationRules.Footprint(
            world.WorldContent.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId), building))
            .Concat(state.Map.Resources.Select(resource => resource.Position)).Concat(world.RoadTiles)
            .Concat(state.Map.CampObjects.Select(item => item.Position)).ToHashSet();
        var fertility = new LandFertility(state.Map, seed);
        var point = state.Map.Tiles.Select(tile => tile.Position).Where(point => fertility.CanFarm(point) &&
            !occupied.Contains(point) && !state.Inhabitants.Any(person => person.Position == point))
            .OrderBy(point => state.Map.FootDistance(farmhouse.Position, point)).First();
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "carried-hoe", "wooden_hoe", actor, 1);
        state = WithInventory(state, inventory) with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { Position = point, HungerBasisPoints = 10_000 } : person).ToArray(),
        };
        return (state, actor, household, point);
    }

    private static async Task<(PrivateWorldRuntimeState State, string Actor, string Household, GridPoint Point)> CompactFarmer(string seed)
    {
        // The crop cycle needs real field work and physical stock, but no distant
        // geography. Keep its full growth duration and every staged-work reload.
        using var setup = new PrivateWorldRuntime(seed, _ => new IdleProvider(), startPace: WorldStartPace.FounderSetup);
        GridPoint[] founders = [new(0, 0), new(1, 2), new(2, 2), new(3, 2)];
        for (var index = 0; index < founders.Length; index++)
            setup.PlaceFounder($"founder:{index + 1:D32}", founders[index]);
        setup.StartWorld();
        Assert.True(setup.StageStarterContent());
        await Advance(setup, 9);
        var state = setup.ExportState();
        var household = state.Society.Society.Households[0].Id;
        var actor = state.Society.Society.Inhabitants.First(person => person.HouseholdId == household).Id;
        var farmhouse = setup.WorldContent.Buildings.Single(definition => definition.LocalId == "farmhouse-1x1");
        var inventory = state.Society.Society.Inventory;
        foreach (var cost in farmhouse.BuildCosts)
            inventory = InventoryFixture.AddLot(inventory, "crop-cycle-building:" + cost.ResourceId,
                cost.ResourceId, household, cost.Amount);
        using var placing = Restore(WithInventory(state, inventory));
        var town = Assert.Single(placing.Towns);
        var placed = state.Map.Tiles.Select(tile => tile.Position)
            .Where(position => TownBorderRules.IsWithinOrAdjacent(town, position, 1, 1))
            .Select(position => placing.PlaceBuilding("crop-cycle-farmhouse", farmhouse.CanonicalId, position, household))
            .First(result => result.Applied);
        state = placing.ExportState();
        var occupied = placing.WorldSimulation.Buildings.SelectMany(building => WorldContentSimulationRules.Footprint(
            placing.WorldContent.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId), building))
            .Concat(state.Map.Resources.Select(resource => resource.Position)).Concat(placing.RoadTiles)
            .Concat(state.Map.CampObjects.Select(item => item.Position)).ToHashSet();
        var fertility = new LandFertility(state.Map, seed);
        var point = state.Map.Tiles.Select(tile => tile.Position).Where(position => fertility.CanFarm(position) &&
                !occupied.Contains(position) && !state.Inhabitants.Any(person => person.Position == position))
            .OrderByDescending(fertility.At).ThenBy(position => position.Y).ThenBy(position => position.X).First();
        inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "carried-hoe", "wooden_hoe", actor, 1);
        state = WithInventory(state, inventory) with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { Position = point, HungerBasisPoints = 10_000 } : person).ToArray(),
        };
        Assert.Equal(household, placing.WorldSimulation.Buildings.Single(building => building.InstanceId == placed.InstanceId).HouseholdId);
        return (state, actor, household, point);
    }

    internal static PrivateWorldRuntimeState WithInventory(PrivateWorldRuntimeState state, InventoryCheckpoint inventory) => state with
    {
        Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
    };
    internal static PrivateWorldRuntime Restore(PrivateWorldRuntimeState state) => PrivateWorldRuntime.Restore(state, _ => new IdleProvider());
    internal static IDecisionProvider HaulProvider() => new ChooseProvider("haul_farm_grain", "haul_household_stock");
    private static PrivateWorldRuntime Reload(PrivateWorldRuntime world) => Restore(PrivateWorldRuntimeCodec.Decode(
        PrivateWorldRuntimeCodec.Encode(world.ExportState())));
    private static async Task Advance(PrivateWorldRuntime world, int count)
    {
        for (var tick = 0; tick < count; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        world.Validate();
    }
    private sealed class IdleProvider : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default) =>
            new DeterministicDecisionProvider().DecideAsync(request with
            {
                Observation = request.Observation with { Candidates = [request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle")] },
            }, cancellationToken);
    }
    private sealed class ChooseProvider(params string[] ids) : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default) =>
            new DeterministicDecisionProvider().DecideAsync(request with
            {
                Observation = request.Observation with
                {
                    Candidates = [ids.Select(id => request.Observation.Candidates.FirstOrDefault(candidate => candidate.Id == id)).FirstOrDefault(candidate => candidate is not null)
                    ?? request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle")]
                },
            }, cancellationToken);
    }

    private sealed record FieldChoiceRequest(string InhabitantId, string[] CandidateIds);

    private sealed class RecordingFieldChoiceProvider(string actor, string candidateId) : IDecisionProvider
    {
        private readonly ConcurrentQueue<FieldChoiceRequest> requests = new();
        private readonly ConcurrentQueue<string> selected = new();

        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public FieldChoiceRequest[] Requests => requests.ToArray();
        public string[] SelectedCandidateIds => selected.ToArray();

        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            var observation = request.Observation;
            requests.Enqueue(new FieldChoiceRequest(observation.InhabitantId,
                observation.Candidates.Select(item => item.Id).ToArray()));
            var chosen = observation.InhabitantId == actor
                ? observation.Candidates.FirstOrDefault(item => item.Id == candidateId)
                : null;
            chosen ??= observation.Candidates.Single(item => item.Id == "safe_idle");
            selected.Enqueue(chosen.Id);
            var probabilities = observation.Candidates.ToDictionary(item => item.Id,
                item => item.Id == chosen.Id ? 1d : 0d, StringComparer.Ordinal);
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, observation.InhabitantId,
                Kind, ProviderEpoch, observation.RunEpoch, observation.DecisionGeneration,
                observation.ObservationDigest, chosen.Id, 1, probabilities));
        }
    }
}

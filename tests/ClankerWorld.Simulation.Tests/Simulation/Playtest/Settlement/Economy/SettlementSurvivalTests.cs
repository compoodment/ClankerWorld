using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.World;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class SettlementSurvivalTests
{
    [Fact]
    public async Task NamedReadyFoodKeepsItsSpoilageWhileDryCropsAndSeedsStayIntactAcrossReload()
    {
        var (state, actor, _, _) = FarmFieldTests.PreparedFarmer("named-food-spoilage");
        string[] readyFoods = ["food", "fruit", "berries", "wild_greens", "cultivated_greens"];
        string[] dryStock = ["grain", "grain_seed", "cultivated_green_seed", "orchard_seed"];
        var inventory = state.Society.Society.Inventory;
        foreach (var kind in readyFoods.Concat(dryStock))
            inventory = InventoryFixture.AddLot(inventory, "spoilage:" + kind, kind, actor, 1);
        using var first = FarmFieldTests.Restore(FarmFieldTests.WithInventory(state, inventory));
        for (var tick = 0; tick < 4; tick++) Assert.True((await first.AdvanceOneTickAsync()).Advanced);
        using var second = FarmFieldTests.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(first.ExportState())));
        for (var tick = 0; tick < 4; tick++) Assert.True((await second.AdvanceOneTickAsync()).Advanced);
        var generic = second.Society.Inventory.GetLot("spoilage:food").FreshnessBasisPoints;
        Assert.True(generic < 10_000);
        Assert.All(readyFoods, kind => Assert.Equal(generic, second.Society.Inventory.GetLot("spoilage:" + kind).FreshnessBasisPoints));
        Assert.All(dryStock, kind => Assert.Equal(10_000, second.Society.Inventory.GetLot("spoilage:" + kind).FreshnessBasisPoints));
        Assert.All(readyFoods.Concat(dryStock), kind => Assert.Equal(1, second.Society.Inventory.GetLot("spoilage:" + kind).Quantity));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task WorkerDeathRespectsProductionCompletionAndReleasesUnfinishedInputs(bool completionDue, bool completedBeforeDeath)
    {
        using var seed = new PrivateWorldRuntime("worker-death", _ => new IdleProvider());
        seed.StageStarterContent();
        for (var tick = 0; tick < 3; tick++) await seed.AdvanceOneTickAsync();
        var state = seed.ExportState();
        var worker = state.Inhabitants[0];
        var site = state.Map.Tiles.First(tile => state.Map.IsPassable(tile.Position) &&
                !state.Map.CampObjects.Any(item => item.Position == tile.Position) &&
                !state.Map.Resources.Any(item => item.Position == tile.Position) &&
                !state.Inhabitants.Any(person => person.InhabitantId != worker.InhabitantId && person.Position == tile.Position)).Position;
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == worker.InhabitantId ? person with { Position = site }
                : person.Position == site ? person with { Position = worker.Position } : person).ToArray(),
            Society = state.Society with
            {
                Society = state.Society.Society with
                {
                    Inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "death-seeds", "seed", "household:camp-alpha", 2),
                },
            },
        };
        using var preparing = PrivateWorldRuntime.Restore(state, _ => new IdleProvider());
        var recipe = preparing.WorldContent.Recipes.Single(item => item.LocalId == "meal");
        var fire = preparing.WorldContent.Buildings.Single(building => building.LocalId == "fire");
        var placement = preparing.PlaceBuilding("death-test-fire", fire.CanonicalId, site);
        Assert.True(placement.Applied, placement.Failure);
        var workstation = placement.InstanceId;
        var started = preparing.StartProduction(recipe.CanonicalId, workstation, worker.InhabitantId);
        Assert.True(started.Applied, started.Failure);
        if (completionDue)
            for (var tick = 1; tick < recipe.DurationTicks; tick++) await preparing.AdvanceOneTickAsync();
        if (completedBeforeDeath) await preparing.AdvanceOneTickAsync();
        state = preparing.ExportState();
        var practice = state.Inhabitants.Single(person => person.InhabitantId == worker.InhabitantId).Proficiency;
        if (completedBeforeDeath)
        {
            Assert.Equal(new SettlementProficiency(Crafting: 1), practice);
            using var proof = PrivateWorldRuntime.Restore(state, _ => new IdleProvider());
            await proof.AdvanceOneTickAsync();
            Assert.Equal(practice, proof.Inhabitants.Single(person => person.InhabitantId == worker.InhabitantId).Proficiency);
        }
        else Assert.Null(practice);
        var produced = state.Society.Society.Inventory.Lots.Where(lot => lot.Id.StartsWith(started.JobId + ":output:", StringComparison.Ordinal))
            .Select(lot => (lot.Id, lot.Quantity)).ToArray();
        using var society = SocietyWorldRuntime.Restore(state.Society);
        society.Apply(checkpoint => SocietyFixture.Kill(checkpoint, worker.InhabitantId, SocietyDeathCause.Accident, checkpoint.WorldTick));
        state = state with
        {
            Society = society.ExportState() with
            {
                Society = society.Checkpoint with
                {
                    Inhabitants = society.Checkpoint.Inhabitants.Select(person => person with { Name = "sk-private-production-test" }).ToArray(),
                },
            },
            Inhabitants = state.Inhabitants.Where(person => person.InhabitantId != worker.InhabitantId).ToArray(),
        };
        using var world = PrivateWorldRuntime.Restore(state, _ => new IdleProvider());
        var messages = await AdvanceProductionWithLogs(world);
        var job = world.WorldSimulation.ProductionJobs.Concat(world.WorldSimulation.CropBuilds ?? []).Single(item => item.JobId == started.JobId);
        var expectedState = completedBeforeDeath ? WorldProductionJobState.Completed : WorldProductionJobState.Cancelled;
        Assert.Equal(expectedState, job.State);
        Assert.NotEmpty(job.InputReservationIds);
        Assert.All(job.InputReservationIds, id => Assert.Equal(completedBeforeDeath ? InventoryReservationState.Completed : InventoryReservationState.Released,
            world.Society.Inventory.GetReservation(id).State));
        Assert.Equal(produced, world.Society.Inventory.Lots.Where(lot => lot.Id.StartsWith(job.JobId + ":output:", StringComparison.Ordinal))
            .Select(lot => (lot.Id, lot.Quantity)).ToArray());
        Assert.DoesNotContain(messages, message => message.Contains("sk-private-production-test", StringComparison.Ordinal));
        if (!completedBeforeDeath)
        {
            Assert.Contains(world.ExportState().Events, item => item.Kind == "production_worker_unavailable" && item.Detail == job.JobId);
            Assert.Contains(messages, message => message.Contains("production_cancelled", StringComparison.Ordinal) &&
                message.Contains("job=" + job.JobId, StringComparison.Ordinal) && message.Contains("reason=worker_unavailable", StringComparison.Ordinal));
        }
        else Assert.DoesNotContain(messages, message => message.Contains("production_cancelled", StringComparison.Ordinal));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        world.Pause();
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), _ => new IdleProvider());
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        restored.Resume();
        await restored.AdvanceOneTickAsync();
        Assert.Equal(expectedState,
            restored.WorldSimulation.ProductionJobs.Concat(restored.WorldSimulation.CropBuilds ?? []).Single(item => item.JobId == job.JobId).State);
    }

    private static async Task<IReadOnlyList<string>> AdvanceProductionWithLogs(PrivateWorldRuntime world)
    {
        var directory = Directory.CreateTempSubdirectory("production-death-");
        try
        {
            var presence = new OwnerClientPresenceLease(TimeSpan.FromSeconds(30));
            presence.RecordAuthenticatedReconnect("owner");
            var logger = new RecordingLogger<PrivateWorldRuntimeService>();
            using var service = new PrivateWorldRuntimeService(world, new PrivateWorldStateFile(Path.Combine(directory.FullName, "world.json")), presence, logger);
            Assert.True(await service.TryAdvanceOnceAsync());
            return logger.Messages.ToArray();
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task UrgentColdStillAllowsProtectiveConstruction()
    {
        using var seed = new PrivateWorldRuntime("cold-bootstrap", _ => new IdleProvider());
        seed.StageStarterContent();
        for (var tick = 0; tick < 3; tick++) await seed.AdvanceOneTickAsync();
        var state = SettlementWeatherTestFixture.WithWeather(seed.ExportState(), WeatherKind.Snow);
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person with
            { Survival = person.Survival! with { WarmthBasisPoints = 1_000 } }).ToArray()
        };
        using var world = PrivateWorldRuntime.Restore(state);
        for (var tick = 0; tick < 80; tick++) await world.AdvanceOneTickAsync();
        Assert.Contains(world.WorldSimulation.Buildings, building => world.WorldContent.Buildings.Any(definition =>
            definition.CanonicalId == building.DefinitionId && definition.Tags.Any(tag => tag is "shelter" or "cooking" or "warmth")));
        Assert.Contains(world.ExportState().Events, item => item.Kind == "fire_fuelled");
    }

    [Fact]
    public async Task SpoiledReservedIngredientCancelsProductionWithoutStoppingTicks()
    {
        using var seed = new PrivateWorldRuntime("spoiled-production", _ => new IdleProvider());
        seed.StageStarterContent();
        for (var tick = 0; tick < 3; tick++) await seed.AdvanceOneTickAsync();
        var state = seed.ExportState();
        var worker = state.Inhabitants[0];
        var position = state.Map.Tiles.First(tile => state.Map.IsPassable(tile.Position) &&
            !state.Map.CampObjects.Any(item => item.Position == tile.Position) && !state.Map.Resources.Any(item => item.Position == tile.Position) &&
            !state.Inhabitants.Any(person => person.InhabitantId != worker.InhabitantId && person.Position == tile.Position)).Position;
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == worker.InhabitantId
            ? person with { Position = position } : person).ToArray()
        };
        using var preparing = PrivateWorldRuntime.Restore(state, _ => new IdleProvider());
        var fire = preparing.WorldContent.Buildings.Single(building => building.LocalId == "fire");
        var placed = preparing.PlaceBuilding("spoilage-test-fire", fire.CanonicalId, position);
        Assert.True(placed.Applied, placed.Failure);
        var recipe = preparing.WorldContent.Recipes.Single(recipe => recipe.LocalId == "meal");
        var started = preparing.StartProduction(recipe.CanonicalId, placed.InstanceId, worker.InhabitantId);
        Assert.True(started.Applied, started.Failure);
        var pending = preparing.ExportState();
        pending = pending with
        {
            Society = pending.Society with
            {
                Society = pending.Society.Society with
                {
                    Inventory = pending.Society.Society.Inventory with
                    {
                        Lots = pending.Society.Society.Inventory.Lots.Select(lot =>
                    lot.ItemKind == "food" ? lot with { FreshnessBasisPoints = 0 } : lot).ToArray()
                    }
                }
            }
        };
        using var world = PrivateWorldRuntime.Restore(pending, _ => new IdleProvider());
        for (var tick = 0; tick <= recipe.DurationTicks; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(WorldProductionJobState.Cancelled, world.WorldSimulation.ProductionJobs.Single(job => job.JobId == started.JobId).State);
        Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.Id.StartsWith(started.JobId + ":output:", StringComparison.Ordinal));
        Assert.Contains(world.ExportState().Events, item => item.Kind == "production_input_unusable");
        Assert.Null(world.Inhabitants.Single(person => person.InhabitantId == worker.InhabitantId).Proficiency);
    }

    [Theory]
    [InlineData(WeatherKind.Clear, false)]
    [InlineData(WeatherKind.Rain, false)]
    [InlineData(WeatherKind.Snow, true)]
    public async Task CropFoodYieldReflectsWeather(WeatherKind weather, bool snow)
    {
        var (state, actor, household, point) = await FarmFieldTests.ReadyFarmer("crop-weather");
        state = SettlementWeatherTestFixture.WithWeather(state, weather);
        var fertility = new LandFertility(state.Map, state.WorldSeed).At(point);
        var rawYield = 4 + fertility / 25;
        using var world = FarmFieldTests.Restore(state);
        Assert.True(world.StartFieldWork(actor, point, FarmWorkKind.Harvest).Accepted);
        for (var tick = 0; tick < 4; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var moisture = WeatherRules.SoilMoistureAt(world.WorldSystems, point, state.Map.Height, WeatherRules.RegionClimate(state.Map, point));
        var expected = snow ? rawYield / 2 : moisture < 15 ? rawYield * 3 / 4 : moisture >= 50 ? rawYield + rawYield / 4 : rawYield;
        var harvest = Assert.Single(world.Society.Inventory.Lots, lot => lot.ItemKind == "grain" && lot.Id.StartsWith("field-", StringComparison.Ordinal));
        Assert.Equal(expected, harvest.Quantity);
        Assert.Equal(household, harvest.OwnerId);
        Assert.Equal(new InventoryGroundPosition(point.X, point.Y), harvest.GroundPosition);
        if (snow) Assert.Contains(world.ExportState().Events, item => item.Kind == "crop_weather_loss" && item.Detail.EndsWith(":snow", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DryStreakReducesCompletedCropYieldAndRecordsCauseAcrossRestart()
    {
        var (state, actor, _, point) = await FarmFieldTests.ReadyFarmer("crop-dry-streak");
        state = SettlementWeatherTestFixture.WithWeather(state, WeatherKind.Clear);
        using var drying = FarmFieldTests.Restore(state);
        var ticksThroughTwoDays = drying.WorldSystems.Config.TicksPerDay * 2;
        while (drying.WorldTick < ticksThroughTwoDays)
            Assert.True((await drying.AdvanceOneTickAsync()).Advanced);
        state = drying.ExportState();
        Assert.InRange(WeatherRules.SoilMoistureAt(state.WorldSystems!, point, state.Map.Height), 0, 14);
        var rawYield = 4 + new LandFertility(state.Map, state.WorldSeed).At(point) / 25;
        using var world = FarmFieldTests.Restore(state);
        Assert.True(world.StartFieldWork(actor, point, FarmWorkKind.Harvest).Accepted);
        for (var tick = 0; tick < 2; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        using var resumed = FarmFieldTests.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        for (var tick = 0; tick < 2; tick++) Assert.True((await resumed.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(rawYield * 3 / 4, Assert.Single(resumed.Society.Inventory.Lots, lot => lot.ItemKind == "grain" && lot.Id.StartsWith("field-", StringComparison.Ordinal)).Quantity);
        Assert.Contains(resumed.ExportState().Events, item => item.Kind == "crop_moisture_effect" && item.Detail.Contains(":dry:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ColdSettlementUsesFuelAndEquipmentAndCanRecoverAcrossRestart()
    {
        using var seed = new PrivateWorldRuntime("cold-settlement");
        var initial = SettlementWeatherTestFixture.WithWeather(seed.ExportState(), WeatherKind.Snow);
        using var world = PrivateWorldRuntime.Restore(initial);
        world.StageStarterContent();
        // Exercise autonomous survival separately from the finite Tailor
        // production/equipment chains in PersonalEquipmentTests.
        for (var tick = 0; tick < 900; tick++)
        {
            await world.AdvanceOneTickAsync();
        }
        var state = world.ExportState();
        Assert.NotNull(state.Survival);
        Assert.Contains(state.Events, item => item.Kind == "fire_fuelled");
        Assert.Contains(state.Events, item => item.Kind == "equipment_collected" && item.Detail.EndsWith(":tool", StringComparison.Ordinal));
        Assert.Contains(state.Events, item => item.Kind == "survival_condition_changed");
        Assert.All(new OwnerWorldObservationStore(world).GetSnapshot().Inhabitants, person => Assert.NotNull(person.Survival));
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)));
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(state), PrivateWorldRuntimeCodec.Encode(restored.ExportState()));

        // A live heater plus access to it is the causal recovery condition; one
        // final-position snapshot after autonomous choices is not that condition.
        var heater = world.WorldSimulation.Buildings.First(building => world.WorldContent.Buildings.Any(definition =>
            definition.CanonicalId == building.DefinitionId &&
            definition.Tags.Any(tag => tag is "cooking" or "warmth") &&
            (building.HouseholdId is null || state.Society.Society.Inhabitants.Any(inhabitant =>
                inhabitant.HouseholdId == building.HouseholdId))));
        var recoveringId = state.Society.Society.Inhabitants.First(inhabitant =>
            heater.HouseholdId is null || inhabitant.HouseholdId == heater.HouseholdId).Id;
        var recoveringPosition = state.Inhabitants.Single(person => person.InhabitantId == recoveringId).Position;
        var recoveryState = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == recoveringId
                ? person with
                {
                    Position = heater.Position,
                    HungerBasisPoints = 9_000,
                    Survival = person.Survival! with { WarmthBasisPoints = 0 },
                    // Only the heater is under test: no unfinished outing or
                    // project may walk the agent away from it.
                    Project = null,
                    Exploration = null,
                }
                : person.Position == heater.Position ? person with { Position = recoveringPosition } : person).ToArray(),
            Society = state.Society with
            {
                Society = state.Society.Society with
                {
                    Inventory = InventoryFixture.AddLot(state.Society.Society.Inventory,
                        "recovery-coat", "clothing", recoveringId, 1),
                },
            },
            Survival = state.Survival! with { Fires = [new CampFireState(heater.InstanceId, world.WorldTick + 120)] },
        };
        using var recovering = PrivateWorldRuntime.Restore(
            PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(recoveryState)), _ => new IdleProvider());
        for (var tick = 0; tick < 110; tick++)
            Assert.True((await recovering.AdvanceOneTickAsync()).Advanced);
        Assert.True(recovering.Inhabitants.Single(person => person.InhabitantId == recoveringId)
            .Survival!.WarmthBasisPoints > 6_000);
    }

    [Fact]
    public async Task ExposureIllnessRecoversWithWarmthAndFood()
    {
        using var seed = new PrivateWorldRuntime("illness-recovery", _ => new IdleProvider());
        seed.StageStarterContent();
        for (var tick = 0; tick < 3; tick++)
        {
            await seed.AdvanceOneTickAsync();
        }
        var original = SettlementWeatherTestFixture.WithWeather(seed.ExportState(), WeatherKind.Clear);
        var sick = original with
        {
            Inhabitants = original.Inhabitants.Select(person => person with
            {
                Survival = new SurvivalCondition(7_000, 5_000),
            }).ToArray()
        };
        using var world = PrivateWorldRuntime.Restore(sick, _ => new IdleProvider());
        for (var tick = 0; tick < 100; tick++)
        {
            await world.AdvanceOneTickAsync();
        }
        Assert.All(world.Inhabitants, person =>
        {
            Assert.True(person.Survival!.IllnessBasisPoints < 5_000);
            Assert.True(person.Survival.WarmthBasisPoints > 7_000);
        });
        world.Pause();
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
    }

    [Fact]
    public async Task IllnessSlowsProjectWorkWithoutPausingThePlan()
    {
        var healthyProgress = await ProjectProgressAtIllness(0);
        var severelyIllProgress = await ProjectProgressAtIllness(9_000);

        Assert.True(healthyProgress > severelyIllProgress,
            $"Expected severe illness to slow project work; healthy progress {healthyProgress}, ill progress {severelyIllProgress}.");
        Assert.True(severelyIllProgress > 0, "Severe illness should leave some project work opportunities.");
    }

    [Fact]
    public async Task IllnessAddsTravelDelayWithoutRemovingExploration()
    {
        var healthySteps = await ExplorationStepsAtIllness(0);
        var severelyIllSteps = await ExplorationStepsAtIllness(9_000);

        Assert.True(healthySteps > severelyIllSteps,
            $"Expected severe illness to add travel delay; healthy steps {healthySteps}, ill steps {severelyIllSteps}.");
        Assert.True(severelyIllSteps > 0, "Illness should slow travel rather than remove the exploration choice.");
    }

    [Fact]
    public async Task IllnessTelemetryReportsItsBoundedEffectsWithoutPrivateText()
    {
        var directory = Directory.CreateTempSubdirectory("illness-logs-");
        try
        {
            using var seed = new PrivateWorldRuntime("illness-telemetry", _ => new IdleProvider());
            seed.StageStarterContent();
            for (var tick = 0; tick < 3; tick++) await seed.AdvanceOneTickAsync();
            var state = SettlementWeatherTestFixture.WithWeather(seed.ExportState(), WeatherKind.Clear);
            var actor = state.Inhabitants[0];
            state = state with
            {
                Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor.InhabitantId ? person with
                {
                    HungerBasisPoints = 9_000,
                    Personality = "private-illness-secret",
                    Survival = new SurvivalCondition(10_000, 7_504),
                } : person).ToArray(),
            };
            using var world = PrivateWorldRuntime.Restore(state, _ => new IdleProvider());
            var presence = new OwnerClientPresenceLease(TimeSpan.FromSeconds(30));
            presence.RecordAuthenticatedReconnect("owner");
            var logger = new RecordingLogger<PrivateWorldRuntimeService>();
            using var service = new PrivateWorldRuntimeService(world,
                new PrivateWorldStateFile(Path.Combine(directory.FullName, "world.json")), presence, logger);

            Assert.True(await service.TryAdvanceOnceAsync());

            Assert.Contains(logger.Messages, message => message.Contains("survival_condition", StringComparison.Ordinal) &&
                message.Contains("inhabitant=" + actor.InhabitantId + " ", StringComparison.Ordinal) &&
                message.Contains("illness_work_percent=50", StringComparison.Ordinal) &&
                message.Contains("illness_travel_delay=1", StringComparison.Ordinal));
            Assert.DoesNotContain(logger.Messages, message => message.Contains("private-illness-secret", StringComparison.Ordinal));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static async Task<int> ProjectProgressAtIllness(int illnessBasisPoints)
    {
        using var seed = new PrivateWorldRuntime("illness-work", _ => new IdleProvider());
        seed.StageStarterContent();
        for (var tick = 0; tick < 3; tick++) await seed.AdvanceOneTickAsync();
        var state = SettlementWeatherTestFixture.WithWeather(seed.ExportState(), WeatherKind.Clear);
        var worker = state.Inhabitants[0];
        var position = state.Map.Tiles.First(tile => state.Map.IsPassable(tile.Position) &&
            !state.Map.CampObjects.Any(item => item.Position == tile.Position) &&
            !state.Map.Resources.Any(item => item.Position == tile.Position) &&
            !state.Inhabitants.Any(person => person.Position == tile.Position)).Position;
        var building = seed.WorldContent.Buildings.Single(item => item.LocalId == "shelter");
        var inventory = state.Society.Society.Inventory with
        {
            Lots = state.Society.Society.Inventory.Lots.Where(lot => lot.ItemKind != "tool").ToArray(),
        };
        inventory = InventoryFixture.AddLot(inventory,
            $"illness-work-wood:{worker.InhabitantId}", "wood", worker.InhabitantId, 8);
        state = state with
        {
            Society = state.Society with
            {
                Society = state.Society.Society with { Inventory = inventory },
            },
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == worker.InhabitantId ? person with
            {
                Position = position,
                HungerBasisPoints = 9_000,
                Survival = new SurvivalCondition(10_000, illnessBasisPoints),
                Proficiency = null,
                Project = new("build:building:" + building.CanonicalId, building.DisplayName, seed.WorldTick,
                    "working", LastTransitionTick: seed.WorldTick),
            } : person).ToArray(),
        };
        using var world = PrivateWorldRuntime.Restore(state, _ => new IdleProvider());
        for (var tick = 0; tick < 8; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var progress = world.Inhabitants.Single(person => person.InhabitantId == worker.InhabitantId).Project!.WorkDone;
        Assert.Equal("working", world.Inhabitants.Single(person => person.InhabitantId == worker.InhabitantId).Project!.Stage);
        return progress;
    }

    private static async Task<int> ExplorationStepsAtIllness(int illnessBasisPoints)
    {
        using var seed = new PrivateWorldRuntime("illness-travel", _ => new ExplorationProvider());
        seed.StageStarterContent();
        for (var tick = 0; tick < 3; tick++) await seed.AdvanceOneTickAsync();
        var state = SettlementWeatherTestFixture.WithWeather(seed.ExportState(), WeatherKind.Clear);
        var scout = state.Inhabitants[0];
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == scout.InhabitantId ? person with
            {
                HungerBasisPoints = 9_000,
                Survival = new SurvivalCondition(10_000, illnessBasisPoints),
                Exploration = new SettlementExploration([person.Position], [person.Position], seed.WorldTick, false),
            } : person).ToArray(),
        };
        using var world = PrivateWorldRuntime.Restore(state, _ => new ExplorationProvider());
        for (var tick = 0; tick < 8; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        return world.ExportState().Events.Count(item => item.Kind == "inhabitant_moved" &&
            item.Detail.StartsWith(scout.InhabitantId + ":", StringComparison.Ordinal));
    }

    private sealed class IdleProvider : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, request.Observation.InhabitantId,
                Kind, ProviderEpoch, request.Observation.RunEpoch, request.Observation.DecisionGeneration,
                request.Observation.ObservationDigest, "safe_idle", 1,
                request.Observation.Candidates.ToDictionary(candidate => candidate.Id, candidate => candidate.Id == "safe_idle" ? 1d : 0d)));
    }

    private sealed class ExplorationProvider : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            var selected = request.Observation.Candidates.FirstOrDefault(candidate => candidate.Id == "explore") ??
                request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle");
            return new DeterministicDecisionProvider().DecideAsync(request with
            {
                Observation = request.Observation with { Candidates = [selected] },
            }, cancellationToken);
        }
    }
}

using System.Text.Json;
using System.Text.Json.Nodes;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class FieldReturnsToGrassTests
{
    [Fact]
    public async Task AFieldNobodyWorksForAFullSeasonReturnsToGrassWhileAWorkedFieldStays()
    {
        var (state, actor, household, idle, worked) = TwoFields("field-returns-to-grass");
        var season = FarmFieldRules.IdleTicksBeforeGrass(state.WorldSystems!.Config);
        var fertility = new LandFertility(state.Map, "field-returns-to-grass").At(idle);
        using var world = FarmFieldTests.Restore(state);
        var start = world.WorldTick;

        // Halfway through the season someone works the second field, which restarts its clock.
        await AdvanceTo(world, start + season / 2);
        var halfway = world.ExportState();
        halfway = FarmFieldTests.WithInventory(halfway, InventoryFixture.AddLot(halfway.Society.Society.Inventory,
            "worked-field-seed", FarmFieldRules.GrainSeed, actor, 1)) with
        {
            Inhabitants = halfway.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { Position = worked, LastDecisionContext = null } : person).ToArray(),
        };
        var reloaded = FarmFieldTests.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(halfway)));
        try
        {
            Assert.True(reloaded.StartFieldWork(actor, worked, FarmWorkKind.Plant, FarmFieldRules.Grain, "worked-field-seed").Accepted);
            var started = reloaded.Fields.Single(field => field.Position == worked);
            await AdvanceTo(reloaded, reloaded.WorldTick + 1);
            var stroke = reloaded.Fields.Single(field => field.Position == worked);
            Assert.True(stroke.Work!.RemainingTicks < started.Work!.RemainingTicks);
            Assert.Equal(reloaded.WorldTick, stroke.LastWorkedTick);
            Assert.Equal(stroke.LastWorkedTick, stroke.Work.LastWorkedTick);
            await FinishWork(reloaded, worked);
            var lastWorked = reloaded.Fields.Single(field => field.Position == worked).LastWorkedTick;
            Assert.Equal(start, reloaded.Fields.Single(field => field.Position == idle).LastWorkedTick);
            await AdvanceTo(reloaded, start + season - 1);
            Assert.Equal(2, reloaded.Fields.Count);
            using var replay = FarmFieldTests.Restore(PrivateWorldRuntimeCodec.Decode(
                PrivateWorldRuntimeCodec.Encode(reloaded.ExportState())));
            await AdvanceTo(reloaded, start + season + 1);
            await AdvanceTo(replay, start + season + 1);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(reloaded.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
            var remaining = Assert.Single(reloaded.Fields);
            Assert.Equal(worked, remaining.Position);
            var events = reloaded.ExportState().Events.Where(item => item.Kind == "field_returned_to_grass").ToArray();
            Assert.Equal($"{FarmFieldRules.FieldId(idle)}:{household}", Assert.Single(events).Detail);
            // The seed kept back for replanting is free again, and the land keeps its fertility.
            Assert.Contains(reloaded.Society.Inventory.Reservations, reservation => reservation.Id == "grass-test:replant" &&
                reservation.State != InventoryReservationState.Reserved);
            Assert.Equal(fertility, new LandFertility(reloaded.ExportState().Map, "field-returns-to-grass").At(idle));
            Assert.DoesNotContain(new OwnerWorldObservationStore(reloaded).GetSnapshot().Fields!, field =>
                field.Position.X == idle.X && field.Position.Y == idle.Y);

            // The worked field goes too once a full season passes without work.
            Assert.NotNull(remaining.Crop);
            await AdvanceTo(reloaded, lastWorked + season + 1);
            Assert.Empty(reloaded.Fields);

            var bytes = PrivateWorldRuntimeCodec.Encode(reloaded.ExportState());
            using var restored = FarmFieldTests.Restore(PrivateWorldRuntimeCodec.Decode(bytes));
            Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        }
        finally { reloaded.Dispose(); }
    }

    [Fact]
    public async Task FieldWorkRecordsWhenTheFieldWasLastWorkedAndSavesIt()
    {
        var (state, actor, _, point) = FarmFieldTests.PreparedFarmer("field-last-worked");
        state = ShortDays(state, 1);
        state = state with
        {
            Society = state.Society with
            {
                Society = state.Society.Society with
                {
                    Config = state.Society.Society.Config with { DaysPerWorldYear = 4 },
                },
            },
            WorldSystems = RegionalWeatherRules.Initialize(state.WorldSystems! with
            {
                Config = state.WorldSystems!.Config with
                {
                    DaysPerYear = 4,
                    SpringDays = 1,
                    SummerDays = 1,
                    AutumnDays = 1,
                    WinterDays = 1,
                },
                RegionalWeather = null,
            }, state.Map),
        };
        using var world = FarmFieldTests.Restore(state);
        Assert.True(world.StartFieldWork(actor, point, FarmWorkKind.Till).Accepted);
        var field = Assert.Single(world.Fields);
        Assert.Equal(world.WorldTick, field.LastWorkedTick);
        var started = field.LastWorkedTick;
        await AdvanceTo(world, world.WorldTick + 1);
        field = Assert.Single(world.Fields);
        Assert.True(world.WorldTick - started >= FarmFieldRules.IdleTicksBeforeGrass(state.WorldSystems.Config));
        Assert.NotNull(field.Work);
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "field_returned_to_grass");
        Assert.Equal(world.WorldTick, field.LastWorkedTick);
        Assert.Equal(field.LastWorkedTick, field.Work!.LastWorkedTick);
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = FarmFieldTests.Restore(PrivateWorldRuntimeCodec.Decode(bytes));
        Assert.Equal(field, Assert.Single(restored.Fields));

        var future = world.ExportState();
        Assert.Throws<InvalidDataException>(() => FarmFieldTests.Restore(future with
        {
            Fields = [field with { LastWorkedTick = future.Society.Society.WorldTick + 1 }],
        }));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CurrentFieldCheckpointRefusesMissingOrContradictoryWorkedTicks(bool missing)
    {
        var (state, actor, _, point) = FarmFieldTests.PreparedFarmer("field-required-worked-clock");
        using var world = FarmFieldTests.Restore(state);
        Assert.True(world.StartFieldWork(actor, point, FarmWorkKind.Till).Accepted);
        await AdvanceTo(world, world.WorldTick + 1);
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        var document = JsonNode.Parse(bytes)!;
        var field = Assert.Single(document["state"]!["fields"]!.AsArray())!.AsObject();
        if (missing) Assert.True(field.Remove("lastWorkedTick"));
        else field["lastWorkedTick"] = world.WorldTick - 1;
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Decode(JsonSerializer.SerializeToUtf8Bytes(document)));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        using var restored = FarmFieldTests.Restore(PrivateWorldRuntimeCodec.Decode(bytes));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AbandonedFieldCanBeRetilledAndHarvestedWithItsPriorInventoryHistory(bool consumeOldLots)
    {
        var (state, actor, household, point) = FarmFieldTests.PreparedFarmer("field-retill-history");
        state = ShortDays(state, 2);
        state = FarmFieldTests.WithInventory(state, InventoryFixture.AddLot(state.Society.Society.Inventory,
            "retill-carried-seed", FarmFieldRules.GrainSeed, actor, 2));
        using var first = FarmFieldTests.Restore(state);
        await CropCycle(first, actor, point, "retill-carried-seed");
        var harvested = Assert.Single(first.Fields);
        var oldReserve = first.Society.Inventory.GetReservation(harvested.ReplantingReservationId!);
        var oldLots = first.Society.Inventory.Lots.Where(lot => lot.Id.StartsWith(
            FarmFieldRules.FieldId(point) + ":harvest:", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, oldLots.Length);
        var fertility = new LandFertility(first.ExportState().Map, state.WorldSeed).At(point);
        await AdvanceTo(first, harvested.LastWorkedTick + FarmFieldRules.IdleTicksBeforeGrass(state.WorldSystems!.Config));
        Assert.Empty(first.Fields);
        Assert.Equal(InventoryReservationState.Released, first.Society.Inventory.GetReservation(oldReserve.Id).State);
        Assert.Equal(oldLots.Select(lot => (lot.Id, lot.Quantity, lot.OwnerId, lot.GroundPosition)),
            oldLots.Select(lot => first.Society.Inventory.GetLot(lot.Id)).Select(lot => (lot.Id, lot.Quantity, lot.OwnerId, lot.GroundPosition)));
        Assert.Equal(fertility, new LandFertility(first.ExportState().Map, state.WorldSeed).At(point));
        Assert.Single(first.ExportState().Events, item => item.Kind == "field_returned_to_grass");
        var abandoned = first.ExportState();
        // The idle interval also drains unrelated survival needs. Supply the
        // second work fixture a healthy farmer; keep the actual clock, terrain,
        // abandoned field and complete first-harvest inventory history.
        abandoned = abandoned with
        {
            Inhabitants = abandoned.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with
                {
                    HungerBasisPoints = 10_000,
                    Survival = (person.Survival ?? new SurvivalCondition()) with
                    { WarmthBasisPoints = 10_000, NutritionBasisPoints = 10_000, IllnessBasisPoints = 0 },
                } : person).ToArray(),
        };
        // The first crop also wore the wooden hoe. A fresh, better carried
        // tool lets this control reach the second harvest rather than stop
        // correctly when the old tool breaks during retilling.
        abandoned = FarmFieldTests.WithInventory(abandoned, InventoryFixture.AddLot(abandoned.Society.Society.Inventory,
            "retill-fresh-hoe", "iron_hoe", actor, 1));
        if (consumeOldLots)
        {
            var inventory = abandoned.Society.Society.Inventory;
            foreach (var lot in oldLots)
            {
                var id = "used-old-harvest:" + lot.Id;
                inventory = InventoryFixture.ConsumeReservation(InventoryFixture.Reserve(inventory, id,
                    household, lot.Id, lot.Quantity, "used_old_harvest", long.MaxValue), id);
            }
            abandoned = FarmFieldTests.WithInventory(abandoned, inventory);
        }
        using var retilled = FarmFieldTests.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(abandoned)));
        Assert.Equal("retill-fresh-hoe", ToolProgressionRules.PlanWork(retilled.Society.Inventory, actor, ToolFamily.Hoe)!.ToolLotId);
        await CropCycle(retilled, actor, point, "retill-carried-seed");
        var second = Assert.Single(retilled.Fields);
        Assert.Equal(FarmFieldStage.Harvested, second.Stage);
        Assert.NotEqual(oldReserve.Id, second.ReplantingReservationId);
        Assert.Equal(InventoryReservationState.Released, retilled.Society.Inventory.GetReservation(oldReserve.Id).State);
        Assert.Equal(InventoryReservationState.Reserved, retilled.Society.Inventory.GetReservation(second.ReplantingReservationId!).State);
        if (!consumeOldLots)
            Assert.Equal(oldLots.Select(lot => (lot.Id, lot.Quantity, lot.OwnerId, lot.GroundPosition)),
                oldLots.Select(lot => retilled.Society.Inventory.GetLot(lot.Id)).Select(lot => (lot.Id, lot.Quantity, lot.OwnerId, lot.GroundPosition)));
        var bytes = PrivateWorldRuntimeCodec.Encode(retilled.ExportState());
        using var restored = FarmFieldTests.Restore(PrivateWorldRuntimeCodec.Decode(bytes));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }

    private static async Task CropCycle(PrivateWorldRuntime world, string actor, GridPoint point, string seed)
    {
        Assert.True(world.StartFieldWork(actor, point, FarmWorkKind.Till).Accepted);
        await FinishWork(world, point);
        Assert.True(world.StartFieldWork(actor, point, FarmWorkKind.Plant, FarmFieldRules.Grain, seed).Accepted);
        await FinishWork(world, point);
        await AdvanceTo(world, world.WorldTick + 1);
        Assert.True(world.StartFieldWork(actor, point, FarmWorkKind.Tend).Accepted);
        await FinishWork(world, point);
        await AdvanceTo(world, Math.Max(world.WorldTick + 1, Assert.Single(world.Fields).ReadyTick));
        Assert.True(world.StartFieldWork(actor, point, FarmWorkKind.Harvest).Accepted);
        await FinishWork(world, point);
    }

    private static async Task FinishWork(PrivateWorldRuntime world, GridPoint point)
    {
        for (var tick = 0; tick < 12 && world.Fields.SingleOrDefault(field => field.Position == point)?.Work is not null; tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Contains(world.Fields, field => field.Position == point);
        Assert.Null(world.Fields.Single(field => field.Position == point).Work);
        world.Validate();
    }

    /// <summary>
    /// A generated world with short days, so a season passes in a few hundred
    /// ticks, and two of the farming household's fields: a prepared one and a
    /// harvested one holding seed back for replanting, both last worked now.
    /// </summary>
    private static (PrivateWorldRuntimeState State, string Actor, string Household, GridPoint Idle, GridPoint Worked) TwoFields(string seed)
    {
        var (state, actor, household, idle) = FarmFieldTests.PreparedFarmer(seed);
        state = ShortDays(state, 2);
        var fertility = new LandFertility(state.Map, seed);
        GridPoint worked;
        using (var probe = FarmFieldTests.Restore(state))
        {
            // A harvested field needs a harvest time behind it.
            probe.AdvanceOneTickAsync().AsTask().GetAwaiter().GetResult();
            probe.AdvanceOneTickAsync().AsTask().GetAwaiter().GetResult();
            state = probe.ExportState();
            var occupied = probe.WorldSimulation.Buildings.SelectMany(building => WorldContentSimulationRules.Footprint(
                    probe.WorldContent.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId), building))
                .Concat(state.Map.Resources.Select(resource => resource.Position)).Concat(probe.RoadTiles)
                .Concat(state.Map.CampObjects.Select(item => item.Position)).ToHashSet();
            worked = state.Map.Tiles.Select(tile => tile.Position)
                .Where(point => point != idle && fertility.CanFarm(point) && !occupied.Contains(point))
                .OrderBy(point => state.Map.FootDistance(idle, point)).First();
        }
        var tick = state.Society.Society.WorldTick;
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "grass-test:seed", "grain_seed", household, 1);
        inventory = InventoryFixture.Reserve(inventory, "grass-test:replant", household, "grass-test:seed", 1, "field_replanting", long.MaxValue);
        state = FarmFieldTests.WithInventory(state, inventory) with
        {
            Fields = new[]
            {
                new FarmFieldState(idle, household, FarmFieldStage.Harvested, "grain", 0, 1, Tended: true, Cycle: 1,
                    ReplantingReservationId: "grass-test:replant", LastWorkedTick: tick),
                new FarmFieldState(worked, household, FarmFieldStage.Prepared, LastWorkedTick: tick),
            }.OrderBy(field => field.Position.Y).ThenBy(field => field.Position.X).ToArray(),
        };
        return (state, actor, household, idle, worked);
    }

    private static async Task AdvanceTo(PrivateWorldRuntime world, long tick)
    {
        while (world.WorldTick < tick) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
    }

    /// <summary>The same world with shorter days, as TownMembershipTests does, so a season takes a few hundred ticks.</summary>
    private static PrivateWorldRuntimeState ShortDays(PrivateWorldRuntimeState state, int day)
    {
        var society = state.Society.Society;
        var old = society.Config.TicksPerWorldDay;
        return state with
        {
            WorldSystems = RegionalWeatherRules.Initialize(state.WorldSystems! with
            {
                Config = state.WorldSystems.Config with { TicksPerDay = day, CalendarOffsetTicks = 0 },
                RegionalWeather = null,
            }, state.Map),
            Society = state.Society with
            {
                Society = society with
                {
                    Config = society.Config with { TicksPerWorldDay = day },
                    Inhabitants = society.Inhabitants.Select(person => person with
                    {
                        BirthTick = person.BirthTick / old * day,
                        BirthLifeTick = person.BirthLifeTick is { } birth ? birth / old * day : null,
                    }).ToArray(),
                },
            },
        };
    }
}

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
        var (state, _, household, idle, worked) = TwoFields("field-returns-to-grass");
        var season = FarmFieldRules.IdleTicksBeforeGrass(state.WorldSystems!.Config);
        var fertility = new LandFertility(state.Map, "field-returns-to-grass").At(idle);
        using var world = FarmFieldTests.Restore(state);
        var start = world.WorldTick;

        // Halfway through the season someone works the second field, which restarts its clock.
        await AdvanceTo(world, start + season / 2);
        var halfway = world.ExportState();
        var reloaded = FarmFieldTests.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(halfway with
        {
            Fields = halfway.Fields!.Select(field => field.Position == worked ? field with { LastWorkedTick = halfway.Society.Society.WorldTick } : field).ToArray(),
        })));
        try
        {
            Assert.Equal(start, reloaded.Fields.Single(field => field.Position == idle).LastWorkedTick);
            await AdvanceTo(reloaded, start + season - 1);
            Assert.Equal(2, reloaded.Fields.Count);

            await AdvanceTo(reloaded, start + season + 1);
            var remaining = Assert.Single(reloaded.Fields);
            Assert.Equal(worked, remaining.Position);
            var events = reloaded.ExportState().Events.Where(item => item.Kind == "field_returned_to_grass").ToArray();
            Assert.Equal($"{FarmFieldRules.FieldId(idle)}:{household}", Assert.Single(events).Detail);
            // The seed kept back for replanting is free again, and the land keeps its fertility.
            Assert.Contains(reloaded.Society.Inventory.Reservations, reservation => reservation.Id == "grass-test:replant" &&
                reservation.State != InventoryReservationState.Reserved);
            Assert.Equal(fertility, new LandFertility(state.Map, "field-returns-to-grass").At(idle));
            Assert.DoesNotContain(new OwnerWorldObservationStore(reloaded).GetSnapshot().Fields!, field =>
                field.Position.X == idle.X && field.Position.Y == idle.Y);

            // The worked field goes too once a full season passes without work.
            await AdvanceTo(reloaded, start + season / 2 + season + 1);
            Assert.Empty(reloaded.Fields);

            var bytes = PrivateWorldRuntimeCodec.Encode(reloaded.ExportState());
            using var restored = FarmFieldTests.Restore(PrivateWorldRuntimeCodec.Decode(bytes));
            Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        }
        finally { reloaded.Dispose(); }
    }

    [Fact]
    public void FieldWorkRecordsWhenTheFieldWasLastWorkedAndSavesIt()
    {
        var (state, actor, _, point) = FarmFieldTests.PreparedFarmer("field-last-worked");
        using var world = FarmFieldTests.Restore(state);
        Assert.True(world.StartFieldWork(actor, point, FarmWorkKind.Till).Accepted);
        var field = Assert.Single(world.Fields);
        Assert.Equal(world.WorldTick, field.LastWorkedTick);
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = FarmFieldTests.Restore(PrivateWorldRuntimeCodec.Decode(bytes));
        Assert.Equal(field, Assert.Single(restored.Fields));

        var future = world.ExportState();
        Assert.Throws<InvalidDataException>(() => FarmFieldTests.Restore(future with
        {
            Fields = [field with { LastWorkedTick = future.Society.Society.WorldTick + 1 }],
        }));
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

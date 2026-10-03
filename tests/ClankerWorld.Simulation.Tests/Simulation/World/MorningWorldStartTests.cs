using System.Text;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed class MorningWorldStartTests
{
    [Fact]
    public async Task GeneratedMorningSetupKeepsRawTickZeroThroughLayoutRedoFounderUndoAndStart()
    {
        const string seed = "morning-start-audit";
        using var world = new PrivateWorldRuntime(seed, _ => new ActionCoverageRecorder(chooseIdle: true),
            startPace: WorldStartPace.FounderSetup,
            geographyOptions: new GeographyOptions(seed, WorldSizePreset.Small));
        AssertMorning(world);
        Assert.True(world.Society.IsPaused);
        Assert.False(world.FounderSetup!.Started);
        Assert.Empty(world.Inhabitants);
        Assert.False((await world.AdvanceOneTickAsync()).Advanced);

        world.InitializeFirstTownContent();
        var map = world.ExportState().Map;
        var site = NormalPathWorld.FindStartingTownSite(map);
        world.AcceptFirstTownLayout(site);
        world.AcceptFirstTownLayout(site);
        Assert.Contains(world.ExportState().Events, item => item.Kind == "first_town_layout_redone");
        AssertMorning(world);

        var positions = world.RoadTiles.Take(PrivateWorldRuntime.RequiredFounders).ToArray();
        Assert.Equal(PrivateWorldRuntime.RequiredFounders, positions.Length);
        for (var index = 0; index < positions.Length; index++)
            world.PlaceFounder($"founder:{index + 1:D32}", positions[index]);
        var lastId = world.FounderSetup.FounderIds[^1];
        var lastBirth = world.Society.GetInhabitant(lastId).BirthTick;
        Assert.Equal(3, world.UndoLastFounder(lastId));
        world.PlaceFounder(lastId, positions[^1]);
        Assert.Equal(lastBirth, world.Society.GetInhabitant(lastId).BirthTick);
        AssertMorning(world);
        for (var index = 0; index < world.FounderSetup.FounderIds.Count; index++)
        {
            var person = world.Society.GetInhabitant(world.FounderSetup.FounderIds[index]);
            Assert.Equal(SocietyFixture.FounderArrivalAge(world.Society.Config, seed, index),
                world.Society.AgeAt(person, 0));
        }
        Assert.All(world.ExportState().Events, item => Assert.Equal(0, item.WorldTick));
        var zeroOffsetState = world.ExportState();
        zeroOffsetState = zeroOffsetState with
        {
            WorldSystems = zeroOffsetState.WorldSystems! with
            {
                SchemaVersion = 2,
                Config = zeroOffsetState.WorldSystems.Config with { ContractVersion = 2, CalendarOffsetTicks = 0 },
            },
        };
        var zeroOffsetBytes = PrivateWorldRuntimeCodec.Encode(zeroOffsetState);
        using var existingClock = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(zeroOffsetBytes));
        Assert.Equal(zeroOffsetBytes, PrivateWorldRuntimeCodec.Encode(existingClock.ExportState()));
        Assert.Equal(0, WorldCalendarRules.FromTick(existingClock.WorldTick, existingClock.WorldSystems.Config).TickOfDay);
        Assert.Equal(world.Society.Inhabitants.Select(person => (person.BirthTick, person.BirthLifeTick, world.Society.AgeAt(person, 0))),
            existingClock.Society.Inhabitants.Select(person => (person.BirthTick, person.BirthLifeTick, existingClock.Society.AgeAt(person, 0))));
        var encoded = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(encoded),
            _ => new ActionCoverageRecorder(chooseIdle: true));
        Assert.Equal(encoded, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        AssertMorning(restored);
        Assert.Equal(world.Society.Inhabitants.Select(person => person.BirthTick),
            restored.Society.Inhabitants.Select(person => person.BirthTick));

        world.StartWorld();
        restored.StartWorld();
        AssertMorning(world);
        Assert.True(world.FounderSetup.Started);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(1, world.WorldTick);
        Assert.Equal(91, WorldCalendarRules.FromTick(world.WorldTick, world.WorldSystems.Config).TickOfDay);
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()),
            PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }

    [Fact]
    public void ModernPresetStartsAtSixWhileExistingZeroOffsetSaveKeepsItsClockAndBytes()
    {
        using var modern = new PrivateWorldRuntime("morning-modern-preset", startPace: WorldStartPace.DecidedPlaytest);
        AssertMorning(modern);
        Assert.NotEmpty(modern.Inhabitants);
        using var legacy = new PrivateWorldRuntime("morning-legacy-save");
        var before = legacy.ExportState();
        Assert.Equal(0, before.WorldSystems!.Config.CalendarOffsetTicks);
        Assert.Equal(2, before.WorldSystems.SchemaVersion);
        var encoded = PrivateWorldRuntimeCodec.Encode(before);
        Assert.DoesNotContain("calendarOffsetTicks", Encoding.UTF8.GetString(encoded), StringComparison.OrdinalIgnoreCase);
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(encoded));
        Assert.Equal(encoded, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        Assert.Equal(0, WorldCalendarRules.FromTick(restored.WorldTick, restored.WorldSystems.Config).TickOfDay);
        Assert.Equal(DaylightRules.FullDarkness, DaylightRules.DarknessBasisPoints(restored.WorldSystems));
        Assert.Equal(before.Society.Society.Inhabitants.Select(person => (person.BirthTick, person.BirthLifeTick, person.AgeBand)),
            restored.Society.Inhabitants.Select(person => (person.BirthTick, person.BirthLifeTick, person.AgeBand)));
    }

    private static void AssertMorning(PrivateWorldRuntime world)
    {
        Assert.Equal(0, world.WorldTick);
        Assert.Equal(0, world.WorldSystems.WorldTick);
        Assert.Equal(360, world.WorldSystems.Config.TicksPerDay);
        Assert.Equal(90, world.WorldSystems.Config.CalendarOffsetTicks);
        Assert.Equal(3, world.WorldSystems.SchemaVersion);
        var calendar = WorldCalendarRules.FromTick(world.WorldTick, world.WorldSystems.Config);
        Assert.Equal(0, calendar.WorldTick);
        Assert.Equal(0, calendar.DayIndex);
        Assert.Equal(0, calendar.DayOfYear);
        Assert.Equal(SeasonKind.Spring, calendar.Season);
        Assert.Equal(90, calendar.TickOfDay);
        Assert.Equal(0, DaylightRules.DarknessBasisPoints(world.WorldSystems));
    }
}

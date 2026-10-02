using System.Globalization;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.World;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class SocietyLifePaceTests
{
    [Fact]
    public void UnknownFutureLifecycleContractIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SocietyConfig(ContractVersion: 4).Validate());
    }

    [Fact]
    public void DecidedDayLifecycleAgesFromBirthAndCannotOutliveDaySixty()
    {
        var config = new SocietyConfig(
            TicksPerWorldDay: 360,
            DaysPerWorldYear: 40,
            BaseNaturalMortalityBasisPoints: 0,
            NaturalMortalitySlopeBasisPoints: 0,
            ContractVersion: 3,
            DayLifecycle: new SocietyDayLifecycle());
        var child = new SocietyInhabitant("child", "Child", 0, SocietyInhabitantStatus.Active,
            SocietyAgeBand.Infant, 10_000, null, null, SocietyWorkRole.Unassigned, 0);
        var society = SocietyFixture.CreateGenesis("short-life", [child], config: config);
        var founder = SocietyFixture.CreateFounder("founder", "Founder", config: config);
        Assert.Equal(15, config.AgeAt(founder.BirthTick, 0));
        Assert.Equal(SocietyAgeBand.Adult, founder.AgeBand);

        society = SocietyFixture.AdvanceTo(society, 3 * 360 - 1).Checkpoint;
        Assert.Equal(SocietyAgeBand.Infant, society.GetInhabitant("child").AgeBand);
        society = SocietyFixture.AdvanceTo(society, 3 * 360).Checkpoint;
        Assert.Equal(SocietyAgeBand.Child, society.GetInhabitant("child").AgeBand);
        society = SocietyFixture.AdvanceTo(society, 15 * 360).Checkpoint;
        society = SocietyCheckpointCodec.Decode(SocietyCheckpointCodec.Encode(society));
        Assert.Equal(SocietyAgeBand.Adult, society.GetInhabitant("child").AgeBand);
        society = SocietyFixture.AdvanceTo(society, 45 * 360).Checkpoint;
        Assert.Equal(SocietyAgeBand.Elder, society.GetInhabitant("child").AgeBand);
        society = SocietyFixture.AdvanceTo(society, 60 * 360).Checkpoint;
        Assert.Equal(SocietyInhabitantStatus.Dead, society.GetInhabitant("child").Status);
        Assert.Equal(SocietyDeathCause.NaturalAge, society.GetInhabitant("child").DeathCause);
        Assert.Equal(60 * 360, society.GetInhabitant("child").DeathTick);
    }

    [Fact]
    public void FounderArrivalAgesAreDifferentSeededDaysFromFifteenToTwentyFive()
    {
        var config = PlaytestDays();
        Assert.Equal(25, SocietyFixture.LatestArrivalAge(config));
        int[] FounderAges(string seed, int count) => Enumerable.Range(0, count)
            .Select(index => SocietyFixture.FounderArrivalAge(config, seed, index)).ToArray();

        var orders = Enumerable.Range(0, 32)
            .Select(index => FounderAges(FormattableString.Invariant($"founder-ages-{index}"), 4)).ToArray();
        foreach (var ages in orders)
        {
            Assert.All(ages, age => Assert.InRange(age, 15, 25));
            Assert.Equal(ages.Length, ages.Distinct().Count());
        }
        Assert.Equal(orders[0], FounderAges("founder-ages-0", 4));
        Assert.True(orders.Select(ages => string.Join(',', ages)).Distinct().Count() > 1);
        // Every arrival day is used once before any founder repeats one.
        Assert.Equal(Enumerable.Range(15, 11), FounderAges("founder-ages-0", 11).Order());
        Assert.Equal(18, SocietyFixture.FounderArrivalAge(new SocietyConfig(), "founder-ages-0", 3));
    }

    [Fact]
    public void AddedAdultsArriveAtSeededAgesFromFifteenToTwentyFive()
    {
        var config = PlaytestDays();
        int[] AddedAges(string seed, string idPrefix)
        {
            var society = SocietyFixture.AdvanceTo(SocietyFixture.CreateGenesis(seed, [], config: config), 1_000).Checkpoint;
            for (var index = 0; index < 60; index++)
                society = SocietyFixture.AddAdult(society, FormattableString.Invariant($"{idPrefix}-{index:D2}"), null).Checkpoint;
            society = SocietyCheckpointCodec.Decode(SocietyCheckpointCodec.Encode(society));
            Assert.All(society.Inhabitants, person => Assert.Equal(SocietyAgeBand.Adult, person.AgeBand));
            return society.Inhabitants.Select(person => society.AgeAt(person, society.WorldTick)).ToArray();
        }

        var ages = AddedAges("added-adults", "agent:first");
        Assert.All(ages, age => Assert.InRange(age, 15, 25));
        Assert.Contains(15, ages);
        Assert.Contains(25, ages);
        // Arrival order, not the client-chosen ID, picks the age.
        Assert.Equal(ages, AddedAges("added-adults", "agent:second"));
        Assert.NotEqual(ages, AddedAges("other-added-adults", "agent:first"));
    }

    [Fact]
    public void EveryArrivalAgeIsAdultAndTheUnchangedDeathCurveEndsLifeByDaySixty()
    {
        var config = PlaytestDays();
        Assert.Equal(0, config.NaturalMortalityRiskBasisPoints(44));
        Assert.Equal(100, config.NaturalMortalityRiskBasisPoints(45));
        Assert.Equal(125, config.NaturalMortalityRiskBasisPoints(46));
        Assert.Equal(450, config.NaturalMortalityRiskBasisPoints(59));
        Assert.Equal(10_000, config.NaturalMortalityRiskBasisPoints(60));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SocietyFixture.CreateFounder("young", "Young", config: config, startingAge: 14));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SocietyFixture.CreateFounder("old", "Old", config: config, startingAge: 26));
        var founders = Enumerable.Range(15, 11).Select(age => SocietyFixture.CreateFounder(
            FormattableString.Invariant($"arrived-day-{age}"), "Founder", config: config, startingAge: age)).ToArray();
        Assert.All(founders, founder => Assert.Equal(SocietyAgeBand.Adult, founder.AgeBand));
        var society = SocietyFixture.CreateGenesis("arrival-lifespans", founders, config: config);

        // A day-25 arrival reaches elder age after 20 days; nobody dies of age before then.
        society = SocietyFixture.AdvanceTo(society, 20 * 360 - 1).Checkpoint;
        Assert.All(society.Inhabitants, person =>
        {
            Assert.Equal(SocietyInhabitantStatus.Active, person.Status);
            Assert.Equal(SocietyAgeBand.Adult, person.AgeBand);
        });
        society = SocietyFixture.AdvanceTo(society, 45 * 360).Checkpoint;
        Assert.All(society.Inhabitants, person =>
        {
            Assert.Equal(SocietyInhabitantStatus.Dead, person.Status);
            Assert.Equal(SocietyDeathCause.NaturalAge, person.DeathCause);
            Assert.InRange(society.AgeAt(person, person.DeathTick!.Value), 45, 60);
        });
    }

    [Fact]
    public void NewWorldPaceIsSavedAndRestored()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"clankerworld-pace-{Guid.NewGuid():N}");
        try
        {
            var newFile = new PrivateWorldStateFile(Path.Combine(directory, "new.json"),
                newWorldPace: WorldStartPace.DecidedPlaytest);
            using var created = newFile.LoadOrCreate("new-pace");
            Assert.True(created.Society.IsPaused);
            Assert.Equal(360, created.WorldSystems.Config.TicksPerDay);
            Assert.Equal(40, created.WorldSystems.Config.DaysPerYear);
            Assert.Equal(10, created.WorldSystems.Config.SpringDays);
            Assert.Equal(SeasonKind.Summer,
                WorldCalendarRules.FromTick(10 * 360, created.WorldSystems.Config).Season);
            Assert.Equal(360, created.Society.Config.TicksPerWorldDay);
            Assert.Equal(60, created.Society.Config.DayLifecycle?.MaximumDay);
            var ages = created.Society.Inhabitants.Select(person => created.Society.AgeAt(person, 0)).ToArray();
            Assert.All(ages, age => Assert.InRange(age, 15, 25));
            Assert.Equal(ages.Length, ages.Distinct().Count());
            var scoutAge = SocietyFixture.FounderArrivalAge(created.Society.Config, "new-pace", 0);
            var scout = new OwnerWorldObservationStore(created).GetSnapshot().Inhabitants
                .Single(person => person.Id == "founder-scout");
            Assert.Equal(scoutAge.ToString(CultureInfo.InvariantCulture),
                scout.DecisionFactors.Single(factor => factor.Key == "age-days").Detail);
            Assert.Equal("adult", scout.DecisionFactors.Single(factor => factor.Key == "age-band").Detail);
            Assert.DoesNotContain(scout.DecisionFactors, factor => factor.Key == "age-years");
            using var reloaded = newFile.LoadOrCreate("new-pace");
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(created.ExportState()),
                PrivateWorldRuntimeCodec.Encode(reloaded.ExportState()));

        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void PaceChangesPreserveCurrentAgeBirthDatesAndTheWorldCalendar()
    {
        var original = SocietyFixture.AdvanceTo(SocietyFixture.CreateGenesis("life-clock"), 1_234).Checkpoint;
        var person = original.Inhabitants[0];
        Assert.Throws<InvalidOperationException>(() => SocietyFixture.SetLifePace(original, 1_460));
        var paused = SocietyFixture.Pause(original).Checkpoint;
        var fast = SocietyFixture.SetLifePace(paused, 1_460).Checkpoint;
        Assert.Equal(original.AgeAt(person, original.WorldTick), fast.AgeAt(person, fast.WorldTick));
        Assert.Equal(person.BirthTick, fast.Inhabitants[0].BirthTick);
        Assert.Equal(365, fast.Config.DaysPerWorldYear);
        Assert.Equal(paused.WorldTick, fast.WorldTick);
        var advanced = SocietyFixture.AdvanceTo(SocietyFixture.Resume(fast).Checkpoint, fast.WorldTick + 360).Checkpoint;
        Assert.Equal(original.AgeAt(person, original.WorldTick) + 1, advanced.AgeAt(advanced.Inhabitants[0], advanced.WorldTick));
        var normal = SocietyFixture.SetLifePace(SocietyFixture.Pause(advanced).Checkpoint, 1).Checkpoint;
        Assert.Equal(advanced.AgeAt(advanced.Inhabitants[0], advanced.WorldTick), normal.AgeAt(normal.Inhabitants[0], normal.WorldTick));
        Assert.Equal(advanced.LifeTickAt(advanced.WorldTick), normal.LifeTickAt(normal.WorldTick));
        Assert.Equal(normal, SocietyFixture.SetLifePace(normal, 1).Checkpoint);
    }

    [Fact]
    public void NewbornStartsAtZeroUnderAcceleratedTimeAndAgesAcrossRestart()
    {
        var founders = new[] { SocietyFixture.CreateFounder("alice", "Alice"), SocietyFixture.CreateFounder("bob", "Bob") };
        var society = SocietyFixture.CreateGenesis("life-family", founders, [new InventoryLot("food", "food", "alice", 10, 10_000, 10_000, 0)]);
        society = SocietyFixture.CreateHousehold(society, "home", "Home", ["alice", "bob"]).Checkpoint;
        society = SocietyFixture.ProposeRelationship(society, new("partners", 1, SocietyRelationshipType.Partnership, "alice", "bob", 0)).Checkpoint;
        society = SocietyFixture.AcceptRelationship(society, "partners", 1, "bob").Checkpoint;
        society = SocietyFixture.SetLifePace(SocietyFixture.Pause(society).Checkpoint, 1_460).Checkpoint;
        society = SocietyFixture.AdvanceTo(SocietyFixture.Resume(society).Checkpoint, 1_000).Checkpoint;
        var birth = SocietyFixture.CommitBirth(society, new("baby", 1, "alice", "bob", "home", ["alice", "bob"],
            ["alice", "bob"], "food", 1, society.WorldTick, PrimaryCaregiverId: "alice"));
        society = birth.Checkpoint;
        var child = society.GetInhabitant(birth.CreatedId!);
        Assert.Equal(1_000, child.BirthTick);
        Assert.Equal(1_460_000, child.BirthLifeTick);
        Assert.Equal(0, society.AgeAt(child, society.WorldTick));
        var restored = SocietyCheckpointCodec.Decode(SocietyCheckpointCodec.Encode(society));
        var before = SocietyFixture.AdvanceTo(restored, 1_719).Checkpoint;
        Assert.Equal(SocietyAgeBand.Infant, before.GetInhabitant(child.Id).AgeBand);
        var after = SocietyFixture.AdvanceTo(before, 1_720).Checkpoint;
        Assert.Equal(SocietyAgeBand.Child, after.GetInhabitant(child.Id).AgeBand);
        var adult = SocietyFixture.AdvanceTo(after, 7_480).Checkpoint;
        Assert.Equal(SocietyAgeBand.Adult, adult.GetInhabitant(child.Id).AgeBand);
        Assert.Equal(18, adult.AgeAt(adult.GetInhabitant(child.Id), adult.WorldTick));
    }

    [Fact]
    public void CurrentLegacyStartWorldKeepsItsPaceUntilTheOwnerChangesIt()
    {
        using var world = new PrivateWorldRuntime("life-current-legacy-start");
        world.Pause();
        var currentBytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.Equal(PrivateWorldRuntime.StateSchemaVersion, world.ExportState().SchemaVersion);
        Assert.Null(world.Society.LifeClock);
        Assert.True(world.Society.IsPaused);
        Assert.False(world.SetLifePace(1));
        Assert.Null(world.Society.LifeClock);
        Assert.Equal(currentBytes, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        Assert.True(world.SetLifePace(365));
        Assert.Equal(PrivateWorldRuntime.StateSchemaVersion, world.ExportState().SchemaVersion);
        Assert.Equal(365, world.Society.LifeClock!.Rate);
        Assert.True(world.Society.IsPaused);
        using var reloaded = PrivateWorldRuntime.Restore(
            PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(reloaded.ExportState()));
        Assert.Throws<ArgumentOutOfRangeException>(() => world.SetLifePace(0));
    }

    [Fact]
    public void AcceleratedMortalityUsesTheSameBiologicalAgeRollsAsCalendarTime()
    {
        var founders = Enumerable.Range(0, 20).Select(index => SocietyFixture.CreateFounder($"person-{index:D2}", "Person")).ToArray();
        var seed = SocietyFixture.CreateGenesis("life-mortality", founders);
        var normal = SocietyFixture.AdvanceTo(seed, 72 * seed.Config.TicksPerWorldYear).Checkpoint;
        var fast = SocietyFixture.SetLifePace(SocietyFixture.Pause(seed).Checkpoint, 1_460).Checkpoint;
        fast = SocietyFixture.AdvanceTo(SocietyFixture.Resume(fast).Checkpoint, 72 * 360).Checkpoint;
        Assert.Contains(normal.Inhabitants, person => person.Status == SocietyInhabitantStatus.Dead);
        Assert.Equal(normal.Inhabitants.Select(person => (person.Id, person.Status, person.AgeBand, person.LastLifecycleYearChecked, person.DeathCause)),
            fast.Inhabitants.Select(person => (person.Id, person.Status, person.AgeBand, person.LastLifecycleYearChecked, person.DeathCause)));
    }

    [Fact]
    public async Task MinorsCannotStartAdultWorkEvenIfAnOldRoleRecordSaysBuilder()
    {
        using var seed = new PrivateWorldRuntime("minor-work");
        var state = seed.ExportState();
        var childId = state.Inhabitants[0].InhabitantId;
        state = state with
        {
            Society = state.Society with
            {
                Society = state.Society.Society with
                {
                    Inhabitants = state.Society.Society.Inhabitants.Select(person => person.Id == childId ? person with
                    {
                        BirthTick = -3 * state.Society.Society.Config.TicksPerWorldYear,
                        AgeBand = SocietyAgeBand.Child,
                        LastLifecycleYearChecked = 3,
                        CurrentRole = SocietyWorkRole.Builder,
                    } : person).ToArray(),
                }
            }
        };
        using var world = PrivateWorldRuntime.Restore(state);
        world.StageStarterContent();
        for (var tick = 0; tick < 50; tick++) await world.AdvanceOneTickAsync();
        Assert.Null(world.Inhabitants.Single(person => person.InhabitantId == childId).Project);
        Assert.Null(world.Inhabitants.Single(person => person.InhabitantId == childId).Lesson);
    }

    [Theory]
    [InlineData(0)]
    public void UnsupportedClockRatesFailClosed(int rate)
    {
        var state = SocietyFixture.CreateGenesis("invalid-life-clock");
        state = state with { Config = state.Config with { ContractVersion = 2 }, LifeClock = new(rate, 0, 0) };
        Assert.Throws<InvalidDataException>(() => SocietyFixture.Validate(state));
    }

    private static SocietyConfig PlaytestDays() => new(
        TicksPerWorldDay: 360,
        DaysPerWorldYear: 40,
        ContractVersion: 3,
        DayLifecycle: new SocietyDayLifecycle());
}

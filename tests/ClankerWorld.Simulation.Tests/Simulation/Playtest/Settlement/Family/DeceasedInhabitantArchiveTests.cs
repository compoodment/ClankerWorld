using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class DeceasedInhabitantArchiveTests
{
    [Fact]
    public async Task DeathRemovesTheActiveActorButKeepsAnInspectableSavedRecord()
    {
        using var seed = new PrivateWorldRuntime("deceased-archive");
        var state = seed.ExportState();
        var society = state.Society.Society;
        var config = society.Config with
        {
            TicksPerWorldDay = 1,
            DaysPerWorldYear = 4,
            AdultYears = 17,
            ElderYears = 18,
            BaseNaturalMortalityBasisPoints = 9_999,
            NaturalMortalitySlopeBasisPoints = 0,
        };
        society = society with
        {
            Config = config,
            Memories = society.Inhabitants.Select(person => new SocietySocialMemory(
                $"last-memory:{person.Id}", person.Id, "founder-scout",
                "I remember the first campfire.", "private", 0)).ToArray(),
            Inhabitants = society.Inhabitants.Select(person => person with
            {
                BirthTick = -75,
                BirthLifeTick = null,
                AgeBand = SocietyAgeBand.Elder,
                LastLifecycleYearChecked = 18,
            }).ToArray(),
        };
        state = state with
        {
            Society = state.Society with { Society = society },
            WorldSystems = state.WorldSystems! with
            {
                Config = state.WorldSystems!.Config with
                {
                    TicksPerDay = 1,
                    DaysPerYear = 4,
                    SpringDays = 1,
                    SummerDays = 1,
                    AutumnDays = 1,
                    WinterDays = 1,
                },
            },
            Inhabitants = state.Inhabitants.Select(person => person with
            {
                RecentThoughts = [new PlaytestPrivateThought(0, "I hope the camp lasts.")],
            }).ToArray(),
        };
        using var world = PrivateWorldRuntime.Restore(state, _ => new DeterministicDecisionProvider());

        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var archived = world.ExportState();
        Assert.NotEmpty(archived.DeceasedInhabitants ?? []);
        var deceased = archived.DeceasedInhabitants![0];
        Assert.Equal(deceased.LastPhysical.InhabitantId, deceased.InhabitantId);
        Assert.DoesNotContain(archived.Inhabitants, person => person.InhabitantId == deceased.InhabitantId);

        var bytes = PrivateWorldRuntimeCodec.Encode(archived);
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes));
        var projected = new OwnerWorldObservationStore(restored).GetSnapshot();
        var historical = Assert.Single(projected.Inhabitants, person => person.Id == deceased.InhabitantId);
        Assert.Equal("dead", historical.Lifecycle);
        Assert.Equal("deceased", historical.Route.Status);
        Assert.Equal(deceased.DeathTick.ToString(System.Globalization.CultureInfo.InvariantCulture),
            historical.DecisionFactors.Single(factor => factor.Key == "death-tick").Detail);
        Assert.Null(historical.PublicIntention);
        Assert.Equal("I hope the camp lasts.", Assert.Single(historical.RecentPrivateThoughts).Text);
        Assert.Equal("I remember the first campfire.", Assert.Single(historical.RecentMemories).Summary);
        Assert.DoesNotContain(restored.Inhabitants, person => person.InhabitantId == deceased.InhabitantId);

        var invalid = archived with
        {
            DeceasedInhabitants = archived.DeceasedInhabitants!
                .Select(person => person.InhabitantId == deceased.InhabitantId
                    ? person with { DeathTick = person.DeathTick + 1 }
                    : person).ToArray(),
        };
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(invalid));
    }

    [Fact]
    public void OwnerMemoryProjectionKeepsPrivateRecordsWithTheirOwnerAndOmitsForgottenOnes()
    {
        using var seed = new PrivateWorldRuntime("memory-inspection");
        var state = seed.ExportState();
        state = state with
        {
            Society = state.Society with
            {
                Society = state.Society.Society with
                {
                    Memories = new[]
                    {
                        new SocietySocialMemory("secret", "founder-scout", "founder-mira",
                            "I hid a tool behind the storehouse.", "private", 0),
                        new SocietySocialMemory("other", "founder-mira", "founder-scout",
                            "Scout helped me harvest.", "public", 0),
                        new SocietySocialMemory("forgotten", "founder-scout", "founder-mira",
                            "An old abandoned belief.", "private", 0, TombstonedTick: 0),
                    }.OrderBy(memory => memory.Id, StringComparer.Ordinal).ToArray(),
                },
            },
        };
        using var world = PrivateWorldRuntime.Restore(state);
        var inhabitants = new OwnerWorldObservationStore(world).GetSnapshot().Inhabitants;
        var scout = inhabitants.Single(person => person.Id == "founder-scout");
        var mira = inhabitants.Single(person => person.Id == "founder-mira");
        Assert.Equal("I hid a tool behind the storehouse.", Assert.Single(scout.RecentMemories).Summary);
        Assert.Equal("private", scout.RecentMemories[0].Visibility);
        Assert.Equal("Mira", scout.RecentMemories[0].SubjectName);
        Assert.DoesNotContain(scout.SocialNotes, note => note.Contains("hid a tool", StringComparison.Ordinal));
        Assert.Equal("Scout helped me harvest.", Assert.Single(mira.RecentMemories).Summary);
        Assert.DoesNotContain(mira.RecentMemories, memory => memory.Summary.Contains("hid a tool", StringComparison.Ordinal));
    }
}

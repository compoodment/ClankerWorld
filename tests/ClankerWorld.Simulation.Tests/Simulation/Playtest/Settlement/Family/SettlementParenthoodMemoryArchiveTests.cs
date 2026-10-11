using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class SettlementParenthoodTests
{
    [Theory]
    [InlineData("requested", false)]
    [InlineData("preparing", false)]
    [InlineData("postponed", true)]
    [InlineData("completed", true)]
    public async Task DailyMemoryArchiveProtectsOnlyAnActiveNativeParenthoodPlan(string stage, bool archives)
    {
        var seed = await PreparedState();
        var owner = seed.Inhabitants[0].InhabitantId;
        var partner = seed.Inhabitants[1].InhabitantId;
        var day = seed.Society.Society.Config.TicksPerWorldDay;
        var boundary = 4L * day;
        var proposing = new ParentProvider("parent_propose:");
        using var family = PrivateWorldRuntime.Restore(seed, actor => actor == owner ? proposing : new ParentProvider(
            actor == partner && stage != "requested" ? stage == "postponed" ? "parent_postpone:" : "parent_accept:" : "safe_idle"));
        // Move only idle calendar time. The actual offered choices create each plan stage.
        if (stage != "completed")
        {
            PositionFamilyFixtureAt(family, boundary - 24);
            family.Pause();
            var fed = family.ExportState();
            // The calendar jump spoils starter food. Supply fresh household bread as
            // a fixture control; consent and plan transitions still use offered choices.
            family.LoadPausedCheckpoint(fed with
            {
                Society = fed.Society with
                {
                    Society = fed.Society.Society with
                    {
                        Inventory = InventoryFixture.AddLot(fed.Society.Society.Inventory, "archive-parent-food", "bread",
                            fed.Society.Society.GetInhabitant(owner).HouseholdId!, 24),
                    },
                },
            });
            family.Resume();
        }
        for (var attempt = 0; attempt < 10 && family.Inhabitants.Single(person => person.InhabitantId == owner).Parenthood is null; attempt++)
            Assert.True((await family.AdvanceOneTickAsync()).Advanced);
        Assert.True(family.Inhabitants.Single(person => person.InhabitantId == owner).Parenthood is not null,
            $"Age={family.Society.GetInhabitant(owner).AgeBand}; food={PrivateWorldRuntime.ParenthoodFoodReadiness(family.Society, owner, family.Towns)}; " +
            string.Join(";", proposing.SeenCandidates.Select(candidate => candidate.Id)));
        Assert.Equal("requested", family.Inhabitants.Single(person => person.InhabitantId == owner).Parenthood!.Stage);
        if (stage != "requested")
            for (var attempt = 0; attempt < 10 && family.Inhabitants.Single(person => person.InhabitantId == owner).Parenthood!.Stage == "requested"; attempt++)
                Assert.True((await family.AdvanceOneTickAsync()).Advanced);
        if (stage == "completed")
        {
            var preparation = family.Inhabitants.Single(person => person.InhabitantId == owner).Parenthood!;
            Assert.Equal("preparing", preparation.Stage);
            PositionFamilyFixtureAt(family, preparation.LastTransitionTick + 599);
            Assert.True((await family.AdvanceOneTickAsync()).Advanced);
            var birth = Assert.Single(family.Society.Births);
            Assert.Equal(birth.ChildId, family.Inhabitants.Single(person => person.InhabitantId == owner).Parenthood!.ChildId);
        }
        PositionFamilyFixtureAt(family, boundary - 1);
        var plan = family.Inhabitants.Single(person => person.InhabitantId == owner).Parenthood!;
        Assert.Equal(stage, plan.Stage);
        Assert.Equal(boundary - 1, family.WorldTick);
        var state = family.ExportState();
        var ordinary = new SocietySocialMemory("parent-old-experience", owner, partner, "An old ordinary experience.", "private", 0);
        var control = new SocietySocialMemory("partner-old-experience", partner, owner, "An independent old experience.", "private", 0);
        var durable = new SocietySocialMemory("parent-life-event", owner, partner, "A lasting family event.", "private", 0)
        { Kind = SocietyMemoryKind.LifeEvent };
        var belief = new SocietyAgentBelief("parent-old-belief", owner, "An old report about a path.",
            SocietyBeliefProvenance.Hearsay, 4_200, 0, SourceAgentId: partner);
        state = state with
        {
            JevEnabled = false,
            RoutineHelper = RoutineHelperSettings.Off,
            Society = state.Society with
            {
                Society = state.Society.Society with
                {
                    Memories = state.Society.Society.Memories.Concat([ordinary, control, durable])
                        .OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(),
                    Beliefs = (state.Society.Society.Beliefs ?? []).Append(belief)
                        .OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(),
                },
            },
        };
        var bytes = PrivateWorldRuntimeCodec.Encode(state);
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), _ => new ParentProvider("safe_idle"));
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), _ => new ParentProvider("safe_idle"));
        Assert.False((await world.AdvanceOneTickAsync(commitPermitted: () => false)).Advanced);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
        var saved = world.ExportState();
        Assert.Equal(boundary, world.WorldTick);
        Assert.Equal(plan, saved.Inhabitants.Single(person => person.InhabitantId == owner).Parenthood);
        Assert.Equal(archives, world.Society.ArchivedMemories.Any(item => item.Memory.Id == ordinary.Id));
        Assert.Equal(archives, world.Society.ArchivedBeliefs.Any(item => item.Belief.Id == belief.Id));
        Assert.Equal(!archives, world.Society.Memories.Contains(ordinary));
        Assert.Equal(!archives, world.Society.Beliefs!.Contains(belief));
        Assert.Contains(durable, world.Society.Memories);
        var independent = Assert.Single(world.Society.ArchivedMemories, item => item.Memory.Id == control.Id);
        Assert.Equal((control, boundary), (independent.Memory, independent.ArchivedTick));
        if (archives)
        {
            var archived = Assert.Single(world.Society.ArchivedMemories, item => item.Memory.Id == ordinary.Id);
            Assert.Equal((ordinary, boundary), (archived.Memory, archived.ArchivedTick));
            var archivedBelief = Assert.Single(world.Society.ArchivedBeliefs, item => item.Belief.Id == belief.Id);
            Assert.Equal((belief, boundary), (archivedBelief.Belief, archivedBelief.ArchivedTick));
        }
        var after = PrivateWorldRuntimeCodec.Encode(saved);
        Assert.Equal(after, PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        using var loaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(after), _ => new ParentProvider("safe_idle"));
        Assert.Equal(after, PrivateWorldRuntimeCodec.Encode(loaded.ExportState()));
        Assert.Equal(plan, loaded.Inhabitants.Single(person => person.InhabitantId == owner).Parenthood);
    }
}

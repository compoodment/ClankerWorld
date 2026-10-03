using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class PrivateWorldConversationTests
{
    [Fact]
    public async Task ConversationAllowanceResetsAtCivilMidnightAndReloadKeepsTheNewDayBudget()
    {
        var provider = new ConversationProvider(new HashSet<string>(StringComparer.Ordinal) { InitiatorId, InviteeId });
        using var seed = NewWorld("conversation-civil-midnight", provider);
        seed.StartWorld(resume: false);
        var state = seed.ExportState();
        // A supported offset puts midnight two ticks away without simulating a whole day.
        state = state with
        {
            WorldSystems = state.WorldSystems! with
            {
                Config = state.WorldSystems.Config with { CalendarOffsetTicks = 358 },
            },
            Inhabitants = state.Inhabitants.Select(person => person with
            {
                HungerBasisPoints = 9_500,
                IdentityChoicePending = false,
                LastDecisionContext = null,
            }).ToArray(),
            ConversationBudgets = [new(InitiatorId, 0, AgentConversationRules.MaximumConversationsPerWorldDay),
                new(InviteeId, 0, AgentConversationRules.MaximumConversationsPerWorldDay)],
        };
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(
            PrivateWorldRuntimeCodec.Encode(state)), _ => provider);
        world.Resume();
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(1, world.WorldTick);
        Assert.Equal(0, WorldCalendarRules.FromTick(world.WorldTick, world.WorldSystems.Config).DayIndex);
        Assert.Empty(world.Conversations);
        Assert.Equal(2, world.ConversationBudgets.Count);
        Assert.All(world.ConversationBudgets, budget => Assert.Equal(AgentConversationRules.MaximumConversationsPerWorldDay, budget.Count));
        Assert.DoesNotContain(provider.PlanningRequests, request => request.Observation.WorldTick == 1 &&
            request.Observation.Candidates.Any(candidate => candidate.Id.StartsWith("talk:", StringComparison.Ordinal)));

        for (var attempt = 0; attempt < 12 && world.ConversationBudgets.Count(budget => budget.WorldDay == 1) < 2; attempt++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);

        Assert.InRange(world.WorldTick, 2, 13);
        Assert.Contains(provider.PlanningRequests, request => request.Observation.WorldTick >= 2 &&
            request.Observation.Candidates.Any(candidate => candidate.Id == $"talk:{InviteeId}"));
        Assert.NotEmpty(world.Conversations);
        Assert.Equal(2, world.ConversationBudgets.Count);
        Assert.All(world.ConversationBudgets, budget =>
        {
            Assert.Equal(1, budget.WorldDay);
            Assert.Equal(1, budget.Count);
        });
        world.Pause();
        var saved = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved), _ => provider);
        Assert.Equal(world.WorldTick, restored.WorldTick);
        Assert.Equal(358, restored.WorldSystems.Config.CalendarOffsetTicks);
        Assert.Equal(world.ConversationBudgets, restored.ConversationBudgets);
        Assert.Equal(1, WorldCalendarRules.FromTick(restored.WorldTick, restored.WorldSystems.Config).DayIndex);
    }
}

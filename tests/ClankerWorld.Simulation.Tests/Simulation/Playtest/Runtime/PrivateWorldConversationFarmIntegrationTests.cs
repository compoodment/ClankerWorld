using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class PrivateWorldConversationTests
{
    [Fact]
    public async Task OngoingFieldWorkAllowsFreshResumeChoicesThenWaitsDuringTheAcceptedConversation()
    {
        var (state, farmer, _, point) = FarmFieldTests.PreparedFarmer("farm-work-conversation-resume");
        using (var working = FarmFieldTests.Restore(state))
        {
            Assert.True(working.StartFieldWork(farmer, point, FarmWorkKind.Till).Accepted);
            state = working.ExportState();
        }
        var remaining = Assert.Single(state.Fields!).Work!.RemainingTicks;
        var other = state.Inhabitants.First(person => person.InhabitantId != farmer).InhabitantId;
        state = WithAcceptedFarmConversation(state, farmer, other, "conversation:farmer-resume");
        var speaker = new ConversationProvider(new HashSet<string>([farmer, other], StringComparer.Ordinal));
        var idle = new PlanningOnlyConversationProvider(new ConversationProvider(new HashSet<string>(StringComparer.Ordinal)));
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)),
            id => id == farmer || id == other ? speaker : idle);
        world.Resume();
        for (var tick = 0; tick < 6 && world.Conversations.Single().Status != AgentConversationStatus.AwaitingSpeaker; tick++)
        {
            _ = await world.AdvanceOneTickNonBlockingAsync();
            await Task.Delay(3);
        }
        Assert.Contains(speaker.PlanningRequests, request => request.Observation.InhabitantId == farmer &&
            request.Observation.Candidates.Any(candidate => candidate.Id == "conversation_resume:conversation:farmer-resume"));
        Assert.Equal(AgentConversationStatus.AwaitingSpeaker, world.Conversations.Single().Status);
        Assert.Equal(remaining, Assert.Single(world.Fields).Work!.RemainingTicks);
        for (var tick = 0; tick < 3; tick++)
        {
            _ = await world.AdvanceOneTickNonBlockingAsync();
            await Task.Delay(3);
            Assert.Equal(remaining, Assert.Single(world.Fields).Work!.RemainingTicks);
        }

        for (var tick = 0; tick < 30 && Assert.Single(world.Fields).Work is not null; tick++)
        {
            _ = await world.AdvanceOneTickNonBlockingAsync();
            await Task.Delay(3);
            if (world.Conversations.Single().Status is AgentConversationStatus.Ready or
                AgentConversationStatus.AwaitingSpeaker or AgentConversationStatus.WrapUp)
                Assert.Equal<int?>(remaining, Assert.Single(world.Fields).Work?.RemainingTicks);
        }
        Assert.Equal(AgentConversationStatus.Closed, world.Conversations.Single().Status);
        Assert.Equal("agreed", world.Conversations.Single().Outcome);
        Assert.Contains(speaker.PlanningRequests, request => request.Observation.InhabitantId == farmer &&
            request.Observation.Candidates.Any(candidate => candidate.Id == "conversation_wrapup_accept:conversation:farmer-resume"));
        Assert.Equal(7, speaker.TurnRequests.Count);
        Assert.Equal(FarmFieldStage.Prepared, Assert.Single(world.Fields).Stage);
        Assert.Null(Assert.Single(world.Fields).Work);
    }

    [Fact]
    public async Task AnAcceptedConversationRefusesStartingOrdinaryFieldWorkWithoutChangingState()
    {
        var (state, farmer, _, point) = FarmFieldTests.PreparedFarmer("conversation-refuses-field-work");
        var other = state.Inhabitants.First(person => person.InhabitantId != farmer).InhabitantId;
        state = WithAcceptedFarmConversation(state, farmer, other, "conversation:farmer-busy");
        var speaker = new ConversationProvider(new HashSet<string>([farmer, other], StringComparer.Ordinal))
        {
            BlockConversationUntilCanceled = true,
        };
        var idle = new PlanningOnlyConversationProvider(new ConversationProvider(new HashSet<string>(StringComparer.Ordinal)));
        using var world = PrivateWorldRuntime.Restore(state, id => id == farmer || id == other ? speaker : idle);
        world.Resume();
        for (var tick = 0; tick < 20 && world.Conversations.Single().Status != AgentConversationStatus.AwaitingSpeaker; tick++)
        {
            _ = await world.AdvanceOneTickNonBlockingAsync();
            await Task.Delay(3);
        }
        Assert.Equal(AgentConversationStatus.AwaitingSpeaker, world.Conversations.Single().Status);
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False(world.StartFieldWork(farmer, point, FarmWorkKind.Till).Accepted);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        Assert.Empty(world.Fields);
    }

    private static PrivateWorldRuntimeState WithAcceptedFarmConversation(
        PrivateWorldRuntimeState state, string farmer, string other, string id)
    {
        var proposal = AgentConversationRules.Propose(id, farmer, other,
            state.Society.Society.WorldTick, state.Society.Society.RunEpoch);
        Assert.True(AgentConversationRules.TryAcceptProposal(proposal, other,
            proposal.CreatedTick, proposal.RunEpoch, out var accepted));
        var point = state.Inhabitants.Single(person => person.InhabitantId == farmer).Position;
        var day = state.Society.Society.WorldTick / state.Society.Society.Config.TicksPerWorldDay;
        return state with
        {
            Conversations = [accepted],
            ConversationBudgets = [new(farmer, day, 1), new(other, day, 1)],
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == farmer || person.InhabitantId == other
                ? person with
                {
                    Position = point,
                    HungerBasisPoints = 10_000,
                    Survival = person.Survival is { } survival ? survival with { WarmthBasisPoints = 10_000 } : null,
                    Project = null,
                    LastDecisionContext = null,
                } : person).ToArray(),
        };
    }
}

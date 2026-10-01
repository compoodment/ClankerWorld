using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class PrivateWorldConversationTests
{
    [Fact]
    public async Task PausedInvitationKeepsItsDeadlineAndUsesNormalAcceptanceInTheNewEpoch()
    {
        var provider = ConsentReviewProvider();
        using var setup = NewWorld("paused-invitation-consent", provider);
        setup.StartWorld();
        var initial = setup.ExportState();
        var proposal = ReviewInvitation(initial, "conversation:paused-consent");
        using var world = PrivateWorldRuntime.Restore(WithReviewInvitation(initial, proposal), _ => provider);

        world.Pause();
        var paused = Assert.Single(world.Conversations);
        Assert.Equal(AgentConversationStatus.Proposed, paused.Status);
        Assert.Equal(proposal.ProposalDeadlineTick, paused.ProposalDeadlineTick);
        Assert.Equal(proposal.Revision, paused.Revision);
        Assert.Equal(1, Assert.Single(world.ConversationBudgets).Count);

        using var reloaded = PrivateWorldRuntime.Restore(
            PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())), _ => provider);
        reloaded.Resume();
        Assert.NotEqual(proposal.RunEpoch, reloaded.Society.RunEpoch);
        for (var attempt = 0; attempt < 60 && reloaded.Conversations.Single().Turns.Count == 0; attempt++)
        {
            _ = await reloaded.AdvanceOneTickNonBlockingAsync();
            await Task.Delay(3);
        }

        var accepted = Assert.Single(reloaded.Conversations);
        Assert.NotEmpty(accepted.Turns);
        Assert.Equal(reloaded.Society.RunEpoch, accepted.RunEpoch);
        Assert.Equal(proposal.ProposalDeadlineTick, accepted.ProposalDeadlineTick);
        Assert.Equal(1, reloaded.ConversationBudgets.Single(item => item.AgentId == InitiatorId).Count);
        Assert.Equal(1, reloaded.ConversationBudgets.Single(item => item.AgentId == InviteeId).Count);
        Assert.Contains(provider.PlanningRequests, request => request.Observation.InhabitantId == InviteeId &&
            request.Observation.Candidates.Any(candidate => candidate.Id == $"conversation_accept:{proposal.Id}"));
        Assert.DoesNotContain(provider.PlanningRequests, request =>
            request.Observation.Candidates.Any(candidate => candidate.Id.StartsWith("conversation_resume:", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task PausedInvitationCannotBypassTheInviteesDailyLimitOrOriginalExpiry()
    {
        var provider = ConsentReviewProvider();
        using var setup = NewWorld("paused-invitation-limit", provider);
        setup.StartWorld();
        var initial = setup.ExportState();
        var proposal = ReviewInvitation(initial, "conversation:paused-limit");
        var saved = WithReviewInvitation(initial, proposal);
        var worldDay = initial.Society.Society.WorldTick / initial.Society.Society.Config.TicksPerWorldDay;
        using var world = PrivateWorldRuntime.Restore(saved with
        {
            ConversationBudgets = [.. saved.ConversationBudgets!, new AgentConversationDailyBudget(
                InviteeId, worldDay, AgentConversationRules.MaximumConversationsPerWorldDay)],
        }, _ => provider);

        world.Pause();
        world.Resume();
        for (var attempt = 0; attempt < 60 && world.WorldTick <= proposal.ProposalDeadlineTick; attempt++)
        {
            _ = await world.AdvanceOneTickNonBlockingAsync();
            await Task.Delay(3);
        }

        var expired = Assert.Single(world.Conversations);
        Assert.Equal(AgentConversationStatus.Closed, expired.Status);
        Assert.Equal("deadline", expired.Outcome);
        Assert.Equal(proposal.ProposalDeadlineTick, expired.ProposalDeadlineTick);
        Assert.Empty(expired.Turns);
        Assert.Empty(provider.TurnRequests);
        Assert.Equal(AgentConversationRules.MaximumConversationsPerWorldDay,
            world.ConversationBudgets.Single(item => item.AgentId == InviteeId).Count);
        Assert.DoesNotContain(provider.PlanningRequests, request => request.Observation.InhabitantId == InviteeId &&
            request.Observation.Candidates.Any(candidate => candidate.Id.StartsWith("conversation_accept:", StringComparison.Ordinal) ||
                candidate.Id.StartsWith("conversation_resume:", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task ReloadingHalfResumedConversationRequiresTwoFreshResumeDecisions()
    {
        var provider = ConsentReviewProvider(InviteeId);
        using var setup = NewWorld("half-resumed-conversation", provider);
        setup.StartWorld();
        var initial = setup.ExportState();
        var proposal = ReviewInvitation(initial, "conversation:half-resumed");
        Assert.True(AgentConversationRules.TryAcceptProposal(proposal, InviteeId, proposal.CreatedTick,
            proposal.RunEpoch, out var accepted));
        var suspended = AgentConversationRules.Suspend(accepted, AgentConversationInterruption.ProviderTimedOut,
            proposal.CreatedTick);
        Assert.True(AgentConversationRules.TryResume(suspended, InitiatorId, proposal.CreatedTick,
            proposal.RunEpoch, out var halfResumed, out var resumed));
        Assert.False(resumed);
        var checkpoint = WithReviewInvitation(initial, halfResumed) with
        {
            ConversationBudgets = [new AgentConversationDailyBudget(InitiatorId, 0, 1),
                new AgentConversationDailyBudget(InviteeId, 0, 1)],
        };
        var encoded = PrivateWorldRuntimeCodec.Encode(checkpoint);
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(encoded), _ => provider);

        var restored = Assert.Single(world.Conversations);
        Assert.Empty(restored.ResumeAcceptedBy);
        Assert.Equal(AgentConversationInterruption.Restored, restored.Interruption);
        Assert.Equal(halfResumed.Revision + 1, restored.Revision);
        world.Resume();
        for (var attempt = 0; attempt < 15; attempt++)
        {
            _ = await world.AdvanceOneTickNonBlockingAsync();
            await Task.Delay(3);
        }

        var waitingForBoth = Assert.Single(world.Conversations);
        Assert.Equal(AgentConversationStatus.Suspended, waitingForBoth.Status);
        Assert.Equal(InviteeId, Assert.Single(waitingForBoth.ResumeAcceptedBy));
        Assert.Empty(provider.TurnRequests);
        Assert.Contains(provider.PlanningRequests, request => request.Observation.InhabitantId == InitiatorId &&
            request.Observation.Candidates.Any(candidate => candidate.Id.StartsWith("conversation_resume:", StringComparison.Ordinal)));

        var consentingProvider = ConsentReviewProvider();
        using var bothResume = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(encoded), _ => consentingProvider);
        bothResume.Resume();
        for (var attempt = 0; attempt < 60 && bothResume.Conversations.Single().Turns.Count == 0; attempt++)
        {
            _ = await bothResume.AdvanceOneTickNonBlockingAsync();
            await Task.Delay(3);
        }
        Assert.NotEmpty(Assert.Single(bothResume.Conversations).Turns);
        Assert.Contains(consentingProvider.PlanningRequests, request => request.Observation.InhabitantId == InitiatorId &&
            request.Observation.Candidates.Any(candidate => candidate.Id.StartsWith("conversation_resume:", StringComparison.Ordinal)));
        Assert.Contains(consentingProvider.PlanningRequests, request => request.Observation.InhabitantId == InviteeId &&
            request.Observation.Candidates.Any(candidate => candidate.Id.StartsWith("conversation_resume:", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData(AgentConversationInterruption.UrgentNeed)]
    [InlineData(AgentConversationInterruption.Disconnected)]
    public void UnsafePendingInvitationClosesInsteadOfGrantingResume(AgentConversationInterruption interruption)
    {
        var proposal = AgentConversationRules.Propose("conversation:unsafe-invitation", InitiatorId, InviteeId, 0, 1);
        var interrupted = AgentConversationRules.Suspend(proposal, interruption, 1);

        Assert.Equal(AgentConversationStatus.Closed, interrupted.Status);
        Assert.Equal("withdrawn", interrupted.Outcome);
        Assert.Empty(interrupted.Turns);
        Assert.Equal(InitiatorId, Assert.Single(interrupted.AcceptedParticipantIds));
        Assert.False(AgentConversationRules.TryResume(interrupted, InviteeId, 1, 1, out _, out _));
        AgentConversationRules.Validate(interrupted, 1);
    }

    [Fact]
    public void UnacceptedSuspendedInvitationCannotPassValidationOrResume()
    {
        var proposal = AgentConversationRules.Propose("conversation:invalid-suspended", InitiatorId, InviteeId, 0, 1);
        var invalid = proposal with
        {
            Status = AgentConversationStatus.Suspended,
            Interruption = AgentConversationInterruption.OwnerPaused,
        };

        Assert.False(AgentConversationRules.TryResume(invalid, InitiatorId, 1, 1, out _, out _));
        Assert.Throws<InvalidDataException>(() => AgentConversationRules.Validate(invalid, 1));
    }

    private static ConversationProvider ConsentReviewProvider(string? onlyResumingAgentId = null) =>
        new(new HashSet<string>(StringComparer.Ordinal) { InitiatorId, InviteeId })
        {
            OnlyResumingAgentId = onlyResumingAgentId,
        };

    private static AgentConversation ReviewInvitation(PrivateWorldRuntimeState state, string id) =>
        AgentConversationRules.Propose(id, InitiatorId, InviteeId, state.Society.Society.WorldTick,
            state.Society.Society.RunEpoch);

    private static PrivateWorldRuntimeState WithReviewInvitation(PrivateWorldRuntimeState state, AgentConversation proposal) =>
        state with
        {
            Conversations = [proposal],
            ConversationBudgets = [new AgentConversationDailyBudget(InitiatorId,
                state.Society.Society.WorldTick / state.Society.Society.Config.TicksPerWorldDay, 1)],
        };
}

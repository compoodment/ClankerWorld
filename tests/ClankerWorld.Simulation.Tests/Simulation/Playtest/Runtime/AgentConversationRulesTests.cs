using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class AgentConversationRulesTests
{
    [Fact]
    public void SixPublicTurnsAlternateAndTrustRequiresSharedWrapUpConsent()
    {
        var conversation = AgentConversationRules.Propose("conversation:test", "agent-a", "agent-b", 0, 3);
        Assert.False(AgentConversationRules.TryAcceptProposal(conversation, "agent-a", 1, 3, out _));
        Assert.True(AgentConversationRules.TryAcceptProposal(conversation, "agent-b", 1, 3, out conversation));

        long worldTick = 1;
        for (var index = 0; index < AgentConversationRules.MaximumPublicTurns; index++)
        {
            Assert.True(AgentConversationRules.TryBeginTurn(conversation, worldTick, 3, out var started));
            var speaker = index % 2 == 0 ? "agent-a" : "agent-b";
            Assert.Equal(speaker, started.CurrentSpeakerId);
            var request = Request(started, AgentConversationPurpose.PublicTurn, speaker, [AgentConversationEffect.None]);
            var response = Response(request, $"Public turn {index + 1}.");
            var listeners = speaker == "agent-a" ? new[] { "agent-b", "listener" } : ["agent-a", "listener"];

            Assert.True(AgentConversationRules.TryAdmitTurn(started, request, response, listeners,
                worldTick + 1, 3, out conversation, out var appended));
            Assert.Equal(response.Text, appended!.Text);
            Assert.Equal(listeners.Order(StringComparer.Ordinal), appended.ListenerIds);
            AgentConversationRules.Validate(conversation, worldTick + 1);
            worldTick++;
        }

        Assert.Equal(AgentConversationStatus.WrapUp, conversation.Status);
        Assert.Equal(3, conversation.Turns.Count(turn => !turn.IsWrapUp && turn.SpeakerId == "agent-a"));
        Assert.Equal(3, conversation.Turns.Count(turn => !turn.IsWrapUp && turn.SpeakerId == "agent-b"));
        Assert.True(AgentConversationRules.TryBeginTurn(conversation, worldTick, 3, out var wrapUpRequestState));
        var wrapUp = Request(wrapUpRequestState, AgentConversationPurpose.WrapUp, "agent-a",
            [AgentConversationEffect.None, AgentConversationEffect.MutualTrust]);

        Assert.False(AgentConversationRules.TryAdmitTurn(wrapUpRequestState, wrapUp,
            Response(wrapUp, "I propose mutual trust.", AgentConversationEffect.MutualTrust),
            [], worldTick + 1, 3, out _, out _));
        Assert.True(AgentConversationRules.TryAdmitTurn(wrapUpRequestState, wrapUp,
            Response(wrapUp, "I propose mutual trust.", AgentConversationEffect.MutualTrust),
            ["agent-b", "listener"], worldTick + 1, 3, out conversation, out var wrapUpTurn));
        Assert.True(wrapUpTurn!.IsWrapUp);
        Assert.Equal(AgentConversationStatus.WrapUp, conversation.Status);

        Assert.True(AgentConversationRules.TryAcceptWrapUp(conversation, "agent-a", worldTick + 2,
            out conversation, out var agreedEarly));
        Assert.False(agreedEarly);
        Assert.Equal(AgentConversationStatus.WrapUp, conversation.Status);
        Assert.True(AgentConversationRules.TryAcceptWrapUp(conversation, "agent-b", worldTick + 3,
            out conversation, out var agreed));
        Assert.True(agreed);
        Assert.Equal("agreed", conversation.Outcome);
        Assert.Equal(AgentConversationEffect.MutualTrust, conversation.WrapUpEffect);
        AgentConversationRules.Validate(conversation, worldTick + 3);
    }

    [Fact]
    public void StaleOrUnapprovedProviderOutputCannotEnterPublicHistory()
    {
        var conversation = AcceptedConversation();
        Assert.True(AgentConversationRules.TryBeginTurn(conversation, 2, 5, out var started));
        var request = Request(started, AgentConversationPurpose.PublicTurn, "agent-a", [AgentConversationEffect.None]);
        var response = Response(request, "The reply is about something private.");

        Assert.False(AgentConversationRules.TryAdmitTurn(started, request with { WorldTick = 1 }, response,
            ["agent-b"], 3, 5, out _, out _));
        Assert.False(AgentConversationRules.TryAdmitTurn(started, request,
            response with { Effect = AgentConversationEffect.MutualTrust }, ["agent-b"], 3, 5, out _, out _));
        Assert.False(AgentConversationRules.TryAdmitTurn(started, request,
            Response(request, new string('x', AgentConversationRules.MaximumUtteranceCharacters + 1)),
            ["agent-b"], 3, 5, out _, out _));
        Assert.False(AgentConversationRules.TryAdmitTurn(started, request, response,
            ["agent-a", "agent-b"], 3, 5, out _, out _));

        Assert.True(AgentConversationRules.TryAdmitTurn(started, request, response,
            ["agent-b"], 3, 5, out var admitted, out _));
        Assert.False(AgentConversationRules.TryAdmitTurn(admitted, request, response,
            ["agent-b"], 4, 5, out _, out _));
    }

    [Fact]
    public void InterruptionNeedsBothAgentsToResumeWithoutReplayingAnUnacceptedTurn()
    {
        var conversation = AcceptedConversation();
        Assert.True(AgentConversationRules.TryBeginTurn(conversation, 2, 5, out var started));
        var request = Request(started, AgentConversationPurpose.PublicTurn, "agent-a", [AgentConversationEffect.None]);
        Assert.True(AgentConversationRules.TryAdmitTurn(started, request, Response(request, "First public turn."),
            ["agent-b"], 3, 5, out conversation, out _));
        conversation = AgentConversationRules.Suspend(conversation,
            AgentConversationInterruption.ProviderTimedOut, 4);

        Assert.Equal(AgentConversationStatus.Suspended, conversation.Status);
        Assert.Single(conversation.Turns);
        Assert.False(conversation.AwaitingWrapUp);
        Assert.True(AgentConversationRules.TryResume(conversation, "agent-a", 5, 6,
            out conversation, out var resumedEarly));
        Assert.False(resumedEarly);
        Assert.Equal(AgentConversationStatus.Suspended, conversation.Status);
        Assert.True(AgentConversationRules.TryResume(conversation, "agent-b", 6, 6,
            out conversation, out var resumed));
        Assert.True(resumed);
        Assert.Equal(AgentConversationStatus.Ready, conversation.Status);
        Assert.Equal("agent-b", conversation.CurrentSpeakerId);
        Assert.Equal(6, conversation.RunEpoch);
        Assert.Single(conversation.Turns);
        Assert.False(AgentConversationRules.TryResume(conversation, "agent-a", 7, 6, out _, out _));
        AgentConversationRules.Validate(conversation, 6);
    }

    [Fact]
    public void RefusalAndDeadlineCloseWithoutStartingPublicDialogue()
    {
        var refused = AgentConversationRules.Propose("conversation:refused", "agent-a", "agent-b", 0, 1);
        Assert.False(AgentConversationRules.TryDeclineProposal(refused, "agent-a", 1, out _));
        Assert.True(AgentConversationRules.TryDeclineProposal(refused, "agent-b", 1, out refused));
        Assert.Equal(AgentConversationStatus.Closed, refused.Status);
        Assert.Equal("refused", refused.Outcome);
        Assert.Empty(refused.Turns);
        AgentConversationRules.Validate(refused, 1);

        var expired = AgentConversationRules.Propose("conversation:expired", "agent-a", "agent-b", 0, 1);
        Assert.False(AgentConversationRules.TryExpireProposal(expired, 30, out _));
        Assert.True(AgentConversationRules.TryExpireProposal(expired, 31, out expired));
        Assert.Equal("deadline", expired.Outcome);
        Assert.Empty(expired.Turns);
        AgentConversationRules.Validate(expired, 31);
    }

    [Fact]
    public void DisagreeingWithAProviderSuggestedWrapUpClosesWithoutApplyingItsProposal()
    {
        var conversation = AcceptedConversation();
        long worldTick = 2;
        for (var index = 0; index < AgentConversationRules.MaximumPublicTurns; index++)
        {
            Assert.True(AgentConversationRules.TryBeginTurn(conversation, worldTick, 5, out var started));
            var speaker = index % 2 == 0 ? "agent-a" : "agent-b";
            var request = Request(started, AgentConversationPurpose.PublicTurn, speaker, [AgentConversationEffect.None]);
            Assert.True(AgentConversationRules.TryAdmitTurn(started, request,
                Response(request, $"Public turn {index + 1}."), [speaker == "agent-a" ? "agent-b" : "agent-a"],
                worldTick + 1, 5, out conversation, out _));
            worldTick++;
        }

        Assert.True(AgentConversationRules.TryBeginTurn(conversation, worldTick, 5, out var wrapUpState));
        var wrapUpRequest = Request(wrapUpState, AgentConversationPurpose.WrapUp, "agent-a",
            [AgentConversationEffect.None, AgentConversationEffect.MutualTrust]);
        Assert.True(AgentConversationRules.TryAdmitTurn(wrapUpState, wrapUpRequest,
            Response(wrapUpRequest, "The speaker asks for trust.", AgentConversationEffect.MutualTrust),
            ["agent-b"], worldTick + 1, 5, out conversation, out _));
        Assert.True(AgentConversationRules.TryAcceptWrapUp(conversation, "agent-a", worldTick + 2,
            out conversation, out var agreed));
        Assert.False(agreed);
        Assert.True(AgentConversationRules.TryDeclineWrapUp(conversation, "agent-b", worldTick + 3,
            out conversation));
        Assert.Equal(AgentConversationStatus.Closed, conversation.Status);
        Assert.Equal("disagreed", conversation.Outcome);
        Assert.Equal(AgentConversationEffect.MutualTrust, conversation.WrapUpEffect);
        Assert.Single(conversation.WrapUpAcceptedBy);
        AgentConversationRules.Validate(conversation, worldTick + 3);
    }

    [Fact]
    public void RefusedStartsStillConsumeInitiatorAllowanceAndReceiverCannotExceedTheirOwnCap()
    {
        IReadOnlyList<AgentConversationDailyBudget> budgets = [];
        budgets = Reserve(budgets, "agent-a", 7);
        budgets = Reserve(budgets, "agent-a", 7);
        Assert.False(AgentConversationRules.CanStartToday(budgets, "agent-a", 7));
        Assert.Throws<InvalidOperationException>(() => AgentConversationRules.ReserveToday(budgets, "agent-a", 7));

        budgets = Reserve(budgets, "agent-b", 7);
        Assert.True(AgentConversationRules.CanStartToday(budgets, "agent-b", 7));
        Assert.True(AgentConversationRules.CanStartToday(budgets, "agent-a", 8));
    }

    [Fact]
    public void TimeoutsMapToABoundedPublicInterruptionCategory()
    {
        Assert.Equal(AgentConversationInterruption.ProviderTimedOut,
            AgentConversationFailureClassifier.Classify(new TimeoutException("private provider detail")));
    }

    private static IReadOnlyList<AgentConversationDailyBudget> Reserve(
        IReadOnlyList<AgentConversationDailyBudget> budgets,
        string agentId,
        long day)
    {
        var next = AgentConversationRules.ReserveToday(budgets, agentId, day);
        return [.. budgets.Where(item => item.AgentId != agentId || item.WorldDay != day), next];
    }

    private static AgentConversation AcceptedConversation()
    {
        var proposed = AgentConversationRules.Propose("conversation:accepted", "agent-a", "agent-b", 0, 5);
        Assert.True(AgentConversationRules.TryAcceptProposal(proposed, "agent-b", 1, 5, out var accepted));
        return accepted;
    }

    private static AgentConversationTurnRequest Request(
        AgentConversation conversation,
        AgentConversationPurpose purpose,
        string speakerId,
        IReadOnlyList<AgentConversationEffect> allowedEffects)
    {
        var otherId = speakerId == conversation.InitiatorId ? conversation.InviteeId : conversation.InitiatorId;
        return new AgentConversationTurnRequest(
            Guid.NewGuid().ToString("N"), conversation.Id, conversation.Revision, conversation.RunEpoch,
            conversation.LastUpdatedTick, purpose, speakerId, speakerId, otherId, otherId,
            "curious", "learn about the valley", conversation.Turns, allowedEffects);
    }

    private static AgentConversationTurnResponse Response(
        AgentConversationTurnRequest request,
        string text,
        AgentConversationEffect effect = AgentConversationEffect.None) => new(
            request.RequestId, request.ConversationId, request.Revision, request.RunEpoch,
            request.SpeakerId, text, AgentConversationDisposition.Continue, effect);
}

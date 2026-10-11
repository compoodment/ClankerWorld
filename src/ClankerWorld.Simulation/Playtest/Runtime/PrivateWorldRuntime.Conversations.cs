using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private const int MaximumConversationProviderCallsPerTick = 2;
    private static readonly TimeSpan ConversationProviderTimeout = TimeSpan.FromSeconds(45);

    public IReadOnlyList<AgentConversation> Conversations => conversations
        .OrderByDescending(item => item.LastUpdatedTick)
        .ThenBy(item => item.Id, StringComparer.Ordinal)
        .ToArray();

    public IReadOnlyList<AgentConversationDailyBudget> ConversationBudgets => conversationBudgets
        .OrderBy(item => item.AgentId, StringComparer.Ordinal)
        .ToArray();

    private void SuspendRestoredConversations() =>
        conversationSystem.SuspendRestoredConversations(conversationWorld);

    private bool HasExplicitConversationProvider(string agentId)
    {
        if (providerFactory is null) return false;
        try
        {
            return providerFactory(agentId) is IAgentConversationProvider provider &&
                provider.CanSpeakAs(agentId);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return false;
        }
    }

    private AgentConversation? ConversationFor(string agentId) =>
        conversationSystem.ConversationFor(agentId);

    private bool IsConversationBusy(string agentId) =>
        conversationSystem.IsConversationBusy(agentId);

    private bool ShouldDispatchConversationChoice(string agentId) =>
        conversationSystem.ShouldDispatchConversationChoice(agentId);

    private string? ConversationChoiceContextFor(string agentId) =>
        conversationSystem.ConversationChoiceContextFor(agentId);

    private long CurrentConversationWorldDay =>
        WorldCalendarRules.FromTick(WorldTick, worldSystems.Config).DayIndex;

    private bool HasConversationAllowance(string agentId) =>
        conversationSystem.HasConversationAllowance(conversationWorld, agentId);

    private List<CognitionCandidate> ConversationCandidates(string agentId) =>
        conversationSystem.ConversationCandidates(conversationWorld, agentId);

    private bool ApplyConversationCandidate(string agentId, string candidateId) =>
        conversationSystem.ApplyConversationCandidate(conversationWorld, agentId, candidateId);

    private bool ProposeConversation(string initiatorId, string inviteeId) =>
        conversationSystem.ProposeConversation(conversationWorld, initiatorId, inviteeId);

    private bool AcceptConversation(string participantId, string conversationId) =>
        conversationSystem.AcceptConversation(conversationWorld, participantId, conversationId);

    private bool DeclineConversation(string participantId, string conversationId) =>
        conversationSystem.DeclineConversation(conversationWorld, participantId, conversationId);

    private bool ResumeConversation(string participantId, string conversationId) =>
        conversationSystem.ResumeConversation(conversationWorld, participantId, conversationId);

    private bool EndConversation(string participantId, string conversationId) =>
        conversationSystem.EndConversation(conversationWorld, participantId, conversationId);

    private bool AcceptConversationWrapUp(string participantId, string conversationId) =>
        conversationSystem.AcceptConversationWrapUp(conversationWorld, participantId, conversationId);

    private bool DeclineConversationWrapUp(string participantId, string conversationId) =>
        conversationSystem.DeclineConversationWrapUp(conversationWorld, participantId, conversationId);

    private bool CanContinueConversation(AgentConversation conversation) =>
        conversationSystem.CanContinueConversation(conversationWorld, conversation);

    private void ReserveConversationAllowance(string agentId) =>
        conversationSystem.ReserveConversationAllowance(conversationWorld, agentId);

    private void TrimConversationHistory() =>
        conversationSystem.TrimConversationHistory(conversationWorld);

    private void UpdateConversationsForTick(long worldTick) =>
        conversationSystem.UpdateConversationsForTick(conversationWorld, worldTick);

    private bool IsUrgentConversationNeed(AgentConversation conversation) =>
        ConversationSystem.IsUrgentConversationNeed(conversationWorld, conversation);

    private bool AreConversationParticipantsTogether(AgentConversation conversation) =>
        conversationSystem.AreConversationParticipantsTogether(conversationWorld, conversation);

    private string[] ConversationListeners(AgentConversation conversation, string speakerId) =>
        ConversationSystem.ConversationListeners(conversationWorld, conversation, speakerId);

    private void ApplyConversationTurnOutcome(PendingConversationTurn pending,
        ConversationTurnOutcome outcome, long worldTick, Func<PendingConversationTurn, bool> providerRouteIsCurrent) =>
        conversationSystem.ApplyConversationTurnOutcome(conversationWorld, pending.Request, outcome.Response, outcome.Failure,
            worldTick, () => providerRouteIsCurrent(pending));

    private void ExtractConversationMemoryClaims(AgentConversationTurn turn) =>
        ConversationSystem.ExtractConversationMemoryClaims(conversationWorld, turn);

    private void CompleteConversationTurns(
        IReadOnlyList<PendingConversationTurn> completed,
        long worldTick,
        Func<PendingConversationTurn, bool> providerRouteIsCurrent)
    {
        foreach (var pending in completed.OrderBy(item => item.Request.ConversationId, StringComparer.Ordinal))
        {
            if (!pending.Task.IsCompleted) continue;
            ApplyConversationTurnOutcome(
                pending,
                pending.Task.GetAwaiter().GetResult(),
                worldTick,
                providerRouteIsCurrent);
        }
    }

    private void StartConversationTurns()
    {
        if (providerFactory is null) return;
        var capacity = Math.Max(0, MaximumConversationProviderCallsPerTick - pendingConversationTurns.Count);
        foreach (var conversation in conversations
                     .Where(item => item.Status == AgentConversationStatus.Ready ||
                         item.Status == AgentConversationStatus.WrapUp && !item.Turns.Any(turn => turn.IsWrapUp))
                     .OrderBy(item => item.CreatedTick)
                     .ThenBy(item => item.Id, StringComparer.Ordinal)
                     .Take(capacity).ToArray())
        {
            if (!CanContinueConversation(conversation))
            {
                SuspendConversation(conversation.Id, AgentConversationInterruption.UrgentNeed);
                continue;
            }

            IAgentConversationProvider provider;
            try
            {
                provider = providerFactory(conversation.CurrentSpeakerId ?? conversation.InitiatorId) as
                    IAgentConversationProvider ?? throw new ProviderConversationUnavailableException("The personal conversation provider is unavailable.");
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                SuspendConversation(conversation.Id, AgentConversationFailureClassifier.Classify(exception));
                continue;
            }

            var speakerId = conversation.Status == AgentConversationStatus.WrapUp
                ? conversation.InitiatorId
                : conversation.CurrentSpeakerId!;
            long providerEpoch;
            try
            {
                if (!provider.CanSpeakAs(speakerId))
                {
                    SuspendConversation(conversation.Id, AgentConversationInterruption.ProviderUnavailable);
                    continue;
                }
                providerEpoch = provider.ProviderEpoch;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                SuspendConversation(conversation.Id, AgentConversationFailureClassifier.Classify(exception));
                continue;
            }

            if (!AgentConversationRules.TryBeginTurn(conversation, WorldTick, society.Checkpoint.RunEpoch, out var started))
                continue;
            AgentConversationTurnRequest request;
            try
            {
                request = CreateConversationRequest(started, speakerId) with { ExpectedProviderEpoch = providerEpoch };
                request.Validate();
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                SuspendConversation(conversation.Id, AgentConversationFailureClassifier.Classify(exception));
                continue;
            }
            var cancellation = new CancellationTokenSource();
            // The provider runs on this gate-holding thread until its first
            // await, which comes after it reserves a paid model call.
            var outerInvocation = providerInvocationUnderGate;
            providerInvocationUnderGate = this;
            Task<ConversationTurnOutcome> task;
            try
            {
                task = RunConversationProviderAsync(provider, request, cancellation.Token);
            }
            finally
            {
                providerInvocationUnderGate = outerInvocation;
            }
            conversationSystem.SetConversation(started);
            pendingConversationTurns.Add(started.Id, new PendingConversationTurn(
                request, task, cancellation, providerEpoch));
            AppendEvent("conversation_turn_started", $"{started.Id}:{speakerId}");
        }
    }

    private AgentConversationTurnRequest CreateConversationRequest(AgentConversation conversation, string speakerId)
    {
        var otherId = speakerId == conversation.InitiatorId ? conversation.InviteeId : conversation.InitiatorId;
        var speaker = society.Checkpoint.GetInhabitant(speakerId);
        var other = society.Checkpoint.GetInhabitant(otherId);
        var physical = inhabitants[speakerId];
        var purpose = conversation.Kind == AgentConversationKind.MarriageSurname
            ? AgentConversationPurpose.SurnameChoice
            : conversation.AwaitingWrapUp
            ? AgentConversationPurpose.WrapUp
            : AgentConversationPurpose.PublicTurn;
        return new AgentConversationTurnRequest(
            Guid.NewGuid().ToString("N"),
            conversation.Id,
            conversation.Revision,
            society.Checkpoint.RunEpoch,
            WorldTick,
            purpose,
            speakerId,
            speaker.Name,
            otherId,
            other.Name,
            physical.Personality,
            physical.Aspiration,
            conversation.Turns,
            purpose == AgentConversationPurpose.WrapUp
                ? CanProposeMarriage(conversation)
                    ? [AgentConversationEffect.None, AgentConversationEffect.MutualTrust, AgentConversationEffect.Marriage]
                    : [AgentConversationEffect.None, AgentConversationEffect.MutualTrust]
                : [AgentConversationEffect.None])
        {
            RequestedActivity = RequestedConversationActivity(conversation, speakerId),
            AllowedSurnames = purpose == AgentConversationPurpose.SurnameChoice
                ? AgentMarriageRules.AllowedSurnames(marriages.Single(item => item.SurnameConversationId == conversation.Id))
                : [],
        };
    }

    private static async Task<ConversationTurnOutcome> RunConversationProviderAsync(
        IAgentConversationProvider provider,
        AgentConversationTurnRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await AgentConversationProviderExecution.SpeakAsync(
                provider, request, ConversationProviderTimeout, cancellationToken).ConfigureAwait(false);
            return new ConversationTurnOutcome(response, null);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return new ConversationTurnOutcome(null, exception);
        }
    }

    private void SuspendConversation(string conversationId, AgentConversationInterruption interruption) =>
        conversationSystem.SuspendConversation(conversationWorld, conversationId, interruption);

    private void SuspendAllConversations(AgentConversationInterruption interruption) =>
        conversationSystem.SuspendAllConversations(conversationWorld, interruption);

    private void CancelPendingConversationTurn(
        string conversationId,
        AgentConversationInterruption interruption,
        bool suspendCurrent = true,
        bool underRuntimeGate = true)
    {
        if (!pendingConversationTurns.Remove(conversationId, out var pending)) return;
        CancelProviderCall(pending.Cancellation, underRuntimeGate);
        _ = pending.Task.ContinueWith(_ => pending.Cancellation.Dispose(), TaskScheduler.Default);
        if (suspendCurrent && conversations.FirstOrDefault(item => item.Id == conversationId) is
            { Status: AgentConversationStatus.AwaitingSpeaker } current &&
            current.Revision == pending.Request.Revision)
            SuspendConversation(conversationId, interruption);
    }

    private void ReconcilePendingConversationTurns()
    {
        foreach (var (id, pending) in pendingConversationTurns.ToArray())
        {
            var conversation = conversations.FirstOrDefault(item => item.Id == id);
            if (conversation is null || conversation.Status != AgentConversationStatus.AwaitingSpeaker ||
                conversation.Revision != pending.Request.Revision ||
                society.Checkpoint.RunEpoch != pending.Request.RunEpoch)
            {
                CancelPendingConversationTurn(id, AgentConversationInterruption.Disconnected, suspendCurrent: false);
                continue;
            }
            try
            {
                var provider = providerFactory?.Invoke(pending.Request.SpeakerId) as IAgentConversationProvider;
                if (provider is null || provider.ProviderEpoch != pending.ProviderEpoch || !provider.CanSpeakAs(pending.Request.SpeakerId))
                    CancelPendingConversationTurn(id, AgentConversationInterruption.ProviderUnavailable);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                CancelPendingConversationTurn(id, AgentConversationInterruption.ProviderUnavailable);
            }
        }
    }

    private bool IsConversationTurnProviderCurrent(PendingConversationTurn pending)
    {
        try
        {
            var provider = providerFactory?.Invoke(pending.Request.SpeakerId) as IAgentConversationProvider;
            return provider is not null &&
                provider.ProviderEpoch == pending.ProviderEpoch &&
                provider.CanSpeakAs(pending.Request.SpeakerId);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return false;
        }
    }

    private void CancelNoLongerAwaitingConversationTurns()
    {
        foreach (var (id, pending) in pendingConversationTurns.ToArray())
        {
            var conversation = conversations.FirstOrDefault(item => item.Id == id);
            if (conversation is null || conversation.Status != AgentConversationStatus.AwaitingSpeaker ||
                conversation.Revision != pending.Request.Revision)
                CancelPendingConversationTurn(id, AgentConversationInterruption.UrgentNeed, suspendCurrent: false);
        }
    }

    private static string ToWire(AgentConversationInterruption interruption) => ConversationSystem.ToWire(interruption);


}

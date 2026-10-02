using System.Security.Cryptography;
using System.Text;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private const int ConversationHearingRange = 3;
    private const int MaximumConversationProviderCallsPerTick = 2;
    private static readonly TimeSpan ConversationProviderTimeout = TimeSpan.FromSeconds(45);

    public IReadOnlyList<AgentConversation> Conversations => conversations
        .OrderByDescending(item => item.LastUpdatedTick)
        .ThenBy(item => item.Id, StringComparer.Ordinal)
        .ToArray();

    public IReadOnlyList<AgentConversationDailyBudget> ConversationBudgets => conversationBudgets
        .OrderBy(item => item.AgentId, StringComparer.Ordinal)
        .ToArray();

    private void SuspendRestoredConversations()
    {
        var activeIds = society.Checkpoint.Inhabitants
            .Where(item => item.Status == SocietyInhabitantStatus.Active)
            .Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var changed = false;
        for (var index = 0; index < conversations.Count; index++)
        {
            var current = conversations[index];
            if (current.Status == AgentConversationStatus.Closed) continue;
            var next = !activeIds.Contains(current.InitiatorId) || !activeIds.Contains(current.InviteeId)
                ? AgentConversationRules.CloseUnavailable(current, WorldTick)
                : current.Status == AgentConversationStatus.Proposed
                    ? current
                    : current.Status == AgentConversationStatus.Suspended
                        ? current with
                        {
                            ResumeAcceptedBy = [],
                            Interruption = AgentConversationInterruption.Restored,
                            Revision = checked(current.Revision + 1),
                            LastUpdatedTick = WorldTick,
                        }
                        : AgentConversationRules.Suspend(current, AgentConversationInterruption.Restored, WorldTick);
            if (next == current) continue;
            conversations[index] = next;
            changed = true;
        }
        if (changed) checkpointSchemaVersion = StateSchemaVersion;
    }

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

    private AgentConversation? ConversationFor(string agentId) => conversations
        .Where(item => item.Status != AgentConversationStatus.Closed && AgentConversationRules.IsParticipant(item, agentId))
        .OrderBy(item => item.CreatedTick)
        .ThenBy(item => item.Id, StringComparer.Ordinal)
        .FirstOrDefault();

    private bool IsConversationBusy(string agentId) => ConversationFor(agentId) is { } conversation &&
        (conversation.Status is AgentConversationStatus.Ready or AgentConversationStatus.AwaitingSpeaker or AgentConversationStatus.WrapUp ||
         conversation.Status == AgentConversationStatus.Proposed && conversation.InitiatorId == agentId);

    private bool ShouldDispatchConversationChoice(string agentId) => ConversationFor(agentId) is { } conversation &&
        (conversation.Status == AgentConversationStatus.Proposed && conversation.InviteeId == agentId ||
         conversation.Status == AgentConversationStatus.Suspended && !conversation.ResumeAcceptedBy.Contains(agentId, StringComparer.Ordinal) ||
         conversation.Status == AgentConversationStatus.WrapUp && conversation.Turns.Any(turn => turn.IsWrapUp) &&
            !conversation.WrapUpAcceptedBy.Contains(agentId, StringComparer.Ordinal));

    private string? ConversationChoiceContextFor(string agentId)
    {
        var conversation = ConversationFor(agentId);
        return conversation is null || !ShouldDispatchConversationChoice(agentId)
            ? null
            : $"{agentId}|{conversation.Id}|{conversation.Revision}|{conversation.Status}";
    }

    private long CurrentConversationWorldDay =>
        WorldTick / society.Checkpoint.Config.TicksPerWorldDay;

    private bool HasConversationAllowance(string agentId) =>
        AgentConversationRules.CanStartToday(conversationBudgets, agentId, CurrentConversationWorldDay);

    private List<CognitionCandidate> ConversationCandidates(string agentId)
    {
        var result = new List<CognitionCandidate>();
        var current = ConversationFor(agentId);
        if (current is not null)
        {
            if (current.Status == AgentConversationStatus.Proposed && current.InviteeId == agentId)
            {
                if (HasConversationAllowance(agentId) && CanContinueConversation(current) &&
                    HasExplicitConversationProvider(current.InitiatorId) && HasExplicitConversationProvider(current.InviteeId))
                    result.Add(new CognitionCandidate(
                        $"conversation_accept:{current.Id}",
                        $"Accept {society.Checkpoint.GetInhabitant(current.InitiatorId).Name}'s invitation to talk.", 42));
                result.Add(new CognitionCandidate(
                    $"conversation_decline:{current.Id}",
                    "Decline the invitation to talk.", 72));
            }
            else if (current.Status == AgentConversationStatus.Suspended)
            {
                if (!current.ResumeAcceptedBy.Contains(agentId, StringComparer.Ordinal))
                {
                    result.Add(new CognitionCandidate(
                        $"conversation_resume:{current.Id}",
                        "Resume the conversation if the other person also agrees.", 45));
                }
                result.Add(new CognitionCandidate(
                    $"conversation_end:{current.Id}",
                    "End the interrupted conversation without agreement.", 75));
            }
            else if (current.Status == AgentConversationStatus.WrapUp && current.Turns.Any(turn => turn.IsWrapUp))
            {
                if (!current.WrapUpAcceptedBy.Contains(agentId, StringComparer.Ordinal))
                {
                    var wrapUp = current.Turns.Single(turn => turn.IsWrapUp);
                    var effect = current.WrapUpEffect == AgentConversationEffect.MutualTrust
                        ? "mutual trust: both people trust each other more"
                        : "none: no world change";
                    result.Add(new CognitionCandidate(
                        $"conversation_wrapup_accept:{current.Id}",
                        $"Accept this public wrap-up: \"{wrapUp.Text}\". Proposed effect: {effect}.", 45));
                    result.Add(new CognitionCandidate(
                        $"conversation_wrapup_decline:{current.Id}",
                        $"Decline this public wrap-up: \"{wrapUp.Text}\". Proposed effect: {effect}.", 75));
                }
            }
            return result;
        }

        if (!HasConversationAllowance(agentId) || !HasExplicitConversationProvider(agentId) ||
            inhabitants[agentId] is not { } state || NeedsUrgentFood(state) || NeedsUrgentWarmth(state))
            return result;

        foreach (var target in society.Checkpoint.Inhabitants
                     .Where(item => item.Id != agentId && item.Status == SocietyInhabitantStatus.Active &&
                         item.AgeBand != SocietyAgeBand.Infant)
                     .OrderBy(item => map.FootDistance(state.Position, inhabitants[item.Id].Position))
                     .ThenBy(item => item.Id, StringComparer.Ordinal))
        {
            if (!HasConversationAllowance(target.Id) || !HasExplicitConversationProvider(target.Id) ||
                ConversationFor(target.Id) is not null ||
                NeedsUrgentFood(inhabitants[target.Id]) || NeedsUrgentWarmth(inhabitants[target.Id]) ||
                !IsWithinInteractionRange(state.Position, inhabitants[target.Id].Position, ResourceInteractionRange))
                continue;
            result.Add(new CognitionCandidate(
                $"talk:{target.Id}",
                $"Talk with {target.Name}.", 58, target.Id));
        }
        return result;
    }

    private bool ApplyConversationCandidate(string agentId, string candidateId)
    {
        if (candidateId.StartsWith("talk:", StringComparison.Ordinal))
            return ProposeConversation(agentId, candidateId[5..]);
        if (candidateId.StartsWith("conversation_accept:", StringComparison.Ordinal))
            return AcceptConversation(agentId, candidateId[20..]);
        if (candidateId.StartsWith("conversation_decline:", StringComparison.Ordinal))
            return DeclineConversation(agentId, candidateId[21..]);
        if (candidateId.StartsWith("conversation_resume:", StringComparison.Ordinal))
            return ResumeConversation(agentId, candidateId[20..]);
        if (candidateId.StartsWith("conversation_end:", StringComparison.Ordinal))
            return EndConversation(agentId, candidateId[17..]);
        if (candidateId.StartsWith("conversation_wrapup_accept:", StringComparison.Ordinal))
            return AcceptConversationWrapUp(agentId, candidateId[27..]);
        if (candidateId.StartsWith("conversation_wrapup_decline:", StringComparison.Ordinal))
            return DeclineConversationWrapUp(agentId, candidateId[28..]);
        return false;
    }

    private bool ProposeConversation(string initiatorId, string inviteeId)
    {
        if (initiatorId == inviteeId || !inhabitants.TryGetValue(initiatorId, out var initiator) ||
            !inhabitants.TryGetValue(inviteeId, out var invitee) ||
            society.Checkpoint.GetInhabitant(initiatorId).Status != SocietyInhabitantStatus.Active ||
            society.Checkpoint.GetInhabitant(inviteeId).Status != SocietyInhabitantStatus.Active ||
            NeedsUrgentFood(initiator) || NeedsUrgentWarmth(initiator) ||
            NeedsUrgentFood(invitee) || NeedsUrgentWarmth(invitee) ||
            !IsWithinInteractionRange(initiator.Position, invitee.Position, ResourceInteractionRange) ||
            ConversationFor(initiatorId) is not null || ConversationFor(inviteeId) is not null ||
            !HasConversationAllowance(initiatorId) || !HasConversationAllowance(inviteeId) ||
            !HasExplicitConversationProvider(initiatorId) || !HasExplicitConversationProvider(inviteeId))
            return false;

        TrimConversationHistory();
        if (conversations.Count >= AgentConversationRules.MaximumSavedConversations) return false;
        ReserveConversationAllowance(initiatorId);
        var id = $"conversation:{WorldTick}:{initiatorId}:{inviteeId}";
        var conversation = AgentConversationRules.Propose(
            id, initiatorId, inviteeId, WorldTick, society.Checkpoint.RunEpoch);
        conversations.Add(conversation);
        checkpointSchemaVersion = StateSchemaVersion;
        AppendEvent("conversation_proposed", id);
        return true;
    }

    private bool AcceptConversation(string participantId, string conversationId)
    {
        var index = conversations.FindIndex(item => item.Id == conversationId);
        if (index < 0) return false;
        var current = conversations[index];
        // The current planning choice authorizes this still-open invitation
        // after a pause or restore. Its original deadline and budget remain.
        if (current.Status == AgentConversationStatus.Proposed)
            current = current with { RunEpoch = society.Checkpoint.RunEpoch };
        if (!CanContinueConversation(current) || !HasConversationAllowance(participantId) ||
            !HasExplicitConversationProvider(current.InitiatorId) || !HasExplicitConversationProvider(current.InviteeId) ||
            !AgentConversationRules.TryAcceptProposal(current, participantId, WorldTick,
                society.Checkpoint.RunEpoch, out var accepted))
            return false;
        ReserveConversationAllowance(participantId);
        conversations[index] = accepted;
        checkpointSchemaVersion = StateSchemaVersion;
        AppendEvent("conversation_accepted", current.Id);
        return true;
    }

    private bool DeclineConversation(string participantId, string conversationId)
    {
        var index = conversations.FindIndex(item => item.Id == conversationId);
        if (index < 0 || !AgentConversationRules.TryDeclineProposal(conversations[index], participantId,
                WorldTick, out var declined))
            return false;
        conversations[index] = declined;
        checkpointSchemaVersion = StateSchemaVersion;
        AppendEvent("conversation_closed", $"{conversationId}:refused");
        return true;
    }

    private bool ResumeConversation(string participantId, string conversationId)
    {
        var index = conversations.FindIndex(item => item.Id == conversationId);
        if (index < 0 || !CanContinueConversation(conversations[index]) ||
            !AgentConversationRules.TryResume(conversations[index], participantId, WorldTick,
                society.Checkpoint.RunEpoch, out var resumed, out var isResumed))
            return false;
        conversations[index] = resumed;
        checkpointSchemaVersion = StateSchemaVersion;
        AppendEvent(isResumed ? "conversation_resumed" : "conversation_resume_requested", conversationId);
        return true;
    }

    private bool EndConversation(string participantId, string conversationId)
    {
        var index = conversations.FindIndex(item => item.Id == conversationId);
        if (index < 0 || !AgentConversationRules.IsParticipant(conversations[index], participantId) ||
            !AgentConversationRules.TryEndSuspended(conversations[index], participantId, WorldTick, out var ended))
            return false;
        conversations[index] = ended;
        checkpointSchemaVersion = StateSchemaVersion;
        AppendEvent("conversation_closed", $"{conversationId}:withdrawn");
        return true;
    }

    private bool AcceptConversationWrapUp(string participantId, string conversationId)
    {
        var index = conversations.FindIndex(item => item.Id == conversationId);
        if (index < 0 || !CanContinueConversation(conversations[index]) ||
            !AgentConversationRules.TryAcceptWrapUp(conversations[index], participantId, WorldTick,
                out var accepted, out var newlyAgreed))
            return false;
        conversations[index] = accepted;
        if (newlyAgreed && accepted.WrapUpEffect == AgentConversationEffect.MutualTrust)
        {
            IncreaseTrust(accepted.InitiatorId, accepted.InviteeId, 1, "accepted_conversation");
            IncreaseTrust(accepted.InviteeId, accepted.InitiatorId, 1, "accepted_conversation");
        }
        checkpointSchemaVersion = StateSchemaVersion;
        AppendEvent(newlyAgreed ? "conversation_agreement_accepted" : "conversation_agreement_pending", conversationId);
        return true;
    }

    private bool DeclineConversationWrapUp(string participantId, string conversationId)
    {
        var index = conversations.FindIndex(item => item.Id == conversationId);
        if (index < 0 || !CanContinueConversation(conversations[index]) ||
            !AgentConversationRules.TryDeclineWrapUp(conversations[index], participantId, WorldTick, out var declined))
            return false;
        conversations[index] = declined;
        checkpointSchemaVersion = StateSchemaVersion;
        AppendEvent("conversation_closed", $"{conversationId}:disagreed");
        return true;
    }

    private bool CanContinueConversation(AgentConversation conversation)
    {
        if (conversation.Status == AgentConversationStatus.Proposed && WorldTick > conversation.ProposalDeadlineTick)
            return false;
        if (!inhabitants.TryGetValue(conversation.InitiatorId, out var first) ||
            !inhabitants.TryGetValue(conversation.InviteeId, out var second) ||
            !society.Checkpoint.Inhabitants.Any(item => item.Id == conversation.InitiatorId && item.Status == SocietyInhabitantStatus.Active) ||
            !society.Checkpoint.Inhabitants.Any(item => item.Id == conversation.InviteeId && item.Status == SocietyInhabitantStatus.Active))
            return false;
        return IsWithinInteractionRange(first.Position, second.Position, ResourceInteractionRange) &&
            !NeedsUrgentFood(first) && !NeedsUrgentWarmth(first) &&
            !NeedsUrgentFood(second) && !NeedsUrgentWarmth(second);
    }

    private void ReserveConversationAllowance(string agentId)
    {
        var reserved = AgentConversationRules.ReserveToday(conversationBudgets, agentId, CurrentConversationWorldDay);
        conversationBudgets.RemoveAll(item => item.AgentId == agentId);
        conversationBudgets.Add(reserved);
    }

    private void TrimConversationHistory()
    {
        if (conversations.Count < AgentConversationRules.MaximumSavedConversations) return;
        var oldestClosed = conversations
            .Where(item => item.Status == AgentConversationStatus.Closed)
            .OrderBy(item => item.LastUpdatedTick)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .FirstOrDefault();
        if (oldestClosed is null) return;

        var turnIds = oldestClosed.Turns.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        if ((society.Checkpoint.Beliefs ?? []).Any(item => item.SourceTurnId is { } turnId && turnIds.Contains(turnId)))
        {
            society.Apply(checkpoint => new SocietyOperationResult(checkpoint with
            {
                Beliefs = (checkpoint.Beliefs ?? []).Select(item =>
                    item.SourceTurnId is { } sourceTurnId && turnIds.Contains(sourceTurnId)
                        ? item with { SourceTurnId = null }
                        : item).ToArray(),
            }));
        }
        conversations.Remove(oldestClosed);
    }

    private void UpdateConversationsForTick(long worldTick)
    {
        var active = society.Checkpoint.Inhabitants
            .Where(item => item.Status == SocietyInhabitantStatus.Active)
            .Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var item in conversations.ToArray())
        {
            var index = conversations.FindIndex(current => current.Id == item.Id);
            if (index < 0 || item.Status == AgentConversationStatus.Closed) continue;
            AgentConversation next;
            string? eventKind = null;
            string? eventDetail = null;
            if (!active.Contains(item.InitiatorId) || !active.Contains(item.InviteeId))
            {
                next = AgentConversationRules.CloseUnavailable(item, worldTick);
                eventKind = "conversation_closed";
                eventDetail = $"{item.Id}:participant_unavailable";
            }
            else if (AgentConversationRules.TryExpireProposal(item, worldTick, out var expired))
            {
                next = expired;
                eventKind = "conversation_closed";
                eventDetail = $"{item.Id}:deadline";
            }
            else if (IsUrgentConversationNeed(item))
            {
                next = AgentConversationRules.Suspend(item, AgentConversationInterruption.UrgentNeed, worldTick);
                eventKind = "conversation_interrupted";
                eventDetail = $"{item.Id}:urgent_need";
            }
            else if (!AreConversationParticipantsTogether(item))
            {
                next = AgentConversationRules.Suspend(item, AgentConversationInterruption.Disconnected, worldTick);
                eventKind = "conversation_interrupted";
                eventDetail = $"{item.Id}:separated";
            }
            else continue;

            if (next == item) continue;
            conversations[index] = next;
            checkpointSchemaVersion = StateSchemaVersion;
            AppendEvent(eventKind!, eventDetail!);
        }
        if (conversationBudgets.RemoveAll(item => item.WorldDay <
                worldTick / society.Checkpoint.Config.TicksPerWorldDay) > 0)
            checkpointSchemaVersion = StateSchemaVersion;
    }

    private bool IsUrgentConversationNeed(AgentConversation conversation) =>
        inhabitants.TryGetValue(conversation.InitiatorId, out var first) &&
            (NeedsUrgentFood(first) || NeedsUrgentWarmth(first)) ||
        inhabitants.TryGetValue(conversation.InviteeId, out var second) &&
            (NeedsUrgentFood(second) || NeedsUrgentWarmth(second));

    private bool AreConversationParticipantsTogether(AgentConversation conversation) =>
        inhabitants.TryGetValue(conversation.InitiatorId, out var first) &&
        inhabitants.TryGetValue(conversation.InviteeId, out var second) &&
        IsWithinInteractionRange(first.Position, second.Position, ResourceInteractionRange);

    private string[] ConversationListeners(AgentConversation conversation, string speakerId)
    {
        var speakerPosition = inhabitants[speakerId].Position;
        var otherId = speakerId == conversation.InitiatorId ? conversation.InviteeId : conversation.InitiatorId;
        var hearers = society.Checkpoint.Inhabitants
            .Where(item => item.Status == SocietyInhabitantStatus.Active && item.Id != speakerId &&
                item.AgeBand != SocietyAgeBand.Infant && inhabitants.ContainsKey(item.Id))
            .Select(item => (item.Id, Distance: map.FootDistance(speakerPosition, inhabitants[item.Id].Position)))
            .Where(item => item.Id == otherId || item.Distance <= ConversationHearingRange)
            .OrderBy(item => item.Id == otherId ? 0 : 1)
            .ThenBy(item => item.Distance)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .Take(AgentConversationRules.MaximumListenersPerTurn)
            .Select(item => item.Id)
            .ToArray();
        return hearers;
    }

    private void ApplyConversationTurnOutcome(
        PendingConversationTurn pending,
        ConversationTurnOutcome outcome,
        long worldTick,
        Func<PendingConversationTurn, bool> providerRouteIsCurrent)
    {
        var index = conversations.FindIndex(item => item.Id == pending.Request.ConversationId);
        if (index < 0) return;
        var current = conversations[index];
        if (current.Status != AgentConversationStatus.AwaitingSpeaker || current.Revision != pending.Request.Revision)
            return;

        // The provider route may change independently of runtime events while
        // a proposed tick is being prepared. Reject the reply before it can
        // add public history, listener memories, or a structured effect.
        if (!providerRouteIsCurrent(pending))
        {
            conversations[index] = AgentConversationRules.Suspend(
                current, AgentConversationInterruption.ProviderUnavailable, worldTick);
            checkpointSchemaVersion = StateSchemaVersion;
            AppendEvent("conversation_interrupted", $"{current.Id}:provider_unavailable");
            return;
        }

        if (outcome.Failure is not null || outcome.Response is null)
        {
            var failure = outcome.Failure;
            var interruption = failure is null
                ? AgentConversationInterruption.ProviderRejected
                : AgentConversationFailureClassifier.Classify(failure);
            conversations[index] = AgentConversationRules.Suspend(current, interruption, worldTick);
            checkpointSchemaVersion = StateSchemaVersion;
            AppendEvent("conversation_interrupted", $"{current.Id}:{ToWire(interruption)}");
            return;
        }

        if (!AgentConversationRules.IsParticipant(current, outcome.Response.SpeakerId))
        {
            conversations[index] = AgentConversationRules.Suspend(
                current, AgentConversationInterruption.ProviderRejected, worldTick);
            checkpointSchemaVersion = StateSchemaVersion;
            AppendEvent("conversation_interrupted", $"{current.Id}:provider_rejected");
            return;
        }

        var listeners = ConversationListeners(current, outcome.Response.SpeakerId);
        if (!AgentConversationRules.TryAdmitTurn(current, pending.Request, outcome.Response, listeners,
                worldTick, society.Checkpoint.RunEpoch, out var admitted, out var appended) || appended is null)
        {
            conversations[index] = AgentConversationRules.Suspend(
                current, AgentConversationInterruption.ProviderRejected, worldTick);
            checkpointSchemaVersion = StateSchemaVersion;
            AppendEvent("conversation_interrupted", $"{current.Id}:provider_rejected");
            return;
        }

        conversations[index] = admitted;
        checkpointSchemaVersion = StateSchemaVersion;
        ExtractConversationMemoryClaims(appended);
        AppendEvent("conversation_turn_admitted", $"{current.Id}:{appended.Id}");
    }

    private void ExtractConversationMemoryClaims(AgentConversationTurn turn)
    {
        foreach (var ownerId in turn.ListenerIds)
        {
            if (ownerId == turn.SpeakerId ||
                society.Checkpoint.GetInhabitant(ownerId).Status != SocietyInhabitantStatus.Active ||
                (society.Checkpoint.Beliefs ?? []).Any(item => item.OwnerId == ownerId && item.SourceTurnId == turn.Id))
                continue;
            var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{turn.Id}|{ownerId}")))
                .ToLowerInvariant();
            var belief = new SocietyAgentBelief(
                $"conversation-memory:{digest}",
                ownerId,
                turn.Text,
                SocietyBeliefProvenance.Hearsay,
                5_000,
                WorldTick,
                turn.SpeakerId,
                SourceTurnId: turn.Id);
            society.Apply(checkpoint => new SocietyOperationResult(
                SocietyFixture.RecordAgentBelief(checkpoint, belief)));
        }
    }

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
            var outerStart = providerStartUnderGate;
            providerStartUnderGate = this;
            Task<ConversationTurnOutcome> task;
            try
            {
                task = RunConversationProviderAsync(provider, request, cancellation.Token);
            }
            finally
            {
                providerStartUnderGate = outerStart;
            }
            conversations[conversations.FindIndex(item => item.Id == started.Id)] = started;
            pendingConversationTurns.Add(started.Id, new PendingConversationTurn(
                request, task, cancellation, providerEpoch));
            checkpointSchemaVersion = StateSchemaVersion;
            AppendEvent("conversation_turn_started", $"{started.Id}:{speakerId}");
        }
    }

    private AgentConversationTurnRequest CreateConversationRequest(AgentConversation conversation, string speakerId)
    {
        var otherId = speakerId == conversation.InitiatorId ? conversation.InviteeId : conversation.InitiatorId;
        var speaker = society.Checkpoint.GetInhabitant(speakerId);
        var other = society.Checkpoint.GetInhabitant(otherId);
        var physical = inhabitants[speakerId];
        var purpose = conversation.AwaitingWrapUp
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
                ? [AgentConversationEffect.None, AgentConversationEffect.MutualTrust]
                : [AgentConversationEffect.None]);
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

    private void SuspendConversation(string conversationId, AgentConversationInterruption interruption)
    {
        var index = conversations.FindIndex(item => item.Id == conversationId);
        if (index < 0) return;
        var current = conversations[index];
        var next = AgentConversationRules.Suspend(current, interruption, WorldTick);
        if (next == current) return;
        conversations[index] = next;
        checkpointSchemaVersion = StateSchemaVersion;
        AppendEvent("conversation_interrupted", $"{conversationId}:{ToWire(interruption)}");
    }

    private void SuspendAllConversations(AgentConversationInterruption interruption)
    {
        foreach (var item in conversations.ToArray()) SuspendConversation(item.Id, interruption);
    }

    private void CancelPendingConversationTurn(
        string conversationId,
        AgentConversationInterruption interruption,
        bool suspendCurrent = true)
    {
        if (!pendingConversationTurns.Remove(conversationId, out var pending)) return;
        pending.Cancellation.Cancel();
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

    private static string ToWire(AgentConversationInterruption interruption) => interruption switch
    {
        AgentConversationInterruption.OwnerPaused => "owner_paused",
        AgentConversationInterruption.Disconnected => "disconnected",
        AgentConversationInterruption.UrgentNeed => "urgent_need",
        AgentConversationInterruption.ProviderUnavailable => "provider_unavailable",
        AgentConversationInterruption.ProviderTimedOut => "provider_timed_out",
        AgentConversationInterruption.ProviderRejected => "provider_rejected",
        AgentConversationInterruption.Restored => "restored",
        _ => "unknown",
    };
}

using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>Owns saved dialogue and marriage rules; live provider work stays in the coordinator.</summary>
public sealed class ConversationSystem(ConversationState state)
{
    private const int ConversationHearingRange = 3;
    private const int ResourceInteractionRange = 1;
    public ConversationState State { get; private set; } = state;
    public ConversationState CheckpointState => CheckpointSnapshots.Get(State);
    private ReadOnlyCollection<AgentConversation> conversations => State.Conversations;
    private ReadOnlyCollection<AgentMarriage> marriages => State.Marriages;
    private IReadOnlyList<AgentConversationDailyBudget> conversationBudgets => State.Budgets;

    private static readonly Derived<ConversationState, Dictionary<string, AgentConversation>> ConversationIndexByAgent =
        new(BuildConversationIndex, (left, right) => left.Count == right.Count &&
            left.All(pair => right.TryGetValue(pair.Key, out var value) && value == pair.Value));

    private static readonly Derived<ConversationState, ConversationState> CheckpointSnapshots =
        new(BuildCheckpointState, (left, right) => left.Conversations.SequenceEqual(right.Conversations) &&
            left.Budgets.SequenceEqual(right.Budgets) && left.Marriages.SequenceEqual(right.Marriages));

    private static ConversationState BuildCheckpointState(ConversationState snapshot)
    {
        var conversations = snapshot.Conversations.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
        var budgets = snapshot.Budgets.OrderBy(item => item.AgentId, StringComparer.Ordinal).ToArray();
        var marriages = snapshot.Marriages.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
        return snapshot.Conversations.SequenceEqual(conversations) && snapshot.Budgets.SequenceEqual(budgets) &&
            snapshot.Marriages.SequenceEqual(marriages) ? snapshot : new(conversations, budgets, marriages);
    }

    private static Dictionary<string, AgentConversation> BuildConversationIndex(ConversationState snapshot) =>
        snapshot.Conversations.Where(item => item.Status != AgentConversationStatus.Closed)
            .OrderBy(item => item.CreatedTick).ThenBy(item => item.Id, StringComparer.Ordinal)
            .SelectMany(item => new[] { (Agent: item.InitiatorId, Conversation: item), (Agent: item.InviteeId, Conversation: item) })
            .GroupBy(item => item.Agent, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Conversation, StringComparer.Ordinal);

    private int ConversationIndex(Predicate<AgentConversation> predicate) => State.Conversations.ToList().FindIndex(predicate);
    public int MarriageIndex(Predicate<AgentMarriage> predicate) => State.Marriages.ToList().FindIndex(predicate);
    public void SetConversation(AgentConversation value) => SetConversationAt(ConversationIndex(item => item.Id == value.Id), value);
    private void SetConversationAt(int index, AgentConversation value)
    {
        var next = State.Conversations.ToArray();
        next[index] = value;
        State = State.WithConversations(next);
    }
    public void SetMarriageAt(int index, AgentMarriage value)
    {
        var next = State.Marriages.ToArray();
        next[index] = value;
        State = State.WithMarriages(next);
    }
    private void AddConversation(AgentConversation value) => State = State.WithConversations(State.Conversations.Append(value));
    private void RemoveConversation(AgentConversation value) => State = State.WithConversations(State.Conversations.Where(item => item != value));
    private void AddMarriage(AgentMarriage value) => State = State.WithMarriages(State.Marriages.Append(value));
    private void AddBudget(AgentConversationDailyBudget value) => State = State.WithBudgets(State.Budgets.Append(value));
    private int RemoveBudgets(Predicate<AgentConversationDailyBudget> predicate)
    {
        var remaining = State.Budgets.Where(item => !predicate(item)).ToArray();
        var removed = State.Budgets.Count - remaining.Length;
        if (removed > 0) State = State.WithBudgets(remaining);
        return removed;
    }

    public void SuspendRestoredConversations(IConversationWorld world)
    {
        var activeIds = world.Society.Inhabitants
            .Where(item => item.Status == SocietyInhabitantStatus.Active)
            .Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        for (var index = 0; index < conversations.Count; index++)
        {
            var current = conversations[index];
            if (current.Status == AgentConversationStatus.Closed) continue;
            var next = !activeIds.Contains(current.InitiatorId) || !activeIds.Contains(current.InviteeId)
                ? AgentConversationRules.CloseUnavailable(current, world.WorldTick)
                : current.Status == AgentConversationStatus.Proposed
                    ? current
                    : current.Status == AgentConversationStatus.Suspended
                        ? current with
                        {
                            ResumeAcceptedBy = [],
                            Interruption = AgentConversationInterruption.Restored,
                            Revision = checked(current.Revision + 1),
                            LastUpdatedTick = world.WorldTick,
                        }
                        : AgentConversationRules.Suspend(current, AgentConversationInterruption.Restored, world.WorldTick);
            if (next == current) continue;
            SetConversationAt(index, next);
            RetainUnavailableSurnameSession(world, next);
        }
    }

    public AgentConversation? ConversationFor(string agentId) =>
        ConversationIndexByAgent.Get(State).GetValueOrDefault(agentId);

    public bool IsConversationBusy(string agentId) => ConversationFor(agentId) is { } conversation &&
        (conversation.Status is AgentConversationStatus.Ready or AgentConversationStatus.AwaitingSpeaker or AgentConversationStatus.WrapUp ||
         conversation.Status == AgentConversationStatus.Proposed && conversation.InitiatorId == agentId);

    public bool ShouldDispatchConversationChoice(string agentId) => ConversationFor(agentId) is { } conversation &&
        (conversation.Status == AgentConversationStatus.Proposed && conversation.InviteeId == agentId ||
         conversation.Status == AgentConversationStatus.Suspended && !conversation.ResumeAcceptedBy.Contains(agentId, StringComparer.Ordinal) ||
         conversation.Status == AgentConversationStatus.WrapUp && conversation.Turns.Any(turn => turn.IsWrapUp) &&
            !conversation.WrapUpAcceptedBy.Contains(agentId, StringComparer.Ordinal));

    public string? ConversationChoiceContextFor(string agentId)
    {
        var conversation = ConversationFor(agentId);
        if (conversation is null || !ShouldDispatchConversationChoice(agentId)) return null;
        var context = JsonSerializer.Serialize(new object[]
            { agentId, conversation.Id, conversation.Revision, conversation.Status });
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(context)));
    }

    public bool HasConversationAllowance(IConversationWorld world, string agentId) =>
        AgentConversationRules.CanStartToday(conversationBudgets, agentId, world.WorldDayAt(world.WorldTick));

    public List<CognitionCandidate> ConversationCandidates(IConversationWorld world, string agentId)
    {
        var result = new List<CognitionCandidate>();
        var current = ConversationFor(agentId);
        if (current is not null)
        {
            if (current.Status == AgentConversationStatus.Proposed && current.InviteeId == agentId)
            {
                if (HasConversationAllowance(world, agentId) && CanContinueConversation(world, current) &&
                    world.HasExplicitConversationProvider(current.InitiatorId) && world.HasExplicitConversationProvider(current.InviteeId))
                    result.Add(new CognitionCandidate(
                        $"conversation_accept:{current.Id}",
                        $"Accept {world.Society.GetInhabitant(current.InitiatorId).Name}'s invitation to talk.", 42));
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
                if (current.Kind == AgentConversationKind.Ordinary)
                    result.Add(new CognitionCandidate(
                        $"conversation_end:{current.Id}",
                        "End the interrupted conversation without agreement.", 75));
            }
            else if (current.Status == AgentConversationStatus.WrapUp && current.Turns.Any(turn => turn.IsWrapUp))
            {
                if (!current.WrapUpAcceptedBy.Contains(agentId, StringComparer.Ordinal))
                {
                    var wrapUp = current.Turns.Single(turn => turn.IsWrapUp);
                    var effect = current.WrapUpEffect switch
                    {
                        AgentConversationEffect.MutualTrust => "mutual trust: both people trust each other more",
                        AgentConversationEffect.Marriage => "marriage: agree to marry and choose a shared surname together",
                        _ => "none: no world change",
                    };
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

        if (!HasConversationAllowance(world, agentId) || !world.HasExplicitConversationProvider(agentId) ||
            world.Inhabitants[agentId] is not { } state || world.NeedsUrgentFood(state) || world.NeedsUrgentWarmth(state))
            return result;

        foreach (var target in world.Society.Inhabitants
                     .Where(item => item.Id != agentId && item.Status == SocietyInhabitantStatus.Active &&
                         item.AgeBand != SocietyAgeBand.Infant)
                     .OrderBy(item => world.FootDistance(state.Position, world.Inhabitants[item.Id].Position))
                     .ThenBy(item => item.Id, StringComparer.Ordinal))
        {
            if (!HasConversationAllowance(world, target.Id) || !world.HasExplicitConversationProvider(target.Id) ||
                ConversationFor(target.Id) is not null || world.IsAboardBoat(target.Id) ||
                world.NeedsUrgentFood(world.Inhabitants[target.Id]) || world.NeedsUrgentWarmth(world.Inhabitants[target.Id]) ||
                !world.IsWithinInteractionRange(state.Position, world.Inhabitants[target.Id].Position, ResourceInteractionRange))
                continue;
            result.Add(new CognitionCandidate(
                $"talk:{target.Id}",
                $"Talk with {target.Name}.", 58, target.Id));
        }
        return result;
    }

    public bool ApplyConversationCandidate(IConversationWorld world, string agentId, string candidateId)
    {
        if (candidateId.StartsWith("talk:", StringComparison.Ordinal))
            return ProposeConversation(world, agentId, candidateId[5..]);
        if (candidateId.StartsWith("conversation_accept:", StringComparison.Ordinal))
            return AcceptConversation(world, agentId, candidateId[20..]);
        if (candidateId.StartsWith("conversation_decline:", StringComparison.Ordinal))
            return DeclineConversation(world, agentId, candidateId[21..]);
        if (candidateId.StartsWith("conversation_resume:", StringComparison.Ordinal))
            return ResumeConversation(world, agentId, candidateId[20..]);
        if (candidateId.StartsWith("conversation_end:", StringComparison.Ordinal))
            return EndConversation(world, agentId, candidateId[17..]);
        if (candidateId.StartsWith("conversation_wrapup_accept:", StringComparison.Ordinal))
            return AcceptConversationWrapUp(world, agentId, candidateId[27..]);
        if (candidateId.StartsWith("conversation_wrapup_decline:", StringComparison.Ordinal))
            return DeclineConversationWrapUp(world, agentId, candidateId[28..]);
        return false;
    }

    public bool ProposeConversation(IConversationWorld world, string initiatorId, string inviteeId)
    {
        if (initiatorId == inviteeId || world.IsAboardBoat(initiatorId) || world.IsAboardBoat(inviteeId) ||
            !world.Inhabitants.TryGetValue(initiatorId, out var initiator) ||
            !world.Inhabitants.TryGetValue(inviteeId, out var invitee) ||
            world.Society.GetInhabitant(initiatorId).Status != SocietyInhabitantStatus.Active ||
            world.Society.GetInhabitant(inviteeId).Status != SocietyInhabitantStatus.Active ||
            world.NeedsUrgentFood(initiator) || world.NeedsUrgentWarmth(initiator) ||
            world.NeedsUrgentFood(invitee) || world.NeedsUrgentWarmth(invitee) ||
            !world.IsWithinInteractionRange(initiator.Position, invitee.Position, ResourceInteractionRange) ||
            ConversationFor(initiatorId) is not null || ConversationFor(inviteeId) is not null ||
            !HasConversationAllowance(world, initiatorId) || !HasConversationAllowance(world, inviteeId) ||
            !world.HasExplicitConversationProvider(initiatorId) || !world.HasExplicitConversationProvider(inviteeId))
            return false;

        TrimConversationHistory(world);
        if (conversations.Count >= AgentConversationRules.MaximumSavedConversations) return false;
        ReserveConversationAllowance(world, initiatorId);
        var participants = JsonSerializer.Serialize(new[] { initiatorId, inviteeId });
        var digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(participants)));
        var id = $"conversation:{world.WorldTick}:{digest}";
        var conversation = AgentConversationRules.Propose(
            id, initiatorId, inviteeId, world.WorldTick, world.Society.RunEpoch);
        AddConversation(conversation);

        world.AppendEvent("conversation_proposed", id);
        return true;
    }

    public bool AcceptConversation(IConversationWorld world, string participantId, string conversationId)
    {
        var index = ConversationIndex(item => item.Id == conversationId);
        if (index < 0) return false;
        var current = conversations[index];
        // The current planning choice authorizes this still-open invitation
        // after a pause or restore. Its original deadline and budget remain.
        if (current.Status == AgentConversationStatus.Proposed)
            current = current with { RunEpoch = world.Society.RunEpoch };
        if (!CanContinueConversation(world, current) || !HasConversationAllowance(world, participantId) ||
            !world.HasExplicitConversationProvider(current.InitiatorId) || !world.HasExplicitConversationProvider(current.InviteeId) ||
            !AgentConversationRules.TryAcceptProposal(current, participantId, world.WorldTick,
                world.Society.RunEpoch, out var accepted))
            return false;
        ReserveConversationAllowance(world, participantId);
        SetConversationAt(index, accepted);

        world.AppendEvent("conversation_accepted", current.Id);
        return true;
    }

    public bool DeclineConversation(IConversationWorld world, string participantId, string conversationId)
    {
        var index = ConversationIndex(item => item.Id == conversationId);
        if (index < 0 || !AgentConversationRules.TryDeclineProposal(conversations[index], participantId,
                world.WorldTick, out var declined))
            return false;
        SetConversationAt(index, declined);

        world.AppendEvent("conversation_closed", $"{conversationId}:refused");
        return true;
    }

    public bool ResumeConversation(IConversationWorld world, string participantId, string conversationId)
    {
        var index = ConversationIndex(item => item.Id == conversationId);
        if (index < 0 || !CanContinueConversation(world, conversations[index]) ||
            !AgentConversationRules.TryResume(conversations[index], participantId, world.WorldTick,
                world.Society.RunEpoch, out var resumed, out var isResumed))
            return false;
        SetConversationAt(index, resumed);

        world.AppendEvent(isResumed ? "conversation_resumed" : "conversation_resume_requested", conversationId);
        return true;
    }

    public bool EndConversation(IConversationWorld world, string participantId, string conversationId)
    {
        var index = ConversationIndex(item => item.Id == conversationId);
        if (index < 0 || !AgentConversationRules.IsParticipant(conversations[index], participantId) ||
            !AgentConversationRules.TryEndSuspended(conversations[index], participantId, world.WorldTick, out var ended))
            return false;
        SetConversationAt(index, ended);

        world.AppendEvent("conversation_closed", $"{conversationId}:withdrawn");
        return true;
    }

    public bool AcceptConversationWrapUp(IConversationWorld world, string participantId, string conversationId)
    {
        var index = ConversationIndex(item => item.Id == conversationId);
        if (index < 0 || !CanContinueConversation(world, conversations[index]) ||
            conversations[index].WrapUpEffect == AgentConversationEffect.Marriage && !CanProposeMarriage(world, conversations[index]) ||
            !AgentConversationRules.TryAcceptWrapUp(conversations[index], participantId, world.WorldTick,
                out var accepted, out var newlyAgreed))
            return false;
        SetConversationAt(index, accepted);
        if (newlyAgreed && accepted.WrapUpEffect == AgentConversationEffect.MutualTrust)
        {
            world.IncreaseTrust(accepted.InitiatorId, accepted.InviteeId, 1, "accepted_conversation");
            world.IncreaseTrust(accepted.InviteeId, accepted.InitiatorId, 1, "accepted_conversation");
        }
        if (newlyAgreed && accepted.WrapUpEffect == AgentConversationEffect.Marriage)
            StartAcceptedMarriage(world, accepted);

        world.AppendEvent(newlyAgreed ? "conversation_agreement_accepted" : "conversation_agreement_pending", conversationId);
        return true;
    }

    public bool DeclineConversationWrapUp(IConversationWorld world, string participantId, string conversationId)
    {
        var index = ConversationIndex(item => item.Id == conversationId);
        if (index < 0 || !CanContinueConversation(world, conversations[index]) ||
            !AgentConversationRules.TryDeclineWrapUp(conversations[index], participantId, world.WorldTick, out var declined))
            return false;
        SetConversationAt(index, declined);

        world.AppendEvent("conversation_closed", $"{conversationId}:disagreed");
        return true;
    }

    public bool CanContinueConversation(IConversationWorld world, AgentConversation conversation)
    {
        if (conversation.Status == AgentConversationStatus.Proposed && world.WorldTick > conversation.ProposalDeadlineTick)
            return false;
        if (!world.Inhabitants.TryGetValue(conversation.InitiatorId, out var first) ||
            !world.Inhabitants.TryGetValue(conversation.InviteeId, out var second) ||
            !world.Society.Inhabitants.Any(item => item.Id == conversation.InitiatorId && item.Status == SocietyInhabitantStatus.Active) ||
            !world.Society.Inhabitants.Any(item => item.Id == conversation.InviteeId && item.Status == SocietyInhabitantStatus.Active))
            return false;
        return (conversation.Kind == AgentConversationKind.MarriageSurname
                ? IsAcceptedSurnameSession(world, conversation)
                : world.IsWithinInteractionRange(first.Position, second.Position, ResourceInteractionRange)) &&
            !world.NeedsUrgentFood(first) && !world.NeedsUrgentWarmth(first) &&
            !world.NeedsUrgentFood(second) && !world.NeedsUrgentWarmth(second);
    }

    public void ReserveConversationAllowance(IConversationWorld world, string agentId)
    {
        var reserved = AgentConversationRules.ReserveToday(conversationBudgets, agentId, world.WorldDayAt(world.WorldTick));
        RemoveBudgets(item => item.AgentId == agentId);
        AddBudget(reserved);
    }

    public void TrimConversationHistory(IConversationWorld world)
    {
        if (conversations.Count < AgentConversationRules.MaximumSavedConversations) return;
        var oldestClosed = conversations
            .Where(item => item.Status == AgentConversationStatus.Closed && !world.ActiveTalkOrderUses(item.Id))
            .OrderBy(item => item.LastUpdatedTick)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .FirstOrDefault();
        if (oldestClosed is null) return;

        var turnIds = oldestClosed.Turns.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        if (world.Society.AllBeliefs().Any(item => item.SourceTurnId is { } turnId && turnIds.Contains(turnId)))
        {
            world.Apply(checkpoint => new SocietyOperationResult(checkpoint with
            {
                Beliefs = (checkpoint.Beliefs ?? []).Select(item =>
                    item.SourceTurnId is { } sourceTurnId && turnIds.Contains(sourceTurnId)
                        ? item with { SourceTurnId = null }
                        : item).ToArray(),
                ArchivedBeliefs = checkpoint.ArchivedBeliefs.Select(item =>
                    item.Belief.SourceTurnId is { } sourceTurnId && turnIds.Contains(sourceTurnId)
                        ? item with { Belief = item.Belief with { SourceTurnId = null } } : item).ToArray(),
            }));
        }
        RemoveConversation(oldestClosed);
    }

    public void UpdateConversationsForTick(IConversationWorld world, long worldTick)
    {
        var active = world.Society.Inhabitants
            .Where(item => item.Status == SocietyInhabitantStatus.Active)
            .Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var item in conversations.ToArray())
        {
            var index = ConversationIndex(current => current.Id == item.Id);
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
            else if (IsUrgentConversationNeed(world, item))
            {
                next = AgentConversationRules.Suspend(item, AgentConversationInterruption.UrgentNeed, worldTick);
                eventKind = "conversation_interrupted";
                eventDetail = $"{item.Id}:urgent_need";
            }
            else if (!AreConversationParticipantsTogether(world, item))
            {
                next = AgentConversationRules.Suspend(item, AgentConversationInterruption.Disconnected, worldTick);
                eventKind = "conversation_interrupted";
                eventDetail = $"{item.Id}:separated";
            }
            else continue;

            if (next == item) continue;
            SetConversationAt(index, next);
            RetainUnavailableSurnameSession(world, next);

            world.AppendEvent(eventKind!, eventDetail!);
        }
        var worldDay = world.WorldDayAt(worldTick);
        RemoveBudgets(item => item.WorldDay < worldDay);

    }

    public static bool IsUrgentConversationNeed(IConversationWorld world, AgentConversation conversation) =>
        world.Inhabitants.TryGetValue(conversation.InitiatorId, out var first) &&
            (world.NeedsUrgentFood(first) || world.NeedsUrgentWarmth(first)) ||
        world.Inhabitants.TryGetValue(conversation.InviteeId, out var second) &&
            (world.NeedsUrgentFood(second) || world.NeedsUrgentWarmth(second));

    public bool AreConversationParticipantsTogether(IConversationWorld world, AgentConversation conversation) =>
        conversation.Kind == AgentConversationKind.MarriageSurname ? IsAcceptedSurnameSession(world, conversation) :
        world.Inhabitants.TryGetValue(conversation.InitiatorId, out var first) &&
        world.Inhabitants.TryGetValue(conversation.InviteeId, out var second) &&
        world.IsWithinInteractionRange(first.Position, second.Position, ResourceInteractionRange);

    public static string[] ConversationListeners(IConversationWorld world, AgentConversation conversation, string speakerId)
    {
        var speakerPosition = world.Inhabitants[speakerId].Position;
        var otherId = speakerId == conversation.InitiatorId ? conversation.InviteeId : conversation.InitiatorId;
        if (conversation.Kind == AgentConversationKind.MarriageSurname) return [otherId];
        var hearers = world.Society.Inhabitants
            .Where(item => item.Status == SocietyInhabitantStatus.Active && item.Id != speakerId &&
                item.AgeBand != SocietyAgeBand.Infant && world.Inhabitants.ContainsKey(item.Id))
            .Select(item => (item.Id, Distance: world.FootDistance(speakerPosition, world.Inhabitants[item.Id].Position)))
            .Where(item => item.Id == otherId || item.Distance <= ConversationHearingRange)
            .OrderBy(item => item.Id == otherId ? 0 : 1)
            .ThenBy(item => item.Distance)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .Take(AgentConversationRules.MaximumListenersPerTurn)
            .Select(item => item.Id)
            .ToArray();
        return hearers;
    }

    public void ApplyConversationTurnOutcome(IConversationWorld world,
        AgentConversationTurnRequest request,
        AgentConversationTurnResponse? response, Exception? failure,
        long worldTick,
        Func<bool> providerRouteIsCurrent)
    {
        var index = ConversationIndex(item => item.Id == request.ConversationId);
        if (index < 0) return;
        var current = conversations[index];
        if (current.Status != AgentConversationStatus.AwaitingSpeaker || current.Revision != request.Revision)
            return;

        // The provider route may change independently of runtime events while
        // a proposed tick is being prepared. Reject the reply before it can
        // add public history, listener memories, or a structured effect.
        if (!providerRouteIsCurrent())
        {
            SetConversationAt(index, AgentConversationRules.Suspend(
                current, AgentConversationInterruption.ProviderUnavailable, worldTick));

            world.AppendEvent("conversation_interrupted", $"{current.Id}:provider_unavailable");
            return;
        }

        if (failure is not null || response is null)
        {
            var interruption = failure is null
                ? AgentConversationInterruption.ProviderRejected
                : AgentConversationFailureClassifier.Classify(failure);
            SetConversationAt(index, AgentConversationRules.Suspend(current, interruption, worldTick));

            world.AppendEvent("conversation_interrupted", $"{current.Id}:{ToWire(interruption)}");
            return;
        }

        if (!AgentConversationRules.IsParticipant(current, response.SpeakerId))
        {
            SetConversationAt(index, AgentConversationRules.Suspend(
                current, AgentConversationInterruption.ProviderRejected, worldTick));

            world.AppendEvent("conversation_interrupted", $"{current.Id}:provider_rejected");
            return;
        }

        var listeners = ConversationListeners(world, current, response.SpeakerId);
        if (current.Kind == AgentConversationKind.MarriageSurname && !IsAcceptedSurnameSession(world, current))
        {
            SetConversationAt(index, AgentConversationRules.Suspend(current, AgentConversationInterruption.Disconnected, worldTick));
            world.AppendEvent("conversation_interrupted", $"{current.Id}:separated");
            return;
        }
        if (!AgentConversationRules.TryAdmitTurn(current, request, response, listeners,
                worldTick, world.Society.RunEpoch, out var admitted, out var appended) || appended is null)
        {
            SetConversationAt(index, AgentConversationRules.Suspend(
                current, AgentConversationInterruption.ProviderRejected, worldTick));

            world.AppendEvent("conversation_interrupted", $"{current.Id}:provider_rejected");
            return;
        }

        if (admitted.Kind == AgentConversationKind.MarriageSurname && admitted.Status == AgentConversationStatus.Closed &&
            !CanCompleteMarriageSurname(world, admitted))
        {
            SetConversationAt(index, AgentConversationRules.Suspend(current, AgentConversationInterruption.ProviderRejected, worldTick));

            world.AppendEvent("marriage_surname_blocked", current.Id);
            return;
        }

        SetConversationAt(index, admitted);

        ExtractConversationMemoryClaims(world, appended);
        world.AppendEvent("conversation_turn_admitted", $"{current.Id}:{appended.Id}");
        if (admitted.Kind == AgentConversationKind.MarriageSurname && admitted.Status == AgentConversationStatus.Closed)
            CompleteMarriageSurname(world, admitted);
    }

    public static void ExtractConversationMemoryClaims(IConversationWorld world, AgentConversationTurn turn)
    {
        foreach (var ownerId in turn.ListenerIds)
        {
            if (ownerId == turn.SpeakerId ||
                world.Society.GetInhabitant(ownerId).Status != SocietyInhabitantStatus.Active ||
                world.Society.AllBeliefs().Any(item => item.OwnerId == ownerId && item.SourceTurnId == turn.Id))
                continue;
            var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{turn.Id}|{ownerId}")))
                .ToLowerInvariant();
            var belief = new SocietyAgentBelief(
                $"conversation-memory:{digest}",
                ownerId,
                turn.Text,
                SocietyBeliefProvenance.Hearsay,
                5_000,
                world.WorldTick,
                turn.SpeakerId,
                SourceTurnId: turn.Id);
            world.Apply(checkpoint => new SocietyOperationResult(
                SocietyFixture.RecordAgentBelief(checkpoint, belief)));
        }
    }

    public void SuspendConversation(IConversationWorld world, string conversationId, AgentConversationInterruption interruption)
    {
        var index = ConversationIndex(item => item.Id == conversationId);
        if (index < 0) return;
        var current = conversations[index];
        var next = AgentConversationRules.Suspend(current, interruption, world.WorldTick);
        if (next == current) return;
        SetConversationAt(index, next);

        world.AppendEvent("conversation_interrupted", $"{conversationId}:{ToWire(interruption)}");
    }

    public void SuspendAllConversations(IConversationWorld world, AgentConversationInterruption interruption)
    {
        foreach (var item in conversations.ToArray()) SuspendConversation(world, item.Id, interruption);
    }

    public static string ToWire(AgentConversationInterruption interruption) => interruption switch
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

    public bool CanProposeMarriage(IConversationWorld world, AgentConversation conversation) => conversation.Kind == AgentConversationKind.Ordinary &&
        AgentMarriageRules.CanPropose(world.Society, marriages, conversation.InitiatorId, conversation.InviteeId);

    public bool IsAcceptedSurnameSession(IConversationWorld world, AgentConversation conversation) =>
        conversation.Kind == AgentConversationKind.MarriageSurname &&
        marriages.SingleOrDefault(item => item.SurnameConversationId == conversation.Id) is { CompletedTick: null, EndReceipt: null } marriage &&
        marriage.InitiatorId == conversation.InitiatorId && marriage.InviteeId == conversation.InviteeId &&
        AgentMarriageRules.IsAdult(world.Society.GetInhabitant(marriage.InitiatorId)) &&
        AgentMarriageRules.IsAdult(world.Society.GetInhabitant(marriage.InviteeId)) &&
        AgentMarriageRules.Partnership(world.Society, marriage.InitiatorId, marriage.InviteeId)?.Id == marriage.PartnershipId;

    public void MaintainMarriages(IConversationWorld world)
    {
        for (var index = 0; index < marriages.Count; index++)
        {
            var marriage = marriages[index];
            if (marriage.EndReceipt is not null) continue;
            var partnership = world.Society.GetRelationship(marriage.PartnershipId);
            if (partnership.State is not (SocietyRelationshipState.Revoked or SocietyRelationshipState.EndedByDeath)) continue;
            marriage = marriage with { EndReceipt = partnership };
            if (marriage.CompletedTick is null)
            {
                var sessionIndex = ConversationIndex(item => item.Id == marriage.SurnameConversationId);
                if (sessionIndex >= 0)
                {
                    var closed = AgentConversationRules.CloseUnavailable(conversations[sessionIndex], world.WorldTick);
                    SetConversationAt(sessionIndex, closed);
                    marriage = marriage with { SurnameReceipt = closed };
                }
            }
            SetMarriageAt(index, marriage);

            var first = world.Society.GetInhabitant(marriage.InitiatorId).Name;
            var second = world.Society.GetInhabitant(marriage.InviteeId).Name;
            world.AppendEvent("marriage_ended", partnership.State == SocietyRelationshipState.EndedByDeath
                ? $"The marriage between {first} and {second} ended after a partner died."
                : $"The marriage between {first} and {second} ended by separation.");
        }
    }

    public void StartAcceptedMarriage(IConversationWorld world, AgentConversation consent)
    {
        var partnership = AgentMarriageRules.Partnership(world.Society, consent.InitiatorId, consent.InviteeId)!;
        var id = AgentMarriageRules.Id(consent.Id);
        var marriage = new AgentMarriage(id, partnership, consent,
            world.Society.GetInhabitant(consent.InitiatorId).Name,
            world.Society.GetInhabitant(consent.InviteeId).Name,
            id + ":surname", world.WorldTick);
        // Consent is retained in the marriage receipt even when ordinary dialogue is compacted.
        AddMarriage(marriage);
        TrimConversationHistory(world);
        AddConversation(AgentConversationRules.Propose(marriage.SurnameConversationId, marriage.InitiatorId,
            marriage.InviteeId, world.WorldTick, world.Society.RunEpoch) with
        {
            Kind = AgentConversationKind.MarriageSurname,
            Status = AgentConversationStatus.Ready,
            AcceptedParticipantIds = [marriage.InitiatorId, marriage.InviteeId],
            CurrentSpeakerId = marriage.InitiatorId,
        });
        world.AppendEvent("marriage_accepted", $"{marriage.InitiatorNameAtAcceptance} and {marriage.InviteeNameAtAcceptance} agreed to marry; their shared surname is still undecided.",
            world.Inhabitants[marriage.InitiatorId].Position);
    }

    public void CompleteMarriageSurname(IConversationWorld world, AgentConversation receipt)
    {
        var index = MarriageIndex(item => item.SurnameConversationId == receipt.Id);
        var marriage = marriages[index];
        var surname = AgentMarriageRules.ResolveSurname(world.WorldSeed, marriage, receipt, out var usedTieBreak);
        RenameSpouses(world, marriage, marriage.InitiatorId,
            AgentMarriageRules.WithSurname(world.Society.GetInhabitant(marriage.InitiatorId).Name, surname));
        SetMarriageAt(index, marriage with
        {
            ChosenSurname = surname,
            CompletedTick = world.WorldTick,
            UsedTieBreak = usedTieBreak,
            SurnameReceipt = receipt,
        });
        var couple = world.Society.GetInhabitant(marriage.InitiatorId).Name + " and " + world.Society.GetInhabitant(marriage.InviteeId).Name;
        world.AppendEvent(usedTieBreak ? "marriage_surname_draw" : "marriage_surname_agreed",
            usedTieBreak ? $"{couple} completed their marriage. A draw chose {surname} after four turns without agreement."
                : $"{couple} completed their marriage and agreed to share the surname {surname}.", world.Inhabitants[marriage.InitiatorId].Position);
    }

    public bool CanCompleteMarriageSurname(IConversationWorld world, AgentConversation receipt)
    {
        var marriage = marriages.Single(item => item.SurnameConversationId == receipt.Id);
        var surname = AgentMarriageRules.ResolveSurname(world.WorldSeed, marriage, receipt, out _);
        return new[] { marriage.InitiatorId, marriage.InviteeId }.All(agentId =>
            AgentMarriageRules.WithSurname(world.Society.GetInhabitant(agentId).Name, surname).Length <= 48);
    }

    public void RetainUnavailableSurnameSession(IConversationWorld world, AgentConversation session)
    {
        if (session.Kind != AgentConversationKind.MarriageSurname || session.Outcome != "participant_unavailable") return;
        var index = MarriageIndex(item => item.SurnameConversationId == session.Id);
        if (index >= 0 && marriages[index].CompletedTick is null)
            SetMarriageAt(index, marriages[index] with { SurnameReceipt = session });
    }

    public static SocietyOperationResult RenameSpouses(IConversationWorld world, AgentMarriage marriage, string agentId, string name, bool fromPlayer = false)
    {
        var otherId = agentId == marriage.InitiatorId ? marriage.InviteeId : marriage.InitiatorId;
        var surname = InhabitantNameRules.SurnameKey(name) ?? throw new ArgumentException("Choose a full name with a surname.", nameof(name));
        var otherName = AgentMarriageRules.WithSurname(world.Society.GetInhabitant(otherId).Name, surname);
        // Society.Apply publishes only the final valid checkpoint. Either refusal leaves both people untouched.
        return world.Apply(checkpoint =>
        {
            var first = fromPlayer ? world.RenameFromPlayer(checkpoint, agentId, name)
                : SocietyFixture.RenameInhabitant(checkpoint, agentId, name);
            var second = fromPlayer ? world.RenameFromPlayer(first.Checkpoint, otherId, otherName)
                : SocietyFixture.RenameInhabitant(first.Checkpoint, otherId, otherName);
            return new SocietyOperationResult(second.Checkpoint, NewEvents: [.. first.NewEvents ?? [], .. second.NewEvents ?? []]);
        });
    }
}

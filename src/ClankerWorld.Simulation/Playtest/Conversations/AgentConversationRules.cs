using ClankerWorld.Simulation.Cognition;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>
/// Pure transitions for two-agent dialogue. The runtime owns presence,
/// budgets, model routing and world effects; this class owns only consent and
/// bounded public-history admission.
/// </summary>
public static class AgentConversationRules
{
    public const int MaximumPublicTurns = 6;
    public const int MaximumTurnsPerParticipant = 3;
    public const int MaximumWrapUpTurns = 1;
    public const int MaximumUtteranceCharacters = AgentConversationText.MaximumUtteranceCharacters;
    public const int MaximumSavedConversations = 64;
    public const int MaximumListenersPerTurn = 64;
    public const int MaximumConversationsPerWorldDay = 2;
    public const long ProposalLifetimeTicks = 30;

    public static AgentConversation Propose(
        string id,
        string initiatorId,
        string inviteeId,
        long worldTick,
        long runEpoch)
    {
        ValidateIdentifier(id, nameof(id), 512);
        ValidateIdentifier(initiatorId, nameof(initiatorId));
        ValidateIdentifier(inviteeId, nameof(inviteeId));
        if (initiatorId == inviteeId)
            throw new ArgumentException("An agent cannot invite itself to a conversation.", nameof(inviteeId));
        ArgumentOutOfRangeException.ThrowIfNegative(worldTick);
        ArgumentOutOfRangeException.ThrowIfNegative(runEpoch);
        return new AgentConversation(
            id,
            initiatorId,
            inviteeId,
            worldTick,
            checked(worldTick + ProposalLifetimeTicks),
            runEpoch,
            Revision: 1,
            AgentConversationStatus.Proposed,
            [initiatorId],
            CurrentSpeakerId: null,
            AwaitingWrapUp: false,
            [],
            AgentConversationEffect.None,
            [],
            [],
            AgentConversationInterruption.None,
            Outcome: null,
            LastUpdatedTick: worldTick);
    }

    public static bool TryAcceptProposal(
        AgentConversation conversation,
        string participantId,
        long worldTick,
        long runEpoch,
        out AgentConversation next)
    {
        next = conversation;
        if (conversation.Status != AgentConversationStatus.Proposed ||
            conversation.InviteeId != participantId ||
            worldTick < conversation.LastUpdatedTick ||
            worldTick > conversation.ProposalDeadlineTick ||
            runEpoch != conversation.RunEpoch)
            return false;

        next = conversation with
        {
            Status = AgentConversationStatus.Ready,
            AcceptedParticipantIds = Participants(conversation),
            CurrentSpeakerId = conversation.InitiatorId,
            Revision = checked(conversation.Revision + 1),
            LastUpdatedTick = worldTick,
            Interruption = AgentConversationInterruption.None,
        };
        return true;
    }

    public static bool TryDeclineProposal(
        AgentConversation conversation,
        string participantId,
        long worldTick,
        out AgentConversation next)
    {
        next = conversation;
        if (conversation.Status != AgentConversationStatus.Proposed ||
            worldTick < conversation.LastUpdatedTick ||
            conversation.InviteeId != participantId)
            return false;
        next = Close(conversation, "refused", worldTick);
        return true;
    }

    public static bool TryExpireProposal(
        AgentConversation conversation,
        long worldTick,
        out AgentConversation next)
    {
        next = conversation;
        if (conversation.Status != AgentConversationStatus.Proposed ||
            worldTick < conversation.LastUpdatedTick ||
            worldTick <= conversation.ProposalDeadlineTick)
            return false;
        next = Close(conversation, "deadline", worldTick);
        return true;
    }

    public static AgentConversation CloseProposalForDailyLimit(AgentConversation conversation, long worldTick)
    {
        if (conversation.Status != AgentConversationStatus.Proposed)
            throw new InvalidOperationException("Only an open proposal can close for a daily limit.");
        return Close(conversation, "daily_limit", worldTick);
    }

    public static bool TryBeginTurn(
        AgentConversation conversation,
        long worldTick,
        long runEpoch,
        out AgentConversation next)
    {
        next = conversation;
        if (runEpoch != conversation.RunEpoch ||
            worldTick < conversation.LastUpdatedTick ||
            conversation.Status is not (AgentConversationStatus.Ready or AgentConversationStatus.WrapUp))
            return false;

        var isWrapUp = conversation.Status == AgentConversationStatus.WrapUp;
        if (isWrapUp && conversation.Kind != AgentConversationKind.Ordinary) return false;
        if (isWrapUp)
        {
            if (conversation.Turns.Any(turn => turn.IsWrapUp) || conversation.WrapUpAcceptedBy.Count != 0)
                return false;
        }
        else if (PublicTurnCount(conversation) >= TurnLimit(conversation) || conversation.CurrentSpeakerId is null)
        {
            return false;
        }

        var speaker = isWrapUp ? conversation.InitiatorId : conversation.CurrentSpeakerId!;
        next = conversation with
        {
            Status = AgentConversationStatus.AwaitingSpeaker,
            CurrentSpeakerId = speaker,
            AwaitingWrapUp = isWrapUp,
            Revision = checked(conversation.Revision + 1),
            LastUpdatedTick = worldTick,
            Interruption = AgentConversationInterruption.None,
        };
        return true;
    }

    public static bool TryAdmitTurn(
        AgentConversation conversation,
        AgentConversationTurnRequest request,
        AgentConversationTurnResponse response,
        IReadOnlyList<string> listenerIds,
        long worldTick,
        long runEpoch,
        out AgentConversation next,
        out AgentConversationTurn? appended)
    {
        next = conversation;
        appended = null;
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(listenerIds);
        if (conversation.Status != AgentConversationStatus.AwaitingSpeaker ||
            conversation.Revision != request.Revision ||
            conversation.RunEpoch != runEpoch ||
            request.RunEpoch != runEpoch ||
            request.ConversationId != conversation.Id ||
            request.SpeakerId != conversation.CurrentSpeakerId ||
            request.WorldTick != conversation.LastUpdatedTick || request.WorldTick > worldTick ||
            request.Purpose != (conversation.Kind == AgentConversationKind.MarriageSurname
                ? AgentConversationPurpose.SurnameChoice
                : conversation.AwaitingWrapUp
                ? AgentConversationPurpose.WrapUp
                : AgentConversationPurpose.PublicTurn) ||
            response.RequestId != request.RequestId ||
            response.ConversationId != request.ConversationId ||
            response.Revision != request.Revision ||
            response.RunEpoch != request.RunEpoch ||
            response.SpeakerId != request.SpeakerId ||
            !request.AllowedEffects.Contains(response.Effect) ||
            !Enum.IsDefined(response.Disposition) ||
            !Enum.IsDefined(response.Effect) ||
            response.InputTokens is < 0 or > 1_000_000 || response.OutputTokens is < 0 or > 1_000_000 ||
            response.Effect != AgentConversationEffect.None && !conversation.AwaitingWrapUp ||
            response.Disposition == AgentConversationDisposition.Withdraw &&
                response.Effect != AgentConversationEffect.None ||
            !AgentConversationText.IsValidUtterance(response.Text) ||
            !IsValidListenerList(listenerIds, conversation, response.SpeakerId))
            return false;

        if (conversation.Kind == AgentConversationKind.MarriageSurname)
            return TryAdmitSurnameTurn(conversation, request, response, listenerIds, worldTick, out next, out appended);
        if (response.SurnameChoice is not null) return false;

        if (!conversation.AwaitingWrapUp &&
            (response.Effect != AgentConversationEffect.None || PublicTurnCount(conversation) >= MaximumPublicTurns))
            return false;
        if (conversation.AwaitingWrapUp && conversation.Turns.Any(turn => turn.IsWrapUp))
            return false;

        var turnId = conversation.AwaitingWrapUp
            ? $"{conversation.Id}:wrapup"
            : $"{conversation.Id}:turn:{PublicTurnCount(conversation) + 1}";
        var turn = new AgentConversationTurn(
            turnId,
            response.SpeakerId,
            response.Text,
            worldTick,
            listenerIds.Order(StringComparer.Ordinal).ToArray(),
            response.Disposition,
            conversation.AwaitingWrapUp);
        if (response.Disposition == AgentConversationDisposition.Withdraw)
        {
            next = Close(conversation with
            {
                Turns = [.. conversation.Turns, turn],
                CurrentSpeakerId = null,
                AwaitingWrapUp = false,
            }, "withdrawn", worldTick);
            appended = turn;
            return true;
        }

        if (conversation.AwaitingWrapUp)
        {
            next = conversation with
            {
                Status = AgentConversationStatus.WrapUp,
                Turns = [.. conversation.Turns, turn],
                CurrentSpeakerId = null,
                AwaitingWrapUp = false,
                WrapUpEffect = response.Effect,
                WrapUpAcceptedBy = [],
                Revision = checked(conversation.Revision + 1),
                LastUpdatedTick = worldTick,
                Interruption = AgentConversationInterruption.None,
            };
            appended = turn;
            return true;
        }

        var nextPublicCount = PublicTurnCount(conversation) + 1;
        next = conversation with
        {
            Status = nextPublicCount >= MaximumPublicTurns
                ? AgentConversationStatus.WrapUp
                : AgentConversationStatus.Ready,
            Turns = [.. conversation.Turns, turn],
            CurrentSpeakerId = nextPublicCount >= MaximumPublicTurns
                ? conversation.InitiatorId
                : OtherParticipant(conversation, response.SpeakerId),
            AwaitingWrapUp = false,
            Revision = checked(conversation.Revision + 1),
            LastUpdatedTick = worldTick,
            Interruption = AgentConversationInterruption.None,
        };
        appended = turn;
        return true;
    }

    public static bool TryAcceptWrapUp(
        AgentConversation conversation,
        string participantId,
        long worldTick,
        out AgentConversation next,
        out bool newlyAgreed)
    {
        next = conversation;
        newlyAgreed = false;
        if (conversation.Status != AgentConversationStatus.WrapUp ||
            worldTick < conversation.LastUpdatedTick ||
            !conversation.Turns.Any(turn => turn.IsWrapUp) ||
            !IsParticipant(conversation, participantId) ||
            conversation.WrapUpAcceptedBy.Contains(participantId, StringComparer.Ordinal))
            return false;

        var accepted = conversation.WrapUpAcceptedBy.Append(participantId)
            .Order(StringComparer.Ordinal).ToArray();
        newlyAgreed = accepted.Length == 2;
        next = conversation with
        {
            Status = newlyAgreed ? AgentConversationStatus.Closed : AgentConversationStatus.WrapUp,
            WrapUpAcceptedBy = accepted,
            CurrentSpeakerId = null,
            Outcome = newlyAgreed ? "agreed" : null,
            Revision = checked(conversation.Revision + 1),
            LastUpdatedTick = worldTick,
            Interruption = AgentConversationInterruption.None,
        };
        return true;
    }

    public static bool TryDeclineWrapUp(
        AgentConversation conversation,
        string participantId,
        long worldTick,
        out AgentConversation next)
    {
        next = conversation;
        if (conversation.Status != AgentConversationStatus.WrapUp ||
            worldTick < conversation.LastUpdatedTick ||
            !conversation.Turns.Any(turn => turn.IsWrapUp) ||
            !IsParticipant(conversation, participantId))
            return false;
        next = Close(conversation, "disagreed", worldTick);
        return true;
    }

    public static bool TryEndSuspended(
        AgentConversation conversation,
        string participantId,
        long worldTick,
        out AgentConversation next)
    {
        next = conversation;
        if (conversation.Status != AgentConversationStatus.Suspended ||
            conversation.Kind == AgentConversationKind.MarriageSurname ||
            worldTick < conversation.LastUpdatedTick ||
            !IsParticipant(conversation, participantId))
            return false;
        next = Close(conversation, "withdrawn", worldTick);
        return true;
    }

    public static bool TryResume(
        AgentConversation conversation,
        string participantId,
        long worldTick,
        long runEpoch,
        out AgentConversation next,
        out bool resumed)
    {
        next = conversation;
        resumed = false;
        if (conversation.Status != AgentConversationStatus.Suspended ||
            conversation.AcceptedParticipantIds.Count != 2 ||
            !conversation.AcceptedParticipantIds.Contains(conversation.InitiatorId, StringComparer.Ordinal) ||
            !conversation.AcceptedParticipantIds.Contains(conversation.InviteeId, StringComparer.Ordinal) ||
            worldTick < conversation.LastUpdatedTick ||
            !IsParticipant(conversation, participantId) ||
            conversation.ResumeAcceptedBy.Contains(participantId, StringComparer.Ordinal))
            return false;

        var accepted = conversation.ResumeAcceptedBy.Append(participantId)
            .Order(StringComparer.Ordinal).ToArray();
        resumed = accepted.Length == 2;
        next = conversation with
        {
            Status = resumed
                ? conversation.Turns.Count(turn => !turn.IsWrapUp) >= MaximumPublicTurns || conversation.Turns.Any(turn => turn.IsWrapUp)
                    ? AgentConversationStatus.WrapUp
                    : AgentConversationStatus.Ready
                : AgentConversationStatus.Suspended,
            ResumeAcceptedBy = accepted,
            CurrentSpeakerId = resumed
                ? conversation.Turns.Count(turn => !turn.IsWrapUp) >= MaximumPublicTurns || conversation.Turns.Any(turn => turn.IsWrapUp)
                    ? conversation.Turns.Any(turn => turn.IsWrapUp) ? null : conversation.InitiatorId
                    : NextSpeaker(conversation)
                : conversation.CurrentSpeakerId,
            AwaitingWrapUp = false,
            RunEpoch = resumed ? runEpoch : conversation.RunEpoch,
            Interruption = resumed ? AgentConversationInterruption.None : conversation.Interruption,
            Revision = checked(conversation.Revision + 1),
            LastUpdatedTick = worldTick,
        };
        return true;
    }

    public static AgentConversation Suspend(
        AgentConversation conversation,
        AgentConversationInterruption interruption,
        long worldTick)
    {
        if (conversation.Status is AgentConversationStatus.Closed or AgentConversationStatus.Suspended)
            return conversation;
        ArgumentOutOfRangeException.ThrowIfLessThan(worldTick, conversation.LastUpdatedTick);
        if (interruption == AgentConversationInterruption.None || !Enum.IsDefined(interruption))
            throw new ArgumentOutOfRangeException(nameof(interruption));
        if (conversation.Status == AgentConversationStatus.Proposed)
        {
            // An invitation has no shared session to resume. A pause keeps the
            // original proposal and deadline; unsafe separation withdraws it.
            return interruption is AgentConversationInterruption.OwnerPaused or AgentConversationInterruption.Restored
                ? conversation
                : Close(conversation, "withdrawn", worldTick);
        }
        return conversation with
        {
            Status = AgentConversationStatus.Suspended,
            CurrentSpeakerId = null,
            AwaitingWrapUp = false,
            ResumeAcceptedBy = [],
            WrapUpAcceptedBy = [],
            Interruption = interruption,
            Revision = checked(conversation.Revision + 1),
            LastUpdatedTick = worldTick,
        };
    }

    public static AgentConversation CloseUnavailable(AgentConversation conversation, long worldTick)
    {
        if (conversation.Status == AgentConversationStatus.Closed) return conversation;
        ArgumentOutOfRangeException.ThrowIfLessThan(worldTick, conversation.LastUpdatedTick);
        return Close(conversation, "participant_unavailable", worldTick);
    }

    public static int PublicTurnCount(AgentConversation conversation) =>
        conversation.Turns.Count(turn => !turn.IsWrapUp);

    public static bool IsParticipant(AgentConversation conversation, string agentId) =>
        conversation.InitiatorId == agentId || conversation.InviteeId == agentId;

    public static bool CanStartToday(
        IReadOnlyList<AgentConversationDailyBudget> budgets,
        string agentId,
        long worldDay) =>
        budgets.FirstOrDefault(item => item.AgentId == agentId && item.WorldDay == worldDay)?.Count
            is not >= MaximumConversationsPerWorldDay;

    public static AgentConversationDailyBudget ReserveToday(
        IReadOnlyList<AgentConversationDailyBudget> budgets,
        string agentId,
        long worldDay)
    {
        if (!CanStartToday(budgets, agentId, worldDay))
            throw new InvalidOperationException("The agent has used today's conversation allowance.");
        var current = budgets.FirstOrDefault(item => item.AgentId == agentId && item.WorldDay == worldDay);
        return new AgentConversationDailyBudget(agentId, worldDay, checked((current?.Count ?? 0) + 1));
    }

    public static bool IsValidUtterance(string? text) => AgentConversationText.IsValidUtterance(text);

    public static void Validate(AgentConversation conversation, long worldTick)
    {
        if (conversation is null)
            throw new InvalidDataException("A saved conversation entry is missing.");
        if (worldTick < 0 || conversation.AcceptedParticipantIds is null || conversation.Turns is null ||
            conversation.WrapUpAcceptedBy is null || conversation.ResumeAcceptedBy is null)
            throw new InvalidDataException("The saved conversation state is incomplete.");
        if (conversation.Turns.Any(turn => turn is null))
            throw new InvalidDataException("A saved conversation turn is missing.");
        ValidateIdentifier(conversation.Id, nameof(conversation.Id), 512);
        ValidateIdentifier(conversation.InitiatorId, nameof(conversation.InitiatorId));
        ValidateIdentifier(conversation.InviteeId, nameof(conversation.InviteeId));
        if (conversation.InitiatorId == conversation.InviteeId || conversation.CreatedTick < 0 ||
            conversation.ProposalDeadlineTick < conversation.CreatedTick ||
            conversation.ProposalDeadlineTick - conversation.CreatedTick != ProposalLifetimeTicks ||
            conversation.RunEpoch < 0 || conversation.Revision < 1 || conversation.LastUpdatedTick < conversation.CreatedTick ||
            conversation.LastUpdatedTick > worldTick || !Enum.IsDefined(conversation.Status) ||
            !Enum.IsDefined(conversation.Interruption) || !Enum.IsDefined(conversation.WrapUpEffect) ||
            !Enum.IsDefined(conversation.Kind) ||
            conversation.Turns.Count > MaximumPublicTurns + MaximumWrapUpTurns ||
            conversation.AcceptedParticipantIds.Distinct(StringComparer.Ordinal).Count() != conversation.AcceptedParticipantIds.Count ||
            conversation.ResumeAcceptedBy.Distinct(StringComparer.Ordinal).Count() != conversation.ResumeAcceptedBy.Count ||
            conversation.WrapUpAcceptedBy.Distinct(StringComparer.Ordinal).Count() != conversation.WrapUpAcceptedBy.Count ||
            conversation.AcceptedParticipantIds.Any(id => !IsParticipant(conversation, id)) ||
            conversation.ResumeAcceptedBy.Any(id => !IsParticipant(conversation, id)) ||
            conversation.WrapUpAcceptedBy.Any(id => !IsParticipant(conversation, id)))
            throw new InvalidDataException("The saved conversation state is invalid.");

        var publicTurns = conversation.Turns.Where(turn => !turn.IsWrapUp).ToArray();
        var wrapUps = conversation.Turns.Where(turn => turn.IsWrapUp).ToArray();
        if (publicTurns.Length > MaximumPublicTurns || wrapUps.Length > MaximumWrapUpTurns ||
            publicTurns.Count(turn => turn.SpeakerId == conversation.InitiatorId) > MaximumTurnsPerParticipant ||
            publicTurns.Count(turn => turn.SpeakerId == conversation.InviteeId) > MaximumTurnsPerParticipant ||
            conversation.Turns.Select(turn => turn.Id).Distinct(StringComparer.Ordinal).Count() != conversation.Turns.Count ||
            publicTurns.Select((turn, index) => (turn, index)).Any(item =>
                item.turn.SpeakerId != (item.index % 2 == 0 ? conversation.InitiatorId : conversation.InviteeId)))
            throw new InvalidDataException("The conversation history exceeds its agreed turn limit.");

        var expectedPublicTurn = 0;
        var previousTurnTick = conversation.CreatedTick;
        foreach (var turn in conversation.Turns)
        {
            if (turn is null || turn.ListenerIds is null)
                throw new InvalidDataException("A saved conversation turn is incomplete.");
            ValidateIdentifier(turn.Id, nameof(turn.Id), 600);
            ValidateIdentifier(turn.SpeakerId, nameof(turn.SpeakerId));
            if (!IsParticipant(conversation, turn.SpeakerId) || turn.WorldTick < conversation.CreatedTick ||
                turn.WorldTick < previousTurnTick || turn.WorldTick > worldTick || !IsValidUtterance(turn.Text) ||
                !Enum.IsDefined(turn.Disposition) || turn.ListenerIds.Count > MaximumListenersPerTurn ||
                turn.ListenerIds.Distinct(StringComparer.Ordinal).Count() != turn.ListenerIds.Count ||
                turn.ListenerIds.Contains(turn.SpeakerId, StringComparer.Ordinal) ||
                !turn.ListenerIds.Contains(OtherParticipant(conversation, turn.SpeakerId), StringComparer.Ordinal))
                throw new InvalidDataException("A saved conversation turn is invalid.");
            previousTurnTick = turn.WorldTick;
            if (turn.IsWrapUp)
            {
                if (turn.Id != $"{conversation.Id}:wrapup" || turn.SpeakerId != conversation.InitiatorId ||
                    turn != conversation.Turns[^1])
                    throw new InvalidDataException("The saved wrap-up turn is not canonical.");
            }
            else
            {
                expectedPublicTurn++;
                var expectedSpeaker = expectedPublicTurn % 2 == 1 ? conversation.InitiatorId : conversation.InviteeId;
                if (turn.Id != $"{conversation.Id}:turn:{expectedPublicTurn}" || turn.SpeakerId != expectedSpeaker)
                    throw new InvalidDataException("The saved public turn sequence is not canonical.");
            }
        }

        if (conversation.Kind == AgentConversationKind.MarriageSurname)
        {
            ValidateSurnameConversation(conversation, publicTurns, wrapUps);
            return;
        }
        if (conversation.Turns.Any(turn => turn.SurnameChoice is not null))
            throw new InvalidDataException("An ordinary conversation cannot contain surname choices.");

        if (conversation.Status == AgentConversationStatus.Proposed &&
            (publicTurns.Length != 0 || wrapUps.Length != 0 || conversation.AcceptedParticipantIds.Count != 1 ||
             conversation.AcceptedParticipantIds[0] != conversation.InitiatorId ||
             conversation.CurrentSpeakerId is not null || conversation.AwaitingWrapUp))
            throw new InvalidDataException("A conversation proposal contains invalid consent or history.");
        if (conversation.Status is AgentConversationStatus.Ready or AgentConversationStatus.AwaitingSpeaker or AgentConversationStatus.WrapUp)
        {
            if (conversation.AcceptedParticipantIds.Count != 2 ||
                conversation.AcceptedParticipantIds.Order(StringComparer.Ordinal)
                    .SequenceEqual(Participants(conversation).Order(StringComparer.Ordinal)) is false)
                throw new InvalidDataException("An active conversation requires both participants to accept.");
        }
        if (conversation.Status == AgentConversationStatus.Ready &&
            (conversation.AwaitingWrapUp || publicTurns.Length >= MaximumPublicTurns || wrapUps.Length != 0 ||
             conversation.CurrentSpeakerId != NextSpeaker(conversation) || conversation.WrapUpAcceptedBy.Count != 0 ||
             conversation.WrapUpEffect != AgentConversationEffect.None))
            throw new InvalidDataException("A ready conversation has an invalid next speaker or wrap-up state.");
        if (conversation.Status == AgentConversationStatus.AwaitingSpeaker &&
            (conversation.CurrentSpeakerId is null || !IsParticipant(conversation, conversation.CurrentSpeakerId) ||
             conversation.AwaitingWrapUp && (publicTurns.Length != MaximumPublicTurns || wrapUps.Length != 0) ||
             !conversation.AwaitingWrapUp && (publicTurns.Length >= MaximumPublicTurns ||
                 wrapUps.Length != 0 || conversation.CurrentSpeakerId != NextSpeaker(conversation)) ||
             conversation.AwaitingWrapUp && conversation.CurrentSpeakerId != conversation.InitiatorId ||
             conversation.WrapUpAcceptedBy.Count != 0))
            throw new InvalidDataException("The saved in-flight conversation turn is invalid.");
        if (conversation.Status == AgentConversationStatus.WrapUp &&
            (publicTurns.Length != MaximumPublicTurns || wrapUps.Length > 1 || conversation.AwaitingWrapUp ||
             wrapUps.Length == 0 && conversation.WrapUpAcceptedBy.Count > 0 ||
             wrapUps.Length == 0 && conversation.CurrentSpeakerId != conversation.InitiatorId ||
             wrapUps.Length == 1 && conversation.CurrentSpeakerId is not null ||
             wrapUps.Length == 1 && conversation.WrapUpAcceptedBy.Count >= 2))
            throw new InvalidDataException("The saved conversation wrap-up is invalid.");
        if (conversation.Status == AgentConversationStatus.Suspended &&
            (conversation.AcceptedParticipantIds.Count != 2 ||
             conversation.CurrentSpeakerId is not null || conversation.AwaitingWrapUp ||
             conversation.ResumeAcceptedBy.Count > 1 || conversation.WrapUpAcceptedBy.Count != 0))
            throw new InvalidDataException("A suspended conversation requires original mutual consent and cannot retain an in-flight turn or final consent.");
        if (conversation.Status == AgentConversationStatus.Closed && conversation.Outcome is null)
            throw new InvalidDataException("A closed conversation requires an outcome.");
        if (conversation.Status == AgentConversationStatus.Closed &&
            (conversation.CurrentSpeakerId is not null || conversation.AwaitingWrapUp))
            throw new InvalidDataException("A closed conversation cannot retain an in-flight turn.");
        if (conversation.Status != AgentConversationStatus.Closed && conversation.Outcome is not null)
            throw new InvalidDataException("An open conversation cannot have a final outcome.");
        if (conversation.Outcome is not null && conversation.Outcome is not
            ("agreed" or "refused" or "deadline" or "daily_limit" or "withdrawn" or "disagreed" or "participant_unavailable"))
            throw new InvalidDataException("The conversation outcome is unknown.");
        if (conversation.Status == AgentConversationStatus.Suspended &&
            conversation.Interruption == AgentConversationInterruption.None)
            throw new InvalidDataException("A suspended conversation must say why it stopped.");
        if (conversation.Status != AgentConversationStatus.Suspended &&
            conversation.Interruption != AgentConversationInterruption.None)
            throw new InvalidDataException("Only a suspended conversation can retain an interruption reason.");
        if (conversation.Outcome is "refused" or "deadline" or "daily_limit" &&
            (conversation.Status != AgentConversationStatus.Closed || publicTurns.Length != 0 || wrapUps.Length != 0 ||
             conversation.AcceptedParticipantIds.Count != 1 ||
             conversation.AcceptedParticipantIds[0] != conversation.InitiatorId))
            throw new InvalidDataException("A proposal outcome contains active turns or invalid acceptance.");
        if (conversation.Outcome == "agreed" &&
            (conversation.Status != AgentConversationStatus.Closed || wrapUps.Length != 1 ||
             conversation.WrapUpAcceptedBy.Count != 2))
            throw new InvalidDataException("A conversation agreement requires a shared wrap-up and consent.");
        if (conversation.Outcome == "disagreed" &&
            (conversation.Status != AgentConversationStatus.Closed || wrapUps.Length != 1 ||
             conversation.WrapUpAcceptedBy.Count > 1))
            throw new InvalidDataException("A conversation disagreement requires a wrap-up and cannot be unanimous.");
        if (conversation.Turns.Any(turn => turn.Disposition == AgentConversationDisposition.Withdraw) &&
            (conversation.Status != AgentConversationStatus.Closed || conversation.Outcome != "withdrawn" ||
             conversation.Turns[^1].Disposition != AgentConversationDisposition.Withdraw))
            throw new InvalidDataException("A withdrawn conversation must end at its withdrawal turn.");
    }

    private static int TurnLimit(AgentConversation conversation) =>
        conversation.Kind == AgentConversationKind.MarriageSurname ? AgentMarriageRules.MaximumSurnameTurns : MaximumPublicTurns;

    private static bool TryAdmitSurnameTurn(AgentConversation conversation, AgentConversationTurnRequest request,
        AgentConversationTurnResponse response, IReadOnlyList<string> listenerIds, long worldTick,
        out AgentConversation next, out AgentConversationTurn? appended)
    {
        next = conversation;
        appended = null;
        if (conversation.AwaitingWrapUp || conversation.Turns.Count >= AgentMarriageRules.MaximumSurnameTurns ||
            response.Disposition != AgentConversationDisposition.Continue || response.Effect != AgentConversationEffect.None ||
            response.SurnameChoice is null || !request.AllowedSurnames.Contains(response.SurnameChoice, StringComparer.Ordinal) ||
            listenerIds.Count != 1)
            return false;
        var turn = new AgentConversationTurn($"{conversation.Id}:turn:{conversation.Turns.Count + 1}",
            response.SpeakerId, response.Text, worldTick, listenerIds.ToArray(), response.Disposition,
            SurnameChoice: response.SurnameChoice);
        var turns = conversation.Turns.Append(turn).ToArray();
        var agreed = turns.Length >= 2 && turns[^1].SurnameChoice == turns[^2].SurnameChoice;
        var finished = agreed || turns.Length == AgentMarriageRules.MaximumSurnameTurns;
        next = conversation with
        {
            Turns = turns,
            Status = finished ? AgentConversationStatus.Closed : AgentConversationStatus.Ready,
            CurrentSpeakerId = finished ? null : OtherParticipant(conversation, response.SpeakerId),
            Outcome = finished ? agreed ? "surname_agreed" : "surname_draw" : null,
            Revision = checked(conversation.Revision + 1),
            LastUpdatedTick = worldTick,
            Interruption = AgentConversationInterruption.None,
        };
        appended = turn;
        return true;
    }

    private static void ValidateSurnameConversation(AgentConversation conversation, AgentConversationTurn[] publicTurns,
        AgentConversationTurn[] wrapUps)
    {
        var count = publicTurns.Length;
        var agreement = count >= 2 && publicTurns[^1].SurnameChoice == publicTurns[^2].SurnameChoice;
        if (wrapUps.Length != 0 || count > AgentMarriageRules.MaximumSurnameTurns || conversation.AwaitingWrapUp ||
            conversation.AcceptedParticipantIds.Count != 2 || conversation.WrapUpAcceptedBy.Count != 0 ||
            conversation.WrapUpEffect != AgentConversationEffect.None ||
            publicTurns.Any(turn => turn.Disposition != AgentConversationDisposition.Continue || turn.ListenerIds.Count != 1 ||
                string.IsNullOrWhiteSpace(turn.SurnameChoice) || turn.SurnameChoice.Length > 128 ||
                turn.SurnameChoice.Any(char.IsWhiteSpace) || turn.SurnameChoice.Any(char.IsControl)) ||
            publicTurns.Take(Math.Max(0, count - 1)).Select((turn, index) => (turn, index)).Any(item =>
                item.index > 0 && item.turn.SurnameChoice == publicTurns[item.index - 1].SurnameChoice) ||
            conversation.Status is AgentConversationStatus.Proposed or AgentConversationStatus.WrapUp ||
            conversation.Status != AgentConversationStatus.Closed && (agreement || count == AgentMarriageRules.MaximumSurnameTurns) ||
            conversation.Status is AgentConversationStatus.Ready or AgentConversationStatus.AwaitingSpeaker &&
                (conversation.CurrentSpeakerId != NextSpeaker(conversation) || conversation.Interruption != AgentConversationInterruption.None) ||
            conversation.Status == AgentConversationStatus.Suspended &&
                (conversation.CurrentSpeakerId is not null || conversation.Interruption == AgentConversationInterruption.None ||
                 conversation.ResumeAcceptedBy.Count > 1) ||
            conversation.Status != AgentConversationStatus.Suspended && conversation.Interruption != AgentConversationInterruption.None ||
            conversation.Status != AgentConversationStatus.Closed && conversation.Outcome is not null ||
            conversation.Status == AgentConversationStatus.Closed &&
                (conversation.CurrentSpeakerId is not null || conversation.Outcome is not ("surname_agreed" or "surname_draw" or "participant_unavailable")) ||
            conversation.Outcome == "surname_agreed" && !agreement ||
            conversation.Outcome == "surname_draw" && (count != AgentMarriageRules.MaximumSurnameTurns || agreement) ||
            conversation.Outcome == "participant_unavailable" && (agreement || count == AgentMarriageRules.MaximumSurnameTurns))
            throw new InvalidDataException("The saved marriage surname conversation is invalid.");
    }

    private static AgentConversation Close(AgentConversation conversation, string outcome, long worldTick) =>
        conversation with
        {
            Status = AgentConversationStatus.Closed,
            CurrentSpeakerId = null,
            AwaitingWrapUp = false,
            Outcome = outcome,
            Revision = checked(conversation.Revision + 1),
            LastUpdatedTick = worldTick,
            Interruption = AgentConversationInterruption.None,
        };

    private static bool IsValidListenerList(
        IReadOnlyList<string> listenerIds,
        AgentConversation conversation,
        string speakerId) =>
        listenerIds.Count <= MaximumListenersPerTurn &&
        listenerIds.All(id => !string.IsNullOrWhiteSpace(id) && id == id.Trim() && !id.Any(char.IsControl)) &&
        listenerIds.Distinct(StringComparer.Ordinal).Count() == listenerIds.Count &&
        listenerIds.Contains(OtherParticipant(conversation, speakerId), StringComparer.Ordinal) &&
        !listenerIds.Contains(speakerId, StringComparer.Ordinal);

    private static string NextSpeaker(AgentConversation conversation)
    {
        var nextTurn = PublicTurnCount(conversation);
        return nextTurn % 2 == 0 ? conversation.InitiatorId : conversation.InviteeId;
    }

    private static string OtherParticipant(AgentConversation conversation, string agentId) =>
        agentId == conversation.InitiatorId ? conversation.InviteeId : conversation.InitiatorId;

    private static string[] Participants(AgentConversation conversation) =>
        [conversation.InitiatorId, conversation.InviteeId];

    private static void ValidateIdentifier(string value, string parameterName, int maximumLength = 128)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > maximumLength || value != value.Trim() || value.Any(char.IsControl))
            throw new ArgumentException("A conversation identifier is invalid.", parameterName);
    }
}

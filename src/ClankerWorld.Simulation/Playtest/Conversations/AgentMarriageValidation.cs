using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

internal static class AgentMarriageValidation
{
    public static void Validate(PrivateWorldRuntimeState state, SocietyCheckpoint society)
    {
        var marriages = state.Marriages;
        if (marriages is null || marriages.Any(item => item is null || item.Consent is null || item.PartnershipReceipt is null) ||
            marriages.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != marriages.Count)
            throw new InvalidDataException("The saved marriage list is missing or contains overlapping consent.");
        foreach (var history in marriages.SelectMany(marriage => new[]
                     { (AgentId: marriage.InitiatorId, Marriage: marriage), (AgentId: marriage.InviteeId, Marriage: marriage) })
                 .GroupBy(item => item.AgentId, StringComparer.Ordinal))
        {
            AgentMarriage? previous = null;
            foreach (var item in history.OrderBy(item => item.Marriage.AcceptedTick).ThenBy(item => item.Marriage.Id, StringComparer.Ordinal))
            {
                if (previous is not null && (previous.EndedTick is null || previous.EndedTick > item.Marriage.AcceptedTick))
                    throw new InvalidDataException("An agent's saved marriages overlap in time.");
                previous = item.Marriage;
            }
        }
        var conversations = state.Conversations ?? [];
        foreach (var marriage in marriages)
        {
            var consent = marriage.Consent;
            AgentConversationRules.Validate(consent, society.WorldTick);
            var partnership = marriage.PartnershipReceipt;
            var parties = new[] { consent.InitiatorId, consent.InviteeId }.Order(StringComparer.Ordinal).ToArray();
            var recordedPartnership = society.Relationships.SingleOrDefault(item => item.Id == partnership.Id);
            if (marriage.Id != AgentMarriageRules.Id(consent.Id) || marriage.SurnameConversationId != marriage.Id + ":surname" ||
                marriage.AcceptedTick != consent.LastUpdatedTick || consent.Kind != AgentConversationKind.Ordinary ||
                consent.Status != AgentConversationStatus.Closed || consent.Outcome != "agreed" || consent.WrapUpEffect != AgentConversationEffect.Marriage ||
                !society.Inhabitants.Any(item => item.Id == consent.InitiatorId) || !society.Inhabitants.Any(item => item.Id == consent.InviteeId) ||
                !ValidOriginalName(marriage.InitiatorNameAtAcceptance) || !ValidOriginalName(marriage.InviteeNameAtAcceptance) ||
                partnership.Type != SocietyRelationshipType.Partnership || partnership.State != SocietyRelationshipState.Accepted ||
                partnership.Consent != SocietyConsentState.Accepted || partnership.ProposedTick < 0 || partnership.Revision < 1 ||
                partnership.EffectiveTick < partnership.ProposedTick || partnership.EffectiveTick > marriage.AcceptedTick ||
                !new[] { partnership.ProposerId, partnership.TargetId }.Order(StringComparer.Ordinal).SequenceEqual(parties) ||
                !partnership.AcceptedParties.Order(StringComparer.Ordinal).SequenceEqual(parties) ||
                recordedPartnership is null || recordedPartnership.Type != partnership.Type || recordedPartnership.Revision < partnership.Revision ||
                recordedPartnership.ProposerId != partnership.ProposerId || recordedPartnership.TargetId != partnership.TargetId ||
                consent.Turns.Any(turn => turn.ListenerIds.Any(id => !society.Inhabitants.Any(person => person.Id == id))))
                throw new InvalidDataException("A saved marriage lacks valid partnership and personal marriage consent.");
            if (conversations.SingleOrDefault(item => item.Id == consent.Id) is { } savedConsent && !SameConversation(savedConsent, consent))
                throw new InvalidDataException("Marriage consent disagrees with its public conversation.");
            ValidateEnding(marriage, recordedPartnership!, society);

            var liveSession = conversations.SingleOrDefault(item => item.Id == marriage.SurnameConversationId);
            var session = marriage.SurnameReceipt ?? liveSession;
            if (session is null || session.Id != marriage.SurnameConversationId || session.Kind != AgentConversationKind.MarriageSurname ||
                session.InitiatorId != consent.InitiatorId || session.InviteeId != consent.InviteeId || session.CreatedTick != marriage.AcceptedTick)
                throw new InvalidDataException("A saved marriage lacks its own surname conversation.");
            AgentConversationRules.Validate(session, society.WorldTick);
            if (marriage.EndReceipt is not null && session.Turns.Any(turn => turn.WorldTick > marriage.EndedTick))
                throw new InvalidDataException("An ended marriage cannot retain surname turns after its ending.");
            if (session.Turns.Any(turn => !AgentMarriageRules.AllowedSurnames(marriage).Contains(turn.SurnameChoice!, StringComparer.Ordinal)))
                throw new InvalidDataException("The surname conversation chose a surname outside the couple's original names.");
            if (marriage.CompletedTick is null)
            {
                if (marriage.EndReceipt is not null &&
                    (marriage.SurnameReceipt is null || session.LastUpdatedTick > marriage.EndedTick))
                    throw new InvalidDataException("An ended marriage must retain its surname closure at or before the ending.");
                if (marriage.SurnameReceipt is null && parties.Any(id =>
                        !AgentMarriageRules.CanKeepSurnameChoices(marriage, society.GetInhabitant(id).Name)))
                    throw new InvalidDataException("An unfinished marriage's surname choices no longer fit both names.");
                if (marriage.ChosenSurname is not null || marriage.UsedTieBreak || marriage.LatestPlayerRename is not null ||
                    marriage.SurnameReceipt is not null && session.Outcome != "participant_unavailable" ||
                    marriage.SurnameReceipt is null && (liveSession is null || session.Outcome == "participant_unavailable") ||
                    session.Outcome is "surname_agreed" or "surname_draw" || liveSession is not null && !SameConversation(liveSession, session))
                    throw new InvalidDataException("An incomplete marriage claims a final surname or lacks its resumable session.");
                continue;
            }
            if (marriage.SurnameReceipt is null || marriage.CompletedTick != session.LastUpdatedTick ||
                session.Status != AgentConversationStatus.Closed || session.Outcome is not ("surname_agreed" or "surname_draw") ||
                marriage.CompletedTick < marriage.AcceptedTick || marriage.CompletedTick > society.WorldTick ||
                AgentMarriageRules.ResolveSurname(state.WorldSeed, marriage, session, out var usedTieBreak) != marriage.ChosenSurname ||
                usedTieBreak != marriage.UsedTieBreak || liveSession is not null && !SameConversation(liveSession, session))
                throw new InvalidDataException("A completed marriage has an invalid surname receipt or draw.");
            if (marriage.LatestPlayerRename is { } rename &&
                (!AgentMarriageRules.HasParticipant(marriage, rename.AgentId) || rename.WorldTick < marriage.CompletedTick ||
                 rename.WorldTick > society.WorldTick || rename.WorldTick > marriage.EndedTick || !ValidSurname(rename.Surname)))
                throw new InvalidDataException("A marriage's later player surname change is invalid.");
            if (marriage.CompletedTick > marriage.EndedTick ||
                marriage.EndReceipt is null && parties.Any(id => InhabitantNameRules.SurnameKey(society.GetInhabitant(id).Name) != marriage.CurrentSurname))
                throw new InvalidDataException("Married agents do not share their recorded current surname.");
        }
        if (conversations.Any(conversation =>
            conversation.Kind == AgentConversationKind.MarriageSurname && !marriages.Any(item => item.SurnameConversationId == conversation.Id) ||
            conversation.Outcome == "agreed" && conversation.WrapUpEffect == AgentConversationEffect.Marriage && !marriages.Any(item => item.Consent.Id == conversation.Id)))
            throw new InvalidDataException("A marriage conversation has no authoritative consent receipt.");
    }

    private static void ValidateEnding(AgentMarriage marriage, SocietyRelationship recorded, SocietyCheckpoint society)
    {
        if (marriage.EndReceipt is not { } ending)
        {
            if (recorded.State != SocietyRelationshipState.Accepted)
                throw new InvalidDataException("A current marriage has an ended partnership but no ending receipt.");
            return;
        }
        var original = marriage.PartnershipReceipt;
        var deaths = new[] { society.GetInhabitant(marriage.InitiatorId).DeathTick,
                society.GetInhabitant(marriage.InviteeId).DeathTick }.OfType<long>().ToArray();
        if (ending.Id != original.Id || ending.Type != SocietyRelationshipType.Partnership ||
            ending.State is not (SocietyRelationshipState.Revoked or SocietyRelationshipState.EndedByDeath) ||
            ending.Consent != SocietyConsentState.Revoked || ending.State != recorded.State ||
            ending.ProposerId != original.ProposerId || ending.TargetId != original.TargetId ||
            ending.ProposedTick != original.ProposedTick || ending.Revision < original.Revision || ending.Revision > recorded.Revision ||
            ending.AcceptedParties is null || !ending.AcceptedParties.Order(StringComparer.Ordinal).SequenceEqual(original.AcceptedParties.Order(StringComparer.Ordinal)) ||
            ending.EffectiveTick < marriage.AcceptedTick || ending.EffectiveTick > recorded.EffectiveTick ||
            ending.EffectiveTick > society.WorldTick ||
            ending.State == SocietyRelationshipState.EndedByDeath && (deaths.Length == 0 || deaths.Min() != ending.EffectiveTick) ||
            ending.State == SocietyRelationshipState.Revoked &&
                (deaths.Any(tick => tick < ending.EffectiveTick) || deaths.Length == 0 && ending.EffectiveTick != recorded.EffectiveTick))
            throw new InvalidDataException("A saved marriage has an invalid separation or death receipt.");
    }

    private static bool ValidOriginalName(string? name) => name is not null && name.Length <= 48 && !name.Any(char.IsControl) &&
        InhabitantNameRules.CanonicalKey(name) is { Length: <= 48 } && InhabitantNameRules.SurnameKey(name) is not null;

    private static bool ValidSurname(string? name) => !string.IsNullOrWhiteSpace(name) && name.Length <= 48 &&
        !name.Any(char.IsControl) && !name.Any(char.IsWhiteSpace) && InhabitantNameRules.CanonicalKey(name) == name;

    private static bool SameConversation(AgentConversation first, AgentConversation second) =>
        first with
        {
            Turns = second.Turns,
            AcceptedParticipantIds = second.AcceptedParticipantIds,
            WrapUpAcceptedBy = second.WrapUpAcceptedBy,
            ResumeAcceptedBy = second.ResumeAcceptedBy,
        } == second && first.AcceptedParticipantIds.SequenceEqual(second.AcceptedParticipantIds) &&
        first.WrapUpAcceptedBy.SequenceEqual(second.WrapUpAcceptedBy) && first.ResumeAcceptedBy.SequenceEqual(second.ResumeAcceptedBy) &&
        first.Turns.Count == second.Turns.Count && first.Turns.Select((turn, index) =>
            turn with { ListenerIds = second.Turns[index].ListenerIds } == second.Turns[index] &&
            turn.ListenerIds.SequenceEqual(second.Turns[index].ListenerIds)).All(equal => equal);
}

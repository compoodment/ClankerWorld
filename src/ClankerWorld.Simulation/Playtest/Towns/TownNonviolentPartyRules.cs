using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>Current response authority, distinct from retained notice participants and personal liability.</summary>
public static class TownNonviolentPartyRules
{
    public static TownCaseParty[] CurrentParties(TownRuntimeState town, TownViolationCase item,
        SocietyCheckpoint society, long tick) => CurrentParties(town, item.Allegation, item.Filings, society, tick);

    public static TownCaseParty[] CurrentParties(TownRuntimeState town, TownViolationAllegation allegation,
        IEnumerable<TownViolationFiling> filings, SocietyCheckpoint society, long tick)
    {
        var recorded = filings.ToArray();
        bool Adult(string id) => society.Inhabitants.Any(person => person.Id == id && person.Status == SocietyInhabitantStatus.Active &&
            person.AgeBand is SocietyAgeBand.Adult or SocietyAgeBand.Elder);
        var people = recorded.Where(filing => filing.Kind == "affected" && filing.AgentId != allegation.SubjectId)
            .Select(filing => (Id: filing.AgentId, Role: "affected")).Prepend((allegation.SubjectId, "subject")).Distinct();
        var parties = people.Select(entry =>
        {
            var person = society.GetInhabitant(entry.Id);
            var adult = Adult(entry.Id);
            var care = !adult && person.PrimaryCaregiverId is { } caregiver && Adult(caregiver)
                ? society.Relationships.FirstOrDefault(edge => edge.Type == SocietyRelationshipType.Caregiver &&
                    edge.State == SocietyRelationshipState.Accepted && edge.TargetId == person.Id && edge.ProposerId == caregiver) : null;
            return new TownCaseParty(entry.Role + ":" + person.Id, entry.Role, person.Id,
                adult ? person.Id : care?.ProposerId, care?.Id, care?.Revision, person.HouseholdId);
        }).ToList();
        if (recorded.FirstOrDefault(filing => filing.Kind == "town") is { } townFiling)
        {
            string? representative = null;
            if (town.Government?.Arrangement.Ordinary == TownArrangementRules.Mayor)
                representative = town.Government.Offices.FirstOrDefault(office => office.Mandates == "ordinary" &&
                    office.HolderId is { } holder && Adult(holder) && town.ResidentIds.Contains(holder, StringComparer.Ordinal) &&
                    office.TermStartTick <= tick && office.TermEndTick > tick)?.HolderId;
            else if (town.Government?.Arrangement.Ordinary is TownArrangementRules.Council or TownArrangementRules.ElectedCouncil or TownArrangementRules.AllAdultCouncil &&
                town.Governance is { } council)
                representative = recorded.Where(filing => filing.Kind == "town" && Adult(filing.AgentId) &&
                        council.Members.Contains(filing.AgentId, StringComparer.Ordinal) && town.ResidentIds.Contains(filing.AgentId, StringComparer.Ordinal) &&
                        council.Proposals.Any(proposal => proposal.Id == filing.AuthorityId && proposal.Kind == "law_case" &&
                            proposal.Status == "passed" && proposal.AuthorId == filing.AgentId))
                    .OrderByDescending(filing => filing.Tick).ThenBy(filing => filing.AgentId, StringComparer.Ordinal)
                    .Select(filing => filing.AgentId).FirstOrDefault();
            parties.Add(new("town:" + town.Id, "town", townFiling.AgentId, representative, null, null,
                representative is null ? null : society.GetInhabitant(representative).HouseholdId));
        }
        return parties.OrderBy(party => party.Id, StringComparer.Ordinal).ToArray();
    }
}

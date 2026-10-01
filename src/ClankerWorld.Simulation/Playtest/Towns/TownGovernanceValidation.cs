using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

public static partial class TownGovernanceValidation
{
    public static void Validate(TownRuntimeState town, SocietyCheckpoint society, int day)
    {
        if (town.Governance is not { } state) return; // Town setup has no open civic process yet.
        var tick = society.WorldTick;
        var known = society.Inhabitants.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        var adults = town.ResidentIds.Where(id => society.Inhabitants.Any(p => p.Id == id &&
            p.Status == SocietyInhabitantStatus.Active && p.AgeBand is SocietyAgeBand.Adult or SocietyAgeBand.Elder)).ToHashSet(StringComparer.Ordinal);
        if (state.Members is null || state.Candidates is null || state.Proposals is null ||
            state.ElectionHistory is null || state.Notices is null || state.Knowledge is null ||
            state.Form is not ("all_adult" or "representative") || state.Fallback is not ("initial" or "none" or "demographic" or "candidates") ||
            state.Revision < 0 || state.Sequence < 0 || state.RetryTick < 0 || state.RetryCircumstances is null ||
            state.RetryCircumstances.Length > 64 || !Unique(state.Members) || state.Members.Any(id => !adults.Contains(id)) ||
            state.Form == "all_adult" && !adults.SetEquals(state.Members) ||
            state.Form == "representative" && (state.Members.Count > TownGovernanceRules.Seats || state.TermEndTick is null) ||
            state.TermEndTick is < 0 || state.Candidates.Any(c => c is null || !adults.Contains(c.AgentId) ||
                !c.FullTerm && c.RemainderTermEndTick is null || c.RemainderTermEndTick is < 0) ||
            !Unique(state.Candidates.Select(c => c.AgentId)) || state.Proposals.Any(p => p is null) ||
            !Unique(state.Proposals.Select(p => p.Id)) || state.ElectionHistory.Any(e => e is null) ||
            !Unique(state.ElectionHistory.Select(e => e.Id).Concat(state.Election is { } live ? [live.Id] : [])) ||
            state.Notices.Any(n => n is null) || !Unique(state.Notices.Select(n => n.Id)) ||
            state.Knowledge.Any(k => k is null) || !Unique(state.Knowledge.Select(k => k.AgentId + "|" + k.NoticeId)))
            throw new InvalidDataException("A Town's saved council, candidates or civic ledger is invalid.");
        foreach (var proposal in state.Proposals)
        {
            if (string.IsNullOrWhiteSpace(proposal.Id) || !proposal.Id.StartsWith(town.Id + ":proposal:", StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(proposal.RequestKey) || proposal.Kind is not ("law" or "admission") || !known.Contains(proposal.AuthorId) ||
                proposal.Kind == "admission" && (proposal.SubjectId is null || !known.Contains(proposal.SubjectId)) ||
                proposal.Kind == "law" && proposal.SubjectId is not null ||
                string.IsNullOrWhiteSpace(proposal.Text) || proposal.Text.Length > TownGovernanceRules.MaximumProposalText || proposal.Text.Any(char.IsControl) ||
                proposal.Circumstances is null || proposal.Circumstances.Length > 512 || proposal.CouncilRevision < 0 || proposal.CouncilRevision > state.Revision ||
                proposal.OpenedTick < 0 || proposal.OpenedTick > tick || proposal.DeadlineTick != proposal.OpenedTick + day ||
                proposal.Voters is null || proposal.Votes is null || !Unique(proposal.Voters) || proposal.Voters.Any(id => !known.Contains(id)) ||
                proposal.RequiredYes < 1 || proposal.RequiredYes > Math.Max(2, proposal.Voters.Count) ||
                proposal.Votes.Any(v => v is null || !proposal.Voters.Contains(v.AgentId, StringComparer.Ordinal) ||
                    proposal.Kind == "admission" && v.AgentId == proposal.SubjectId) || !Unique(proposal.Votes.Select(v => v.AgentId)) ||
                proposal.Status is not ("pending" or "passed" or "rejected" or "cancelled" or "withdrawn") ||
                proposal.Status == "pending" && (proposal.SettledTick is not null || proposal.DeadlineTick <= tick ||
                    proposal.CouncilRevision != state.Revision || !proposal.Voters.SequenceEqual(state.Members) ||
                    proposal.RequiredYes != (state.Form == "representative" ? 2 : state.Members.Count / 2 + 1) ||
                    proposal.Votes.Count(v => v.Yes) >= proposal.RequiredYes) ||
                proposal.Status != "pending" && (proposal.SettledTick is null || proposal.SettledTick < proposal.OpenedTick || proposal.SettledTick > tick) ||
                proposal.Status == "passed" && proposal.Votes.Count(v => v.Yes) < proposal.RequiredYes)
                throw new InvalidDataException("A Town's saved proposal or final votes are invalid.");
        }
        if (state.Proposals.Where(p => p.Status == "pending").GroupBy(p => p.RequestKey, StringComparer.Ordinal).Any(g => g.Count() > 1))
            throw new InvalidDataException("Equivalent pending Town proposals must share a single window.");
        foreach (var election in state.ElectionHistory.Concat(state.Election is { } current ? [current] : []))
        {
            var active = state.Election?.Id == election.Id;
            if (string.IsNullOrWhiteSpace(election.Id) || !election.Id.StartsWith(town.Id + ":election:", StringComparison.Ordinal) ||
                election.Kind is not ("initial" or "regular" or "replacement") ||
                election.Stage is not ("main" or "runoff" or "ready" or "completed" or "failed" or "cancelled") ||
                active != (election.Stage is "main" or "runoff" or "ready") || election.OpenedTick < 0 || election.OpenedTick > tick ||
                election.DeadlineTick != election.OpenedTick + day || election.TermEndTick < 0 || election.Seats is < 1 or > TownGovernanceRules.Seats ||
                election.Voters is null || election.Candidates is null || election.Ballots is null || election.Supported is null ||
                election.SettledSeats is null || election.DrawOrder is null || !Unique(election.Voters) || !Unique(election.Candidates) ||
                !Unique(election.Supported) || !Unique(election.SettledSeats) || !Unique(election.DrawOrder) ||
                election.Voters.Concat(election.Candidates).Concat(election.Supported).Concat(election.SettledSeats).Any(id => !known.Contains(id)) ||
                election.Ballots.Any(b => b is null || b.Choices is null || !election.Voters.Contains(b.AgentId, StringComparer.Ordinal) ||
                    b.Choices.Count > election.Seats || !Unique(b.Choices) || b.Choices.Any(id => !election.Candidates.Contains(id, StringComparer.Ordinal))) ||
                !Unique(election.Ballots.Select(b => b.AgentId)) ||
                election.SettledSeats.Any(id => !election.Supported.Contains(id, StringComparer.Ordinal)) ||
                election.DrawOrder.Any(id => !election.Supported.Contains(id, StringComparer.Ordinal)) ||
                active && (election.Voters.Concat(election.Candidates).Concat(election.SettledSeats).Any(id => !adults.Contains(id)) ||
                    election.Stage == "ready" && election.SettledSeats.Count != TownGovernanceRules.Seats))
                throw new InvalidDataException("A Town's saved election, consent, ballots or recorded draw is invalid.");
        }
        foreach (var notice in state.Notices)
            if (string.IsNullOrWhiteSpace(notice.Id) || string.IsNullOrWhiteSpace(notice.SubjectId) ||
                notice.Kind is not ("council" or "candidate" or "election" or "runoff" or "result" or "proposal" or "cancelled") ||
                string.IsNullOrWhiteSpace(notice.Text) || notice.Text.Length > 32768 || notice.PostedTick < 0 || notice.PostedTick > tick)
                throw new InvalidDataException("A saved Town civic notice is invalid.");
        foreach (var receipt in state.Knowledge)
            if (!known.Contains(receipt.AgentId) || !state.Notices.Any(n => n.Id == receipt.NoticeId && n.PostedTick <= receipt.LearnedTick) ||
                receipt.LearnedTick < 0 || receipt.LearnedTick > tick || receipt.SourceAgentId is { } source &&
                    (!known.Contains(source) || !state.Knowledge.Any(k => k.AgentId == source && k.NoticeId == receipt.NoticeId && k.LearnedTick <= receipt.LearnedTick)))
                throw new InvalidDataException("Saved civic knowledge requires an actual notice or informed relay.");
    }

    private static bool Unique(IEnumerable<string> ids) => ids.All(id => !string.IsNullOrWhiteSpace(id)) &&
        ids.Distinct(StringComparer.Ordinal).Count() == ids.Count();
}

using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>Rejects civic authority that is not backed by the saved resident and election decisions.</summary>
internal static class TownGovernmentStateValidation
{
    public static void Validate(TownRuntimeState town, TownGovernmentState state, TownGovernanceState council,
        SocietyCheckpoint society, int day)
    {
        var tick = society.WorldTick;
        var known = society.Inhabitants.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        var adults = town.ResidentIds.Where(id => society.Inhabitants.Any(p => p.Id == id &&
            p.Status == SocietyInhabitantStatus.Active && p.AgeBand is SocietyAgeBand.Adult or SocietyAgeBand.Elder)).ToHashSet(StringComparer.Ordinal);
        if (state.Offices is null || state.Changes.Any(c => c is null) || state.Consents.Any(c => c is null) ||
            state.OfficeHistory.Any(o => o is null) || state.Offices.Any(o => o is null) || state.ContestHistory.Any(c => c is null) ||
            state.Changes.Count(c => c.Status is "voting" or "handover") > 1 ||
            state.Changes.Where(c => c.Status is "queued" or "voting" or "handover").GroupBy(c => c.RequestKey).Any(g => g.Count() > 1))
            Fail("The Town's government processes or office records are invalid.");
        var forcedElections = new HashSet<string>(StringComparer.Ordinal);
        foreach (var change in state.Changes)
        {
            var opened = change.OpenedTick is not null;
            var approved = change.ApprovedTick is not null;
            if (!TownGovernmentValidation.ValidId(change.Id, town.Id + ":government:", state.Sequence) ||
                !TownArrangementRules.IsSupported(change.Target) || !known.Contains(change.AuthorId) ||
                change.Kind is not ("arrangement" or "replace_mayor") ||
                change.RequestKey != change.Kind + ":" + TownArrangementRules.Key(change.Target) ||
                change.Circumstances is not { Length: 64 } || change.SubmittedTick < 0 || change.SubmittedTick > tick ||
                change.Status is not ("queued" or "voting" or "handover" or "completed" or "rejected" or "withdrawn" or "cancelled") ||
                change.Voters is null || change.OpeningVoters is null || change.Votes is null ||
                !Ids(change.OpeningVoters, known) || !Ids(change.Voters, known) || change.Voters.Any(id => !change.OpeningVoters.Contains(id)) ||
                change.Votes.Any(v => v is null || !change.Voters.Contains(v.AgentId)) || !Unique(change.Votes.Select(v => v.AgentId)) ||
                opened != (change.DeadlineTick is not null) ||
                opened && (change.OpenedTick < change.SubmittedTick || change.OpenedTick > tick || change.DeadlineTick != change.OpenedTick + day) ||
                !opened && (change.OpeningVoters.Count != 0 || change.Voters.Count != 0 || change.Votes.Count != 0) ||
                change.Status == "queued" && (opened || change.SettledTick is not null) ||
                change.Status is "voting" or "handover" or "completed" or "rejected" && !opened ||
                change.Status == "voting" && (change.DeadlineTick <= tick || change.Voters.Any(id => !adults.Contains(id)) || approved ||
                    change.Votes.Count(v => v.Yes) > change.Voters.Count / 2 || change.SettledTick is not null) ||
                approved != (change.HandoverDeadlineTick is not null) ||
                approved && (change.ApprovedTick < change.OpenedTick || change.ApprovedTick > change.DeadlineTick ||
                    change.ApprovedTick > tick || change.HandoverDeadlineTick != change.ApprovedTick + (long)TownArrangementRules.HandoverDays * day ||
                    change.Votes.Count(v => v.Yes) <= change.Voters.Count / 2) ||
                change.Status is "handover" or "completed" && !approved ||
                change.Status == "handover" && (change.HandoverDeadlineTick <= tick || change.SettledTick is not null) ||
                change.Status == "completed" && (change.SettledTick < change.ApprovedTick || change.SettledTick > change.HandoverDeadlineTick) ||
                change.Status is "completed" or "rejected" or "withdrawn" or "cancelled" &&
                    (change.SettledTick is null || change.SettledTick < change.SubmittedTick || change.SettledTick > tick) ||
                change.SuccessorId is not null && !known.Contains(change.SuccessorId) ||
                change.Reason is { Length: > 512 })
                Fail("A saved protected resident vote or handover is invalid.");
            ValidateForcedCouncilElection(town.Id, council, change, forcedElections);
        }
        var lastArrangement = state.Changes.LastOrDefault(c => c.Status == "completed")?.Target ?? TownArrangementRules.Initial;
        if (state.Arrangement != lastArrangement || !Unique(state.Offices.Select(o => o.Mandates)) ||
            !state.Offices.Select(o => o.Mandates).ToHashSet(StringComparer.Ordinal).SetEquals(TownGovernmentRules.Mandates(state.Arrangement)))
            Fail("The current government and its mandates need a completed resident handover.");
        if (state.Consents.Any(c => !adults.Contains(c.AgentId) || !TownArrangementRules.IsMandates(c.Mandates) || c.Tick < 0 || c.Tick > tick) ||
            state.Consents.Select(c => (c.AgentId, c.Mandates)).Distinct().Count() != state.Consents.Count)
            Fail("Saved mayoral willingness must be personal, eligible and specific to its mandates.");
        foreach (var contest in state.ContestHistory.Concat(state.Contest is { } live ? [live] : []))
            ValidateContest(town, state, council, contest, known, adults, tick, day);
        var completed = state.ContestHistory.Where(c => c.Stage == "completed").ToDictionary(c => c.Id, StringComparer.Ordinal);
        foreach (var office in state.Offices)
        {
            if (office.Mandates is not ("land" or "ordinary") ||
                office.HolderId is null && (office.TermStartTick is not null || office.TermEndTick is not null || office.ElectionId is not null ||
                    office.VacantSinceTick is null || office.VacantSinceTick < 0 || office.VacantSinceTick > tick || string.IsNullOrWhiteSpace(office.VacancyReason)) ||
                office.HolderId is not null && (!adults.Contains(office.HolderId) || office.TermStartTick is null || office.TermStartTick > tick ||
                    office.TermEndTick != office.TermStartTick + (long)TownArrangementRules.MayorTermDays * day || office.TermEndTick <= tick ||
                    office.VacantSinceTick is not null || office.VacancyReason is not null ||
                    !ValidTerm(completed, office.ElectionId, office.HolderId, office.Mandates, office.TermStartTick.Value)))
                Fail("An elected mandate is vacant or must have a valid, unexpired election-backed term.");
        }
        foreach (var term in state.OfficeHistory)
            if (term.Mandates is not ("land" or "ordinary") || !known.Contains(term.HolderId) || term.StartTick < 0 ||
                term.EndTick < term.StartTick || term.EndTick > tick || term.EndTick > term.StartTick + (long)TownArrangementRules.MayorTermDays * day ||
                string.IsNullOrWhiteSpace(term.EndReason) || term.EndReason.Length > 512 ||
                !ValidTerm(completed, term.ElectionId, term.HolderId, term.Mandates, term.StartTick))
                Fail("An archived elected mandate has no valid term or election.");
        var terms = state.OfficeHistory.Select(o => (o.Mandates, o.StartTick, End: o.EndTick, o.ElectionId))
            .Concat(state.Offices.Where(o => o.HolderId is not null).Select(o => (o.Mandates, o.TermStartTick!.Value, End: o.TermEndTick!.Value, o.ElectionId!))).ToArray();
        foreach (var group in terms.GroupBy(t => t.Mandates))
        {
            var ordered = group.OrderBy(t => t.Item2).ThenBy(t => t.End).ToArray();
            if (!Unique(ordered.Select(t => t.Item4)) || ordered.Skip(1).Where((t, i) => t.Item2 < ordered[i].End).Any())
                Fail("Two terms cannot hold the same mandate at once.");
        }
        var leader = TownGovernmentRules.GoverningOffice(state)?.HolderId;
        if (state.Arrangement.Ordinary == TownArrangementRules.Mayor &&
            (leader is not null ? council.Form != "leader" || !council.Members.SequenceEqual([leader]) : council.Form != "all_adult") ||
            state.Arrangement.Ordinary != TownArrangementRules.Mayor && council.Form == "leader" ||
            state.Arrangement.Ordinary == TownArrangementRules.AllAdultCouncil && council.Form != "all_adult")
            Fail("Ordinary decision authority does not match the approved arrangement and living officeholder.");
    }

    private static void ValidateForcedCouncilElection(string townId, TownGovernanceState council,
        TownGovernmentChange change, HashSet<string> forcedElections)
    {
        if (change.ForcedCouncilElectionId is not { } electionId) return;
        if (!TownGovernmentValidation.ValidId(electionId, townId + ":election:", council.Sequence) ||
            !forcedElections.Add(electionId) || change.Kind != "arrangement" ||
            change.Status is not ("handover" or "completed" or "cancelled") || change.ApprovedTick is null ||
            change.Target.Ordinary is not (TownArrangementRules.Council or TownArrangementRules.ElectedCouncil))
            Fail("A forced Council election needs one approved government change in the same Town.");
        var elections = council.ElectionHistory.Concat(council.Election is { } live ? [live] : [])
            .Where(election => election.Id == electionId).ToArray();
        if (elections.Length != 1)
            Fail("A government change's forced Council election must exist exactly once.");
        var election = elections[0];
        // A runoff updates OpenedTick, so it can be later than approval or the first round.
        if (election.Kind != "initial" || election.OpenedTick < change.ApprovedTick ||
            change.SettledTick is { } settled && election.OpenedTick > settled ||
            council.Election?.Id == electionId && change.Status != "handover" ||
            election.Stage == "completed" && change.Status != "completed")
            Fail("A forced Council election does not match its creating handover's lifecycle.");
    }

    private static bool ValidTerm(Dictionary<string, TownMayoralContest> completed, string? electionId,
        string holder, string mandate, long start) => electionId is not null && completed.TryGetValue(electionId, out var election) &&
        election.WinnerId == holder && election.Mandates.Split('+').Contains(mandate) && election.SettledTick == start;

    private static void ValidateContest(TownRuntimeState town, TownGovernmentState state, TownGovernanceState council,
        TownMayoralContest contest, HashSet<string> known, HashSet<string> adults, long tick, int day)
    {
        var live = ReferenceEquals(state.Contest, contest);
        if (!TownGovernmentValidation.ValidId(contest.Id, town.Id + ":mayor:", state.Sequence) ||
            !TownArrangementRules.IsMandates(contest.Mandates) || contest.Purpose is not ("handover" or "vacancy" or "renewal") ||
            contest.Stage is not ("waiting" or "voting" or "ready" or "completed" or "failed" or "cancelled") ||
            live != (contest.Stage is "waiting" or "voting" or "ready") || contest.Round < 0 || contest.Interruptions < 0 ||
            contest.OpenedTick < 0 || contest.OpenedTick > tick || contest.Rounds is null || contest.Rounds.Any(r => r is null) ||
            contest.Voters is null || contest.Candidates is null || contest.Ballots is null || contest.TiedCandidates is null ||
            !Ids(contest.Voters, known) || !Ids(contest.Candidates, known) || !Ids(contest.TiedCandidates, known) ||
            !Ballots(contest.Ballots, contest.Voters, contest.Candidates) ||
            contest.Purpose == "handover" != (contest.ChangeId is not null) ||
            contest.ChangeId is { } changeId && !state.Changes.Any(c => c.Id == changeId && c.ApprovedTick is not null &&
                contest.OpenedTick >= c.ApprovedTick && (!live || c.Status == "handover") &&
                (contest.Stage != "completed" || c.Status == "completed") && contest.Mandates.Split('+').All(m => TownGovernmentRules.Mandates(c.Target).Contains(m))) ||
            live && contest.Voters.Concat(contest.Candidates).Any(id => !adults.Contains(id)) ||
            live && contest.Candidates.Any(id => !state.Consents.Any(c => c.AgentId == id && c.Mandates == contest.Mandates)) ||
            contest.Stage == "voting" && (contest.Round < 1 || contest.RoundOpenedTick is null || contest.RoundOpenedTick > tick ||
                contest.RoundDeadlineTick != contest.RoundOpenedTick + day || contest.RoundDeadlineTick <= tick ||
                council.Election is { Stage: "main" or "runoff" } || contest.Candidates.Count == 0) ||
            contest.Stage == "waiting" && (contest.RoundOpenedTick is not null || contest.RoundDeadlineTick is not null ||
                contest.Voters.Count > 0 || contest.Candidates.Count > 0 || contest.Ballots.Count > 0) ||
            !live && (contest.SettledTick is null || contest.SettledTick < contest.OpenedTick || contest.SettledTick > tick) ||
            live && contest.SettledTick is not null || contest.Reason is { Length: > 512 })
            Fail("A saved mayoral contest, mandate, electorate or ballot is invalid.");
        string[]? priorTie = null;
        foreach (var (round, index) in contest.Rounds.Select((r, i) => (r, i)))
        {
            if (round.Number != index + 1 || round.OpenedTick < contest.OpenedTick || round.ClosedTick < round.OpenedTick || round.ClosedTick > tick ||
                index > 0 && round.OpenedTick < contest.Rounds[index - 1].ClosedTick ||
                round.Result is not ("tie" or "winner" or "interrupted" or "failed" or "cancelled") ||
                round.Voters is null || round.Candidates is null || round.Ballots is null || round.TiedCandidates is null ||
                !Ids(round.Voters, known) || !Ids(round.Candidates, known) || !Ids(round.TiedCandidates, known) ||
                !Ballots(round.Ballots, round.Voters, round.Candidates) ||
                priorTie is not null && round.Candidates.Any(id => !priorTie.Contains(id)) ||
                round.Result is "winner" or "tie" && round.ClosedTick < round.OpenedTick + day)
                Fail("Saved mayoral rounds must preserve their electorate, ballots and previous top tie.");
            var top = Top(round.Candidates, round.Ballots);
            if (round.Result == "tie")
            {
                if (top.Length < 2) Fail("A recorded mayoral tie needs tied candidates with actual votes.");
                priorTie = top;
            }
            if (round.Result == "winner" && (top.Length != 1 || top[0] != contest.WinnerId))
                Fail("A mayoral winner needs the unique highest positive vote count in the deciding round.");
        }
        if (contest.Interruptions != contest.Rounds.Count(r => r.Result == "interrupted") ||
            contest.Round != contest.Rounds.Count + (contest.Stage == "voting" ? 1 : 0) ||
            contest.Stage is "ready" or "completed" && (contest.WinnerId is null || (contest.Rounds.Count == 0 || contest.Rounds[^1].Result != "winner")) ||
            contest.WinnerId is not null && !known.Contains(contest.WinnerId) ||
            priorTie is not null && contest.Candidates.Any(id => !priorTie.Contains(id)))
            Fail("A saved mayoral result or interrupted round is inconsistent.");
    }

    private static string[] Top(IReadOnlyList<string> candidates, IReadOnlyList<TownMayoralBallot> ballots)
    {
        var totals = candidates.ToDictionary(id => id, id => ballots.Count(b => b.CandidateId == id));
        var most = totals.Count == 0 ? 0 : totals.Values.Max();
        return most == 0 ? [] : candidates.Where(id => totals[id] == most).ToArray();
    }
    private static bool Ballots(IReadOnlyList<TownMayoralBallot> ballots, IReadOnlyList<string> voters, IReadOnlyList<string> candidates) =>
        ballots.All(b => b is not null && voters.Contains(b.AgentId) && candidates.Contains(b.CandidateId)) && Unique(ballots.Select(b => b.AgentId));
    private static bool Ids(IEnumerable<string> ids, HashSet<string> known) => Unique(ids) && ids.All(known.Contains);
    private static bool Unique(IEnumerable<string> ids) => ids.All(id => !string.IsNullOrWhiteSpace(id)) && ids.Distinct(StringComparer.Ordinal).Count() == ids.Count();
    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void Fail(string message) => throw new InvalidDataException(message);
}

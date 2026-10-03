namespace ClankerWorld.Simulation.Playtest;

public static partial class TownGovernmentRules
{
    public static (TownGovernanceState Council, TownGovernmentState Government) RegisterMayor(
        TownGovernanceState council, TownGovernmentState state, string actor, string mandates,
        IEnumerable<string> adultResidents, long tick)
    {
        if (!Has(adultResidents, actor) || !TownArrangementRules.IsMandates(mandates))
            throw new InvalidOperationException("Only an adult resident may personally agree to a supported mayoral mandate.");
        if (state.Consents.Any(c => c.AgentId == actor && c.Mandates == mandates)) return (council, state);
        state = state with { Consents = state.Consents.Append(new TownMayoralConsent(actor, mandates, tick)).ToArray() };
        return (Notice(council, "mayor", "mayor-consent:" + actor,
            $"{actor} personally agreed to seek election for {TownArrangementRules.MandateLabel(mandates)}. This grants no office.", tick), state);
    }

    public static (TownGovernanceState Council, TownGovernmentState Government) WithdrawMayor(
        TownGovernanceState council, TownGovernmentState state, string actor, string mandates, long tick)
    {
        if (!state.Consents.Any(c => c.AgentId == actor && c.Mandates == mandates))
            throw new InvalidOperationException("There is no matching mayoral consent to withdraw.");
        state = state with { Consents = state.Consents.Where(c => c.AgentId != actor || c.Mandates != mandates).ToArray() };
        return (Notice(council, "mayor", "mayor-consent:" + actor,
            $"{actor} withdrew willingness to seek {TownArrangementRules.MandateLabel(mandates)}. Any current office continues unless separately resigned.", tick), state);
    }

    public static TownGovernmentState VoteMayor(TownGovernmentState state, string token, string actor, string candidate, long tick)
    {
        if (state.Contest is not { Stage: "voting" } contest || RoundToken(contest) != token ||
            tick >= contest.RoundDeadlineTick || !Has(contest.Voters, actor) || !Has(contest.Candidates, candidate))
            throw new InvalidOperationException("This mayoral ballot is stale or ineligible.");
        return state with
        {
            Contest = contest with
            {
                Ballots = contest.Ballots.Where(b => b.AgentId != actor).Append(new(actor, candidate))
                .OrderBy(b => b.AgentId, StringComparer.Ordinal).ToArray()
            }
        };
    }

    public static (TownGovernanceState Council, TownGovernmentState Government) Resign(
        TownGovernanceState council, TownGovernmentState state, string actor, string mandate, long tick)
    {
        var office = state.Offices.SingleOrDefault(o => o.Mandates == mandate && o.HolderId == actor);
        if (office is null) throw new InvalidOperationException("Only the current holder may resign this mandate.");
        return EndOffice(council, state, office, tick, "The holder resigned this mandate.");
    }

    private static (TownGovernanceState, TownGovernmentState) EndOffice(TownGovernanceState council,
        TownGovernmentState state, TownOffice office, long tick, string reason)
    {
        var ended = Math.Min(tick, office.TermEndTick!.Value);
        state = state with
        {
            OfficeHistory = state.OfficeHistory.Append(new TownOfficeTerm(office.HolderId!, office.Mandates,
                office.TermStartTick!.Value, ended, reason, office.ElectionId!)).ToArray(),
            Offices = state.Offices.Select(o => o.Mandates == office.Mandates
                ? new TownOffice(o.Mandates, null, null, null, ended, reason) : o).ToArray()
        };
        return (Notice(council, "mayor", "office:" + office.Mandates,
            $"{office.HolderId}'s mandate for {TownArrangementRules.MandateLabel(office.Mandates)} ended. {reason} " +
            (office.Mandates == "land" ? "Land decisions wait for a valid successor." : "Every adult resident makes ordinary decisions until a valid successor takes office."), tick), state);
    }

    private static (TownGovernanceState, TownGovernmentState) AdvanceMayor(TownGovernanceState council,
        TownGovernmentState state, string townId, string[] adults, long tick, int day)
    {
        if (state.Contest is { } live)
        {
            var willing = Willing(state, adults, live.Mandates);
            var voters = live.Voters.Where(id => Has(adults, id)).ToArray();
            var candidates = live.Candidates.Where(id => Has(willing, id)).ToArray();
            live = live with
            {
                Voters = voters,
                Candidates = candidates,
                Ballots = live.Ballots.Where(b => Has(voters, b.AgentId) && Has(candidates, b.CandidateId)).ToArray(),
                TiedCandidates = live.TiedCandidates.Where(id => Has(willing, id)).ToArray()
            };
            state = state with { Contest = live };
            if (live.Purpose == "handover" && !state.Changes.Any(c => c.Id == live.ChangeId && c.Status == "handover"))
                (council, state) = ArchiveContest(council, state, "cancelled", "The creating transition is no longer active.", adults, tick, day);
            else if (live.Stage == "ready" && (live.WinnerId is null || !Has(willing, live.WinnerId)))
                (council, state) = ArchiveContest(council, state, "failed", "The selected successor is no longer eligible or willing.", adults, tick, day);
            else if (live.Stage == "voting" && council.Election is not null)
            {
                live = SaveRound(live, tick, "interrupted") with
                {
                    Stage = "waiting",
                    Voters = [],
                    Candidates = [],
                    Ballots = [],
                    RoundOpenedTick = null,
                    RoundDeadlineTick = null,
                    Interruptions = live.Interruptions + 1
                };
                state = state with { Contest = live };
                council = Notice(council, "mayor", live.Id,
                    "Mayoral voting interrupted by a Council election. Unfinished ballots are discarded; an existing top tie is preserved.", tick);
            }
            else if (live.Stage == "voting" && (candidates.Length == 0 || tick >= live.RoundDeadlineTick))
            {
                var totals = candidates.ToDictionary(id => id, id => live.Ballots.Count(b => b.CandidateId == id), StringComparer.Ordinal);
                var most = totals.Count > 0 ? totals.Values.Max() : 0;
                if (most == 0)
                    (council, state) = ArchiveContest(council, state, "failed", "No eligible candidate received a vote in this round.", adults, tick, day);
                else
                {
                    var top = Ordered(candidates.Where(id => totals[id] == most));
                    live = SaveRound(live, tick, top.Length == 1 ? "winner" : "tie");
                    if (top.Length == 1)
                    {
                        live = live with { Stage = "ready", WinnerId = top[0] };
                        state = state with { Contest = live };
                        council = Notice(council, "mayor", live.Id,
                            $"Residents selected {top[0]} for {TownArrangementRules.MandateLabel(live.Mandates)}. Authority starts only at a valid handover or term start.", tick);
                    }
                    else
                    {
                        state = state with
                        {
                            Contest = live with
                            {
                                Stage = "waiting",
                                TiedCandidates = top,
                                Ballots = [],
                                Voters = [],
                                Candidates = [],
                                RoundOpenedTick = null,
                                RoundDeadlineTick = null
                            }
                        };
                        council = Notice(council, "mayor", live.Id,
                            "Mayoral vote tied between " + string.Join(", ", top) + ". Another resident vote is required; no draw is used.", tick);
                    }
                }
            }
        }
        if (state.Contest is { Stage: "ready", Purpose: not "handover" } ready &&
            ready.Mandates.Split('+').All(m => !state.Offices.Any(o => o.Mandates == m && o.HolderId is not null)))
            (council, state) = SeatMayor(council, state, ready, tick, day);

        if (state.Contest is null)
        {
            var handover = ActiveChange(state) is { Status: "handover" } change ? change : null;
            var needed = handover is not null
                ? Mandates(handover.Target).Where(m => handover.Kind == "replace_mayor" || !state.Offices.Any(o => o.Mandates == m && o.HolderId is not null)).ToArray()
                : Mandates(state.Arrangement).Where(m => !state.Offices.Any(o => o.Mandates == m && o.HolderId is not null)).ToArray();
            var purpose = handover is not null ? "handover" : "vacancy";
            if (needed.Length == 0)
            {
                // Renewal concerns only mandates whose terms end together. A separate mandate keeps its term.
                var ending = state.Offices.Where(o => o.HolderId is not null && tick >= o.TermEndTick - day)
                    .OrderBy(o => o.TermEndTick).FirstOrDefault();
                if (ending is not null)
                {
                    needed = state.Offices.Where(o => o.HolderId is not null && o.TermEndTick == ending.TermEndTick).Select(o => o.Mandates).ToArray();
                    purpose = "renewal";
                }
            }
            var mandates = string.Join('+', Ordered(needed));
            if (needed.Length > 0 && (tick >= state.MayoralRetryTick ||
                ElectionCircumstances(state, adults, mandates) != state.MayoralRetryCircumstances))
            {
                var id = townId + ":mayor:" + (state.Sequence + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
                state = state with
                {
                    Sequence = state.Sequence + 1,
                    Contest = new(id, purpose, mandates, purpose == "handover" ? handover!.Id : null,
                        "waiting", 0, tick, null, null, [], [], [], [], 0)
                };
                council = Notice(council, "mayor", id,
                    $"An election is due for {TownArrangementRules.MandateLabel(mandates)}. Adult residents must personally agree to stand for these mandates; Council candidacy is not mayoral consent.", tick);
            }
        }
        if (state.Contest is { Stage: "waiting" } waiting && council.Election is null)
        {
            var candidates = Willing(state, adults, waiting.Mandates);
            // A tie remains restricted even if every tied candidate later withdraws.
            if (waiting.Rounds.Any(r => r.Result == "tie")) candidates = candidates.Where(id => Has(waiting.TiedCandidates, id)).ToArray();
            waiting = waiting with
            {
                Stage = "voting",
                Round = waiting.Round + 1,
                RoundOpenedTick = tick,
                RoundDeadlineTick = tick + day,
                Voters = adults,
                Candidates = candidates,
                Ballots = []
            };
            state = state with { Contest = waiting };
            if (candidates.Length == 0)
                return ArchiveContest(council, state, "failed", "No willing eligible candidate is available.", adults, tick, day);
            council = Notice(council, "mayor", RoundToken(waiting),
                $"Mayoral round {waiting.Round}: choose one of {string.Join(", ", candidates)} for {TownArrangementRules.MandateLabel(waiting.Mandates)}. " +
                $"Ballots may change until tick {tick + day}. Self-voting is allowed; a winner needs an actual vote in this round.", tick);
        }
        return (council, state);
    }

    private static TownMayoralContest SaveRound(TownMayoralContest contest, long tick, string result) =>
        contest.RoundOpenedTick is not { } opened || contest.Rounds.Any(r => r.Number == contest.Round) ? contest :
        contest with
        {
            Rounds = contest.Rounds.Append(new TownMayoralRound(contest.Round, opened, tick, result,
            contest.Voters, contest.Candidates, contest.Ballots, contest.TiedCandidates)).ToArray()
        };

    private static (TownGovernanceState, TownGovernmentState) ArchiveContest(TownGovernanceState council,
        TownGovernmentState state, string stage, string reason, string[] adults, long tick, int day)
    {
        var contest = state.Contest!;
        if (contest.Stage == "voting") contest = SaveRound(contest, tick, stage);
        contest = contest with { Stage = stage, SettledTick = tick, Reason = reason };
        state = state with { Contest = null, ContestHistory = state.ContestHistory.Append(contest).ToArray() };
        if (stage == "failed") state = state with
        {
            MayoralRetryTick = tick + day,
            MayoralRetryCircumstances = ElectionCircumstances(state, adults, contest.Mandates)
        };
        return (Notice(council, "mayor", contest.Id, "Mayoral election " + stage + ". " + reason, tick), state);
    }

    private static (TownGovernanceState, TownGovernmentState) SeatMayor(TownGovernanceState council,
        TownGovernmentState state, TownMayoralContest contest, long tick, int day)
    {
        foreach (var mandate in contest.Mandates.Split('+'))
        {
            if (state.Offices.SingleOrDefault(o => o.Mandates == mandate && o.HolderId is not null) is { } previous)
                (council, state) = EndOffice(council, state, previous, tick, "A protected replacement took office.");
            var office = new TownOffice(mandate, contest.WinnerId, tick, tick + (long)TownArrangementRules.MayorTermDays * day, null, null, contest.Id);
            state = state with { Offices = state.Offices.Where(o => o.Mandates != mandate).Append(office).OrderBy(o => o.Mandates, StringComparer.Ordinal).ToArray() };
        }
        state = state with
        {
            Contest = null,
            ContestHistory = state.ContestHistory.Append(contest with
            { Stage = "completed", SettledTick = tick, Reason = "The elected successor took office." }).ToArray(),
            MayoralRetryTick = 0,
            MayoralRetryCircumstances = ""
        };
        return (Notice(council, "mayor", contest.Id,
            $"{contest.WinnerId} took office for {TownArrangementRules.MandateLabel(contest.Mandates)} until tick {tick + (long)TownArrangementRules.MayorTermDays * day}. " +
            "The mandates are separate; election grants no ownership or physical enforcement powers.", tick), state);
    }
}

using System.Globalization;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>Case-only elections use actual resident ballots, never a Council appointment or random tie-break.</summary>
public static class TownCaseJudgeRules
{
    public static string RoundToken(TownCaseJudgeContest contest) =>
        contest.Id + ":" + contest.Round.ToString(CultureInfo.InvariantCulture) + ":" + contest.Interruptions.ToString(CultureInfo.InvariantCulture);
    public static bool IsBusy(TownNonviolentState state) => state.Cases.Any(c => c.Contest is { Stage: "voting" });
    private static TownNonviolentState Replace(TownNonviolentState state, TownViolationCase item) =>
        state with { Cases = state.Cases.Select(c => c.Id == item.Id ? item : c).ToArray() };
    private static bool Willing(TownViolationCase item, string actor) => item.JudgeConsents.Any(c => c.AgentId == actor && c.WithdrawnTick is null);
    private static bool Eligible(TownViolationCase item, string actor, IReadOnlyList<string> adults,
        IReadOnlyDictionary<string, string?> households, IReadOnlyList<TownCaseParty> parties) =>
        adults.Contains(actor, StringComparer.Ordinal) && !TownNonviolentRules.JudgeConflict(actor,
            households.GetValueOrDefault(actor), parties, item.DirectStakeIds.ToHashSet(StringComparer.Ordinal));

    public static TownNonviolentState Register(TownNonviolentState state, string caseId, string actor,
        IReadOnlyList<string> adultResidents, IReadOnlyDictionary<string, string?> households, long tick,
        IReadOnlyList<TownCaseParty>? currentParties = null)
    {
        var item = state.Cases.Single(c => c.Id == caseId);
        if (!Eligible(item, actor, adultResidents, households, currentParties ?? TownNonviolentRules.CurrentRevision(item).Parties))
            throw new InvalidOperationException("Only a willing, eligible and independent adult resident may seek this case mandate.");
        return Willing(item, actor) ? state : Replace(state, item with { JudgeConsents = item.JudgeConsents.Append(new(actor, tick)).ToArray() });
    }

    public static TownNonviolentState Withdraw(TownNonviolentState state, string caseId, string actor, long tick)
    {
        var item = state.Cases.Single(c => c.Id == caseId);
        if (!Willing(item, actor)) throw new InvalidOperationException("There is no active consent for this case mandate.");
        state = Replace(state, item with { JudgeConsents = item.JudgeConsents.Select(c => c.AgentId == actor && c.WithdrawnTick is null ? c with { WithdrawnTick = tick } : c).ToArray() });
        return item.Judge is { Kind: "case_elected" } judge && judge.AgentId == actor ?
            TownNonviolentRules.InvalidateJudge(state, caseId, tick, "willingness_withdrawn") : state;
    }

    public static TownNonviolentState Resign(TownNonviolentState state, string caseId, string actor, long tick)
    {
        var item = state.Cases.Single(c => c.Id == caseId);
        if (item.Judge is not { Kind: "case_elected" } judge || judge.AgentId != actor) throw new InvalidOperationException("Only a current elected case judge may resign this case-only mandate.");
        if (Willing(item, actor)) state = Withdraw(state, caseId, actor, tick);
        return TownNonviolentRules.InvalidateJudge(state, caseId, tick, "resigned");
    }

    public static TownNonviolentState Vote(TownNonviolentState state, string caseId, string token,
        string actor, string candidate, long tick)
    {
        var item = state.Cases.Single(c => c.Id == caseId);
        var contest = item.Contest;
        if (contest is not { Stage: "voting" } || RoundToken(contest) != token || tick >= contest.RoundDeadlineTick ||
            !contest.Voters.Contains(actor, StringComparer.Ordinal) || !contest.Candidates.Contains(candidate, StringComparer.Ordinal))
            throw new InvalidOperationException("This case ballot is stale or ineligible.");
        return Replace(state, item with
        {
            Contest = contest with
            {
                Ballots = contest.Ballots.Where(b => b.AgentId != actor)
            .Append(new(actor, candidate)).OrderBy(b => b.AgentId, StringComparer.Ordinal).ToArray()
            }
        });
    }

    public static (TownNonviolentState State, TownGovernanceState Council) Advance(TownNonviolentState state,
        TownGovernanceState council, TownGovernmentState government, IReadOnlyList<string> adultResidents,
        IReadOnlyDictionary<string, string?> households, long tick, int day, bool ordinaryContestBusy = false,
        IReadOnlyDictionary<string, IReadOnlyList<TownCaseParty>>? currentPartiesByCase = null)
    {
        var adults = TownHearingProcedure.Ordered(adultResidents);
        var office = TownGovernmentRules.CurrentNonLandAuthority(government, tick);
        var schedulerBusy = ordinaryContestBusy || council.Election is { Stage: "main" or "runoff" } || government.Contest is { Stage: "voting" };
        var heldCaseId = state.Cases.FirstOrDefault(c => c.Contest is { Stage: "voting" })?.Id;
        foreach (var saved in state.Cases.OrderBy(c => c.FiledTick).ThenBy(c => c.Id, StringComparer.Ordinal).ToArray())
        {
            var item = saved;
            var parties = currentPartiesByCase?.GetValueOrDefault(item.Id) ?? TownNonviolentRules.CurrentRevision(item).Parties;
            if (item.Judge is { } judge && (!Eligible(item, judge.AgentId, adults, households, parties) || government.Arrangement.NonLand != TownArrangementRules.Mayor ||
                    judge.Kind == "case_elected" && !Willing(item, judge.AgentId) ||
                    judge.Kind == "non_land_mayor" && (office?.HolderId != judge.AgentId || office.AuthorityId != judge.AuthorityId)))
            {
                state = TownNonviolentRules.InvalidateJudge(state, item.Id, tick, "authority_or_eligibility_ended");
                item = state.Cases.Single(c => c.Id == item.Id);
            }
            if (item.Judge is not null || item.Status != "pending" && !item.ReopenRequests.Any(r => r.Status == "pending")) continue;
            if (government.Arrangement.NonLand != TownArrangementRules.Mayor)
            {
                state = Replace(state, CancelContest(item, tick, "non_land_mandate_ended"));
                continue;
            }
            if (office is not null && Eligible(item, office.HolderId!, adults, households, parties))
            {
                state = TownNonviolentRules.AssignJudge(state, item.Id, new(office.HolderId!, "non_land_mayor", office.AuthorityId!, tick));
                continue;
            }
            if (office is null && item.Contest is null && item.ContestHistory.Count == 0) continue;
            var candidates = TownHearingProcedure.Ordered(item.JudgeConsents.Where(c => c.WithdrawnTick is null && Eligible(item, c.AgentId, adults, households, parties)).Select(c => c.AgentId));
            var contest = item.Contest;
            var busyForCase = schedulerBusy || heldCaseId is not null && heldCaseId != item.Id;
            if (contest is null)
            {
                var last = item.ContestHistory.Count == 0 ? null : item.ContestHistory[^1];
                if (last is { Stage: "failed", SettledTick: { } failed } && tick < failed + day && last.Candidates.SequenceEqual(candidates)) continue;
                contest = new(item.Id + ":judge:" + (item.ContestHistory.Count + 1).ToString(CultureInfo.InvariantCulture), "waiting", 0,
                    tick, null, null, [], [], [], [], 0, []);
            }
            var previousRound = contest.Round;
            contest = TownHearingProcedure.AdvanceContest(contest, adults, candidates, busyForCase, tick, day);
            if (contest.Stage == "voting" && contest.Round != previousRound)
            {
                council = TownGovernanceRules.PostNotice(council, "nonviolent_hearing", RoundToken(contest),
                    "Choose one willing independent adult to decide this non-land case only: " + string.Join(", ", contest.Candidates) + ".", tick);
                schedulerBusy = true;
            }
            item = item with { Contest = contest };
            if (contest.Stage is "completed" or "failed")
            {
                item = item with { Contest = null, ContestHistory = item.ContestHistory.Append(contest).ToArray() };
                if (contest.Stage == "completed") item = item with { Judge = new(contest.WinnerId!, "case_elected", contest.Id, tick) };
            }
            if (contest.Stage == "voting") schedulerBusy = true;
            else if (heldCaseId == item.Id) heldCaseId = null;
            state = Replace(state, item);
        }
        return (state, council);
    }

    private static TownCaseJudgeContest SaveRound(TownCaseJudgeContest contest, long tick, string result) =>
        contest.RoundOpenedTick is not { } opened || contest.Rounds.Any(r => r.Number == contest.Round) ? contest :
            contest with
            {
                Rounds = contest.Rounds.Append(new(contest.Round, opened, tick, result,
                contest.Voters, contest.Candidates, contest.Ballots, contest.TiedCandidates)).ToArray()
            };

    internal static TownViolationCase CancelContest(TownViolationCase item, long tick, string reason)
    {
        if (item.Contest is not { } contest) return item;
        if (contest.Stage == "voting") contest = SaveRound(contest, tick, "cancelled");
        contest = contest with { Stage = "cancelled", SettledTick = tick, Reason = reason };
        return item with { Contest = null, ContestHistory = item.ContestHistory.Append(contest).ToArray() };
    }
}

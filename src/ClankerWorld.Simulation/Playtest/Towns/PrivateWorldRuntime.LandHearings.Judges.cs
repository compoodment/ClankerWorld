namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private Dictionary<string, string?> LandHearingHouseholds() => society.Checkpoint.Inhabitants
        .ToDictionary(person => person.Id, person => person.HouseholdId, StringComparer.Ordinal);

    private (TownGovernanceState Council, TownLandHearingState LandHearings) AdvanceLandHearingJudges(
        TownRuntimeState town, TownGovernanceState council, TownGovernmentState government, TownLandHearingState hearings)
    {
        // Only a case that can still need a judge is recomputed. A closed case keeps its saved record, and the
        // ledger keeps its identity when nothing changed, so an idle Town is not rewritten every tick.
        var open = hearings.Cases.Where(item => item.Status == "pending" || item.ReopenRequests.Any(request => request.Status == "pending")).ToArray();
        if (open.Length == 0) return (council, hearings);
        var stakes = open.ToDictionary(item => item.Id, item => LandHearingDirectStakes(item).Order(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
        if (open.Any(item => !item.DirectStakeIds.SequenceEqual(stakes[item.Id], StringComparer.Ordinal)))
            hearings = hearings with
            {
                Cases = hearings.Cases.Select(item => stakes.TryGetValue(item.Id, out var ids) ? item with { DirectStakeIds = ids } : item).ToArray()
            };
        var parties = open.ToDictionary(item => item.Id,
            item => (IReadOnlyList<TownLandCaseParty>)LandHearingParties(town, TownLandHearingRules.CurrentRevision(item).Tiles, item),
            StringComparer.Ordinal);
        (hearings, council) = TownLandCaseJudgeRules.Advance(hearings, council, government, TownAdults(town),
            LandHearingHouseholds(), WorldTick, CivicDay,
            ordinaryContestBusy: TownNonviolentElectionBusy(town) || government.Contest is { Stage: "voting" or "ready" } || council.Election is { Stage: "ready" },
            currentPartiesByCase: parties);
        foreach (var old in open)
        {
            var item = hearings.Cases.Single(c => c.Id == old.Id);
            if (item.Judge is { } judge && judge != old.Judge)
                LandHearingEvent("judge_assigned", town, item, judge.AgentId, judge.Kind);
            if (item.Contest is { Stage: "voting" } contest &&
                (old.Contest is null || TownLandCaseJudgeRules.RoundToken(old.Contest) != TownLandCaseJudgeRules.RoundToken(contest)))
                LandHearingEvent("judge_election", town, item, null, "voting");
        }
        return (council, hearings);
    }
}

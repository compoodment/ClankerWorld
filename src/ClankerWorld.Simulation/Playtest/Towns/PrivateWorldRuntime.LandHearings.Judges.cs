namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private Dictionary<string, string?> LandHearingHouseholds() => society.Checkpoint.Inhabitants
        .ToDictionary(person => person.Id, person => person.HouseholdId, StringComparer.Ordinal);

    private (TownGovernanceState Council, TownLandHearingState LandHearings) AdvanceLandHearingJudges(
        TownRuntimeState town, TownGovernanceState council, TownGovernmentState government, TownLandHearingState hearings)
    {
        hearings = hearings with
        {
            Cases = hearings.Cases.Select(item => item with
            {
                DirectStakeIds = LandHearingDirectStakes(item).Order(StringComparer.Ordinal).ToArray(),
            }).ToArray()
        };
        var parties = hearings.Cases.ToDictionary(item => item.Id,
            item => (IReadOnlyList<TownLandCaseParty>)LandHearingParties(town, TownLandHearingRules.CurrentRevision(item).Tiles, item),
            StringComparer.Ordinal);
        var before = hearings;
        (hearings, council) = TownLandCaseJudgeRules.Advance(hearings, council, government, TownAdults(town),
            LandHearingHouseholds(), WorldTick, CivicDay,
            ordinaryContestBusy: TownNonviolentElectionBusy(town) || government.Contest is { Stage: "voting" or "ready" } || council.Election is { Stage: "ready" },
            currentPartiesByCase: parties);
        foreach (var item in hearings.Cases)
        {
            var old = before.Cases.Single(c => c.Id == item.Id);
            if (item.Judge is { } judge && judge != old.Judge)
                LandHearingEvent("judge_assigned", town, item, judge.AgentId, judge.Kind);
            if (item.Contest is { Stage: "voting" } contest &&
                (old.Contest is null || TownLandCaseJudgeRules.RoundToken(old.Contest) != TownLandCaseJudgeRules.RoundToken(contest)))
                LandHearingEvent("judge_election", town, item, null, "voting");
        }
        return (council, hearings);
    }
}

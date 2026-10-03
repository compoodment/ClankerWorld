using System.Text.Json;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Tests;

public sealed class TownLandCasePartySuccessionTests
{
    private static readonly string[] Adults = ["a", "b", "judge", "other"];
    private static readonly GridPoint[] Plot = [new(1, 0), new(2, 0)];
    private static readonly HouseholdLandUseRight[] Rights = [new("right", "town", "alpha", Plot, 0, "original-grant", 100)];
    private static readonly HouseholdLandUseRequest[] Requests = [new("beta-request", "town", "beta", "b", Plot, 0, 100)];
    private static readonly SocietyInhabitant[] People =
    [Person("a", "alpha"), Person("b", "beta"), Person("judge", "neutral"), Person("other", "other-household")];
    private static readonly IReadOnlyDictionary<string, string?> Households = People.ToDictionary(person => person.Id, person => person.HouseholdId);

    [Fact]
    public void AnElectedCaseJudgeWhoBecomesTheTownsRepresentativeIsReplacedWithoutRestartingTheNotice()
    {
        var government = TownGovernmentState.Create() with
        {
            Arrangement = new("mayor", "mayor"),
            Offices = [new("ordinary", "a", 0, 100, null, null, "ordinary:first"), new("land", "a", 0, 100, null, null, "land:first")],
        };
        var council = TownGovernanceState.Create(Adults);
        var town = new TownRuntimeState("town", "Town", "founded", 0, Adults, [], Plot, Governance: council, Government: government);
        var parties = TownLandCasePartyRules.CurrentParties(town, Plot, Rights, Requests, People, 0, townRepresentative: "a");
        var state = TownLandHearingRules.File(TownLandHearingState.Create(), town.Id,
            new("a", "town", "Review the existing permission", new("confirm"), 0, "ordinary:first"), Plot, Rights, parties, 0, 10, "notice:1");
        var id = state.Cases[0].Id;
        council = TownGovernanceRules.PostNotice(council, "land_hearing", id + ":1", "The formal hearing notice", 0);
        council = TownGovernanceRules.LearnNotice(council, "a", "notice:1", 1);
        state = TownLandHearingRules.Respond(state, id, 1, "a", "waive", "Only my household response is waived", 1,
            council.Knowledge, parties, "household:alpha");
        state = TownLandCaseJudgeRules.Register(state, id, "judge", Adults, Households, 1, parties);
        state = TownLandCaseJudgeRules.Register(state, id, "other", Adults, Households, 1, parties);
        (state, council) = TownLandCaseJudgeRules.Advance(state, council, government, Adults, Households, 1, 10,
            currentPartiesByCase: ForCase(id, parties));
        state = TownLandCaseJudgeRules.Vote(state, id, TownLandCaseJudgeRules.RoundToken(state.Cases[0].Contest!), "a", "judge", 2);
        (state, council) = TownLandCaseJudgeRules.Advance(state, council, government, Adults, Households, 11, 10,
            currentPartiesByCase: ForCase(id, parties));
        Assert.Equal("judge", state.Cases[0].Judge!.AgentId);
        var originalNotice = JsonSerializer.Serialize(state.Cases[0].Revisions);
        var originalResponses = JsonSerializer.Serialize(state.Cases[0].Responses);

        var successor = government with
        {
            Offices = [new("ordinary", "judge", 12, 112, null, null, "ordinary:successor"), government.Offices[1]],
            OfficeHistory = [new("a", "ordinary", 0, 12, "term_ended", "ordinary:first")],
        };
        town = town with { Government = successor, Governance = council, LandHearings = state };
        var current = TownLandCasePartyRules.CurrentParties(town, Plot, Rights, Requests, People, 12, state.Cases[0]);
        Assert.Equal("judge", Assert.Single(current, party => party.Kind == "town").RepresentativeId);
        Assert.False(TownLandCasePartyRules.TownFilingAuthority(town, "a", "ordinary:first", People, 12));
        Assert.True(TownLandCasePartyRules.TownFilingAuthority(town, "judge", "ordinary:successor", People, 12));
        Assert.Throws<InvalidOperationException>(() => TownLandCaseJudgeRules.Register(state, id, "judge", Adults, Households, 12, current));
        Assert.Throws<InvalidDataException>(() => Validate(state, council, successor, current, 12));

        (state, council) = TownLandCaseJudgeRules.Advance(state, council, successor, Adults, Households, 12, 10,
            currentPartiesByCase: ForCase(id, current));
        Assert.Null(state.Cases[0].Judge);
        Assert.Equal("judge", Assert.Single(state.Cases[0].JudgeHistory).Judge.AgentId);
        Assert.Equal("other", Assert.Single(state.Cases[0].Contest!.Candidates));
        Assert.Equal(22, state.Cases[0].Contest!.RoundDeadlineTick);
        state = TownLandCaseJudgeRules.Vote(state, id, TownLandCaseJudgeRules.RoundToken(state.Cases[0].Contest!), "a", "other", 13);
        (state, council) = TownLandCaseJudgeRules.Advance(state, council, successor, Adults, Households, 22, 10,
            currentPartiesByCase: ForCase(id, current));
        Assert.Equal("other", state.Cases[0].Judge!.AgentId);
        Assert.Equal(originalNotice, JsonSerializer.Serialize(state.Cases[0].Revisions));
        Assert.Equal(originalResponses, JsonSerializer.Serialize(state.Cases[0].Responses));
        Assert.Empty(state.Adjustments);
        Assert.Equal(10, state.Cases[0].Revisions[0].DeadlineTick);
        Validate(state, council, successor, current, 22);
        var restored = JsonSerializer.Deserialize<TownLandHearingState>(JsonSerializer.Serialize(state))!;
        Validate(restored, council, successor, current, 22);
    }

    [Fact]
    public void CurrentHouseholdAdultsReflectDeathsAndMembershipWhileThePublishedRosterStaysImmutable()
    {
        var town = new TownRuntimeState("town", "Town", "founded", 0, Adults, [], Plot);
        var parties = TownLandCasePartyRules.CurrentParties(town, Plot, Rights, [], People, 0, filingHousehold: "beta");
        var state = TownLandHearingRules.File(TownLandHearingState.Create(), town.Id,
            new("b", "dispute", "Consider this permission", new("confirm"), 0), Plot, Rights, parties, 0, 10, "notice:1");
        var notice = JsonSerializer.Serialize(state.Cases[0].Revisions[0]);
        var changed = People.Select(person => person.Id switch
        {
            "a" => person with { Status = SocietyInhabitantStatus.Dead, DeathTick = 1 },
            "b" => person with { HouseholdId = "neutral" },
            _ => person,
        }).Append(Person("new-adult", "beta")).ToArray();
        var current = TownLandCasePartyRules.CurrentParties(town, Plot, Rights, [], changed, 2, state.Cases[0]);
        Assert.Empty(Assert.Single(current, party => party.HouseholdId == "alpha").AdultIds);
        Assert.Equal("new-adult", Assert.Single(Assert.Single(current, party => party.HouseholdId == "beta").AdultIds));
        Assert.True(TownLandHearingRules.RequiresNewNotice(state.Cases[0].Revisions[0], Plot, Rights, current));
        var withoutNewAdult = TownLandCasePartyRules.CurrentParties(town, Plot, Rights, [], changed.Where(person => person.Id != "new-adult").ToArray(), 2, state.Cases[0]);
        Assert.False(TownLandHearingRules.RequiresNewNotice(state.Cases[0].Revisions[0], Plot, Rights, withoutNewAdult));
        Assert.Equal(notice, JsonSerializer.Serialize(state.Cases[0].Revisions[0]));
    }

    private static SocietyInhabitant Person(string id, string household) =>
        new(id, id, 0, SocietyInhabitantStatus.Active, SocietyAgeBand.Adult, 10_000, household, null, SocietyWorkRole.Unassigned, 0);
    private static IReadOnlyDictionary<string, IReadOnlyList<TownLandCaseParty>> ForCase(string id, IReadOnlyList<TownLandCaseParty> parties) =>
        new Dictionary<string, IReadOnlyList<TownLandCaseParty>>(StringComparer.Ordinal) { [id] = parties };
    private static void Validate(TownLandHearingState state, TownGovernanceState council, TownGovernmentState government,
        IReadOnlyList<TownLandCaseParty> parties, long tick)
    {
        var map = new SeededMap(4, 1, 0, Enumerable.Range(0, 4).Select(x => new TerrainTile(new(x, 0), TerrainKind.Meadow)).ToArray(), [], [], "party-succession");
        TownLandTitleRecord[] titles = [new("title", "town", Plot, 0)];
        TownLandHearingValidation.Validate(map, tick, "town", state, Rights, titles, Adults.ToHashSet(StringComparer.Ordinal),
            Households.Values.OfType<string>().ToHashSet(StringComparer.Ordinal), council, 10, government, Adults, Households,
            ForCase(state.Cases[0].Id, parties));
    }
}

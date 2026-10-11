using System.Text;
using System.Text.Json;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class LandHearingEvidenceUnicodeTests
{
    private static readonly Lazy<Task<byte[]>> Baseline = new(CreateBaseline);
    private const string Judge = NonviolentRuntimeFixture.Judge;
    private const string Source = NonviolentRuntimeFixture.Subject;

    [Theory]
    [InlineData(158)]
    [InlineData(159)]
    [InlineData(160)]
    public async Task NativeRulingChoicesKeepWholeCharactersAndInspectedSourceHistory(int prefix)
    {
        var state = PrivateWorldRuntimeCodec.Decode(await Baseline.Value);
        var text = new string('a', prefix) + "😀tail";
        var statement = new NonviolentTestProvider
        {
            Choose = observation => observation.InhabitantId == Source
                ? observation.Candidates.FirstOrDefault(candidate => candidate.Id.Contains("|hearing_statement|", StringComparison.Ordinal)) : null,
            LandPayload = (_, candidate) => candidate.Id.Contains("|hearing_statement|", StringComparison.Ordinal) ? new(Statement: text) : null,
        };
        using var world = NonviolentRuntimeFixture.Create(state, statement);
        NonviolentRuntimeFixture.Wake(world, Source, "unicode-source-statement");
        await NonviolentRuntimeFixture.UntilAsync(world, () => world.Towns[0].LandHearings.Cases[0].Evidence.Any(evidence => evidence.Kind == "allegation"), 6);
        var evidence = Assert.Single(world.Towns[0].LandHearings.Cases[0].Evidence, item => item.Kind == "allegation");
        Assert.Equal(text, evidence.Text);
        Assert.Equal(Source, evidence.SourceAgentId);
        Assert.Equal(Source, evidence.SubmittedByAgentId);
        Assert.Equal("statement", evidence.Acquisition);
        var inspect = new NonviolentTestProvider
        {
            Choose = observation => observation.InhabitantId == Judge
                ? observation.Candidates.FirstOrDefault(candidate => candidate.Id.Contains("|hearing_inspect|", StringComparison.Ordinal)) : null,
        };
        using var inspected = NonviolentRuntimeFixture.Create(NonviolentRuntimeFixture.Strict(world.ExportState()), inspect);
        NonviolentRuntimeFixture.Wake(inspected, Judge, "unicode-judge-inspection");
        await NonviolentRuntimeFixture.UntilAsync(inspected, () => inspected.Towns[0].LandHearings.Cases[0].Reads.Any(read =>
            read.AgentId == Judge && read.EvidenceIds.Contains(evidence.Id)), 6);
        var idle = new NonviolentTestProvider();
        using var captured = NonviolentRuntimeFixture.Create(NonviolentRuntimeFixture.Strict(inspected.ExportState()), idle);
        NonviolentRuntimeFixture.Wake(captured, Judge, "unicode-ruling-context");
        await NonviolentRuntimeFixture.UntilAsync(captured, () => idle.Observations.Any(observation => observation.InhabitantId == Judge &&
            observation.Candidates.Any(candidate => candidate.Id.Contains("|hearing_rule|", StringComparison.Ordinal))), 6);
        var candidate = idle.Observations.Where(observation => observation.InhabitantId == Judge)
            .SelectMany(observation => observation.Candidates).First(candidate => candidate.Id.Contains("|hearing_rule|", StringComparison.Ordinal));
        var marker = evidence.Id + "=allegation from " + Source + ": ";
        var start = candidate.Description.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, candidate.Description);
        start += marker.Length;
        var end = candidate.Description.IndexOf("; inspected law versions:", start, StringComparison.Ordinal);
        Assert.True(end > start, candidate.Description);
        var excerpt = candidate.Description[start..end];
        var expected = prefix == 158 ? new string('a', 158) + "😀" : new string('a', Math.Min(prefix, 160));
        Assert.Equal(expected, excerpt);
        var final = captured.ExportState();
        Assert.Equal(JsonSerializer.Serialize(state.TownLandTitles), JsonSerializer.Serialize(final.TownLandTitles));
        Assert.Equal(JsonSerializer.Serialize(state.HouseholdLandUseRights), JsonSerializer.Serialize(final.HouseholdLandUseRights));
        Assert.Equal(state.Society.Society.Inventory.Lots.Select(lot =>
                (lot.Id, lot.OwnerId, lot.Quantity, lot.StorageBuildingId, lot.CarrierId)),
            final.Society.Society.Inventory.Lots.Select(lot =>
                (lot.Id, lot.OwnerId, lot.Quantity, lot.StorageBuildingId, lot.CarrierId)));
        Assert.Equal("pending", captured.Towns[0].LandHearings.Cases[0].Status);
        Assert.Empty(captured.Towns[0].LandHearings.Cases[0].Rulings);
        Assert.Equal(evidence, captured.Towns[0].LandHearings.Cases[0].Evidence.Single(item => item.Id == evidence.Id));
        captured.Validate();
        var bytes = PrivateWorldRuntimeCodec.Encode(captured.ExportState());
        using var reloaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(reloaded.ExportState()));
        var wire = JsonSerializer.Deserialize<string>(JsonSerializer.Serialize(excerpt))!;

        Assert.Equal(excerpt, wire);
        _ = new UTF8Encoding(false, true).GetBytes(excerpt);
    }

    private static async Task<byte[]> CreateBaseline()
    {
        var state = NonviolentRuntimeFixture.Prepared();
        var town = state.Towns![0];
        var tick = state.Society.Society.WorldTick;
        var (council, government) = TownGovernmentRules.RegisterMayor(town.Governance!, town.Government!, Judge, "land", town.ResidentIds, tick);
        (council, government) = TownGovernmentRules.Propose(council, government, town.Id, Judge,
            new(TownArrangementRules.Council, TownArrangementRules.Mayor), false, town.ResidentIds, tick, NonviolentRuntimeFixture.Day);
        foreach (var voter in town.ResidentIds.Take(3))
            government = TownGovernmentRules.Vote(government, government.Changes[^1].Id, voter, true, tick);
        (council, government) = TownGovernmentRules.Advance(council, government, town.Id, town.Name,
            state.WorldSeed, town.ResidentIds, tick, NonviolentRuntimeFixture.Day);
        var contest = Assert.IsType<TownMayoralContest>(government.Contest);
        foreach (var voter in town.ResidentIds)
            government = TownGovernmentRules.VoteMayor(government, TownGovernmentRules.RoundToken(contest), voter, Judge, tick);
        state = state with { Towns = [town with { Governance = council, Government = government }] };
        using (var clock = NonviolentRuntimeFixture.Create(state, new NonviolentTestProvider()))
        {
            await NonviolentRuntimeFixture.UntilAsync(clock, () => clock.Towns[0].Government!.Offices.Any(office => office.HolderId == Judge && office.Mandates == "land"), NonviolentRuntimeFixture.Day + 3);
            state = clock.ExportState();
        }
        var household = state.Society.Society.GetInhabitant(Source).HouseholdId;
        var permission = state.HouseholdLandUseRights!.First(right => right.HouseholdId == household);
        state = state with
        {
            HouseholdLandUseRights = state.HouseholdLandUseRights!.Select(right => right.Id == permission.Id ? right with { AgreedEndTick = state.Society.Society.WorldTick + 1 } : right).ToArray(),
            Inhabitants = state.Inhabitants.Select(person => person with
            {
                Position = state.Towns![0].OriginSite!.Value,
                HungerBasisPoints = 10_000,
                Survival = person.Survival is { } survival ? survival with
                { WarmthBasisPoints = 10_000, NutritionBasisPoints = 10_000, IllnessBasisPoints = 0 } : null,
                LastDecisionContext = null,
                Project = null,
                TravelCooldownTicks = 0,
            }).ToArray(),
        };
        using (var expiry = NonviolentRuntimeFixture.Create(state, new NonviolentTestProvider()))
        {
            Assert.True((await expiry.AdvanceOneTickAsync()).Advanced);
            Assert.Single(expiry.Towns[0].LandHearings.Cases);
            state = expiry.ExportState();
        }
        town = state.Towns![0];
        var item = Assert.Single(town.LandHearings.Cases);
        var revision = TownLandHearingRules.CurrentRevision(item);
        council = TownGovernanceRules.LearnNotices(town.Governance!, Judge, [revision.NoticeId], state.Society.Society.WorldTick);
        var hearings = town.LandHearings;
        foreach (var party in revision.Parties)
            foreach (var adult in party.AdultIds)
            {
                council = TownGovernanceRules.LearnNotices(council, adult, [revision.NoticeId], state.Society.Society.WorldTick);
                hearings = TownLandHearingRules.Respond(hearings, item.Id, revision.Number, adult, "waive", "I waive only my own response opportunity.",
                    state.Society.Society.WorldTick, council.Knowledge, revision.Parties);
            }
        var inspect = new NonviolentTestProvider
        {
            Choose = observation => observation.InhabitantId == Judge
                ? observation.Candidates.FirstOrDefault(candidate => candidate.Id.Contains("|hearing_inspect|", StringComparison.Ordinal)) : null,
        };
        using var prepared = NonviolentRuntimeFixture.Create(state with { Towns = [town with { Governance = council, LandHearings = hearings }] }, inspect);
        NonviolentRuntimeFixture.Wake(prepared, Judge, "inspect-original-records");
        await NonviolentRuntimeFixture.UntilAsync(prepared, () => prepared.Towns[0].LandHearings.Cases[0].Reads.Any(read => read.AgentId == Judge), 6);
        Assert.Equal("pending", prepared.Towns[0].LandHearings.Cases[0].Status);
        return PrivateWorldRuntimeCodec.Encode(NonviolentRuntimeFixture.Strict(prepared.ExportState()));
    }
}

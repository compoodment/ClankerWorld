using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class TownNonviolentRuntimeTests
{
    [Fact]
    public async Task ActualTravelProducesOnlyNearbyKnowledgeAndAPersonalReportRemainsAnAllegation()
    {
        var state = await NonviolentRuntimeFixture.ConductAsync();
        var town = Assert.Single(state.Towns!);
        var conduct = Assert.Single(town.Nonviolent.ConductRecords);
        Assert.Equal(town.Id, conduct.ActorTownId);
        Assert.Equal(1, Assert.Single(conduct.Laws).Version);
        Assert.NotEmpty(conduct.Laws[0].PriorNoticeIds);
        Assert.Contains(town.Nonviolent.Acquisitions, acquisition => acquisition.AgentId == NonviolentRuntimeFixture.Witness && acquisition.Kind == "firsthand");
        Assert.DoesNotContain(town.Nonviolent.Acquisitions, acquisition => acquisition.AgentId == NonviolentRuntimeFixture.Stranger);
        Assert.Empty(town.Nonviolent.Cases);
        var physical = Property(state);
        var provider = NonviolentRuntimeFixture.FilingProvider();
        using var world = NonviolentRuntimeFixture.Create(state, provider);
        await NonviolentRuntimeFixture.UntilAsync(world, () => world.Towns[0].Nonviolent.Cases.Count > 0, 8);

        var item = Assert.Single(world.Towns[0].Nonviolent.Cases);
        Assert.Equal(conduct.Id, item.Allegation.IncidentId);
        Assert.Equal(conduct.Tick, item.Allegation.ConductTick);
        Assert.Equal("pending", item.Status);
        Assert.Equal("witness", Assert.Single(item.Filings).Kind);
        Assert.Equal(NonviolentRuntimeFixture.Witness, item.Filings[0].AgentId);
        var evidence = Assert.Single(item.Evidence);
        Assert.Equal("firsthand", evidence.Acquisition);
        Assert.Equal(conduct.Version, evidence.SourceVersion);
        Assert.Empty(item.Findings);
        Assert.Empty(item.Responses);
        Assert.Empty(world.Towns[0].Nonviolent.Offers);
        Assert.Equal(physical, Property(world.ExportState()));
        var notice = Assert.Single(item.Revisions).NoticeId;
        Assert.Contains(world.Towns[0].Governance!.Knowledge, receipt => receipt.NoticeId == notice && receipt.AgentId == NonviolentRuntimeFixture.Witness);
        Assert.DoesNotContain(world.Towns[0].Governance!.Knowledge, receipt => receipt.NoticeId == notice && receipt.AgentId == NonviolentRuntimeFixture.Subject);
        Assert.DoesNotContain(provider.Observations.Where(observation => observation.InhabitantId == NonviolentRuntimeFixture.Stranger)
            .SelectMany(observation => observation.Candidates), candidate => candidate.Id.Contains("|law_case_file|", StringComparison.Ordinal));
        NonviolentRuntimeFixture.Strict(world.ExportState());
    }

    [Theory]
    [InlineData(DecisionProviderKind.Jev, true)]
    [InlineData(DecisionProviderKind.LargeLanguageModel, false)]
    public async Task AVisibleCandidateDoesNotTurnAnotherProviderOrMissingPersonalStatementIntoAFiling(DecisionProviderKind kind, bool payload)
    {
        var provider = NonviolentRuntimeFixture.FilingProvider(kind, payload);
        using var world = NonviolentRuntimeFixture.Create(await NonviolentRuntimeFixture.ConductAsync(), provider);
        await NonviolentRuntimeFixture.UntilAsync(world,
            () => provider.Selected.Any(choice => choice.Contains("|law_case_file|", StringComparison.Ordinal)), 6);
        Assert.Empty(world.Towns[0].Nonviolent.Cases);
        Assert.Empty(world.Towns[0].Nonviolent.Effects);
        NonviolentRuntimeFixture.Strict(world.ExportState());
    }

    [Fact]
    public async Task AmendingTheLawAfterTheActCannotRetargetTheKnownHistoricalReport()
    {
        using var waiting = NonviolentRuntimeFixture.Create(await NonviolentRuntimeFixture.ConductAsync(), new NonviolentTestProvider());
        Assert.True((await waiting.AdvanceOneTickAsync()).Advanced);
        var state = waiting.ExportState();
        var town = state.Towns![0];
        var law = Assert.Single(town.Government!.Laws);
        var original = Assert.Single(town.Nonviolent.ConductRecords);
        Assert.True(state.Society.Society.WorldTick > original.Tick);
        var (council, government) = TownLawRules.ProposeAmendment(town.Governance!, town.Government,
            town.Id, NonviolentRuntimeFixture.Judge, law.Id, "Paths: The later wording allows free passage.",
            town.ResidentIds, state.Society.Society.WorldTick, NonviolentRuntimeFixture.Day);
        foreach (var voter in town.ResidentIds.Take(3))
            council = TownGovernanceRules.VoteProposal(council, council.Proposals[^1].Id, voter, true, state.Society.Society.WorldTick);
        (council, government) = TownLawRules.Enact(council, government, town.Id, town.Name, state.Society.Society.WorldTick);
        Assert.Equal(2, government.Laws[0].Versions.Count);
        state = NonviolentRuntimeFixture.Strict(state with { Towns = [town with { Governance = council, Government = government }] });
        using var world = NonviolentRuntimeFixture.Create(state, NonviolentRuntimeFixture.FilingProvider());
        await NonviolentRuntimeFixture.UntilAsync(world, () => world.Towns[0].Nonviolent.Cases.Count > 0, 8);

        var item = Assert.Single(world.Towns[0].Nonviolent.Cases);
        Assert.Equal(1, item.Allegation.LawVersion);
        Assert.Equal(original.Tick, item.Allegation.ConductTick);
        Assert.Equal(original.Version, Assert.Single(item.Evidence).SourceVersion);
        Assert.Equal(JsonSerializer.Serialize(original), JsonSerializer.Serialize(Assert.Single(world.Towns[0].Nonviolent.ConductRecords)));
        NonviolentRuntimeFixture.Strict(world.ExportState());
    }

    [Fact]
    public async Task AVisitorMustActuallyReceiveTheNearbyReportBeforeFilingAndKeepsNoMembershipOrPrivateRights()
    {
        var state = await NonviolentRuntimeFixture.ConductAsync();
        var town = state.Towns![0];
        var residents = town.ResidentIds.Where(id => id != NonviolentRuntimeFixture.Stranger).ToArray();
        var council = TownGovernanceRules.Advance(town.Governance!, town.Id, state.WorldSeed, residents,
            state.Society.Society.WorldTick, NonviolentRuntimeFixture.Day);
        // The adult visitor already has a household, but no membership in this Town.
        // Arrival at its public board does not grant sight of an earlier act.
        state = NonviolentRuntimeFixture.Strict(state with
        {
            Towns = [town with { ResidentIds = residents, Governance = council }],
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == NonviolentRuntimeFixture.Stranger
                ? person with { Position = town.OriginSite!.Value, LastDecisionContext = null } : person).ToArray()
        });
        var property = Property(state);
        var provider = new NonviolentTestProvider
        {
            Choose = observation => observation.InhabitantId == NonviolentRuntimeFixture.Stranger
                ? observation.Candidates.FirstOrDefault(candidate => candidate.Id.Contains("|read|", StringComparison.Ordinal)) ??
                    observation.Candidates.FirstOrDefault(candidate => candidate.Id.Contains("|law_case_file|", StringComparison.Ordinal))
                : observation.InhabitantId == NonviolentRuntimeFixture.Witness
                    ? observation.Candidates.FirstOrDefault(candidate => candidate.Id.StartsWith("nonviolent_relay:", StringComparison.Ordinal) &&
                        candidate.DestinationId == NonviolentRuntimeFixture.Stranger) : null,
            Payload = (_, choice) => choice.Id.Contains("|law_case_file|", StringComparison.Ordinal)
                ? new(Statement: "The nearby witness told me about the earlier passage; I did not see it myself.") : null
        };
        using var world = NonviolentRuntimeFixture.Create(state, provider);
        await NonviolentRuntimeFixture.UntilAsync(world, () => world.Towns[0].Nonviolent.Cases.Count > 0, 10);
        var views = provider.Observations.Where(observation => observation.InhabitantId == NonviolentRuntimeFixture.Stranger).ToArray();
        Assert.DoesNotContain(views[0].Candidates, candidate => candidate.Id.Contains("|law_case_file|", StringComparison.Ordinal));
        Assert.Contains(views.Skip(1).SelectMany(observation => observation.Candidates), candidate => candidate.Id.Contains("|law_case_file|", StringComparison.Ordinal));
        var acquired = Assert.Single(world.Towns[0].Nonviolent.Acquisitions, acquisition => acquisition.AgentId == NonviolentRuntimeFixture.Stranger);
        Assert.Equal("relay", acquired.Kind);
        Assert.Equal(NonviolentRuntimeFixture.Witness, acquired.SourceAgentId);
        Assert.NotNull(acquired.ParentId);
        var item = Assert.Single(world.Towns[0].Nonviolent.Cases);
        Assert.Equal(NonviolentRuntimeFixture.Stranger, Assert.Single(item.Filings).AgentId);
        Assert.Equal("relay", Assert.Single(item.Evidence).Acquisition);
        Assert.Empty(item.Findings);
        Assert.DoesNotContain(NonviolentRuntimeFixture.Stranger, world.Towns[0].ResidentIds);
        Assert.Equal(property, Property(world.ExportState()));
        NonviolentRuntimeFixture.Strict(world.ExportState());
    }

    [Fact]
    public async Task RefusingThePreparedReportPreservesTheWholeWorldAndAcceptedReplayFilesOnlyOnce()
    {
        var provider = NonviolentRuntimeFixture.FilingProvider();
        using var world = NonviolentRuntimeFixture.Create(await NonviolentRuntimeFixture.ConductAsync(), provider);
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False((await world.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Contains(provider.Selected, choice => choice.Contains("|law_case_file|", StringComparison.Ordinal));
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        using var replay = NonviolentRuntimeFixture.Create(PrivateWorldRuntimeCodec.Decode(before), NonviolentRuntimeFixture.FilingProvider());
        for (var tick = 0; tick < 3; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
        }
        Assert.Single(world.Towns[0].Nonviolent.Cases);
        Assert.Single(world.Towns[0].Nonviolent.Cases[0].Filings);
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        NonviolentRuntimeFixture.Strict(world.ExportState());
    }

    [Theory]
    [InlineData("missing-ledger")]
    [InlineData("null-conduct-entry")]
    [InlineData("invented-observer")]
    [InlineData("changed-native-fact")]
    [InlineData("forged-evidence-version")]
    public async Task MalformedOrInventedNativeCaseProvenanceIsRefusedAsInvalidData(string damage)
    {
        var state = await NonviolentRuntimeFixture.FiledAsync();
        var healthy = PrivateWorldRuntimeCodec.Encode(state);
        var document = JsonNode.Parse(healthy)!;
        var town = document["state"]!["towns"]![0]!;
        var ledger = town["nonviolent"]!;
        switch (damage)
        {
            case "missing-ledger": town.AsObject().Remove("nonviolent"); break;
            case "null-conduct-entry": ledger["conductRecords"]![0] = null; break;
            case "invented-observer": ledger["acquisitions"]![0]!["agentId"] = "invented-observer"; break;
            case "changed-native-fact": ledger["conductRecords"]![0]!["quantity"] = 500; break;
            case "forged-evidence-version": ledger["cases"]![0]!["evidence"]![0]!["sourceVersion"] = "invented"; break;
        }
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Decode(Encoding.UTF8.GetBytes(document.ToJsonString())));
        Assert.Equal(healthy, PrivateWorldRuntimeCodec.Encode(NonviolentRuntimeFixture.Strict(state)));
    }

    private static string Property(PrivateWorldRuntimeState state) => JsonSerializer.Serialize(new
    {
        state.TownLandTitles,
        state.HouseholdLandUseRights,
        Buildings = state.WorldSimulation!.Buildings,
        Lots = state.Society.Society.Inventory.Lots.Select(lot => new
        { lot.Id, lot.OwnerId, lot.ItemKind, lot.Quantity, lot.StorageBuildingId, lot.GroundPosition, lot.ContainerLotId, lot.DeliveryBuildingId }),
        Residents = state.Towns!.Select(town => new { town.Id, town.ResidentIds })
    });
}

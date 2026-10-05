using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class TownNonviolentRuntimeTests
{
    [Fact]
    public async Task LongDescendantConductReferencesCanBeCitedInAnActualFindingThroughShortAliases()
    {
        // Keep the fixture's canonical roster order while extending this opaque actor ID.
        var subject = NonviolentRuntimeFixture.Subject + ":child:" + new string('a', 250);
        var state = await NonviolentRuntimeFixture.ReadyToFindAsync(subject);
        Assert.True(Assert.Single(state.Towns![0].Nonviolent.ConductRecords).Id.Length > 256);
        var provider = new NonviolentTestProvider
        {
            Choose = observation => observation.InhabitantId == NonviolentRuntimeFixture.Judge
                ? observation.Candidates.FirstOrDefault(candidate => candidate.Id.Contains("|law_case_find|", StringComparison.Ordinal) &&
                    candidate.Id.EndsWith("|warning", StringComparison.Ordinal)) : null,
            Payload = (_, candidate) =>
            {
                if (!candidate.Id.Contains("|law_case_find|", StringComparison.Ordinal)) return null;
                var payload = NonviolentRuntimeFixture.FindingPayload(candidate);
                Assert.NotEmpty(payload.EvidenceIds!);
                Assert.All(payload.EvidenceIds!, reference => Assert.InRange(reference.Length, 1, 256));
                payload.Validate();
                return payload;
            },
        };
        using var world = NonviolentRuntimeFixture.Create(state, provider);
        await NonviolentRuntimeFixture.UntilAsync(world, () => world.Towns[0].Nonviolent.Cases[0].Findings.Count > 0, 8);
        var item = Assert.Single(world.Towns[0].Nonviolent.Cases);
        var finding = Assert.Single(item.Findings);
        Assert.Equal("supported", finding.Result);
        Assert.Equal("warning", finding.Consequence);
        Assert.Contains(finding.EvidenceIds, id => id.Length > 256 && item.Evidence.Any(evidence => evidence.Id == id));
        var found = NonviolentRuntimeFixture.Strict(world.ExportState());
        var household = found.Society.Society.GetInhabitant(subject).HouseholdId!;
        var lotId = "personal-stone:" + subject;
        var inventory = InventoryFixture.AddLot(found.Society.Society.Inventory, lotId, "stone", subject, 1);
        var toolId = "borrowed-axe:" + subject;
        inventory = InventoryFixture.AddLot(inventory, toolId, "wooden_axe", household, 1, conditionBasisPoints: 3_000);
        inventory = InventoryFixture.Relocate(inventory, "borrow-for-remedy", toolId, household, 1, carrierId: subject);
        var carriedToolId = inventory.Lots.Single(lot => lot.Id == toolId && lot.CarrierId == subject).Id;
        found = found with { Society = found.Society with { Society = found.Society.Society with { Inventory = inventory } } };
        var name = found.Society.Society.GetInhabitant(subject).Name;
        var offering = new NonviolentTestProvider
        {
            Choose = observation => observation.InhabitantId == subject
                ? observation.Candidates.FirstOrDefault(candidate => candidate.Id.Contains("|read|", StringComparison.Ordinal)) ??
                    observation.Candidates.FirstOrDefault(candidate => candidate.Id.Contains("|law_case_inspect|", StringComparison.Ordinal)) ??
                    observation.Candidates.FirstOrDefault(candidate => candidate.Id.Contains("|remedy_offer|", StringComparison.Ordinal)) : null,
            Payload = (_, candidate) =>
            {
                if (!candidate.Id.Contains("|remedy_offer|", StringComparison.Ordinal)) return null;
                var contributor = Regex.Match(candidate.Description, Regex.Escape(name) + @" \(([^)]+)\)").Groups[1].Value;
                var target = Regex.Match(candidate.Description, @"stone \(([^)]+)\) held by " + Regex.Escape(contributor)).Groups[1].Value;
                Assert.NotEmpty(contributor);
                Assert.NotEmpty(target);
                Assert.InRange(contributor.Length, 1, 256);
                Assert.InRange(target.Length, 1, 256);
                Assert.DoesNotContain(lotId, candidate.Description, StringComparison.Ordinal);
                var tool = Regex.Match(candidate.Description, @"wooden axe \(([^)]+)\) held by (\S+)");
                Assert.True(tool.Success);
                var toolReference = tool.Groups[1].Value;
                var householdReference = tool.Groups[2].Value.TrimEnd(';', '.');
                Assert.InRange(householdReference.Length, 1, 256);
                return new(Statement: "I offer this named personal stone voluntarily.",
                    Terms: [new("return_goods", contributor, NonviolentRuntimeFixture.Witness, "stone", 1, target),
                        new("repair_equipment", contributor, householdReference, "wooden_axe", 1, toolReference)]);
            },
        };
        using var remedy = NonviolentRuntimeFixture.Create(found, offering);
        NonviolentRuntimeFixture.Wake(remedy, subject, "offer-through-short-references");
        await NonviolentRuntimeFixture.UntilAsync(remedy, () => remedy.Towns[0].Nonviolent.Offers.Count > 0, 10);
        var terms = Assert.Single(remedy.Towns[0].Nonviolent.Offers).Terms;
        var term = Assert.Single(terms, term => term.Kind == "return_goods");
        Assert.Equal(subject, term.ContributorId);
        Assert.Equal(lotId, term.TargetId);
        var repair = Assert.Single(terms, term => term.Kind == "repair_equipment");
        Assert.Equal(household, repair.BeneficiaryId);
        Assert.Equal(carriedToolId, repair.TargetId);
        NonviolentRuntimeFixture.Strict(remedy.ExportState());
    }

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

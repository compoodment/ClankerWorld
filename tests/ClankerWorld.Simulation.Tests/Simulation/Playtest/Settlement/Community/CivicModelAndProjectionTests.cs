using System.Net;
using System.Text;
using System.Text.Json;
using ClankerWorld.GodotClient.UI;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class CivicModelContractTests
{
    [Fact]
    public async Task HostedStructuredRuleReachesAdmissionButCannotReplayFromSavedIntention()
    {
        using var client = new HttpClient(new RuleReply());
        var provider = new OpenAiCompatibleDecisionProvider(client, () => "synthetic-key",
            new Uri("https://model.test/v1/chat/completions"), "synthetic-model");
        var runtime = new CognitionRuntime("councillor", provider);
        var admission = await runtime.RequestAndDecideAsync(Observation());
        Assert.True(admission.Accepted);
        Assert.False(admission.FellBack);
        Assert.Equal("council_town_author", admission.Intention!.CandidateId);
        Assert.Equal(new CognitionTownLawProposal("protected_grove", "Ask before felling Town trees."), admission.TownLawProposal);
        var saved = runtime.ExportState();
        Assert.DoesNotContain("Ask before felling", JsonSerializer.Serialize(saved));
        var restored = CognitionRuntime.Restore(saved, provider);
        Assert.Equal("council_town_author", restored.Capture().CurrentIntention!.CandidateId);
        Assert.Empty(restored.Capture().Requests);
    }

    [Theory]
    [InlineData("other_action", "town_law_not_requested")]
    [InlineData("missing_payload", "town_law_not_requested")]
    [InlineData("deterministic_provider", "town_law_not_requested")]
    [InlineData("unoffered_action", "candidate_not_legal")]
    [InlineData("reserved_key", "malformed_response")]
    [InlineData("control_text", "malformed_response")]
    public async Task RulePayloadCannotAttachToAnotherActionOrBypassAdmission(string scenario, string outcome)
    {
        var provider = new RuleProvider(scenario);
        var runtime = new CognitionRuntime("councillor", provider);
        var observation = Observation();
        if (scenario == "unoffered_action") observation = observation with
        { Candidates = observation.Candidates.Where(candidate => candidate.Id != "council_town_author").ToArray() };
        var admission = await runtime.RequestAndDecideAsync(observation);
        Assert.Equal(outcome, admission.Outcome);
        Assert.True(admission.Accepted);
        Assert.True(admission.FellBack);
        Assert.Equal("safe_idle", admission.Intention!.CandidateId);
        Assert.Null(admission.TownLawProposal);
    }

    private static InhabitantObservation Observation() => new("councillor", 301, 0, 1, "sha256:civic-model", 8_000,
        [new("safe_idle", "Continue safely.", 100), new("council_town_author", "Propose a Town social rule.", 110)]);

    private sealed class RuleProvider(string scenario) : IDecisionProvider
    {
        public DecisionProviderKind Kind => scenario == "deterministic_provider" ? DecisionProviderKind.Deterministic : DecisionProviderKind.LargeLanguageModel;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var choice = scenario == "other_action" ? "safe_idle" : "council_town_author";
            var rule = scenario == "missing_payload" ? null : new CognitionTownLawProposal(
                scenario == "reserved_key" ? "shared_food" : "protected_grove",
                scenario == "control_text" ? "Hidden\ncontrol text" : "Ask before felling Town trees.");
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, request.Observation.InhabitantId,
                Kind, ProviderEpoch, request.Observation.RunEpoch, request.Observation.DecisionGeneration,
                request.Observation.ObservationDigest, choice, 1, new Dictionary<string, double> { [choice] = 1 }, TownLawProposal: rule));
        }
    }

    private sealed class RuleReply : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var answer = JsonSerializer.Serialize(new
            {
                selected_candidate_id = "council_town_author",
                confidence = 1,
                probabilities = new Dictionary<string, double> { ["council_town_author"] = 1 },
                town_law = new { key = "protected_grove", text = "Ask before felling Town trees.", repeal = false }
            });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(JsonSerializer.Serialize(new { choices = new[] { new { message = new { content = answer } } } }), Encoding.UTF8, "application/json") });
        }
    }
}

public sealed partial class TownHallTests
{
    [Fact]
    public async Task UnrelatedRulePayloadTerminatesOneDecisionWithoutRepeatedModelCalls()
    {
        var state = AtHall(WithHall(Initial()));
        var actor = state.Society.Society.Inhabitants[0].Id;
        var provider = new UnrelatedRuleProvider();
        using var world = PrivateWorldRuntime.Restore(state, id => id == actor ? provider : new CivicChooser(false, false));
        for (var tick = 0; tick < 5; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(1, provider.RequestCount);
        Assert.Null(world.TownCouncils.Single().Ballot);
        Assert.Empty(world.TownCouncils.Single().Laws!);
        Assert.Equal("safe_idle", world.ExportState().Society.Cognition.Runtimes.Single(item => item.InhabitantId == actor).CurrentIntention!.CandidateId);
    }

    private sealed class UnrelatedRuleProvider : IDecisionProvider
    {
        public int RequestCount { get; private set; }
        public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            RequestCount++;
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, request.Observation.InhabitantId,
                Kind, ProviderEpoch, request.Observation.RunEpoch, request.Observation.DecisionGeneration,
                request.Observation.ObservationDigest, "safe_idle", 1, new Dictionary<string, double> { ["safe_idle"] = 1 },
                TownLawProposal: new("protected_grove", "Ask before felling Town trees.")));
        }
    }

    [Fact]
    public async Task ModelRuleUsesActualHallAndCouncilMajorityAcrossPendingBallotReload()
    {
        var state = AtHall(WithHall(Initial()));
        var author = state.Society.Society.Inhabitants[0].Id;
        using var world = PrivateWorldRuntime.Restore(state, actor => actor == author ? new PhysicalRuleAuthor() : new CivicChooser(false, false));
        for (var tick = 0; tick < 320 && world.TownCouncils.SingleOrDefault()?.Ballot is null; tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var proposed = Assert.IsType<TownLawBallot>(world.TownCouncils.Single().Ballot);
        Assert.Equal(author, proposed.ProposerId);
        Assert.Equal("protected_grove", proposed.Key);
        Assert.Empty(world.TownCouncils.Single().Laws!);
        var saved = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        using var restored = PrivateWorldRuntime.Restore(saved, _ => new CivicChooser(false, false));
        var inventoryBefore = JsonSerializer.Serialize(restored.Society.Inventory);
        foreach (var voter in restored.TownCouncils.Single().MemberIds.Where(id => id != author).Take(2))
            Assert.True(restored.VoteTownLaw(voter, Town, true).Applied);
        Assert.Null(restored.TownCouncils.Single().Ballot);
        Assert.Equal("Ask before felling Town trees.", Assert.Single(restored.TownCouncils.Single().Laws!).Text);
        Assert.Equal(inventoryBefore, JsonSerializer.Serialize(restored.Society.Inventory));
        restored.Validate();
    }

    [Fact]
    public void OwnerProjectionSurvivesGodotDtoDeserializationWithTownLawAndExactBusinessTerms()
    {
        var state = AtHall(WithHall(Initial()));
        var author = state.Society.Society.Inhabitants[0].Id;
        var others = state.Society.Society.Inhabitants.Select(person => person.Id).Where(id => id != author).ToArray();
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "listed-grain", "grain", Alpha, 3,
            storageBuildingId: "first-town-farmhouse");
        state = state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } };
        using var world = PrivateWorldRuntime.Restore(state, _ => new CivicChooser(false, false));
        Assert.True(world.ProposeTownLaw(author, Town, "protected_grove", "Ask before felling Town trees.").Applied);
        foreach (var voter in others.Take(2)) Assert.True(world.VoteTownLaw(voter, Town, true).Applied);
        var farmhouse = world.WorldSimulation.Buildings.Single(building => building.InstanceId == "first-town-farmhouse");
        state = world.ExportState() with
        {
            Inhabitants = world.Inhabitants.Select(person => person.InhabitantId == author
            ? person with { Position = farmhouse.Position } : person).ToArray()
        };
        using var atBusiness = PrivateWorldRuntime.Restore(state, _ => new CivicChooser(false, false));
        Assert.True(atBusiness.ListBusinessGoods(author, farmhouse.InstanceId, "listed-grain", 2, "wood", 3).Applied);
        var server = new OwnerWorldObservationStore(atBusiness).GetSnapshot();
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var client = JsonSerializer.Deserialize<OwnerWorldSnapshot>(JsonSerializer.Serialize(server, options), options)!;
        Assert.Null(client.Council);
        var council = Assert.Single(client.TownCouncils);
        Assert.Equal(Town, council.TownId);
        Assert.Equal("test-town-hall", council.HallId);
        Assert.Equal(4, council.MemberNames.Count);
        Assert.Equal("protected_grove", Assert.Single(council.Laws).Key);
        Assert.Equal("Ask before felling Town trees.", council.Laws.Single().Text);
        var listing = Assert.Single(client.BusinessTrade!.Listings);
        Assert.Equal(farmhouse.InstanceId, listing.BuildingId);
        Assert.Equal(Alpha, listing.HouseholdId);
        Assert.Equal("grain", listing.GoodsKind);
        Assert.Equal(2, listing.GoodsQuantity);
        Assert.Equal("wood", listing.PaymentKind);
        Assert.Equal(3, listing.PaymentQuantity);
        Assert.Empty(listing.Contents);
        Assert.Equal("Aster offered 2 Grain for 3 Wood at Farmhouse.", WorldEventText.Describe(new(1, 1, "business_goods_listed",
            $"{author}|{farmhouse.InstanceId}|grain|2|wood|3"), client with
            {
                Inhabitants = client.Inhabitants.Select(person =>
                person.Id == author ? person with { DisplayName = "Aster" } : person).ToArray()
            }));
    }

    private sealed class PhysicalRuleAuthor : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var choice = request.Observation.Candidates.FirstOrDefault(candidate => candidate.Id == "council_town_author") ??
                request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle");
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, request.Observation.InhabitantId, Kind,
                ProviderEpoch, request.Observation.RunEpoch, request.Observation.DecisionGeneration, request.Observation.ObservationDigest,
                choice.Id, 1, new Dictionary<string, double> { [choice.Id] = 1 }, TownLawProposal: choice.Id == "council_town_author"
                    ? new("protected_grove", "Ask before felling Town trees.") : null));
        }
    }
}

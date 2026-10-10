using System.Text.RegularExpressions;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class NoLegalBuildingSiteTests
{
    [Theory]
    [InlineData("request_land_use", true)]
    [InlineData("claim_land", true)]
    [InlineData("request_land_use", false)]
    [InlineData("claim_land", false)]
    public async Task FailedSiteSearchExplainsEligibleLandRoutesWithoutGrantingLand(string action, bool blocked)
    {
        using var setup = NormalPathWorld.CreateGenerated("council-land-claim", _ => new ActionCoverageRecorder(chooseIdle: true));
        // A real approved design goes through the normal content pipeline. Its
        // large footprint cannot fit the existing Town; the small version can.
        var version = ContentVersion.Parse("1.0.0");
        var digest = "sha256:" + new string('c', 64);
        var dimension = blocked ? 32 : 1;
        var definition = new BuildingDefinition(digest, "land-test-clinic", version, "Land test clinic",
            dimension, dimension, 1, [], ["clinic"]);
        var manifest = new ContentPackageManifest("land-test-clinic", version, digest, [],
        [new ContentDefinition(BuildingDefinition.SchemaKind, definition.LocalId, version, definition.DisplayName,
            definition.PayloadDigest,
            $$"""{"schema":"building/v1","width":{{dimension}},"height":{{dimension}},"capacity":1,"buildCosts":[],"tags":["clinic"]}""")], []);
        setup.ProposeContent(manifest);
        setup.ValidateContent(manifest.PackageId, setup.ResolveContent(manifest.PackageId));
        setup.ApproveContent(manifest.PackageId);
        setup.StageContent(manifest.PackageId);
        Assert.True((await setup.AdvanceOneTickAsync()).Advanced);
        Assert.Contains(setup.WorldContent.Buildings, item => item.CanonicalId == definition.CanonicalId);
        var initial = setup.ExportState();
        var author = setup.Towns[0].ResidentIds[0];
        var provider = new LandRouteProvider(author, action, definition.CanonicalId);
        using var world = PrivateWorldRuntime.Restore(initial with
        {
            Inhabitants = initial.Inhabitants.Select(person => person with { Position = initial.Towns![0].OriginSite!.Value }).ToArray(),
        }, _ => provider);
        world.SubmitInstruction(new OwnerInstructionRequest("consider-building-land", "owner:test", author,
            OwnerInstructionKind.Suggestive, "Consider a land request for the household building."));
        for (var tick = 0; tick < 10 && (action == "request_land_use"
                 ? world.HouseholdLandUseRequests.Count == 0
                 : world.Towns[0].Governance!.Proposals.All(item => item.Kind != "land_claim")); tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);

        Assert.True(provider.Description is not null, provider.LastObservation);
        Assert.Equal(blocked, provider.Description.StartsWith("No legal building site is available for Land test clinic.", StringComparison.Ordinal));
        Assert.Equal(!blocked, provider.BuildingSiteOffered);
        Assert.Contains("nearest you:", provider.Description, StringComparison.Ordinal);
        Assert.Equal(initial.TownLandTitles, world.TownLandTitles);
        Assert.Equal(initial.HouseholdLandUseRights, world.HouseholdLandUseRights);
        if (action == "request_land_use")
        {
            var request = Assert.Single(world.HouseholdLandUseRequests);
            Assert.Equal("pending", request.Status);
            Assert.Empty(request.Consents);
            Assert.Equal(author, request.RequestedByAgentId);
        }
        else
        {
            var proposal = Assert.Single(world.Towns[0].Governance!.Proposals, item => item.Kind == "land_claim");
            Assert.Equal("pending", proposal.Status);
            Assert.Empty(proposal.Votes);
        }
        world.Validate();
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False((await world.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        using var reloaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(before), _ => provider);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(reloaded.ExportState()));
    }

    private sealed class LandRouteProvider(string author, string action, string definitionId) : IDecisionProvider
    {
        public string? Description { get; private set; }
        public bool BuildingSiteOffered { get; private set; }
        public string? LastObservation { get; private set; }
        public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
        public long ProviderEpoch => 1;

        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var o = request.Observation;
            if (o.InhabitantId == author)
                LastObservation = System.Text.Json.JsonSerializer.Serialize(o.Self) + "\n" +
                    string.Join("\n", o.Candidates.Select(item => item.Id + ": " + item.Description));
            var choice = o.InhabitantId == author && Description is null
                ? o.Candidates.FirstOrDefault(item => item.Id.Contains("|" + action + "|", StringComparison.Ordinal)) : null;
            var selected = choice ?? o.Candidates.Single(item => item.Id == "safe_idle");
            CognitionLandTile[]? tiles = null;
            if (choice is not null)
            {
                Description = choice.Description;
                BuildingSiteOffered = o.Candidates.Any(item => TownConstructionCandidateIds.TryParse(item.Id, out var site) &&
                    site.IsBuilding && site.DefinitionId == definitionId);
                var match = Regex.Match(choice.Description[(choice.Description.IndexOf("nearest you:", StringComparison.Ordinal))..], @"\((-?\d+), (-?\d+)\)");
                Assert.True(match.Success);
                tiles = [new CognitionLandTile(int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
                    int.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture))];
            }
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, o.InhabitantId, Kind, ProviderEpoch,
                o.RunEpoch, o.DecisionGeneration, o.ObservationDigest, selected.Id, 1,
                o.Candidates.ToDictionary(item => item.Id, item => item.Id == selected.Id ? 1d : 0d, StringComparer.Ordinal),
                CivicLandTiles: tiles));
        }
    }
}

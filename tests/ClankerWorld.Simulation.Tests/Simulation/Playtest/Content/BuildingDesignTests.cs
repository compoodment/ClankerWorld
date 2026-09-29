using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Control;

namespace ClankerWorld.Simulation.Tests;

public sealed class BuildingDesignTests
{
    [Theory]
    [InlineData("shelter")]
    [InlineData("hearth")]
    public async Task InhabitantsConstructReviewedDesignThroughOrdinaryPlanning(string purpose)
    {
        var package = BuildingDesign.Create("Resident-built " + purpose, purpose, 8);
        var definitionId = package.Definitions.Single().CanonicalId(package.PackageDigest);
        var target = "build:building:" + definitionId;
        using var world = new PrivateWorldRuntime("building-design-playtest", _ => new DesignProvider(target));
        world.StageStarterContent();
        for (var tick = 0; tick < 3; tick++) await world.AdvanceOneTickAsync();
        world.ProposeContent(package);
        world.ValidateContent(package.PackageId, world.ResolveContent(package.PackageId));
        world.ApproveContent(package.PackageId);
        world.StageContent(package.PackageId);
        for (var tick = 0; tick < 400 && !world.WorldSimulation.Buildings.Any(building =>
            building.DefinitionId == definitionId); tick++) await world.AdvanceOneTickAsync();
        var placed = Assert.Single(world.WorldSimulation.Buildings, building => building.DefinitionId == definitionId);
        Assert.Contains(world.Inhabitants, person => person.Project is { Stage: "completed" } project &&
            project.CandidateId == TownConstructionCandidateIds.Building(definitionId, placed.Position));
        using var restored = PrivateWorldRuntime.Restore(world.ExportState());
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }

    [Theory]
    [InlineData("shelter", 1)]
    [InlineData("hearth", 48)]
    public async Task BuildingPreviewUsesRealConstructionCostsAndReturnsExactValidatedBytes(string purpose, int woodCost)
    {
        var action = new OwnerBuildingDesignAction("My building", purpose, woodCost);
        var preview = await OwnerBuildingDesign.PreviewAsync(action, CancellationToken.None);
        Assert.True(preview.ConstructionPassed);
        Assert.Equal(woodCost, preview.WoodConsumed);
        Assert.True(OwnerContentBinding.TryMapManifest(preview.Package, out var manifest, out var failure), failure);
        Assert.Equal(preview.ManifestDigest, ContentPackageManifestCodec.ComputeManifestDigest(manifest!));
        Assert.Equal(ContentPackageManifestCodec.Encode(BuildingDesign.Create(action.Name, purpose, woodCost)),
            ContentPackageManifestCodec.Encode(manifest!));
        Assert.Empty(manifest!.DeclaredCapabilities);
        Assert.Empty(manifest.Dependencies);
        var result = PrivateWorldRuntime.PreviewWorldContent([manifest], [manifest.PackageId]);
        Assert.True(result.IsValid);
        Assert.Contains(purpose == "hearth" ? "warmth" : purpose, Assert.Single(result.WorldContent.Buildings).Tags);
        Assert.Equal(OwnerContentBinding.ProposePayload(preview.Package),
            ClankerWorld.GodotClient.UI.OwnerWorldActionPayload.ContentPropose(
                System.Text.Json.JsonSerializer.Deserialize<ClankerWorld.GodotClient.UI.OwnerContentPackageAction>(
                    System.Text.Json.JsonSerializer.Serialize(preview.Package))!));
        Assert.Equal(OwnerBuildingDesign.Payload(action), ClankerWorld.GodotClient.UI.OwnerWorldActionPayload.BuildingDesign(
            new(action.Name, purpose, woodCost)));
    }

    [Theory]
    [InlineData("", "shelter", 4)]
    [InlineData("name", "filesystem", 4)]
    [InlineData("name", "shelter", 49)]
    public void InvalidBuildingDesignsFailBeforeCreatingContent(string name, string purpose, int woodCost) =>
        Assert.ThrowsAny<ArgumentException>(() => BuildingDesign.Create(name, purpose, woodCost));

    [Fact]
    public async Task CancelledPreviewDoesNotProduceAResult()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            OwnerBuildingDesign.PreviewAsync(new("Home", "shelter", 8), cancellation.Token));
    }

    [Fact]
    public void ReviewRejectsUnrecognizedManifestAndOversizedName()
    {
        var original = BuildingDesign.Create("Home", "shelter", 8);
        Assert.Equal(("Home", "shelter", 8), BuildingDesign.Read(original));
        Assert.ThrowsAny<ArgumentException>(() => BuildingDesign.Create(new string('x', 97), "shelter", 8));
        Assert.ThrowsAny<ArgumentException>(() => BuildingDesign.Read(original with { PackageId = "other-package" }));
    }

    [Fact]
    public void EditedDesignHasDifferentImmutableIdentity()
    {
        var original = BuildingDesign.Create("Home", "shelter", 8);
        Assert.NotEqual(original.PackageDigest, BuildingDesign.Create("Home", "shelter", 9).PackageDigest);
        Assert.NotEqual(original.PackageDigest, BuildingDesign.Create("Store", "storage", 8).PackageDigest);
    }

    private sealed class DesignProvider(string target) : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var selected = request.Observation.Candidates.OrderBy(candidate =>
                    candidate.Id.StartsWith(target + ":site:", StringComparison.Ordinal) ? -1 : candidate.DeterministicPriority)
                .ThenBy(candidate => candidate.Id, StringComparer.Ordinal).First().Id;
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, request.Observation.InhabitantId,
                Kind, ProviderEpoch, request.Observation.RunEpoch, request.Observation.DecisionGeneration,
                request.Observation.ObservationDigest, selected, 1,
                request.Observation.Candidates.ToDictionary(candidate => candidate.Id, candidate => candidate.Id == selected ? 1d : 0d, StringComparer.Ordinal)));
        }
    }
}

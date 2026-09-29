using ClankerWorld.Simulation.Content;

namespace ClankerWorld.Simulation.Tests;

public sealed class ContentDefinitionTests
{
    private const string PackageDigest = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public void DefinitionCollectionsAreDefensivelyCopiedAndCanonicalized()
    {
        var costs = new List<ContentQuantity>
        {
            new("wood", 2),
            new("stone", 1)
        };
        var tags = new List<string> { "zeta", "alpha" };
        var building = Building("workshop", tags, costs);
        costs[0] = new ContentQuantity("glass", 9);
        tags[0] = "mutated";

        Assert.Equal(["stone", "wood"], building.BuildCosts.Select(item => item.ResourceId));
        Assert.Equal(["alpha", "zeta"], building.Tags);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)building.Tags)[0] = "mutated");
        Assert.Throws<NotSupportedException>(() => ((IList<ContentQuantity>)building.BuildCosts)[0] = new("x", 1));
    }

    [Fact]
    public void InvalidBuildingAndRecipeDefinitionsAreRejected()
    {
        Assert.Throws<ArgumentException>(() => Building("Bad ID").Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => Building("tiny", width: 0).Validate());
        Assert.Throws<ArgumentException>(() => Building("named", displayName: "  not canonical  ").Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new BuildingDefinition(
            PackageDigest,
            "forge",
            new ContentVersion(-1, 0, 0),
            "Forge",
            2,
            2,
            1).Validate());

        var invalidRecipe = Recipe(
            "invalid",
            inputs: [],
            outputs: [new ContentQuantity("item", 1)],
            durationTicks: 0);
        Assert.Throws<ArgumentException>(() => invalidRecipe.Validate());

        var crop = Recipe(
            "carrots",
            inputs: [],
            outputs: [new ContentQuantity("carrot", 1)],
            tags: ["crop"]);
        crop.Validate();
        Assert.True(crop.IsCrop);

        var badReference = Recipe(
            "bad-reference",
            workstationBuildingId: "not-a-canonical-building-id");
        Assert.Throws<ArgumentException>(() => badReference.Validate());

        var badDigest = Building(
            "bad-digest",
            payloadDigest: "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");
        Assert.Throws<InvalidDataException>(() => badDigest.Validate());
    }

    [Fact]
    public void ApplyingDefinitionsIsDeterministicAndOrdersEachKindByCanonicalId()
    {
        var forge = Building("forge");
        var shelter = Building("shelter");
        var smelt = Recipe("smelt", forge);
        var rest = Recipe("rest", shelter);

        var first = ContentDefinitionApplicator.Apply([shelter, forge], [rest, smelt]);
        var second = ContentDefinitionApplicator.Apply([forge, shelter], [smelt, rest]);

        Assert.Equal(first.StateDigest, second.StateDigest);
        Assert.Equal(
            [forge.CanonicalId, shelter.CanonicalId],
            first.Buildings.Select(item => item.CanonicalId));
        Assert.Equal(
            [rest.CanonicalId, smelt.CanonicalId],
            first.Recipes.Select(item => item.CanonicalId));
        first.Validate();
    }

    [Fact]
    public void StateCodecRoundTripsCanonicalBytesAndDerivedDigest()
    {
        var building = Building("forge");
        var state = ContentDefinitionApplicator.Apply(
            [building],
            [Recipe("smelt", building)]);

        var encoded = DeclarativeWorldContentCodec.Encode(state);
        var decoded = DeclarativeWorldContentCodec.Decode(encoded);
        var reencoded = DeclarativeWorldContentCodec.Encode(decoded);

        Assert.Equal(state.StateDigest, decoded.StateDigest);
        Assert.True(encoded.SequenceEqual(reencoded));
        Assert.Equal(state.Buildings.Select(item => item.CanonicalId), decoded.Buildings.Select(item => item.CanonicalId));
        Assert.Equal(state.Recipes.Select(item => item.PayloadDigest), decoded.Recipes.Select(item => item.PayloadDigest));
    }

    [Fact]
    public void ApplyingToExistingStateRejectsDuplicateIdsAndDanglingWorkstations()
    {
        var building = Building("forge");
        var state = ContentDefinitionApplicator.Apply([building], []);

        Assert.Throws<InvalidOperationException>(() => ContentDefinitionApplicator.Apply(state, [building], []));

        var dangling = Recipe(
            "smelt",
            workstationBuildingId: $"{PackageDigest}/building/missing@1.0.0");
        Assert.Throws<InvalidDataException>(() => ContentDefinitionApplicator.Apply([], [dangling]));
    }

    [Fact]
    public void ContentPreviewResolvesAndMaterializesWithoutMutatingTheBaseProjection()
    {
        var building = Building("preview-kitchen", displayName: "Preview kitchen");
        var package = new ContentPackageManifest(
            "preview-content",
            ContentVersion.Parse("1.0.0"),
            PackageDigest,
            [],
            [new ContentDefinition(
                BuildingDefinition.SchemaKind,
                building.LocalId,
                building.Version,
                building.DisplayName,
                building.PayloadDigest,
                """{"schema":"building/v1","width":2,"height":2,"capacity":4,"buildCosts":[{"resourceId":"wood","amount":1}],"tags":["camp"]}""")],
            []);
        var baseContent = new DeclarativeWorldContentState([], []);

        var preview = ContentPackagePreview.Run([package], [package.PackageId], baseContent);

        Assert.True(preview.IsValid, preview.Diagnostic);
        Assert.Equal(building.CanonicalId, Assert.Single(preview.WorldContent.Buildings).CanonicalId);
        Assert.Empty(baseContent.Buildings);
        Assert.Empty(baseContent.Recipes);
    }

    [Fact]
    public void ContentPreviewReturnsTheBaseProjectionWhenTypedMaterializationFails()
    {
        var package = new ContentPackageManifest(
            "bad-content",
            ContentVersion.Parse("1.0.0"),
            "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
            [],
            [new ContentDefinition(
                BuildingDefinition.SchemaKind,
                "bad-building",
                ContentVersion.Parse("1.0.0"),
                "Bad building",
                "sha256:cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc",
                """{"schema":"building/v1","width":0,"height":1,"capacity":1,"buildCosts":[],"tags":[]}""")],
            []);

        var preview = ContentPackagePreview.Run([package], [package.PackageId]);

        Assert.False(preview.IsValid);
        Assert.Equal("typed_content_invalid", preview.FailureCode);
        Assert.Empty(preview.WorldContent.Buildings);
    }

    private static BuildingDefinition Building(
        string localId,
        IEnumerable<string>? tags = null,
        IEnumerable<ContentQuantity>? costs = null,
        int width = 2,
        string displayName = "Forge",
        string? payloadDigest = null) => new(
        PackageDigest,
        localId,
        ContentVersion.Parse("1.0.0"),
        displayName,
        width,
        2,
        4,
        costs ?? [new ContentQuantity("wood", 1)],
        tags ?? ["camp"],
        payloadDigest);

    private static RecipeDefinition Recipe(
        string localId,
        BuildingDefinition? workstation = null,
        IEnumerable<ContentQuantity>? inputs = null,
        IEnumerable<ContentQuantity>? outputs = null,
        int durationTicks = 3,
        string? workstationBuildingId = null,
        IEnumerable<string>? tags = null) => new(
        PackageDigest,
        localId,
        ContentVersion.Parse("1.0.0"),
        "Recipe",
        inputs ?? [new ContentQuantity("ore", 1)],
        outputs ?? [new ContentQuantity("item", 1)],
        durationTicks,
        workstationBuildingId ?? workstation?.CanonicalId,
        tags ?? ["crafting"]);
}

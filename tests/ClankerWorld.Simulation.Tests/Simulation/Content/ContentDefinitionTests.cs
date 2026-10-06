using ClankerWorld.Simulation.Content;

namespace ClankerWorld.Simulation.Tests;

public sealed class ContentDefinitionTests
{
    private const string PackageDigest = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

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
    public void ApplyingToExistingStateRejectsDuplicateIdsAndDanglingWorkstations()
    {
        var building = Building("forge");
        var shelter = Building("shelter");
        var state = ContentDefinitionApplicator.Apply([shelter, building], []);
        Assert.Equal([building.CanonicalId, shelter.CanonicalId], state.Buildings.Select(item => item.CanonicalId));

        Assert.Throws<InvalidOperationException>(() => ContentDefinitionApplicator.Apply(state, [building], []));

        var dangling = Recipe(
            "smelt",
            workstationBuildingId: $"{PackageDigest}/building/missing@1.0.0");
        Assert.Throws<InvalidDataException>(() => ContentDefinitionApplicator.Apply([], [dangling]));
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

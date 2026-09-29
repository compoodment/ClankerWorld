using System.Security.Cryptography;
using System.Text.Json;
using ClankerWorld.Simulation.Content;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>Owner-authored data using existing building effects, never executable content.</summary>
public static class BuildingDesign
{
    public static ContentPackageManifest Create(string name, string purpose, int woodCost)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name != name.Trim() || name.Length > 96 || name.Any(char.IsControl))
            throw new ArgumentException("Use a trimmed, printable building name of at most 96 characters.", nameof(name));
        if (purpose is not ("shelter" or "storage" or "hearth"))
            throw new ArgumentException("Choose shelter, storage or hearth.", nameof(purpose));
        if (woodCost is < 1 or > 48)
            throw new ArgumentOutOfRangeException(nameof(woodCost), "Building designs must cost between 1 and 48 wood.");
        var designBytes = JsonSerializer.SerializeToUtf8Bytes(new { schema = "clankerworld.building-design/v1", name, purpose, woodCost });
        var hash = Convert.ToHexStringLower(SHA256.HashData(designBytes));
        var digest = "sha256:" + hash;
        var version = ContentVersion.Parse("1.0.0");
        var building = new BuildingDefinition(digest, "design", version, name, 1, 1,
            purpose == "hearth" ? 1 : 4, [new("wood", woodCost)], [purpose == "hearth" ? "warmth" : purpose]);
        return StarterContent.BuildManifest("owner-building-" + hash, version, digest, [building], [], []);
    }

    public static string Describe(string purpose) => purpose switch
    {
        "shelter" => "Shelter protects against weather exposure.",
        "storage" => "Storage slows spoilage in shared household stores.",
        "hearth" => "A hearth provides nearby heat while fuelled; it consumes wood and can go out.",
        _ => throw new ArgumentException("Unknown building purpose.", nameof(purpose)),
    };

    public static (string Name, string Purpose, int WoodCost) Read(ContentPackageManifest manifest)
    {
        var content = ContentDefinitionPayloadCodec.ApplyPackage(new DeclarativeWorldContentState([], []), manifest);
        if (content.Buildings.Count != 1 || content.Recipes.Count != 0)
            throw new ArgumentException("This workbench reviews single-building designs only.", nameof(manifest));
        var building = content.Buildings.Single();
        if (building.Tags.Count != 1 || building.BuildCosts.Count != 1 || building.BuildCosts[0].ResourceId != "wood")
            throw new ArgumentException("The package is not a workbench building design.", nameof(manifest));
        var purpose = building.Tags[0] == "warmth" ? "hearth" : building.Tags[0];
        var expected = Create(building.DisplayName, purpose, building.BuildCosts[0].Amount);
        if (ContentPackageManifestCodec.ComputeManifestDigest(expected) != ContentPackageManifestCodec.ComputeManifestDigest(manifest))
            throw new ArgumentException("The package differs from the bounded workbench design format.", nameof(manifest));
        return (building.DisplayName, purpose, building.BuildCosts[0].Amount);
    }
}

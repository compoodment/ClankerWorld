using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClankerWorld.Simulation.Content;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>Versioned, host-shipped data content; activation still uses package governance.</summary>
public static class StarterContent
{
    public const string PackageId = "clankerworld-starter-v1";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static ContentPackageManifest Create()
    {
        var digest = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(
            Encoding.UTF8.GetBytes("clankerworld-starter-v1:1.0.0:shelter-storage-fire-workshop:crops-meals-tools")));
        var version = ContentVersion.Parse("1.0.0");
        var shelter = new BuildingDefinition(digest, "shelter", version, "Shelter", 1, 1, 4,
            [new("wood", 8)], ["shelter"]);
        var storage = new BuildingDefinition(digest, "storage", version, "Storehouse", 1, 1, 4,
            [new("wood", 6)], ["storage"]);
        var fire = new BuildingDefinition(digest, "fire", version, "Cooking fire", 1, 1, 1,
            [new("wood", 4)], ["cooking"]);
        var workshop = new BuildingDefinition(digest, "workshop", version, "Workshop", 1, 1, 1,
            [new("wood", 10)], ["workshop"]);
        BuildingDefinition[] buildings = [shelter, storage, fire, workshop];
        RecipeDefinition[] recipes =
        [
            new(digest, "vegetables", version, "Vegetable plot", [], [new("food", 6)], 60, null, ["crop"]),
            new(digest, "meal", version, "Cook a meal", [new("food", 2), new("wood", 1)], [new("food", 4)], 12, fire.CanonicalId, ["food"]),
            new(digest, "tools", version, "Craft tools", [new("wood", 3)], [new("tool", 1)], 20, workshop.CanonicalId, ["craft"]),
        ];
        return BuildManifest(PackageId, version, digest, buildings, recipes, []);
    }

    internal static ContentPackageManifest BuildManifest(string packageId, ContentVersion version, string digest,
        IEnumerable<BuildingDefinition> buildings, IEnumerable<RecipeDefinition> recipes,
        IReadOnlyList<ContentDependency> dependencies, DeclarativeWorldContentState? baseWorldContent = null)
    {
        var definitions = buildings.Select(building => new ContentDefinition(
            BuildingDefinition.SchemaKind, building.LocalId, version, building.DisplayName, building.PayloadDigest,
            JsonSerializer.Serialize(new
            {
                schema = "building/v1",
                building.Width,
                building.Height,
                building.Capacity,
                buildCosts = building.BuildCosts,
                building.Tags,
            }, JsonOptions)))
            .Concat(recipes.Select(recipe => new ContentDefinition(
                RecipeDefinition.SchemaKind, recipe.LocalId, version, recipe.DisplayName, recipe.PayloadDigest,
                JsonSerializer.Serialize(new
                {
                    schema = "recipe/v1",
                    recipe.Inputs,
                    recipe.Outputs,
                    recipe.DurationTicks,
                    recipe.WorkstationBuildingId,
                    recipe.Tags,
                }, JsonOptions))))
            .ToArray();
        var manifest = new ContentPackageManifest(packageId, version, digest, dependencies, definitions, []);
        manifest.Validate();
        _ = ContentDefinitionPayloadCodec.ApplyPackage(baseWorldContent ?? new DeclarativeWorldContentState([], []), manifest);
        return manifest;
    }
}

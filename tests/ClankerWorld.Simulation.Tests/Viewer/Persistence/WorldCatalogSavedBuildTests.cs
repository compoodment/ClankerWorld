using System.Text.Json;
using System.Text.Json.Nodes;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Control;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class WorldCatalogSavedBuildTests
{
    [Theory]
    [InlineData("GameVersion", "42")]
    [InlineData("GameVersion", "[]")]
    [InlineData("GameVersion", "{}")]
    [InlineData("SourceRevision", "42")]
    [InlineData("SourceRevision", "[]")]
    [InlineData("SourceRevision", "{}")]
    public void MalformedOptionalBuildValuesDoNotPreventReadingTheWorldCatalog(string field, string malformedJson)
    {
        var directory = Directory.CreateTempSubdirectory("catalog-build-diagnostics-");
        try
        {
            var path = Path.Combine(directory.FullName, "active.json");
            var indexPath = path + ".worlds/catalog.json";
            using var runtime = new PrivateWorldRuntime("catalog-build-diagnostics");
            var settings = new WorldAutosaveSettings(runtime.Society.WorldId, true, 5, 3, DateTimeOffset.MinValue, -1);
            var catalog = new WorldCatalogStore(path, runtime.ExportState(), [], settings);
            var document = JsonNode.Parse(File.ReadAllBytes(indexPath))!;
            document["Worlds"]![0]![field] = JsonNode.Parse(malformedJson);
            var original = document.ToJsonString();
            File.WriteAllText(indexPath, original);
            var identity = WorldCatalogStore.ReadActiveIdentity(path);
            Assert.Equal(runtime.Society.WorldId, identity?.WorldId);
            Assert.Null(field == "GameVersion" ? identity?.GameVersion : identity?.SourceRevision);
            Assert.Equal(original, File.ReadAllText(indexPath));
            var reopened = new WorldCatalogStore(path, runtime.ExportState(), [], settings);
            Assert.Equal(catalog.Active().Id, reopened.Active().Id);
            using var restored = PrivateWorldRuntime.Restore(reopened.Read(reopened.Active().Id));
            restored.Validate();
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public void CatalogEntriesNameTheBuildThatLastWroteOrOpenedEachWorld()
    {
        var directory = Directory.CreateTempSubdirectory("catalog-saved-build-");
        try
        {
            var activePath = Path.Combine(directory.FullName, "active.json");
            var indexPath = activePath + ".worlds/catalog.json";
            using var first = new PrivateWorldRuntime("catalog-saved-build-first");
            using var second = new PrivateWorldRuntime("catalog-saved-build-second");
            var settings = new WorldAutosaveSettings("catalog-saved-build-first", true, 5, 3, DateTimeOffset.MinValue, -1);
            var catalog = new WorldCatalogStore(activePath, first.ExportState(), [], settings);
            var added = catalog.Add("Second", second.ExportState());
            AssertThisBuild(catalog.Active());
            AssertThisBuild(added);

            // An index written by another build, or before builds were recorded.
            var older = catalog.Capture();
            older = older with { Worlds = older.Worlds.Select(world => world with { GameVersion = null, SourceRevision = null }).ToArray() };
            File.WriteAllBytes(indexPath, JsonSerializer.SerializeToUtf8Bytes(older));
            Assert.DoesNotContain("GameVersion", File.ReadAllText(indexPath), StringComparison.Ordinal);

            catalog = new WorldCatalogStore(activePath, first.ExportState(), [], settings);
            AssertThisBuild(catalog.Active());
            Assert.Null(catalog.Capture().Worlds.Single(world => world.Id == added.Id).GameVersion);

            catalog.Select(added.Id);
            AssertThisBuild(catalog.Capture().Worlds.Single(world => world.Id == added.Id));
        }
        finally { directory.Delete(recursive: true); }
    }

    private static void AssertThisBuild(CatalogWorld world)
    {
        Assert.Equal(BuildInformation.Version, world.GameVersion);
        Assert.Equal(BuildInformation.SourceRevision, world.SourceRevision);
    }
}

using System.Text.Json;
using System.Text.Json.Nodes;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Control;
using ClankerWorld.Viewer.Observation;
using Microsoft.Extensions.DependencyInjection;

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

    [Theory]
    [InlineData("older", "other_version")]
    [InlineData("current", "incompatible")]
    [InlineData(null, "incompatible")]
    public void UnrestorableWorldListAndSelectionPreserveFiles(string? savedBuild, string compatibility)
    {
        var directory = Directory.CreateTempSubdirectory("catalog-other-version-");
        try
        {
            string id;
            string activePath;
            using (var host = new ViewerWebApplicationFactory(directory.FullName, privateWorld: true))
            using (var client = host.CreateClient())
            {
                host.Services.GetRequiredService<PrivateWorldRuntime>().Pause();
                using var other = new PrivateWorldRuntime("catalog-other-version");
                other.Pause();
                id = host.Services.GetRequiredService<WorldCatalogStore>().Add("Other", other.ExportState()).Id;
                activePath = host.Services.GetRequiredService<PrivateWorldStateFile>().Path;
            }
            var indexPath = activePath + ".worlds/catalog.json";
            var document = JsonNode.Parse(File.ReadAllBytes(indexPath))!;
            var world = document["Worlds"]!.AsArray().Single(node => node!["Id"]!.GetValue<string>() == id)!;
            world["GameVersion"] = savedBuild switch
            {
                "older" => "0.0.1-older",
                "current" => BuildInformation.Version,
                _ => null,
            };
            File.WriteAllText(indexPath, document.ToJsonString());
            var checkpointPath = Path.Combine(activePath + ".worlds", id + ".save");
            var damaged = File.ReadAllBytes(checkpointPath);
            damaged[0] = (byte)'x';
            File.WriteAllBytes(checkpointPath, damaged);

            using var reopened = new ViewerWebApplicationFactory(directory.FullName, privateWorld: true);
            using var reopenedClient = reopened.CreateClient();
            var runtime = reopened.Services.GetRequiredService<PrivateWorldRuntime>();
            runtime.Pause();
            var selection = reopened.Services.GetRequiredService<WorldSelectionCoordinator>();
            var activeWorld = runtime.Society.WorldId;
            var activeBytes = File.ReadAllBytes(activePath);
            var indexBytes = File.ReadAllBytes(indexPath);
            var listed = selection.List().Worlds.Single(entry => entry.Id == id);
            Assert.Equal(compatibility, listed.Compatibility);
            if (savedBuild == "older") Assert.Contains("0.0.1-older", listed.CompatibilityReason, StringComparison.Ordinal);
            Assert.Throws<InvalidDataException>(() => selection.Select(id));
            Assert.Equal(activeWorld, runtime.Society.WorldId);
            Assert.Equal(activeBytes, File.ReadAllBytes(activePath));
            Assert.Equal(indexBytes, File.ReadAllBytes(indexPath));
            Assert.Equal(damaged, File.ReadAllBytes(checkpointPath));
        }
        finally { directory.Delete(recursive: true); }
    }

    private static void AssertThisBuild(CatalogWorld world)
    {
        Assert.Equal(BuildInformation.Version, world.GameVersion);
        Assert.Equal(BuildInformation.SourceRevision, world.SourceRevision);
    }
}

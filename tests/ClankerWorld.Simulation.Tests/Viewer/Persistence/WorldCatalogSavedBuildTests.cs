using System.Text.Json;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Control;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class WorldCatalogSavedBuildTests
{
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

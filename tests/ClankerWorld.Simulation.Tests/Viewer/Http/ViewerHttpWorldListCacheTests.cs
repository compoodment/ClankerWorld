using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Control;
using ClankerWorld.Viewer.Observation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class ViewerHttpTests
{
    [Fact]
    public void WorldListRechecksCheckpointBytesAndSelectionAfterAFileIsReplaced()
    {
        var directory = Directory.CreateTempSubdirectory("world-list-checkpoint-cache-");
        try
        {
            using var host = new ViewerWebApplicationFactory(directory.FullName, privateWorld: true);
            using var client = host.CreateClient();
            var runtime = host.Services.GetRequiredService<PrivateWorldRuntime>();
            runtime.Pause();
            var catalog = host.Services.GetRequiredService<WorldCatalogStore>();
            var selection = host.Services.GetRequiredService<WorldSelectionCoordinator>();
            var listLog = new RecordingLogger<WorldSelectionCoordinator>();
            host.Services.GetRequiredService<ILoggerFactory>().AddProvider(
                new RecordingLoggerProvider<WorldSelectionCoordinator>(listLog));
            using var other = new PrivateWorldRuntime("checkpoint-cache-world");
            other.Pause();
            var entry = catalog.Add("Other", other.ExportState());
            var path = Path.Combine(host.Services.GetRequiredService<PrivateWorldStateFile>().Path + ".worlds",
                entry.Id + ".save");
            var original = File.ReadAllBytes(path);
            var modifiedUtc = File.GetLastWriteTimeUtc(path);

            Assert.Equal("compatible", selection.List().Worlds.Single(world => world.Id == entry.Id).Compatibility);
            Assert.Equal("compatible", selection.List().Worlds.Single(world => world.Id == entry.Id).Compatibility);
            Assert.Contains(listLog.Messages, message => message.Contains(
                "world_list outcome=checked worlds=2 cache_hits=1 scans=0", StringComparison.Ordinal));
            Assert.DoesNotContain(listLog.Messages, message => message.Contains(
                "checkpoint-cache-world", StringComparison.Ordinal));
            var damaged = (byte[])original.Clone();
            damaged[0] = (byte)'x';
            File.WriteAllBytes(path, damaged);
            File.SetLastWriteTimeUtc(path, modifiedUtc);

            Assert.Equal("incompatible", selection.List().Worlds.Single(world => world.Id == entry.Id).Compatibility);
            Assert.Throws<InvalidDataException>(() => selection.Select(entry.Id));
            Assert.NotEqual(entry.WorldId, runtime.Society.WorldId);
            Assert.Equal(damaged, File.ReadAllBytes(path));

            File.WriteAllBytes(path, original);
            File.SetLastWriteTimeUtc(path, modifiedUtc);
            Assert.Equal("compatible", selection.List().Worlds.Single(world => world.Id == entry.Id).Compatibility);
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            Assert.Throws<OperationCanceledException>(() => selection.List(canceled.Token));
            Assert.Equal("compatible", selection.List().Worlds.Single(world => world.Id == entry.Id).Compatibility);
            selection.Select(entry.Id);
            Assert.Equal(entry.WorldId, runtime.Society.WorldId);
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public void WorldListRechecksHistoryAfterAHealthyCheckpointWasCached()
    {
        var directory = Directory.CreateTempSubdirectory("world-list-history-cache-");
        try
        {
            using var host = new ViewerWebApplicationFactory(directory.FullName, privateWorld: true);
            using var client = host.CreateClient();
            var file = host.Services.GetRequiredService<PrivateWorldStateFile>();
            var catalog = host.Services.GetRequiredService<WorldCatalogStore>();
            var selection = host.Services.GetRequiredService<WorldSelectionCoordinator>();
            using var other = new PrivateWorldRuntime("history-cache-world");
            other.Pause();
            for (var n = 0; n < PrivateWorldHistory.CompactionThreshold + 2; n++)
                other.SetJevEnabled(!other.JevEnabled);
            file.Save(other);
            var head = other.ExportState().HistoryArchiveHead!;
            var entry = catalog.Add("History", other.ExportState());
            var historyPath = Path.Combine(file.Path + ".history", head + ".json");
            var history = File.ReadAllBytes(historyPath);

            Assert.Equal("compatible", selection.List().Worlds.Single(world => world.Id == entry.Id).Compatibility);
            File.WriteAllText(historyPath, "damaged history");
            Assert.Equal("incompatible", selection.List().Worlds.Single(world => world.Id == entry.Id).Compatibility);
            File.WriteAllBytes(historyPath, history);
            Assert.Equal("compatible", selection.List().Worlds.Single(world => world.Id == entry.Id).Compatibility);
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public void WorldListRechecksCredentialsAndKeepsUntouchedWorldCachedAcrossSelection()
    {
        var directory = Directory.CreateTempSubdirectory("world-list-provider-cache-");
        try
        {
            using var host = new ViewerWebApplicationFactory(directory.FullName, privateWorld: true);
            using var client = host.CreateClient();
            var runtime = host.Services.GetRequiredService<PrivateWorldRuntime>();
            runtime.Pause();
            var file = host.Services.GetRequiredService<PrivateWorldStateFile>();
            var catalog = host.Services.GetRequiredService<WorldCatalogStore>();
            var selection = host.Services.GetRequiredService<WorldSelectionCoordinator>();
            var providers = host.Services.GetRequiredService<ProviderConfigurationStore>();
            var listLog = new RecordingLogger<WorldSelectionCoordinator>();
            host.Services.GetRequiredService<ILoggerFactory>().AddProvider(
                new RecordingLoggerProvider<WorldSelectionCoordinator>(listLog));
            var autosave = host.Services.GetRequiredService<WorldAutosaveStore>();
            var firstId = catalog.Capture().ActiveId;
            var slotId = Guid.NewGuid().ToString("N");
            catalog.ArchiveActive(runtime.ExportState(),
                [new InhabitantProviderAssignment("founder:checkpoint", "planning", "openai", "test-model", slotId)],
                autosave.Capture());
            using var other = new PrivateWorldRuntime("provider-cache-world");
            other.Pause();
            var entry = catalog.Add("Other", other.ExportState());
            using var bystander = new PrivateWorldRuntime("provider-cache-bystander");
            bystander.Pause();
            var bystanderEntry = catalog.Add("Bystander", bystander.ExportState());
            runtime.SwitchPausedWorld(other.ExportState());
            file.Save(runtime);
            autosave.SelectWorld(entry.WorldId, entry.AutosaveSettings);
            catalog.Select(entry.Id);

            Assert.Equal("incompatible", selection.List().Worlds.Single(world => world.Id == firstId).Compatibility);
            providers.CreateCredentialSlot(new OwnerCredentialSlotCreationAction(slotId, "openai", "Test account",
                "test-only-credential"));
            Assert.Equal("compatible", selection.List().Worlds.Single(world => world.Id == firstId).Compatibility);
            Assert.Contains("world_list outcome=checked worlds=3 cache_hits=2 scans=0",
                listLog.Messages.Last(message => message.Contains("world_list outcome=checked", StringComparison.Ordinal)));
            selection.Select(firstId);
            var afterFirstSwitch = selection.List();
            Assert.Equal("compatible", afterFirstSwitch.Worlds.Single(world => world.Id == entry.Id).Compatibility);
            Assert.Equal("compatible", afterFirstSwitch.Worlds.Single(world => world.Id == bystanderEntry.Id).Compatibility);
            Assert.Contains("world_list outcome=checked worlds=3 cache_hits=1 scans=1",
                listLog.Messages.Last(message => message.Contains("world_list outcome=checked", StringComparison.Ordinal)));
            Assert.Equal(firstId, catalog.Capture().ActiveId);
            selection.Select(entry.Id);
            var afterSecondSwitch = selection.List();
            Assert.Equal("compatible", afterSecondSwitch.Worlds.Single(world => world.Id == firstId).Compatibility);
            Assert.Equal("compatible", afterSecondSwitch.Worlds.Single(world => world.Id == bystanderEntry.Id).Compatibility);
            Assert.DoesNotContain(listLog.Messages, message => message.Contains(
                "test-only-credential", StringComparison.Ordinal));
        }
        finally { directory.Delete(recursive: true); }
    }
}

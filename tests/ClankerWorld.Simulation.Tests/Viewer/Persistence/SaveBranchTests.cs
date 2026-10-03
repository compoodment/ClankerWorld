using System.Text.Json.Nodes;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Control;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

/// <summary>Playing on from an older save keeps the original saves in their own branch.</summary>
public sealed class SaveBranchTests : IDisposable
{
    private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("save-branches-");
    private readonly PrivateWorldRuntime runtime = new("save-branches");
    private readonly string path;
    private readonly ManualWorldSaveStore store;

    public SaveBranchTests()
    {
        path = Path.Combine(directory.FullName, "world.json");
        store = new ManualWorldSaveStore(path);
        runtime.Pause();
    }

    public void Dispose()
    {
        runtime.Dispose();
        directory.Delete(recursive: true);
    }

    private string WorldId => runtime.Society.WorldId;
    private string ManualDirectory => path + ".manual";

    private async Task PlayAsync(int ticks = 1)
    {
        runtime.Resume();
        for (var i = 0; i < ticks; i++) Assert.True((await runtime.AdvanceOneTickAsync()).Advanced);
        runtime.Pause();
    }

    private void Load(ManualWorldSave save)
    {
        store.ContinueFrom(save.Id);
        runtime.LoadPausedCheckpoint(store.Read(save.Id));
    }

    private ManualWorldSave Listed(string id) => Assert.Single(store.List(WorldId), save => save.Id == id);

    [Fact]
    public async Task SavesOfOneHistoryShareTheFirstBranch()
    {
        var first = store.Create("Before the flood", runtime, []);
        await PlayAsync();
        var second = store.Create("Big harvest", runtime, []);

        Assert.NotNull(first.Branch);
        Assert.Equal(1, first.Branch.Number);
        Assert.Null(first.Branch.StartedFromId);
        Assert.Null(first.ContinuedFromId);
        Assert.Equal(first.Branch, second.Branch);
        Assert.Equal(first.Id, second.ContinuedFromId);
    }

    [Fact]
    public async Task PlayingOnFromAnOlderSaveStartsANewBranchAndKeepsTheOriginal()
    {
        var flood = store.Create("Before the flood", runtime, []);
        await PlayAsync(2);
        var harvest = store.Create("Big harvest", runtime, []);

        Load(flood);
        await PlayAsync();
        var winter = store.Create("Hungry winter", runtime, []);
        var autosave = store.CreateAutosave(runtime, [],
            new WorldAutosaveSettings(WorldId, true, 5, 5, DateTimeOffset.UtcNow, runtime.WorldTick));

        Assert.NotNull(winter.Branch);
        Assert.Equal(2, winter.Branch.Number);
        Assert.Equal(flood.Id, winter.Branch.StartedFromId);
        Assert.Equal("Before the flood", winter.Branch.StartedFromName);
        Assert.Equal(flood.WorldTick, winter.Branch.StartedFromTick);
        Assert.Equal(flood.Id, winter.ContinuedFromId);
        Assert.Equal(winter.Branch, autosave.Branch);

        var reopened = new ManualWorldSaveStore(path);
        var listed = reopened.List(WorldId);
        Assert.Equal(harvest, Assert.Single(listed, save => save.Id == harvest.Id));
        Assert.Equal(flood.Branch, Assert.Single(listed, save => save.Id == flood.Id).Branch);
        Assert.Equal(winter.Branch, Assert.Single(listed, save => save.Id == winter.Id).Branch);
    }

    [Fact]
    public void ChangesWhilePausedBranchEvenAtTheSameWorldTick()
    {
        var before = store.Create("Before changing Jev", runtime, []);
        runtime.SetJevEnabled(false);
        var changed = store.Create("Jev off", runtime, []);
        Assert.Equal(before.Branch, changed.Branch);

        Load(before);
        var again = store.Create("Jev still on", runtime, []);

        Assert.Equal(before.WorldTick, again.WorldTick);
        Assert.Equal(2, again.Branch?.Number);
        Assert.Equal(before.Id, again.Branch?.StartedFromId);
        Assert.Equal(before.Branch, Listed(changed.Id).Branch);
    }

    [Fact]
    public async Task LoadingTheLatestSaveOfABranchContinuesIt()
    {
        var flood = store.Create("Before the flood", runtime, []);
        await PlayAsync();
        var harvest = store.Create("Big harvest", runtime, []);
        Load(flood);
        Load(harvest);
        await PlayAsync();
        var later = store.Create("Later", runtime, []);

        Assert.Equal(harvest.Branch, later.Branch);
        Assert.Equal(harvest.Id, later.ContinuedFromId);
    }

    [Fact]
    public async Task TheCurrentPositionSaysWhereTheNextSaveGoes()
    {
        Assert.Equal(new SaveTimelinePosition(null, null, true, 1), store.CurrentPosition(WorldId));
        var flood = store.Create("Before the flood", runtime, []);
        Assert.Equal(new SaveTimelinePosition(flood.Id, flood.Branch?.Id, false, 1, flood.WorldTick), store.CurrentPosition(WorldId));
        await PlayAsync();
        var harvest = store.Create("Big harvest", runtime, []);

        Load(flood);
        Assert.Equal(new SaveTimelinePosition(flood.Id, flood.Branch?.Id, true, 2, flood.WorldTick), store.CurrentPosition(WorldId));
        await PlayAsync();
        var winter = store.Create("Hungry winter", runtime, []);
        Assert.NotEqual(flood.Branch, winter.Branch);
        Assert.Equal(new SaveTimelinePosition(winter.Id, winter.Branch?.Id, false, 2, winter.WorldTick), store.CurrentPosition(WorldId));

        Load(harvest);
        Assert.Equal(new SaveTimelinePosition(harvest.Id, harvest.Branch?.Id, false, 1, harvest.WorldTick), store.CurrentPosition(WorldId));
        Assert.Equal(harvest.Branch, store.Create("Later", runtime, []).Branch);
    }

    [Fact]
    public async Task TheNextBranchNumberSurvivesDeletingTheHighestBranch()
    {
        var flood = store.Create("Before the flood", runtime, []);
        await PlayAsync();
        store.Create("Big harvest", runtime, []);
        Load(flood);
        await PlayAsync();
        var winter = store.Create("Hungry winter", runtime, []);
        Assert.Equal(2, winter.Branch?.Number);
        store.Delete(winter.Id, WorldId, winter.CreatedUtc);
        Load(flood);

        var position = store.CurrentPosition(WorldId);

        Assert.True(position.StartsNewBranch);
        Assert.Equal(3, position.NextBranchNumber);
        Assert.Equal(flood.WorldTick, position.ContinuedFromTick);
        var next = store.Create("Another winter", runtime, []);
        Assert.Equal(position.NextBranchNumber, next.Branch?.Number);
        Assert.Equal(flood.Id, next.Branch?.StartedFromId);
    }

    [Fact]
    public async Task ADeletedContinuationKeepsItsTimeAfterRestartWithoutChangingFiles()
    {
        var flood = store.Create("Before the flood", runtime, []);
        await PlayAsync();
        var harvest = store.Create("Big harvest", runtime, []);
        store.Delete(harvest.Id, WorldId, harvest.CreatedUtc);
        var files = Directory.GetFiles(ManualDirectory).ToDictionary(file => file, File.ReadAllBytes);
        var reopened = new ManualWorldSaveStore(path);

        var position = reopened.CurrentPosition(WorldId);

        Assert.Equal(new SaveTimelinePosition(harvest.Id, harvest.Branch?.Id, false,
            1, harvest.WorldTick), position);
        Assert.Equal(files.Keys.Order(StringComparer.Ordinal), Directory.GetFiles(ManualDirectory).Order(StringComparer.Ordinal));
        foreach (var (file, bytes) in files) Assert.Equal(bytes, File.ReadAllBytes(file));
        var next = reopened.Create("After deletion", runtime, []);
        Assert.Equal(flood.Branch, next.Branch);
        Assert.Equal(position.NextBranchNumber, next.Branch?.Number);
        Assert.Equal(harvest.BranchPosition + 1, next.BranchPosition);
    }

    [Fact]
    public async Task BrowsingSavesWithoutPlayingNeedsNoCopyAndStartsNoBranch()
    {
        var flood = store.Create("Before the flood", runtime, []);
        await PlayAsync();
        Assert.Null(store.FindUnchangedSave(runtime, [], null));
        var harvest = store.Create("Big harvest", runtime, []);
        Assert.Equal(harvest.Id, store.FindUnchangedSave(runtime, [], null)?.Id);

        Load(flood);
        Assert.Equal(flood.Id, store.FindUnchangedSave(runtime, [], null)?.Id);
        InhabitantProviderAssignment[] changedRouting = [new("founder-scout", "planning", "openai", "new-model")];
        Assert.Null(store.FindUnchangedSave(runtime, changedRouting, null));
        Assert.Null(store.FindUnchangedSave(runtime, [],
            new WorldAutosaveSettings(WorldId, false, 5, 5, DateTimeOffset.UtcNow, 0)));

        Load(harvest);
        await PlayAsync();
        Assert.Equal(harvest.Branch, store.Create("Later", runtime, []).Branch);
        Assert.All(store.List(WorldId), save => Assert.Equal(1, save.Branch?.Number));
    }

    [Fact]
    public async Task OverwritingMovesTheChosenSlotIntoTheRunningBranch()
    {
        var flood = store.Create("Before the flood", runtime, []);
        await PlayAsync(2);
        var harvest = store.Create("Big harvest", runtime, []);
        Load(flood);
        await PlayAsync();

        var receipt = store.Overwrite(harvest.Id, runtime, []);

        Assert.Equal(2, receipt.Saved.Branch?.Number);
        Assert.Equal(flood.Id, receipt.Saved.Branch?.StartedFromId);
        var backup = Listed(receipt.BackupId);
        Assert.Equal(harvest.Branch, backup.Branch);
        Assert.Equal(harvest.WorldTick, backup.WorldTick);
    }

    [Fact]
    public async Task OverwritingTheSaveTheWorldContinuesFromBranchesFromItsKeptCopy()
    {
        var flood = store.Create("Before the flood", runtime, []);
        await PlayAsync(2);
        store.Create("Big harvest", runtime, []);
        Load(flood);
        await PlayAsync();

        var receipt = store.Overwrite(flood.Id, runtime, []);

        Assert.Equal(2, receipt.Saved.Branch?.Number);
        Assert.Equal(receipt.BackupId, receipt.Saved.Branch?.StartedFromId);
        Assert.Equal("Before overwriting: Before the flood", receipt.Saved.Branch?.StartedFromName);
        Assert.Equal(flood.Branch, Listed(receipt.BackupId).Branch);
    }

    [Fact]
    public async Task AutosaveRotationKeepsEachBranchsOwnNewestAutosaves()
    {
        var settings = new WorldAutosaveSettings(WorldId, true, 5, 1, DateTimeOffset.UtcNow, 0);
        var start = store.Create("Start", runtime, []);
        await PlayAsync();
        var firstOld = store.CreateAutosave(runtime, [], settings);
        await PlayAsync();
        var firstNewest = store.CreateAutosave(runtime, [], settings);
        Load(start);
        await PlayAsync();
        var secondOld = store.CreateAutosave(runtime, [], settings);
        await PlayAsync();
        var secondNewest = store.CreateAutosave(runtime, [], settings);
        Assert.NotEqual(firstNewest.Branch, secondNewest.Branch);

        store.KeepNewestAutosaves(1, secondNewest.Id, WorldId);

        var ids = store.List(WorldId).Select(save => save.Id).ToArray();
        Assert.Contains(start.Id, ids);
        Assert.Contains(firstNewest.Id, ids);
        Assert.Contains(secondNewest.Id, ids);
        Assert.DoesNotContain(firstOld.Id, ids);
        Assert.DoesNotContain(secondOld.Id, ids);
    }

    [Fact]
    public async Task ADamagedBranchRecordStartsANewBranchWithoutHidingSaves()
    {
        var log = new RecordingLogger<ManualWorldSaveStore>();
        var logged = new ManualWorldSaveStore(path, log);
        var first = logged.Create("First", runtime, []);
        var timeline = Assert.Single(Directory.GetFiles(ManualDirectory, "timeline-*.json"));
        File.WriteAllText(timeline, "{not json");
        await PlayAsync();

        var next = logged.Create("Next", runtime, []);

        Assert.Equal(2, next.Branch?.Number);
        Assert.Null(next.Branch?.StartedFromId);
        Assert.Equal(2, logged.List(WorldId).Count);
        Assert.Contains(log.Messages, message => message.Contains("outcome=branch_record_ignored", StringComparison.Ordinal));
        Assert.Equal(first.Branch, Assert.Single(logged.List(WorldId), save => save.Id == first.Id).Branch);
    }

    [Fact]
    public void ADamagedBranchOnASaveKeepsTheSaveListed()
    {
        var save = store.Create("First", runtime, []);
        var metadataPath = Path.Combine(ManualDirectory, save.Id + ".meta.json");
        var metadata = JsonNode.Parse(File.ReadAllText(metadataPath))!;
        metadata["Save"]!["Branch"]!["Id"] = "not-an-id";
        File.WriteAllText(metadataPath, metadata.ToJsonString());

        var listed = Listed(save.Id);

        Assert.Null(listed.Branch);
        Assert.Equal(save.Name, listed.Name);
    }

    [Fact]
    public async Task SavesFromBeforeBranchesStayListedAndCanStartABranch()
    {
        var legacy = store.Create("Old save", runtime, []);
        var metadataPath = Path.Combine(ManualDirectory, legacy.Id + ".meta.json");
        var metadata = JsonNode.Parse(File.ReadAllText(metadataPath))!;
        metadata["Save"]!.AsObject().Remove("Branch");
        metadata["Save"]!.AsObject().Remove("ContinuedFromId");
        File.WriteAllText(metadataPath, metadata.ToJsonString());
        File.Delete(Assert.Single(Directory.GetFiles(ManualDirectory, "timeline-*.json")));

        Assert.Null(Listed(legacy.Id).Branch);
        Load(Listed(legacy.Id));
        await PlayAsync();
        var next = store.Create("Next", runtime, []);

        Assert.Equal(1, next.Branch?.Number);
        Assert.Equal(legacy.Id, next.Branch?.StartedFromId);
        Assert.Null(Listed(legacy.Id).Branch);
    }

    [Fact]
    public async Task ARestoredBranchRecordUndoesAFailedLoad()
    {
        var flood = store.Create("Before the flood", runtime, []);
        await PlayAsync();
        var harvest = store.Create("Big harvest", runtime, []);

        var restore = store.ContinueFrom(flood.Id);
        store.RestoreTimeline(restore);
        await PlayAsync();

        Assert.Equal(harvest.Branch, store.Create("Later", runtime, []).Branch);
    }

    [Fact]
    public void DeletingTheWorldRemovesItsBranchRecord()
    {
        store.Create("First", runtime, []);
        Assert.Single(Directory.GetFiles(ManualDirectory, "timeline-*.json"));

        store.DeleteWorldSnapshots(WorldId);

        Assert.Empty(store.List(WorldId));
        Assert.Empty(Directory.GetFiles(ManualDirectory, "timeline-*.json"));
    }
}

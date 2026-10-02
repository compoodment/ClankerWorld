using System.Text.Json.Nodes;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class SaveBranchRecoveryRegressionTests : IDisposable
{
    private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("save-branch-recovery-");
    private readonly PrivateWorldRuntime runtime = new("save-branch-recovery");
    private readonly string path;
    private readonly ManualWorldSaveStore store;

    public SaveBranchRecoveryRegressionTests()
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

    private void Load(ManualWorldSave save)
    {
        store.ContinueFrom(save.Id);
        runtime.LoadPausedCheckpoint(store.Read(save.Id));
    }

    private async Task<ManualWorldSave> MakeRunningAutosaveAsync()
    {
        runtime.Resume();
        Assert.True((await runtime.AdvanceOneTickAsync()).Advanced);
        var settings = new WorldAutosaveSettings(WorldId, true, 5, 5, DateTimeOffset.UtcNow, runtime.WorldTick);
        var saved = store.CreateAutosave(runtime, [], settings);
        runtime.Pause();
        return saved;
    }

    [Fact]
    public void DeletingAnIntermediatePausedSaveDoesNotMergeDivergentHistories()
    {
        var first = store.Create("Before changing Jev", runtime, []);
        runtime.SetJevEnabled(false);
        var middle = store.Create("Jev off", runtime, []);
        runtime.SetJevEnabled(true);
        var later = store.Create("Jev on again", runtime, []);
        var laterBytes = PrivateWorldRuntimeCodec.Encode(store.Read(later.Id));
        Assert.Equal(first.WorldTick, later.WorldTick);
        Assert.Equal(first.Branch, later.Branch);

        store.Delete(middle.Id, WorldId, middle.CreatedUtc);
        Load(first);
        runtime.SetJevEnabled(false);
        var divergent = store.Create("Another Jev choice", runtime, []);

        Assert.NotNull(divergent.Branch);
        Assert.NotEqual(first.Branch?.Id, divergent.Branch.Id);
        Assert.Equal(first.Id, divergent.Branch.StartedFromId);
        Assert.Equal(later.Branch, Assert.Single(store.List(WorldId), save => save.Id == later.Id).Branch);
        Assert.Equal(laterBytes, PrivateWorldRuntimeCodec.Encode(store.Read(later.Id)));
        Assert.DoesNotContain(store.List(WorldId), save => save.Id == middle.Id);
        var reopened = new ManualWorldSaveStore(path);
        Assert.Equal(divergent.Branch, Assert.Single(reopened.List(WorldId), save => save.Id == divergent.Id).Branch);
        Assert.Equal(laterBytes, PrivateWorldRuntimeCodec.Encode(reopened.Read(later.Id)));
    }

    [Fact]
    public void OverwritingTheLoadedPausedSaveKeepsLaterHistoryOnItsOriginalBranch()
    {
        var first = store.Create("Before changing Jev", runtime, []);
        var firstBytes = PrivateWorldRuntimeCodec.Encode(store.Read(first.Id));
        runtime.SetJevEnabled(false);
        var later = store.Create("Jev off", runtime, []);
        var laterBytes = PrivateWorldRuntimeCodec.Encode(store.Read(later.Id));
        Assert.Equal(first.WorldTick, later.WorldTick);

        Load(first);
        runtime.SetJevEnabled(false);
        var receipt = store.Overwrite(first.Id, runtime, []);

        Assert.NotNull(receipt.Saved.Branch);
        Assert.NotEqual(first.Branch?.Id, receipt.Saved.Branch.Id);
        Assert.Equal(receipt.BackupId, receipt.Saved.Branch.StartedFromId);
        var kept = Assert.Single(store.List(WorldId), save => save.Id == receipt.BackupId);
        Assert.Equal(first.Branch, kept.Branch);
        Assert.Equal(firstBytes, PrivateWorldRuntimeCodec.Encode(store.Read(kept.Id)));
        Assert.Equal(later.Branch, Assert.Single(store.List(WorldId), save => save.Id == later.Id).Branch);
        Assert.Equal(laterBytes, PrivateWorldRuntimeCodec.Encode(store.Read(later.Id)));
        var reopened = new ManualWorldSaveStore(path);
        Assert.Equal(receipt.Saved.Branch, Assert.Single(reopened.List(WorldId), save => save.Id == first.Id).Branch);
        Assert.Equal(firstBytes, PrivateWorldRuntimeCodec.Encode(reopened.Read(receipt.BackupId)));
    }

    [Theory]
    [InlineData("Branch", "[]")]
    [InlineData("Branch", "\"not an object\"")]
    [InlineData("Branch.Number", "\"not a number\"")]
    [InlineData("Branch.StartedFromTick", "\"not a tick\"")]
    [InlineData("Branch.StartedFromName", "{}")]
    [InlineData("ContinuedFromId", "[]")]
    [InlineData("ContinuedFromCreatedUtc", "\"not a date\"")]
    public void MalformedOptionalBranchFieldsKeepTheCheckpointReadable(string field, string malformedJson)
    {
        var save = store.Create("Recoverable checkpoint", runtime, []);
        var savedBytes = PrivateWorldRuntimeCodec.Encode(store.Read(save.Id));
        var metadataPath = Path.Combine(path + ".manual", save.Id + ".meta.json");
        var metadata = JsonNode.Parse(File.ReadAllText(metadataPath))!;
        var target = metadata["Save"]!;
        var fields = field.Split('.');
        if (fields.Length == 2) target = target[fields[0]]!;
        target[fields[^1]] = JsonNode.Parse(malformedJson);
        var damagedText = metadata.ToJsonString();
        File.WriteAllText(metadataPath, damagedText);

        var reopened = new ManualWorldSaveStore(path);
        var listed = Assert.Single(reopened.List(WorldId));
        Assert.Equal(save.Id, listed.Id);
        Assert.Equal(save.Name, listed.Name);
        Assert.Null(listed.Branch);
        Assert.Null(listed.ContinuedFromId);
        Assert.Null(listed.ContinuedFromCreatedUtc);
        Assert.Equal(savedBytes, PrivateWorldRuntimeCodec.Encode(reopened.Read(save.Id)));
        reopened.ContinueFrom(save.Id);
        runtime.LoadPausedCheckpoint(reopened.Read(save.Id));
        Assert.Equal(damagedText, File.ReadAllText(metadataPath));
    }

    [Theory]
    [InlineData("Name", "{}")]
    [InlineData("WorldTick", "\"not a tick\"")]
    [InlineData("Id", "[]")]
    public void MalformedRequiredMetadataIsStillRefusedAndPreserved(string field, string malformedJson)
    {
        var save = store.Create("Do not accept corrupt metadata", runtime, []);
        var metadataPath = Path.Combine(path + ".manual", save.Id + ".meta.json");
        var checkpointPath = Path.Combine(path + ".manual", save.Id + ".save");
        var savedBytes = File.ReadAllBytes(checkpointPath);
        var metadata = JsonNode.Parse(File.ReadAllText(metadataPath))!;
        metadata["Save"]![field] = JsonNode.Parse(malformedJson);
        var damagedText = metadata.ToJsonString();
        File.WriteAllText(metadataPath, damagedText);

        var reopened = new ManualWorldSaveStore(path);
        Assert.Empty(reopened.List(WorldId));
        Assert.Throws<InvalidDataException>(() => reopened.Read(save.Id));
        Assert.Throws<InvalidDataException>(() => reopened.ContinueFrom(save.Id));
        Assert.Equal(damagedText, File.ReadAllText(metadataPath));
        Assert.Equal(savedBytes, File.ReadAllBytes(checkpointPath));
    }

    [Theory]
    [InlineData("Number", "\"not a number\"")]
    [InlineData("Id", "\"not-a-branch-id\"")]
    public async Task AutosaveRotationPreservesUnknownBranchesInsteadOfMergingTheirHistories(
        string branchField, string malformedJson)
    {
        var origin = store.Create("Before the histories diverged", runtime, []);
        var first = await MakeRunningAutosaveAsync();
        Load(origin);
        var second = await MakeRunningAutosaveAsync();
        Assert.NotNull(first.Branch);
        Assert.NotNull(second.Branch);
        Assert.NotEqual(first.Branch.Id, second.Branch.Id);
        var checkpoints = new[] { first, second }.ToDictionary(save => save.Id,
            save => File.ReadAllBytes(Path.Combine(path + ".manual", save.Id + ".save")));

        foreach (var save in new[] { first, second })
        {
            var metadataPath = Path.Combine(path + ".manual", save.Id + ".meta.json");
            var metadata = JsonNode.Parse(File.ReadAllText(metadataPath))!;
            metadata["Save"]!["Branch"]![branchField] = JsonNode.Parse(malformedJson);
            File.WriteAllText(metadataPath, metadata.ToJsonString());
        }

        var reopened = new ManualWorldSaveStore(path);
        var unknownBranches = reopened.List(WorldId).Where(save => save.IsAutosave).ToArray();
        Assert.Equal(2, unknownBranches.Length);
        Assert.All(unknownBranches, save => Assert.Null(save.Branch));
        foreach (var save in new[] { first, second })
            Assert.Equal(WorldId, reopened.Read(save.Id).Society.Society.WorldId);

        reopened.KeepNewestAutosaves(1, preserveId: second.Id, worldId: WorldId);

        var retained = reopened.List(WorldId).Where(save => save.IsAutosave).ToArray();
        Assert.Equal(new[] { first.Id, second.Id }.Order(), retained.Select(save => save.Id).Order());
        foreach (var save in new[] { first, second })
        {
            Assert.Equal(checkpoints[save.Id], File.ReadAllBytes(Path.Combine(path + ".manual", save.Id + ".save")));
            Assert.Equal(WorldId, reopened.Read(save.Id).Society.Society.WorldId);
        }
    }

    [Fact]
    public async Task AutosaveRotationStillTrimsGenuineSavesFromBeforeBranches()
    {
        var legacy = new[]
        {
            await MakeRunningAutosaveAsync(),
            await MakeRunningAutosaveAsync(),
            await MakeRunningAutosaveAsync()
        };
        foreach (var save in legacy)
        {
            var metadataPath = Path.Combine(path + ".manual", save.Id + ".meta.json");
            var metadata = JsonNode.Parse(File.ReadAllText(metadataPath))!;
            var record = metadata["Save"]!.AsObject();
            record["Branch"] = null;
            record["BranchPosition"] = 0;
            record.Remove("ContinuedFromId");
            record.Remove("ContinuedFromCreatedUtc");
            File.WriteAllText(metadataPath, metadata.ToJsonString());
        }
        var newest = legacy[^1];
        var newestPath = Path.Combine(path + ".manual", newest.Id + ".save");
        var newestBytes = File.ReadAllBytes(newestPath);
        var reopened = new ManualWorldSaveStore(path);
        Assert.Equal(3, reopened.List(WorldId).Count);
        Assert.All(reopened.List(WorldId), save => Assert.Null(save.Branch));

        reopened.KeepNewestAutosaves(1, preserveId: newest.Id, worldId: WorldId);

        Assert.Equal(newest.Id, Assert.Single(reopened.List(WorldId)).Id);
        Assert.Equal(newestBytes, File.ReadAllBytes(newestPath));
        Assert.Equal(WorldId, reopened.Read(newest.Id).Society.Society.WorldId);
    }
}

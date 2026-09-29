using ClankerWorld.Simulation.Harness;
using System.Text.Json;

namespace ClankerWorld.Simulation.Tests;

public sealed class OwnerWorldRuntimeTests
{
    [Fact]
    public void InstructionCannotTargetAnInhabitantThatIsNotActiveInThisWorld()
    {
        var runtime = new OwnerWorldRuntime("camp-alpha");

        var exception = Assert.Throws<ArgumentException>(() => runtime.SubmitInstruction(
            new OwnerInstructionRequest(
                "missing-target",
                "owner-device:test",
                "not-a-real-inhabitant",
                OwnerInstructionKind.Suggestive,
                "Do a little dance.")));

        Assert.Contains("No active inhabitant", exception.Message, StringComparison.Ordinal);
        Assert.Empty(runtime.Capture().Events);
    }

    [Fact]
    public void ValidPausedBatchIsAtomicAndChangesOnlyTheCurrentTopologyDigest()
    {
        var runtime = new OwnerWorldRuntime("camp-alpha");
        var before = runtime.Capture();
        var water = before.Snapshot.CurrentMap.Tiles.Single(tile => tile.Terrain == TerrainKind.Water).Position;
        Assert.True(runtime.Pause());

        var receipt = runtime.ApplyAuthoringBatch(new OwnerAuthoringBatch(
            "turn-water-into-mountain",
            [new SetTerrainOperation(water, TerrainKind.Mountain)]));
        var after = runtime.Capture();

        Assert.True(receipt.Applied, receipt.Failure);
        Assert.Equal(before.Snapshot.InitialMapManifestDigest, after.Snapshot.InitialMapManifestDigest);
        Assert.Equal(before.Snapshot.World.Identity.InitialMapManifestDigest, after.Snapshot.InitialMapManifestDigest);
        Assert.NotEqual(before.Snapshot.CurrentMapManifestDigest, after.Snapshot.CurrentMapManifestDigest);
        Assert.Equal(before.Snapshot.TopologyRevision + 1, after.Snapshot.TopologyRevision);
        Assert.Equal(before.Snapshot.World.Map.ManifestDigest, after.Snapshot.World.Map.ManifestDigest);
        Assert.Equal(TerrainKind.Mountain, after.Snapshot.CurrentMap.Tiles.Single(tile => tile.Position == water).Terrain);
    }

    [Fact]
    public void OwnerCannotAuthorABuildingOnMountainGround()
    {
        var runtime = new OwnerWorldRuntime("camp-alpha");
        Assert.True(runtime.Pause());
        var before = runtime.Capture();
        var mountain = before.Snapshot.CurrentMap.Tiles.Single(tile => tile.Terrain == TerrainKind.Mountain).Position;

        var result = runtime.ApplyAuthoringBatch(new OwnerAuthoringBatch(
            "mountain-building",
            [new PlaceBuildingOperation("mountain-home", "shelter", mountain)]));

        Assert.False(result.Applied);
        Assert.Contains("mountains and peaks", result.Failure, StringComparison.Ordinal);
        Assert.Equal(before.Snapshot.CurrentMapManifestDigest, runtime.Capture().Snapshot.CurrentMapManifestDigest);
    }

    [Fact]
    public void InvalidMixedPausedBatchLeavesEveryLiveProjectionUntouched()
    {
        var runtime = new OwnerWorldRuntime("camp-alpha");
        Assert.True(runtime.Pause());
        var before = runtime.Capture();
        var water = before.Snapshot.CurrentMap.Tiles.Single(tile => tile.Terrain == TerrainKind.Water).Position;

        var receipt = runtime.ApplyAuthoringBatch(new OwnerAuthoringBatch(
            "mixed-invalid",
            [
                new SetTerrainOperation(water, TerrainKind.Mountain),
                new PlaceResourceOperation("berry-patch", "food", new GridPoint(3, 2), true),
            ]));
        var after = runtime.Capture();

        Assert.False(receipt.Applied);
        Assert.Equal(before.Snapshot.Revision, after.Snapshot.Revision);
        Assert.Equal(before.Snapshot.TopologyRevision, after.Snapshot.TopologyRevision);
        Assert.Equal(before.Snapshot.CurrentMapManifestDigest, after.Snapshot.CurrentMapManifestDigest);
        Assert.Equal(
            MapManifestCodec.Encode(before.Snapshot.CurrentMap),
            MapManifestCodec.Encode(after.Snapshot.CurrentMap));
        Assert.Equal(before.Events, after.Events);
    }

    [Fact]
    public void UnapprovedAssetReferenceRejectsTheEntirePausedAuthoringBatch()
    {
        var runtime = new OwnerWorldRuntime("camp-alpha");
        Assert.True(runtime.Pause("owner-device:alice"));
        var before = runtime.Capture();

        var receipt = runtime.ApplyAuthoringBatch(new OwnerAuthoringBatch(
            "unapproved-asset-is-atomic",
            [
                new SetWeatherOperation("rain"),
                new AddApprovedAssetReferenceOperation("portrait-alice", "sha256:untrusted"),
            ],
            "owner-device:alice"));
        var after = runtime.Capture();

        Assert.False(receipt.Applied);
        Assert.Contains("not approved", receipt.Failure, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before.Snapshot.Revision, after.Snapshot.Revision);
        Assert.Equal(before.Snapshot.Climate, after.Snapshot.Climate);
        Assert.Empty(after.Snapshot.ApprovedAssetReferences);
        Assert.Equal(before.Events, after.Events);
    }

    [Fact]
    public void ServerSuppliedAssetPolicyAllowsOnlyItsExactReference()
    {
        var approved = new OwnerApprovedAssetReference("portrait-alice", "sha256:approved");
        var runtime = new OwnerWorldRuntime(
            "camp-alpha",
            new AllowListedAssetReferencePolicy(approved));
        Assert.True(runtime.Pause("owner-device:alice"));

        var applied = runtime.ApplyAuthoringBatch(new OwnerAuthoringBatch(
            "approved-asset",
            [new AddApprovedAssetReferenceOperation(approved.AssetId, approved.AssetDigest)],
            "owner-device:alice"));
        var beforeRejectedRetry = runtime.Capture();
        var rejected = runtime.ApplyAuthoringBatch(new OwnerAuthoringBatch(
            "wrong-asset-digest",
            [new AddApprovedAssetReferenceOperation("portrait-alice-copy", "sha256:different")],
            "owner-device:alice"));
        var afterRejectedRetry = runtime.Capture();

        Assert.True(applied.Applied, applied.Failure);
        Assert.Equal(approved, Assert.Single(beforeRejectedRetry.Snapshot.ApprovedAssetReferences));
        Assert.False(rejected.Applied);
        Assert.Contains("not approved", rejected.Failure, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(beforeRejectedRetry.Snapshot.Revision, afterRejectedRetry.Snapshot.Revision);
        Assert.Equal(beforeRejectedRetry.Events, afterRejectedRetry.Events);
        Assert.Equal(approved, Assert.Single(afterRejectedRetry.Snapshot.ApprovedAssetReferences));
    }

    [Fact]
    public void AuthoringIsRejectedWhileTheWorldIsRunning()
    {
        var runtime = new OwnerWorldRuntime("camp-alpha");
        var before = runtime.Capture();
        var water = before.Snapshot.CurrentMap.Tiles.Single(tile => tile.Terrain == TerrainKind.Water).Position;

        var receipt = runtime.ApplyAuthoringBatch(new OwnerAuthoringBatch(
            "must-pause-first",
            [new SetTerrainOperation(water, TerrainKind.Mountain)]));
        var after = runtime.Capture();

        Assert.False(receipt.Applied);
        Assert.Contains("paused", receipt.Failure, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before.Snapshot.Revision, after.Snapshot.Revision);
        Assert.Equal(before.Snapshot.CurrentMapManifestDigest, after.Snapshot.CurrentMapManifestDigest);
        Assert.Empty(after.Events);
    }

    [Fact]
    public void AppliedAuthoringBatchIsIdempotentAndRetainsTheServerStampedIssuer()
    {
        var runtime = new OwnerWorldRuntime("camp-alpha");
        var water = runtime.Capture().Snapshot.CurrentMap.Tiles.Single(tile => tile.Terrain == TerrainKind.Water).Position;
        Assert.True(runtime.Pause("owner-device:first"));
        var request = new OwnerAuthoringBatch(
            "same-authoring-request",
            [new SetTerrainOperation(water, TerrainKind.Mountain)],
            "owner-device:first");

        var first = runtime.ApplyAuthoringBatch(request);
        var afterFirst = runtime.Capture();
        var replay = runtime.ApplyAuthoringBatch(request);
        var afterReplay = runtime.Capture();
        var collision = runtime.ApplyAuthoringBatch(request with
        {
            Operations = [new SetTerrainOperation(water, TerrainKind.Meadow)],
        });

        Assert.True(first.Applied, first.Failure);
        Assert.Equal(first, replay);
        Assert.Equal(afterFirst.Snapshot.Revision, afterReplay.Snapshot.Revision);
        Assert.Equal(afterFirst.Events, afterReplay.Events);
        Assert.Contains("issuer=owner-device:first", afterReplay.Events[^1].Detail, StringComparison.Ordinal);
        Assert.False(collision.Applied);
        Assert.Contains("cannot be reused", collision.Failure, StringComparison.Ordinal);
    }

    [Fact]
    public void RestoreFailsClosedWhenNoPolicyApprovesPersistedAssetReferences()
    {
        var approvedAsset = new OwnerApprovedAssetReference("portrait-lena", "sha256:portrait-lena");
        var runtime = new OwnerWorldRuntime(
            "camp-alpha",
            new AllowListedAssetReferencePolicy(approvedAsset));
        Assert.True(runtime.Pause("owner-device:alice"));
        Assert.True(runtime.ApplyAuthoringBatch(new OwnerAuthoringBatch(
            "persisted-approved-asset",
            [new AddApprovedAssetReferenceOperation(approvedAsset.AssetId, approvedAsset.AssetDigest)],
            "owner-device:alice")).Applied);

        var exception = Assert.Throws<InvalidDataException>(() =>
            OwnerWorldRuntime.Restore(runtime.ExportState(), "camp-alpha"));

        Assert.Contains("saved authoring operations", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RestoreRejectsTamperedRuntimeState()
    {
        var runtime = new OwnerWorldRuntime("camp-alpha");
        _ = runtime.SubmitInstruction(new OwnerInstructionRequest(
            "tamper-target",
            "owner-device:alice",
            "actor-scout",
            OwnerInstructionKind.Suggestive,
            "Observe the horizon."));
        var exported = runtime.ExportState();
        var invalidCounter = exported with
        {
            NextGlobalEventId = exported.NextGlobalEventId + 1,
        };
        var invalidMap = exported with
        {
            CurrentMap = exported.CurrentMap with { ManifestDigest = "sha256:tampered" },
        };

        Assert.Throws<InvalidDataException>(() => OwnerWorldRuntime.Restore(invalidCounter, "camp-alpha"));
        Assert.Throws<InvalidDataException>(() => OwnerWorldRuntime.Restore(invalidMap, "camp-alpha"));
    }

    [Fact]
    public void RemoveResourceOperationAppliesOnceAndCanBeRetried()
    {
        var runtime = new OwnerWorldRuntime("camp-alpha");
        const string resourceId = "temporary-food";
        var batch = new OwnerAuthoringBatch(
            "remove-resource-once",
            [
                new PlaceResourceOperation(resourceId, "food", new GridPoint(3, 2), true),
                new RemoveResourceOperation(resourceId),
            ],
            "owner-device:alice");
        Assert.True(runtime.Pause("owner-device:alice"));

        var applied = runtime.ApplyAuthoringBatch(batch);

        Assert.True(applied.Applied, applied.Failure);
        Assert.DoesNotContain(
            runtime.Capture().Snapshot.CurrentMap.Resources,
            resource => string.Equals(resource.Id, resourceId, StringComparison.Ordinal));
        Assert.Equal(applied, runtime.ApplyAuthoringBatch(batch));
    }

    private sealed class AllowListedAssetReferencePolicy : IOwnerApprovedAssetReferencePolicy
    {
        private readonly HashSet<OwnerApprovedAssetReference> approvedReferences;

        public AllowListedAssetReferencePolicy(params OwnerApprovedAssetReference[] approvedReferences)
        {
            ArgumentNullException.ThrowIfNull(approvedReferences);
            this.approvedReferences = new HashSet<OwnerApprovedAssetReference>(approvedReferences);
        }

        public bool IsApproved(OwnerApprovedAssetReference reference)
        {
            ArgumentNullException.ThrowIfNull(reference);
            return approvedReferences.Contains(reference);
        }
    }
}

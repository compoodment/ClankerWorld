using System.Text.Json;
using ClankerWorld.GodotClient.UI;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class ObserverTimelineRecoveryTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PassiveDeviceRecoversAfterAnotherDeviceLoadsOrSelectsAWorld(bool sameWorld)
    {
        using var runtime = new PrivateWorldRuntime("cross-device-world-a",
            _ => new ActionCoverageRecorder(chooseIdle: true));
        var earlier = runtime.ExportState();
        for (var tick = 0; tick < 5; tick++)
            Assert.True((await runtime.AdvanceOneTickAsync()).Advanced);
        runtime.Pause();
        var store = new OwnerWorldObservationStore(runtime);
        var initiator = new OwnerWorldObservationSession();
        var passive = new OwnerWorldObservationSession();
        AcceptInitial(initiator, store);
        AcceptInitial(passive, store);
        var held = passive.Current;
        var request = passive.CaptureRequest();
        var obsolete = Reconnect(store, request, passive);
        using var other = new PrivateWorldRuntime("cross-device-world-b");

        await initiator.ChangeTimelineAsync(() =>
        {
            if (sameWorld) runtime.LoadPausedCheckpoint(earlier);
            else runtime.SwitchPausedWorld(other.ExportState());
            return Task.CompletedTask;
        });

        var changed = Reconnect(store, request, passive);
        Assert.True(changed.Baseline.Snapshot.LatestEventId < request.AfterEventId);
        Assert.False(passive.TryAccept(changed, request, out _, out _));
        Assert.Same(held, passive.Current);
        Assert.True(passive.AwaitingFreshBaseline);
        Assert.Equal(0, passive.EventCursor);
        Assert.True(passive.RequestGeneration > request.Generation);
        Assert.False(passive.TryAccept(obsolete, request, out _, out _));
        var freshRequest = passive.CaptureRequest();
        Assert.True(freshRequest.RequiresFullBaseline);
        Assert.Equal(0, freshRequest.AfterEventId);
        var fresh = Reconnect(store, freshRequest, passive);
        Assert.True(passive.TryAccept(fresh, freshRequest, out var failure, out var timelineChanged), failure);
        Assert.True(timelineChanged);
        Assert.False(passive.AwaitingFreshBaseline);
        Assert.Equal(runtime.Society.WorldId, passive.Current!.Baseline.Snapshot.WorldId);
        Assert.Equal(runtime.WorldTick, passive.Current.Baseline.Snapshot.WorldTick);
        Assert.Equal(fresh.Baseline.Snapshot.LatestEventId, passive.EventCursor);
        runtime.Validate();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task SameWorldLoadRequiresFreshBaselineEvenWithoutRegressingTickOrEventIds(int additionalTicks)
    {
        using var runtime = new PrivateWorldRuntime("same-world-timeline", _ => new ActionCoverageRecorder(chooseIdle: true));
        runtime.Pause();
        var store = new OwnerWorldObservationStore(runtime);
        var session = new OwnerWorldObservationSession();
        AcceptInitial(session, store);
        var held = session.Current!;
        using var future = PrivateWorldRuntime.Restore(runtime.ExportState(), _ => new ActionCoverageRecorder(chooseIdle: true));
        if (additionalTicks > 0)
        {
            future.Resume();
            for (var tick = 0; tick < additionalTicks; tick++) Assert.True((await future.AdvanceOneTickAsync()).Advanced);
            future.Pause();
        }
        var request = session.CaptureRequest();

        runtime.LoadPausedCheckpoint(future.ExportState());

        var response = Reconnect(store, request, session);
        Assert.Equal(held.Baseline.Snapshot.WorldId, response.Baseline.Snapshot.WorldId);
        Assert.True(response.Baseline.Snapshot.WorldTick >= held.Baseline.Snapshot.WorldTick);
        Assert.True(response.Baseline.Snapshot.LatestEventId >= held.Baseline.Snapshot.LatestEventId);
        if (additionalTicks == 0)
        {
            Assert.Equal(held.Baseline.Snapshot.WorldTick, response.Baseline.Snapshot.WorldTick);
            Assert.Equal(held.Baseline.Snapshot.LatestEventId, response.Baseline.Snapshot.LatestEventId);
        }
        Assert.NotEqual(held.Baseline.Timeline, response.Baseline.Timeline);
        Assert.False(session.TryAccept(response, request, out _, out _));
        Assert.Same(held, session.Current);
        AcceptRecovery(session, store);
        Assert.Equal(response.Baseline.Timeline, session.Timeline);
    }

    [Fact]
    public void NewerRecoverySupersedesOldRequestsAndRetiredHostInstances()
    {
        using var runtime = new PrivateWorldRuntime("timeline-response-order");
        runtime.Pause();
        var saved = runtime.ExportState();
        var store = new OwnerWorldObservationStore(runtime);
        var session = new OwnerWorldObservationSession();
        AcceptInitial(session, store);
        var originalRequest = session.CaptureRequest();
        var originalResponse = Reconnect(store, originalRequest, session);
        runtime.LoadPausedCheckpoint(saved);
        Assert.False(session.TryAccept(Reconnect(store, originalRequest, session), originalRequest, out _, out _));
        var firstRecoveryRequest = session.CaptureRequest();
        var firstRecoveryResponse = Reconnect(store, firstRecoveryRequest, session);
        runtime.LoadPausedCheckpoint(saved);
        Assert.True(session.TryAccept(Reconnect(store, firstRecoveryRequest, session), firstRecoveryRequest,
            out var failure, out var changed), failure);
        Assert.True(changed);
        Assert.False(session.TryAccept(firstRecoveryResponse, firstRecoveryRequest, out _, out _));
        var held = session.Current;
        var generation = session.RequestGeneration;
        Assert.False(session.TryAccept(originalResponse, originalRequest, out _, out _));
        // Even a response attached to a current request cannot revive an older host generation.
        Assert.False(session.TryAccept(originalResponse, session.CaptureRequest(), out _, out _));
        Assert.Same(held, session.Current);
        Assert.Equal(generation, session.RequestGeneration);

        using var restarted = PrivateWorldRuntime.Restore(saved);
        var restartedStore = new OwnerWorldObservationStore(restarted);
        var oldInstanceRequest = session.CaptureRequest();
        var oldInstanceResponse = Reconnect(store, oldInstanceRequest, session);
        Assert.False(session.TryAccept(Reconnect(restartedStore, oldInstanceRequest, session), oldInstanceRequest, out _, out _));
        AcceptRecovery(session, restartedStore);
        Assert.NotEqual(oldInstanceResponse.Baseline.Timeline!.InstanceId, session.Timeline!.InstanceId);
        generation = session.RequestGeneration;
        held = session.Current;
        Assert.False(session.TryAccept(oldInstanceResponse, session.CaptureRequest(), out _, out _));
        Assert.Equal(generation, session.RequestGeneration);
        Assert.Same(held, session.Current);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InitiatingDeviceAcceptsItsFirstCompleteBaselineEvenWhenLoadReceiptIsLost(bool loseReceipt)
    {
        using var runtime = new PrivateWorldRuntime("timeline-own-load");
        runtime.Pause();
        var saved = runtime.ExportState();
        var store = new OwnerWorldObservationStore(runtime);
        var session = new OwnerWorldObservationSession();
        AcceptInitial(session, store);
        var oldRequest = session.CaptureRequest();
        var oldResponse = Reconnect(store, oldRequest, session);
        Task Load()
        {
            runtime.LoadPausedCheckpoint(saved);
            return loseReceipt ? Task.FromException(new IOException("response lost after commit")) : Task.CompletedTask;
        }
        if (loseReceipt) await Assert.ThrowsAsync<IOException>(() => session.ChangeTimelineAsync(Load));
        else await session.ChangeTimelineAsync(Load);
        Assert.True(session.AwaitingFreshBaseline);
        var request = session.CaptureRequest();
        Assert.True(request.RequiresFullBaseline);
        Assert.Equal(0, request.AfterEventId);
        Assert.True(session.TryAccept(Reconnect(store, request, session), request, out var failure, out var changed), failure);
        Assert.True(changed);
        Assert.False(session.TryAccept(oldResponse, oldRequest, out _, out _));
    }

    [Fact]
    public async Task OlderRepliesOnTheSameTimelineCannotReplaceNewerAcceptedState()
    {
        using var runtime = new PrivateWorldRuntime("timeline-out-of-order", _ => new ActionCoverageRecorder(chooseIdle: true));
        var store = new OwnerWorldObservationStore(runtime);
        var session = new OwnerWorldObservationSession();
        AcceptInitial(session, store);
        var request = session.CaptureRequest();
        Assert.True((await runtime.AdvanceOneTickAsync()).Advanced);
        var older = Reconnect(store, request, session);
        Assert.True((await runtime.AdvanceOneTickAsync()).Advanced);
        var newer = Reconnect(store, request, session);
        Assert.True(session.TryAccept(newer, request, out var failure, out var changed), failure);
        Assert.False(changed);
        Assert.Equal(request.Generation, session.RequestGeneration);
        Assert.False(session.TryAccept(older, request, out _, out _));
        Assert.Same(newer, session.Current);
        Assert.False(session.AwaitingFreshBaseline);
    }

    [Fact]
    public void AdvertisedTimelineCannotDisappearOrDowngradeAfterAcceptance()
    {
        using var runtime = new PrivateWorldRuntime("timeline-required-metadata");
        var store = new OwnerWorldObservationStore(runtime);
        var session = new OwnerWorldObservationSession();
        AcceptInitial(session, store);
        var held = session.Current;
        var request = session.CaptureRequest();
        var response = Reconnect(store, request, session);
        var withoutCapability = response.Handshake with
        {
            ServerCapabilities = response.Handshake.ServerCapabilities.Where(value => value != "owner-observation-timeline.v1").ToArray(),
        };
        OwnerWorldReconnect[] invalid =
        [
            response with { Baseline = response.Baseline with { Timeline = null } },
            response with { Handshake = withoutCapability },
            response with { Handshake = withoutCapability, Baseline = response.Baseline with { Timeline = null } },
            response with { Baseline = response.Baseline with { Timeline = response.Baseline.Timeline! with { Generation = -1 } } },
        ];
        foreach (var item in invalid)
        {
            Assert.False(session.TryAccept(item, request, out var failure, out _));
            Assert.Contains("world update", failure, StringComparison.Ordinal);
            Assert.Same(held, session.Current);
            Assert.False(session.AwaitingFreshBaseline);
        }
    }

    [Fact]
    public void ChangedTimelineCannotBorrowTerrainOrLayersFromTheHeldWorld()
    {
        var geography = new GeographyOptions("timeline-map-cache", WorldSizePreset.Small);
        using var runtime = new PrivateWorldRuntime(geography.Seed, startPace: WorldStartPace.FounderSetup,
            geographyOptions: geography);
        var store = new OwnerWorldObservationStore(runtime);
        var session = new OwnerWorldObservationSession();
        AcceptInitial(session, store);
        Assert.NotNull(session.Current!.Baseline.Snapshot.PackedTerrain);
        Assert.NotNull(session.Current.Baseline.Snapshot.PackedMapLayers);
        var request = session.CaptureRequest();
        runtime.LoadPausedCheckpoint(runtime.ExportState());
        var cachedResponse = Reconnect(store, request, session);
        Assert.Null(cachedResponse.Baseline.Snapshot.PackedTerrain);
        Assert.Null(cachedResponse.Baseline.Snapshot.PackedMapLayers);
        Assert.False(session.TryAccept(cachedResponse, request, out _, out _));
        var freshRequest = session.CaptureRequest();
        var fresh = Reconnect(store, freshRequest, session);
        Assert.NotNull(fresh.Baseline.Snapshot.PackedTerrain);
        Assert.NotNull(fresh.Baseline.Snapshot.PackedMapLayers);
        Assert.False(session.TryAccept(fresh with
        {
            Baseline = fresh.Baseline with { Snapshot = fresh.Baseline.Snapshot with { PackedTerrain = null, Tiles = [] } },
        }, freshRequest, out _, out _));
        Assert.False(session.TryAccept(fresh with
        {
            Baseline = fresh.Baseline with { Snapshot = fresh.Baseline.Snapshot with { PackedMapLayers = null } },
        }, freshRequest, out _, out _));
        Assert.True(session.TryAccept(fresh, freshRequest, out var failure, out var changed), failure);
        Assert.True(changed);
    }

    private static void AcceptInitial(OwnerWorldObservationSession session, OwnerWorldObservationStore store)
    {
        var request = session.CaptureRequest();
        Assert.True(session.TryAccept(Reconnect(store, request, session), request, out var failure, out _), failure);
    }

    private static void AcceptRecovery(OwnerWorldObservationSession session, OwnerWorldObservationStore store)
    {
        var request = session.CaptureRequest();
        Assert.Equal(0, request.AfterEventId);
        Assert.True(request.RequiresFullBaseline);
        Assert.True(session.TryAccept(Reconnect(store, request, session), request, out var failure, out var changed), failure);
        Assert.True(changed);
    }

    private static OwnerWorldReconnect Reconnect(OwnerWorldObservationStore store,
        OwnerObservationRequestContext request, OwnerWorldObservationSession session)
    {
        var cached = request.RequiresFullBaseline ? null : session.Current?.Baseline.Snapshot;
        var actual = new ViewerOwnerReconnect(store.GetOwnerHandshake(), store.GetReconnectBaseline(request.AfterEventId,
            cached?.WorldId, cached?.MapManifestDigest, cached?.MapLayersDigest));
        return JsonSerializer.Deserialize<OwnerWorldReconnect>(JsonSerializer.Serialize(actual, JsonOptions), JsonOptions)!;
    }
}

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ClankerWorld.GodotClient.ClientState;
using ClankerWorld.GodotClient.Pairing;

namespace ClankerWorld.GodotClient.UI;

public sealed record OwnerObservationRequestContext(long Generation, long AfterEventId, bool RequiresFullBaseline);

/// <summary>
/// Checks a complete signed owner baseline before giving it to the renderer.
/// A failure deliberately retains the last accepted observation, just as a
/// networked client must never invent state during a broken refresh.
/// </summary>
public sealed class OwnerWorldObservationSession
{
    private const string TimelineCapability = "owner-observation-timeline.v1";
    private readonly HashSet<string> retiredInstances = new(StringComparer.Ordinal);
    private OwnerObserverTimeline? pendingTimeline;

    private static readonly string[] RequiredCapabilities =
    [
        "owner-observation.read.v1",
        "inhabitant-inspection.read.v1",
        "spatial-knowledge.read.v1",
        "owner-control.request.v1",
        "paused-authoring.request.v1",
    ];

    public OwnerWorldReconnect? Current { get; private set; }

    public OwnerDeviceRegistration? Registration { get; private set; }

    public long RequestGeneration { get; private set; }

    public bool AwaitingFreshBaseline { get; private set; }

    public OwnerObserverTimeline? Timeline => Current?.Baseline.Timeline;

    /// <summary>Explicit registration replacement starts a fresh host observation timeline.</summary>
    public void ReplaceRegistration(OwnerDeviceRegistration? registration)
    {
        Registration = registration;
        Current = null;
        pendingTimeline = null;
        retiredInstances.Clear();
        AwaitingFreshBaseline = false;
        RequestGeneration++;
    }

    public long EventCursor => AwaitingFreshBaseline ? 0 : Current?.Baseline.Snapshot.LatestEventId ?? 0;

    public OwnerObservationRequestContext CaptureRequest() =>
        new(RequestGeneration, EventCursor, AwaitingFreshBaseline || Current is null);

    /// <summary>A possible rewind starts a new observation timeline, even if its receipt is lost.</summary>
    public void ResetAfterLoad()
    {
        RequestGeneration++;
        AwaitingFreshBaseline = Timeline is not null || pendingTimeline is not null;
        if (!AwaitingFreshBaseline)
        {
            Current = null;
            pendingTimeline = null;
        }
    }

    public async Task ChangeTimelineAsync(Func<Task> mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        ResetAfterLoad();
        await mutation();
    }

    public bool TryAccept(OwnerWorldReconnect response, long requestedAfterEventId, out string failure) =>
        TryAccept(response, new(RequestGeneration, requestedAfterEventId, AwaitingFreshBaseline), out failure, out _);

    public bool TryAccept(OwnerWorldReconnect response, OwnerObservationRequestContext request,
        out string failure, out bool timelineChanged)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(request);
        timelineChanged = false;
        var requestedAfterEventId = request.AfterEventId;
        ArgumentOutOfRangeException.ThrowIfNegative(requestedAfterEventId);
        if (request.Generation != RequestGeneration)
        {
            failure = "The world changed while this reply was arriving.";
            return false;
        }
        if (response.Handshake?.Protocol is null || response.Handshake.Protocol.Major != 1)
        {
            failure = "The owner observation protocol major is unsupported.";
            return false;
        }

        var capabilities = (response.Handshake.ServerCapabilities ?? []).ToHashSet(StringComparer.Ordinal);
        if (RequiredCapabilities.Any(capability => !capabilities.Contains(capability)))
        {
            failure = "The server did not advertise the required paired-owner capabilities.";
            return false;
        }

        var baseline = response.Baseline;
        if (baseline?.Snapshot is null || baseline.Events is null)
        {
            failure = "The owner reconnect baseline is internally inconsistent.";
            return false;
        }

        var timeline = baseline.Timeline;
        if (capabilities.Contains(TimelineCapability) != (timeline is not null) ||
            timeline is { IsValid: false } || (Timeline is not null || pendingTimeline is not null) && timeline is null)
        {
            failure = "The host sent an incomplete world update. Keeping the last confirmed view.";
            return false;
        }
        var expectedTimeline = pendingTimeline ?? Timeline;
        if (timeline is not null &&
            (retiredInstances.Contains(timeline.InstanceId) ||
             expectedTimeline is not null && timeline.InstanceId == expectedTimeline.InstanceId &&
             timeline.Generation < expectedTimeline.Generation))
        {
            failure = "This reply belongs to an earlier world state.";
            return false;
        }
        if (timeline != expectedTimeline && (Current is not null || expectedTimeline is not null))
        {
            if (expectedTimeline is not null && expectedTimeline.InstanceId != timeline?.InstanceId)
                retiredInstances.Add(expectedTimeline.InstanceId);
            pendingTimeline = timeline;
            AwaitingFreshBaseline = true;
            RequestGeneration++;
            if (!request.RequiresFullBaseline || requestedAfterEventId != 0)
            {
                failure = "The world changed. Refreshing the view.";
                return false;
            }
        }

        if (AwaitingFreshBaseline && (!request.RequiresFullBaseline || requestedAfterEventId != 0))
        {
            failure = "The world changed. Waiting for a complete update.";
            return false;
        }
        if (baseline.Snapshot.WorldTick < 0 || baseline.Snapshot.LatestEventId < 0 ||
            baseline.Events.AfterEventId != requestedAfterEventId ||
            baseline.Events.SnapshotTick != baseline.Snapshot.WorldTick ||
            baseline.Snapshot.LatestEventId < requestedAfterEventId ||
            baseline.Events.EventHistoryFloor < 0 ||
            baseline.Events.EventHistoryFloor > baseline.Snapshot.LatestEventId ||
            baseline.Events.ResetRequired != (requestedAfterEventId < baseline.Events.EventHistoryFloor))
        {
            failure = "The owner reconnect baseline is internally inconsistent.";
            return false;
        }

        var previousEventId = Math.Max(requestedAfterEventId, baseline.Events.EventHistoryFloor);
        foreach (var worldEvent in baseline.Events.Events ?? [])
        {
            if (worldEvent is null || previousEventId == long.MaxValue || worldEvent.EventId != previousEventId + 1 ||
                worldEvent.EventId > baseline.Snapshot.LatestEventId ||
                worldEvent.WorldTick > baseline.Snapshot.WorldTick)
            {
                failure = "The owner reconnect event suffix is not ordered against its snapshot.";
                return false;
            }

            previousEventId = worldEvent.EventId;
        }

        if (previousEventId != baseline.Snapshot.LatestEventId)
        {
            failure = "The owner reconnect event suffix is incomplete for its snapshot.";
            return false;
        }

        if (Current is { } current && timeline == Timeline &&
            (baseline.Snapshot.WorldTick < current.Baseline.Snapshot.WorldTick ||
             baseline.Snapshot.LatestEventId < current.Baseline.Snapshot.LatestEventId ||
             baseline.Snapshot.WorldId != current.Baseline.Snapshot.WorldId))
        {
            failure = "The owner reconnect response regresses the held world state.";
            return false;
        }

        if (baseline.Snapshot.Tiles is null || request.RequiresFullBaseline &&
            (baseline.Snapshot.PackedTerrain is null && baseline.Snapshot.Tiles.Count == 0 ||
             baseline.Snapshot.MapLayersDigest is not null && baseline.Snapshot.PackedMapLayers is null))
        {
            failure = "The world update is missing its map. Keeping the last confirmed view.";
            return false;
        }
        var cachedSnapshot = AwaitingFreshBaseline ? null : Current?.Baseline.Snapshot;
        if (baseline.Snapshot.PackedTerrain is null && baseline.Snapshot.Tiles.Count == 0)
        {
            var cached = cachedSnapshot;
            if (!capabilities.Contains("owner-terrain-delta.v1") || cached?.PackedTerrain is null ||
                !string.Equals(cached.WorldId, baseline.Snapshot.WorldId, StringComparison.Ordinal) ||
                !string.Equals(cached.MapManifestDigest, baseline.Snapshot.MapManifestDigest, StringComparison.Ordinal))
            {
                failure = "The owner terrain cache cannot satisfy this reconnect baseline.";
                return false;
            }
            var merged = baseline.Snapshot with
            {
                PackedTerrain = cached.PackedTerrain,
                PackedMapLayers = baseline.Snapshot.PackedMapLayers ??
                    (baseline.Snapshot.MapLayersDigest is null ||
                     string.Equals(baseline.Snapshot.MapLayersDigest, cached.MapLayersDigest, StringComparison.Ordinal)
                        ? cached.PackedMapLayers : null),
            };
            response = response with { Baseline = baseline with { Snapshot = merged } };
        }

        baseline = response.Baseline;
        if (baseline.Snapshot.MapLayersDigest is { } expectedLayersDigest &&
            baseline.Snapshot.PackedMapLayers is null)
        {
            var cached = cachedSnapshot;
            if (cached?.PackedMapLayers is null ||
                !string.Equals(cached.WorldId, baseline.Snapshot.WorldId, StringComparison.Ordinal) ||
                !string.Equals(cached.MapLayersDigest, expectedLayersDigest, StringComparison.Ordinal))
            {
                failure = "The owner map-layer cache cannot satisfy this reconnect baseline.";
                return false;
            }
            var merged = baseline.Snapshot with { PackedMapLayers = cached.PackedMapLayers };
            response = response with { Baseline = baseline with { Snapshot = merged } };
        }

        timelineChanged = AwaitingFreshBaseline;
        if (timelineChanged || Current is null && timeline is not null) RequestGeneration++;
        Current = response;
        pendingTimeline = null;
        AwaitingFreshBaseline = false;
        failure = string.Empty;
        return true;
    }
}

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ClankerWorld.GodotClient.ClientState;
using ClankerWorld.GodotClient.Pairing;

namespace ClankerWorld.GodotClient.UI;

/// <summary>
/// Checks a complete signed owner baseline before giving it to the renderer.
/// A failure deliberately retains the last accepted observation, just as a
/// networked client must never invent state during a broken refresh.
/// </summary>
public sealed class OwnerWorldObservationSession
{
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

    /// <summary>Explicit registration replacement starts a fresh host observation timeline.</summary>
    public void ReplaceRegistration(OwnerDeviceRegistration? registration)
    {
        Registration = registration;
        ResetAfterLoad();
    }

    public long EventCursor => Current?.Baseline.Snapshot.LatestEventId ?? 0;

    /// <summary>A possible rewind starts a new observation timeline, even if its receipt is lost.</summary>
    public void ResetAfterLoad() => Current = null;

    public async Task ChangeTimelineAsync(Func<Task> mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        ResetAfterLoad();
        await mutation();
    }

    public bool TryAccept(OwnerWorldReconnect response, long requestedAfterEventId, out string failure)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentOutOfRangeException.ThrowIfNegative(requestedAfterEventId);
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
        if (baseline?.Snapshot is null || baseline.Events is null ||
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

        var expectedEventId = checked(Math.Max(requestedAfterEventId, baseline.Events.EventHistoryFloor) + 1);
        foreach (var worldEvent in baseline.Events.Events ?? [])
        {
            if (worldEvent.EventId != expectedEventId ||
                worldEvent.EventId > baseline.Snapshot.LatestEventId ||
                worldEvent.WorldTick > baseline.Snapshot.WorldTick)
            {
                failure = "The owner reconnect event suffix is not ordered against its snapshot.";
                return false;
            }

            expectedEventId = checked(expectedEventId + 1);
        }

        if (expectedEventId - 1 != baseline.Snapshot.LatestEventId)
        {
            failure = "The owner reconnect event suffix is incomplete for its snapshot.";
            return false;
        }

        if (Current is { } current &&
            (baseline.Snapshot.WorldTick < current.Baseline.Snapshot.WorldTick ||
             baseline.Snapshot.LatestEventId < current.Baseline.Snapshot.LatestEventId))
        {
            failure = "The owner reconnect response regresses the held world state.";
            return false;
        }

        if (baseline.Snapshot.PackedTerrain is null && baseline.Snapshot.Tiles.Count == 0)
        {
            var cached = Current?.Baseline.Snapshot;
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
            var cached = Current?.Baseline.Snapshot;
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

        Current = response;
        failure = string.Empty;
        return true;
    }
}

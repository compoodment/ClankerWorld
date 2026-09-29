using ClankerWorld.GodotClient.Protocol;

namespace ClankerWorld.Simulation.Tests;

public sealed class GodotWorldObservationProtocolTests
{
    [Fact]
    public void AcceptsCoherentReadOnlyReconnectBaselineAndAdvancesCursor()
    {
        var session = new WorldObservationSession();
        var observation = CreateValidObservation();

        var accepted = session.TryAccept(observation, out var failure);

        Assert.True(accepted, failure);
        Assert.Same(observation, session.Current);
        Assert.Equal(5, session.EventCursor);
    }

    [Fact]
    public void RejectsMalformedRefreshAndKeepsLastAcceptedObservation()
    {
        var session = new WorldObservationSession();
        var accepted = CreateValidObservation();
        Assert.True(session.TryAccept(accepted, out _));

        var malformed = accepted with
        {
            Baseline = accepted.Baseline with
            {
                Events = accepted.Baseline.Events with { SnapshotTick = 4 },
            },
        };

        var wasAccepted = session.TryAccept(malformed, out var failure);

        Assert.False(wasAccepted);
        Assert.Contains("coherent snapshot", failure, StringComparison.Ordinal);
        Assert.Same(accepted, session.Current);
        Assert.Equal(5, session.EventCursor);
    }

    [Fact]
    public void RejectsIncompatibleProtocolMajorWithoutDiscardingPriorWorld()
    {
        var session = new WorldObservationSession();
        var accepted = CreateValidObservation();
        Assert.True(session.TryAccept(accepted, out _));

        var incompatible = accepted with
        {
            Handshake = accepted.Handshake with { Protocol = new WorldProtocolVersion(2, 0) },
        };

        var wasAccepted = session.TryAccept(incompatible, out var failure);

        Assert.False(wasAccepted);
        Assert.Contains("not supported", failure, StringComparison.Ordinal);
        Assert.Same(accepted, session.Current);
    }

    [Fact]
    public void RejectsIncompleteOrWrongCursorReconnectAndKeepsLastWorld()
    {
        var session = new WorldObservationSession();
        var accepted = CreateValidObservation();
        Assert.True(session.TryAccept(accepted, expectedAfterEventId: 3, out _));

        var incomplete = accepted with
        {
            Baseline = accepted.Baseline with
            {
                Events = accepted.Baseline.Events with { Events = [accepted.Baseline.Events.Events[0]] },
            },
        };

        Assert.False(session.TryAccept(incomplete, expectedAfterEventId: 3, out var incompleteFailure));
        Assert.Contains("incomplete", incompleteFailure, StringComparison.Ordinal);
        Assert.Same(accepted, session.Current);

        Assert.False(session.TryAccept(accepted, expectedAfterEventId: 2, out var cursorFailure));
        Assert.Contains("coherent snapshot", cursorFailure, StringComparison.Ordinal);
        Assert.Same(accepted, session.Current);
    }

    [Fact]
    public void RejectsRegressedSnapshotWithoutDiscardingLastWorld()
    {
        var session = new WorldObservationSession();
        var accepted = CreateValidObservation();
        Assert.True(session.TryAccept(accepted, expectedAfterEventId: 3, out _));
        var regressed = accepted with
        {
            Baseline = accepted.Baseline with
            {
                Snapshot = accepted.Baseline.Snapshot with { WorldTick = 4, LatestEventId = 4 },
                Events = new WorldEventSlice(4, 3, [new WorldEvent(4, 4, "move", "south")]),
            },
        };

        var wasAccepted = session.TryAccept(regressed, expectedAfterEventId: 3, out var failure);

        Assert.False(wasAccepted);
        Assert.Contains("regresses", failure, StringComparison.Ordinal);
        Assert.Same(accepted, session.Current);
    }

    private static WorldObservation CreateValidObservation()
    {
        var handshake = new WorldHandshake(
            Protocol: new WorldProtocolVersion(1, 0),
            ServerCapabilities:
            [
                "event-replay.read.v1",
                "reconnect-baseline.read.v1",
                "snapshot.read.v1",
            ],
            ClientCapabilities: []);
        var snapshot = new WorldSnapshot(
            WorldId: "fixture-world",
            WorldTick: 5,
            MapManifestDigest: "fixture-map",
            Tiles: [new WorldTile(0, 0, "meadow")],
            Objects: [],
            Resources: [],
            Actor: new WorldActor("camp-alpha", new WorldPosition(0, 0), 5000, 1, 0),
            LatestEventId: 5);
        var events = new WorldEventSlice(
            SnapshotTick: 5,
            AfterEventId: 3,
            Events:
            [
                new WorldEvent(4, 4, "move", "north"),
                new WorldEvent(5, 5, "harvest", "berries"),
            ]);

        return new WorldObservation(handshake, new WorldReconnectBaseline(snapshot, events));
    }
}

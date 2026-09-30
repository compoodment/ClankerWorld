using ClankerWorld.GodotClient.UI;
using OwnerHttpBinding = ClankerWorld.Viewer.Control.OwnerHttpBinding;
using ServerReconnectAction = ClankerWorld.Viewer.Control.OwnerReconnectAction;
using ServerDeviceManagementAction = ClankerWorld.Viewer.Control.OwnerDeviceManagementAction;
using ServerInstructionAction = ClankerWorld.Viewer.Control.OwnerInstructionAction;
using ServerPairingApprovalAction = ClankerWorld.Viewer.Control.OwnerPairingApprovalAction;
using ServerProviderConfigurationAction = ClankerWorld.Viewer.Control.OwnerProviderConfigurationAction;
using ServerProviderModelListAction = ClankerWorld.Viewer.Control.OwnerProviderModelListAction;

namespace ClankerWorld.Simulation.Tests;

public sealed class GodotOwnerWorldApiTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RegistrationReplacementAcceptsYoungerHostWithoutReusingOldTerrain(bool forgetFirst)
    {
        var session = new OwnerWorldObservationSession();
        var priorRegistration = new ClankerWorld.GodotClient.ClientState.OwnerDeviceRegistration(
            new("old-server", "old-authority"), "old-device", "fixture-key", "http://127.0.0.1:5188/");
        session.ReplaceRegistration(priorRegistration);
        var old = CreateCoherentReconnect();
        Assert.True(session.TryAccept(old, 3, out _));
        Assert.Equal(5, session.EventCursor);
        if (forgetFirst)
        {
            session.ReplaceRegistration(null);
            Assert.Null(session.Current);
            Assert.Null(session.Registration);
            Assert.Equal(0, session.EventCursor);
        }
        var nextRegistration = priorRegistration with { Authority = new("new-server", "new-authority"), WorldUrl = "http://127.0.0.1:5189/" };
        session.ReplaceRegistration(nextRegistration);
        Assert.Same(nextRegistration, session.Registration);
        Assert.Null(session.Current);
        Assert.Equal(0, session.EventCursor);
        var fresh = old with
        {
            Baseline = new OwnerWorldReconnectBaseline(
                old.Baseline.Snapshot with { WorldId = "new-host-world", WorldTick = 0, LatestEventId = 0 },
                new OwnerWorldEventSlice(0, 0, []))
        };
        Assert.False(session.TryAccept(fresh with
        {
            Baseline = fresh.Baseline with
            { Snapshot = fresh.Baseline.Snapshot with { Tiles = [], PackedTerrain = null } }
        }, 0, out _));
        Assert.True(session.TryAccept(fresh, session.EventCursor, out var failure), failure);
        var advanced = fresh with
        {
            Baseline = old.Baseline with
            { Snapshot = old.Baseline.Snapshot with { WorldId = "new-host-world" } }
        };
        Assert.True(session.TryAccept(advanced, 3, out _));
        Assert.False(session.TryAccept(fresh, 0, out _)); // Same-timeline regression remains invalid.
    }

    [Theory]
    [InlineData("new-world")]
    [InlineData("earlier-existing-world")]
    public async Task LostSwitchReceiptStillAllowsTheActualEarlierTimeline(string selectedWorld)
    {
        var session = new OwnerWorldObservationSession();
        var old = CreateCoherentReconnect();
        Assert.True(session.TryAccept(old, 3, out _));
        var selected = old with
        {
            Baseline = new OwnerWorldReconnectBaseline(
                old.Baseline.Snapshot with { WorldId = selectedWorld, WorldTick = 0, LatestEventId = 0 },
                new OwnerWorldEventSlice(0, 0, []))
        };
        var committed = false;
        await Assert.ThrowsAsync<IOException>(() => session.ChangeTimelineAsync(() =>
        {
            Assert.Equal(0, session.EventCursor);
            committed = true;
            return Task.FromException(new IOException("response lost after commit"));
        }));
        Assert.True(committed);
        Assert.True(session.TryAccept(selected, session.EventCursor, out var failure), failure);
        Assert.Equal(selectedWorld, session.Current!.Baseline.Snapshot.WorldId);
        Assert.True(session.TryAccept(old, 3, out _));
        Assert.False(session.TryAccept(selected, 0, out _));
    }

    [Fact]
    public void CachedTerrainReconnectPayloadMatchesHostAndLegacyPayloadRemainsStable()
    {
        var digest = new string('a', 64);
        Assert.Equal(OwnerHttpBinding.ReconnectPayload(new ServerReconnectAction(5)),
            OwnerWorldActionPayload.Reconnect(new OwnerReconnectAction(5)));
        Assert.Equal(OwnerHttpBinding.ReconnectPayload(new ServerReconnectAction(5, "world-1", digest)),
            OwnerWorldActionPayload.Reconnect(new OwnerReconnectAction(5, "world-1", digest)));
        var mapLayersDigest = new string('b', 64);
        Assert.Equal(OwnerHttpBinding.ReconnectPayload(new ServerReconnectAction(5, "world-1", digest,
                mapLayersDigest)),
            OwnerWorldActionPayload.Reconnect(new OwnerReconnectAction(5, "world-1", digest,
                mapLayersDigest)));
    }

    [Fact]
    public void ReconnectReusesOnlyTheSameWorldAndManifestTerrain()
    {
        var first = CreateCoherentReconnect();
        var packed = new OwnerWorldPackedTerrain(1, 1, "terrain-kind-v1", "AA==");
        var packedLayers = new OwnerWorldPackedMapLayers(1, 1, "map-layers-v1",
            "AA==", "AQ==", "AA==", "AA==", "AQ==");
        var layerDigest = new string('b', 64);
        first = first with
        {
            Handshake = first.Handshake with
            {
                ServerCapabilities = [.. first.Handshake.ServerCapabilities, "owner-terrain-delta.v1"],
            },
            Baseline = first.Baseline with
            {
                Snapshot = first.Baseline.Snapshot with
                {
                    Tiles = [],
                    PackedTerrain = packed,
                    PackedMapLayers = packedLayers,
                    MapLayersDigest = layerDigest,
                },
            },
        };
        var session = new OwnerWorldObservationSession();
        Assert.True(session.TryAccept(first, 3, out var firstFailure), firstFailure);
        var delta = first with
        {
            Baseline = first.Baseline with
            {
                Snapshot = first.Baseline.Snapshot with
                {
                    Tiles = [],
                    PackedTerrain = null,
                    PackedMapLayers = null,
                    MapLayersDigest = layerDigest,
                },
                Events = first.Baseline.Events with { AfterEventId = 5, Events = [] },
            },
        };
        Assert.True(session.TryAccept(delta, 5, out var deltaFailure), deltaFailure);
        Assert.Same(packed, session.Current!.Baseline.Snapshot.PackedTerrain);
        Assert.Same(packedLayers, session.Current.Baseline.Snapshot.PackedMapLayers);
        Assert.Equal(layerDigest, session.Current.Baseline.Snapshot.MapLayersDigest);
        Assert.False(session.TryAccept(delta with
        {
            Baseline = delta.Baseline with
            {
                Snapshot = delta.Baseline.Snapshot with { MapLayersDigest = new string('c', 64) },
            },
        }, 5, out var layersFailure));
        Assert.Contains("map-layer cache", layersFailure, StringComparison.Ordinal);
        Assert.Same(packedLayers, session.Current.Baseline.Snapshot.PackedMapLayers);
        Assert.False(session.TryAccept(delta with
        {
            Baseline = delta.Baseline with
            {
                Snapshot = delta.Baseline.Snapshot with { MapManifestDigest = "changed-map" },
            },
        }, 5, out _));
        Assert.Same(packed, session.Current!.Baseline.Snapshot.PackedTerrain);
        session.ResetAfterLoad();
        Assert.False(session.TryAccept(delta, 5, out _));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1_460)]
    public void LifePacePayloadMatchesTheHostExactly(int rate)
    {
        Assert.Equal(OwnerHttpBinding.LifePacePayload(new ClankerWorld.Viewer.Control.OwnerLifePaceAction(rate)),
            OwnerWorldActionPayload.LifePace(new OwnerLifePaceAction(rate)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void JevAssistancePayloadMatchesTheHostExactly(bool enabled)
    {
        Assert.Equal(OwnerHttpBinding.JevAssistancePayload(new ClankerWorld.Viewer.Control.OwnerJevAssistanceAction(enabled)),
            OwnerWorldActionPayload.JevAssistance(new OwnerJevAssistanceAction(enabled)));
    }

    [Fact]
    public void AcceptsExplicitHistoryResetButRejectsSilentOrFalseReset()
    {
        var original = CreateCoherentReconnect();
        var response = original with
        {
            Baseline = original.Baseline with
            {
                Events = original.Baseline.Events with { AfterEventId = 0, EventHistoryFloor = 3, ResetRequired = true },
            },
        };
        var session = new OwnerWorldObservationSession();
        Assert.True(session.TryAccept(response, 0, out var failure), failure);
        Assert.Equal(5, session.EventCursor);
        Assert.False(new OwnerWorldObservationSession().TryAccept(response with
        {
            Baseline = response.Baseline with { Events = response.Baseline.Events with { ResetRequired = false } },
        }, 0, out _));
        Assert.False(new OwnerWorldObservationSession().TryAccept(response with
        {
            Baseline = response.Baseline with { Events = response.Baseline.Events with { EventHistoryFloor = 0 } },
        }, 0, out _));
    }

    [Fact]
    public void AcceptsCoherentPairedOwnerReconnectAndAdvancesCursor()
    {
        var session = new OwnerWorldObservationSession();
        var response = CreateCoherentReconnect();

        var accepted = session.TryAccept(response, requestedAfterEventId: 3, out var failure);

        Assert.True(accepted, failure);
        Assert.Same(response, session.Current);
        Assert.Equal(5, session.EventCursor);
    }

    [Fact]
    public void RejectsMissingOwnerCapabilityWithoutDiscardingLastGoodWorld()
    {
        var session = new OwnerWorldObservationSession();
        var accepted = CreateCoherentReconnect();
        Assert.True(session.TryAccept(accepted, requestedAfterEventId: 3, out _));

        var missingCapability = accepted with
        {
            Handshake = accepted.Handshake with
            {
                ServerCapabilities = accepted.Handshake.ServerCapabilities
                    .Where(capability => capability != "paused-authoring.request.v1")
                    .ToArray(),
            },
        };

        var wasAccepted = session.TryAccept(missingCapability, requestedAfterEventId: 3, out var failure);

        Assert.False(wasAccepted);
        Assert.Contains("required paired-owner capabilities", failure, StringComparison.Ordinal);
        Assert.Same(accepted, session.Current);
    }

    [Fact]
    public void RejectsIncoherentOwnerReconnectWithoutDiscardingLastGoodWorld()
    {
        var session = new OwnerWorldObservationSession();
        var accepted = CreateCoherentReconnect();
        Assert.True(session.TryAccept(accepted, requestedAfterEventId: 3, out _));

        var incomplete = accepted with
        {
            Baseline = accepted.Baseline with
            {
                Events = accepted.Baseline.Events with
                {
                    Events = [accepted.Baseline.Events.Events[0]],
                },
            },
        };

        var wasAccepted = session.TryAccept(incomplete, requestedAfterEventId: 3, out var failure);

        Assert.False(wasAccepted);
        Assert.Contains("incomplete", failure, StringComparison.Ordinal);
        Assert.Same(accepted, session.Current);
    }

    [Fact]
    public void InstructionPayloadMatchesViewerOwnerProtocolByteForByte()
    {
        var clientAction = new OwnerInstructionAction(
            IdempotencyKey: "instruction-01",
            TargetInhabitantId: "camp-alpha",
            Kind: "must-do",
            Text: "Gather wood before dusk.");
        var serverAction = new ServerInstructionAction(
            clientAction.IdempotencyKey,
            clientAction.TargetInhabitantId,
            clientAction.Kind,
            clientAction.Text);

        var clientPayload = OwnerWorldActionPayload.Instruction(clientAction);
        var serverPayload = OwnerHttpBinding.InstructionPayload(serverAction);

        Assert.Equal(serverPayload, clientPayload);
    }

    [Fact]
    public void PairingApprovalPayloadMatchesViewerOwnerProtocolByteForByte()
    {
        var clientAction = new OwnerPairingApprovalAction("pairing_01", "042069");
        var serverAction = new ServerPairingApprovalAction(clientAction.PairingId, clientAction.PairingCode);

        var clientPayload = OwnerWorldActionPayload.PairingApproval(clientAction);
        var serverPayload = OwnerHttpBinding.PairingApprovalPayload(serverAction);

        Assert.Equal(serverPayload, clientPayload);
    }

    [Fact]
    public void DeviceManagementPayloadMatchesViewerOwnerProtocolByteForByte()
    {
        var clientAction = new OwnerDeviceManagementAction("device_01");
        var serverAction = new ServerDeviceManagementAction(clientAction.DeviceId);

        var clientPayload = OwnerWorldActionPayload.DeviceManagement(clientAction);
        var serverPayload = OwnerHttpBinding.DeviceManagementPayload(serverAction);

        Assert.Equal(serverPayload, clientPayload);
    }

    [Fact]
    public void DeviceListPayloadMatchesViewerOwnerProtocolByteForByte()
    {
        var clientPayload = OwnerWorldActionPayload.DeviceList();
        var serverPayload = OwnerHttpBinding.DeviceListPayload();

        Assert.Equal(serverPayload, clientPayload);
    }

    [Fact]
    public void ProviderConfigurationPayloadMatchesViewerOwnerProtocolWithoutEmbeddingTheSecret()
    {
        const string secret = "player-provider-secret";
        var clientAction = new OwnerProviderConfigurationAction(
            "planning",
            "ollama-cloud",
            "gpt-oss:120b-cloud",
            secret,
            false);
        var serverAction = new ServerProviderConfigurationAction(
            clientAction.Role,
            clientAction.Provider,
            clientAction.Model,
            clientAction.ApiKey,
            clientAction.ForgetCredential);

        var clientPayload = OwnerWorldActionPayload.ProviderConfiguration(clientAction);
        var serverPayload = OwnerHttpBinding.ProviderConfigurationPayload(serverAction);

        Assert.Equal(serverPayload, clientPayload);
        Assert.DoesNotContain(secret, clientPayload, StringComparison.Ordinal);
        Assert.Equal(OwnerHttpBinding.ProviderStatusPayload(), OwnerWorldActionPayload.ProviderStatus());
    }

    [Theory]
    [InlineData("openai", null, null, true)]
    [InlineData("ollama-cloud", "0123456789abcdef0123456789abcdef", null, true)]
    [InlineData("openai", null, "pasted-provider-secret", true)]
    [InlineData("ollama-cloud", null, null, false)]
    public void ProviderModelListPayloadMatchesViewerOwnerProtocolWithoutEmbeddingTheSecret(
        string provider, string? slot, string? apiKey, bool checkKey)
    {
        var clientPayload = OwnerWorldActionPayload.ProviderModelList(new OwnerProviderModelListAction(provider, slot, apiKey, checkKey));
        var serverPayload = OwnerHttpBinding.ProviderModelListPayload(new ServerProviderModelListAction(provider, slot, apiKey, checkKey));

        Assert.Equal(serverPayload, clientPayload);
        Assert.EndsWith($"check-key={checkKey.ToString().ToLowerInvariant()}", clientPayload, StringComparison.Ordinal);
        if (apiKey is not null) Assert.DoesNotContain(apiKey, clientPayload, StringComparison.Ordinal);
    }

    [Fact]
    public void ControlPayloadMatchesViewerOwnerProtocolByteForByte()
    {
        var clientPayload = OwnerWorldActionPayload.Control("pause");
        var serverPayload = OwnerHttpBinding.EmptyPayload("pause");

        Assert.Equal(serverPayload, clientPayload);
    }

    private static OwnerWorldReconnect CreateCoherentReconnect()
    {
        var handshake = new OwnerWorldHandshake(
            Protocol: new OwnerWorldProtocolVersion(1, 1),
            ServerCapabilities:
            [
                "owner-observation.read.v1",
                "inhabitant-inspection.read.v1",
                "spatial-knowledge.read.v1",
                "owner-control.request.v1",
                "paused-authoring.request.v1",
            ],
            ClientCapabilities: []);
        var snapshot = new OwnerWorldSnapshot(
            WorldId: "fixture-world",
            WorldTick: 5,
            MapManifestDigest: "fixture-map",
            Tiles: [new OwnerWorldTile(0, 0, "meadow")],
            Objects: [],
            Resources: [],
            Actor: new OwnerWorldActor("camp-alpha", new OwnerWorldPosition(0, 0), 5000, 1, 0),
            LatestEventId: 5);
        var events = new OwnerWorldEventSlice(
            SnapshotTick: 5,
            AfterEventId: 3,
            Events:
            [
                new OwnerWorldEvent(4, 4, "move", "north"),
                new OwnerWorldEvent(5, 5, "harvest", "berries"),
            ]);

        return new OwnerWorldReconnect(handshake, new OwnerWorldReconnectBaseline(snapshot, events));
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Control;
using ClankerWorld.Viewer.Observation;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class ViewerHttpTests
{
    [Fact]
    public async Task PairingVolumeIsBoundedWithoutBlockingAnExistingOwnersSignedRequests()
    {
        using var host = new ViewerWebApplicationFactory();
        using var client = host.CreateClient();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var device = await StartAndActivateAsync(host, client, key);
        for (var index = 0; index < 7; index++)
        {
            using var rejected = await client.PostAsJsonAsync("/api/v1/pairings", new StartOwnerPairingHttpRequest("invalid"));
            Assert.NotEqual(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        }
        using var limited = await client.PostAsJsonAsync("/api/v1/pairings", new StartOwnerPairingHttpRequest("invalid"));
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.NotNull(limited.Headers.RetryAfter);
        using var owner = await SendSignedAsync(host, client, key, device.DeviceId, "/api/v1/owner/reconnect",
            new OwnerReconnectAction(0), OwnerHttpBinding.ReconnectPayload(new OwnerReconnectAction(0)));
        Assert.Equal(HttpStatusCode.OK, owner.StatusCode);
        using var hidden = await client.PostAsJsonAsync("/api/v1/local/pairings", new StartOwnerPairingHttpRequest("invalid"));
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        using var oversized = await client.PostAsJsonAsync("/api/v1/pairings", new StartOwnerPairingHttpRequest(new string('x', 17_000)));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversized.StatusCode);
    }

    [Fact]
    public async Task UnpairedClientsCanDiscoverPairingButCannotReadTheWorld()
    {
        using var client = factory.CreateClient();

        var handshake = await client.GetFromJsonAsync<ViewerHandshake>("/api/v1/handshake");
        using var world = await client.GetAsync("/api/v1/world");
        using var events = await client.GetAsync("/api/v1/events?afterEventId=0");
        using var reconnect = await client.GetAsync("/api/v1/reconnect?afterEventId=0");
        using var attemptedWrite = await client.PostAsync("/api/v1/world", content: null);
        using var remotePairingApproval = await client.PostAsJsonAsync(
            "/api/v1/local/pairings/pairing_not_real/approve",
            new LocalPairingApprovalHttpRequest("000000"));
        using var remoteRecoveryRevoke = await client.PostAsJsonAsync(
            "/api/v1/local/devices/device_not_real/revoke",
            new LocalDeviceRevokeHttpRequest(null));
        using var unsignedDeviceList = await client.PostAsJsonAsync(
            "/api/v1/owner/devices/list",
            new { });
        var page = await client.GetStringAsync("/");

        Assert.NotNull(handshake);
        Assert.Equal(new ProtocolVersion(1, 1), handshake.Protocol);
        Assert.Equal(["owner-device-pairing.v1"], handshake.ServerCapabilities);
        Assert.Equal(HttpStatusCode.Unauthorized, world.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, events.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, reconnect.StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, attemptedWrite.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, remotePairingApproval.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, remoteRecoveryRevoke.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, unsignedDeviceList.StatusCode);
        Assert.Contains("read-only deterministic inspection", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PairingBackpressureUsesTooManyRequestsInsteadOfCreatingUnboundedState()
    {
        var directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"clankerworld-viewer-pairing-capacity-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            using var host = new ViewerWebApplicationFactory(directory);
            using var client = host.CreateClient();
            for (var index = 0; index < OwnerAuthorityStore.MaximumPendingPairings; index++)
            {
                using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
                using var started = await client.PostAsJsonAsync(
                    "/api/v1/pairings",
                    new StartOwnerPairingHttpRequest(Convert.ToBase64String(key.ExportSubjectPublicKeyInfo())));
                Assert.Equal(HttpStatusCode.OK, started.StatusCode);
            }

            using var overflowKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            using var overflow = await client.PostAsJsonAsync(
                "/api/v1/pairings",
                new StartOwnerPairingHttpRequest(Convert.ToBase64String(overflowKey.ExportSubjectPublicKeyInfo())));

            Assert.Equal((HttpStatusCode)429, overflow.StatusCode);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task RejectedReconnectDoesNotCreateAClientPresenceLease()
    {
        var directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"clankerworld-viewer-presence-auth-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            using var host = new ViewerWebApplicationFactory(directory);
            using var client = host.CreateClient();

            using var rejected = await client.PostAsJsonAsync(
                "/api/v1/owner/reconnect",
                new { });

            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            Assert.False(host.Services.GetRequiredService<OwnerClientPresenceLease>().HasActiveClient);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ReplayedChallengeIsRejectedBeforeASecondControlCanReachTheRuntime()
    {
        using var client = factory.CreateClient();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pairing = await StartAndActivateAsync(client, key, Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()));
        var envelope = await CreateSignedRequestAsync(
            client,
            key,
            pairing.DeviceId,
            "/api/v1/owner/control/pause",
            new OwnerControlAction("pause"),
            OwnerHttpBinding.EmptyPayload("pause"));

        using var first = await client.PostAsJsonAsync("/api/v1/owner/control/pause", envelope);
        using var replay = await client.PostAsJsonAsync("/api/v1/owner/control/pause", envelope);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, replay.StatusCode);
        var runtime = factory.Services.GetRequiredService<OwnerWorldRuntime>();
        Assert.True(runtime.Capture().Snapshot.IsPaused);
        Assert.Single(runtime.Capture().Events, worldEvent => worldEvent.Kind == "paused");
    }

    [Fact]
    public async Task PairedOwnerCanApproveAndRevokeAnotherDeviceThroughSignedRequests()
    {
        var directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"clankerworld-viewer-device-management-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            using var firstKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            using var secondKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            using var host = new ViewerWebApplicationFactory(directory);
            using var client = host.CreateClient();
            var firstDevice = await StartAndActivateAsync(host, client, firstKey);

            using var start = await client.PostAsJsonAsync(
                "/api/v1/pairings",
                new StartOwnerPairingHttpRequest(Convert.ToBase64String(secondKey.ExportSubjectPublicKeyInfo())));
            var pending = await start.Content.ReadFromJsonAsync<OwnerPairingStart>();
            Assert.Equal(HttpStatusCode.OK, start.StatusCode);
            Assert.NotNull(pending);

            var approveAction = new OwnerPairingApprovalAction(pending.PairingId, pending.PairingCode);
            using var approve = await SendSignedAsync(
                host,
                client,
                firstKey,
                firstDevice.DeviceId,
                "/api/v1/owner/pairings/approve",
                approveAction,
                OwnerHttpBinding.PairingApprovalPayload(approveAction));
            var approval = await approve.Content.ReadFromJsonAsync<OwnerPairingApproval>();
            Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
            Assert.NotNull(approval);
            Assert.Equal(pending.DeviceId, approval.DeviceId);

            var activation = new ActivateOwnerPairingHttpRequest(
                pending.PairingId,
                pending.ActivationCanonicalProof,
                Sign(secondKey, pending.ActivationCanonicalProof));
            using var activate = await client.PostAsJsonAsync("/api/v1/pairings/activate", activation);
            var secondDevice = await activate.Content.ReadFromJsonAsync<OwnerDevice>();
            Assert.Equal(HttpStatusCode.OK, activate.StatusCode);
            Assert.NotNull(secondDevice);
            Assert.Equal(OwnerDeviceState.Active, secondDevice.State);

            var listAction = new OwnerDeviceListAction();
            var activeListEnvelope = await CreateSignedRequestAsync(
                host,
                client,
                firstKey,
                firstDevice.DeviceId,
                "/api/v1/owner/devices/list",
                listAction,
                OwnerHttpBinding.DeviceListPayload());
            using var activeList = await client.PostAsJsonAsync(
                "/api/v1/owner/devices/list",
                activeListEnvelope);
            using var activeListReplay = await client.PostAsJsonAsync(
                "/api/v1/owner/devices/list",
                activeListEnvelope);
            var activeDevices = await activeList.Content.ReadFromJsonAsync<OwnerDevice[]>();

            Assert.Equal(HttpStatusCode.OK, activeList.StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, activeListReplay.StatusCode);
            Assert.NotNull(activeDevices);
            Assert.Equal(2, activeDevices.Length);
            Assert.All(activeDevices, device => Assert.Equal(OwnerDeviceState.Active, device.State));
            var listedFirstDevice = Assert.Single(activeDevices, device => device.DeviceId == firstDevice.DeviceId);
            var listedSecondDevice = Assert.Single(activeDevices, device => device.DeviceId == secondDevice.DeviceId);
            Assert.Equal(firstDevice.PublicKeyFingerprint, listedFirstDevice.PublicKeyFingerprint);
            Assert.Equal(secondDevice.PublicKeyFingerprint, listedSecondDevice.PublicKeyFingerprint);
            Assert.Equal(firstDevice.PublicKeySpkiBase64, listedFirstDevice.PublicKeySpkiBase64);
            Assert.Equal(secondDevice.PublicKeySpkiBase64, listedSecondDevice.PublicKeySpkiBase64);
            Assert.Null(listedFirstDevice.RevokedAtUtc);
            Assert.Null(listedSecondDevice.RevokedAtUtc);

            var revokeAction = new OwnerDeviceManagementAction(secondDevice.DeviceId);
            using var revoke = await SendSignedAsync(
                host,
                client,
                firstKey,
                firstDevice.DeviceId,
                "/api/v1/owner/devices/revoke",
                revokeAction,
                OwnerHttpBinding.DeviceManagementPayload(revokeAction));
            var revoked = await revoke.Content.ReadFromJsonAsync<OwnerDevice>();
            Assert.Equal(HttpStatusCode.OK, revoke.StatusCode);
            Assert.NotNull(revoked);
            Assert.Equal(OwnerDeviceState.Revoked, revoked.State);

            using var revokedList = await SendSignedAsync(
                host,
                client,
                firstKey,
                firstDevice.DeviceId,
                "/api/v1/owner/devices/list",
                listAction,
                OwnerHttpBinding.DeviceListPayload());
            var devicesAfterRevoke = await revokedList.Content.ReadFromJsonAsync<OwnerDevice[]>();

            Assert.Equal(HttpStatusCode.OK, revokedList.StatusCode);
            Assert.NotNull(devicesAfterRevoke);
            Assert.Equal(2, devicesAfterRevoke.Length);
            Assert.Equal(
                OwnerDeviceState.Active,
                Assert.Single(devicesAfterRevoke, device => device.DeviceId == firstDevice.DeviceId).State);
            var listedRevokedDevice = Assert.Single(devicesAfterRevoke, device => device.DeviceId == secondDevice.DeviceId);
            Assert.Equal(OwnerDeviceState.Revoked, listedRevokedDevice.State);
            Assert.NotNull(listedRevokedDevice.RevokedAtUtc);

            var authority = host.Services.GetRequiredService<OwnerAuthorityStore>();
            const string rejectedRequestId = "revoked-device-challenge";
            var issueProof = OwnerAuthorityStore.CreateChallengeIssueCanonicalProof(
                authority.Identity,
                secondDevice.DeviceId,
                rejectedRequestId);
            using var rejectedChallenge = await client.PostAsJsonAsync(
                "/api/v1/owner/challenges",
                new IssueOwnerChallengeHttpRequest(
                    secondDevice.DeviceId,
                    rejectedRequestId,
                    issueProof,
                    Sign(secondKey, issueProof)));

            Assert.Equal(HttpStatusCode.Forbidden, rejectedChallenge.StatusCode);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task PairedOwnerReconnectAndPausedWorldSurviveAHostRestart()
    {
        var directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"clankerworld-viewer-restart-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            OwnerDevice pairedDevice;
            using (var firstHost = new ViewerWebApplicationFactory(directory))
            using (var firstClient = firstHost.CreateClient())
            {
                pairedDevice = await StartAndActivateAsync(firstHost, firstClient, key);
                var firstRuntime = firstHost.Services.GetRequiredService<OwnerWorldRuntime>();
                Assert.True(firstRuntime.Pause($"owner-device:{pairedDevice.DeviceId}"));
                firstHost.Services.GetRequiredService<OwnerWorldStateFile>().Save(firstRuntime);
            }

            using var restartedHost = new ViewerWebApplicationFactory(directory);
            using var restartedClient = restartedHost.CreateClient();
            using var reconnect = await SendSignedAsync(
                restartedHost,
                restartedClient,
                key,
                pairedDevice.DeviceId,
                "/api/v1/owner/reconnect",
                new OwnerReconnectAction(0),
                OwnerHttpBinding.ReconnectPayload(new OwnerReconnectAction(0)));
            var baseline = await reconnect.Content.ReadFromJsonAsync<ViewerOwnerReconnect>();

            Assert.Equal(HttpStatusCode.OK, reconnect.StatusCode);
            Assert.NotNull(baseline);
            Assert.True(baseline.Baseline.Snapshot.Authoring!.IsPaused);
            Assert.Contains(
                baseline.Baseline.Events.Events,
                worldEvent => worldEvent.Kind == "paused" &&
                    worldEvent.Detail.Contains($"issuer:owner-device:{pairedDevice.DeviceId}", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}

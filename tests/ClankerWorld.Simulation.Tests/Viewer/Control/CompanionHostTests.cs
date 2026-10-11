using System.Net;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Control;
using ClankerWorld.Viewer.Observation;
using Microsoft.AspNetCore.Http;

namespace ClankerWorld.Simulation.Tests;

/// <summary>A host the game starts on the player's own PC (#1563).</summary>
public sealed class CompanionHostTests
{
    private const int ApprovalPort = 5189;
    private static readonly string Secret = new('s', OwnerPairingHostOptions.MinimumCompanionSecretLength);

    [Fact]
    public void OnlyTheGameThatStartedTheHostCanApproveOrStopIt()
    {
        var companion = new OwnerPairingHostOptions(ApprovalPort, Secret);
        Assert.True(companion.IsLocalApprovalRequest(Request(ApprovalPort, Secret)));
        Assert.True(companion.IsCompanionShutdownRequest(Request(ApprovalPort, Secret)));
        // Any other program on the PC can reach the loopback listener, but not without the secret.
        Assert.False(companion.IsLocalApprovalRequest(Request(ApprovalPort, null)));
        Assert.False(companion.IsLocalApprovalRequest(Request(ApprovalPort, Secret[..^1] + "x")));
        Assert.False(companion.IsCompanionShutdownRequest(Request(ApprovalPort, "short")));
        Assert.False(companion.IsLocalApprovalRequest(Request(5188, Secret)));
        Assert.False(companion.IsLocalApprovalRequest(Request(ApprovalPort, Secret, IPAddress.Parse("192.0.2.4"))));
    }

    [Fact]
    public void AServerHostKeepsItsLocalApprovalAndCannotBeStoppedOverHttp()
    {
        var server = new OwnerPairingHostOptions(ApprovalPort);
        Assert.True(server.IsLocalApprovalRequest(Request(ApprovalPort, null)));
        Assert.False(server.IsCompanionShutdownRequest(Request(ApprovalPort, null)));
        Assert.False(server.IsCompanionShutdownRequest(Request(ApprovalPort, Secret)));
        Assert.False(new OwnerPairingHostOptions(0).IsLocalApprovalRequest(Request(0, null)));
    }

    [Fact]
    public void CompanionSecretFileMustHoldOneLongValue()
    {
        var path = Path.Combine(Path.GetTempPath(), $"clankerworld-companion-{Guid.NewGuid():N}.secret");
        try
        {
            File.WriteAllText(path, Secret + "\r\n");
            Assert.Equal(Secret, OwnerPairingHostOptions.ReadCompanionSecret(path));
            File.WriteAllText(path, "too-short");
            Assert.Throws<InvalidOperationException>(() => OwnerPairingHostOptions.ReadCompanionSecret(path));
            File.WriteAllText(path, Secret + " " + Secret);
            Assert.Throws<InvalidOperationException>(() => OwnerPairingHostOptions.ReadCompanionSecret(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task StoppingTheHostSavesTheLatestTickAndCancelsModelWork()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"clankerworld-shutdown-save-{Guid.NewGuid():N}");
        try
        {
            var path = Path.Combine(directory, "world.json");
            var logger = new RecordingLogger<PrivateWorldRuntimeService>();
            var provider = new WaitingHostedProvider();
            using var runtime = new PrivateWorldRuntime("shutdown-save", id =>
                id == "founder-scout" ? provider : new DeterministicDecisionProvider());
            using var service = new PrivateWorldRuntimeService(runtime, new PrivateWorldStateFile(path),
                new OwnerClientPresenceLease(TimeSpan.FromMinutes(1)), logger);
            // Ticks committed in memory but not yet written, as when the host stops mid-loop.
            Assert.True((await runtime.AdvanceOneTickNonBlockingAsync()).Advanced);
            await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var tick = runtime.WorldTick;

            Assert.Equal(ShutdownSaveOutcome.Saved, service.SaveBeforeShutdown(pauseWorld: true));
            Assert.True(provider.Cancellation.IsCancellationRequested);

            using var reloaded = new PrivateWorldStateFile(path).LoadOrCreate("shutdown-save");
            Assert.Equal(tick, reloaded.WorldTick);
            Assert.True(reloaded.Society.IsPaused);
            Assert.False((await reloaded.AdvanceOneTickNonBlockingAsync()).Advanced);
            Assert.Equal(tick, reloaded.WorldTick);
            Assert.Contains(logger.Messages, message => message.Contains("host_shutdown outcome=saved", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class WaitingHostedProvider : IDecisionProvider
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken Cancellation { get; private set; }
        public DecisionProviderKind Kind => DecisionProviderKind.Jev;
        public long ProviderEpoch => 1;

        public async ValueTask<CognitionDecisionResponse> DecideAsync(
            CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            Cancellation = cancellationToken;
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The hosted call must be cancelled.");
        }
    }

    private static DefaultHttpContext Request(int localPort, string? secret, IPAddress? remote = null)
    {
        var context = new DefaultHttpContext();
        context.Connection.LocalPort = localPort;
        context.Connection.RemoteIpAddress = remote ?? IPAddress.Loopback;
        if (secret is not null) context.Request.Headers[OwnerPairingHostOptions.CompanionSecretHeader] = secret;
        return context;
    }
}

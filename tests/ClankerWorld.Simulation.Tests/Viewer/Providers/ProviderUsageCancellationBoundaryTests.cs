using System.Net;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Control;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class ProviderUsageCancellationBoundaryTests
{
    [Fact]
    public async Task ExternalConfiguredHostedCompletionStillPausesAndSavesBeforeDecisionAdmission()
    {
        var fixture = new HeldConfiguredUsageWorld();
        try
        {
            await fixture.StartHeldRequestAsync();

            // Completion comes from an independent provider thread. Its usage
            // effect must remain synchronous here, rather than allowing the
            // completed personal response to race ahead of the pause.
            await fixture.CompleteResponseAsync();

            Assert.True(fixture.World.Society.IsPaused);
            Assert.True(PrivateWorldRuntimeCodec.Decode(File.ReadAllBytes(fixture.StateFile.Path))
                .Society.Society.IsPaused);
            var usage = fixture.Usage.Capture();
            Assert.Equal(1, usage.Attempts);
            Assert.Equal(1, usage.Completed);
            Assert.Equal(0, usage.Abandoned);
            var next = await fixture.TickAsync();
            Assert.False(next.Advanced);
            Assert.Empty(next.Decisions);
            Assert.DoesNotContain(fixture.World.ExportState().Events,
                item => item.Kind == "hosted_decision_completed");
            Assert.Single(fixture.World.ExportState().Society.Cognition.Queue,
                entry => entry.InhabitantId == HeldConfiguredUsageWorld.Actor);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    private sealed class HeldConfiguredUsageWorld : IDisposable
    {
        public const string Actor = "founder-scout";
        private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);
        private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("clankerworld-usage-cancel-boundary-");
        private readonly InlineCancellationHttpHandler handler = new();
        private readonly RecordingLogger<ProviderUsageCancellationBoundaryTests> log = new();
        private Task? runningOperation;
        private readonly AwaitingDecisionProvider provider;

        public HeldConfiguredUsageWorld()
        {
            var configuration = new ProviderConfigurationStore(Path.Combine(directory.FullName, "providers.json"),
                new("deterministic", null, null, null, null, null, null));
            _ = configuration.Configure(new("personal", "openai", "boundary-test-model", "boundary-test-key", false,
                Actor, Guid.NewGuid().ToString("N"), "Boundary test model"));
            Usage = new ProviderUsageStore(Path.Combine(directory.FullName, "usage.json"));
            _ = Usage.Configure(new ProviderUsageLimitAction(1));
            provider = new AwaitingDecisionProvider(new ConfigurableDecisionProvider(
                configuration, new HeldHttpClientFactory(handler), usageStore: Usage));
            World = new PrivateWorldRuntime("configured-usage-cancellation-boundary", id =>
                id == Actor ? provider : new DeterministicDecisionProvider());
            StateFile = new PrivateWorldStateFile(Path.Combine(directory.FullName, "world.json"));
            var effects = new ProviderUsageWorldEffects(World, StateFile, Usage, configuration.WorldMutationGate, log);
            Usage.LimitReached += effects.PauseAtLimit;
            Usage.WarningReached += effects.RecordWarning;
        }

        public PrivateWorldRuntime World { get; }
        public PrivateWorldStateFile StateFile { get; }
        public ProviderUsageStore Usage { get; }

        public async Task StartHeldRequestAsync()
        {
            Assert.True((await TickAsync()).Advanced);
            await handler.Started.Task.WaitAsync(Deadline);
            // Started is deliberately early: SendAsync has not returned yet.
            // Only the configured provider's returned incomplete operation proves
            // that its full HTTP/usage chain has suspended on the held response.
            Assert.False(provider.AwaitingReply.Task.IsCompleted);
            handler.AllowReturn.Set();
            await provider.AwaitingReply.Task.WaitAsync(Deadline);
            // No second request may spend another call while the first is held.
            Assert.True((await TickAsync()).Advanced);
            Assert.Equal(1, handler.RequestCount);
            Assert.Equal(1, Usage.Capture().Attempts);
            Assert.Equal(0, Usage.Capture().Completed);
            Assert.Equal(0, Usage.Capture().Abandoned);
        }

        public Task<PrivateWorldStepResult> TickAsync() => RunWithDeadlineAsync(() =>
            World.AdvanceOneTickNonBlockingAsync().AsTask());

        public Task<bool> CompleteResponseAsync() => RunWithDeadlineAsync(() =>
        {
            handler.CompleteResponse();
            return Task.FromResult(true);
        });

        private async Task<T> RunWithDeadlineAsync<T>(Func<Task<T>> operation)
        {
            var task = Task.Run(operation);
            runningOperation = task;
            return await task.WaitAsync(Deadline);
        }

        public void Dispose()
        {
            // On a negative-baseline deadlock the cancelled HTTP continuation
            // and Pause still own/wait on the gate. Disposing the world or the
            // handler's cancellation registration would hang the test again.
            // Leave that isolated failed fixture to the test-process teardown;
            // successful cases dispose normally and remove their temp files.
            if (runningOperation is { IsCompleted: false }) return;
            World.Dispose();
            handler.Dispose();
            directory.Delete(recursive: true);
        }
    }

    private sealed class AwaitingDecisionProvider(IDecisionProvider inner) : IDecisionProvider
    {
        public TaskCompletionSource AwaitingReply { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public DecisionProviderKind Kind => inner.Kind;
        public long ProviderEpoch => inner.ProviderEpoch;
        public DecisionProviderKind KindFor(InhabitantObservation observation) => inner.KindFor(observation);

        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            var pending = inner.DecideAsync(request, cancellationToken);
            Assert.False(pending.IsCompleted);
            AwaitingReply.TrySetResult();
            return pending;
        }
    }

    private sealed class HeldHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class InlineCancellationHttpHandler : HttpMessageHandler
    {
        // Default options intentionally permit inline continuations. This is a
        // legal HTTP handler behavior that the runtime must tolerate.
        private readonly TaskCompletionSource<HttpResponseMessage> response = new();
        private CancellationTokenRegistration cancellation;
        private int requestCount;

        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int RequestCount => Volatile.Read(ref requestCount);
        public ManualResetEventSlim AllowReturn { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref requestCount);
            cancellation = cancellationToken.Register(() => response.TrySetCanceled(cancellationToken));
            Started.TrySetResult(true);
            if (!AllowReturn.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("The fixture did not release the early HTTP start signal.");
            return response.Task;
        }

        public void CompleteResponse() => response.TrySetResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                {"model":"boundary-test-model","choices":[{"message":{"role":"assistant","content":"{\"selected_candidate_id\":\"safe_idle\",\"confidence\":1.0,\"probabilities\":{\"safe_idle\":1.0}}"}}]}
                """),
        });

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                AllowReturn.Set();
                cancellation.Dispose();
                AllowReturn.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}

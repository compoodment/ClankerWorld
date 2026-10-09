using System.Collections.Concurrent;
using ClankerWorld.Simulation.Cognition;

namespace ClankerWorld.Simulation.Tests;

internal sealed class HeldPhysicalTaskDecisionProvider : IDecisionProvider
{
    public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
    public long ProviderEpoch => 1;
    public ConcurrentQueue<InhabitantObservation> Requests { get; } = new();
    public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
        CancellationToken cancellationToken = default)
    {
        Requests.Enqueue(request.Observation);
        Started.TrySetResult(true);
        await Task.Delay(Timeout.Infinite, cancellationToken);
        throw new InvalidOperationException("The held physical-task reply must be cancelled, not completed.");
    }
}

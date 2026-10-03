using System.Collections.Concurrent;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class RetainedCognitionCostTests
{
    private const string TargetId = "founder:00000000000000000000000000000001";

    // Ordinary peers change the world while a reply is held, so some choices
    // disappear. That leaves the accepted reply valid and needs no second call.
    [Theory]
    [InlineData(3)]
    [InlineData(8)]
    public async Task ChoicesThatOnlyDisappearWhileAReplyIsHeldAskNoSecondCall(int heldTicks)
    {
        var provider = new FirstHeldProvider();
        using var world = NormalPathWorld.CreateGenerated("review-cost", id =>
            id == TargetId ? provider : new DeterministicDecisionProvider());
        for (var tick = 0; tick < 400 && provider.Requests.IsEmpty; tick++)
            Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        for (var tick = 0; tick < heldTicks; tick++)
            Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
        provider.Release.TrySetResult();
        for (var tick = 0; tick < 6; tick++)
        {
            Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
            await Task.Delay(20);
        }
        var requests = provider.Requests.ToArray();
        Assert.True(requests.Length == 1, string.Join("\n", requests.Select(request =>
            $"tick={request.Observation.WorldTick} {string.Join(",", request.Observation.Candidates.Select(c => c.Id).Order(StringComparer.Ordinal))}")));
    }

    private sealed class FirstHeldProvider : IDecisionProvider
    {
        private int calls;
        public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
        public long ProviderEpoch => 1;
        public ConcurrentQueue<CognitionDecisionRequest> Requests { get; } = new();
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            var call = Interlocked.Increment(ref calls);
            Requests.Enqueue(request);
            if (call == 1)
            {
                Started.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
            var observation = request.Observation;
            var selected = observation.Candidates.OrderBy(c => c.DeterministicPriority).ThenBy(c => c.Id, StringComparer.Ordinal)
                .First(c => c.Id != "safe_idle" && !c.Id.StartsWith("conversation", StringComparison.Ordinal)).Id;
            return new(request.RequestId, observation.InhabitantId, Kind, ProviderEpoch,
                observation.RunEpoch, observation.DecisionGeneration, observation.ObservationDigest, selected, 1,
                observation.Candidates.ToDictionary(c => c.Id, c => c.Id == selected ? 1d : 0d, StringComparer.Ordinal),
                ChosenName: observation.NeedsName ? "Reviewa" : null,
                ChosenPersonality: observation.NeedsPersonality ? "Calm" : null,
                ChosenAspiration: observation.NeedsAspiration ? "Build a home" : null);
        }
    }
}

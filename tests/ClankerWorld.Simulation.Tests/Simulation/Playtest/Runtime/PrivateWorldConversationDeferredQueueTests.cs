using System.Collections;
using System.Reflection;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class PrivateWorldConversationTests
{
    [Fact]
    public async Task NewGuidanceWaitsForCurrentChoicesAfterAnOlderReplyStartsAConversation()
    {
        var provider = new ConversationProvider(new HashSet<string>([InitiatorId, InviteeId], StringComparer.Ordinal));
        var held = new HeldFirstConversationPlanningProvider(provider);
        using var world = NewWorld("conversation-call-baseline", id =>
            id is InitiatorId or InviteeId ? held : new DeterministicDecisionProvider());
        world.StartWorld();
        Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
        await held.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var guidance = world.SubmitInstruction(new OwnerInstructionRequest(
            "guidance-during-talk", "owner:test", InitiatorId, OwnerInstructionKind.Suggestive,
            "Remember the river path after this conversation."));
        Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
        Assert.Contains(world.ExportState().Society.Cognition.Queue, entry =>
            entry.InhabitantId == InitiatorId && entry.Observation.ObserverGuidance!.Any(message =>
                message.InstructionId == guidance.InstructionId));

        held.Release.TrySetResult();
        await AwaitConversationFixtureCallsAsync(world);
        var admitted = await world.AdvanceOneTickNonBlockingAsync();
        Assert.Contains(admitted.Decisions, decision => decision.InhabitantId == InitiatorId &&
            decision.Admission.Intention?.CandidateId == $"talk:{InviteeId}");
        var proposal = Assert.Single(world.Conversations);
        var proposedAt = world.WorldTick;
        Assert.Equal(AgentConversationStatus.Proposed, proposal.Status);
        await AwaitConversationFixtureCallsAsync(world);
        Assert.Single(provider.PlanningRequests, request => request.Observation.InhabitantId == InitiatorId);

        for (var tick = 0; tick < 64 && world.ExportState().Instructions!.Single(instruction =>
                 instruction.InstructionId == guidance.InstructionId).ObservedTick is null; tick++)
        {
            await AwaitConversationFixtureCallsAsync(world);
            Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
        }
        await AwaitConversationFixtureCallsAsync(world);

        Assert.NotNull(world.ExportState().Instructions!.Single(instruction =>
            instruction.InstructionId == guidance.InstructionId).ObservedTick);
        var later = Assert.Single(provider.PlanningRequests, request =>
            request.Observation.InhabitantId == InitiatorId && request.Observation.ObserverGuidance!.Any(message =>
                message.InstructionId == guidance.InstructionId));
        Assert.True(later.Observation.WorldTick > proposedAt,
            "The retained guidance must use choices observed after the older talk action was applied.");
        var calls = provider.PlanningRequests.Where(request => request.Observation.InhabitantId == InitiatorId).ToArray();
        Assert.Equal(calls.Length, calls.Select(request =>
            (request.RequestId, request.Observation.RunEpoch, request.Observation.DecisionGeneration,
                request.Observation.ObservationDigest)).Distinct().Count());
        world.Validate();
    }

    private static async Task AwaitConversationFixtureCallsAsync(PrivateWorldRuntime world)
    {
        // Only this test advances the world. Wait for its actual fake-provider
        // tasks between ticks so neither admission nor call counts depend on sleep.
        var tasks = new[] { "pendingHosted", "pendingConversationTurns" }.SelectMany(fieldName =>
        {
            var field = typeof(PrivateWorldRuntime).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            var pending = Assert.IsAssignableFrom<IDictionary>(field!.GetValue(world));
            return pending.Values.Cast<object>().Select(item =>
                Assert.IsAssignableFrom<Task>(item.GetType().GetProperty("Task")!.GetValue(item)));
        }).ToArray();
        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(10));
    }

    private sealed class HeldFirstConversationPlanningProvider(ConversationProvider provider) :
        IDecisionProvider, IAgentConversationProvider
    {
        private int held;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public DecisionProviderKind Kind => provider.Kind;
        public long ProviderEpoch => provider.ProviderEpoch;
        public bool CanSpeakAs(string agentId) => provider.CanSpeakAs(agentId);

        public async ValueTask<CognitionDecisionResponse> DecideAsync(
            CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            if (request.Observation.InhabitantId == InitiatorId && Interlocked.Exchange(ref held, 1) == 0)
            {
                Started.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
            return await provider.DecideAsync(request, cancellationToken);
        }

        public ValueTask<AgentConversationTurnResponse> SpeakAsync(
            AgentConversationTurnRequest request, CancellationToken cancellationToken = default) =>
            provider.SpeakAsync(request, cancellationToken);
    }
}

using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class PrivateWorldConversationTests
{
    [Fact]
    public async Task BothParticipantsSeeThePublicWrapUpAndProposedEffectBeforeAccepting()
    {
        var provider = new ConversationProvider(new HashSet<string>([InitiatorId, InviteeId], StringComparer.Ordinal))
        {
            SuggestedWrapUpEffect = AgentConversationEffect.MutualTrust,
            HoldWrapUpChoices = true,
        };
        using var world = NewWorld("informed-conversation-wrap-up", provider);
        world.StartWorld();

        for (var tick = 0; tick < 100 && !new[] { InitiatorId, InviteeId }.All(id =>
                 provider.PlanningRequests.Any(request => request.Observation.InhabitantId == id &&
                     request.Observation.Candidates.Any(candidate => candidate.Id.StartsWith(
                         "conversation_wrapup_accept:", StringComparison.Ordinal)))); tick++)
        {
            _ = await world.AdvanceOneTickNonBlockingAsync();
            await Task.Delay(2);
        }

        var conversation = Assert.Single(world.Conversations, item => item.Status == AgentConversationStatus.WrapUp);
        var wrapUp = Assert.Single(conversation.Turns, item => item.IsWrapUp);
        Assert.Equal(AgentConversationEffect.MutualTrust, conversation.WrapUpEffect);
        foreach (var actor in new[] { InitiatorId, InviteeId })
        {
            var request = Assert.Single(provider.PlanningRequests.Where(item =>
                item.Observation.InhabitantId == actor && item.Observation.Candidates.Any(candidate =>
                    candidate.Id == $"conversation_wrapup_accept:{conversation.Id}")).Take(1));
            var accept = Assert.Single(request.Observation.Candidates,
                item => item.Id == $"conversation_wrapup_accept:{conversation.Id}");
            Assert.Contains(wrapUp.Text, accept.Description, StringComparison.Ordinal);
            Assert.Contains("mutual trust", accept.Description, StringComparison.Ordinal);
        }
        Assert.Empty(conversation.WrapUpAcceptedBy);
        Assert.Equal(0, TrustScore(world, InitiatorId, InviteeId));
        Assert.Equal(0, TrustScore(world, InviteeId, InitiatorId));
    }

    [Fact]
    public async Task FixedSeedConversationsHaveSevenTurnCallsEachAndBoundedOverheadAgainstNoDialogue()
    {
        var talking = new ConversationProvider(new HashSet<string>([InitiatorId, InviteeId], StringComparer.Ordinal));
        var quiet = new ConversationProvider(new HashSet<string>([InitiatorId, InviteeId], StringComparer.Ordinal));
        var quietPlanner = new PlanningOnlyConversationProvider(quiet);
        using var dialogue = NewWorld("conversation-call-baseline", talking);
        using var baseline = NewWorld("conversation-call-baseline", id =>
            id is InitiatorId or InviteeId ? quietPlanner : new DeterministicDecisionProvider());
        dialogue.StartWorld();
        baseline.StartWorld();

        for (var tick = 0; tick < 64; tick++)
        {
            _ = await dialogue.AdvanceOneTickNonBlockingAsync();
            _ = await baseline.AdvanceOneTickNonBlockingAsync();
            await Task.Delay(2);
        }

        Assert.Equal(dialogue.WorldTick, baseline.WorldTick);
        Assert.Empty(baseline.Conversations);
        Assert.Empty(quiet.TurnRequests);
        Assert.Equal(14, talking.TurnRequests.Count);
        Assert.Equal(2, dialogue.Conversations.Count);
        foreach (var calls in talking.TurnRequests.GroupBy(item => item.ConversationId))
        {
            Assert.Equal(7, calls.Count());
            Assert.Equal(6, calls.Count(item => item.Purpose == AgentConversationPurpose.PublicTurn));
            Assert.Single(calls, item => item.Purpose == AgentConversationPurpose.WrapUp);
        }
        var baselineCalls = quiet.PlanningRequests.Count;
        var dialogueCalls = talking.PlanningRequests.Count + talking.TurnRequests.Count;
        Assert.InRange(dialogueCalls - baselineCalls, 0, 24);
        Console.WriteLine($"Fake fixed-seed 64-tick baseline: no-dialogue planning={baselineCalls}, " +
            $"dialogue planning={talking.PlanningRequests.Count}, public/wrap-up calls={talking.TurnRequests.Count}, " +
            $"incremental calls={dialogueCalls - baselineCalls}. No hosted calls or credentials.");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AcceptedConversationSuspendsBothParticipantsOfAnOngoingLesson(bool speakerIsTeacher)
    {
        var provider = new ConversationProvider(new HashSet<string>([InitiatorId, InviteeId], StringComparer.Ordinal))
        {
            BlockConversationUntilCanceled = true,
        };
        using var setup = NewWorld("conversation-pauses-lesson", provider);
        setup.StartWorld();
        setup.Pause();
        var state = setup.ExportState();
        var teacher = speakerIsTeacher ? InitiatorId : ListenerId;
        var learner = speakerIsTeacher ? ListenerId : InitiatorId;
        var camp = state.Map.CampObjects.FirstOrDefault(item => item.Id == "storage")?.Position ??
            setup.WorldSimulation.Buildings.FirstOrDefault(item => item.InstanceId == "first-town-warehouse")?.Position ??
            state.Towns?.FirstOrDefault(item => item.OriginSite is not null)?.OriginSite ??
            state.Map.Resources.First(item => item.Id == "berry-patch").Position;
        var proposal = ReviewInvitation(state, "conversation:ongoing-lesson");
        Assert.True(AgentConversationRules.TryAcceptProposal(proposal, InviteeId, setup.WorldTick,
            setup.Society.RunEpoch, out var accepted));
        state = state with
        {
            Conversations = [accepted],
            ConversationBudgets = [new(InitiatorId, 0, 1), new(InviteeId, 0, 1)],
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId is InitiatorId or InviteeId or ListenerId
                ? person with
                {
                    Position = camp,
                    HungerBasisPoints = 9_000,
                    Survival = person.Survival is { } survival ? survival with { WarmthBasisPoints = 9_000 } : null,
                    Project = null,
                    LastDecisionContext = null,
                    Skills = person.InhabitantId == teacher ? [new(SettlementSkillKind.Building, setup.WorldTick)] : null,
                    Lesson = person.InhabitantId == learner
                        ? new(teacher, SettlementSkillKind.Building, "training", 3, setup.WorldTick, setup.WorldTick) : null,
                } : person).ToArray(),
        };
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)),
            id => id is InitiatorId or InviteeId ? provider : new DeterministicDecisionProvider());
        world.Resume();
        for (var tick = 0; tick < 30 && world.Conversations.Single().Status != AgentConversationStatus.AwaitingSpeaker; tick++)
        {
            _ = await world.AdvanceOneTickNonBlockingAsync();
            await Task.Delay(2);
        }
        Assert.Equal(AgentConversationStatus.AwaitingSpeaker, world.Conversations.Single().Status);
        var progress = world.Inhabitants.Single(person => person.InhabitantId == learner).Lesson!.Progress;
        Assert.InRange(progress, 3, 19);
        for (var tick = 0; tick < 3; tick++)
        {
            _ = await world.AdvanceOneTickNonBlockingAsync();
            await Task.Delay(2);
            Assert.Equal(progress, world.Inhabitants.Single(person => person.InhabitantId == learner).Lesson!.Progress);
        }
        Assert.Empty(world.Inhabitants.Single(person => person.InhabitantId == learner).Skills ?? []);

        world.Pause();
        provider.EndSuspendedConversations = true;
        world.Resume();
        var completionStartTick = world.WorldTick;
        const int completionTickHorizon = 80;
        const int maxCompletionAttempts = completionTickHorizon * 10;
        using var completionDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        long? conversationClosedAtTick = null;
        var completionAttempts = 0;
        for (; completionAttempts < maxCompletionAttempts && !completionDeadline.IsCancellationRequested;
             completionAttempts++)
        {
            if (world.Conversations.Single().Status == AgentConversationStatus.Closed)
            {
                conversationClosedAtTick ??= world.WorldTick;
                var lesson = world.Inhabitants.Single(person => person.InhabitantId == learner).Lesson!;
                if (lesson.Stage == "completed" || world.WorldTick - conversationClosedAtTick >= completionTickHorizon)
                    break;
                // Await hosted decisions before spending the lesson tick budget.
                _ = await world.AdvanceOneTickAsync(completionDeadline.Token);
            }
            else
            {
                _ = await world.AdvanceOneTickNonBlockingAsync(cancellationToken: completionDeadline.Token);
                await Task.Delay(2, completionDeadline.Token);
            }
        }
        if (world.Conversations.Single().Status == AgentConversationStatus.Closed)
            conversationClosedAtTick ??= world.WorldTick;
        Assert.True(world.Conversations.Single().Status == AgentConversationStatus.Closed,
            $"Conversation did not close within {maxCompletionAttempts} attempts or 15 seconds; " +
            $"tick={world.WorldTick}, attempts={completionAttempts}.");
        var completedLesson = world.Inhabitants.Single(person => person.InhabitantId == learner).Lesson!;
        Assert.True(completedLesson.Stage == "completed",
            $"Lesson did not complete within {completionTickHorizon} committed ticks after conversation closure; " +
            $"stage={completedLesson.Stage}, progress={completedLesson.Progress}, " +
            $"committedTicks={(conversationClosedAtTick is { } closedAt ? world.WorldTick - closedAt : 0)}, " +
            $"totalTicks={world.WorldTick - completionStartTick}, attempts={completionAttempts}.");
        var skill = Assert.Single(world.Inhabitants.Single(person => person.InhabitantId == learner).Skills!);
        Assert.Equal(SettlementSkillKind.Building, skill.Kind);
        Assert.Equal(teacher, skill.TeacherId);
    }

    private sealed class PlanningOnlyConversationProvider(ConversationProvider planner) : IDecisionProvider
    {
        public DecisionProviderKind Kind => planner.Kind;
        public long ProviderEpoch => planner.ProviderEpoch;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
            CancellationToken cancellationToken = default) => planner.DecideAsync(request, cancellationToken);
    }
}

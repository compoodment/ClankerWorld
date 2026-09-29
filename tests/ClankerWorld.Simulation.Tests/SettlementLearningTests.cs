using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class SettlementLearningTests
{
    [Fact]
    public async Task SimultaneousDeclineAndCancelDoesNotHaltTheRuntimeService()
    {
        var directory = Directory.CreateTempSubdirectory("lesson-terminal-");
        try
        {
            using var world = new PrivateWorldRuntime("lesson-decline-cancel-repro", actor => new CancellationRaceProvider(actor));
            world.StageStarterContent();
            var beforeRole = world.Society.GetInhabitant("founder-scout").CurrentRole;
            var file = new PrivateWorldStateFile(Path.Combine(directory.FullName, "world.json"));
            file.Save(world);
            var presence = new OwnerClientPresenceLease(TimeSpan.FromMinutes(5));
            presence.RecordAuthenticatedReconnect("test-owner");
            using var service = new PrivateWorldRuntimeService(world, file, presence);
            for (var tick = 0; tick < 3; tick++) Assert.True(await service.TryAdvanceOnceAsync());
            Assert.Equal("declined", world.Inhabitants.Single(person => person.InhabitantId == "founder-scout").Lesson!.Stage);
            Assert.Equal(beforeRole, world.Society.GetInhabitant("founder-scout").CurrentRole);
            Assert.False(world.Society.IsPaused);
            Assert.True(await service.TryAdvanceOnceAsync());
            using var reloaded = file.LoadOrCreate("lesson-decline-cancel-repro");
            Assert.Equal(world.WorldTick, reloaded.WorldTick);
        }
        finally { directory.Delete(recursive: true); }
    }

    private sealed class CancellationRaceProvider(string actor) : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var candidates = request.Observation.Candidates;
            var choice = actor == "founder-scout"
                ? candidates.FirstOrDefault(item => item.Id == "lesson_cancel") ?? candidates.FirstOrDefault(item => item.Id == "learn:farmer:founder-ilya")
                : actor == "founder-ilya" ? candidates.FirstOrDefault(item => item.Id == "lesson_decline:founder-scout") : null;
            choice ??= candidates.Single(item => item.Id == "safe_idle");
            return new DeterministicDecisionProvider().DecideAsync(request with { Observation = request.Observation with { Candidates = [choice] } }, cancellationToken);
        }
    }

    [Fact]
    public async Task BusyMentorGetsAnIndependentTeachingDecisionBeforeFinishingTheirProject()
    {
        var state = await PreparedState();
        var learner = state.Society.Society.Inhabitants.Single(person => person.CurrentRole == SocietyWorkRole.Trader).Id;
        using var requesting = PrivateWorldRuntime.Restore(state, actor => new LessonProvider(actor == learner ? "learn:builder:" : "safe_idle"));
        await requesting.AdvanceOneTickAsync();
        state = requesting.ExportState();
        var teacher = state.Inhabitants.Single(person => person.InhabitantId == learner).Lesson!.TeacherId;
        var definition = state.WorldContent!.Buildings[0];
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == teacher ? person with
            {
                Project = new("build:building:" + definition.CanonicalId, definition.DisplayName, requesting.WorldTick, "acquiring",
                LastTransitionTick: requesting.WorldTick),
            } : person).ToArray()
        };
        using var world = PrivateWorldRuntime.Restore(state, _ => new LessonProvider("lesson_accept:"));
        await world.AdvanceOneTickAsync();
        Assert.Equal("accepted", world.Inhabitants.Single(person => person.InhabitantId == learner).Lesson!.Stage);
        Assert.Equal(0, world.Inhabitants.Single(person => person.InhabitantId == teacher).Project!.WorkDone);
    }

    [Fact]
    public async Task AcceptedTrainingSurvivesPauseAndRestartAndUnlocksActualBuildingWork()
    {
        var state = await PreparedState();
        var learner = state.Society.Society.Inhabitants.Single(person => person.CurrentRole == SocietyWorkRole.Trader).Id;
        IDecisionProvider Provider(string actor) => new LessonProvider(actor == learner ? "learn:builder:" : "lesson_accept:");
        using var world = PrivateWorldRuntime.Restore(state, Provider);
        await world.AdvanceOneTickAsync();
        Assert.Equal("requested", world.Inhabitants.Single(person => person.InhabitantId == learner).Lesson!.Stage);
        Assert.Equal(SocietyWorkRole.Trader, world.Society.GetInhabitant(learner).CurrentRole);
        for (var tick = 0; tick < 80 && world.Inhabitants.Single(person => person.InhabitantId == learner).Lesson!.Progress < 3; tick++)
        {
            await world.AdvanceOneTickAsync();
        }
        var lesson = world.Inhabitants.Single(person => person.InhabitantId == learner).Lesson!;
        Assert.InRange(lesson.Progress, 3, 19);
        Assert.Equal(SocietyWorkRole.Trader, world.Society.GetInhabitant(learner).CurrentRole);
        Assert.Equal(lesson.Progress, new OwnerWorldObservationStore(world).GetSnapshot().Inhabitants.Single(person => person.Id == learner).Lesson!.Progress);
        world.Pause();
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), Provider);
        restored.Resume();
        for (var tick = 0; tick < 100 && restored.Inhabitants.Single(person => person.InhabitantId == learner).Project is null; tick++)
        {
            await restored.AdvanceOneTickAsync();
        }
        Assert.Equal(SocietyWorkRole.Builder, restored.Society.GetInhabitant(learner).CurrentRole);
        Assert.Equal("completed", restored.Inhabitants.Single(person => person.InhabitantId == learner).Lesson!.Stage);
        Assert.NotNull(restored.Inhabitants.Single(person => person.InhabitantId == learner).Project);
        Assert.Contains(restored.Society.Memories, memory => memory.OwnerId == learner && memory.Id.StartsWith("lesson-gratitude:", StringComparison.Ordinal));
        var completed = restored.Inhabitants.Single(person => person.InhabitantId == learner);
        Assert.Equal(2, completed.SocialStanding!.Single(item => item.SubjectId == completed.Lesson!.TeacherId).Trust);
    }

    [Fact]
    public async Task MentorRefusalDoesNotAssignTheRequestedRole()
    {
        var state = await PreparedState();
        var learner = state.Society.Society.Inhabitants.Single(person => person.CurrentRole == SocietyWorkRole.Trader).Id;
        using var world = PrivateWorldRuntime.Restore(state, actor => new LessonProvider(actor == learner ? "learn:builder:" : "lesson_decline:"));
        for (var tick = 0; tick < 5; tick++)
        {
            await world.AdvanceOneTickAsync();
        }
        Assert.Equal("declined", world.Inhabitants.Single(person => person.InhabitantId == learner).Lesson!.Stage);
        Assert.Equal(0, world.Inhabitants.Single(person => person.InhabitantId == learner).Lesson!.Progress);
        Assert.Equal(SocietyWorkRole.Trader, world.Society.GetInhabitant(learner).CurrentRole);
    }

    [Fact]
    public async Task MentorDeathCancelsTrainingWithoutGrantingAnUnearnedRole()
    {
        var state = await PreparedState();
        var learner = state.Society.Society.Inhabitants.Single(person => person.CurrentRole == SocietyWorkRole.Trader).Id;
        using var world = PrivateWorldRuntime.Restore(state, actor => new LessonProvider(actor == learner ? "learn:builder:" : "lesson_accept:"));
        await world.AdvanceOneTickAsync();
        await world.AdvanceOneTickAsync();
        state = world.ExportState();
        var mentor = state.Inhabitants.Single(person => person.InhabitantId == learner).Lesson!.TeacherId;
        using var society = SocietyWorldRuntime.Restore(state.Society);
        society.Apply(checkpoint => SocietyFixture.Kill(checkpoint, mentor, SocietyDeathCause.Accident, checkpoint.WorldTick));
        state = state with { Society = society.ExportState(), Inhabitants = state.Inhabitants.Where(person => person.InhabitantId != mentor).ToArray() };
        using var restored = PrivateWorldRuntime.Restore(state, _ => new LessonProvider("safe_idle"));
        await restored.AdvanceOneTickAsync();
        Assert.Equal("cancelled", restored.Inhabitants.Single(person => person.InhabitantId == learner).Lesson!.Stage);
        Assert.Equal(SocietyWorkRole.Trader, restored.Society.GetInhabitant(learner).CurrentRole);
    }

    [Fact]
    public async Task HungerInterruptsTrainingWithoutLosingProgressOrGrantingTheRole()
    {
        var state = await PreparedState();
        var learner = state.Society.Society.Inhabitants.Single(person => person.CurrentRole == SocietyWorkRole.Trader).Id;
        using var world = PrivateWorldRuntime.Restore(state, actor => new LessonProvider(actor == learner ? "learn:builder:" : "lesson_accept:"));
        for (var tick = 0; tick < 80 && world.Inhabitants.Single(person => person.InhabitantId == learner).Lesson?.Progress is not >= 3; tick++)
        {
            await world.AdvanceOneTickAsync();
        }
        state = world.ExportState();
        var progress = state.Inhabitants.Single(person => person.InhabitantId == learner).Lesson!.Progress;
        Assert.InRange(progress, 3, 19);
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == learner ? person with { HungerBasisPoints = 1_000 } : person).ToArray(),
        };
        using var restored = PrivateWorldRuntime.Restore(state, _ => new LessonProvider("safe_idle"));
        for (var tick = 0; tick < 10; tick++)
        {
            await restored.AdvanceOneTickAsync();
        }
        Assert.Equal(progress, restored.Inhabitants.Single(person => person.InhabitantId == learner).Lesson!.Progress);
        Assert.Equal(SocietyWorkRole.Trader, restored.Society.GetInhabitant(learner).CurrentRole);
    }

    [Fact]
    public async Task LessonJournalReportsBoundedStagesWithoutNames()
    {
        var directory = Directory.CreateTempSubdirectory("clankerworld-lesson-log-");
        try
        {
            var state = await PreparedState();
            var learner = state.Society.Society.Inhabitants.Single(person => person.CurrentRole == SocietyWorkRole.Trader).Id;
            state = state with
            {
                Society = state.Society with
                {
                    Society = state.Society.Society with
                    {
                        Inhabitants = state.Society.Society.Inhabitants.Select(person => person.Id == learner
                            ? person with { Name = "lesson-free-text-secret" } : person).ToArray(),
                    },
                },
            };
            using var world = PrivateWorldRuntime.Restore(state, actor => new LessonProvider(actor == learner ? "learn:builder:" : "safe_idle"));
            var presence = new OwnerClientPresenceLease(TimeSpan.FromSeconds(30));
            presence.RecordAuthenticatedReconnect("owner");
            var logger = new RecordingLogger<PrivateWorldRuntimeService>();
            using var service = new PrivateWorldRuntimeService(world, new PrivateWorldStateFile(Path.Combine(directory.FullName, "world.json")), presence, logger);
            Assert.True(await service.TryAdvanceOnceAsync());
            Assert.Contains(logger.Messages, message => message.Contains("settlement_lesson", StringComparison.Ordinal) &&
                message.Contains("event=lesson_requested", StringComparison.Ordinal));
            Assert.DoesNotContain(logger.Messages, message => message.Contains("lesson-free-text-secret", StringComparison.Ordinal));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task InvalidTrainingProgressAndOldSchemaFailClosed()
    {
        var state = await PreparedState();
        var learner = state.Inhabitants[0].InhabitantId;
        var mentor = state.Inhabitants[1].InhabitantId;
        var withLesson = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == learner
                ? person with { Lesson = new(mentor, SocietyWorkRole.Builder, "training", 21, 0, 0) } : person).ToArray(),
        };
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(withLesson));
        withLesson = withLesson with
        {
            SchemaVersion = 7,
            Inhabitants = withLesson.Inhabitants.Select(person => person.Lesson is { } lesson
                ? person with { Lesson = lesson with { Progress = 1 } } : person).ToArray(),
        };
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(withLesson));
    }

    private static async Task<PrivateWorldRuntimeState> PreparedState()
    {
        using var world = new PrivateWorldRuntime("settlement-learning", _ => new LessonProvider("safe_idle"));
        world.StageStarterContent();
        for (var tick = 0; tick < 5; tick++)
        {
            await world.AdvanceOneTickAsync();
        }
        var state = world.ExportState();
        return state with
        {
            Inhabitants = state.Inhabitants.Select(person => person with { LastDecisionContext = null }).ToArray(),
        };
    }

    private sealed class LessonProvider(string prefix) : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var candidate = request.Observation.Candidates.FirstOrDefault(item => item.Id.StartsWith(prefix, StringComparison.Ordinal))
                ?? (prefix == "learn:builder:" ? request.Observation.Candidates.FirstOrDefault(item => item.Id.StartsWith("build:building:", StringComparison.Ordinal)) : null)
                ?? request.Observation.Candidates.Single(item => item.Id == "safe_idle");
            return new DeterministicDecisionProvider().DecideAsync(request with
            {
                Observation = request.Observation with { Candidates = [candidate] },
            }, cancellationToken);
        }
    }
}

using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Tests;

public sealed class ChildhoodMemoryRecallTests
{
    [Theory]
    [InlineData("play")]
    [InlineData("converse")]
    [InlineData("learn")]
    public async Task GeneratedChildRecallsAnActuallyOfferedSocialExperienceAfterReload(string kind)
    {
        using var generated = NormalPathWorld.CreateGenerated("social-memory-d9efa858",
            _ => new ActionCoverageRecorder(chooseIdle: true));
        var state = generated.ExportState();
        var checkpoint = state.Society.Society;
        var householdId = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == "first-town-house-a").HouseholdId;
        var household = checkpoint.Households.Single(item => item.Id == householdId);
        var parent = household.MemberIds[0];
        var partner = household.MemberIds[1];
        checkpoint = SocietyFixture.ProposeRelationship(checkpoint, new("recall-partnership", 1,
            SocietyRelationshipType.Partnership, parent, partner, checkpoint.WorldTick)).Checkpoint;
        checkpoint = SocietyFixture.AcceptRelationship(checkpoint, "recall-partnership", 1, partner).Checkpoint;
        checkpoint = ChosenBirthNameTestFixture.NameParent(checkpoint, parent);
        checkpoint = checkpoint with
        {
            Inventory = InventoryFixture.AddLot(checkpoint.Inventory, "recall-birth-food", "food", household.Id, 4),
        };
        var birth = SocietyFixture.CommitBirth(checkpoint, new($"family:{parent}:{checkpoint.WorldTick}", 1,
            parent, partner, household.Id, household.MemberIds, [parent, partner], "recall-birth-food", 4,
            checkpoint.WorldTick, ChildName: ChosenBirthNameTestFixture.ChildName(checkpoint, parent, "Recall"),
            PrimaryCaregiverId: parent));
        var child = Assert.IsType<string>(birth.CreatedId);
        Assert.True(child.Length > 80);
        checkpoint = birth.Checkpoint;
        var age = Assert.IsType<SocietyDayLifecycle>(checkpoint.Config.DayLifecycle).ChildStartDay;
        var birthLifeTick = checkpoint.LifeTickAt(checkpoint.WorldTick) - age * checkpoint.Config.TicksPerLifecycleAge;
        checkpoint = checkpoint with
        {
            Inhabitants = checkpoint.Inhabitants.Select(person => person.Id == child ? person with
            {
                BirthTick = birthLifeTick,
                BirthLifeTick = checkpoint.LifeClock is null ? null : birthLifeTick,
                AgeBand = SocietyAgeBand.Child,
                LastLifecycleYearChecked = age,
            } : person).ToArray(),
        };
        var parentPosition = state.Inhabitants.Single(person => person.InhabitantId == parent).Position;
        var position = state.Map.FootNeighbors(parentPosition).First(point => state.Map.IsPassable(point) &&
            state.Inhabitants.All(person => person.Position != point));
        state = state with
        {
            JevEnabled = false,
            RoutineHelper = RoutineHelperSettings.Off,
            Society = state.Society with { Society = checkpoint },
            Inhabitants = state.Inhabitants.Append(new(child, position, 10_000, 0, "curious", "grow with the household")).ToArray(),
            Towns = state.Towns!.Select(town => town.ResidentIds.Contains(parent)
                ? town with { ResidentIds = town.ResidentIds.Append(child).Order(StringComparer.Ordinal).ToArray() }
                : town).ToArray(),
        };
        var choice = $"child_{kind}:{parent}";
        var chooser = new SocialProvider(choice);
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)),
            id => id == child ? chooser : new ActionCoverageRecorder(chooseIdle: true));
        for (var tick = 0; tick < 20 && !world.Society.Memories.Any(memory => memory.OwnerId == child); tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Contains(chooser.Observations, observation => observation.Candidates.Any(candidate => candidate.Id == choice));
        var memory = Assert.Single(world.Society.Memories, memory => memory.OwnerId == child);
        Assert.Equal(parent, memory.SubjectId);
        Assert.StartsWith($"child-social:{kind}:", memory.Id);
        Assert.InRange(memory.Id.Length, 1, 128);
        using var replay = PrivateWorldRuntime.Restore(state,
            id => id == child ? new SocialProvider(choice) : new ActionCoverageRecorder(chooseIdle: true));
        while (replay.WorldTick < world.WorldTick)
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(memory, Assert.Single(replay.Society.Memories, item => item.OwnerId == child));
        var saved = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        var recall = new SocialProvider("safe_idle");
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved),
            id => id == child ? recall : new ActionCoverageRecorder(chooseIdle: true));
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        restored.SubmitInstruction(new("recall-experience", "owner", child, OwnerInstructionKind.Suggestive,
            memory.Summary));
        for (var tick = 0; tick < 20 && recall.Observations.Count == 0; tick++)
            Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
        var observed = Assert.Single(recall.Observations);
        observed.Validate();
        var recalled = Assert.Single(observed.RetrievedMemories!, item => item.Id == memory.Id);
        Assert.Equal((child, parent, memory.Summary, memory.SourceTick),
            (recalled.OwnerId, recalled.SubjectId, recalled.Summary, recalled.SourceTick));
        Assert.DoesNotContain(observed.Candidates, candidate => candidate.Id == choice);
        Assert.Equal(memory, Assert.Single(restored.Society.Memories, item => item.Id == memory.Id));
        var again = PrivateWorldRuntimeCodec.Encode(restored.ExportState());
        using var roundtrip = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(again));
        Assert.Equal(again, PrivateWorldRuntimeCodec.Encode(roundtrip.ExportState()));
    }

    [Fact]
    public async Task MalformedMemoryKeysRemainExcludedFromPersonalRecall()
    {
        using var seed = new PrivateWorldRuntime("child-memory-validation");
        var state = seed.ExportState();
        var valid = new SocietySocialMemory("valid-memory", "founder-scout", "founder-mira", "A shared game.", "public", 0);
        var memories = new[]
        {
            valid,
            valid with { Id = new string('x', 129) },
            valid with { Id = " padded-memory" },
            valid with { Id = "control\u0001memory" },
        };
        state = state with
        {
            JevEnabled = false,
            RoutineHelper = RoutineHelperSettings.Off,
            Society = state.Society with
            {
                Society = state.Society.Society with { Memories = memories.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray() },
            },
        };
        var capture = new SocialProvider("safe_idle");
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)),
            id => id == "founder-scout" ? capture : new ActionCoverageRecorder(chooseIdle: true));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var observation = Assert.Single(capture.Observations);
        observation.Validate();
        Assert.Equal(valid.Id, Assert.Single(observation.RetrievedMemories!).Id);
        Assert.Equal(memories.OrderBy(item => item.Id, StringComparer.Ordinal), world.Society.Memories);
    }

    private sealed class SocialProvider(string choice) : IDecisionProvider
    {
        public List<InhabitantObservation> Observations { get; } = [];
        public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
        public long ProviderEpoch => 1;

        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            Observations.Add(request.Observation);
            var selected = request.Observation.Candidates.Any(candidate => candidate.Id == choice) ? choice : "safe_idle";
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId,
                request.Observation.InhabitantId, Kind, ProviderEpoch, request.Observation.RunEpoch,
                request.Observation.DecisionGeneration, request.Observation.ObservationDigest, selected, 1,
                request.Observation.Candidates.ToDictionary(candidate => candidate.Id,
                    candidate => candidate.Id == selected ? 1d : 0d, StringComparer.Ordinal)));
        }
    }
}

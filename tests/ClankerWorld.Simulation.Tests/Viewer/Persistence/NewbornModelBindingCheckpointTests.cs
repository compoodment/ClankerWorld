using System.Collections.Concurrent;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.World;
using ClankerWorld.Viewer.Control;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class NewbornModelBindingCheckpointTests
{
    private const string First = "founder:00000000000000000000000000000001";
    private const string Second = "founder:00000000000000000000000000000002";
    private static readonly ConcurrentDictionary<int, Lazy<Task<byte[]>>> Prepared = new();

    [Theory]
    [InlineData(9, false)]
    [InlineData(78, false)]
    [InlineData(9, true)]
    [InlineData(78, true)]
    public async Task ARealNewbornBindsBothRolesAndRecoversItsSavedChoiceWithLongGeneratedIds(int seedSuffix, bool delayBinding)
    {
        var state = PrivateWorldRuntimeCodec.Decode(await Prepared.GetOrAdd(seedSuffix,
            length => new(() => BeforeBirth(length))).Value);
        var directory = Directory.CreateTempSubdirectory("newborn-model-checkpoint-");
        try
        {
            var providerDirectory = Path.Combine(directory.FullName, "providers");
            var providerPath = Path.Combine(providerDirectory, "choices.json");
            var providers = new ProviderConfigurationStore(providerPath, EmptySeed());
            _ = providers.Configure(new("personal", "deterministic", null, null, false, First));
            _ = providers.Configure(new("personal", "deterministic", null, null, false, Second));
            var file = new PrivateWorldStateFile(Path.Combine(directory.FullName, "world.json"));
            var presence = new OwnerClientPresenceLease(TimeSpan.FromHours(1));
            presence.RecordAuthenticatedReconnect("test-owner");
            var logger = new RecordingLogger<PrivateWorldRuntimeService>();
            using var world = PrivateWorldRuntime.Restore(state, Providers);
            file.Save(world);
            if (delayBinding)
            {
                // Actual provider-write failure after the birth checkpoint commits.
                // Preserve the parents' original configuration for restart recovery.
                Directory.Move(providerDirectory, providerDirectory + ".held");
                File.WriteAllText(providerDirectory, "controlled provider storage failure");
            }
            using (var service = new PrivateWorldRuntimeService(world, file, presence, logger, providers: providers))
            {
                var advanced = await service.TryAdvanceOnceAsync();
                if (delayBinding)
                {
                    Assert.False(advanced);
                    Assert.True(world.Society.IsPaused);
                    var born = Assert.Single(world.Society.Births);
                    Assert.DoesNotContain(providers.CaptureStatus().Assignments!, row => row.InhabitantId == born.ChildId);
                    Assert.Contains(logger.Messages, message => message.Contains("waiting_for_child_model_binding", StringComparison.Ordinal));
                }
                else
                {
                    Assert.True(advanced, string.Join("\n", logger.Messages));
                    Assert.False(world.Society.IsPaused);
                }
            }
            var birth = Assert.Single(world.Society.Births);
            var childId = birth.ChildId;
            Assert.Equal(seedSuffix == 78, childId.Length > 128);
            Assert.Equal(SocietyAgeBand.Infant, world.Society.GetInhabitant(childId).AgeBand);
            var child = world.Inhabitants.Single(person => person.InhabitantId == childId);
            Assert.Equal("deterministic", child.ChildModelSelection!.Provider);
            Assert.Equal(PrivateWorldRuntime.ChildModelChoiceParentsAgreed, child.ChildModelSelection.ChoiceReason);
            Assert.Equal(world.WorldTick, PrivateWorldRuntimeCodec.Decode(File.ReadAllBytes(file.Path)).Society.Society.WorldTick);
            if (!delayBinding) AssertBound(providers, childId);
            else
            {
                File.Delete(providerDirectory);
                Directory.Move(providerDirectory + ".held", providerDirectory);
            }

            // Recover the actual saved newborn, including the no-assignment case.
            var saved = File.ReadAllBytes(file.Path);
            using var restarted = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved), Providers);
            Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(restarted.ExportState()));
            var reopened = new ProviderConfigurationStore(providerPath, EmptySeed());
            using var restartedService = new PrivateWorldRuntimeService(restarted, file, presence, logger, providers: reopened);
            restarted.Resume();
            Assert.True(await restartedService.TryAdvanceOnceAsync(), string.Join("\n", logger.Messages));
            AssertBound(reopened, childId);
            var boundRevision = reopened.CaptureRuntimeConfiguration().Revision;
            restarted.Pause();
            restarted.Resume();
            for (var step = 0; step < 2; step++)
            {
                Assert.True(await restartedService.TryAdvanceOnceAsync(), string.Join("\n", logger.Messages));
                Assert.False(restarted.Society.IsPaused);
                Assert.Equal(restarted.WorldTick, PrivateWorldRuntimeCodec.Decode(File.ReadAllBytes(file.Path)).Society.Society.WorldTick);
            }
            Assert.Equal(boundRevision, reopened.CaptureRuntimeConfiguration().Revision);
            AssertBound(new ProviderConfigurationStore(providerPath, EmptySeed()), childId);

            // The owner can override one role using the same generated identity.
            // Recovery must retain that choice and the separate birth history.
            _ = reopened.Configure(new(PlayerDecisionProviders.PlanningRole, "deterministic", null, null, false, childId));
            var overrideRevision = reopened.CaptureRuntimeConfiguration().Revision;
            Assert.True(await restartedService.TryAdvanceOnceAsync(), string.Join("\n", logger.Messages));
            Assert.Equal(overrideRevision, reopened.CaptureRuntimeConfiguration().Revision);
            var selected = new ProviderConfigurationStore(providerPath, EmptySeed()).CaptureStatus().Assignments!;
            Assert.Null(Assert.Single(selected, row => row.InhabitantId == childId && row.Role == PlayerDecisionProviders.PlanningRole).SelectionReason);
            Assert.Equal(PrivateWorldRuntime.ChildModelChoiceParentsAgreed,
                Assert.Single(selected, row => row.InhabitantId == childId && row.Role == PlayerDecisionProviders.RoutineRole).SelectionReason);
            Assert.Single(restarted.Society.Births);
            Assert.Equal(child.ChildModelSelection,
                restarted.Inhabitants.Single(person => person.InhabitantId == childId).ChildModelSelection);
            using var final = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(File.ReadAllBytes(file.Path)), Providers);
            Assert.Equal(File.ReadAllBytes(file.Path), PrivateWorldRuntimeCodec.Encode(final.ExportState()));
        }
        finally { directory.Delete(recursive: true); }
    }

    private static async Task<byte[]> BeforeBirth(int seedSuffix)
    {
        using var generated = NormalPathWorld.CreateGenerated("social-talk-" + new string('x', seedSuffix), Providers);
        var state = generated.ExportState();
        var household = state.Society.Society.GetInhabitant(First).HouseholdId!;
        var home = state.WorldSimulation!.Buildings.Single(building => building.HouseholdId == household &&
            state.WorldContent!.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId).Tags.Contains("house"));
        using (var society = SocietyWorldRuntime.Restore(state.Society))
        {
            society.Apply(checkpoint => SocietyFixture.ProposeRelationship(checkpoint,
                new("model-birth-parents", 1, SocietyRelationshipType.Partnership, First, Second, checkpoint.WorldTick)));
            society.Apply(checkpoint => SocietyFixture.AcceptRelationship(checkpoint, "model-birth-parents", 1, Second));
            state = state with { Society = society.ExportState() };
        }
        state = state with
        {
            Society = state.Society with
            {
                Society = state.Society.Society with
                {
                    Inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "model-birth-food", "food", household, 8,
                    storageBuildingId: home.InstanceId),
                }
            },
            Inhabitants = state.Inhabitants.Select(person => person with
            {
                Position = person.InhabitantId is First or Second ? home.Position : person.Position,
                HungerBasisPoints = 10_000,
                LastDecisionContext = null,
            }).ToArray(),
        };
        state = ShelterOrderTestFixture.WithClearWeather(state);
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)), Providers);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var plan = world.Inhabitants.Single(person => person.InhabitantId == First).Parenthood!;
        Assert.Equal("preparing", plan.Stage);
        // Advance the real waiting ticks; no clock, preparation or birth is fabricated.
        while (world.WorldTick < plan.LastTransitionTick + 599)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Empty(world.Society.Births);
        return PrivateWorldRuntimeCodec.Encode(world.ExportState());
    }

    private static ProviderConfigurationSeed EmptySeed() => new("deterministic", null, null, null, null, null, null);

    private static void AssertBound(ProviderConfigurationStore providers, string childId)
    {
        var rows = providers.CaptureStatus().Assignments!.Where(row => row.InhabitantId == childId).ToArray();
        Assert.Equal(new[] { PlayerDecisionProviders.PlanningRole, PlayerDecisionProviders.RoutineRole }.Order(), rows.Select(row => row.Role).Order());
        Assert.All(rows, row =>
        {
            Assert.Equal("deterministic", row.Provider);
            Assert.Equal(PrivateWorldRuntime.ChildModelChoiceParentsAgreed, row.SelectionReason);
            Assert.Null(row.CredentialSlotId);
        });
    }

    private static IDecisionProvider Providers(string actor) => new ParentChoices(actor);

    private sealed class ParentChoices(string actor) : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var prefix = actor == First ? "parent_propose:" : actor == Second ? "parent_accept:" : "safe_idle";
            var selected = request.Observation.Candidates.FirstOrDefault(candidate => candidate.Id.StartsWith(prefix, StringComparison.Ordinal))
                ?? request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle");
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, request.Observation.InhabitantId, Kind, request.ProviderEpoch,
                request.Observation.RunEpoch, request.Observation.DecisionGeneration, request.Observation.ObservationDigest,
                selected.Id, 1, new Dictionary<string, double> { [selected.Id] = 1 }));
        }
    }
}

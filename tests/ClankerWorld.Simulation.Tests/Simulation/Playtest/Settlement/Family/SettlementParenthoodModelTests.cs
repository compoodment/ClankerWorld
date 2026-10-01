using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Viewer.Control;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class SettlementParenthoodTests
{
    [Fact]
    public async Task NewbornKeepsInitiatingParentsModelAcrossSaveAndUsesItOnlyAfterInfancy()
    {
        var directory = Directory.CreateTempSubdirectory("clankerworld-child-model-birth-");
        try
        {
            var state = await PreparedState();
            var initiatorId = state.Inhabitants[0].InhabitantId;
            var partnerId = state.Inhabitants[1].InhabitantId;
            var callCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            IDecisionProvider ProviderFor(string id) => id == initiatorId
                ? new ParentProvider("parent_propose:")
                : id == partnerId
                    ? new ParentProvider("parent_accept:")
                    : new CountingProvider(id, callCounts);

            var providers = new ProviderConfigurationStore(Path.Combine(directory.FullName, "providers.json"),
                new ProviderConfigurationSeed("deterministic", null, null, null, null, null, null));
            _ = providers.Configure(new("personal", "openai", "initiating-parent-model", "initiating-parent-secret",
                false, initiatorId));
            _ = providers.Configure(new("personal", "ollama-cloud", "other-parent-model", "other-parent-secret",
                false, partnerId));

            using var world = PrivateWorldRuntime.Restore(state, ProviderFor);
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            for (var tick = 0; tick < 599; tick++)
                Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.Empty(world.Society.Births);

            var stateFile = new PrivateWorldStateFile(Path.Combine(directory.FullName, "world.json"), ProviderFor);
            var presence = new OwnerClientPresenceLease(TimeSpan.FromHours(1));
            presence.RecordAuthenticatedReconnect("owner");
            using var service = new PrivateWorldRuntimeService(world, stateFile, presence, providers: providers);
            Assert.True(await service.TryAdvanceOnceAsync());

            var birth = Assert.Single(world.Society.Births);
            var childId = birth.ChildId;
            var child = world.Inhabitants.Single(item => item.InhabitantId == childId);
            Assert.Equal(SocietyAgeBand.Infant, world.Society.GetInhabitant(childId).AgeBand);
            Assert.Equal("initiating-parent-model", child.ChildModelSelection!.ModelId);
            Assert.Equal("openai", child.ChildModelSelection.Provider);
            Assert.Equal(PrivateWorldRuntime.OpenAiModelEndpointIdentity, child.ChildModelSelection.EndpointIdentity);
            Assert.Equal(PrivateWorldRuntime.ChildModelChoiceInitiatingParent, child.ChildModelSelection.ChoiceReason);
            Assert.Equal(0, callCounts.GetValueOrDefault(childId));

            var childAssignments = providers.CaptureStatus().Assignments!
                .Where(item => item.InhabitantId == childId).ToArray();
            Assert.Equal(2, childAssignments.Length);
            Assert.All(childAssignments, assignment =>
            {
                Assert.Equal("openai", assignment.Provider);
                Assert.Equal("initiating-parent-model", assignment.Model);
                Assert.Equal(PrivateWorldRuntime.ChildModelChoiceInitiatingParent, assignment.SelectionReason);
            });

            var savedText = System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(stateFile.Path));
            Assert.DoesNotContain("initiating-parent-secret", savedText, StringComparison.Ordinal);
            Assert.DoesNotContain("other-parent-secret", savedText, StringComparison.Ordinal);
            Assert.Contains("initiating-parent-model", savedText, StringComparison.Ordinal);

            using var restored = stateFile.LoadOrCreate("settlement-parenthood");
            Assert.Equal(child.ChildModelSelection,
                restored.Inhabitants.Single(item => item.InhabitantId == childId).ChildModelSelection);
            var restoredProviders = new ProviderConfigurationStore(providers.Path,
                new ProviderConfigurationSeed("deterministic", null, null, null, null, null, null));
            Assert.Equal("initiating-parent-model", Assert.Single(restoredProviders.CaptureStatus().Assignments!,
                item => item.InhabitantId == childId && item.Role == "planning").Model);

            var saved = restored.ExportState();
            var hungryChildState = saved with
            {
                Inhabitants = saved.Inhabitants.Select(item => item.InhabitantId == childId
                    ? item with { HungerBasisPoints = 500 } : item).ToArray(),
            };
            using var agingWorld = PrivateWorldRuntime.Restore(hungryChildState, ProviderFor);
            agingWorld.Pause();
            Assert.True(agingWorld.SetLifePace(1_460));
            agingWorld.Resume();
            var maturityTicks = agingWorld.Society.Config.TicksPerWorldDay;
            for (var tick = 0; tick < maturityTicks && callCounts.GetValueOrDefault(childId) == 0; tick++)
                Assert.True((await agingWorld.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(SocietyAgeBand.Child, agingWorld.Society.GetInhabitant(childId).AgeBand);
            Assert.True(callCounts.GetValueOrDefault(childId) > 0);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private sealed class CountingProvider(string inhabitantId, Dictionary<string, int> callCounts) : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;

        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            callCounts[inhabitantId] = callCounts.GetValueOrDefault(inhabitantId) + 1;
            return new DeterministicDecisionProvider().DecideAsync(request, cancellationToken);
        }
    }
}

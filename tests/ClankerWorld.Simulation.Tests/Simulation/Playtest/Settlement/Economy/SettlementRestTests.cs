using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class SettlementRestTests
{
    [Fact]
    public async Task NewPrivateWorldHasNoBedrollBeddingRecipeOrEnergyObservation()
    {
        using var world = new PrivateWorldRuntime("new-needs-content");
        world.StageStarterContent();
        for (var tick = 0; tick < 3; tick++) await world.AdvanceOneTickAsync();

        Assert.DoesNotContain(world.ExportState().Map.CampObjects, item => item.Kind == "bedroll");
        Assert.DoesNotContain(world.WorldContent.Recipes,
            recipe => recipe.Outputs.Any(output => output.ResourceId == "bedding"));
        var observation = new OwnerWorldObservationStore(world).GetSnapshot();
        Assert.DoesNotContain("EnergyBasisPoints", JsonSerializer.Serialize(observation), StringComparison.Ordinal);
    }

    [Fact]
    public async Task IllnessAndHungerNeverOfferSleepAndOrdinaryTicksStillAdvance()
    {
        using var seed = new PrivateWorldRuntime("needs-without-sleep");
        var state = seed.ExportState();
        var provider = new RecordingProvider();
        using var world = PrivateWorldRuntime.Restore(state with
        {
            Survival = new SettlementSurvivalState(0, []),
            Inhabitants = state.Inhabitants.Select(person => person with
            {
                HungerBasisPoints = 2_500,
                Survival = new SurvivalCondition(WarmthBasisPoints: 2_000, IllnessBasisPoints: 7_000),
            }).ToArray(),
        }, _ => provider);

        var result = await world.AdvanceOneTickAsync();

        Assert.True(result.Advanced);
        Assert.Equal(1, result.WorldTick);
        Assert.NotEmpty(provider.Candidates);
        Assert.DoesNotContain(provider.Candidates, id => id == "sleep");
        Assert.DoesNotContain(result.Events, item => item.Kind is "inhabitant_slept" or "inhabitant_rested_outdoors");
        Assert.Contains(world.Inhabitants, person => person.HungerBasisPoints != 2_500);
        Assert.Contains(provider.Candidates, id => id is "consume_food" or "seek_food" or "harvest_food" or "collect_shared_food");
    }

    [Fact]
    public async Task LegacyBedrollWorldLoadsWithoutRestoringEnergyOrShowingBedroll()
    {
        const string seedText = "legacy-needs-migration";
        using var seed = new PrivateWorldRuntime(seedText);
        var state = seed.ExportState() with
        {
            SchemaVersion = 17,
            Map = SeededMapGenerator.Generate(seedText, includeLegacyBedroll: true),
        };
        var encoded = Encoding.UTF8.GetString(PrivateWorldRuntimeCodec.Encode(state));
        var withOldEnergy = encoded.Replace("\"hungerBasisPoints\":6500,",
            "\"hungerBasisPoints\":6500,\"energyBasisPoints\":0,", StringComparison.Ordinal);
        Assert.NotEqual(encoded, withOldEnergy);
        var decoded = PrivateWorldRuntimeCodec.Decode(Encoding.UTF8.GetBytes(withOldEnergy));
        using var world = PrivateWorldRuntime.Restore(decoded);

        Assert.Equal(PrivateWorldRuntime.StateSchemaVersion, world.ExportState().SchemaVersion);
        Assert.DoesNotContain("energyBasisPoints", Encoding.UTF8.GetString(PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        Assert.DoesNotContain(new OwnerWorldObservationStore(world).GetSnapshot().Objects,
            item => item.Kind == "bedroll");
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        using var reloaded = PrivateWorldRuntime.Restore(
            PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        Assert.Equal(world.WorldTick, reloaded.WorldTick);
    }

    [Fact]
    public void LegacyGeneratedMapKeepsItsVerifiedResourceTopologyOnLoad()
    {
        var options = new GeographyOptions("legacy-generated-needs", WorldSizePreset.Small, WrapEastWest: true);
        using var seed = new PrivateWorldRuntime(options.Seed,
            startPace: WorldStartPace.FounderSetup, geographyOptions: options);
        var legacyMap = GeneratedCampMapGenerator.Generate(options, includeLegacyBedroll: true);
        var state = seed.ExportState() with { SchemaVersion = 17, Map = legacyMap };

        using var restored = PrivateWorldRuntime.Restore(state);

        Assert.Equal(legacyMap.ManifestDigest, restored.ExportState().Map.ManifestDigest);
        Assert.Equal(legacyMap.Resources, restored.ExportState().Map.Resources);
        Assert.DoesNotContain(new OwnerWorldObservationStore(restored).GetSnapshot().Objects,
            item => item.Kind == "bedroll");
        Assert.Equal(PrivateWorldRuntime.StateSchemaVersion, restored.ExportState().SchemaVersion);
    }

    private sealed class RecordingProvider : IDecisionProvider
    {
        private readonly ConcurrentBag<string> candidates = [];
        public IReadOnlyCollection<string> Candidates => candidates.ToArray();
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            foreach (var candidate in request.Observation.Candidates)
                candidates.Add(candidate.Id);
            return new DeterministicDecisionProvider().DecideAsync(request, cancellationToken);
        }
    }
}

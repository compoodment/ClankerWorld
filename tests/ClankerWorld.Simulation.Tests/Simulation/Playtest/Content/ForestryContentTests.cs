using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed class ForestryContentTests
{
    [Fact]
    public async Task AdditiveForestryWaitsForResumeAndRespectsRollbackAcrossRestart()
    {
        using var seed = new PrivateWorldRuntime("forestry-migration", _ => new IdleProvider());
        seed.StageStarterContent();
        for (var tick = 0; tick < 2; tick++) await seed.AdvanceOneTickAsync();
        seed.Pause();
        var before = PrivateWorldRuntimeCodec.Encode(seed.ExportState());
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(before), _ => new IdleProvider());
        Assert.False((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        Assert.DoesNotContain(world.WorldContent.Recipes, recipe => recipe.LocalId == "managed-coppice");
        world.Resume();
        await world.AdvanceOneTickAsync();
        Assert.Single(world.WorldContent.Recipes, recipe => recipe.LocalId == "managed-coppice");
        world.RollbackContent(ForestryContent.PackageId, "test withdrawal");
        using var restored = PrivateWorldRuntime.Restore(world.ExportState(), _ => new IdleProvider());
        for (var tick = 0; tick < 3; tick++) await restored.AdvanceOneTickAsync();
        Assert.DoesNotContain(restored.WorldContent.Recipes, recipe => recipe.LocalId == "managed-coppice");
        Assert.Equal(ContentPackageLifecycle.Quarantined,
            restored.ExportState().Content!.Packages.Single(package => package.Manifest.PackageId == ForestryContent.PackageId).Lifecycle);
    }

    [Fact]
    public async Task ManagedTimberTakesAFullDayAndDoesNotRefillWildWood()
    {
        using var seed = new PrivateWorldRuntime("forestry-growth", _ => new IdleProvider());
        seed.StageStarterContent();
        for (var tick = 0; tick < 3; tick++) await seed.AdvanceOneTickAsync();
        var state = seed.ExportState();
        var site = state.Map.GetResource(SeededMapGenerator.FertileLandResourceId).Position;
        var worker = state.Inhabitants[0];
        state = state with
        {
            Society = state.Society with
            {
                Society = state.Society.Society with
                {
                    Inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "forestry-seeds", "seed", "household:camp-alpha", 2),
                },
            },
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == worker.InhabitantId
                ? person with { Position = site } : person.Position == site ? person with { Position = worker.Position } : person).ToArray(),
        };
        using var world = PrivateWorldRuntime.Restore(state, _ => new IdleProvider());
        var recipe = world.WorldContent.Recipes.Single(recipe => recipe.LocalId == "managed-coppice");
        Assert.Equal(KernelClock.TicksPerDay, recipe.DurationTicks);
        var wildWood = world.WorldSystems.Ecology.Resources.Single(resource => resource.Kind == "construction").Quantity;
        var started = world.StartProduction(recipe.CanonicalId, WorldBuildSiteRules.FertileLandSiteId(site), worker.InhabitantId);
        Assert.True(started.Applied, started.Failure);
        for (var tick = 1; tick < recipe.DurationTicks; tick++) await world.AdvanceOneTickAsync();
        Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.Id == started.JobId + ":output:00");
        using var restored = PrivateWorldRuntime.Restore(world.ExportState(), _ => new IdleProvider());
        await restored.AdvanceOneTickAsync();
        var wood = restored.Society.Inventory.Lots.Single(lot => lot.ItemKind == "wood" && lot.Id.StartsWith(started.JobId + ":output:", StringComparison.Ordinal));
        Assert.Equal("wood", wood.ItemKind);
        Assert.Equal(24, wood.Quantity);
        Assert.Equal(wildWood, restored.WorldSystems.Ecology.Resources.Single(resource => resource.Kind == "construction").Quantity);
        Assert.Equal(WorldProductionJobState.Completed, restored.WorldSimulation.CropBuilds!.Single(job => job.JobId == started.JobId).State);
    }

    private sealed class IdleProvider : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default) =>
            new DeterministicDecisionProvider().DecideAsync(request with
            {
                Observation = request.Observation with { Candidates = request.Observation.Candidates.Where(candidate => candidate.Id == "safe_idle").ToArray() },
            }, cancellationToken);
    }
}

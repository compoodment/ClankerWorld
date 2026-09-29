using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class PrivateWorldRuntimeTests
{
    [Theory]
    [InlineData("acquiring")]
    [InlineData("completed")]
    public async Task RollbackPreservesRecordedProjectReferences(string stage)
    {
        using var seed = new PrivateWorldRuntime("rollback-project",
            _ => new CountingSelectingProvider(DecisionProviderKind.Deterministic, chooseIdle: true));
        var (package, building, _) = MaterialPackage(2);
        Activate(seed, package);
        await seed.AdvanceOneTickAsync();
        var state = seed.ExportState();
        var actor = state.Inhabitants[0].InhabitantId;
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor ? person with
            {
                Project = new SettlementProject("build:building:" + building.CanonicalId, "Workshop", seed.WorldTick,
                    stage, stage == "completed" ? 10 : 0, LastTransitionTick: seed.WorldTick),
            } : person).ToArray(),
        };
        using var world = PrivateWorldRuntime.Restore(state);
        world.Pause();
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.Throws<InvalidOperationException>(() => world.RollbackContent(package.PackageId, "withdraw"));
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task RollbackWithCommittedProductionReferencesRejectsWithoutDeletingWorldState(bool crop, bool completed)
    {
        var (world, usedPackage, _, jobId) = await WorldWithRollbackJob(crop);
        using (world)
        {
            if (completed)
            {
                await world.AdvanceOneTickAsync();
                await world.AdvanceOneTickAsync();
            }
            world.Pause();
            var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
            var error = Assert.Throws<InvalidOperationException>(() => world.RollbackContent(usedPackage, "withdraw content"));
            Assert.Contains("migration", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
            using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(before),
                _ => new CountingSelectingProvider(DecisionProviderKind.Deterministic, chooseIdle: true));
            Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
            restored.Resume();
            await restored.AdvanceOneTickAsync();
            await restored.AdvanceOneTickAsync();
            Assert.Equal(WorldProductionJobState.Completed, restored.WorldSimulation.ProductionJobs
                .Concat(restored.WorldSimulation.CropBuilds ?? []).Single(job => job.JobId == jobId).State);
        }
    }

    [Theory]
    [InlineData(false)]
    public async Task WithdrawingUnusedPackageDoesNotCancelOtherPackagesWork(bool crop)
    {
        var (world, _, unusedPackage, jobId) = await WorldWithRollbackJob(crop);
        using (world)
        {
            var beforeInventory = world.Society.Inventory;
            var receipt = world.RollbackContent(unusedPackage, "unused package");
            Assert.Equal(ContentPackageLifecycle.Quarantined, receipt.Lifecycle);
            Assert.Equal(beforeInventory, world.Society.Inventory);
            Assert.Equal(WorldProductionJobState.Running, world.WorldSimulation.ProductionJobs
                .Concat(world.WorldSimulation.CropBuilds ?? []).Single(job => job.JobId == jobId).State);
            await world.AdvanceOneTickAsync();
            await world.AdvanceOneTickAsync();
            Assert.Equal(WorldProductionJobState.Completed, world.WorldSimulation.ProductionJobs
                .Concat(world.WorldSimulation.CropBuilds ?? []).Single(job => job.JobId == jobId).State);
            using var restored = PrivateWorldRuntime.Restore(world.ExportState());
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        }
    }

    private static async Task<(PrivateWorldRuntime World, string Used, string Unused, string JobId)> WorldWithRollbackJob(bool crop)
    {
        using var seed = new PrivateWorldRuntime("rollback-safety",
            _ => new CountingSelectingProvider(DecisionProviderKind.Deterministic, chooseIdle: true));
        var (workPackage, building, workRecipe) = MaterialPackage(2);
        var (cropPackage, cropRecipe) = CropPackage();
        Activate(seed, workPackage);
        Activate(seed, cropPackage);
        await seed.AdvanceOneTickAsync();
        var state = seed.ExportState();
        var worker = state.Inhabitants[0];
        var site = crop ? state.Map.GetResource(SeededMapGenerator.FertileLandResourceId).Position :
            state.Map.Tiles.First(tile => state.Map.IsPassable(tile.Position) &&
                !state.Map.CampObjects.Any(item => item.Position == tile.Position) &&
                !state.Map.Resources.Any(item => item.Position == tile.Position) &&
                !state.Inhabitants.Any(person => person.InhabitantId != worker.InhabitantId && person.Position == tile.Position)).Position;
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == worker.InhabitantId ? person with { Position = site }
                : person.Position == site ? person with { Position = worker.Position } : person).ToArray(),
        };
        var world = PrivateWorldRuntime.Restore(state,
            _ => new CountingSelectingProvider(DecisionProviderKind.Deterministic, chooseIdle: true));
        var workstation = WorldBuildSiteRules.FertileLandSiteId(site);
        if (!crop)
        {
            var placed = world.PlaceBuilding("rollback-workshop", building.CanonicalId, site);
            Assert.True(placed.Applied, placed.Failure);
            workstation = placed.InstanceId;
        }
        var started = world.StartProduction((crop ? cropRecipe : workRecipe).CanonicalId, workstation, worker.InhabitantId);
        Assert.True(started.Applied, started.Failure);
        Assert.NotNull(started.JobId);
        return (world, crop ? cropPackage.PackageId : workPackage.PackageId,
            crop ? workPackage.PackageId : cropPackage.PackageId, started.JobId);
    }
}

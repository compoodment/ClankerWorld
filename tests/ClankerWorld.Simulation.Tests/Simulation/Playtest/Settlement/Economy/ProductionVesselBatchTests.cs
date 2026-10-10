using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class ToolMakingRequestTests
{
    [Theory]
    [InlineData("water_jug", 1)]
    [InlineData("water_jug", 2)]
    [InlineData("water_jug", 3)]
    [InlineData("storage_pot", 1)]
    [InlineData("storage_pot", 2)]
    [InlineData("handcart", 1)]
    [InlineData("handcart", 2)]
    [InlineData("wood", 3)]
    public async Task AcceptedVesselBatchesCompleteAsPhysicalItemsThroughTheHostAndReplay(string kind, int amount)
    {
        var (state, _, worker, shop) = Prepared();
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == worker
                ? person with { Position = shop.Position } : person).ToArray(),
        };
        if (kind == InventoryContainerRules.Handcart)
        {
            var inventory = InventoryFixture.Transfer(state.Society.Society.Inventory, "cart-starting-wood",
                shop.HouseholdId!, worker, "smith-input", 3, "controlled-starting-cargo");
            state = state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } };
        }
        using var setup = PrivateWorldRuntime.Restore(state, _ => new RequestChoices(DecisionProviderKind.Deterministic));
        var package = VesselBatchRecipe(kind, amount, shop.DefinitionId);
        setup.ProposeContent(package);
        var resolution = setup.ResolveContent(package.PackageId);
        Assert.True(resolution.IsSuccess, resolution.Diagnostic);
        setup.ValidateContent(package.PackageId, resolution);
        setup.ApproveContent(package.PackageId);
        setup.StageContent(package.PackageId);
        Assert.True((await setup.AdvanceOneTickAsync()).Advanced);
        var recipe = Assert.Single(setup.WorldContent.Recipes, item => item.PackageDigest == package.PackageDigest);
        state = Roundtrip(setup);
        using var admitting = PrivateWorldRuntime.Restore(state, _ => new RequestChoices(DecisionProviderKind.Deterministic));
        var start = admitting.StartProduction(recipe.CanonicalId, shop.InstanceId, worker);
        Assert.True(start.Applied, start.Failure);
        var running = Assert.Single(admitting.WorldSimulation.ProductionJobs, job => job.JobId == start.JobId);
        Assert.Equal(WorldProductionJobState.Running, running.State);
        Assert.Equal(3, running.InputReservationIds.Sum(id => admitting.Society.Inventory.GetReservation(id).Quantity));
        state = Roundtrip(admitting);
        using var world = PrivateWorldRuntime.Restore(state, _ => new RequestChoices(DecisionProviderKind.Deterministic));
        using var replay = PrivateWorldRuntime.Restore(state, _ => new RequestChoices(DecisionProviderKind.Deterministic));
        using var host = new VesselBatchCheckpointHost(world);
        using var replayHost = new VesselBatchCheckpointHost(replay);
        world.Resume();
        replay.Resume();
        while (world.WorldTick + 1 < running.CompletionTick)
        {
            await host.Advance();
            await replayHost.Advance();
            Assert.Equal(host.Saved(), replayHost.Saved());
        }
        var beforeCompletion = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False((await world.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(beforeCompletion, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        await host.Advance();
        await replayHost.Advance();
        Assert.Equal(host.Saved(), replayHost.Saved());
        Assert.Equal(WorldProductionJobState.Completed,
            Assert.Single(world.WorldSimulation.ProductionJobs, job => job.JobId == start.JobId).State);
        var outputs = world.Society.Inventory.Lots.Where(lot => lot.Id.StartsWith(start.JobId + ":output:", StringComparison.Ordinal)).ToArray();
        Assert.Equal(amount, outputs.Sum(lot => lot.Quantity));
        Assert.Equal(InventoryContainerRules.IsContainer(kind) ? amount : 1, outputs.Length);
        Assert.Equal(outputs.Length, outputs.Select(lot => lot.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(outputs, lot => lot.Id == start.JobId + ":output:00");
        Assert.All(outputs, lot =>
        {
            Assert.Equal(kind, lot.ItemKind);
            Assert.Equal(InventoryContainerRules.IsContainer(kind) ? 1 : amount, lot.Quantity);
            Assert.Equal(kind == InventoryContainerRules.Handcart ? worker : shop.HouseholdId, lot.OwnerId);
            Assert.Null(lot.ContainerLotId);
            if (kind == InventoryContainerRules.Handcart)
            {
                Assert.Null(lot.StorageBuildingId);
                Assert.Equal(new InventoryGroundPosition(shop.Position.X, shop.Position.Y), lot.GroundPosition);
            }
            else Assert.Equal(shop.InstanceId, lot.StorageBuildingId);
        });
        Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.Id == "smith-input");
        Assert.All(running.InputReservationIds, id => Assert.Equal(InventoryReservationState.Completed,
            world.Society.Inventory.GetReservation(id).State));
        state = PrivateWorldRuntimeCodec.Decode(host.Saved());
        using var reload = PrivateWorldRuntime.Restore(state, _ => new RequestChoices(DecisionProviderKind.Deterministic));
        using var reloadHost = new VesselBatchCheckpointHost(reload);
        world.Pause();
        reload.Pause();
        world.Resume();
        reload.Resume();
        await host.Advance();
        await reloadHost.Advance();
        Assert.Equal(host.Saved(), reloadHost.Saved());
        Assert.Equal(amount, world.Society.Inventory.Lots.Where(lot => lot.Id.StartsWith(start.JobId + ":output:", StringComparison.Ordinal))
            .Sum(lot => lot.Quantity));
        _ = Roundtrip(world);
    }

    private static ContentPackageManifest VesselBatchRecipe(string kind, int amount, string workstation)
    {
        var packageId = "test-vessel-batch-" + kind + "-" + amount;
        var version = ContentVersion.Parse("1.0.0");
        var digest = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(packageId)));
        var recipe = new RecipeDefinition(digest, "make-batch", version, "Make a batch",
            [new("wood", 3)], [new(kind, amount)], 2, workstation, ["crafting"]);
        var definition = new ContentDefinition(RecipeDefinition.SchemaKind, recipe.LocalId, version, recipe.DisplayName,
            recipe.PayloadDigest, JsonSerializer.Serialize(new
            {
                schema = "recipe/v1",
                recipe.Inputs,
                recipe.Outputs,
                recipe.DurationTicks,
                recipe.WorkstationBuildingId,
                recipe.Tags,
            }, PayloadOptions));
        return new ContentPackageManifest(packageId, version, digest,
            [new ContentDependency(BlacksmithContent.PackageId, new ContentVersionRange(
                ContentVersion.Parse("1.0.0"), ContentVersion.Parse("2.0.0")))], [definition], []);
    }

    private sealed class VesselBatchCheckpointHost : IDisposable
    {
        private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("clanker-vessel-batch-");
        private readonly PrivateWorldRuntime world;
        private readonly PrivateWorldStateFile file;
        private readonly PrivateWorldRuntimeService service;
        private readonly RecordingLogger<PrivateWorldRuntimeService> logger = new();

        public VesselBatchCheckpointHost(PrivateWorldRuntime world)
        {
            this.world = world;
            file = new PrivateWorldStateFile(Path.Combine(directory.FullName, "world.json"));
            file.Save(world);
            var presence = new OwnerClientPresenceLease(TimeSpan.FromMinutes(5));
            presence.RecordAuthenticatedReconnect("test-owner");
            service = new PrivateWorldRuntimeService(world, file, presence, logger);
        }

        public byte[] Saved() => File.ReadAllBytes(file.Path);

        public async Task Advance()
        {
            Assert.True(await service.TryAdvanceOnceAsync(), string.Join("\n", logger.Messages));
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), Saved());
        }

        public void Dispose()
        {
            service.Dispose();
            directory.Delete(recursive: true);
        }
    }
}

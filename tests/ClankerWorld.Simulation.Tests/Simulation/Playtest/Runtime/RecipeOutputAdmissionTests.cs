using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class RecipeOutputAdmissionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WaterOutputIsRefusedWithoutReservingInputsAndHostKeepsSaving(bool mixedOutput)
    {
        var choices = new IdleChoices();
        var (world, recipe, shop, worker) = await Activated(mixedOutput ? [new("wood", 1), new("fresh_water", 1)] : [new("fresh_water", 1)], choices);
        using (world)
        {
            var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
            var result = world.StartProduction(recipe.CanonicalId, shop.InstanceId, worker);
            Assert.False(result.Applied);
            Assert.Contains("water jug", result.Failure);
            Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
            Assert.Empty(world.WorldSimulation.ProductionJobs);
            Assert.DoesNotContain(choices.Offered, candidate => candidate.Id == "build:recipe:" + recipe.CanonicalId);
            await CheckHostContinuation(world);
            Assert.Equal(3, world.Society.Inventory.GetLot("smith-input").Quantity);
            Assert.DoesNotContain(world.Society.Inventory.Reservations, reservation => reservation.LotId == "smith-input");
        }
    }

    [Fact]
    public async Task LooseWoodControlCompletesAndItsOutputSurvivesHostReload()
    {
        var choices = new IdleChoices();
        var (world, recipe, shop, worker) = await Activated([new("wood", 1)], choices);
        using (world)
        {
            Assert.Contains(choices.Offered, candidate => candidate.Id == "build:recipe:" + recipe.CanonicalId);
            Assert.True(world.StartProduction(recipe.CanonicalId, shop.InstanceId, worker).Applied);
            var running = PrivateWorldRuntimeCodec.Encode(world.ExportState());
            using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(running), _ => new IdleChoices());
            for (var tick = 0; tick < 4 && world.WorldSimulation.ProductionJobs.Single().State == WorldProductionJobState.Running; tick++)
            {
                Assert.True((await world.AdvanceOneTickAsync()).Advanced);
                Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
                Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
            }
            var job = Assert.Single(world.WorldSimulation.ProductionJobs);
            Assert.Equal(WorldProductionJobState.Completed, job.State);
            var output = world.Society.Inventory.GetLot(job.JobId + ":output:00");
            Assert.Equal(("wood", 1, shop.HouseholdId, shop.InstanceId), (output.ItemKind, output.Quantity, output.OwnerId, output.StorageBuildingId));
            Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.Id == "smith-input");
            await CheckHostContinuation(world);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreviouslyAdmittedWaterJobCancelsWithoutConsumingInputsAndReplays(bool mixedOutput)
    {
        var (setup, recipe, shop, worker) = await Activated(mixedOutput ? [new("wood", 1), new("fresh_water", 1)] : [new("fresh_water", 1)], new IdleChoices());
        using (setup)
        {
            var control = setup.ExportState().WorldContent!.Recipes.Single(item => item.LocalId == "control-output");
            Assert.True(setup.StartProduction(control.CanonicalId, shop.InstanceId, worker).Applied);
            var state = setup.ExportState();
            // Represent a valid checkpoint admitted by the older build, using a real job's
            // worker, ownership, deadline and reservations with the equally shaped water recipe.
            state = state with
            {
                WorldSimulation = state.WorldSimulation! with
                {
                    ProductionJobs = state.WorldSimulation.ProductionJobs.Select(job => job with { RecipeId = recipe.CanonicalId }).ToArray(),
                }
            };
            var bytes = PrivateWorldRuntimeCodec.Encode(state);
            using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), _ => new IdleChoices());
            using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), _ => new IdleChoices());
            var input = world.Society.Inventory.GetLot("smith-input");
            Assert.False((await world.AdvanceOneTickAsync(() => false)).Advanced);
            Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
            for (var tick = 0; tick < 4 && world.WorldSimulation.ProductionJobs.Single().State == WorldProductionJobState.Running; tick++)
            {
                Assert.True((await world.AdvanceOneTickAsync()).Advanced);
                Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
                Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
            }
            var cancelled = Assert.Single(world.WorldSimulation.ProductionJobs);
            Assert.Equal(WorldProductionJobState.Cancelled, cancelled.State);
            Assert.Equal(input, world.Society.Inventory.GetLot(input.Id) with { LastProcessedTick = input.LastProcessedTick });
            foreach (var reservationId in cancelled.InputReservationIds)
                Assert.Equal(InventoryReservationState.Released, world.Society.Inventory.GetReservation(reservationId).State);
            Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.Id.StartsWith(cancelled.JobId + ":output:", StringComparison.Ordinal));
            Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "recipe_completed");
            Assert.Contains(world.ExportState().Events, item => item.Kind == "production_output_unsupported");
            await CheckHostContinuation(world);
        }
    }

    private static async Task<(PrivateWorldRuntime World, RecipeDefinition Recipe, PlacedBuilding Shop, string Worker)> Activated(
        IReadOnlyList<ContentQuantity> outputs, IdleChoices choices)
    {
        var (state, _, worker, shop) = ToolMakingRequestTests.Prepared();
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == worker
            ? person with { Position = shop.Position } : person).ToArray()
        };
        var version = ContentVersion.Parse("1.0.0");
        var digest = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("physical-output")));
        var recipe = new RecipeDefinition(digest, "make-output", version, "Make output", [new("wood", 3)], outputs, 2, shop.DefinitionId, []);
        var definition = new ContentDefinition(RecipeDefinition.SchemaKind, recipe.LocalId, version, recipe.DisplayName,
            recipe.PayloadDigest, JsonSerializer.Serialize(new
            {
                schema = "recipe/v1",
                recipe.Inputs,
                recipe.Outputs,
                recipe.DurationTicks,
                recipe.WorkstationBuildingId,
                recipe.Tags
            }, JsonSerializerOptions.Web));
        var control = new RecipeDefinition(digest, "control-output", version, "Control output", [new("wood", 3)], [new("wood", 1)], 2, shop.DefinitionId, []);
        var controlDefinition = new ContentDefinition(RecipeDefinition.SchemaKind, control.LocalId, version, control.DisplayName,
            control.PayloadDigest, JsonSerializer.Serialize(new
            {
                schema = "recipe/v1",
                control.Inputs,
                control.Outputs,
                control.DurationTicks,
                control.WorkstationBuildingId,
                control.Tags
            }, JsonSerializerOptions.Web));
        var package = new ContentPackageManifest("physical-output", version, digest,
            [new(BusinessContent.PackageId, new(version, ContentVersion.Parse("2.0.0")))], [definition, controlDefinition], []);
        var world = PrivateWorldRuntime.Restore(state, _ => choices);
        world.ProposeContent(package);
        var resolution = world.ResolveContent(package.PackageId);
        Assert.True(resolution.IsSuccess, resolution.Diagnostic);
        world.ValidateContent(package.PackageId, resolution);
        world.ApproveContent(package.PackageId);
        world.StageContent(package.PackageId);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), _ => new IdleChoices());
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        return (world, recipe, shop, worker);
    }

    private static async Task CheckHostContinuation(PrivateWorldRuntime world)
    {
        var directory = Directory.CreateTempSubdirectory("recipe-output-");
        try
        {
            var file = new PrivateWorldStateFile(Path.Combine(directory.FullName, "world.json"));
            file.Save(world);
            var presence = new OwnerClientPresenceLease(TimeSpan.FromMinutes(5));
            presence.RecordAuthenticatedReconnect("test-owner");
            using var host = new PrivateWorldRuntimeService(world, file, presence);
            Assert.True(await host.TryAdvanceOnceAsync());
            Assert.True(await host.TryAdvanceOnceAsync());
            var bytes = File.ReadAllBytes(file.Path);
            using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), _ => new IdleChoices());
            Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
            Assert.Equal(world.WorldTick, restored.WorldTick);
            Assert.False(world.Society.IsPaused);
        }
        finally { directory.Delete(recursive: true); }
    }

    private sealed class IdleChoices : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public List<CognitionCandidate> Offered { get; } = [];
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            Offered.AddRange(request.Observation.Candidates);
            var idle = request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle");
            return new DeterministicDecisionProvider().DecideAsync(request with { Observation = request.Observation with { Candidates = [idle] } }, cancellationToken);
        }
    }
}

using System.Globalization;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;
using static ClankerWorld.Simulation.Tests.ShelterOrderTestFixture;

namespace ClankerWorld.Simulation.Tests;

public sealed class ShelterOrderRollbackSafetyTests
{
    private const string RefugeId = "rollback-refuge";
    private static readonly (ContentPackageManifest Manifest, BuildingDefinition Definition) CustomContent = CreatePackage();
    private static readonly Lazy<Task<byte[]>> Baseline = new(CreateBaselineAsync);

    [Theory]
    [InlineData("seek shelter", false)]
    [InlineData("seek shelter", true)]
    [InlineData("light a fire", false)]
    [InlineData("light a fire", true)]
    public async Task RemovedBuildingOrdersKeepTheirDefinitionThroughCompletionOrCancellation(string command, bool cancelled)
    {
        var state = PrivateWorldRuntimeCodec.Decode(await Baseline.Value);
        var actor = Actor(state);
        var building = state.WorldSimulation!.Buildings.Single(item => item.InstanceId == RefugeId);
        if (command == "light a fire")
            state = WithInventory(state, InventoryFixture.AddLot(state.Society.Society.Inventory,
                "rollback-firewood", "wood", actor, 1));
        using var world = Restore(state);
        var receipt = world.SubmitInstruction(new("rollback-order", "owner:test", actor, OwnerInstructionKind.MustDo,
            string.Create(CultureInfo.InvariantCulture, $"{command} at ({building.Position.X}, {building.Position.Y})")));
        await Tick(world);
        var bound = Order(world, receipt);
        var binding = Assert.IsType<OwnerShelterBinding>(bound.ShelterBinding);
        Assert.Equal(CustomContent.Definition.CanonicalId, binding.DefinitionId);
        Assert.Equal(RefugeId, binding.BuildingInstanceId);
        Assert.Equal(0, bound.CompletedUnits);
        if (cancelled)
            Assert.True(world.CancelOrder(new("cancel-rollback-order", "owner:test", world.Society.WorldId,
                actor, receipt.InstructionId)).Changed);
        else
        {
            // Native routing and movement cooldowns can require more than one further tick.
            for (var tick = 0; tick < 16 && Order(world, receipt).CompletedUnits == 0; tick++)
                await Tick(world);
            var finished = Order(world, receipt);
            Assert.Equal(1, finished.CompletedUnits);
            Assert.Equal(binding, finished.ShelterBinding);
            var completion = Assert.IsType<OwnerShelterCompletion>(finished.ShelterCompletion);
            Assert.Equal(world.WorldTick, completion.WorldTick);
            Assert.Equal(binding.Position, completion.Position);
            Assert.Equal(binding.Position, world.Inhabitants.Single(person => person.InhabitantId == actor).Position);
            Assert.NotNull(finished.LastEffectId);
            if (command == "light a fire")
            {
                Assert.NotNull(completion.FuelReservationId);
                var fuel = world.Society.Inventory.GetReservation(completion.FuelReservationId);
                Assert.Equal((actor, "rollback-firewood", 1, "heating_fuel", InventoryReservationState.Completed, completion.WorldTick),
                    (fuel.OwnerId, fuel.LotId, fuel.Quantity, fuel.Purpose, fuel.State, fuel.ExpiryTick));
            }
        }
        Assert.Equal(cancelled ? "cancelled" : "finished", Order(world, receipt).Status);
        RemoveRefuge(world);
        AssertNoOtherPackageReferences(world);
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var beforeReload = Restore(PrivateWorldRuntimeCodec.Decode(before));
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(beforeReload.ExportState()));

        Assert.Throws<InvalidOperationException>(() => world.RollbackContent(CustomContent.Manifest.PackageId, "withdraw removed refuge"));

        var after = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.Equal(before, after);
        using var afterReload = Restore(PrivateWorldRuntimeCodec.Decode(after));
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(afterReload.ExportState()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TheSameRemovedPackageCanBeWithdrawnWithOnlyUnboundOrNaturalShelterOrders(bool natural)
    {
        using var removed = Restore(PrivateWorldRuntimeCodec.Decode(await Baseline.Value));
        RemoveRefuge(removed);
        var state = removed.ExportState();
        var actor = Actor(state);
        GridPoint? cover = null;
        if (natural)
        {
            cover = state.Map.Tiles.Where(tile => state.Map.IsPassable(tile.Position) &&
                    state.Map.VegetationAt(tile.Position) == VegetationCover.Forest &&
                    state.WorldSimulation!.Buildings.All(building => state.Map.FootDistance(building.Position, tile.Position) > 14) &&
                    state.Inhabitants.All(person => person.Position != tile.Position))
                .OrderBy(tile => tile.Position.Y).ThenBy(tile => tile.Position.X).First().Position;
            state = At(WithStorm(state), actor, cover.Value);
        }
        using var world = Restore(state);
        var text = cover is { } point
            ? string.Create(CultureInfo.InvariantCulture, $"take cover at ({point.X}, {point.Y})") : "light a fire";
        var receipt = world.SubmitInstruction(new("unrelated-order", "owner:test", actor, OwnerInstructionKind.MustDo, text));
        if (natural)
        {
            await Tick(world);
            Assert.Equal(("finished", 1, "natural"),
                (Order(world, receipt).Status, Order(world, receipt).CompletedUnits, Order(world, receipt).ShelterBinding!.Kind));
        }
        else
        {
            Assert.Equal(("waiting", 0), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
            Assert.Null(Order(world, receipt).ShelterBinding);
        }
        var order = Order(world, receipt);
        AssertNoOtherPackageReferences(world);

        var rollback = world.RollbackContent(CustomContent.Manifest.PackageId, "withdraw unrelated refuge");

        Assert.Equal(ContentPackageLifecycle.Quarantined, rollback.Lifecycle);
        Assert.DoesNotContain(world.WorldContent.Buildings, definition => definition.PackageDigest == CustomContent.Manifest.PackageDigest);
        Assert.Equal(order, Order(world, receipt));
        var after = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var reloaded = Restore(PrivateWorldRuntimeCodec.Decode(after));
        Assert.Equal(after, PrivateWorldRuntimeCodec.Encode(reloaded.ExportState()));
    }

    private static async Task<byte[]> CreateBaselineAsync()
    {
        using var initial = Restore(Prepared());
        initial.ProposeContent(CustomContent.Manifest);
        initial.ValidateContent(CustomContent.Manifest.PackageId,
            PrivateWorldRuntime.PreviewContent([CustomContent.Manifest], [CustomContent.Manifest.PackageId]));
        initial.ApproveContent(CustomContent.Manifest.PackageId);
        initial.StageContent(CustomContent.Manifest.PackageId);
        await Tick(initial);
        Assert.Contains(initial.WorldContent.Buildings, definition => definition.CanonicalId == CustomContent.Definition.CanonicalId);
        var state = initial.ExportState();
        var actor = Actor(state);
        var origin = state.Inhabitants.Single(person => person.InhabitantId == actor).Position;
        var occupied = state.Map.CampObjects.Select(item => item.Position)
            .Concat(state.Map.Resources.Select(item => item.Position))
            .Concat(state.RoadTiles ?? [])
            .Concat(state.Inhabitants.Select(person => person.Position)).ToHashSet();
        var site = state.Map.Tiles.Where(tile => state.Map.IsBuildable(tile.Position) && !occupied.Contains(tile.Position) &&
                state.Map.FootDistance(origin, tile.Position) < int.MaxValue &&
                state.WorldSimulation!.Buildings.All(building => state.Map.FootDistance(building.Position, tile.Position) > 2) &&
                state.Map.FootNeighbors(tile.Position).Any(point => !occupied.Contains(point)))
            .OrderBy(tile => state.Map.FootDistance(origin, tile.Position)).ThenBy(tile => tile.Position.Y).ThenBy(tile => tile.Position.X)
            .First().Position;
        var approach = state.Map.FootNeighbors(site).Where(point => !occupied.Contains(point))
            .OrderBy(point => point.Y).ThenBy(point => point.X).First();
        state = At(state, actor, approach);
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { TravelCooldownTicks = 1 } : person).ToArray(),
        };
        using var placed = Restore(state);
        var placement = placed.PlaceBuilding(RefugeId, CustomContent.Definition.CanonicalId, site);
        Assert.True(placement.Applied, placement.Failure);
        Assert.Null(placed.WorldSimulation.Buildings.Single(item => item.InstanceId == RefugeId).HouseholdId);
        return PrivateWorldRuntimeCodec.Encode(placed.ExportState());
    }

    private static void RemoveRefuge(PrivateWorldRuntime world)
    {
        var building = world.WorldSimulation.Buildings.Single(item => item.InstanceId == RefugeId);
        var result = world.RemoveBuilding(RefugeId, building.TownId, building.HouseholdId);
        Assert.True(result.Applied, result.Failure);
        Assert.DoesNotContain(world.WorldSimulation.Buildings, item => item.InstanceId == RefugeId);
    }

    private static void AssertNoOtherPackageReferences(PrivateWorldRuntime world)
    {
        Assert.DoesNotContain(world.WorldSimulation.Buildings, building => building.DefinitionId == CustomContent.Definition.CanonicalId);
        Assert.Empty(world.WorldSimulation.ProductionJobs);
        Assert.Empty(world.WorldSimulation.CropBuilds ?? []);
        Assert.Empty(world.WorldSimulation.BuildingExpansions ?? []);
        Assert.Empty(world.WorldSimulation.ConstructionReceipts ?? []);
        Assert.All(world.Inhabitants, person => Assert.Null(person.Project));
    }

    private static (ContentPackageManifest, BuildingDefinition) CreatePackage()
    {
        var version = ContentVersion.Parse("1.0.0");
        var digest = "sha256:" + new string('b', 64);
        var definition = new BuildingDefinition(digest, "refuge", version, "Test refuge", 1, 1, 1, [], ["shelter", "warmth"]);
        var manifest = new ContentPackageManifest("shelter-rollback-test", version, digest, [],
        [
            new ContentDefinition(BuildingDefinition.SchemaKind, definition.LocalId, version, definition.DisplayName,
                definition.PayloadDigest,
                """{"schema":"building/v1","width":1,"height":1,"capacity":1,"buildCosts":[],"tags":["shelter","warmth"]}"""),
        ], []);
        return (manifest, definition);
    }

    private static PrivateWorldRuntime Restore(PrivateWorldRuntimeState state) => PrivateWorldRuntime.Restore(state, _ => new OrderChoices());
    private static OwnerInstructionOrder Order(PrivateWorldRuntime world, OwnerInstructionReceipt receipt) =>
        world.ExportState().Instructions!.Single(item => item.InstructionId == receipt.InstructionId).Order!;
    private static async Task Tick(PrivateWorldRuntime world) => Assert.True((await world.AdvanceOneTickAsync()).Advanced);

    private sealed class OrderChoices : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var observation = request.Observation;
            var selected = observation.OperativeOrderInstructionId is null ? "safe_idle" :
                observation.Candidates.FirstOrDefault(candidate => candidate.Id is "seek_shelter" or "tend_fire")?.Id ?? "safe_idle";
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, observation.InhabitantId, Kind,
                request.ProviderEpoch, observation.RunEpoch, observation.DecisionGeneration, observation.ObservationDigest,
                selected, 1, new Dictionary<string, double> { [selected] = 1 }));
        }
    }
}

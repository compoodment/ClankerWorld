using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class HouseToolsBehaviorTests
{
    [Theory]
    [InlineData("owner")]
    [InlineData("autonomous")]
    [InlineData("typed")]
    [InlineData("owner_stored")]
    [InlineData("autonomous_stored")]
    public async Task NativeFelledTreeSeedCanBePlantedThroughAnOwnerTask(string mode)
    {
        using var setup = NormalPathWorld.CreateGenerated("smith-whole-load", _ => new IdleProvider());
        Assert.True((await setup.AdvanceOneTickAsync()).Advanced);
        var state = setup.ExportState();
        var actor = state.Inhabitants[0].InhabitantId;
        var source = state.Map.Resources.Where(resource => TreeGrowthRules.IsWoodTree(resource.TreeKind) &&
                state.Resources.Single(item => item.ResourceId == resource.Id).State == ResourceState.Available &&
                state.WorldSystems!.Ecology.GetResource(resource.Id).Quantity == 1 &&
                FreeNeighbors(state, actor, resource).Any())
            .OrderBy(resource => resource.Id, StringComparer.Ordinal).First();
        using var felling = Restore(BesideSource(state, actor, source, HouseToolsContent.CrudeWoodenAxe));
        var harvested = Gather(felling, actor, source, "wood", "native-planting-seed");
        Assert.True((await felling.AdvanceOneTickAsync()).Advanced);
        Assert.Equal("finished", Order(felling, harvested).Status);
        Assert.Equal(1, PersonalQuantity(felling, actor, TreeGrowthRules.TreeSeedItem));
        Assert.Contains(felling.ExportState().Events, item => item.Kind == "tree_seed_collected" &&
            item.Detail == actor + ":" + source.Id + ":1");
        state = felling.ExportState();
        if (mode.EndsWith("_stored", StringComparison.Ordinal))
        {
            var household = state.Society.Society.GetInhabitant(actor).HouseholdId!;
            var house = Assert.Single(state.WorldSimulation!.Buildings, building => building.HouseholdId == household &&
                state.WorldContent!.Buildings.Single(item => item.CanonicalId == building.DefinitionId).Tags.Contains("house"));
            state = state with
            {
                Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                    ? person with { Position = house.Position, TravelCooldownTicks = 0, LastDecisionContext = null }
                    : person).ToArray(),
            };
            using var storing = Restore(state);
            var stored = storing.SubmitInstruction(new OwnerInstructionRequest("store-real-tree-seed", "owner:test", actor,
                OwnerInstructionKind.MustDo, "store tree seed"));
            for (var tick = 0; tick < 12 && Order(storing, stored).Status != "finished"; tick++)
                Assert.True((await storing.AdvanceOneTickAsync()).Advanced);
            Assert.Equal("finished", Order(storing, stored).Status);
            state = storing.ExportState();
            var storedSeed = Assert.Single(state.Society.Society.Inventory.Lots,
                lot => lot.ItemKind == TreeGrowthRules.TreeSeedItem && lot.OwnerId == actor);
            Assert.Equal(house.InstanceId, storedSeed.StorageBuildingId);
            Assert.Equal(1, storedSeed.Quantity);
            if (mode.StartsWith("autonomous", StringComparison.Ordinal))
            {
                var collected = storing.SubmitInstruction(new OwnerInstructionRequest("collect-real-tree-seed", "owner:test", actor,
                    OwnerInstructionKind.MustDo, "collect tree seed"));
                for (var tick = 0; tick < 12 && Order(storing, collected).Status != "finished"; tick++)
                    Assert.True((await storing.AdvanceOneTickAsync()).Advanced);
                Assert.Equal("finished", Order(storing, collected).Status);
                state = storing.ExportState();
                Assert.True(PersonalEquipmentRules.IsCarried(state.Society.Society.Inventory.GetLot(storedSeed.Id), actor));
            }
        }
        var seed = Assert.Single(state.Society.Society.Inventory.Lots,
            lot => lot.ItemKind == TreeGrowthRules.TreeSeedItem);
        var blocked = state.WorldSimulation!.Buildings.SelectMany(building => WorldContentSimulationRules.Footprint(
                state.WorldContent!.Buildings.Single(item => item.CanonicalId == building.DefinitionId), building))
            .Concat(felling.RoadTiles).Concat(state.Map.Resources.Select(resource => resource.Position))
            .Concat(state.Map.CampObjects.Select(item => item.Position))
            .Concat(state.Inhabitants.Where(person => person.InhabitantId != actor).Select(person => person.Position))
            .Concat(state.Towns!.SelectMany(town => town.BorderTiles)).ToHashSet();
        bool Legal(GridPoint point) => !blocked.Contains(point) && TreeGrowthRules.GroundRefusal(state.Map, point) is null &&
            state.WorldSystems!.Chunks.Any(chunk => chunk.Coordinate == ChunkRules.ToChunkCoordinate(point, chunk.ChunkSize) &&
                chunk.Resources.Count < state.WorldSystems.Config.MaxResourcesPerChunk);
        var target = state.Map.Tiles.Select(tile => tile.Position).Where(Legal)
            .Where(point => state.Map.IsReachableOnFoot(state.Inhabitants[0].Position, point) &&
                state.Map.FootNeighbors(point).Any(Legal))
            .OrderBy(point => state.Map.FootDistance(state.Inhabitants[0].Position, point)).First();
        var stand = state.Map.FootNeighbors(target).First(Legal);
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor ? person with
            {
                Position = stand, HungerBasisPoints = 9_000, TravelCooldownTicks = 0,
                Project = null, LastDecisionContext = null, Exploration = null,
            } : person).ToArray(),
        };
        IDecisionProvider Provider(string id) => mode.StartsWith("autonomous", StringComparison.Ordinal) && id == actor
            ? new NativePlantingProvider() : new IdleProvider();
        using var world = PrivateWorldRuntime.Restore(state, Provider);
        using var replay = PrivateWorldRuntime.Restore(state, Provider);
        OwnerInstructionReceipt? receipt = null;
        if (mode.StartsWith("owner", StringComparison.Ordinal))
        {
            var request = new OwnerInstructionRequest("native-tree-task", "owner:test", actor,
                OwnerInstructionKind.MustDo, $"plant a conifer tree at {target.X},{target.Y}");
            receipt = world.SubmitInstruction(request);
            Assert.Equal(receipt.InstructionId, replay.SubmitInstruction(request).InstructionId);
        }
        else if (mode == "typed")
        {
            Assert.True(world.PlantTree(actor, TreeGrowthRules.Conifer, seed.Id, target).Planted);
            Assert.True(replay.PlantTree(actor, TreeGrowthRules.Conifer, seed.Id, target).Planted);
        }
        world.Validate();
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False((await world.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        for (var tick = 0; tick < 12 && !world.ExportState().Map.Resources.Any(resource =>
            resource.Id.StartsWith(TreeGrowthRules.PlantedTreeIdPrefix, StringComparison.Ordinal)); tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        }
        var tree = Assert.Single(world.ExportState().Map.Resources,
            resource => resource.Id.StartsWith(TreeGrowthRules.PlantedTreeIdPrefix, StringComparison.Ordinal));
        Assert.Equal(0, PersonalQuantity(world, actor, TreeGrowthRules.TreeSeedItem));
        Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.ItemKind == TreeGrowthRules.TreeSeedItem);
        Assert.DoesNotContain(state.Towns!.SelectMany(town => town.BorderTiles), point => point == tree.Position);
        if (!mode.StartsWith("autonomous", StringComparison.Ordinal)) Assert.Equal((target, TreeGrowthRules.Conifer), (tree.Position, tree.TreeKind));
        if (receipt is not null) Assert.Equal(("finished", 1), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
        world.Validate();
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = Restore(PrivateWorldRuntimeCodec.Decode(bytes));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        restored.Validate();
    }

    private sealed class NativePlantingProvider : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            var choice = request.Observation.Candidates.FirstOrDefault(candidate => candidate.Id == "plant_tree") ??
                request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle");
            return new DeterministicDecisionProvider().DecideAsync(request with
            {
                Observation = request.Observation with { Candidates = [choice] },
            }, cancellationToken);
        }
    }
}

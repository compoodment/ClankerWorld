using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class HouseToolsBehaviorTests
{
    private static readonly Lazy<byte[]> NativeTreeOrderSeeds = new(() =>
        PrepareNativeTreeOrderSeeds().GetAwaiter().GetResult());

    private static async Task<byte[]> PrepareNativeTreeOrderSeeds()
    {
        using var generated = NormalPathWorld.CreateGenerated("smith-whole-load", _ => new IdleProvider());
        Assert.True((await generated.AdvanceOneTickAsync()).Advanced);
        var state = generated.ExportState();
        var actor = state.Inhabitants[0].InhabitantId;
        for (var count = 0; count < 2; count++)
        {
            var source = state.Map.Resources.Where(resource => TreeGrowthRules.IsWoodTree(resource.TreeKind) &&
                    state.Resources.Single(item => item.ResourceId == resource.Id).State == ResourceState.Available &&
                    state.WorldSystems!.Ecology.GetResource(resource.Id).Quantity == 1 &&
                    FreeNeighbors(state, actor, resource).Any())
                .OrderBy(resource => resource.Id, StringComparer.Ordinal).First();
            state = count == 0 ? BesideSource(state, actor, source, HouseToolsContent.CrudeWoodenAxe) : state with
            {
                Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor ? person with
                {
                    Position = FreeNeighbors(state, actor, source).First(), TravelCooldownTicks = 0,
                    LastDecisionContext = null, Project = null, Exploration = null,
                } : person).ToArray(),
            };
            using var felling = Restore(state);
            var receipt = Gather(felling, actor, source, "wood", "native-two-seeds-" + count);
            Assert.True((await felling.AdvanceOneTickAsync()).Advanced);
            Assert.Equal("finished", Order(felling, receipt).Status);
            Assert.Equal(count + 1, PersonalQuantity(felling, actor, TreeGrowthRules.TreeSeedItem));
            state = felling.ExportState();
        }
        return PrivateWorldRuntimeCodec.Encode(state);
    }

    private static (PrivateWorldRuntimeState State, string Actor, GridPoint Target) PreparedTreeOrder()
    {
        var state = PrivateWorldRuntimeCodec.Decode(NativeTreeOrderSeeds.Value);
        var actor = state.Inhabitants[0].InhabitantId;
        var blocked = state.WorldSimulation!.Buildings.SelectMany(building => WorldContentSimulationRules.Footprint(
                state.WorldContent!.Buildings.Single(item => item.CanonicalId == building.DefinitionId), building))
            .Concat(state.Map.Resources.Select(resource => resource.Position))
            .Concat(state.Map.CampObjects.Select(item => item.Position))
            .Concat(state.Inhabitants.Where(person => person.InhabitantId != actor).Select(person => person.Position))
            .Concat(state.Towns!.SelectMany(town => town.BorderTiles)).ToHashSet();
        using var roads = Restore(state);
        blocked.UnionWith(roads.RoadTiles);
        bool Legal(GridPoint point) => !blocked.Contains(point) && TreeGrowthRules.GroundRefusal(state.Map, point) is null &&
            state.WorldSystems!.Chunks.Any(chunk => chunk.Coordinate == ChunkRules.ToChunkCoordinate(point, chunk.ChunkSize) &&
                chunk.Resources.Count < state.WorldSystems.Config.MaxResourcesPerChunk);
        var target = state.Map.Tiles.Select(tile => tile.Position).Where(Legal)
            .Where(point => state.Map.IsReachableOnFoot(state.Inhabitants[0].Position, point) &&
                state.Map.FootNeighbors(point).Any(Legal))
            .OrderBy(point => state.Map.FootDistance(state.Inhabitants[0].Position, point)).First();
        var stand = state.Map.FootNeighbors(target).First(Legal);
        return (state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor ? person with
            {
                Position = stand, HungerBasisPoints = 9_000, TravelCooldownTicks = 0,
                Project = null, LastDecisionContext = null, Exploration = null,
            } : person).ToArray(),
        }, actor, target);
    }

    [Theory]
    [InlineData("plant two trees", 2, false)]
    [InlineData("keep planting broadleaf trees", 2, true)]
    [InlineData("plant a conifer tree", 1, false)]
    public async Task TreeOrderCountsOnlySaplingsAndRetainsProgressAcrossReload(string text, int count, bool repeat)
    {
        var (state, actor, _) = PreparedTreeOrder();
        using var world = Restore(state);
        var receipt = world.SubmitInstruction(new("tree-count", "owner:test", actor, OwnerInstructionKind.MustDo, text));
        for (var tick = 0; tick < 20 && Order(world, receipt).CompletedUnits < count; tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(count, Order(world, receipt).CompletedUnits);
        Assert.Equal(2 - count, PersonalQuantity(world, actor, TreeGrowthRules.TreeSeedItem));
        Assert.Equal(count, world.ExportState().Events.Count(item => item.Kind == "tree_planted"));
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var loaded = Restore(PrivateWorldRuntimeCodec.Decode(bytes));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(loaded.ExportState()));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.True((await loaded.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(loaded.ExportState()));
        Assert.Equal(repeat ? "blocked" : "finished", Order(world, receipt).Status);
        Assert.Equal(count, Order(world, receipt).CompletedUnits);
        if (repeat)
        {
            Assert.Contains("tree seed", Order(world, receipt).BlockedReason, StringComparison.Ordinal);
            Assert.True(world.CancelOrder(new("stop-trees", "owner:test", world.Society.WorldId, actor, receipt.InstructionId)).Changed);
            Assert.Equal("cancelled", Order(world, receipt).Status);
        }
        world.Validate();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExactTreeOrderNeverFallsBackAfterItsTileIsOccupied(bool repeat)
    {
        var (state, actor, target) = PreparedTreeOrder();
        using var world = Restore(state);
        var text = (repeat ? "keep planting conifer trees" : "plant two conifer trees") + $" at {target.X},{target.Y}";
        var receipt = world.SubmitInstruction(new("exact-trees", "owner:test", actor, OwnerInstructionKind.MustDo, text));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(1, Order(world, receipt).CompletedUnits);
        using var loaded = Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        for (var tick = 0; tick < 3; tick++) Assert.True((await loaded.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(("blocked", 1), (Order(loaded, receipt).Status, Order(loaded, receipt).CompletedUnits));
        Assert.Equal(1, PersonalQuantity(loaded, actor, TreeGrowthRules.TreeSeedItem));
        var tree = Assert.Single(loaded.ExportState().Map.Resources,
            resource => resource.Id.StartsWith(TreeGrowthRules.PlantedTreeIdPrefix, StringComparison.Ordinal));
        Assert.Equal((target, TreeGrowthRules.Conifer), (tree.Position, tree.TreeKind));
        loaded.Validate();
    }

    [Theory]
    [InlineData("finish")]
    [InlineData("cancel")]
    [InlineData("replace")]
    public async Task TreeOrderTravelCanResumeOrStopWithoutSpendingASeed(string mode)
    {
        var (state, actor, target) = PreparedTreeOrder();
        var start = state.Map.Tiles.Select(tile => tile.Position)
            .Where(point => state.Map.IsPassable(point) && state.Map.FootDistance(point, target) is >= 4 and <= 6 &&
                state.Map.IsReachableOnFoot(point, target) && !state.Inhabitants.Any(person => person.Position == point))
            .OrderBy(point => state.Map.FootDistance(point, target)).First();
        state = state with { Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
            ? person with { Position = start } : person).ToArray() };
        using var world = Restore(state);
        var receipt = world.SubmitInstruction(new("walk-tree", "owner:test", actor, OwnerInstructionKind.MustDo,
            $"plant a broadleaf tree at {target.X},{target.Y}"));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.NotEqual(start, world.Inhabitants.Single(person => person.InhabitantId == actor).Position);
        Assert.Equal(0, Order(world, receipt).CompletedUnits);
        Assert.Equal(2, PersonalQuantity(world, actor, TreeGrowthRules.TreeSeedItem));
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var loaded = Restore(PrivateWorldRuntimeCodec.Decode(bytes));
        if (mode == "cancel")
            Assert.True(loaded.CancelOrder(new("cancel-walk", "owner:test", loaded.Society.WorldId, actor, receipt.InstructionId)).Changed);
        else if (mode == "replace")
            _ = loaded.SubmitInstruction(new("replace-walk", "owner:test", actor, OwnerInstructionKind.MustDo, "eat berries"));
        for (var tick = 0; tick < (mode == "finish" ? 20 : 3) && (mode != "finish" || Order(loaded, receipt).Status != "finished"); tick++)
            Assert.True((await loaded.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(mode == "finish" ? ("finished", 1) : ("cancelled", 0),
            (Order(loaded, receipt).Status, Order(loaded, receipt).CompletedUnits));
        Assert.Equal(mode == "finish" ? 1 : 2, PersonalQuantity(loaded, actor, TreeGrowthRules.TreeSeedItem));
        loaded.Validate();
    }

    [Theory]
    [InlineData("town")]
    [InlineData("ground")]
    [InlineData("occupied")]
    [InlineData("outside")]
    [InlineData("foreign")]
    [InlineData("missing")]
    public async Task RefusedTreeOrdersKeepSeedsAndExplainTheActualBlocker(string cause)
    {
        var (state, actor, target) = PreparedTreeOrder();
        if (cause == "town") target = state.Towns!.SelectMany(town => town.BorderTiles).First();
        else if (cause == "ground") target = state.Map.Tiles.First(tile =>
            TreeGrowthRules.GroundRefusal(state.Map, tile.Position) is not null &&
            !state.Towns!.Any(town => town.BorderTiles.Contains(tile.Position))).Position;
        else if (cause == "occupied") target = state.Map.Resources[0].Position;
        else if (cause == "outside") target = new(state.Map.Width, state.Map.Height);
        if (cause is "foreign" or "missing")
        {
            var inventory = state.Society.Society.Inventory;
            var other = state.Inhabitants.First(person => person.InhabitantId != actor).InhabitantId;
            var seeds = inventory.Lots.Where(lot => lot.ItemKind == TreeGrowthRules.TreeSeedItem).ToArray();
            foreach (var seed in seeds)
                inventory = cause == "foreign" ? InventoryFixture.Transfer(inventory, "foreign-" + seed.Id,
                    actor, other, seed.Id, seed.Quantity, "test_seed_owner") : InventoryFixture.ConsumeReservation(
                    InventoryFixture.Reserve(inventory, "missing-" + seed.Id, actor, seed.Id, seed.Quantity,
                        "test_seed_absent", state.WorldTick), "missing-" + seed.Id);
            state = WithInventory(state, inventory);
        }
        using var world = Restore(state);
        var seedCount = world.Society.Inventory.Lots.Where(lot => lot.ItemKind == TreeGrowthRules.TreeSeedItem).Sum(lot => lot.Quantity);
        var receipt = world.SubmitInstruction(new("refused-tree", "owner:test", actor, OwnerInstructionKind.MustDo,
            $"plant a conifer tree at {target.X},{target.Y}"));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(("blocked", 0), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
        Assert.False(string.IsNullOrWhiteSpace(Order(world, receipt).BlockedReason));
        Assert.Equal(seedCount, world.Society.Inventory.Lots.Where(lot => lot.ItemKind == TreeGrowthRules.TreeSeedItem).Sum(lot => lot.Quantity));
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "tree_planted");
        using var loaded = Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        Assert.Equal(Order(world, receipt), Order(loaded, receipt));
        loaded.Validate();
    }

    [Fact]
    public async Task QueuedTreeOrdersPlantTheirOwnSpeciesAndRejectCorruptProgress()
    {
        var (state, actor, target) = PreparedTreeOrder();
        using var world = Restore(state);
        var first = world.SubmitInstruction(new("queue-first-tree", "owner:test", actor, OwnerInstructionKind.MustDo,
            $"plant a conifer tree at {target.X},{target.Y}"));
        var second = world.SubmitInstruction(new("queue-second-tree", "owner:test", actor, OwnerInstructionKind.MustDo,
            "plant a broadleaf tree", Queue: true));
        Assert.Equal("queued", Order(world, second).Status);
        for (var tick = 0; tick < 15 && Order(world, second).Status != "finished"; tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(("finished", 1), (Order(world, first).Status, Order(world, first).CompletedUnits));
        Assert.Equal(("finished", 1), (Order(world, second).Status, Order(world, second).CompletedUnits));
        var trees = world.ExportState().Map.Resources.Where(resource =>
            resource.Id.StartsWith(TreeGrowthRules.PlantedTreeIdPrefix, StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, trees.Length);
        Assert.Contains(trees, tree => tree.Position == target && tree.TreeKind == TreeGrowthRules.Conifer);
        Assert.Contains(trees, tree => tree.Position != target && tree.TreeKind == TreeGrowthRules.Broadleaf);
        state = world.ExportState();
        foreach (var corrupt in new[]
        {
            Order(world, second) with { ProgressUnit = "fields" },
            Order(world, second) with { CompletedUnits = 2 },
            Order(world, second) with { LastEffectId = null },
            Order(world, second) with { LastEffectId = "tree:plant:planted-tree-made-up" },
            Order(world, second) with { TargetCropKind = "grain" },
            Order(world, second) with { TargetAgentId = actor },
        })
            Assert.Throws<InvalidDataException>(() => Restore(state with
            {
                Instructions = state.Instructions!.Select(instruction => instruction.InstructionId == second.InstructionId
                    ? instruction with { Order = corrupt } : instruction).ToArray(),
            }));
        world.Validate();
    }
}

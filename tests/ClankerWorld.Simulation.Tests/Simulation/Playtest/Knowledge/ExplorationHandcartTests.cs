using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed class ExplorationHandcartTests
{
    private static readonly GridPoint Start = new(234, 55);
    private static readonly GridPoint Mountain = new(233, 54);
    private static readonly Lazy<Task<PrivateWorldRuntimeState>> Prepared = new(PrepareCart);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NativeScoutUsesLegalCartExitsAndRecordsOnlyActualMovementAcrossReload(bool attached)
    {
        var state = await Prepared.Value;
        var cart = Assert.Single(state.Society.Society.Inventory.Lots, lot => lot.ItemKind == "handcart");
        var actor = cart.OwnerId;
        var inventory = InventoryFixture.Relocate(state.Society.Society.Inventory, "scout-position", cart.Id, actor, 1,
            groundPosition: new(Start.X, Start.Y));
        state = state with
        {
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
            Inhabitants = state.Inhabitants.Select(person => person with
            {
                Position = person.InhabitantId == actor ? Start : person.Position,
                HungerBasisPoints = 9_500,
                Survival = new(),
                LastDecisionContext = null,
                Project = null,
                TravelCooldownTicks = 0,
                MoveWaitTicks = 0,
                Exploration = null,
            }).ToArray(),
        };
        Assert.Equal(TerrainKind.Mountain, state.Map.Tiles.Single(tile => tile.Position == Mountain).Terrain);
        Assert.True(state.Map.CanFootStep(Start, new(234, 54)));
        Assert.DoesNotContain(state.RoadTiles!, point => point == Mountain);
        Assert.DoesNotContain(state.Inhabitants.Where(person => person.InhabitantId != actor),
            person => state.Map.FootDistance(Start, person.Position) <= 1);
        var chooser = new ScoutChooser(attached ? "attach_handcart:" + cart.Id : "safe_idle");
        using var initial = PrivateWorldRuntime.Restore(state, id => id == actor ? chooser : new ScoutChooser("safe_idle"));
        if (attached)
            await Until(initial, () => initial.ExportState().HandcartHitches!.Count == 1, 20);
        chooser.Preferred = "explore";
        var bytes = PrivateWorldRuntimeCodec.Encode(initial.ExportState());
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes),
            id => new ScoutChooser(id == actor ? "explore" : "safe_idle"));
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes),
            id => new ScoutChooser(id == actor ? "explore" : "safe_idle"));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        Assert.False((await world.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        var moves = 0;
        for (var tick = 0; tick < 40; tick++)
        {
            var before = world.Inhabitants.Single(person => person.InhabitantId == actor);
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
            var after = world.Inhabitants.Single(person => person.InhabitantId == actor);
            var previousVisited = before.Exploration?.VisitedTiles ?? [];
            Assert.All(after.Exploration!.VisitedTiles, point => Assert.True(previousVisited.Contains(point) ||
                point == before.Position || point == after.Position));
            if (after.Position != before.Position)
            {
                moves++;
                Assert.True(state.Map.CanFootStep(before.Position, after.Position));
                Assert.Contains(after.Position, after.Exploration.VisitedTiles);
                if (attached)
                {
                    Assert.False(state.Map.IsDiagonalFootStep(before.Position, after.Position));
                    Assert.NotEqual(TerrainKind.Mountain, state.Map.Tiles.Single(tile => tile.Position == after.Position).Terrain);
                    Assert.True(state.Map.ElevationAt(after.Position) < SeededMap.MountainElevationThreshold);
                }
            }
            if (attached)
                Assert.Equal(new InventoryGroundPosition(after.Position.X, after.Position.Y), world.Society.Inventory.GetLot(cart.Id).GroundPosition);
            else
                Assert.Equal(new InventoryGroundPosition(Start.X, Start.Y), world.Society.Inventory.GetLot(cart.Id).GroundPosition);
            world.Validate();
        }
        var result = world.Inhabitants.Single(person => person.InhabitantId == actor);
        Assert.True(moves > 0 || result.Exploration!.OutingPath.Count == 0,
            "A scout must take the available legal cart exit or end the outing within 40 native ticks.");
        Assert.Contains(world.ExportState().Events, item => item.Kind == "exploration_completed" &&
            item.Detail.StartsWith(actor + ":", StringComparison.Ordinal));
        Assert.Equal(Start, result.Position);
        Assert.Equal(actor, world.Society.Inventory.GetLot(cart.Id).OwnerId);
        Assert.Equal(1, world.Society.Inventory.GetLot(cart.Id).Quantity);
        var final = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var loaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(final), _ => new ScoutChooser("safe_idle"));
        Assert.Equal(final, PrivateWorldRuntimeCodec.Encode(loaded.ExportState()));
        loaded.Validate();
    }

    private static async Task<PrivateWorldRuntimeState> PrepareCart()
    {
        using var setup = NormalPathWorld.CreateGenerated("normal-visible-handcart", _ => new ScoutChooser("safe_idle"));
        Assert.True((await setup.AdvanceOneTickAsync()).Advanced);
        var state = setup.ExportState();
        var smith = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == "first-town-blacksmith");
        var actor = state.Society.Society.Inhabitants.First(person => person.HouseholdId == smith.HouseholdId).Id;
        // Crafting/payment is covered by NormalCraftLoadPullSaveParkAndUnloadConservePhysicalMaterials.
        // This fixture starts at the independent attachment/scouting boundary on unchanged generated terrain.
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "scout-cart", "handcart", actor, 1,
            groundPosition: new(Start.X, Start.Y));
        return state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } };
    }

    private static async Task Until(PrivateWorldRuntime world, Func<bool> done, int limit)
    {
        for (var tick = 0; tick < limit && !done(); tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.True(done());
    }

    private sealed class ScoutChooser(string preferred) : IDecisionProvider
    {
        public string Preferred { get; set; } = preferred;
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            var choice = request.Observation.Candidates.FirstOrDefault(item => item.Id == Preferred) ??
                request.Observation.Candidates.FirstOrDefault(item => item.Id == "continue_project") ??
                request.Observation.Candidates.Single(item => item.Id == "safe_idle");
            return new DeterministicDecisionProvider().DecideAsync(request with
            { Observation = request.Observation with { Candidates = [choice] } }, cancellationToken);
        }
    }
}

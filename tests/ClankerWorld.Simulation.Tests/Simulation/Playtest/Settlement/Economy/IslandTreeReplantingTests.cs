using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed class IslandTreeReplantingTests
{
    [Fact]
    public async Task OccupiedNearestStumpDoesNotHideAnotherReachableStump()
    {
        using var setup = IslandWorld();
        var actor = setup.Inhabitants[0].InhabitantId;
        var map = setup.ExportState().Map;
        var blocked = map.GetResource("tree-136-16");
        var reachable = map.GetResource("tree-138-17");
        var origin = setup.Inhabitants[0].Position;
        Assert.True(map.FootDistance(origin, blocked.Position) < map.FootDistance(origin, reachable.Position));
        Assert.True(map.IsReachableOnFoot(origin, blocked.Position));
        Assert.True(map.IsReachableOnFoot(origin, reachable.Position));
        var state = StorageRoutingTestFixture.BlockAccess(setup, blocked.Position, 1);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory,
            "replanting-seed", TreeGrowthRules.TreeSeedItem, actor, 1);
        state = state with
        {
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
            Survival = new SettlementSurvivalState(0, []),
            Inhabitants = state.Inhabitants.Select(person => person with
            {
                HungerBasisPoints = 9_000,
                Survival = new SurvivalCondition(WarmthBasisPoints: 9_000),
            }).ToArray(),
            WorldSystems = state.WorldSystems! with
            {
                Ecology = state.WorldSystems.Ecology with
                {
                    Resources = state.WorldSystems.Ecology.Resources.Select(tree => tree.Id == blocked.Id || tree.Id == reachable.Id
                        ? EcologyRules.Harvest(tree, 1).Resource! with { NextRegenerationDay = TreeGrowthRules.StumpRegrowthDays }
                        : tree).ToArray(),
                },
            },
        };
        var provider = new ActionProvider("replant_tree");
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)),
            id => id == actor ? provider : new ActionProvider("safe_idle"));
        for (var tick = 0; tick < 24 && !world.WorldSystems.Ecology.GetResource(reachable.Id).IsPlanted; tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);

        Assert.Contains(provider.Seen, request => request.Observation.Candidates.Any(candidate =>
            candidate.Id == "replant_tree" && candidate.DestinationId == reachable.Id));
        Assert.True(world.WorldSystems.Ecology.GetResource(reachable.Id).IsPlanted);
        Assert.False(world.WorldSystems.Ecology.GetResource(blocked.Id).IsPlanted);
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "movement_blocked" &&
            item.Detail.StartsWith(actor + ":no_route", StringComparison.Ordinal));
        world.Validate();
    }

    private static PrivateWorldRuntime IslandWorld()
    {
        var options = new GeographyOptions("island-fuel-review-0", WorldSizePreset.Small);
        var world = new PrivateWorldRuntime(options.Seed, _ => new ActionProvider("safe_idle"),
            startPace: WorldStartPace.FounderSetup, geographyOptions: options);
        world.InitializeFirstTownContent();
        world.AcceptFirstTownLayout(new GridPoint(136, 14));
        var positions = new[] { new GridPoint(135, 14), new GridPoint(131, 9), new GridPoint(132, 9), new GridPoint(133, 9) };
        for (var index = 0; index < positions.Length; index++) world.PlaceFounder($"founder:{index + 1:D32}", positions[index]);
        world.StartWorld();
        return world;
    }

    private sealed class ActionProvider(string action, string? fallback = null) : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public List<CognitionDecisionRequest> Seen { get; } = [];

        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            Seen.Add(request);
            var selected = request.Observation.Candidates.FirstOrDefault(candidate => candidate.Id == action) ??
                (fallback is null ? null : request.Observation.Candidates.FirstOrDefault(candidate => candidate.Id == fallback)) ??
                request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle");
            return new DeterministicDecisionProvider().DecideAsync(request with
            {
                Observation = request.Observation with { Candidates = [selected] },
            }, cancellationToken);
        }
    }
}

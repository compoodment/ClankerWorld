using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed class StoredFuelRoutingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HouseholdUsesLocalFuelWhenStoredWoodCannotBeReached(bool blockStorage)
    {
        var options = new GeographyOptions("island-fuel-review-0", WorldSizePreset.Small);
        using var initial = new PrivateWorldRuntime(options.Seed, startPace: WorldStartPace.FounderSetup, geographyOptions: options);
        initial.InitializeFirstTownContent();
        initial.AcceptFirstTownLayout(new GridPoint(136, 14));
        var setupMap = initial.ExportState().Map;
        var storagePosition = initial.WorldSimulation.Buildings.Single(building => building.InstanceId == "first-town-warehouse").Position;
        var localTree = setupMap.Resources.Single(resource => resource.Id == "wild-128-16").Position;
        var actorPosition = setupMap.Tiles.First(tile => setupMap.IsBuildable(tile.Position) &&
            setupMap.FootDistance(tile.Position, storagePosition) > 1 && setupMap.FootDistance(tile.Position, localTree) <= 1 &&
            !setupMap.Resources.Any(resource => resource.Position == tile.Position)).Position;
        var positions = new[] { actorPosition, new GridPoint(131, 9), new GridPoint(132, 9), new GridPoint(133, 9) };
        var ids = Enumerable.Range(1, 4).Select(index => $"founder:{index:D32}").ToArray();
        for (var index = 0; index < 4; index++) initial.PlaceFounder(ids[index], positions[index]);
        initial.StartWorld();
        if (blockStorage)
        {
            var map = initial.ExportState().Map;
            var storage = initial.WorldSimulation.Buildings.Single(building => building.InstanceId == "first-town-warehouse").Position;
            var blockers = map.Tiles.Where(tile => map.FootDistance(tile.Position, storage) <= 1 &&
                map.IsBuildable(tile.Position) && !map.Resources.Any(resource => resource.Position == tile.Position) &&
                !initial.Inhabitants.Any(person => person.Position == tile.Position)).ToArray();
            Assert.NotEmpty(blockers);
            for (var index = 0; index < blockers.Length; index++)
                initial.AddAgent($"agent:{index + 100:D32}", blockers[index].Position);
            Assert.All(map.Tiles.Where(tile => map.FootDistance(tile.Position, storage) <= 1 && map.IsPassable(tile.Position)),
                tile => Assert.Contains(initial.Inhabitants, person => person.Position == tile.Position));
        }
        var state = initial.ExportState();
        var household = state.Society.Society.GetInhabitant(ids[0]).HouseholdId!;
        var society = state.Society.Society;
        var storedWood = society.Inventory.Lots.Where(lot => lot.OwnerId == household && lot.ItemKind == "wood")
            .Sum(lot => lot.Quantity);
        Assert.True(storedWood > 0);
        var systems = state.WorldSystems!;
        state = state with
        {
            Society = state.Society with { Society = society },
            Survival = new SettlementSurvivalState(0, []),
            Inhabitants = state.Inhabitants.Select(person => person with
            {
                HungerBasisPoints = 9_000,
                Survival = new SurvivalCondition(WarmthBasisPoints: 4_000),
            }).ToArray(),
            WorldSystems = systems with
            {
                RegionalWeather = null,
                Config = systems.Config with { WeatherProfiles = Enum.GetValues<SeasonKind>().Select(season => new WeatherProfile(season, 0, 0, 0, 0, 1)).ToArray() },
                Climate = systems.Climate with { Weather = WeatherKind.Snow },
            },
        };
        var local = state.Map.Resources.Single(resource => resource.Id == "wild-128-16");
        Assert.True(state.Map.IsReachableOnFoot(positions[0], local.Position));
        Assert.False(state.Map.IsReachableFromCampOnFoot(local.Position));
        var provider = new HeatProvider(ids[0]);
        var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)), _ => provider);
        try
        {
            for (var tick = 0; tick < 24; tick++)
            {
                Assert.True((await world.AdvanceOneTickAsync()).Advanced);
                if (tick == 5)
                {
                    var saved = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState()));
                    world.Dispose();
                    world = PrivateWorldRuntime.Restore(saved, _ => provider);
                }
            }
            var result = world.ExportState();
            Assert.Equal(!blockStorage, provider.WasToolOffered);
            if (blockStorage)
            {
                Assert.True(result.Events.Any(item => item.Kind == "material_gathered" && item.Detail.StartsWith(ids[0] + ":", StringComparison.Ordinal)),
                    $"tick={world.WorldTick}; fires={result.Survival!.Fires.Count}; no_route={result.Events.Count(item => item.Kind == "movement_blocked" && item.Detail.StartsWith(ids[0] + ":no_route", StringComparison.Ordinal))}; warmth={result.Inhabitants.Single(person => person.InhabitantId == ids[0]).Survival!.WarmthBasisPoints}; stored wood={storedWood}");
                Assert.Equal(storedWood, result.Society.Society.Inventory.Lots.Where(lot => lot.OwnerId == household && lot.ItemKind == "wood").Sum(lot => lot.Quantity));
            }
            Assert.Contains(result.Survival!.Fires, fire => fire.BuildingId == "first-town-house-a");
            Assert.DoesNotContain(result.Events, item => item.Kind == "movement_blocked" && item.Detail.StartsWith(ids[0] + ":no_route", StringComparison.Ordinal));
            using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(result)));
            Assert.NotEmpty(restored.ExportState().Survival!.Fires);
        }
        finally { world.Dispose(); }
    }

    private sealed class HeatProvider(string actor) : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public bool WasToolOffered { get; private set; }
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            if (request.Observation.InhabitantId == actor &&
                request.Observation.Candidates.Any(candidate => candidate.Id == "collect_wooden_axe"))
                WasToolOffered = true;
            var selected = request.Observation.InhabitantId == actor
                ? request.Observation.Candidates.FirstOrDefault(candidate => candidate.Id == "tend_fire") : null;
            selected ??= request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle");
            return new DeterministicDecisionProvider().DecideAsync(request with
            {
                Observation = request.Observation with { Candidates = [selected] },
            }, cancellationToken);
        }
    }
}

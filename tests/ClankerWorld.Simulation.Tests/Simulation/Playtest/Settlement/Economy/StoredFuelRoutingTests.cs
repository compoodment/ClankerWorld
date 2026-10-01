using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
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
        var hearth = initial.WorldSimulation.Buildings.Single(building => building.InstanceId == "first-town-house-a").Position;
        // Use a real nearby tree on the Town's disconnected component. Legacy
        // resource IDs can move farther from this hearth when generation changes.
        var localTree = setupMap.Resources.Where(resource => resource.Kind == "construction" &&
            resource.TreeKind is "broadleaf" or "conifer" && !setupMap.IsReachableFromCampOnFoot(resource.Position) &&
            setupMap.IsReachableOnFoot(resource.Position, hearth) && setupMap.FootDistance(resource.Position, hearth) <= 8)
            .OrderBy(resource => setupMap.FootDistance(resource.Position, hearth))
            .ThenBy(resource => resource.Id, StringComparer.Ordinal).First();
        var actorPosition = setupMap.Tiles.First(tile => setupMap.IsBuildable(tile.Position) &&
            setupMap.FootDistance(tile.Position, storagePosition) > 1 && setupMap.FootDistance(tile.Position, localTree.Position) <= 1 &&
            !setupMap.Resources.Any(resource => resource.Position == tile.Position)).Position;
        var positions = new[] { actorPosition, new GridPoint(131, 9), new GridPoint(132, 9), new GridPoint(133, 9) };
        var ids = Enumerable.Range(1, 4).Select(index => $"founder:{index:D32}").ToArray();
        for (var index = 0; index < 4; index++) initial.PlaceFounder(ids[index], positions[index]);
        initial.StartWorld();
        var householdId = initial.Society.GetInhabitant(ids[0]).HouseholdId!;
        var workStock = initial.WorldSimulation.Buildings.First(building => building.HouseholdId == householdId &&
            !initial.WorldContent.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId).Tags.Contains("house"));
        var state = blockStorage
            ? BlockFuelStorageAccess(initial, storagePosition, workStock.Position)
            : initial.ExportState();
        var household = state.Society.Society.GetInhabitant(ids[0]).HouseholdId!;
        var society = state.Society.Society;
        // The private fuel stock is at a workplace, separate from the House
        // whose hearth must remain reachable even while stock pickup is blocked.
        society = society with
        {
            Inventory = society.Inventory with
            {
                Lots = society.Inventory.Lots.Select(lot =>
            lot.OwnerId == household && lot.ItemKind == "wood"
                ? lot with { StorageBuildingId = workStock.InstanceId } : lot).ToArray()
            }
        };
        if (blockStorage)
        {
            // This check blocks stock pickup, not the new requirement to use
            // a real axe. Take the Town's actual starter tool before the blockage.
            var axe = society.Inventory.Lots.Single(lot => lot.Id == "first-town-wooden-axe");
            society = society with
            {
                Inventory = InventoryFixture.Transfer(society.Inventory, "fuel-fixture-axe",
                axe.OwnerId, ids[0], axe.Id, 1, "equipment_collected")
            };
        }
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
        state = SettlementWeatherTestFixture.WithWeather(state, WeatherKind.Snow);
        var local = state.Map.Resources.Single(resource => resource.Id == localTree.Id);
        Assert.True(state.Map.FootDistance(local.Position, hearth) <= 8);
        Assert.True(state.Map.FootDistance(positions[0], local.Position) <= 1);
        Assert.True(state.Map.IsReachableOnFoot(local.Position, hearth));
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
                    $"tick={world.WorldTick}; fires={result.Survival!.Fires.Count}; no_route={result.Events.Count(item => item.Kind == "movement_blocked" && item.Detail.StartsWith(ids[0] + ":no_route", StringComparison.Ordinal))}; warmth={result.Inhabitants.Single(person => person.InhabitantId == ids[0]).Survival!.WarmthBasisPoints}; stored wood={storedWood}; actor={System.Text.Json.JsonSerializer.Serialize(result.Inhabitants.Single(person => person.InhabitantId == ids[0]))}; inventory={System.Text.Json.JsonSerializer.Serialize(result.Society.Society.Inventory.Lots.Where(lot => lot.OwnerId == ids[0]))}; events={string.Join(';', result.Events.Where(item => item.Detail.StartsWith(ids[0] + ":", StringComparison.Ordinal)).Select(item => item.Kind + ":" + item.Detail))}");
                Assert.Equal(storedWood, result.Society.Society.Inventory.Lots.Where(lot => lot.OwnerId == household && lot.ItemKind == "wood").Sum(lot => lot.Quantity));
            }
            Assert.Contains(result.Survival!.Fires, fire => fire.BuildingId == "first-town-house-a");
            Assert.DoesNotContain(result.Events, item => item.Kind == "movement_blocked" && item.Detail.StartsWith(ids[0] + ":no_route", StringComparison.Ordinal));
            using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(result)));
            Assert.NotEmpty(restored.ExportState().Survival!.Fires);
        }
        finally { world.Dispose(); }
    }

    private static PrivateWorldRuntimeState BlockFuelStorageAccess(PrivateWorldRuntime initial,
        GridPoint warehouse, GridPoint workplace)
    {
        var map = initial.ExportState().Map;
        var positions = map.Tiles.Where(tile => map.IsPassable(tile.Position) &&
            (map.FootDistance(tile.Position, warehouse) <= 1 || map.FootDistance(tile.Position, workplace) <= 1))
            .Select(tile => tile.Position).ToArray();
        Assert.NotEmpty(positions);
        var placements = new Dictionary<string, GridPoint>(StringComparer.Ordinal);
        foreach (var position in positions.Where(position => !initial.Inhabitants.Any(person => person.Position == position)))
        {
            var id = $"agent:{placements.Count + 100:D32}";
            // Occupy all legal approaches, including gathering sites, while
            // preserving the map and the real fuel at both storage locations.
            var spare = map.Tiles.First(tile => map.IsBuildable(tile.Position) &&
                map.FootDistance(tile.Position, warehouse) > 1 && map.FootDistance(tile.Position, workplace) > 1 &&
                !map.Resources.Any(resource => resource.Position == tile.Position) &&
                !map.CampObjects.Any(item => item.Position == tile.Position) &&
                !initial.Inhabitants.Any(person => person.Position == tile.Position) &&
                initial.Towns.Any(town => town.BorderTiles.Contains(tile.Position))).Position;
            initial.AddAgent(id, spare);
            placements.Add(id, position);
        }
        var state = initial.ExportState() with
        {
            Inhabitants = initial.ExportState().Inhabitants.Select(person => placements.TryGetValue(person.InhabitantId, out var position)
                ? person with { Position = position } : person).ToArray(),
        };
        Assert.All(positions, position => Assert.Contains(state.Inhabitants, person => person.Position == position));
        return state;
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

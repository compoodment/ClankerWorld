using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private List<BridgeState> bridges = [];
    private BridgeTrafficState bridgeTraffic = BridgeTrafficState.Empty;
    private HashSet<GridPoint> roadBridgeDecks = [];

    public IReadOnlyList<BridgeState> Bridges => bridges.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();

    public BridgeTrafficState BridgeTraffic => bridgeTraffic;

    /// <summary>Rebuilds the passable decks that movement reads from the saved bridges.</summary>
    private void ApplyBridgeDecks()
    {
        var decks = RiverBridgeRules.Decks(bridges);
        if (!RiverBridgeRules.SameDecks(map.BridgeDecks, decks)) map = map with { BridgeDecks = decks };
        roadBridgeDecks = bridges.Where(item => item.Trigger == BridgeTriggers.Road)
            .SelectMany(item => item.Span).ToHashSet();
    }

    /// <summary>Occupied or held ground that new Road tiles and bridge banks must leave clear.</summary>
    private HashSet<GridPoint> RoadBlockedTiles()
    {
        var definitions = worldContent.Buildings.ToDictionary(item => item.CanonicalId, StringComparer.Ordinal);
        return map.Resources.Select(item => item.Position)
            .Concat(fields.Select(field => field.Position))
            .Concat(HouseholdLandHeldByOthers(null))
            .Concat(map.CampObjects.Select(item => item.Position))
            .Concat(worldSimulation.Buildings.SelectMany(building =>
                WorldContentSimulationRules.Footprint(definitions[building.DefinitionId], building)))
            .Concat((worldSimulation.BuildingExpansions ?? []).Where(job => job.State is WorldProductionJobState.Running or WorldProductionJobState.Paused).SelectMany(ExpansionTiles))
            .Concat(TownProjectFootprintTiles())
            .ToHashSet();
    }

    /// <summary>Road tiles and bridge entrances, which buildings and new resources must leave clear.</summary>
    private IEnumerable<GridPoint> RoadAndBridgeTiles() =>
        roadTiles.Concat(bridges.SelectMany(item => item.Entrances));

    /// <summary>
    /// Saves already-validated Road bridges, together with the Road tiles the
    /// caller has just committed, and makes their decks walkable.
    /// </summary>
    private void CommitRoadBridges(IReadOnlyList<RiverCrossing> crossings, string routeId)
    {
        if (crossings.Count == 0) return;
        var built = crossings
            .Select(crossing => RiverBridgeRules.ToBridge(crossing, BridgeTriggers.Road, WorldTick, routeId))
            .ToArray();
        foreach (var bridge in built)
        {
            bridges.Add(bridge);
            bridgeTraffic = BridgeTrafficRules.Forget(bridgeTraffic, bridge.Id);
        }
        ApplyBridgeDecks();
        checkpointSchemaVersion = StateSchemaVersion;
        foreach (var bridge in built)
            AppendEvent("bridge_built", $"{BridgeTriggers.Road}:{bridge.Id}:{routeId}");
    }

    /// <summary>Counts one committed foot step toward the separate traffic bridge trigger.</summary>
    private void RecordBridgeTraffic(string agentId, GridPoint from, GridPoint to)
    {
        var next = BridgeTrafficRules.RecordStep(bridgeTraffic, map, agentId, from, to, WorldTick);
        if (ReferenceEquals(next, bridgeTraffic) ||
            next.InProgress.SequenceEqual(bridgeTraffic.InProgress) && next.Completed.SequenceEqual(bridgeTraffic.Completed))
            return;
        bridgeTraffic = next;
        checkpointSchemaVersion = StateSchemaVersion;
    }

    /// <summary>
    /// End-of-tick traffic check: expire old evidence, then build a bridge at
    /// each crossing that reached the threshold, unless a bank is occupied or
    /// an existing bridge already joins the same banks. It never paints Roads.
    /// </summary>
    private void AdvanceBridgeTraffic()
    {
        if (bridgeTraffic.IsEmpty) return;
        var positions = inhabitants.ToDictionary(item => item.Key, item => item.Value.Position, StringComparer.Ordinal);
        var pruned = BridgeTrafficRules.Prune(bridgeTraffic, map, WorldTick,
            society.Checkpoint.Config.TicksPerWorldDay, positions);
        if (!ReferenceEquals(pruned, bridgeTraffic))
        {
            bridgeTraffic = pruned;
            checkpointSchemaVersion = StateSchemaVersion;
        }
        var ready = BridgeTrafficRules.ReadyCrossings(bridgeTraffic);
        if (ready.Count == 0) return;
        var blocked = RoadBlockedTiles();
        foreach (var id in ready)
        {
            bridgeTraffic = BridgeTrafficRules.Forget(bridgeTraffic, id);
            checkpointSchemaVersion = StateSchemaVersion;
            if (!RiverBridgeRules.TryResolve(map, id, out var crossing)) continue;
            if (crossing!.Entrances.Any(blocked.Contains))
            {
                AppendEvent("traffic_bridge_not_built", $"{id}:occupied_bank");
                continue;
            }
            if (RiverBridgeRules.IsRedundant(map, crossing, bridges.Select(RiverBridgeRules.ToCrossing)))
            {
                AppendEvent("traffic_bridge_not_built", $"{id}:same_banks_bridged");
                continue;
            }
            bridges.Add(RiverBridgeRules.ToBridge(crossing, BridgeTriggers.Traffic, WorldTick, routeId: null));
            ApplyBridgeDecks();
            AppendEvent("bridge_built", $"{BridgeTriggers.Traffic}:{id}");
        }
    }

    private static void ValidateBridges(
        IReadOnlyList<BridgeState> savedBridges,
        BridgeTrafficState traffic,
        SeededMap map,
        IReadOnlyList<GridPoint> roads,
        WorldContentSimulationState simulation,
        DeclarativeWorldContentState content,
        SocietyCheckpoint society,
        IEnumerable<PlaytestInhabitantState> inhabitants)
    {
        RiverBridgeRules.ValidateSaved(savedBridges, map, roads, society.WorldTick);
        var definitions = content.Buildings.ToDictionary(item => item.CanonicalId, StringComparer.Ordinal);
        var blocked = map.Resources.Select(item => item.Position)
            .Concat(map.CampObjects.Select(item => item.Position))
            .Concat(simulation.Buildings.SelectMany(building =>
                definitions.TryGetValue(building.DefinitionId, out var definition)
                    ? WorldContentSimulationRules.Footprint(definition, building.Position)
                    : throw new InvalidDataException("A placed building has no active definition.")))
            .ToHashSet();
        if (savedBridges.Any(bridge => bridge.Entrances.Any(blocked.Contains)))
            throw new InvalidDataException("A saved bridge lands on a building, resource or camp object.");
        BridgeTrafficRules.Validate(traffic, map, society.WorldTick, society.Config.TicksPerWorldDay,
            society.Inhabitants.Select(person => person.Id).ToHashSet(StringComparer.Ordinal),
            inhabitants.ToDictionary(person => person.InhabitantId, person => person.Position, StringComparer.Ordinal),
            savedBridges);
    }
}

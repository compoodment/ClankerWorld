using System.Collections.Concurrent;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

/// <summary>
/// Builds a world the way the game does: generated map, first Town site,
/// four placed founders and Start World. No roles are assigned.
/// </summary>
internal static class NormalPathWorld
{
    public static PrivateWorldRuntime CreateGenerated(string seed, Func<string, IDecisionProvider> providerFactory)
    {
        var options = new GeographyOptions(seed, WorldSizePreset.Small);
        var world = new PrivateWorldRuntime(options.Seed, providerFactory,
            startPace: WorldStartPace.FounderSetup, geographyOptions: options);
        var map = world.ExportState().Map;
        var anchor = FindStartingTownSite(map);
        world.InitializeFirstTownContent();
        world.AcceptFirstTownLayout(anchor);
        var buildings = world.WorldSimulation.Buildings.SelectMany(building =>
            WorldContentSimulationRules.Footprint(world.WorldContent.Buildings.Single(item =>
                item.CanonicalId == building.DefinitionId), building.Position)).ToHashSet();
        var roads = world.RoadTiles.ToHashSet();
        var placements = map.Tiles.Where(tile => Math.Abs(tile.Position.X - anchor.X) <= 5 &&
                Math.Abs(tile.Position.Y - anchor.Y) <= 5 && map.IsBuildable(tile.Position) &&
                !buildings.Contains(tile.Position) && !roads.Contains(tile.Position) &&
                !map.CampObjects.Any(item => item.Position == tile.Position) &&
                !map.Resources.Any(item => item.Position == tile.Position))
            .Take(PrivateWorldRuntime.RequiredFounders).Select(tile => tile.Position).ToArray();
        for (var index = 0; index < placements.Length; index++)
            world.PlaceFounder($"founder:{index + 1:D32}", placements[index]);
        world.StartWorld();
        return world;
    }

    /// <summary>Choose a feasible site near the camp without changing generated terrain or resources.</summary>
    internal static GridPoint FindStartingTownSite(SeededMap map)
    {
        var camp = map.Resources.Single(item => item.Id == "berry-patch").Position;
        var occupied = map.Resources.Select(item => item.Position)
            .Concat(map.CampObjects.Select(item => item.Position)).ToHashSet();
        foreach (var point in map.Tiles.Select(tile => tile.Position)
            .Where(point => map.IsBuildable(point) && !occupied.Contains(point))
            .OrderBy(point => Math.Abs(point.X - camp.X) + Math.Abs(point.Y - camp.Y))
            .ThenBy(point => point.Y).ThenBy(point => point.X).Prepend(camp))
        {
            if (FirstTownLayoutPlanner.Plan(map, point) is not { } layout) continue;
            var taken = occupied.Concat(layout.RoadTiles).Concat(layout.Buildings.SelectMany(building =>
                Enumerable.Range(0, building.Height).SelectMany(y => Enumerable.Range(0, building.Width)
                    .Select(x => new GridPoint(building.Position.X + x, building.Position.Y + y)))))
                .ToHashSet();
            if (map.Tiles.Count(tile => Math.Abs(tile.Position.X - point.X) <= 5 &&
                    Math.Abs(tile.Position.Y - point.Y) <= 5 && map.IsBuildable(tile.Position) &&
                    !taken.Contains(tile.Position)) >= PrivateWorldRuntime.RequiredFounders)
                return point;
        }
        throw new InvalidOperationException("The generated map has no feasible first Town site with room for its founders.");
    }

    /// <summary>A stable action family for a candidate, e.g. <c>recipe:mill-grain</c>.</summary>
    public static string Family(string candidateId, DeclarativeWorldContentState content)
    {
        if (TownConstructionCandidateIds.TryParse(candidateId, out var selection))
        {
            var localId = selection.IsBuilding
                ? content.Buildings.FirstOrDefault(item => item.CanonicalId == selection.DefinitionId)?.LocalId
                : content.Recipes.FirstOrDefault(item => item.CanonicalId == selection.DefinitionId)?.LocalId;
            return (selection.IsBuilding ? "building:" : "recipe:") + (localId ?? "unknown");
        }
        var separator = candidateId.IndexOf(':', StringComparison.Ordinal);
        return separator < 0 ? candidateId : candidateId[..separator];
    }
}

/// <summary>
/// Records every candidate offered to each agent, then lets the built-in rules
/// choose, or always stays idle so the world changes as little as possible.
/// </summary>
internal sealed class ActionCoverageRecorder(bool chooseIdle = false) : IDecisionProvider
{
    private readonly DeterministicDecisionProvider chooser = new();

    public ConcurrentDictionary<string, ConcurrentDictionary<string, int>> OfferedByAgent { get; } = new(StringComparer.Ordinal);

    public ConcurrentDictionary<string, int> Chosen { get; } = new(StringComparer.Ordinal);

    public ConcurrentDictionary<(string AgentId, string DestinationId), byte> WorkstationSupplyOffers { get; } = new();

    /// <summary>The world tick at which each agent was first offered each candidate.</summary>
    public ConcurrentDictionary<(string AgentId, string CandidateId), long> FirstOfferedTick { get; } = new();

    public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;

    public long ProviderEpoch => 0;

    public async ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
        CancellationToken cancellationToken = default)
    {
        var offered = OfferedByAgent.GetOrAdd(request.Observation.InhabitantId,
            _ => new ConcurrentDictionary<string, int>(StringComparer.Ordinal));
        foreach (var candidate in request.Observation.Candidates)
        {
            offered.AddOrUpdate(candidate.Id, 1, (_, count) => count + 1);
            FirstOfferedTick.TryAdd((request.Observation.InhabitantId, candidate.Id), request.Observation.WorldTick);
            if (candidate.Id.StartsWith("supply_workstation:", StringComparison.Ordinal) &&
                candidate.DestinationId is { } destination)
                WorkstationSupplyOffers.TryAdd((request.Observation.InhabitantId, destination), 0);
        }
        var choice = chooseIdle
            ? request with
            {
                Observation = request.Observation with
                {
                    Candidates = [request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle")],
                },
            }
            : request;
        var response = await chooser.DecideAsync(choice, cancellationToken);
        Chosen.AddOrUpdate(response.SelectedCandidateId, 1, (_, count) => count + 1);
        return response;
    }

    public IReadOnlySet<string> FamiliesOfferedTo(string agentId, DeclarativeWorldContentState content) =>
        OfferedByAgent.TryGetValue(agentId, out var offered)
            ? offered.Keys.Select(id => NormalPathWorld.Family(id, content)).ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);

    public IReadOnlySet<string> FamiliesOffered(DeclarativeWorldContentState content) =>
        OfferedByAgent.Values.SelectMany(offered => offered.Keys)
            .Select(id => NormalPathWorld.Family(id, content)).ToHashSet(StringComparer.Ordinal);
}

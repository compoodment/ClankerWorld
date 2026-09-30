using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>
/// A small, per-person record of places actually visited. An outing only picks
/// its next visible neighbour; it does not give the person the player's map.
/// </summary>
public sealed record SettlementExploration(
    IReadOnlyList<GridPoint> VisitedTiles,
    IReadOnlyList<GridPoint> OutingPath,
    long LastOutingTick,
    bool Returning,
    IReadOnlyList<GridPoint>? OutingDiscoveries = null);

public sealed partial class PrivateWorldRuntime
{
    private const int ExplorationStepsPerOuting = 8;
    private const int ExplorationCooldownTicks = 180;
    private const int ExplorationMemoryLimit = 256;

    private void AddExplorationCandidate(List<CognitionCandidate> candidates, string actor, PlaytestInhabitantState person)
    {
        if (NeedsUrgentFood(person) || NeedsUrgentWarmth(person))
            return;

        var exploration = person.Exploration;
        if (exploration?.OutingPath.Count > 0)
        {
            candidates.Add(new("explore", "Continue a short local scouting trip, then return to its start.", 75));
            return;
        }

        if (!HasWarmthForOuting(person) || person.HungerBasisPoints < OutingFullnessReserve ||
            person.Project is { Stage: not ("completed" or "cancelled") } ||
            exploration is not null && WorldTick - exploration.LastOutingTick < ExplorationCooldownTicks ||
            !map.FootNeighbors(person.Position).Any(map.IsPassable))
            return;

        candidates.Add(new("explore", "Scout adjacent terrain and resource sites out of curiosity, then return.", 75));
    }

    private bool HasWarmthForOuting(PlaytestInhabitantState person)
    {
        if (person.Survival is not { } condition) return true;
        // Budget the short out-and-back trip using adjacent exposure and actual movement costs.
        // Shelter at the starting tile is not protection carried along on the outing.
        var clothing = HasCarriedItem(person.InhabitantId, "clothing") ? 35 : 0;
        return map.FootNeighbors(person.Position).Where(map.IsPassable).All(next =>
        {
            var loss = Math.Max(0, WeatherExposure(next) - clothing);
            var stepTicks = (RoadStepCost(person.Position, next) + 99) / 100 +
                SettlementIllnessRules.TravelDelayTicks(condition.IllnessBasisPoints);
            return loss == 0 || condition.WarmthBasisPoints - loss * stepTicks * ExplorationStepsPerOuting * 2 >= UrgentWarmth;
        });
    }

    private void Explore(string actor, PlaytestInhabitantState person)
    {
        var exploration = person.Exploration ?? new SettlementExploration([], [], WorldTick, false);
        // A return path holds remaining waypoints, not each intermediate detour.
        // Only the outward path must end at the actor's current position.
        if (!exploration.Returning && exploration.OutingPath.Count > 0 && exploration.OutingPath[^1] != person.Position)
        {
            // Another legal intention moved the actor. Never splice that move
            // into a stale scouting path or pretend its intermediate tiles were visited.
            exploration = exploration with { OutingPath = [], OutingDiscoveries = [], Returning = false };
            AppendEvent("exploration_aborted", $"{actor}:interrupted_movement");
        }
        if (exploration.OutingPath.Count == 0)
        {
            var learnedStartingTile = RecordKnowledgeFact(actor, person.Position);
            exploration = exploration with
            {
                VisitedTiles = exploration.VisitedTiles.Contains(person.Position)
                    ? exploration.VisitedTiles
                    : exploration.VisitedTiles.Append(person.Position).TakeLast(ExplorationMemoryLimit).ToArray(),
                OutingPath = [person.Position],
                LastOutingTick = WorldTick,
                Returning = false,
                OutingDiscoveries = learnedStartingTile ? [person.Position] : [],
            };
            AppendEvent("exploration_started", $"{actor}:{person.Position.X},{person.Position.Y}");
        }

        if (exploration.Returning || exploration.OutingPath.Count > ExplorationStepsPerOuting)
        {
            ReturnFromExploration(actor, person, exploration with { Returning = true });
            return;
        }

        var occupied = inhabitants.Values.Where(item => item.InhabitantId != actor)
            .Select(item => item.Position).ToHashSet();
        var next = map.FootNeighbors(person.Position)
            .Where(point => map.IsPassable(point) && !occupied.Contains(point) &&
                !exploration.OutingPath.Contains(point))
            .OrderBy(point => exploration.VisitedTiles.Contains(point) ? 1 : 0)
            .ThenByDescending(point => map.FootDistance(point, exploration.OutingPath[0]))
            .ThenBy(point => point.Y).ThenBy(point => point.X)
            .Select(point => (GridPoint?)point).FirstOrDefault();
        if (next is null)
        {
            ReturnFromExploration(actor, person, exploration with { Returning = true });
            return;
        }

        // Movement still obeys the ordinary travel delay, weather, occupancy,
        // narrow-river and mountain rules. Only a completed step joins memory.
        inhabitants[actor] = person with { Exploration = exploration };
        MoveToward(actor, inhabitants[actor], next.Value, "explore");
        var moved = inhabitants[actor];
        if (moved.Position == person.Position)
            return;
        var visited = exploration.VisitedTiles.Contains(moved.Position)
            ? exploration.VisitedTiles
            : exploration.VisitedTiles.Append(moved.Position).TakeLast(ExplorationMemoryLimit).ToArray();
        var learned = !exploration.VisitedTiles.Contains(moved.Position) && RecordKnowledgeFact(actor, moved.Position);
        inhabitants[actor] = moved with
        {
            Exploration = exploration with
            {
                VisitedTiles = visited,
                OutingPath = exploration.OutingPath.Append(moved.Position).ToArray(),
                OutingDiscoveries = learned
                    ? (exploration.OutingDiscoveries ?? []).Append(moved.Position).TakeLast(ExplorationStepsPerOuting + 1).ToArray()
                    : exploration.OutingDiscoveries ?? [],
            }
        };
        if (!exploration.VisitedTiles.Contains(moved.Position))
        {
            var terrain = map.Tiles.First(tile => tile.Position == moved.Position).Terrain;
            var resourcesHere = map.Resources.Where(site => site.Position == moved.Position)
                .Select(site => site.Kind).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
            AppendEvent("exploration_discovered",
                $"{actor}:{moved.Position.X},{moved.Position.Y}:{terrain}:{string.Join(',', resourcesHere)}");
        }
    }

    private void ReturnFromExploration(string actor, PlaytestInhabitantState person, SettlementExploration exploration)
    {
        if (exploration.OutingPath.Count == 1 && person.Position == exploration.OutingPath[0])
        {
            CreateKnowledgeArtifact(actor, exploration.OutingDiscoveries ?? []);
            inhabitants[actor] = person with
            {
                Exploration = exploration with
                {
                    OutingPath = [],
                    Returning = false,
                    LastOutingTick = WorldTick,
                }
            };
            AppendEvent("exploration_completed", $"{actor}:visited={exploration.VisitedTiles.Count}");
            return;
        }

        var destination = exploration.OutingPath.Count == 1 ? exploration.OutingPath[0] : exploration.OutingPath[^2];
        inhabitants[actor] = person with { Exploration = exploration };
        MoveToward(actor, inhabitants[actor], destination, "explore_return");
        var moved = inhabitants[actor];
        if (moved.Position == destination && exploration.OutingPath.Count > 1)
            inhabitants[actor] = moved with
            {
                Exploration = exploration with
                {
                    OutingPath = exploration.OutingPath.Take(exploration.OutingPath.Count - 1).ToArray(),
                }
            };
        else if (moved.MoveWaitTicks >= 30)
        {
            CreateKnowledgeArtifact(actor, exploration.OutingDiscoveries ?? []);
            inhabitants[actor] = moved with
            {
                Exploration = exploration with
                {
                    OutingPath = [],
                    Returning = false,
                    LastOutingTick = WorldTick,
                }
            };
            AppendEvent("exploration_aborted", $"{actor}:return_blocked");
        }
    }

    private static void ValidateExploration(SettlementExploration? exploration, SeededMap map, long worldTick)
    {
        if (exploration is null) return;
        if (exploration.VisitedTiles is null || exploration.OutingPath is null ||
            exploration.VisitedTiles.Count > ExplorationMemoryLimit ||
            exploration.OutingPath.Count > ExplorationStepsPerOuting + 1 ||
            (exploration.OutingDiscoveries?.Count ?? 0) > ExplorationStepsPerOuting + 1 ||
            exploration.LastOutingTick < 0 || exploration.LastOutingTick > worldTick ||
            exploration.VisitedTiles.Any(point => !map.IsPassable(point)) ||
            exploration.OutingPath.Any(point => !map.IsPassable(point)) ||
            (exploration.OutingDiscoveries ?? []).Any(point => !map.IsPassable(point)) ||
            (exploration.OutingDiscoveries ?? []).Distinct().Count() != (exploration.OutingDiscoveries?.Count ?? 0) ||
            exploration.OutingPath.Zip(exploration.OutingPath.Skip(1),
                (first, second) => map.CanFootStep(first, second)).Any(legal => !legal))
            throw new InvalidDataException("The saved local exploration record is invalid.");
    }
}

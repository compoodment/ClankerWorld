using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>
/// A per-person record of places actually visited. An outing only picks
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
    private const int StartingExplorationWarmthBudgetSteps = 8;
    private const int ExplorationCooldownTicks = 180;
    private const int ExplorationMemoryLimit = 256;

    private void AddExplorationCandidate(List<CognitionCandidate> candidates, string actor, PlaytestInhabitantState person)
    {
        if (NeedsUrgentFood(person) || NeedsUrgentWarmth(person))
            return;

        var exploration = person.Exploration;
        if (exploration?.OutingPath.Count > 0)
        {
            if (exploration.Returning || HasWarmthForOuting(person))
                candidates.Add(new("explore", exploration.Returning
                    ? "Keep returning to where your scouting trip started."
                    : "Continue scouting nearby terrain and resources; return when you choose.", 75));
            candidates.Add(new("explore_return", "Return to where your scouting trip started.", 76));
            return;
        }

        if (!HasWarmthForOuting(person) || person.HungerBasisPoints < OutingFullnessReserve ||
            person.Project is { Stage: not ("completed" or "cancelled") } ||
            exploration is not null && WorldTick - exploration.LastOutingTick < ExplorationCooldownTicks ||
            !map.FootNeighbors(person.Position).Any(map.IsPassable))
            return;

        candidates.Add(new("explore", "Scout nearby terrain and resources out of curiosity; choose when to return.", 75));
    }

    private bool HasWarmthForOuting(PlaytestInhabitantState person)
    {
        if (person.Survival is not { } condition) return true;
        // Keep the starting trial budget, then account for a longer recorded return.
        // Shelter at the starting tile is not protection carried along on the outing.
        var steps = Math.Max(StartingExplorationWarmthBudgetSteps, person.Exploration?.OutingPath.Count ?? 0);
        return map.FootNeighbors(person.Position).Where(map.IsPassable).All(next =>
        {
            var loss = Math.Max(0, OutdoorExposure(next) - ClothingProtection(person.InhabitantId, next));
            var stepTicks = (RoadStepCost(person.Position, next) + 99) / 100 +
                SettlementIllnessRules.TravelDelayTicks(condition.IllnessBasisPoints);
            return loss == 0 || condition.WarmthBasisPoints - (long)loss * stepTicks * steps * 2 >= UrgentWarmth;
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

        if (exploration.Returning)
        {
            ReturnFromExploration(actor, person, exploration with { Returning = true });
            return;
        }

        var occupied = inhabitants.Values.Where(item => item.InhabitantId != actor)
            .Select(item => item.Position).ToHashSet();
        var pullingCart = AttachedHandcart(actor) is not null;
        var next = map.FootNeighbors(person.Position)
            .Where(point => map.IsPassable(point) && !occupied.Contains(point) &&
                !exploration.OutingPath.Contains(point))
            .Where(point => !pullingCart || LegalHandcartStep(person.Position, point))
            // Match movement's corner occupancy rules before ranking exits.
            .Where(point => !map.IsDiagonalFootStep(person.Position, point) ||
                !occupied.Contains(new GridPoint(point.X, person.Position.Y)) &&
                !occupied.Contains(new GridPoint(person.Position.X, point.Y)))
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
        // river-wading and mountain rules. Only a completed step joins memory.
        inhabitants[actor] = person with { Exploration = exploration };
        MoveToward(actor, inhabitants[actor], next.Value, "explore");
        var moved = inhabitants[actor];
        if (moved.Position == person.Position)
            return;
        var visited = exploration.VisitedTiles.Contains(moved.Position)
            ? exploration.VisitedTiles
            : exploration.VisitedTiles.Append(moved.Position).TakeLast(ExplorationMemoryLimit).ToArray();
        var learned = RecordKnowledgeFact(actor, moved.Position);
        inhabitants[actor] = moved with
        {
            Exploration = exploration with
            {
                VisitedTiles = visited,
                OutingPath = exploration.OutingPath.Append(moved.Position).ToArray(),
                OutingDiscoveries = learned
                    ? (exploration.OutingDiscoveries ?? []).Append(moved.Position).TakeLast(ExplorationMemoryLimit).ToArray()
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

    private void ChooseExplorationReturn(string actor, PlaytestInhabitantState person)
    {
        if (person.Exploration is not { OutingPath.Count: > 0 } exploration) return;
        if (!exploration.Returning)
            AppendEvent("exploration_return_started", actor);
        ReturnFromExploration(actor, person, exploration with { Returning = true });
    }

    private void ReturnFromExploration(string actor, PlaytestInhabitantState person, SettlementExploration exploration)
    {
        if (exploration.OutingPath.Count == 1 && person.Position == exploration.OutingPath[0])
        {
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
        if (moved.Position != person.Position)
            RecordKnowledgeFact(actor, moved.Position);
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

    private static void ValidateExploration(SettlementExploration? exploration, SeededMap map,
        IEnumerable<BridgeState> bridges, long worldTick)
    {
        if (exploration is null) return;
        if (exploration.VisitedTiles is null || exploration.OutingPath is null ||
            exploration.VisitedTiles.Count > ExplorationMemoryLimit ||
            // An outward route never revisits a tile, so the map itself bounds
            // its length without imposing a distance limit on the scout.
            exploration.OutingPath.Count > map.Tiles.Count ||
            exploration.OutingPath.Distinct().Count() != exploration.OutingPath.Count ||
            (exploration.OutingDiscoveries?.Count ?? 0) > ExplorationMemoryLimit ||
            exploration.LastOutingTick < 0 || exploration.LastOutingTick > worldTick ||
            exploration.VisitedTiles.Any(point => !map.IsPassable(point)) ||
            exploration.OutingPath.Any(point => !map.IsPassable(point)) ||
            (exploration.OutingDiscoveries ?? []).Any(point => !map.IsPassable(point)) ||
            (exploration.OutingDiscoveries ?? []).Distinct().Count() != (exploration.OutingDiscoveries?.Count ?? 0))
            throw new InvalidDataException("The saved exploration record is invalid.");

        SeededMap? beforeOuting = null;
        for (var index = 1; index < exploration.OutingPath.Count; index++)
        {
            var first = exploration.OutingPath[index - 1];
            var second = exploration.OutingPath[index];
            if (map.CanFootStep(first, second)) continue;
            // These are committed outward steps, or remaining return waypoints.
            // A later deck can forbid an earlier wade through its tile. Bridges
            // built on the outing's first tick may also have appeared after a
            // step; only earlier ticks prove a deck existed throughout the trip.
            beforeOuting ??= MapWithBridges(map, bridges.Where(bridge => bridge.BuiltTick < exploration.LastOutingTick));
            if (!beforeOuting.CanFootStep(first, second))
                throw new InvalidDataException("The saved exploration record is invalid.");
        }
    }

    private static SeededMap MapWithBridges(SeededMap map, IEnumerable<BridgeState> bridges)
    {
        var decks = RiverBridgeRules.Decks(bridges);
        return RiverBridgeRules.SameDecks(map.BridgeDecks, decks) ? map : map with { BridgeDecks = decks };
    }
}

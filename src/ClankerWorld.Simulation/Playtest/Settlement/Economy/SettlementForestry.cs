using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private void AddForestryCandidates(List<CognitionCandidate> candidates, string actor,
        PlaytestInhabitantState state)
    {
        if (!HasCarriedItem(actor, "seed") && SharedItem("seed", actor) is null)
            return;
        if (ReplantableTree(state.Position) is { } tree)
            candidates.Add(new CognitionCandidate("replant_tree",
                "Carry a seed to a depleted tree site and replant it.", 32, tree.Id));
    }

    private MapResource? ReplantableTree(GridPoint origin)
    {
        var depleted = worldSystems.Ecology.Resources.Where(ecology =>
                ecology.Quantity == 0 && !ecology.IsPlanted)
            .Select(ecology => ecology.Id).ToHashSet(StringComparer.Ordinal);
        return map.Resources
            .Where(resource => resource.TreeKind is "broadleaf" or "conifer" && resource.IsRenewable &&
                depleted.Contains(resource.Id) && map.IsReachableFromCampOnFoot(resource.Position))
            .OrderBy(resource => map.FootDistance(origin, resource.Position))
            .ThenBy(resource => resource.Id, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private void ReplantTree(string actor, PlaytestInhabitantState state)
    {
        var tree = ReplantableTree(state.Position);
        if (tree is null) return;
        if (!HasCarriedItem(actor, "seed"))
        {
            CollectEquipment(actor, state, "seed");
            return;
        }
        if (!IsWithinInteractionRange(state.Position, tree.Position, ResourceInteractionRange))
        {
            MoveToward(actor, state, tree.Position, "replant_tree", ResourceInteractionRange);
            return;
        }

        var seed = society.Checkpoint.Inventory.Lots.FirstOrDefault(lot =>
            lot.OwnerId == actor && lot.ItemKind == "seed" && AvailableLotQuantity(lot) > 0);
        if (seed is null) return;
        var ecology = worldSystems.Ecology.GetResource(tree.Id);
        if (ecology.Quantity != 0 || ecology.IsPlanted) return;
        society.Apply(checkpoint => SocietyFixture.ConsumeInventory(checkpoint,
            actor, seed.Id, 1, "tree_replanting"));
        var day = WorldCalendarRules.FromTick(WorldTick, worldSystems.Config).DayIndex;
        var planted = ecology with
        {
            State = EcologyResourceState.Regenerating,
            IsPlanted = true,
            NextRegenerationDay = day + 3,
        };
        worldSystems = worldSystems with
        {
            Ecology = worldSystems.Ecology with
            {
                Resources = worldSystems.Ecology.Resources.Select(resource =>
                    resource.Id == tree.Id ? planted : resource).ToArray(),
            },
        };
        SyncEcologyResourceStates();
        AppendEvent("tree_replanted", $"{actor}:{tree.Id}:{tree.TreeKind}");
    }
}

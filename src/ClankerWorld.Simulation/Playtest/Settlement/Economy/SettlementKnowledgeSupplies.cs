namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private bool NeedsKnowledgePaper(string? householdId)
    {
        if (householdId is null || knowledge.Artifacts.Count >= AgentKnowledgeRules.MaximumArtifactsInWorld)
            return false;

        var writers = society.Checkpoint.Inhabitants.Where(person => person.HouseholdId == householdId &&
                AdultResident(person.Id) && knowledge.Facts.Any(fact => fact.OwnerId == person.Id) &&
                knowledge.Artifacts.Count(artifact => artifact.CreatorId == person.Id) <
                    AgentKnowledgeRules.MaximumArtifactsPerCreator)
            .Select(person => person.Id).ToHashSet(StringComparer.Ordinal);
        if (writers.Count == 0)
            return false;

        // A provisional reserve of two sheets per eligible writer permits a
        // book. Count sheets already collected too, so collection alone does
        // not prompt the household to manufacture another batch.
        var available = society.Checkpoint.Inventory.Lots.Where(lot => lot.ItemKind == KnowledgeContent.Paper &&
                (lot.OwnerId == householdId || writers.Contains(lot.OwnerId)))
            .Sum(AvailableLotQuantity);
        return available < writers.Count * 2;
    }
}

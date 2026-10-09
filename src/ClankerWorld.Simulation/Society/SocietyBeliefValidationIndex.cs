namespace ClankerWorld.Simulation.Society;

/// <summary>Indexes one validation's complete belief history, without changing or caching the checkpoint.</summary>
internal sealed class SocietyBeliefValidationIndex
{
    private readonly IReadOnlyDictionary<string, SocietyAgentBelief> byId;
    private readonly HashSet<string> inhabitants;
    private readonly Dictionary<string, string> roots = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Owner, string Turn), string> sourceRoots = [];

    internal SocietyBeliefValidationIndex(SocietyCheckpoint checkpoint,
        IReadOnlyDictionary<string, SocietyAgentBelief>? beliefsById = null)
    {
        byId = beliefsById ?? (checkpoint.Beliefs ?? []).ToDictionary(item => item.Id, StringComparer.Ordinal);
        inhabitants = checkpoint.Inhabitants.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var belief in byId.Values)
        {
            var root = Root(belief);
            if (belief.SourceTurnId is not { } turn) continue;
            var key = (belief.OwnerId, turn);
            if (sourceRoots.TryGetValue(key, out var earlierRoot) && earlierRoot != root)
                throw new InvalidDataException("An agent belief source turn is invalid or already recorded for this owner.");
            sourceRoots[key] = root;
        }
    }

    internal bool KnowsInhabitant(string id) => inhabitants.Contains(id);

    internal bool AllowsSourceTurn(SocietyAgentBelief belief) =>
        belief.SourceTurnId is not { } turn || !sourceRoots.TryGetValue((belief.OwnerId, turn), out var root) ||
        root == Root(belief);

    private string Root(SocietyAgentBelief belief)
    {
        if (roots.TryGetValue(belief.Id, out var root)) return root;
        if (belief.SupersedesBeliefId is not { } parentId)
            return roots[belief.Id] = belief.Id;
        if (roots.TryGetValue(parentId, out root))
            return roots[belief.Id] = root;
        var path = new List<SocietyAgentBelief>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var current = belief;
        while (!roots.TryGetValue(current.Id, out root))
        {
            if (!seen.Add(current.Id))
                throw new InvalidDataException("An agent belief correction chain is cyclic.");
            path.Add(current);
            if (current.SupersedesBeliefId is not { } previousId)
            {
                root = current.Id;
                break;
            }
            if (!byId.TryGetValue(previousId, out current!))
                throw new InvalidDataException("An agent belief correction has no matching owner-private predecessor.");
        }
        foreach (var item in path) roots[item.Id] = root!;
        return root!;
    }
}

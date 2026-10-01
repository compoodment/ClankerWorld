namespace ClankerWorld.GodotClient.ClientState;

/// <summary>
/// Remembers a rename the world host refused because another agent holds that
/// full name, so the Profile can keep the player's attempt in its name field
/// through ordinary world refreshes. It never changes the agent's displayed
/// name; only the host's snapshot does that. The attempt is forgotten once the
/// field no longer holds it for the same agent in the same world, or when the
/// Profile forgets it after an edit, a cancel or a successful rename.
/// </summary>
public sealed class RefusedAgentRename
{
    private Attempt? attempt;

    public void Remember(string worldId, string agentId, string fieldText) =>
        attempt = new Attempt(worldId, agentId, fieldText);

    public void Forget() => attempt = null;

    /// <summary>
    /// True while a refresh of this agent should leave the name field alone.
    /// Any other agent, world or field text forgets the attempt for good.
    /// </summary>
    public bool Keeps(string? worldId, string agentId, string fieldText)
    {
        if (attempt is { } refused &&
            string.Equals(refused.WorldId, worldId, StringComparison.Ordinal) &&
            string.Equals(refused.AgentId, agentId, StringComparison.Ordinal) &&
            string.Equals(refused.FieldText, fieldText, StringComparison.Ordinal))
            return true;
        attempt = null;
        return false;
    }

    private sealed record Attempt(string WorldId, string AgentId, string FieldText);
}

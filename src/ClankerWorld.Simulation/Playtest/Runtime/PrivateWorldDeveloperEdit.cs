namespace ClankerWorld.Simulation.Playtest;

/// <summary>An explicit owner edit bound to the world and observation it was prepared from.</summary>
public sealed record PrivateWorldDeveloperEdit(string WorldId, long ExpectedEventId, string AgentId,
    string Operation, string Value, int Amount = 0, string? OtherAgentId = null);

public sealed record PrivateWorldDeveloperEditResult(bool Applied, bool AlreadyApplied, string? Failure);

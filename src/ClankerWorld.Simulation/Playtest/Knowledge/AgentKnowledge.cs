using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>
/// An agent-scoped account of a map fact. DiscovererId remains the original
/// observer when the fact is later shared or read from a physical artifact.
/// </summary>
public sealed record AgentKnowledgeFact(
    string Id,
    string OwnerId,
    string DiscovererId,
    GridPoint Position,
    string Terrain,
    IReadOnlyList<string> ResourceKinds,
    long LearnedTick,
    string Acquisition,
    string? SourceAgentId = null,
    string? SourceArtifactId = null);

/// <summary>A bounded, physical map or field record made from firsthand finds.</summary>
public sealed record AgentKnowledgeArtifact(
    string Id,
    string CreatorId,
    string LotId,
    string Kind,
    string Title,
    long CreatedTick,
    IReadOnlyList<AgentKnowledgeFact> Facts);

/// <summary>Saved personal knowledge and the bounded artifacts carrying it.</summary>
public sealed record PrivateWorldKnowledgeState(
    IReadOnlyList<AgentKnowledgeFact> Facts,
    IReadOnlyList<AgentKnowledgeArtifact> Artifacts)
{
    public static PrivateWorldKnowledgeState Empty { get; } = new([], []);
}

internal static class AgentKnowledgeRules
{
    public const int MaximumFactsPerAgent = 128;
    public const int MaximumArtifactsPerCreator = 8;
    public const int MaximumArtifactsInWorld = 512;
    public const int MaximumFactsPerArtifact = 9;
    public const int MaximumResourceKindsPerFact = 4;

    public static void Validate(
        PrivateWorldKnowledgeState? knowledge,
        SeededMap map,
        SocietyCheckpoint society,
        long worldTick,
        int schemaVersion)
    {
        if (knowledge is null)
        {
            if (schemaVersion >= 23)
                throw new InvalidDataException("The current private-world schema requires agent map knowledge state.");
            knowledge = PrivateWorldKnowledgeState.Empty;
        }
        if (knowledge.Facts is null || knowledge.Artifacts is null)
            throw new InvalidDataException("The agent map-knowledge collections are missing.");
        if (knowledge.Facts.Any(item => item is null) || knowledge.Artifacts.Any(item => item is null) ||
            knowledge.Artifacts.Any(item => item.Facts is null || item.Facts.Any(fact => fact is null)))
            throw new InvalidDataException("The agent map-knowledge collections contain missing records.");
        if (schemaVersion < 23 && (knowledge.Facts.Count != 0 || knowledge.Artifacts.Count != 0))
            throw new InvalidDataException("Agent map knowledge requires private-world schema 23.");

        var knownAgents = society.Inhabitants.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        if (knowledge.Facts.Count > checked(knownAgents.Count * MaximumFactsPerAgent) ||
            knowledge.Artifacts.Count > MaximumArtifactsInWorld ||
            knowledge.Facts.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != knowledge.Facts.Count ||
            knowledge.Artifacts.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != knowledge.Artifacts.Count ||
            knowledge.Artifacts.Select(item => item.LotId).Distinct(StringComparer.Ordinal).Count() != knowledge.Artifacts.Count)
            throw new InvalidDataException("Agent map knowledge exceeds its bounded ledger or has duplicate IDs.");

        foreach (var group in knowledge.Facts.GroupBy(item => item.OwnerId, StringComparer.Ordinal))
            if (group.Count() > MaximumFactsPerAgent)
                throw new InvalidDataException("An agent map-knowledge ledger exceeds its size limit.");
        foreach (var group in knowledge.Artifacts.GroupBy(item => item.CreatorId, StringComparer.Ordinal))
            if (group.Count() > MaximumArtifactsPerCreator)
                throw new InvalidDataException("An agent has created more map artifacts than the world limit.");

        foreach (var fact in knowledge.Facts)
        {
            if (fact is null || string.IsNullOrWhiteSpace(fact.Id) || fact.Id.Length > 160 ||
                !knownAgents.Contains(fact.OwnerId) || !knownAgents.Contains(fact.DiscovererId) ||
                fact.SourceAgentId is { } source && !knownAgents.Contains(source) ||
                fact.SourceArtifactId is { } artifactId && !knowledge.Artifacts.Any(item => item.Id == artifactId) ||
                !map.Contains(fact.Position) || !map.IsPassable(fact.Position) ||
                !Enum.TryParse<TerrainKind>(fact.Terrain, ignoreCase: false, out _) ||
                fact.ResourceKinds is null || fact.ResourceKinds.Count > MaximumResourceKindsPerFact ||
                fact.ResourceKinds.Any(kind => !IsSafeToken(kind, 48)) ||
                fact.ResourceKinds.Distinct(StringComparer.Ordinal).Count() != fact.ResourceKinds.Count ||
                fact.LearnedTick < 0 || fact.LearnedTick > worldTick ||
                fact.Acquisition is not ("firsthand" or "shared" or "read") ||
                fact.Acquisition == "firsthand" && (fact.SourceAgentId is not null || fact.SourceArtifactId is not null) ||
                fact.Acquisition != "firsthand" && (fact.SourceAgentId is null || fact.SourceArtifactId is null ||
                    !knowledge.Artifacts.Any(item => item.Id == fact.SourceArtifactId && item.Facts.Any(sourceFact =>
                        sourceFact.Position == fact.Position && sourceFact.Terrain == fact.Terrain &&
                        sourceFact.DiscovererId == fact.DiscovererId &&
                        sourceFact.ResourceKinds.SequenceEqual(fact.ResourceKinds, StringComparer.Ordinal)))))
                throw new InvalidDataException("The agent map-knowledge ledger contains an invalid fact.");
        }

        var inventoryLots = society.Inventory.Lots.ToDictionary(item => item.Id, StringComparer.Ordinal);
        foreach (var artifact in knowledge.Artifacts)
        {
            if (artifact is null || string.IsNullOrWhiteSpace(artifact.Id) || artifact.Id.Length > 128 ||
                !knownAgents.Contains(artifact.CreatorId) || string.IsNullOrWhiteSpace(artifact.LotId) ||
                artifact.Kind is not ("field_map" or "field_record") || string.IsNullOrWhiteSpace(artifact.Title) ||
                artifact.Title.Length > 64 || artifact.Title.Any(char.IsControl) ||
                artifact.CreatedTick < 0 || artifact.CreatedTick > worldTick ||
                artifact.Facts is null || artifact.Facts.Count is < 1 or > MaximumFactsPerArtifact ||
                artifact.Facts.Select(item => item.Position).Distinct().Count() != artifact.Facts.Count ||
                !inventoryLots.TryGetValue(artifact.LotId, out var lot) || lot.ItemKind != artifact.Kind || lot.Quantity != 1 ||
                artifact.Facts.Any(fact => !IsValidArtifactFact(fact, artifact.CreatorId, artifact.CreatedTick, knownAgents, map)))
                throw new InvalidDataException("An agent knowledge artifact is invalid or has no matching physical inventory lot.");
        }
    }

    private static bool IsValidArtifactFact(
        AgentKnowledgeFact fact,
        string creatorId,
        long createdTick,
        HashSet<string> knownAgents,
        SeededMap map) =>
        fact is not null && !string.IsNullOrWhiteSpace(fact.Id) && fact.Id.Length <= 160 &&
        fact.OwnerId == creatorId && fact.DiscovererId == creatorId &&
        knownAgents.Contains(fact.OwnerId) && knownAgents.Contains(fact.DiscovererId) &&
        map.Contains(fact.Position) && map.IsPassable(fact.Position) &&
        Enum.TryParse<TerrainKind>(fact.Terrain, ignoreCase: false, out _) &&
        fact.ResourceKinds is not null && fact.ResourceKinds.Count <= MaximumResourceKindsPerFact &&
        fact.ResourceKinds.All(kind => IsSafeToken(kind, 48)) &&
        fact.ResourceKinds.Distinct(StringComparer.Ordinal).Count() == fact.ResourceKinds.Count &&
        fact.LearnedTick >= 0 && fact.LearnedTick <= createdTick && fact.Acquisition == "firsthand" &&
        fact.SourceAgentId is null && fact.SourceArtifactId is null;

    private static bool IsSafeToken(string? value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximumLength &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-');
}

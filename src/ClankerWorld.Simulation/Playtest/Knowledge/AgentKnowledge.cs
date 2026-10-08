using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using System.Text.Json.Serialization;
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

/// <summary>An exact paid input, retained after its physical lot has been consumed.</summary>
public sealed record AgentKnowledgeMaterial(string ReservationId, string LotId, string ItemKind, int Quantity);

/// <summary>Writing is physical work on reserved personal materials, paused between actual work steps.</summary>
public sealed record AgentKnowledgeWritingProject(
    string Id, string ActorId, string Kind, string CandidateId, string? SourceArtifactId,
    IReadOnlyList<AgentKnowledgeFact> Facts, long StartedTick, long LastWorkedTick, int WorkDone,
    IReadOnlyList<AgentKnowledgeMaterial> Materials)
{
    [JsonIgnore] public IReadOnlyList<string> MaterialReservationIds => Materials.Select(item => item.ReservationId).ToArray();
    [JsonIgnore] public int WorkRequired => AgentKnowledgeRules.WritingWork(Kind);
}

/// <summary>A bounded, physical map, record or book containing its author's actual learned facts.</summary>
public sealed record AgentKnowledgeArtifact(
    string Id,
    string CreatorId,
    string LotId,
    string Kind,
    string Title,
    long CreatedTick,
    IReadOnlyList<AgentKnowledgeFact> Facts)
{
    public string? WritingProjectId { get; init; }
    public IReadOnlyList<AgentKnowledgeMaterial> Materials { get; init; } = [];
    public string? SourceArtifactId { get; init; }
    [JsonIgnore] public IReadOnlyList<string> MaterialReservationIds => Materials.Select(item => item.ReservationId).ToArray();
}

/// <summary>Saved personal knowledge and the bounded artifacts carrying it.</summary>
public sealed record PrivateWorldKnowledgeState(
    IReadOnlyList<AgentKnowledgeFact> Facts,
    IReadOnlyList<AgentKnowledgeArtifact> Artifacts)
{
    [JsonRequired] public IReadOnlyList<AgentKnowledgeWritingProject> WritingProjects { get; init; } = [];
    [JsonRequired] public IReadOnlyList<AgentKnowledgeFact> EarlierFacts { get; init; } = [];
    public static PrivateWorldKnowledgeState Empty { get; } = new([], []);
}

internal static class AgentKnowledgeRules
{
    public const int MaximumFactsPerAgent = 128;
    public const int MaximumArtifactsPerCreator = 8;
    public const int MaximumArtifactsInWorld = 512;
    public const int MaximumFactsPerArtifact = 9;
    public const int MaximumResourceKindsPerFact = 4;
    public const int MaximumEarlierFactsPerAgent = MaximumFactsPerArtifact * (MaximumArtifactsPerCreator + 1);

    public static bool IsArtifactKind(string kind) => kind is "field_record" or "field_map" or "book";
    public static int WritingWork(string kind) => kind switch { "field_record" => 4, "field_map" => 6, "book" => 12, _ => 0 };
    public static IReadOnlyList<(string Kind, int Quantity)> WritingMaterials(string kind) => kind == "book"
        ? [("paper", 2), ("cloth", 1)] : [("paper", 1)];
    public static string MaterialPurpose(string projectId, string kind) => $"knowledge_writing:{projectId}:{kind}";

    public static void Validate(PrivateWorldKnowledgeState knowledge, SeededMap map,
        SocietyCheckpoint society, long worldTick)
    {
        if (knowledge.Facts is null || knowledge.Artifacts is null || knowledge.WritingProjects is null || knowledge.EarlierFacts is null ||
            knowledge.Facts.Any(item => item is null) || knowledge.Artifacts.Any(item => item is null) ||
            knowledge.EarlierFacts.Any(item => item is null) ||
            knowledge.WritingProjects.Any(item => item is null) ||
            knowledge.Artifacts.Any(item => !ValidText(item.Id, 128) || item.Facts is null || item.Facts.Any(fact => fact is null)))
            throw new InvalidDataException("The agent knowledge collections are missing or contain null records.");
        var agents = society.Inhabitants.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        if (knowledge.Facts.Count > checked(agents.Count * MaximumFactsPerAgent) ||
            knowledge.Artifacts.Count > MaximumArtifactsInWorld || knowledge.WritingProjects.Count > agents.Count ||
            knowledge.Facts.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != knowledge.Facts.Count ||
            knowledge.Facts.Select(item => (item.OwnerId, item.Position)).Distinct().Count() != knowledge.Facts.Count ||
            knowledge.Artifacts.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != knowledge.Artifacts.Count ||
            knowledge.Artifacts.Select(item => item.LotId).Distinct(StringComparer.Ordinal).Count() != knowledge.Artifacts.Count ||
            knowledge.WritingProjects.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != knowledge.WritingProjects.Count ||
            knowledge.WritingProjects.Select(item => item.ActorId).Distinct(StringComparer.Ordinal).Count() != knowledge.WritingProjects.Count ||
            knowledge.Facts.GroupBy(item => item.OwnerId).Any(group => group.Count() > MaximumFactsPerAgent) ||
            knowledge.Artifacts.GroupBy(item => item.CreatorId).Any(group => group.Count() > MaximumArtifactsPerCreator))
            throw new InvalidDataException("Agent knowledge exceeds its bounded ledger or has duplicate identities.");
        var artifacts = knowledge.Artifacts.ToDictionary(item => item.Id, StringComparer.Ordinal);
        foreach (var fact in knowledge.Facts)
            if (!ValidFact(fact, worldTick))
                throw new InvalidDataException("The agent map-knowledge ledger contains an invalid fact or provenance.");
        if (knowledge.EarlierFacts.Count > 0)
        {
            var earlierKeys = knowledge.EarlierFacts.Select(FactVersionKey).ToHashSet();
            var liveKeys = knowledge.Facts.Select(FactVersionKey).ToHashSet();
            var references = FactReferencesFor(knowledge);
            var currentSites = knowledge.Facts.ToDictionary(fact => (fact.OwnerId, fact.Position));
            if (knowledge.EarlierFacts.Count > checked(agents.Count * MaximumEarlierFactsPerAgent) ||
                knowledge.EarlierFacts.GroupBy(fact => fact.OwnerId).Any(group => group.Count() > MaximumEarlierFactsPerAgent) ||
                earlierKeys.Count != knowledge.EarlierFacts.Count || knowledge.EarlierFacts.Any(fact =>
                    !ValidFact(fact, worldTick) || !currentSites.TryGetValue((fact.OwnerId, fact.Position), out var current) ||
                    current.Id != fact.Id || current.LearnedTick < fact.LearnedTick ||
                    liveKeys.Contains(FactVersionKey(fact)) || !references.Contains(fact)))
                throw new InvalidDataException("Earlier knowledge must be bounded, distinct and retained only for an existing written snapshot.");
        }
        var recordedFacts = knowledge.EarlierFacts.Count == 0 ? knowledge.Facts
            : knowledge.Facts.Concat(knowledge.EarlierFacts).ToArray();
        var usedReservations = new HashSet<string>(StringComparer.Ordinal);
        var usedProjects = new HashSet<string>(StringComparer.Ordinal);
        foreach (var artifact in knowledge.Artifacts)
        {
            var lot = society.Inventory.Lots.FirstOrDefault(item => item.Id == artifact.LotId);
            if (!ValidText(artifact.Id, 128) || !ValidText(artifact.LotId, 128) || !agents.Contains(artifact.CreatorId) ||
                !IsArtifactKind(artifact.Kind) || !ValidText(artifact.Title, 64) ||
                artifact.CreatedTick < 0 || artifact.CreatedTick > worldTick ||
                lot is null || lot.ItemKind != artifact.Kind || lot.Quantity != 1 ||
                !ValidFacts(artifact.Facts, artifact.CreatorId, artifact.CreatedTick, artifact.SourceArtifactId) ||
                artifact.Kind == "field_record" && artifact.Facts.Count != 1 ||
                !ValidSource(artifact.SourceArtifactId, artifact.Kind, artifact.Facts, artifact.CreatedTick) ||
                !ValidateMaterials(artifact.WritingProjectId, artifact.CreatorId, artifact.Kind, artifact.Materials, completed: true))
                throw new InvalidDataException("An agent knowledge artifact has invalid facts, inputs or physical identity.");
        }
        foreach (var project in knowledge.WritingProjects)
        {
            if (!ValidText(project.Id, 128) || !agents.Contains(project.ActorId) ||
                society.GetInhabitant(project.ActorId).Status != SocietyInhabitantStatus.Active || !IsArtifactKind(project.Kind) ||
                project.StartedTick < 0 || project.StartedTick > project.LastWorkedTick || project.LastWorkedTick > worldTick ||
                project.WorkDone < 0 || project.WorkDone >= WritingWork(project.Kind) ||
                project.CandidateId != (project.SourceArtifactId is null ? "knowledge_write:" + project.Kind : "knowledge_copy:" + project.SourceArtifactId) ||
                !ValidFacts(project.Facts, project.ActorId, project.StartedTick, project.SourceArtifactId) ||
                project.Kind == "field_record" && project.Facts.Count != 1 ||
                !ValidSource(project.SourceArtifactId, project.Kind, project.Facts, project.StartedTick) ||
                !ValidateMaterials(project.Id, project.ActorId, project.Kind, project.Materials, completed: false))
                throw new InvalidDataException("An agent knowledge-writing project has invalid work, facts or reserved inputs.");
        }
        // A source chain must end at an actual firsthand observation, even when several generations of copies exist.
        var validatedAncestry = new HashSet<string>(StringComparer.Ordinal);
        foreach (var artifact in knowledge.Artifacts)
            CheckAncestry(artifact, new HashSet<string>(StringComparer.Ordinal));

        bool ValidFact(AgentKnowledgeFact fact, long latestTick) => fact is not null && ValidText(fact.Id, 160) &&
            agents.Contains(fact.OwnerId) && agents.Contains(fact.DiscovererId) &&
            map.Contains(fact.Position) && map.IsPassable(fact.Position) &&
            Enum.TryParse<TerrainKind>(fact.Terrain, ignoreCase: false, out _) &&
            fact.ResourceKinds is not null && fact.ResourceKinds.Count <= MaximumResourceKindsPerFact &&
            fact.ResourceKinds.All(kind => IsSafeToken(kind, 48)) && fact.ResourceKinds.Distinct(StringComparer.Ordinal).Count() == fact.ResourceKinds.Count &&
            fact.LearnedTick >= 0 && fact.LearnedTick <= latestTick &&
            (fact.Acquisition == "firsthand" && fact.OwnerId == fact.DiscovererId && fact.SourceAgentId is null && fact.SourceArtifactId is null ||
             fact.Acquisition is "shared" or "read" && fact.SourceAgentId is { } source && agents.Contains(source) &&
             fact.SourceArtifactId is { } sourceArtifact && artifacts.TryGetValue(sourceArtifact, out var original) &&
             original.CreatedTick <= fact.LearnedTick && original.Facts is not null && original.Facts.Any(other => SameDiscovery(fact, other)));

        bool ValidFacts(IReadOnlyList<AgentKnowledgeFact>? facts, string actor, long tick, string? sourceArtifactId) =>
            facts is { Count: >= 1 and <= MaximumFactsPerArtifact } &&
            facts.Select(item => item?.Position).Distinct().Count() == facts.Count &&
            facts.All(fact => fact is not null && fact.OwnerId == actor && ValidFact(fact, tick) &&
                (sourceArtifactId is null
                    ? recordedFacts.Any(learned => SameLearnedFact(learned, fact))
                    : artifacts.TryGetValue(sourceArtifactId, out var source) &&
                      fact.Acquisition == "read" && fact.SourceArtifactId == sourceArtifactId &&
                      fact.SourceAgentId == source.CreatorId &&
                      recordedFacts.Any(learned => learned.OwnerId == actor && learned.Id == fact.Id &&
                          learned.LearnedTick <= fact.LearnedTick && learned.Position == fact.Position)));

        bool ValidSource(string? source, string kind, IReadOnlyList<AgentKnowledgeFact> facts, long tick) => source is null ||
            artifacts.TryGetValue(source, out var original) && original.Kind == kind && original.CreatedTick <= tick &&
            original.Facts is not null && original.Facts.Count == facts.Count &&
            original.Facts.All(fact => facts.Any(copied => SameDiscovery(fact, copied)));

        bool ValidateMaterials(string? projectId, string actor, string kind, IReadOnlyList<AgentKnowledgeMaterial>? materials, bool completed)
        {
            if (!ValidText(projectId, 128) || !usedProjects.Add(projectId!) || materials is not { Count: >= 1 and <= 3 } ||
                materials.Any(item => item is null || !ValidText(item.ReservationId, 256) || !ValidText(item.LotId, int.MaxValue) ||
                    item.Quantity <= 0 || !usedReservations.Add(item.ReservationId))) return false;
            var expected = WritingMaterials(kind);
            if (materials.Any(item => !expected.Any(input => input.Kind == item.ItemKind)) ||
                expected.Any(input => materials.Where(item => item.ItemKind == input.Kind).Sum(item => (long)item.Quantity) != input.Quantity)) return false;
            return materials.All(input =>
            {
                var reservation = society.Inventory.Reservations.FirstOrDefault(item => item.Id == input.ReservationId);
                if (reservation is null || reservation.OwnerId != actor || reservation.LotId != input.LotId ||
                    reservation.Quantity != input.Quantity || reservation.Purpose != MaterialPurpose(projectId!, input.ItemKind) ||
                    reservation.ExpiryTick != long.MaxValue || !reservation.IsExclusive ||
                    reservation.State != (completed ? InventoryReservationState.Completed : InventoryReservationState.Reserved)) return false;
                if (completed) return true;
                var lot = society.Inventory.Lots.FirstOrDefault(item => item.Id == input.LotId);
                return lot is not null && lot.OwnerId == actor && lot.ItemKind == input.ItemKind && lot.Quantity >= input.Quantity &&
                    lot.ContainerLotId is null && lot.DeliveryBuildingId is null && PersonalEquipmentRules.IsCarried(lot, actor);
            });
        }

        void CheckAncestry(AgentKnowledgeArtifact artifact, HashSet<string> path)
        {
            if (validatedAncestry.Contains(artifact.Id)) return;
            if (!path.Add(artifact.Id)) throw new InvalidDataException("Knowledge artifacts contain cyclic provenance.");
            foreach (var source in artifact.Facts.Select(fact => fact.SourceArtifactId).Append(artifact.SourceArtifactId)
                         .Where(id => id is not null).Distinct(StringComparer.Ordinal))
                CheckAncestry(artifacts[source!], path);
            path.Remove(artifact.Id);
            validatedAncestry.Add(artifact.Id);
        }
    }

    internal static bool ReferencesFact(PrivateWorldKnowledgeState knowledge, AgentKnowledgeFact fact) =>
        FactReferencesFor(knowledge).Contains(fact);

    internal static PrivateWorldKnowledgeState PruneEarlierFacts(PrivateWorldKnowledgeState knowledge)
    {
        var references = FactReferencesFor(knowledge);
        return knowledge with { EarlierFacts = knowledge.EarlierFacts.Where(references.Contains).ToArray() };
    }

    private sealed record FactVersion(string Id, string Owner, string Discoverer, GridPoint Position, string Terrain,
        long Learned, string Acquisition, string? Agent, string? Artifact, string Resources);

    private static FactVersion FactVersionKey(AgentKnowledgeFact fact) =>
        new(fact.Id, fact.OwnerId, fact.DiscovererId, fact.Position, fact.Terrain, fact.LearnedTick, fact.Acquisition,
            fact.SourceAgentId, fact.SourceArtifactId, string.Join('\0', fact.ResourceKinds ?? []));

    private static (string Id, string Owner, GridPoint Position) CopySiteKey(AgentKnowledgeFact fact) =>
        (fact.Id, fact.OwnerId, fact.Position);

    private sealed record FactReferences(HashSet<FactVersion> Exact,
        Dictionary<(string Id, string Owner, GridPoint Position), long> Copies)
    {
        public bool Contains(AgentKnowledgeFact fact) => Exact.Contains(FactVersionKey(fact)) ||
            Copies.TryGetValue(CopySiteKey(fact), out var copiedTick) && fact.LearnedTick <= copiedTick;
    }

    private static FactReferences FactReferencesFor(PrivateWorldKnowledgeState knowledge)
    {
        var references = new FactReferences([], []);
        foreach (var artifact in knowledge.Artifacts) Add(artifact.Facts ?? [], artifact.SourceArtifactId);
        foreach (var project in knowledge.WritingProjects) Add(project.Facts ?? [], project.SourceArtifactId);
        return references;

        void Add(IReadOnlyList<AgentKnowledgeFact> facts, string? source)
        {
            foreach (var fact in facts.Where(fact => fact is not null))
            {
                if (source is null) references.Exact.Add(FactVersionKey(fact));
                else
                {
                    var key = CopySiteKey(fact);
                    references.Copies[key] = Math.Max(references.Copies.GetValueOrDefault(key), fact.LearnedTick);
                }
            }
        }
    }

    internal static bool SameSiteKnowledge(AgentKnowledgeFact first, AgentKnowledgeFact second) =>
        first.Position == second.Position && first.Terrain == second.Terrain &&
        first.ResourceKinds is not null && second.ResourceKinds is not null && first.ResourceKinds.SequenceEqual(second.ResourceKinds, StringComparer.Ordinal);
    internal static bool SameDiscovery(AgentKnowledgeFact first, AgentKnowledgeFact second) =>
        first.DiscovererId == second.DiscovererId && SameSiteKnowledge(first, second);
    internal static bool SameLearnedFact(AgentKnowledgeFact first, AgentKnowledgeFact second) => SameDiscovery(first, second) &&
        first.Id == second.Id && first.OwnerId == second.OwnerId && first.LearnedTick == second.LearnedTick &&
        first.Acquisition == second.Acquisition && first.SourceAgentId == second.SourceAgentId && first.SourceArtifactId == second.SourceArtifactId;
    private static bool ValidText(string? value, int maximumLength) => !string.IsNullOrWhiteSpace(value) &&
        value.Length <= maximumLength && !value.Any(char.IsControl);
    private static bool IsSafeToken(string? value, int maximumLength) => ValidText(value, maximumLength) &&
        value!.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-');
}

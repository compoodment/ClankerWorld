using System.Collections.Concurrent;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class AgentKnowledgeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InheritedNaturalFieldRecordKeepsItsPhysicalIdentityAndPrivateProvenance(bool selectedHeir)
    {
        using var seed = new PrivateWorldRuntime("inherited-natural-record");
        var initial = seed.ExportState();
        var creator = initial.Inhabitants[0].InhabitantId;
        using var scout = PrivateWorldRuntime.Restore(initial with
        {
            Inhabitants = initial.Inhabitants.Select(person => person with { HungerBasisPoints = 9_500 }).ToArray(),
        }, _ => new CandidateProvider(creator, "explore"));
        for (var tick = 0; tick < 75 && scout.ExportState().Knowledge!.Artifacts.Count == 0; tick++)
            _ = await scout.AdvanceOneTickAsync();
        var state = scout.ExportState();
        var artifact = Assert.Single(state.Knowledge!.Artifacts);
        var physical = state.Inhabitants.Single(person => person.InhabitantId == creator);
        var dead = SocietyFixture.Kill(state.Society.Society, creator, SocietyDeathCause.Accident).Checkpoint;
        var estate = Assert.Single(dead.Estates);
        // Bounded test clock: accelerate only this valid fixture's escrow deadline.
        dead = dead with { Estates = [estate with { ExpiryTick = state.Society.Society.WorldTick + 1 }] };
        var heir = dead.Inhabitants.Where(person => person.Status == SocietyInhabitantStatus.Active).Last().Id;
        if (selectedHeir)
        {
            dead = SocietyFixture.MarkWillStarted(dead, estate.Id).Checkpoint;
            dead = SocietyFixture.ResolveWill(dead, estate.Id, heir, "accepted").Checkpoint;
        }
        state = state with
        {
            Society = state.Society with { Society = dead },
            Inhabitants = state.Inhabitants.Where(person => person.InhabitantId != creator).ToArray(),
            DeceasedInhabitants = [new PlaytestDeceasedInhabitantState(creator, state.Society.Society.WorldTick,
                dead.AgeAt(dead.GetInhabitant(creator), state.Society.Society.WorldTick), physical)],
        };
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)),
            _ => new CandidateProvider("nobody", "never"));
        _ = await world.AdvanceOneTickAsync();
        var settled = world.ExportState();
        Assert.True(settled.Society.Society.GetEstate(estate.Id).Settled);
        var inherited = settled.Society.Society.Inventory.GetLot(artifact.LotId);
        Assert.Equal(1, inherited.Quantity);
        Assert.Equal(selectedHeir ? heir : estate.BeneficiaryIds.Order(StringComparer.Ordinal).First(), inherited.OwnerId);
        Assert.Equal(ArtifactKey(artifact), ArtifactKey(Assert.Single(settled.Knowledge!.Artifacts)));
        Assert.DoesNotContain(settled.Knowledge.Facts, fact => fact.OwnerId != creator);
        var directory = Directory.CreateTempSubdirectory("inherited-record-save-");
        try
        {
            var file = new PrivateWorldStateFile(Path.Combine(directory.FullName, "world.json"));
            file.Save(world);
            using var restored = file.LoadOrCreate(state.WorldSeed);
            var snapshot = new OwnerWorldObservationStore(restored).GetSnapshot();
            Assert.Single(snapshot.Inhabitants.Single(person => person.Id == inherited.OwnerId).KnowledgeArtifacts);
            Assert.Equal(artifact.CreatorId, restored.ExportState().Knowledge!.Artifacts.Single().CreatorId);
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public async Task ExplorationCreatesBoundedPersonalKnowledgeAndMigratesThroughSaveReload()
    {
        using var seed = new PrivateWorldRuntime("personal-map-records");
        var initial = seed.ExportState();
        var explorerId = initial.Inhabitants[0].InhabitantId;
        var provider = new CandidateProvider(explorerId, "explore");
        using var world = PrivateWorldRuntime.Restore(initial with
        {
            Inhabitants = initial.Inhabitants.Select(person => person with { HungerBasisPoints = 9_500 }).ToArray(),
        }, _ => provider);

        for (var tick = 0; tick < 75; tick++)
            _ = await world.AdvanceOneTickAsync();

        var saved = world.ExportState();
        var facts = saved.Knowledge!.Facts.Where(fact => fact.OwnerId == explorerId).ToArray();
        var artifacts = saved.Knowledge.Artifacts.Where(artifact => artifact.CreatorId == explorerId).ToArray();
        Assert.NotEmpty(facts);
        Assert.NotEmpty(artifacts);
        Assert.All(facts, fact => Assert.Equal(explorerId, fact.DiscovererId));
        Assert.All(artifacts, artifact => Assert.InRange(artifact.Facts.Count, 1, 9));
        Assert.DoesNotContain(saved.Knowledge.Facts, fact => fact.OwnerId != explorerId);
        Assert.NotEmpty(provider.KnownMapFactsByAgent[explorerId]);
        Assert.All(saved.Inhabitants.Where(person => person.InhabitantId != explorerId), person =>
            Assert.Empty(provider.KnownMapFactsByAgent.GetValueOrDefault(person.InhabitantId) ?? []));
        Assert.All(artifacts, artifact => Assert.Equal(artifact.Kind,
            saved.Society.Society.Inventory.GetLot(artifact.LotId).ItemKind));

        var bytes = PrivateWorldRuntimeCodec.Encode(saved);
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes));
        var restoredKnowledge = restored.ExportState().Knowledge!;
        Assert.Equal(facts.Select(FactKey), restoredKnowledge.Facts.Where(fact => fact.OwnerId == explorerId).Select(FactKey));
        Assert.Equal(artifacts.Select(ArtifactKey), restoredKnowledge.Artifacts.Where(artifact => artifact.CreatorId == explorerId).Select(ArtifactKey));
    }

    [Fact]
    public async Task SharingTeachesOnlyTheRecipientAndProjectsSafeTelemetryAndAgentRecords()
    {
        using var seed = new PrivateWorldRuntime("shared-field-record");
        var state = seed.ExportState();
        var sourceId = state.Inhabitants[0].InhabitantId;
        var recipientId = state.Inhabitants[1].InhabitantId;
        var otherId = state.Inhabitants[2].InhabitantId;
        state = WithArtifact(state, sourceId, state.Inhabitants[0].Position);
        var sourcePosition = state.Inhabitants.Single(person => person.InhabitantId == sourceId).Position;
        var recipientPosition = state.Map.FootNeighbors(sourcePosition)
            .Where(state.Map.IsPassable)
            .First(point => !state.Inhabitants.Any(person => person.InhabitantId != recipientId && person.Position == point));
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == recipientId
                ? person with { Position = recipientPosition } : person).ToArray(),
        };
        using var world = PrivateWorldRuntime.Restore(state, id => new CandidateProvider(
            sourceId, $"knowledge_share:knowledge-artifact-000001|{recipientId}", id == sourceId));
        var directory = Directory.CreateTempSubdirectory("clankerworld-knowledge-log-");
        try
        {
            var presence = new OwnerClientPresenceLease(TimeSpan.FromSeconds(30));
            presence.RecordAuthenticatedReconnect("test-owner");
            var logger = new RecordingLogger<PrivateWorldRuntimeService>();
            using var service = new PrivateWorldRuntimeService(world,
                new PrivateWorldStateFile(Path.Combine(directory.FullName, "world.json")), presence, logger);
            Assert.True(await service.TryAdvanceOnceAsync());

            var learned = world.ExportState().Knowledge!.Facts.Single(fact => fact.OwnerId == recipientId);
            Assert.Equal(sourceId, learned.DiscovererId);
            Assert.Equal(sourceId, learned.SourceAgentId);
            Assert.Equal("shared", learned.Acquisition);
            Assert.DoesNotContain(world.ExportState().Knowledge!.Facts, fact => fact.OwnerId == otherId);
            Assert.Contains(world.ExportState().Events, item => item.Kind == "agent_knowledge_shared");

            var snapshot = new OwnerWorldObservationStore(world).GetSnapshot();
            var source = snapshot.Inhabitants.Single(item => item.Id == sourceId);
            var recipient = snapshot.Inhabitants.Single(item => item.Id == recipientId);
            Assert.Single(source.KnowledgeArtifacts);
            Assert.Contains(recipient.RecentKnowledgeFacts, fact => fact.Acquisition == "shared" && fact.SourceAgentName == source.DisplayName);
            Assert.Empty(recipient.KnowledgeArtifacts);

            Assert.Contains(logger.Messages, message => message.Contains("agent_knowledge", StringComparison.Ordinal) &&
                message.Contains("agent_knowledge_shared", StringComparison.Ordinal) &&
                message.Contains("recipient=" + recipientId, StringComparison.Ordinal));
            Assert.DoesNotContain(logger.Messages, message => message.Contains("private-secret-resource", StringComparison.Ordinal));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task TradingAPhysicalMapTransfersItsKnowledgeOnlyToTheBuyer()
    {
        using var seed = new PrivateWorldRuntime("traded-field-map");
        var state = seed.ExportState();
        var sellerId = state.Inhabitants[0].InhabitantId;
        var buyerId = state.Inhabitants[1].InhabitantId;
        var otherId = state.Inhabitants[2].InhabitantId;
        state = WithArtifact(state, sellerId, state.Inhabitants[0].Position, "field_map");
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory,
            "trade-clothing-for-map", "clothing", buyerId, 2);
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person with { HungerBasisPoints = 9_500 }).ToArray(),
            Society = state.Society with
            {
                Society = state.Society.Society with { Inventory = inventory },
            },
        };
        using var world = PrivateWorldRuntime.Restore(state, id => id == sellerId
            ? new CandidateProvider(sellerId, "trade_propose:")
            : id == buyerId ? new CandidateProvider(buyerId, "trade_accept:")
            : new CandidateProvider("nobody", "never"));

        for (var tick = 0; tick < 120; tick++)
        {
            var currentOffers = world.ExportState().Society.Society.Inventory.Offers;
            if (currentOffers.Count > 0 && currentOffers.All(offer => offer.State != DirectBarterState.Open))
                break;
            _ = await world.AdvanceOneTickAsync();
        }

        var final = world.ExportState();
        Assert.Contains(final.Society.Society.Inventory.Lots,
            lot => lot.ItemKind == "field_map" && lot.OwnerId == buyerId);
        Assert.Contains(final.Society.Society.Inventory.Lots,
            lot => lot.ItemKind == "clothing" && lot.OwnerId == sellerId);
        var learned = Assert.Single(final.Knowledge!.Facts, fact => fact.OwnerId == buyerId);
        Assert.Equal(sellerId, learned.DiscovererId);
        Assert.Equal(sellerId, learned.SourceAgentId);
        Assert.Equal("read", learned.Acquisition);
        Assert.DoesNotContain(final.Knowledge.Facts, fact => fact.OwnerId == otherId);
        Assert.Contains(final.Events, item => item.Kind == "agent_knowledge_artifact_read");
    }

    [Fact]
    public void SchemaTwentyThroughTwentyTwoCheckpointsMigrateWithEmptyPrivateKnowledge()
    {
        using var seed = new PrivateWorldRuntime("knowledge-schema-migration");
        var schema20Checkpoint = seed.ExportState() with { SchemaVersion = 20, Towns = null, Knowledge = null };
        using var restored20 = PrivateWorldRuntime.Restore(schema20Checkpoint);
        Assert.Empty(restored20.ExportState().Knowledge!.Facts);
        Assert.Empty(restored20.ExportState().Knowledge!.Artifacts);
        Assert.Equal(PrivateWorldRuntime.StateSchemaVersion, restored20.ExportState().SchemaVersion);

        var schema21Checkpoint = seed.ExportState() with { SchemaVersion = 21, Knowledge = null };
        using var restored21 = PrivateWorldRuntime.Restore(schema21Checkpoint);
        Assert.Empty(restored21.ExportState().Knowledge!.Facts);
        Assert.Empty(restored21.ExportState().Knowledge!.Artifacts);
        var restored21State = restored21.ExportState();
        Assert.NotNull(schema21Checkpoint.Towns);
        Assert.NotNull(restored21State.Towns);
        Assert.Equal(schema21Checkpoint.Towns!.Select(town => town.Id),
            restored21State.Towns!.Select(town => town.Id));
        Assert.Equal(PrivateWorldRuntime.StateSchemaVersion, restored21State.SchemaVersion);

        var schema22Checkpoint = seed.ExportState() with { SchemaVersion = 22, Knowledge = null };
        var schema22Society = schema22Checkpoint.Society.Society;
        var ownerId = schema22Society.Inhabitants[0].Id;
        var memory = new SocietySocialMemory(
            "migration-memory", ownerId, "founder-mira", "A remembered trail beside the ridge.", "private", 0);
        var compaction = new SocietyAgentMemoryCompaction(ownerId,
        [
            new SocietyAgentMemoryImportance(
                memory.Id, SocietyMemorySourceKind.Experience, memory.SourceTick, 7_500, 9_000, 0),
        ]);
        schema22Checkpoint = schema22Checkpoint with
        {
            Society = schema22Checkpoint.Society with
            {
                Society = schema22Society with
                {
                    Memories = [memory],
                    MemoryCompactions = [compaction],
                },
            },
        };
        using var restored22 = PrivateWorldRuntime.Restore(schema22Checkpoint);
        var migrated22 = restored22.ExportState();
        Assert.Empty(migrated22.Knowledge!.Facts);
        Assert.Empty(migrated22.Knowledge.Artifacts);
        Assert.NotNull(schema22Checkpoint.Towns);
        Assert.NotNull(migrated22.Towns);
        Assert.Equal(schema22Checkpoint.Towns!.Select(town => town.Id),
            migrated22.Towns!.Select(town => town.Id));
        Assert.Equal(PrivateWorldRuntime.StateSchemaVersion, migrated22.SchemaVersion);
        Assert.Equal(memory, Assert.Single(migrated22.Society.Society.Memories));
        Assert.Equal(compaction.Sources,
            Assert.Single(migrated22.Society.Society.MemoryCompactions!).Sources);
    }

    [Fact]
    public void RestoreRejectsMoreKnowledgeArtifactsThanThePerAgentLimit()
    {
        using var seed = new PrivateWorldRuntime("knowledge-artifact-bound");
        var state = seed.ExportState();
        var creatorId = state.Inhabitants[0].InhabitantId;
        var position = state.Inhabitants[0].Position;
        var terrain = state.Map.Tiles.Single(tile => tile.Position == position).Terrain.ToString();
        var fact = new AgentKnowledgeFact(
            $"knowledge-fact:{creatorId}:{position.X}:{position.Y}", creatorId, creatorId,
            position, terrain, [], state.Society.Society.WorldTick, "firsthand");
        var artifacts = Enumerable.Range(1, 9) // The world allows eight artifacts per creator.
            .Select(index => new AgentKnowledgeArtifact(
                $"knowledge-artifact-{index:D6}", creatorId, $"bounded-lot-{index:D6}",
                "field_record", "Field record · 1 site", state.Society.Society.WorldTick, [fact]))
            .ToArray();
        var inventory = state.Society.Society.Inventory;
        foreach (var artifact in artifacts)
            inventory = InventoryFixture.AddLot(inventory, artifact.LotId, artifact.Kind, creatorId, 1,
                state.Society.Society.WorldTick);
        var malformed = state with
        {
            Knowledge = new PrivateWorldKnowledgeState([fact], artifacts),
            Society = state.Society with
            {
                Society = state.Society.Society with { Inventory = inventory },
            },
        };

        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(malformed));
    }

    private static PrivateWorldRuntimeState WithArtifact(
        PrivateWorldRuntimeState state,
        string creatorId,
        GridPoint position,
        string kind = "field_record")
    {
        var sequence = (state.Knowledge?.Artifacts.Count ?? 0) + 1;
        var terrain = state.Map.Tiles.Single(tile => tile.Position == position).Terrain.ToString();
        var fact = new AgentKnowledgeFact(
            $"knowledge-fact:{creatorId}:{position.X}:{position.Y}", creatorId, creatorId,
            position, terrain, ["private-secret-resource"], state.Society.Society.WorldTick, "firsthand");
        var artifactId = $"knowledge-artifact-{sequence:D6}";
        var lotId = $"knowledge-lot-{sequence:D6}";
        var artifact = new AgentKnowledgeArtifact(artifactId, creatorId, lotId, kind,
            kind == "field_map" ? "Field map · 1 site" : "Field record · 1 site",
            state.Society.Society.WorldTick, [fact]);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory,
            lotId, kind, creatorId, 1, state.Society.Society.WorldTick);
        return state with
        {
            Knowledge = new PrivateWorldKnowledgeState(
                (state.Knowledge?.Facts ?? []).Append(fact).ToArray(),
                (state.Knowledge?.Artifacts ?? []).Append(artifact).ToArray()),
            Society = state.Society with
            {
                Society = state.Society.Society with { Inventory = inventory },
            },
        };
    }

    private static (string Id, string OwnerId, string DiscovererId, GridPoint Position,
        string Terrain, string Resources, string Acquisition, string? SourceAgentId, string? SourceArtifactId, long LearnedTick) FactKey(
        AgentKnowledgeFact fact) =>
        (fact.Id, fact.OwnerId, fact.DiscovererId, fact.Position, fact.Terrain,
            string.Join(',', fact.ResourceKinds), fact.Acquisition, fact.SourceAgentId, fact.SourceArtifactId, fact.LearnedTick);

    private static (string Id, string CreatorId, string LotId, string Kind, long CreatedTick,
        string Sites) ArtifactKey(AgentKnowledgeArtifact artifact) =>
        (artifact.Id, artifact.CreatorId, artifact.LotId, artifact.Kind, artifact.CreatedTick,
            string.Join(';', artifact.Facts.Select(fact =>
                $"{fact.Position.X},{fact.Position.Y}:{fact.Terrain}:{string.Join(',', fact.ResourceKinds)}")));

    private sealed class CandidateProvider(string targetId, string prefix, bool onlyTarget = true) : IDecisionProvider
    {
        public ConcurrentDictionary<string, IReadOnlyList<CognitionKnowledgeFact>> KnownMapFactsByAgent { get; } = new(StringComparer.Ordinal);

        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;

        public ValueTask<CognitionDecisionResponse> DecideAsync(
            CognitionDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            KnownMapFactsByAgent[request.Observation.InhabitantId] = request.Observation.KnownMapFacts ?? [];
            var selected = onlyTarget && request.Observation.InhabitantId != targetId
                ? null
                : request.Observation.Candidates.FirstOrDefault(item => item.Id.StartsWith(prefix, StringComparison.Ordinal));
            selected ??= request.Observation.Candidates.SingleOrDefault(item => item.Id == "safe_idle")
                ?? request.Observation.Candidates[0];
            return new DeterministicDecisionProvider().DecideAsync(request with
            {
                Observation = request.Observation with { Candidates = [selected] },
            }, cancellationToken);
        }
    }
}

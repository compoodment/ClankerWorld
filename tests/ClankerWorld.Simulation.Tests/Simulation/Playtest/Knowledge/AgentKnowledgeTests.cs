using System.Collections.Concurrent;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class AgentKnowledgeTests
{
    [Theory]
    [InlineData(100, 1)]
    [InlineData(50, 2)]
    public async Task DescendantsKeepExploringAndSavingWithLongInheritedIdentities(int seedLength, int generations)
    {
        using var seed = new PrivateWorldRuntime(new string('s', seedLength));
        var state = await PrepareWritingHouseAsync(seed.ExportState(), "founder-scout");
        var parent = "founder-scout";
        for (var generation = 0; generation < generations; generation++)
        {
            var partner = generation == 0 ? "founder-mira" : "founder-rowan";
            var society = state.Society.Society;
            var relationshipId = $"descendant-partnership-{generation}";
            society = SocietyFixture.ProposeRelationship(society, new(relationshipId, 1,
                SocietyRelationshipType.Partnership, parent, partner, society.WorldTick)).Checkpoint;
            society = SocietyFixture.AcceptRelationship(society, relationshipId, 1, partner).Checkpoint;
            var birth = SocietyFixture.CommitBirth(society, new($"family:{parent}:{society.WorldTick}", 1,
                parent, partner, "household:camp-alpha", [parent, partner], [parent, partner],
                "food:camp-alpha", 2, society.WorldTick, ChildName: "Explorer"));
            parent = Assert.IsType<string>(birth.CreatedId);
            society = birth.Checkpoint;
            // Accelerate age only, retaining the actual birth identity and family records.
            var adultBirth = society.LifeTickAt(society.WorldTick) - 20 * society.Config.TicksPerLifecycleAge;
            society = society with
            {
                Inhabitants = society.Inhabitants.Select(person => person.Id == parent ? person with
                {
                    BirthTick = adultBirth,
                    BirthLifeTick = society.LifeClock is null ? null : adultBirth,
                    AgeBand = SocietyAgeBand.Adult,
                    LastLifecycleYearChecked = 20,
                } : person).ToArray(),
            };
            var position = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == "knowledge-test-house").Position;
            state = state with
            {
                Society = state.Society with { Society = society },
                Inhabitants = state.Inhabitants.Append(new PlaytestInhabitantState(parent, position,
                    9_500, 0, "curious", "explore")).ToArray(),
            };
        }
        Assert.True(parent.Length > 128);
        var provider = new CandidateProvider(parent, "explore");
        using var world = PrivateWorldRuntime.Restore(
            PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)), _ => provider);
        for (var tick = 0; tick < 80; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            world.Validate();
            _ = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        }
        var saved = world.ExportState();
        Assert.Contains(saved.Knowledge!.Facts, fact => fact.OwnerId == parent);
        Assert.All(saved.Knowledge.Facts.Where(fact => fact.OwnerId == parent),
            fact => Assert.Equal(parent, fact.DiscovererId));
        Assert.Contains(saved.Knowledge.Artifacts, artifact => artifact.CreatorId == parent);
        Assert.NotEmpty(provider.KnownMapFactsByAgent[parent]);
        var discovererReference = Assert.Single(provider.KnownMapFactsByAgent[parent]
            .Select(fact => fact.DiscovererId).Distinct());
        Assert.InRange(discovererReference.Length, 1, 128);
        using var reloaded = PrivateWorldRuntime.Restore(
            PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(saved)), _ => provider);
        for (var tick = 0; tick < 25; tick++)
        {
            Assert.True((await reloaded.AdvanceOneTickAsync()).Advanced);
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        }
        reloaded.Validate();
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()),
            PrivateWorldRuntimeCodec.Encode(reloaded.ExportState()));
        Assert.All(provider.KnownMapFactsByAgent[parent],
            fact => Assert.Equal(discovererReference, fact.DiscovererId));
        Assert.Equal(saved.Knowledge.Facts.Select(fact => fact.Id),
            reloaded.ExportState().Knowledge!.Facts.Select(fact => fact.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InheritedNaturalFieldRecordKeepsItsPhysicalIdentityAndPrivateProvenance(bool selectedHeir)
    {
        using var seed = new PrivateWorldRuntime("inherited-natural-record");
        var initial = seed.ExportState();
        var creator = initial.Inhabitants[0].InhabitantId;
        initial = await PrepareWritingHouseAsync(initial, creator);
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
        initial = await PrepareWritingHouseAsync(initial, explorerId);
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
        state = await WithArtifactAsync(state, sourceId, state.Inhabitants[0].Position);
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
        state = await WithArtifactAsync(state, sellerId, state.Inhabitants[0].Position, "field_map");
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
    public async Task RestoreRejectsMoreKnowledgeArtifactsThanThePerAgentLimit()
    {
        using var seed = new PrivateWorldRuntime("knowledge-artifact-bound");
        var state = seed.ExportState();
        var creatorId = state.Inhabitants[0].InhabitantId;
        var position = state.Inhabitants[0].Position;
        state = await WithArtifactAsync(state, creatorId, position);
        using var writing = PrivateWorldRuntime.Restore(state, _ => new CandidateProvider("nobody", "never"));
        for (var index = 1; index < 8; index++)
            Assert.True(writing.WriteKnowledgeArtifact(creatorId, "field_record", [position]).Applied);
        Assert.Equal(8, writing.Knowledge.Artifacts.Count);
        Assert.False(writing.WriteKnowledgeArtifact(creatorId, "field_record", [position]).Applied);
        state = writing.ExportState();
        var house = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == "knowledge-test-house");
        var inventory = state.Society.Society.Inventory;
        var paper = inventory.GetLot("knowledge-test-paper");
        const string ninthId = "knowledge-artifact-000009";
        const string purpose = "knowledge-writing:" + ninthId;
        var reservationId = purpose + ":quantity:0:lot:0";
        inventory = InventoryFixture.ConsumeReservation(InventoryFixture.Reserve(inventory, reservationId,
            house.HouseholdId!, paper.Id, 1, purpose, state.Society.Society.WorldTick), reservationId);
        inventory = InventoryFixture.AddLot(inventory, "knowledge-lot-000009", "field_record", creatorId, 1, state.Society.Society.WorldTick);
        var ninth = state.Knowledge!.Artifacts[0] with { Id = ninthId, LotId = "knowledge-lot-000009", InputReservationIds = [reservationId] };
        var malformed = state with
        {
            Knowledge = state.Knowledge with { Artifacts = state.Knowledge.Artifacts.Append(ninth).ToArray() },
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } }
        };
        // The ninth item has genuine House/paper proof; only the creator bound is invalid.
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(malformed));
    }

    private static async Task<PrivateWorldRuntimeState> WithArtifactAsync(
        PrivateWorldRuntimeState state, string creatorId, GridPoint position, string kind = "field_record")
    {
        state = await PrepareWritingHouseAsync(state, creatorId);
        var terrain = state.Map.Tiles.Single(tile => tile.Position == position).Terrain.ToString();
        var fact = new AgentKnowledgeFact($"knowledge-fact:{creatorId}:{position.X}:{position.Y}", creatorId, creatorId,
            position, terrain, ["private-secret-resource"], state.Society.Society.WorldTick, "firsthand");
        using var writing = PrivateWorldRuntime.Restore(state with { Knowledge = new PrivateWorldKnowledgeState([fact], []) },
            _ => new CandidateProvider("nobody", "never"));
        var result = writing.WriteKnowledgeArtifact(creatorId, kind, [position]);
        Assert.True(result.Applied, result.Failure);
        return writing.ExportState();
    }

    private static async Task<PrivateWorldRuntimeState> PrepareWritingHouseAsync(PrivateWorldRuntimeState state, string creatorId)
    {
        using var activating = PrivateWorldRuntime.Restore(state, _ => new CandidateProvider("nobody", "never"));
        Assert.True(activating.StageStarterContent());
        for (var tick = 0; tick < 8; tick++) Assert.True((await activating.AdvanceOneTickAsync()).Advanced);
        state = activating.ExportState();
        var actor = state.Inhabitants.Single(person => person.InhabitantId == creatorId);
        var household = state.Society.Society.GetInhabitant(creatorId).HouseholdId!;
        var occupied = state.Map.CampObjects.Select(item => item.Position).Concat(state.Map.Resources.Select(item => item.Position))
            .Concat(state.RoadTiles ?? []).Concat(state.WorldSimulation!.Buildings.SelectMany(building =>
                WorldContentSimulationRules.Footprint(state.WorldContent!.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId), building))).ToHashSet();
        var point = state.Map.Tiles.Where(tile => state.Map.IsBuildable(tile.Position) && !occupied.Contains(tile.Position))
            .OrderBy(tile => state.Map.FootDistance(actor.Position, tile.Position)).First().Position;
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "knowledge-test-wood", "wood", household, 8);
        using var placing = PrivateWorldRuntime.Restore(state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } },
            _ => new CandidateProvider("nobody", "never"));
        Assert.True(placing.PlaceBuilding("knowledge-test-house", HouseContent.House1x1().CanonicalId, point, household).Applied);
        state = placing.ExportState();
        inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "knowledge-test-paper", "paper", household, 16, storageBuildingId: "knowledge-test-house");
        return state with
        {
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == creatorId
                ? person with { Position = point, HungerBasisPoints = 10_000 } : person).ToArray()
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
                : prefix == "explore" ? request.Observation.Candidates.FirstOrDefault(item => item.Id.StartsWith("knowledge_write:", StringComparison.Ordinal))
                    ?? request.Observation.Candidates.FirstOrDefault(item => item.Id == "explore")
                : request.Observation.Candidates.FirstOrDefault(item => item.Id.StartsWith(prefix, StringComparison.Ordinal))
                    ?? (prefix.StartsWith("trade_propose:", StringComparison.Ordinal)
                        ? request.Observation.Candidates.FirstOrDefault(item => item.Id.StartsWith("trade_meet:", StringComparison.Ordinal)) : null);
            selected ??= request.Observation.Candidates.SingleOrDefault(item => item.Id == "safe_idle")
                ?? request.Observation.Candidates[0];
            return new DeterministicDecisionProvider().DecideAsync(request with
            {
                Observation = request.Observation with { Candidates = [selected] },
            }, cancellationToken);
        }
    }
}

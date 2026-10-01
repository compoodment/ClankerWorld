using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed class PhysicalKnowledgeTests
{
    private static readonly Lazy<Task<byte[]>> ExploredAndSupplied = new(CreateSuppliedWorldAsync);

    [Fact]
    public async Task PaperConsumesActualContainedWaterAndKeepsItsJugAcrossAnInterruptedJob()
    {
        using var world = await SuppliedWorldAsync();
        var state = world.ExportState();
        var author = state.Inhabitants[0].InhabitantId;
        var house = House(state, author);
        var paper = world.WorldContent.Recipes.Single(item => item.Tags.Contains("paper"));
        Assert.True(world.StartProduction(paper.CanonicalId, house.InstanceId, author).Applied);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())), _ => new Pick());
        for (var tick = 0; tick < 20; tick++) Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
        var inventory = restored.ExportState().Society.Society.Inventory;
        Assert.Equal(1, inventory.GetLot("author-water").Quantity);
        Assert.Equal((1, 8), (inventory.GetLot("author-jug").Quantity, inventory.GetLot("author-jug").ContainerCapacity));
        Assert.Equal(2, inventory.Lots.Where(lot => lot.ItemKind == "paper" && lot.StorageBuildingId == house.InstanceId).Sum(lot => lot.Quantity));
        Assert.DoesNotContain(inventory.Lots, lot => lot.Id == "author-fiber");
        Assert.All(Assert.Single(restored.WorldSimulation.ProductionJobs).InputReservationIds.Select(inventory.GetReservation),
            reservation => Assert.Equal(InventoryReservationState.Completed, reservation.State));
        restored.Validate();
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("foreign-house")]
    [InlineData("no-cover")]
    public async Task WritingRejectsUnlearnedSitesWrongPhysicalHouseAndMissingSuppliesWithoutSpending(string failure)
    {
        using var ready = await PaperWorldAsync();
        var state = ready.ExportState();
        var author = state.Inhabitants[0].InhabitantId;
        var reader = state.Inhabitants[2].InhabitantId;
        var positions = state.Knowledge!.Facts.Where(fact => fact.OwnerId == author).Select(fact => fact.Position).Take(9).ToArray();
        if (failure == "unknown") positions = [state.Map.Tiles.First(tile => state.Map.IsPassable(tile.Position) && !positions.Contains(tile.Position)).Position];
        else if (failure == "foreign-house") state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == author
            ? person with { Position = House(state, reader).Position } : person.InhabitantId == reader ? person with { Position = House(state, author).Position } : person).ToArray()
        };
        else
        {
            var inventory = state.Society.Society.Inventory;
            inventory = InventoryFixture.ConsumeReservation(InventoryFixture.Reserve(inventory, "consume-cover", House(state, author).HouseholdId!,
                "author-cloth", 1, "fixture-cover-used", state.Society.Society.WorldTick), "consume-cover");
            state = state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } };
        }
        using var world = PrivateWorldRuntime.Restore(state, _ => new Pick());
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False(world.WriteKnowledgeArtifact(author, "book", positions).Applied);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
    }

    [Fact]
    public async Task ARealBookAndItsCopyKeepPrivatePayloadAndTheOriginalDiscoverer()
    {
        using var writing = await PaperWorldAsync();
        var state = writing.ExportState();
        var author = state.Inhabitants[0].InhabitantId;
        var reader = state.Inhabitants[2].InhabitantId;
        var bystander = state.Inhabitants[3].InhabitantId;
        var facts = state.Knowledge!.Facts.Where(fact => fact.OwnerId == author).Take(9).ToArray();
        var result = writing.WriteKnowledgeArtifact(author, "book", facts.Select(fact => fact.Position).ToArray());
        Assert.True(result.Applied, result.Failure);
        var book = Assert.Single(writing.Knowledge.Artifacts);
        Assert.Equal(facts.Select(fact => (fact.Position, fact.Terrain, fact.DiscovererId)), book.Facts.Select(fact => (fact.Position, fact.Terrain, fact.DiscovererId)));
        state = writing.ExportState();
        var inventory = InventoryFixture.Transfer(state.Society.Society.Inventory, "real-book-purchase", author, reader, book.LotId, 1, "knowledge_traded");
        using var reading = PrivateWorldRuntime.Restore(state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } }, _ => new Pick());
        Assert.False(reading.CopyKnowledgeArtifact(reader, book.Id).Applied);
        Assert.True(reading.ReadKnowledgeArtifact(reader, book.Id).Applied);
        Assert.DoesNotContain(reading.Knowledge.Facts, fact => fact.OwnerId == bystander);
        Assert.All(reading.Knowledge.Facts.Where(fact => fact.OwnerId == reader), fact =>
        {
            Assert.Equal(author, fact.DiscovererId); Assert.Equal(book.Id, fact.SourceArtifactId); Assert.Equal("read", fact.Acquisition);
        });
        var copyResult = reading.CopyKnowledgeArtifact(reader, book.Id);
        Assert.True(copyResult.Applied, copyResult.Failure);
        var copy = reading.Knowledge.Artifacts.Single(item => item.Id == copyResult.ArtifactId);
        Assert.Equal(book.Id, copy.CopiedFromArtifactId);
        Assert.Equal(book.Facts.Select(Payload), copy.Facts.Select(Payload));
        Assert.All(copy.Facts, fact => Assert.Equal(reader, fact.OwnerId));
        Assert.Equal(reader, reading.ExportState().Society.Society.Inventory.GetLot(book.LotId).OwnerId);
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(reading.ExportState())), _ => new Pick());
        Assert.Equal(reading.Knowledge.Artifacts.Select(item => item.LotId), restored.Knowledge.Artifacts.Select(item => item.LotId));
        restored.Validate();
    }

    [Fact]
    public async Task NormalCopyingCollectsTheActualStoredSourceAndDeliversCoverStockToTheHouse()
    {
        using var writing = await PaperWorldAsync();
        var state = writing.ExportState();
        var author = state.Inhabitants[0].InhabitantId;
        var reader = state.Inhabitants[2].InhabitantId;
        var facts = state.Knowledge!.Facts.Where(fact => fact.OwnerId == author).Take(9).ToArray();
        Assert.True(writing.WriteKnowledgeArtifact(author, "book", facts.Select(fact => fact.Position).ToArray()).Applied);
        var book = Assert.Single(writing.Knowledge.Artifacts);
        state = writing.ExportState();
        var readerHouse = House(state, reader);
        var occupied = state.Map.Resources.Select(resource => resource.Position).Concat(state.Map.CampObjects.Select(item => item.Position))
            .Concat(state.RoadTiles ?? []).Concat(state.WorldSimulation!.Buildings.SelectMany(building =>
                WorldContentSimulationRules.Footprint(state.WorldContent!.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId), building))).ToHashSet();
        var sourcePosition = state.Map.Tiles.Where(tile => state.Map.IsBuildable(tile.Position) && !occupied.Contains(tile.Position))
            .OrderBy(tile => state.Map.FootDistance(readerHouse.Position, tile.Position)).First().Position;
        var materials = InventoryFixture.AddLot(state.Society.Society.Inventory, "reader-farmhouse-wood", "wood", readerHouse.HouseholdId!, 8);
        materials = InventoryFixture.AddLot(materials, "reader-farmhouse-stone", "stone", readerHouse.HouseholdId!, 2);
        using var placing = PrivateWorldRuntime.Restore(state with { Society = state.Society with { Society = state.Society.Society with { Inventory = materials } } }, _ => new Pick());
        var placement = placing.PlaceBuilding("reader-farmhouse", FarmContent.Farmhouse1x1().CanonicalId, sourcePosition, readerHouse.HouseholdId);
        Assert.True(placement.Applied, placement.Failure);
        state = placing.ExportState();
        var sourceSite = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == "reader-farmhouse");
        var inventory = InventoryFixture.Transfer(state.Society.Society.Inventory, "stored-book-purchase", author, readerHouse.HouseholdId!,
            book.LotId, 1, "knowledge_traded", sourceSite.InstanceId);
        inventory = inventory with { Lots = inventory.Lots.Select(lot => lot.Id == "reader-cloth" ? lot with { StorageBuildingId = sourceSite.InstanceId } : lot).ToArray() };
        state = state with
        {
            Society = state.Society with
            {
                Society = state.Society.Society with { Inventory = inventory },
                Cognition = state.Society.Cognition with { Runtimes = state.Society.Cognition.Runtimes.Select(runtime => runtime with { CurrentIntention = null }).ToArray() }
            },
            Inhabitants = state.Inhabitants.Select(person => person with { LastDecisionContext = null }).ToArray()
        };
        using var copying = PrivateWorldRuntime.Restore(state, id => id == reader ? new Pick("knowledge_read:", "knowledge_supply:", "haul_household_stock", "knowledge_copy:") : new Pick());
        for (var tick = 0; tick < 160 && !copying.Knowledge.Artifacts.Any(item => item.CreatorId == reader); tick++) Assert.True((await copying.AdvanceOneTickAsync()).Advanced);
        var copy = Assert.Single(copying.Knowledge.Artifacts, item => item.CreatorId == reader);
        Assert.Equal(book.Id, copy.CopiedFromArtifactId);
        Assert.Equal(book.Facts.Select(Payload), copy.Facts.Select(Payload));
        var completed = copying.ExportState();
        Assert.Contains(completed.Society.Society.Inventory.Events, item => item.Kind == "inventory_transferred" &&
            item.Detail.EndsWith(":knowledge_supplies_collected", StringComparison.Ordinal));
        Assert.Contains(completed.Events, item => item.Kind == "household_stock_delivered");
        Assert.Contains(completed.Events, item => item.Kind == "knowledge_source_collected");
        Assert.Equal(reader, completed.Society.Society.Inventory.GetLot(book.LotId).OwnerId);
        copying.Validate();
    }

    [Theory]
    [InlineData("proof")]
    [InlineData("author-never-knew")]
    [InlineData("old-free")]
    public async Task ForgedOrOlderFreeKnowledgeGoodsAreRefusedBeforeAdvancement(string defect)
    {
        using var writing = await PaperWorldAsync();
        var state = writing.ExportState();
        var author = state.Inhabitants[0].InhabitantId;
        Assert.True(writing.WriteKnowledgeArtifact(author, "field_map", state.Knowledge!.Facts.Where(fact => fact.OwnerId == author)
            .Select(fact => fact.Position).Take(9).ToArray()).Applied);
        state = writing.ExportState();
        var artifact = Assert.Single(state.Knowledge!.Artifacts);
        if (defect == "proof") state = state with
        {
            Society = state.Society with
            {
                Society = state.Society.Society with
                {
                    Inventory = state.Society.Society.Inventory with
                    {
                        Reservations = state.Society.Society.Inventory.Reservations
                .Where(item => !artifact.InputReservationIds!.Contains(item.Id)).ToArray()
                    }
                }
            }
        };
        else if (defect == "author-never-knew") state = state with { Knowledge = state.Knowledge with { Facts = [] } };
        else state = state with
        {
            SchemaVersion = 30,
            Knowledge = state.Knowledge with
            {
                Artifacts = [artifact with {
            WritingBuildingId = null, InputReservationIds = null
        }]
            }
        };
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(state));
    }

    private static (GridPoint Position, string Terrain, string DiscovererId, string Resources) Payload(AgentKnowledgeFact fact) =>
        (fact.Position, fact.Terrain, fact.DiscovererId, string.Join('|', fact.ResourceKinds));

    private static PlacedBuilding House(PrivateWorldRuntimeState state, string actor) => state.WorldSimulation!.Buildings.Single(building =>
        building.HouseholdId == state.Society.Society.GetInhabitant(actor).HouseholdId &&
        state.WorldContent!.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId).Tags.Contains("house"));

    private static async Task<PrivateWorldRuntime> SuppliedWorldAsync() => PrivateWorldRuntime.Restore(
        PrivateWorldRuntimeCodec.Decode(await ExploredAndSupplied.Value), _ => new Pick());

    private static async Task<PrivateWorldRuntime> PaperWorldAsync()
    {
        var world = await SuppliedWorldAsync();
        var state = world.ExportState();
        var recipe = world.WorldContent.Recipes.Single(item => item.Tags.Contains("paper"));
        foreach (var actor in new[] { state.Inhabitants[0].InhabitantId, state.Inhabitants[2].InhabitantId })
            Assert.True(world.StartProduction(recipe.CanonicalId, House(state, actor).InstanceId, actor).Applied);
        for (var tick = 0; tick < 20; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        return world;
    }

    private static async Task<byte[]> CreateSuppliedWorldAsync()
    {
        var firstFounder = $"founder:{1:D32}";
        using var seed = new PrivateWorldRuntime("knowledge-probe", id => id == firstFounder ? new Pick("explore") : new Pick(),
            startPace: WorldStartPace.FounderSetup, geographyOptions: new GeographyOptions("knowledge-probe", WorldSizePreset.Small));
        var map = seed.ExportState().Map;
        var anchor = map.Resources.Single(item => item.Id == "berry-patch").Position;
        seed.InitializeFirstTownContent(); seed.AcceptFirstTownLayout(anchor);
        var occupied = map.Resources.Select(item => item.Position).Concat(seed.RoadTiles).Concat(seed.WorldSimulation.Buildings.SelectMany(building =>
            WorldContentSimulationRules.Footprint(seed.WorldContent.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId), building))).ToHashSet();
        var founders = map.Tiles.Where(tile => Math.Abs(tile.Position.X - anchor.X) <= 5 && Math.Abs(tile.Position.Y - anchor.Y) <= 5 &&
            map.IsBuildable(tile.Position) && !occupied.Contains(tile.Position)).Take(4).Select(tile => tile.Position).ToArray();
        for (var index = 0; index < 4; index++) seed.PlaceFounder($"founder:{index + 1:D32}", founders[index]);
        seed.StartWorld();
        for (var tick = 0; tick < 5; tick++) Assert.True((await seed.AdvanceOneTickAsync()).Advanced);
        var state = seed.ExportState();
        var author = state.Inhabitants[0].InhabitantId;
        var reader = state.Inhabitants[2].InhabitantId;
        Assert.Contains(state.Knowledge!.Facts, fact => fact.OwnerId == author);
        Assert.DoesNotContain(state.Knowledge.Facts, fact => fact.OwnerId != author);
        var inventory = state.Society.Society.Inventory;
        foreach (var (actor, prefix) in new[] { (author, "author"), (reader, "reader") })
        {
            var house = House(state, actor);
            inventory = InventoryFixture.AddLot(inventory, prefix + "-fiber", "fiber", house.HouseholdId!, 2, storageBuildingId: house.InstanceId);
            inventory = InventoryFixture.AddLot(inventory, prefix + "-jug", "water_jug", house.HouseholdId!, 1, storageBuildingId: house.InstanceId, containerCapacity: 8);
            inventory = InventoryFixture.AddLot(inventory, prefix + "-water", "water", house.HouseholdId!, 2, storageBuildingId: house.InstanceId, containerLotId: prefix + "-jug");
            inventory = InventoryFixture.AddLot(inventory, prefix + "-cloth", "cloth", house.HouseholdId!, 1, storageBuildingId: house.InstanceId);
        }
        state = state with
        {
            Society = state.Society with
            {
                Society = state.Society.Society with { Inventory = inventory },
                Cognition = state.Society.Cognition with { Runtimes = state.Society.Cognition.Runtimes.Select(runtime => runtime with { CurrentIntention = null }).ToArray() }
            },
            Inhabitants = state.Inhabitants.Select(person => person with
            {
                Exploration = null,
                LastDecisionContext = null,
                HungerBasisPoints = 10_000,
                Position = person.InhabitantId == author || person.InhabitantId == reader ? House(state, person.InhabitantId).Position : person.Position
            }).ToArray()
        };
        return PrivateWorldRuntimeCodec.Encode(state);
    }

    private sealed class Pick(params string[] prefixes) : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var selected = prefixes.Select(prefix => request.Observation.Candidates.FirstOrDefault(item => item.Id.StartsWith(prefix, StringComparison.Ordinal)))
                .FirstOrDefault(item => item is not null) ?? request.Observation.Candidates.Single(item => item.Id == "safe_idle");
            return new DeterministicDecisionProvider().DecideAsync(request with { Observation = request.Observation with { Candidates = [selected] } }, cancellationToken);
        }
    }
}

using System.Text.Json;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class PhysicalKnowledgePipelineTests
{
    [Theory]
    [InlineData("field_record", "Read a field record", 1, 0, true)]
    [InlineData("field_map", "Read a map", 1, 0, false)]
    [InlineData("book", "Read a book", 2, 1, true)]
    public async Task ReadOrderLearnsOnlyAnActualWrittenArtifactsFactsAndReplays(string kind, string command, int paper, int cloth, bool exact)
    {
        var (state, artifact, reader) = await WrittenReadOrderState(kind, paper, cloth);
        Func<string, IDecisionProvider> factory = _ => new ChoosingProvider(reader);
        using var world = PrivateWorldRuntime.Restore(state, factory);
        var receipt = world.SubmitInstruction(new("read-real-artifact", "owner:test", reader,
            OwnerInstructionKind.MustDo, command + (exact ? " " + artifact.Id : string.Empty)));
        var order = Assert.Single(world.ExportState().Instructions!).Order!;
        Assert.Equal("read_knowledge", order.Action);
        Assert.Equal(0, order.CompletedUnits);
        var initial = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(initial), factory);
        for (var tick = 0; tick < 8 && !world.ExportState().CompletedInstructionIds!.Contains(receipt.InstructionId); tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        }
        var final = world.ExportState();
        Assert.Equal(("finished", 1), (Assert.Single(final.Instructions!).Order!.Status, Assert.Single(final.Instructions!).Order!.CompletedUnits));
        var learned = final.Knowledge!.Facts.Where(fact => fact.OwnerId == reader).ToArray();
        Assert.Equal(artifact.Facts.Count, learned.Length);
        Assert.All(learned, fact =>
        {
            var written = Assert.Single(artifact.Facts, item => item.Position == fact.Position);
            Assert.Equal((written.DiscovererId, written.Terrain), (fact.DiscovererId, fact.Terrain));
            Assert.Equal(written.ResourceKinds, fact.ResourceKinds);
            Assert.Equal(("read", artifact.CreatorId, artifact.Id), (fact.Acquisition, fact.SourceAgentId, fact.SourceArtifactId));
        });
        Assert.Equal(JsonSerializer.Serialize(state.Knowledge!.Facts.Where(fact => fact.OwnerId != reader)),
            JsonSerializer.Serialize(final.Knowledge.Facts.Where(fact => fact.OwnerId != reader)));
        Assert.Equal(JsonSerializer.Serialize(state.Knowledge.Artifacts), JsonSerializer.Serialize(final.Knowledge.Artifacts));
        var held = final.Society.Society.Inventory.GetLot(artifact.LotId);
        // Inventory time may advance; reading changes none of the physical lot.
        Assert.Equal(state.Society.Society.Inventory.GetLot(artifact.LotId) with { LastProcessedTick = held.LastProcessedTick }, held);
        Assert.Equal(artifact.Id, Assert.Single(new OwnerWorldObservationStore(world).GetSnapshot().Instructions).Order!.TargetKnowledgeArtifactId);
        Assert.Single(final.Events, item => item.Kind == "agent_knowledge_artifact_read" && item.Detail.Contains(artifact.Id, StringComparison.Ordinal));
        using var reloaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(final)), factory);
        Assert.True((await reloaded.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(1, Assert.Single(reloaded.ExportState().Instructions!).Order!.CompletedUnits);
        Assert.Single(reloaded.ExportState().Events, item => item.Kind == "agent_knowledge_artifact_read" && item.Detail.Contains(artifact.Id, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("foreign")]
    [InlineData("reserved")]
    [InlineData("ground")]
    [InlineData("missing")]
    public async Task ReadOrderWaitsForItsExactItemWithoutPrivateOrRemoteLearning(string obstacle)
    {
        var (state, artifact, reader) = await WrittenReadOrderState();
        using var setup = PrivateWorldRuntime.Restore(state, _ => new ChoosingProvider(reader));
        setup.SubmitInstruction(new("blocked-read", "owner:test", reader, OwnerInstructionKind.MustDo, "Read " + artifact.Id));
        state = setup.ExportState();
        var inventory = state.Society.Society.Inventory;
        if (obstacle == "foreign") inventory = InventoryFixture.Transfer(inventory, "withhold-read-item", reader, artifact.CreatorId, artifact.LotId, 1, "withhold it");
        else if (obstacle == "reserved") inventory = InventoryFixture.Reserve(inventory, "read-item-held", reader, artifact.LotId, 1, "other-work", inventory.WorldTick + 200);
        else if (obstacle == "ground") inventory = inventory with
        {
            Lots = inventory.Lots.Select(lot => lot.Id == artifact.LotId ? lot with
            {
                GroundPosition = new(state.Inhabitants.Single(person => person.InhabitantId == reader).Position.X,
                    state.Inhabitants.Single(person => person.InhabitantId == reader).Position.Y)
            } : lot).ToArray()
        };
        var blocked = WithInventory(state, inventory);
        if (obstacle == "missing") blocked = blocked with { Knowledge = blocked.Knowledge! with { Artifacts = [], EarlierFacts = [] } };
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(blocked)), _ => new ChoosingProvider(reader));
        for (var tick = 0; tick < 4; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var order = Assert.Single(world.ExportState().Instructions!).Order!;
        Assert.Equal("blocked", order.Status);
        Assert.Contains(obstacle == "missing" ? "no longer available" : "personally owned, carried and available", order.BlockedReason);
        Assert.Equal(artifact.Id, order.TargetKnowledgeArtifactId);
        Assert.Equal(0, order.CompletedUnits);
        Assert.DoesNotContain(world.Knowledge.Facts, fact => fact.OwnerId == reader);
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "agent_knowledge_artifact_read");
    }

    [Fact]
    public async Task ReadOrderDoesNotCreditAnotherReadersLearningAndWaitsForOwnershipToReturn()
    {
        var (state, artifact, reader) = await WrittenReadOrderState();
        var other = state.Inhabitants.First(person => person.InhabitantId != reader && person.InhabitantId != artifact.CreatorId).InhabitantId;
        using var setup = PrivateWorldRuntime.Restore(state, _ => new ChoosingProvider(reader));
        setup.SubmitInstruction(new("wait-for-my-item", "owner:test", reader, OwnerInstructionKind.MustDo, "Read " + artifact.Id));
        state = setup.ExportState();
        var loan = InventoryFixture.Transfer(state.Society.Society.Inventory, "give-to-other-reader", reader, other, artifact.LotId, 1, "give to another reader");
        using var world = PrivateWorldRuntime.Restore(WithInventory(state, loan), _ => new ChoosingProvider(other, "knowledge_read:" + artifact.Id));
        await AdvanceUntil(world, () => world.Knowledge.Facts.Any(fact => fact.OwnerId == other), 8);
        Assert.Equal(0, Assert.Single(world.ExportState().Instructions!).Order!.CompletedUnits);
        Assert.DoesNotContain(world.Knowledge.Facts, fact => fact.OwnerId == reader);
        var afterOtherRead = world.ExportState();
        var returned = InventoryFixture.Transfer(afterOtherRead.Society.Society.Inventory, "give-back-for-reading", other, reader, artifact.LotId, 1, "return the item");
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(WithInventory(afterOtherRead, returned))), _ => new ChoosingProvider(reader));
        await AdvanceUntil(restored, () => Assert.Single(restored.ExportState().Instructions!).Order!.Status == "finished", 8);
        Assert.Equal(artifact.Id, Assert.Single(restored.ExportState().Instructions!).Order!.TargetKnowledgeArtifactId);
        Assert.Equal(1, Assert.Single(restored.ExportState().Instructions!).Order!.CompletedUnits);
        Assert.Equal(JsonSerializer.Serialize(afterOtherRead.Knowledge!.Facts.Where(fact => fact.OwnerId == other)),
            JsonSerializer.Serialize(restored.Knowledge.Facts.Where(fact => fact.OwnerId == other)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellingOrReplacingAReadOrderKeepsTheBookUnread(bool replace)
    {
        var (state, artifact, reader) = await WrittenReadOrderState("book", 2, 1);
        using var world = PrivateWorldRuntime.Restore(state, _ => new ChoosingProvider(reader));
        var read = world.SubmitInstruction(new("cancel-reading", "owner:test", reader, OwnerInstructionKind.MustDo, "Read a book"));
        if (replace) world.SubmitInstruction(new("instead-of-reading", "owner:test", reader, OwnerInstructionKind.MustDo, "Move to (1000,1000)"));
        else world.CancelOrder(new("cancel-read-receipt", "owner:test", world.Society.WorldId, reader, read.InstructionId));
        for (var tick = 0; tick < 3; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var order = world.ExportState().Instructions!.Single(item => item.InstructionId == read.InstructionId).Order!;
        Assert.Equal(("cancelled", 0), (order.Status, order.CompletedUnits));
        Assert.DoesNotContain(world.Knowledge.Facts, fact => fact.OwnerId == reader);
        Assert.Equal(reader, world.Society.Inventory.GetLot(artifact.LotId).OwnerId);
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())), _ => new ChoosingProvider(reader));
        Assert.DoesNotContain(restored.Knowledge.Facts, fact => fact.OwnerId == reader);
    }

    [Fact]
    public async Task QueuedReadingSurvivesATypoUrgentFoodAndSavingWithoutFreeKnowledge()
    {
        var (state, artifact, reader) = await WrittenReadOrderState();
        state = WithInventory(state, InventoryFixture.AddLot(state.Society.Society.Inventory, "reading-food", "berries", reader, 3, state.Society.Society.WorldTick));
        using var world = PrivateWorldRuntime.Restore(state, _ => new ChoosingProvider(reader));
        var hold = world.SubmitInstruction(new("hold-reading", "owner:test", reader, OwnerInstructionKind.MustDo, "Move to (1000,1000)"));
        var read = world.SubmitInstruction(new("queued-reading", "owner:test", reader, OwnerInstructionKind.MustDo, "Read a map", Queue: true));
        world.SubmitInstruction(new("reading-typo", "owner:test", reader, OwnerInstructionKind.MustDo, "Read a mapp"));
        var queued = world.ExportState();
        Assert.Equal("queued", queued.Instructions!.Single(item => item.InstructionId == read.InstructionId).Order!.Status);
        queued = queued with
        {
            Inhabitants = queued.Inhabitants.Select(person => person.InhabitantId == reader
            ? person with { HungerBasisPoints = 0 } : person).ToArray()
        };
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(queued)), _ => new ChoosingProvider(reader));
        restored.CancelOrder(new("release-reading", "owner:test", restored.Society.WorldId, reader, hold.InstructionId));
        Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
        var interrupted = restored.ExportState();
        Assert.Equal("interrupted", interrupted.Instructions!.Single(item => item.InstructionId == read.InstructionId).Order!.Status);
        Assert.DoesNotContain(interrupted.Knowledge!.Facts, fact => fact.OwnerId == reader);
        Assert.True(interrupted.Society.Society.Inventory.GetLot("reading-food").Quantity < 3);
        using var eatingReload = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(interrupted)), _ => new ChoosingProvider(reader));
        await AdvanceUntil(eatingReload, () => eatingReload.ExportState().Instructions!.Single(item => item.InstructionId == read.InstructionId).Order!.Status == "finished", 12);
        Assert.Equal(artifact.Id, eatingReload.ExportState().Instructions!.Single(item => item.InstructionId == read.InstructionId).Order!.TargetKnowledgeArtifactId);
        Assert.Single(eatingReload.ExportState().Events, item => item.Kind == "agent_knowledge_artifact_read");
    }

    [Fact]
    public async Task ReadCompletionRejectsUnrelatedFactsChangedTargetsAndForgedProgressAndRollsBackAtomically()
    {
        var (state, artifact, reader) = await WrittenReadOrderState();
        using var world = PrivateWorldRuntime.Restore(state, _ => new ChoosingProvider(reader));
        world.SubmitInstruction(new("atomic-reading", "owner:test", reader, OwnerInstructionKind.MustDo, "Read " + artifact.Id));
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False((await world.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var completed = world.ExportState();
        var instruction = Assert.Single(completed.Instructions!);
        Assert.Equal(1, instruction.Order!.CompletedUnits);
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(completed with
        { Instructions = [instruction with { Order = instruction.Order with { TargetKnowledgeArtifactId = "another-artifact" } }] }));
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(completed with
        { Instructions = [instruction with { Order = instruction.Order with { LastEffectId = "read-order:forged" } }] }));
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(completed with
        { Knowledge = completed.Knowledge! with { Facts = completed.Knowledge!.Facts.Where(fact => fact.OwnerId != reader).ToArray() } }));
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(completed with
        { Instructions = [instruction with { Order = instruction.Order with { KnowledgeReadCompletion = instruction.Order.KnowledgeReadCompletion! with { LearnedSites = [] } } }] }));
        Assert.Single(completed.Events, item => item.Kind == "agent_knowledge_artifact_read");
    }

    [Fact]
    public async Task ReadOrderHonorsKnowledgeCapacityWithoutInventingOrReplacingFacts()
    {
        // The retired camp has only thirty tiles. Use a normal generated world
        // for the boundary, with a real explored and written source.
        using var generated = NormalPathWorld.CreateGenerated("read-order-knowledge-cap", _ => new ActionCoverageRecorder(chooseIdle: true));
        var initial = generated.ExportState();
        var author = initial.Inhabitants[0].InhabitantId;
        var reader = initial.Inhabitants[1].InhabitantId;
        initial = initial with
        {
            Inhabitants = initial.Inhabitants.Select(person => person with
            { HungerBasisPoints = 9_500 }).ToArray()
        };
        var scout = new ChoosingProvider(author, "explore");
        using var exploring = PrivateWorldRuntime.Restore(initial, _ => scout);
        await AdvanceUntil(exploring, () => exploring.Knowledge.Facts.Count(fact => fact.OwnerId == author) >= 2, 20);
        // This boundary needs two actual discoveries, not a long outing. Ask for
        // the native return before writing so current scouting does not consume
        // the writing budget or add unrelated facts to the capacity fixture.
        scout.Prefixes = ["explore_return"];
        exploring.SubmitInstruction(new("return-before-capacity-map", "owner:test", author,
            OwnerInstructionKind.Suggestive, "Return from scouting before writing the map."));
        await AdvanceUntil(exploring, () => exploring.ExportState().Inhabitants.Single(person =>
            person.InhabitantId == author).Exploration is { OutingPath.Count: 0 }, 12);
        Assert.Contains(exploring.ExportState().Events, item => item.Kind == "exploration_completed");
        var explored = exploring.ExportState();
        using var writing = PrivateWorldRuntime.Restore(WithInventory(explored, AddWritingSupplies(explored, author, 1, 0)), _ =>
            new ChoosingProvider(author, "knowledge_write:field_map", "knowledge_continue"));
        await AdvanceUntil(writing, () => writing.Knowledge.Artifacts.Count == 1, 32);
        var written = writing.ExportState();
        var artifact = Assert.Single(written.Knowledge!.Artifacts);
        var state = WithInventory(written, InventoryFixture.Transfer(written.Society.Society.Inventory,
            "give-capacity-map", author, reader, artifact.LotId, 1, "give the actual map"));
        foreach (var full in new[] { false, true })
        {
            // A controlled existing ledger isolates its saved capacity boundary.
            var existing = state.Map.Tiles.Where(tile => state.Map.IsPassable(tile.Position) && artifact.Facts.All(fact => fact.Position != tile.Position))
                .Take(full ? 128 : 127).Select(tile => new AgentKnowledgeFact(
                    $"knowledge-fact:{reader}:{tile.Position.X}:{tile.Position.Y}", reader, reader, tile.Position,
                    tile.Terrain.ToString(), [], state.Society.Society.WorldTick, "firsthand")).ToArray();
            Assert.Equal(full ? 128 : 127, existing.Length);
            using var world = PrivateWorldRuntime.Restore(state with
            { Knowledge = state.Knowledge! with { Facts = state.Knowledge!.Facts.Concat(existing).ToArray() } }, _ => new ChoosingProvider(reader));
            world.SubmitInstruction(new("capacity-read", "owner:test", reader, OwnerInstructionKind.MustDo, "Read " + artifact.Id));
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            var order = Assert.Single(world.ExportState().Instructions!).Order!;
            Assert.Equal(128, world.Knowledge.Facts.Count(fact => fact.OwnerId == reader));
            Assert.Equal(JsonSerializer.Serialize(existing), JsonSerializer.Serialize(world.Knowledge.Facts.Where(fact => fact.OwnerId == reader && fact.Acquisition == "firsthand")));
            if (full)
            {
                Assert.Equal(("blocked", 0), (order.Status, order.CompletedUnits));
                Assert.Contains("ledger is full", order.BlockedReason);
                Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "agent_knowledge_artifact_read");
            }
            else
            {
                Assert.Equal(("finished", 1), (order.Status, order.CompletedUnits));
                Assert.Single(order.KnowledgeReadCompletion!.LearnedSites);
                var learned = Assert.Single(world.Knowledge.Facts, fact => fact.OwnerId == reader && fact.Acquisition == "read");
                Assert.Contains(artifact.Facts, fact => fact.Position == learned.Position);
                Assert.Equal(artifact.Id, learned.SourceArtifactId);
            }
        }
    }

    [Fact]
    public async Task ReadReceiptRequiresItsOwnReadButSurvivesALaterFirsthandAccount()
    {
        var (state, artifact, reader) = await WrittenReadOrderState();
        using var world = PrivateWorldRuntime.Restore(state, _ => new ChoosingProvider(reader));
        world.SubmitInstruction(new("historical-read", "owner:test", reader, OwnerInstructionKind.MustDo, "Read " + artifact.Id));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var read = world.ExportState();
        var firsthand = read.Knowledge!.Facts.Select(fact => fact.OwnerId == reader ? fact with
        { DiscovererId = reader, Acquisition = "firsthand", SourceAgentId = null, SourceArtifactId = null } : fact).ToArray();
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(read with { Knowledge = read.Knowledge with { Facts = firsthand } }));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var later = world.ExportState();
        // A later account of the same sites must not erase the completed task.
        firsthand = firsthand.Select(fact => fact.OwnerId == reader ? fact with { LearnedTick = later.Society.Society.WorldTick } : fact).ToArray();
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(
            later with { Knowledge = later.Knowledge! with { Facts = firsthand } })), _ => new ChoosingProvider(reader));
        Assert.Equal(JsonSerializer.Serialize(Assert.Single(read.Instructions!).Order), JsonSerializer.Serialize(Assert.Single(restored.ExportState().Instructions!).Order));
    }

    [Fact]
    public async Task NamedReadingDoesNotSwitchToAnEligibleBookWhileTheSelectedMapIsElsewhere()
    {
        var (state, map, reader) = await WrittenReadOrderState();
        var materials = InventoryFixture.AddLot(state.Society.Society.Inventory, "other-book-paper", "paper", map.CreatorId, 2, state.Society.Society.WorldTick);
        materials = InventoryFixture.AddLot(materials, "other-book-cover", "cloth", map.CreatorId, 1, state.Society.Society.WorldTick);
        using var writing = PrivateWorldRuntime.Restore(WithInventory(state, materials), _ =>
            new ChoosingProvider(map.CreatorId, "knowledge_write:book", "knowledge_continue"));
        await AdvanceUntil(writing, () => writing.Knowledge.Artifacts.Count == 2, 32);
        var written = writing.ExportState();
        var book = Assert.Single(written.Knowledge!.Artifacts, item => item.Kind == "book");
        var inventory = InventoryFixture.Transfer(written.Society.Society.Inventory, "hold-other-book", book.CreatorId, reader, book.LotId, 1, "give the alternative book");
        inventory = InventoryFixture.Transfer(inventory, "withhold-named-map", reader, map.CreatorId, map.LotId, 1, "withhold the selected map");
        using var world = PrivateWorldRuntime.Restore(WithInventory(written, inventory), _ => new ChoosingProvider(reader));
        world.SubmitInstruction(new("exact-map-title", "owner:test", reader, OwnerInstructionKind.MustDo, "Read " + map.Title));
        for (var tick = 0; tick < 3; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(map.Id, Assert.Single(world.ExportState().Instructions!).Order!.TargetKnowledgeArtifactId);
        Assert.Equal(("blocked", 0), (Assert.Single(world.ExportState().Instructions!).Order!.Status, Assert.Single(world.ExportState().Instructions!).Order!.CompletedUnits));
        Assert.DoesNotContain(world.Knowledge.Facts, fact => fact.OwnerId == reader);
        var waiting = world.ExportState();
        inventory = InventoryFixture.Transfer(waiting.Society.Society.Inventory, "return-selected-map", map.CreatorId, reader, map.LotId, 1, "return the selected map");
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(WithInventory(waiting, inventory))), _ => new ChoosingProvider(reader));
        await AdvanceUntil(restored, () => Assert.Single(restored.ExportState().Instructions!).Order!.Status == "finished", 8);
        Assert.Equal(map.Id, Assert.Single(restored.ExportState().Instructions!).Order!.TargetKnowledgeArtifactId);
        Assert.All(restored.Knowledge.Facts.Where(fact => fact.OwnerId == reader), fact => Assert.Equal(map.Id, fact.SourceArtifactId));
    }

    private static async Task<(PrivateWorldRuntimeState State, AgentKnowledgeArtifact Artifact, string Reader)> WrittenReadOrderState(
        string kind = "field_map", int paper = 1, int cloth = 0)
    {
        var (state, author) = await LearnByExploring("read-order-physical-" + kind);
        using var writing = PrivateWorldRuntime.Restore(WithInventory(state, AddWritingSupplies(state, author, paper, cloth)), _ =>
            new ChoosingProvider(author, "knowledge_write:" + kind, "knowledge_continue"));
        await AdvanceUntil(writing, () => writing.Knowledge.Artifacts.Count == 1, 64);
        var written = writing.ExportState();
        var artifact = Assert.Single(written.Knowledge!.Artifacts);
        var reader = written.Inhabitants.First(person => person.InhabitantId != author).InhabitantId;
        var transferred = InventoryFixture.Transfer(written.Society.Society.Inventory, "give-read-order-artifact",
            author, reader, artifact.LotId, 1, "give the actual written artifact");
        return (WithInventory(written, transferred), artifact, reader);
    }
}

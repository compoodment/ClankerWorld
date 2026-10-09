using System.Text.Json;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class KnowledgeCopyOrderTests
{
    private static readonly Lazy<byte[]> Baseline = new(() => PrivateWorldRuntimeCodec.Encode(
        GeographyGeneratorTests.StartedGeneratedWorld(new GeographyOptions("knowledge-copy-orders", WorldSizePreset.Small))));
    private static readonly Lazy<Task<byte[]>> LearnedBaseline = new(async () =>
        PrivateWorldRuntimeCodec.Encode(await PrepareCore(learn: true)));
    private static readonly Dictionary<string, Lazy<Task<byte[]>>> Sources = new()
    {
        ["field_record"] = new(() => MakeSource("field_record")),
        ["field_map"] = new(() => MakeSource("field_map")),
        ["book"] = new(() => MakeSource("book")),
    };

    [Theory]
    [InlineData("copy a record", "field_record", 1, 0)]
    [InlineData("copy a map", "field_map", 1, 0)]
    [InlineData("copy a book", "book", 2, 1)]
    public async Task CopyOrdersPreserveThePaidSourceAndCreateExactlyOnePaidPhysicalCopy(
        string text, string kind, int paper, int cloth)
    {
        var state = await CopyReady(kind);
        var actor = CopyActor(state);
        var source = Assert.Single(state.Knowledge!.Artifacts);
        using var world = Restore(state);
        var receipt = Submit(world, "ordered-copy", text);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var working = world.ExportState();
        var job = Assert.Single(working.Knowledge!.WritingProjects);
        Assert.Equal((actor, kind, source.Id, receipt.InstructionId, job.Id),
            (job.ActorId, job.Kind, job.SourceArtifactId, job.OrderInstructionId, Order(world, receipt).KnowledgeWritingProjectId));
        Assert.Equal(source.Id, Order(world, receipt).KnowledgeCopySourceArtifactId);
        Assert.Equal(0, Order(world, receipt).CompletedUnits);
        Assert.Single(working.Knowledge.Artifacts);
        Assert.Equal((3, 2), (Quantity(working, actor, "paper"), Quantity(working, actor, "cloth")));
        Assert.All(job.Materials, input => Assert.Equal(InventoryReservationState.Reserved,
            world.Society.Inventory.GetReservation(input.ReservationId).State));
        await FinishTogether(world, receipt);
        var final = world.ExportState();
        var copy = Assert.Single(final.Knowledge!.Artifacts, item => item.CreatorId == actor);
        Assert.Equal(JsonSerializer.Serialize(source), JsonSerializer.Serialize(final.Knowledge.Artifacts[0]));
        Assert.Equal((source.Id, receipt.InstructionId, job.Id, source.Facts.Count),
            (copy.SourceArtifactId, copy.OrderInstructionId, copy.WritingProjectId, copy.Facts.Count));
        Assert.Equal(source.Facts.Select(Account), copy.Facts.Select(Account));
        Assert.All(copy.Facts, fact => Assert.Equal((source.CreatorId, source.Id, "read"),
            (fact.DiscovererId, fact.SourceArtifactId, fact.Acquisition)));
        var lot = world.Society.Inventory.GetLot(copy.LotId);
        Assert.Equal((kind, actor, 1), (lot.ItemKind, lot.OwnerId, lot.Quantity));
        Assert.Equal((3 - paper, 2 - cloth), (Quantity(final, actor, "paper"), Quantity(final, actor, "cloth")));
        Assert.All(job.Materials, input => Assert.Equal(InventoryReservationState.Completed,
            world.Society.Inventory.GetReservation(input.ReservationId).State));
        Assert.Equal(("finished", 1, "copies", (string?)null, (string?)null),
            (Order(world, receipt).Status, Order(world, receipt).CompletedUnits, Order(world, receipt).ProgressUnit,
                Order(world, receipt).KnowledgeWritingProjectId, Order(world, receipt).KnowledgeCopySourceArtifactId));
        var projected = Assert.Single(new OwnerWorldObservationStore(world).GetSnapshot().Instructions,
            item => item.InstructionId == receipt.InstructionId).Order!;
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var client = JsonSerializer.Deserialize<ClankerWorld.GodotClient.UI.OwnerWorldInstructionOrder>(JsonSerializer.Serialize(projected, json), json)!;
        Assert.Equal((kind, "copies", 1), (client.TargetKnowledgeKind, client.ProgressUnit, client.CompletedUnits));
        AssertReload(final);
    }

    [Theory]
    [InlineData("foreign")]
    [InlineData("borrowed")]
    [InlineData("full")]
    [InlineData("reserved")]
    [InlineData("unknown-sites")]
    [InlineData("missing-paper")]
    [InlineData("missing-cloth")]
    public async Task CopyingDoesNotAcquireSourceKnowledgeOrUnavailableProperty(string boundary)
    {
        var state = await CopyReady("book");
        var actor = CopyActor(state);
        var other = state.Inhabitants.First(person => person.InhabitantId != actor).InhabitantId;
        var source = Assert.Single(state.Knowledge!.Artifacts);
        var inventory = state.Society.Society.Inventory;
        if (boundary is "foreign" or "borrowed") inventory = InventoryFixture.Transfer(inventory, "foreign-source", actor, other, source.LotId, 1, "return source");
        if (boundary == "borrowed") inventory = InventoryFixture.Relocate(inventory, "borrowed-source", source.LotId,
            other, 1, carrierId: actor);
        if (boundary == "full")
        {
            var household = state.Society.Society.GetInhabitant(actor).HouseholdId!;
            var house = state.WorldSimulation!.Buildings.First(building => building.HouseholdId == household &&
                state.WorldContent!.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId).Tags.Contains("house"));
            inventory = inventory with { Lots = inventory.Lots.Where(lot => lot.Id != "copy-paper").ToArray() };
            inventory = InventoryFixture.AddLot(inventory, "full-copy-house-paper", "paper", household, 2,
                inventory.WorldTick, storageBuildingId: house.InstanceId);
            inventory = InventoryFixture.AddLot(inventory, "full-copy-hands", "wood", actor, 100, inventory.WorldTick);
        }
        if (boundary == "reserved") inventory = InventoryFixture.Reserve(inventory, "source-unavailable", actor, source.LotId, 1, "other commitment", long.MaxValue);
        if (boundary.StartsWith("missing-", StringComparison.Ordinal))
            inventory = inventory with { Lots = inventory.Lots.Where(lot => lot.OwnerId != actor || lot.ItemKind != boundary[8..]).ToArray() };
        state = FarmFieldTests.WithInventory(state, inventory);
        if (boundary == "unknown-sites") state = state with
        {
            Knowledge = state.Knowledge! with
            { Facts = state.Knowledge.Facts.Where(fact => fact.OwnerId != actor).ToArray() }
        };
        var facts = JsonSerializer.Serialize(state.Knowledge!.Facts);
        using var world = Restore(state);
        var receipt = Submit(world, "unavailable-copy", "copy a book");
        for (var tick = 0; tick < 3; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var final = world.ExportState();
        Assert.Equal("blocked", Order(world, receipt).Status);
        Assert.Equal(0, Order(world, receipt).CompletedUnits);
        Assert.Empty(final.Knowledge!.WritingProjects);
        Assert.Single(final.Knowledge.Artifacts);
        Assert.Equal(facts, JsonSerializer.Serialize(final.Knowledge.Facts));
        AssertUnmovedLot(inventory.GetLot(source.LotId), world.Society.Inventory.GetLot(source.LotId));
        AssertReload(final);
    }

    [Fact]
    public async Task CopySourceStaysPinnedWhilePermittedHouseMaterialsAreCollected()
    {
        var state = await CopyReady("field_map");
        var actor = CopyActor(state);
        var source = Assert.Single(state.Knowledge!.Artifacts);
        var household = state.Society.Society.GetInhabitant(actor).HouseholdId!;
        var house = state.WorldSimulation!.Buildings.First(building => building.HouseholdId == household &&
            state.WorldContent!.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId).Tags.Contains("house"));
        var start = state.Map.Tiles.Select(tile => tile.Position).First(point =>
            state.Map.FootDistance(point, house.Position) == 2 && state.Map.IsReachableOnFoot(point, house.Position) &&
            state.Inhabitants.All(person => person.Position != point));
        var inventory = state.Society.Society.Inventory with
        { Lots = state.Society.Society.Inventory.Lots.Where(lot => lot.Id != "copy-paper").ToArray() };
        inventory = InventoryFixture.AddLot(inventory, "house-copy-paper", "paper", household, 2,
            inventory.WorldTick, storageBuildingId: house.InstanceId);
        state = FarmFieldTests.WithInventory(state, inventory) with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor ? person with
            { Position = start, TravelCooldownTicks = 0, LastDecisionContext = null } : person).ToArray(),
        };
        using var world = Restore(state);
        var receipt = Submit(world, "house-copy", "copy a map");
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.NotEqual(start, world.Inhabitants.Single(person => person.InhabitantId == actor).Position);
        Assert.Equal(source.Id, Order(world, receipt).KnowledgeCopySourceArtifactId);
        Assert.Null(Order(world, receipt).KnowledgeWritingProjectId);
        Assert.Empty(world.Knowledge.WritingProjects);
        Assert.Single(world.Knowledge.Artifacts);
        AssertReload(world.ExportState());
        await FinishTogether(world, receipt);
        Assert.Equal(2, world.Knowledge.Artifacts.Count);
        Assert.Equal((household, house.InstanceId, 1), (world.Society.Inventory.GetLot("house-copy-paper").OwnerId,
            world.Society.Inventory.GetLot("house-copy-paper").StorageBuildingId, world.Society.Inventory.GetLot("house-copy-paper").Quantity));
        Assert.Contains(world.ExportState().Events, item => item.Kind == "agent_knowledge_material_collected" && item.Detail == actor + "|paper|1");
        Assert.Equal(source.Facts.Select(Account), world.Knowledge.Artifacts[1].Facts.Select(Account));
        AssertReload(world.ExportState());
    }

    [Fact]
    public async Task LostSourceReleasesInputsAndWaitsForThatSameSourceBeforeRetrying()
    {
        var state = await CopyReady("book");
        var actor = CopyActor(state);
        var other = state.Inhabitants.First(person => person.InhabitantId != actor).InhabitantId;
        var source = Assert.Single(state.Knowledge!.Artifacts);
        using var starting = Restore(state);
        var receipt = Submit(starting, "source-loss", "copy a book");
        Assert.True((await starting.AdvanceOneTickAsync()).Advanced);
        state = starting.ExportState();
        var job = Assert.Single(state.Knowledge!.WritingProjects);
        var inventory = InventoryFixture.Transfer(state.Society.Society.Inventory, "lost-source", actor, other, source.LotId, 1, "return source");
        using var interrupted = Restore(FarmFieldTests.WithInventory(state, inventory));
        Assert.True((await interrupted.AdvanceOneTickAsync()).Advanced);
        state = interrupted.ExportState();
        Assert.Empty(state.Knowledge!.WritingProjects);
        Assert.Single(state.Knowledge.Artifacts);
        Assert.Equal(("blocked", 0, source.Id, (string?)null),
            (Order(interrupted, receipt).Status, Order(interrupted, receipt).CompletedUnits,
                Order(interrupted, receipt).KnowledgeCopySourceArtifactId, Order(interrupted, receipt).KnowledgeWritingProjectId));
        Assert.All(job.Materials, input => Assert.Equal(InventoryReservationState.Released,
            interrupted.Society.Inventory.GetReservation(input.ReservationId).State));
        Assert.Equal((3, 2), (Quantity(state, actor, "paper"), Quantity(state, actor, "cloth")));
        AssertReload(state);
        inventory = InventoryFixture.Transfer(state.Society.Society.Inventory, "returned-source", other, actor, source.LotId, 1, "return the same source");
        using var resumed = Restore(FarmFieldTests.WithInventory(state, inventory));
        await FinishTogether(resumed, receipt);
        var final = resumed.ExportState();
        Assert.Equal(2, final.Knowledge!.Artifacts.Count);
        Assert.NotEqual(job.Id, final.Knowledge.Artifacts[1].WritingProjectId);
        Assert.Equal((1, 1), (Quantity(final, actor, "paper"), Quantity(final, actor, "cloth")));
        AssertReload(final);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationOrReplacementReleasesOnlyTheCopyOrdersInputs(bool replace)
    {
        var state = await CopyReady("book");
        var actor = CopyActor(state);
        var inventory = InventoryFixture.Reserve(state.Society.Society.Inventory, "unrelated-copy-paper", actor,
            "copy-paper", 1, "other commitment", long.MaxValue);
        using var world = Restore(FarmFieldTests.WithInventory(state, inventory));
        var receipt = Submit(world, "cancel-copy", "copy a book");
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var job = Assert.Single(world.ExportState().Knowledge!.WritingProjects);
        if (replace) Submit(world, "replace-copy", $"move to {world.Inhabitants[0].Position.X},{world.Inhabitants[0].Position.Y}");
        else Assert.True(world.CancelOrder(new("cancel-copy-receipt", "owner:test", world.Society.WorldId, actor, receipt.InstructionId)).Changed);
        Assert.Equal(("cancelled", 0, (string?)null),
            (Order(world, receipt).Status, Order(world, receipt).CompletedUnits, Order(world, receipt).KnowledgeWritingProjectId));
        Assert.Empty(world.ExportState().Knowledge!.WritingProjects);
        Assert.Single(world.ExportState().Knowledge!.Artifacts);
        Assert.Equal((3, 2), (Quantity(world.ExportState(), actor, "paper"), Quantity(world.ExportState(), actor, "cloth")));
        Assert.All(job.Materials, input => Assert.Equal(InventoryReservationState.Released,
            world.Society.Inventory.GetReservation(input.ReservationId).State));
        Assert.Equal(InventoryReservationState.Reserved, world.Society.Inventory.GetReservation("unrelated-copy-paper").State);
        AssertReload(world.ExportState());
    }

    [Theory]
    [InlineData("copy 2 maps")]
    [InlineData("keep copying maps")]
    public async Task ACountedOrRepeatingCopyWaitsAfterCopyingTheOnlyHeldAccount(string text)
    {
        var state = await CopyReady("field_map");
        var actor = CopyActor(state);
        using var world = Restore(state);
        var receipt = Submit(world, "more-copying", text);
        for (var tick = 0; tick < 14; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(("blocked", 1, (string?)null),
            (Order(world, receipt).Status, Order(world, receipt).CompletedUnits, Order(world, receipt).KnowledgeCopySourceArtifactId));
        Assert.Contains("already been written", Order(world, receipt).BlockedReason!);
        Assert.Equal(2, world.ExportState().Knowledge!.Artifacts.Count);
        Assert.Equal(2, Quantity(world.ExportState(), actor, "paper"));
        AssertReload(world.ExportState());
        // A second account is produced by the original author observing an
        // actual planted tree and doing another paid native writing job.
        state = world.ExportState();
        var author = state.Knowledge!.Artifacts[0].CreatorId;
        var target = state.Knowledge.Artifacts[0].Facts.First(fact => fact.Position !=
            state.Inhabitants.Single(person => person.InhabitantId == author).Position).Position;
        state = FarmFieldTests.WithInventory(state, InventoryFixture.AddLot(state.Society.Society.Inventory,
            "new-copy-account-seed", TreeGrowthRules.SeedItem(TreeGrowthRules.Orchard), author, 1, state.Society.Society.WorldTick));
        using var writing = PrivateWorldRuntime.Restore(state,
            id => id == author ? new Choose("knowledge_write:field_map") : new Idle());
        Assert.True(writing.PlantTree(author, TreeGrowthRules.Orchard, "new-copy-account-seed", target).Planted);
        for (var tick = 0; tick < 12 && writing.Knowledge.Artifacts.Count < 3; tick++)
            Assert.True((await writing.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(3, writing.Knowledge.Artifacts.Count);
        state = writing.ExportState();
        var next = state.Knowledge!.Artifacts[2];
        Assert.Equal(author, next.CreatorId);
        var inventory = InventoryFixture.Transfer(state.Society.Society.Inventory, "give-next-account",
            author, actor, next.LotId, 1, "give the new paid account");
        using var resumed = Restore(FarmFieldTests.WithInventory(state, inventory));
        for (var tick = 0; tick < 12 && Order(resumed, receipt).CompletedUnits < 2; tick++)
            Assert.True((await resumed.AdvanceOneTickAsync()).Advanced);
        Assert.Equal((text.StartsWith("keep", StringComparison.Ordinal) ? "doing" : "finished", 2),
            (Order(resumed, receipt).Status, Order(resumed, receipt).CompletedUnits));
        Assert.Equal(4, resumed.Knowledge.Artifacts.Count);
        Assert.Equal(next.Id, resumed.Knowledge.Artifacts[3].SourceArtifactId);
        Assert.Equal(next.Facts.Select(Account), resumed.Knowledge.Artifacts[3].Facts.Select(Account));
        Assert.Equal(1, Quantity(resumed.ExportState(), actor, "paper"));
        AssertReload(resumed.ExportState());
    }

    [Fact]
    public async Task CopyingAnOldSourceKeepsItsFrozenAccountAfterTheCopierObservesAChange()
    {
        var state = await CopyReady("field_map");
        var actor = CopyActor(state);
        var source = Assert.Single(state.Knowledge!.Artifacts);
        var target = source.Facts.First(fact => fact.Position != state.Inhabitants.Single(person => person.InhabitantId == actor).Position).Position;
        state = FarmFieldTests.WithInventory(state, InventoryFixture.AddLot(state.Society.Society.Inventory,
            "copy-observation-seed", TreeGrowthRules.SeedItem(TreeGrowthRules.Orchard), actor, 1, state.Society.Society.WorldTick));
        using var world = Restore(state);
        Assert.True(world.PlantTree(actor, TreeGrowthRules.Orchard, "copy-observation-seed", target).Planted);
        Assert.Contains(world.Knowledge.Facts, fact => fact.OwnerId == actor && fact.Position == target && fact.ResourceKinds.Contains("fruit"));
        Assert.DoesNotContain(source.Facts, fact => fact.Position == target && fact.ResourceKinds.Contains("fruit"));
        var personal = JsonSerializer.Serialize(world.Knowledge.Facts);
        var receipt = Submit(world, "old-source-copy", "copy a map");
        await FinishTogether(world, receipt);
        var copy = Assert.Single(world.Knowledge.Artifacts, item => item.CreatorId == actor);
        Assert.Equal(source.Facts.Select(Account), copy.Facts.Select(Account));
        Assert.Equal(personal, JsonSerializer.Serialize(world.Knowledge.Facts));
        AssertReload(world.ExportState());
    }

    [Theory]
    [InlineData("copy a book", true)]
    [InlineData("bind a book", false)]
    public async Task MatchingOrdinaryCopyIsAdoptedAndDifferentNativeWorkIsPreserved(string text, bool matching)
    {
        var state = await CopyReady("book");
        var actor = CopyActor(state);
        var source = Assert.Single(state.Knowledge!.Artifacts);
        using var world = PrivateWorldRuntime.Restore(state,
            id => id == actor ? new Choose("knowledge_copy:" + source.Id) : new Idle());
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var job = Assert.Single(world.Knowledge.WritingProjects);
        Assert.Null(job.OrderInstructionId);
        var receipt = Submit(world, "adopt-copy", text);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var current = Assert.Single(world.Knowledge.WritingProjects);
        Assert.Equal(job.Id, current.Id);
        if (matching)
        {
            Assert.Equal(receipt.InstructionId, current.OrderInstructionId);
            Assert.Equal(source.Id, Order(world, receipt).KnowledgeCopySourceArtifactId);
            using var resumed = Restore(world.ExportState());
            await FinishTogether(resumed, receipt);
            Assert.Equal(job.Id, resumed.Knowledge.Artifacts[1].WritingProjectId);
        }
        else
        {
            Assert.Equal(JsonSerializer.Serialize(job), JsonSerializer.Serialize(current));
            Assert.Equal("blocked", Order(world, receipt).Status);
            Assert.Contains("Another writing job", Order(world, receipt).BlockedReason!);
            Assert.True(world.CancelOrder(new("cancel-other-order", "owner:test", world.Society.WorldId, actor, receipt.InstructionId)).Changed);
            Assert.Equal(JsonSerializer.Serialize(job), JsonSerializer.Serialize(Assert.Single(world.Knowledge.WritingProjects)));
        }
        AssertReload(world.ExportState());
    }

    [Fact]
    public async Task QueuedCopyStartsAfterWritingAndUrgentFoodPausesItsWork()
    {
        var state = await CopyReady("book");
        var actor = CopyActor(state);
        using var starting = Restore(state);
        var first = Submit(starting, "write-before-copy", "write a record");
        Assert.True((await starting.AdvanceOneTickAsync()).Advanced);
        var second = Submit(starting, "queued-copy", "copy a book", queue: true);
        Assert.Equal("queued", Order(starting, second).Status);
        Assert.Null(Order(starting, second).KnowledgeCopySourceArtifactId);
        await FinishTogether(starting, first);
        Assert.Equal(0, Order(starting, second).CompletedUnits);
        Assert.True((await starting.AdvanceOneTickAsync()).Advanced);
        state = starting.ExportState();
        var job = Assert.Single(state.Knowledge!.WritingProjects);
        state = FarmFieldTests.WithInventory(state, InventoryFixture.AddLot(state.Society.Society.Inventory,
            "copy-urgent-food", "berries", actor, 4, state.Society.Society.WorldTick)) with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor ? person with
            { HungerBasisPoints = 1000, LastDecisionContext = null } : person).ToArray(),
        };
        using var world = Restore(state);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal("interrupted", Order(world, second).Status);
        Assert.Equal(JsonSerializer.Serialize(job), JsonSerializer.Serialize(Assert.Single(world.Knowledge.WritingProjects)));
        Assert.True(Quantity(world.ExportState(), actor, "berries") < 4);
        await FinishTogether(world, second);
        Assert.Equal((1, 1), (Order(world, first).CompletedUnits, Order(world, second).CompletedUnits));
        Assert.Equal(3, world.Knowledge.Artifacts.Count);
        Assert.Equal(0, Quantity(world.ExportState(), actor, "paper"));
        AssertReload(world.ExportState());
    }

    [Fact]
    public async Task StrictLoadRejectsInventedSourcesAndOriginalWritingCreditedAsACopy()
    {
        var state = await CopyReady("book");
        using var world = Restore(state);
        var receipt = Submit(world, "strict-copy", "copy a book");
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        state = world.ExportState();
        var instruction = state.Instructions!.Single(item => item.InstructionId == receipt.InstructionId);
        foreach (var changed in new[]
        {
            instruction.Order! with { KnowledgeCopySourceArtifactId = "missing-artifact" },
            instruction.Order! with { KnowledgeCopySourceArtifactId = null },
            instruction.Order! with { Action = "write_knowledge", ProgressUnit = "artifacts", KnowledgeCopySourceArtifactId = null },
            instruction.Order! with { CompletedUnits = 1, RepeatUntilCancelled = true, LastEffectId = "writing:artifact:knowledge-artifact-000001" },
        }) Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(state with
        { Instructions = state.Instructions!.Select(item => item == instruction ? item with { Order = changed } : item).ToArray() }));
        AssertReload(state);
    }

    private static string CopyActor(PrivateWorldRuntimeState state) => state.Knowledge!.Artifacts.Count > 0
        ? state.Inhabitants.First(person => person.InhabitantId != state.Knowledge.Artifacts[0].CreatorId).InhabitantId
        : state.Inhabitants[0].InhabitantId;

    private static string Account(AgentKnowledgeFact fact) => JsonSerializer.Serialize(new
    { fact.DiscovererId, fact.Position, fact.ResourceKinds, fact.Terrain });

    private static async Task<PrivateWorldRuntimeState> CopyReady(string kind) => PrivateWorldRuntimeCodec.Decode(await Sources[kind].Value);

    private static async Task<byte[]> MakeSource(string kind)
    {
        var state = await Prepared();
        var author = state.Inhabitants[0].InhabitantId;
        using var writing = PrivateWorldRuntime.Restore(Supplies(state, author, 2, 1),
            id => id == author ? new Choose("knowledge_write:" + kind) : new Idle());
        for (var tick = 0; tick < 16 && writing.ExportState().Knowledge!.Artifacts.Count == 0; tick++)
            Assert.True((await writing.AdvanceOneTickAsync()).Advanced);
        state = writing.ExportState();
        var source = Assert.Single(state.Knowledge!.Artifacts);
        var reader = state.Inhabitants.First(person => person.InhabitantId != author).InhabitantId;
        var inventory = InventoryFixture.Transfer(state.Society.Society.Inventory, "give-paid-source", author,
            reader, source.LotId, 1, "give the actual written source");
        // The only fixture changes here are physical custody and a controlled
        // reader position/survival state. Knowledge comes from the native read action.
        state = FarmFieldTests.WithInventory(state, inventory) with
        {
            Inhabitants = state.Inhabitants.OrderBy(person => person.InhabitantId == reader ? 0 : 1).Select(person =>
                person.InhabitantId == reader ? person with
                {
                    Position = state.Inhabitants[0].Position,
                    HungerBasisPoints = 9500,
                    LastDecisionContext = null,
                    TravelCooldownTicks = 0
                } : person).ToArray(),
        };
        using var reading = PrivateWorldRuntime.Restore(state,
            id => id == reader ? new Choose("knowledge_read:" + source.Id) : new Idle());
        Assert.True((await reading.AdvanceOneTickAsync()).Advanced);
        state = reading.ExportState();
        Assert.Equal(source.Facts.Count, state.Knowledge!.Facts.Count(fact => fact.OwnerId == reader));
        inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "copy-paper", "paper", reader, 3, reading.WorldTick);
        inventory = InventoryFixture.AddLot(inventory, "copy-cloth", "cloth", reader, 2, reading.WorldTick);
        return PrivateWorldRuntimeCodec.Encode(FarmFieldTests.WithInventory(state, inventory));
    }

    private static async Task<PrivateWorldRuntimeState> Prepared(bool learn = true) => learn
        ? PrivateWorldRuntimeCodec.Decode(await LearnedBaseline.Value) : await PrepareCore(learn: false);

    private static async Task<PrivateWorldRuntimeState> PrepareCore(bool learn)
    {
        var state = PrivateWorldRuntimeCodec.Decode(Baseline.Value);
        using (var initial = PrivateWorldRuntime.Restore(state, _ => new Idle()))
        {
            Assert.True((await initial.AdvanceOneTickAsync()).Advanced);
            state = initial.ExportState();
        }
        var actor = state.Inhabitants[0].InhabitantId;
        using var geometry = PrivateWorldRuntime.Restore(state, _ => new Idle());
        var occupied = state.WorldSimulation!.Buildings.SelectMany(building => WorldContentSimulationRules.Footprint(
                state.WorldContent!.Buildings.Single(item => item.CanonicalId == building.DefinitionId), building))
            .Concat(geometry.RoadTiles).Concat(state.Map.Resources.Select(resource => resource.Position))
            .Concat(state.Map.CampObjects.Select(item => item.Position)).ToHashSet();
        var target = state.Map.Tiles.Select(tile => tile.Position).Where(point => !occupied.Contains(point) &&
                !state.Towns!.Any(town => town.BorderTiles.Contains(point)) &&
                TreeGrowthRules.GroundRefusal(state.Map, point) is null &&
                state.Map.FootNeighbors(point).Any(next => state.Inhabitants.All(person => person.Position != next)) &&
                state.Map.IsReachableOnFoot(state.Inhabitants[0].Position, point))
            .OrderBy(point => state.Map.FootDistance(state.Inhabitants[0].Position, point)).First();
        var stand = state.Map.FootNeighbors(target).First(point => state.Inhabitants.All(person => person.Position != point));
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor ? person with
            {
                Position = stand,
                HungerBasisPoints = 9500,
                TravelCooldownTicks = 0,
                LastDecisionContext = null,
                Project = null,
            } : person).ToArray(),
        };
        if (!learn) return state;
        using var world = Restore(state);
        foreach (var destination in new[] { target, stand })
        {
            var receipt = Submit(world, "learn-site-" + destination, $"move to {destination.X},{destination.Y}");
            for (var tick = 0; tick < 12 && Order(world, receipt).Status != "finished"; tick++)
                Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.Equal("finished", Order(world, receipt).Status);
        }
        Assert.Equal(2, world.ExportState().Knowledge!.Facts.Count);
        return world.ExportState();
    }

    private static PrivateWorldRuntimeState Supplies(PrivateWorldRuntimeState state, string owner, int paper, int cloth)
    {
        var inventory = state.Society.Society.Inventory;
        if (paper > 0) inventory = InventoryFixture.AddLot(inventory, "ordered-paper", "paper", owner, paper, inventory.WorldTick);
        if (cloth > 0) inventory = InventoryFixture.AddLot(inventory, "ordered-cloth", "cloth", owner, cloth, inventory.WorldTick);
        return FarmFieldTests.WithInventory(state, inventory);
    }

    private static PrivateWorldRuntime Restore(PrivateWorldRuntimeState state) => PrivateWorldRuntime.Restore(
        PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)),
        id => id == CopyActor(state) ? new DeterministicDecisionProvider() : new Idle());

    private static OwnerInstructionReceipt Submit(PrivateWorldRuntime world, string key, string text, bool queue = false) =>
        world.SubmitInstruction(new(key, "owner:test", CopyActor(world.ExportState()), OwnerInstructionKind.MustDo, text, Queue: queue));

    private static OwnerInstructionOrder Order(PrivateWorldRuntime world, OwnerInstructionReceipt receipt) =>
        world.ExportState().Instructions!.Single(item => item.InstructionId == receipt.InstructionId).Order!;

    private static int Quantity(PrivateWorldRuntimeState state, string owner, string kind) =>
        state.Society.Society.Inventory.Lots.Where(lot => lot.OwnerId == owner && lot.ItemKind == kind).Sum(lot => lot.Quantity);

    private static string FactKey(AgentKnowledgeFact fact) => JsonSerializer.Serialize(fact);

    private static void AssertUnmovedLot(InventoryLot before, InventoryLot after) =>
        Assert.Equal(before with { LastProcessedTick = after.LastProcessedTick }, after);

    private static void AssertReload(PrivateWorldRuntimeState state)
    {
        var bytes = PrivateWorldRuntimeCodec.Encode(state);
        using var loaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes));
        loaded.Validate();
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(loaded.ExportState()));
    }

    private static async Task FinishTogether(PrivateWorldRuntime world, OwnerInstructionReceipt receipt)
    {
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False((await world.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        using var replay = Restore(PrivateWorldRuntimeCodec.Decode(bytes));
        for (var tick = 0; tick < 32 && Order(world, receipt).Status != "finished"; tick++)
        {
            if (world.ExportState().Knowledge!.WritingProjects.FirstOrDefault(project =>
                    project.OrderInstructionId == receipt.InstructionId) is { } finishing &&
                finishing.WorkDone + 1 == finishing.WorkRequired)
            {
                var beforeCompletion = PrivateWorldRuntimeCodec.Encode(world.ExportState());
                Assert.False((await world.AdvanceOneTickAsync(() => false)).Advanced);
                Assert.Equal(beforeCompletion, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
            }
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        }
        Assert.Equal("finished", Order(world, receipt).Status);
    }

    private sealed class Idle : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default) =>
            new DeterministicDecisionProvider().DecideAsync(request with
            {
                Observation = request.Observation with { Candidates = [request.Observation.Candidates.Single(item => item.Id == "safe_idle")] },
            }, cancellationToken);
    }

    private sealed class Choose(string choice) : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default) =>
            new DeterministicDecisionProvider().DecideAsync(request with
            {
                Observation = request.Observation with
                {
                    Candidates = [request.Observation.Candidates.FirstOrDefault(item => item.Id == choice) ??
                        request.Observation.Candidates.Single(item => item.Id == "safe_idle")],
                },
            }, cancellationToken);
    }
}

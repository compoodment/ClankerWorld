using System.Text.Json;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class KnowledgeOrderTests
{
    private static readonly Lazy<byte[]> Baseline = new(() => PrivateWorldRuntimeCodec.Encode(
        GeographyGeneratorTests.StartedGeneratedWorld(new GeographyOptions("knowledge-writing-orders", WorldSizePreset.Small))));
    // Each case loads its own checkpoint, learned by actual move orders once.
    private static readonly Lazy<Task<byte[]>> LearnedBaseline = new(async () =>
        PrivateWorldRuntimeCodec.Encode(await PrepareCore(learn: true)));

    [Theory]
    [InlineData("write a field record", "field_record", 1, 0, 1)]
    [InlineData("draw a map", "field_map", 1, 0, 2)]
    [InlineData("bind a book", "book", 2, 1, 2)]
    public async Task AnOrderWritesOneRealItemFromLearnedSitesAndPaidMaterials(
        string text, string kind, int paperCost, int clothCost, int sites)
    {
        var state = await Prepared();
        var actor = state.Inhabitants[0].InhabitantId;
        state = Supplies(state, actor, 3, 2);
        using var world = Restore(state);
        var receipt = Submit(world, "ordered-writing", text);
        Assert.Equal("write_knowledge", Order(world, receipt).Action);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var working = world.ExportState();
        var project = Assert.Single(working.Knowledge!.WritingProjects);
        Assert.Equal((actor, kind, receipt.InstructionId, project.Id),
            (project.ActorId, project.Kind, project.OrderInstructionId, Order(world, receipt).KnowledgeWritingProjectId));
        Assert.Equal(0, Order(world, receipt).CompletedUnits);
        Assert.Empty(working.Knowledge.Artifacts);
        Assert.Equal(3, Quantity(working, actor, "paper"));
        Assert.All(project.Materials, input => Assert.Equal(InventoryReservationState.Reserved,
            world.Society.Inventory.GetReservation(input.ReservationId).State));
        await FinishTogether(world, receipt);
        var final = world.ExportState();
        var artifact = Assert.Single(final.Knowledge!.Artifacts);
        Assert.Equal((actor, kind, receipt.InstructionId, project.Id, sites),
            (artifact.CreatorId, artifact.Kind, artifact.OrderInstructionId, artifact.WritingProjectId, artifact.Facts.Count));
        Assert.Equal(project.Facts.Select(FactKey), artifact.Facts.Select(FactKey));
        Assert.Equal((kind, actor, 1), (world.Society.Inventory.GetLot(artifact.LotId).ItemKind,
            world.Society.Inventory.GetLot(artifact.LotId).OwnerId, world.Society.Inventory.GetLot(artifact.LotId).Quantity));
        Assert.Equal((3 - paperCost, 2 - clothCost), (Quantity(final, actor, "paper"), Quantity(final, actor, "cloth")));
        Assert.Equal(("finished", 1, (string?)null),
            (Order(world, receipt).Status, Order(world, receipt).CompletedUnits, Order(world, receipt).KnowledgeWritingProjectId));
        Assert.All(project.Materials, input => Assert.Equal(InventoryReservationState.Completed,
            world.Society.Inventory.GetReservation(input.ReservationId).State));
        var projected = Assert.Single(new OwnerWorldObservationStore(world).GetSnapshot().Instructions,
            item => item.InstructionId == receipt.InstructionId).Order!;
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var client = JsonSerializer.Deserialize<ClankerWorld.GodotClient.UI.OwnerWorldInstructionOrder>(JsonSerializer.Serialize(projected, json), json)!;
        Assert.Equal((kind, "artifacts", 1), (client.TargetKnowledgeKind, client.ProgressUnit, client.CompletedUnits));
        AssertReload(final);
    }

    [Fact]
    public async Task PaperDoesNotInventKnowledgeForAWritingOrder()
    {
        var state = await Prepared(learn: false);
        var actor = state.Inhabitants[0].InhabitantId;
        state = Supplies(state, actor, 3, 2);
        using var world = Restore(state);
        var receipt = Submit(world, "unknown-map", "draw a map");
        for (var tick = 0; tick < 3; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var final = world.ExportState();
        Assert.Equal("blocked", Order(world, receipt).Status);
        Assert.Contains("learned", Order(world, receipt).BlockedReason!);
        Assert.Empty(final.Knowledge!.Facts);
        Assert.Empty(final.Knowledge.WritingProjects);
        Assert.Empty(final.Knowledge.Artifacts);
        Assert.Equal(3, Quantity(final, actor, "paper"));
        AssertReload(final);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationOrReplacementReleasesOnlyTheOrdersUnspentInputs(bool replace)
    {
        var state = await Prepared();
        var actor = state.Inhabitants[0].InhabitantId;
        state = Supplies(state, actor, 3, 2);
        var inventory = InventoryFixture.Reserve(state.Society.Society.Inventory, "unrelated-paper", actor,
            "ordered-paper", 1, "another commitment", long.MaxValue);
        using var world = Restore(FarmFieldTests.WithInventory(state, inventory));
        var receipt = Submit(world, "cancel-writing", "bind a book");
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var project = Assert.Single(world.ExportState().Knowledge!.WritingProjects);
        if (replace)
            Submit(world, "replace-writing", "move to " + world.Inhabitants[0].Position.X + "," + world.Inhabitants[0].Position.Y);
        else
        {
            var request = new OwnerOrderCancelRequest("cancel-writing-receipt", "owner:test", world.Society.WorldId,
                actor, receipt.InstructionId);
            var cancelled = world.CancelOrder(request);
            Assert.True(cancelled.Changed);
            Assert.Equal(cancelled, world.CancelOrder(request));
            using var reloaded = Restore(world.ExportState());
            Assert.Equal(cancelled, reloaded.CancelOrder(request));
        }
        Assert.Equal(("cancelled", 0, (string?)null),
            (Order(world, receipt).Status, Order(world, receipt).CompletedUnits, Order(world, receipt).KnowledgeWritingProjectId));
        Assert.Empty(world.ExportState().Knowledge!.WritingProjects);
        Assert.Empty(world.ExportState().Knowledge!.Artifacts);
        Assert.Equal((3, 2), (Quantity(world.ExportState(), actor, "paper"), Quantity(world.ExportState(), actor, "cloth")));
        Assert.All(project.Materials, input => Assert.Equal(InventoryReservationState.Released,
            world.Society.Inventory.GetReservation(input.ReservationId).State));
        Assert.Equal(InventoryReservationState.Reserved, world.Society.Inventory.GetReservation("unrelated-paper").State);
        AssertReload(world.ExportState());
    }

    [Fact]
    public async Task QueuedWritingStartsAfterTheActiveOrderAndCreditsItsOwnItem()
    {
        var state = await Prepared();
        var actor = state.Inhabitants[0].InhabitantId;
        using var world = Restore(Supplies(state, actor, 3, 2));
        var first = Submit(world, "first-writing", "write a record");
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var project = Assert.Single(world.ExportState().Knowledge!.WritingProjects);
        var second = Submit(world, "second-writing", "draw a map", queue: true);
        Assert.Equal("queued", Order(world, second).Status);
        Assert.Equal(JsonSerializer.Serialize(project), JsonSerializer.Serialize(Assert.Single(world.ExportState().Knowledge!.WritingProjects)));
        AssertReload(world.ExportState());
        await FinishTogether(world, first);
        Assert.Equal(0, Order(world, second).CompletedUnits);
        await FinishTogether(world, second);
        var final = world.ExportState();
        Assert.Equal(new[] { first.InstructionId, second.InstructionId },
            final.Knowledge!.Artifacts.Select(item => item.OrderInstructionId));
        Assert.Equal(1, Quantity(final, actor, "paper"));
        AssertReload(final);
    }

    [Theory]
    [InlineData("write a record", "foreign")]
    [InlineData("draw a map", "reserved")]
    [InlineData("bind a book", "missing-cloth")]
    [InlineData("draw a map", "full")]
    public async Task WritingOrdersRefuseUnavailableMaterialsWithoutTakingOtherProperty(string text, string reason)
    {
        var state = await Prepared();
        var actor = state.Inhabitants[0].InhabitantId;
        var other = state.Inhabitants.First(person => person.InhabitantId != actor).InhabitantId;
        state = Supplies(state, reason == "foreign" ? other : actor, reason == "missing-cloth" ? 2 : 1, 0);
        var inventory = state.Society.Society.Inventory;
        if (reason == "reserved")
            inventory = InventoryFixture.Reserve(inventory, "paper-unavailable", actor, "ordered-paper", 1,
                "another commitment", long.MaxValue);
        if (reason == "full")
        {
            // Controlled carried wood fills the actor's capacity; the paper stays in shared House stock.
            var household = state.Society.Society.GetInhabitant(actor).HouseholdId!;
            var house = state.WorldSimulation!.Buildings.First(building => building.HouseholdId == household &&
                state.WorldContent!.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId).Tags.Contains("house"));
            inventory = inventory with { Lots = inventory.Lots.Where(lot => lot.Id != "ordered-paper").ToArray() };
            inventory = InventoryFixture.AddLot(inventory, "ordered-paper", "paper", household, 1,
                inventory.WorldTick, storageBuildingId: house.InstanceId);
            inventory = InventoryFixture.AddLot(inventory, "full-writing-hands", "wood", actor, 100, inventory.WorldTick);
        }
        using var world = Restore(FarmFieldTests.WithInventory(state, inventory));
        var receipt = Submit(world, "unavailable-writing", text);
        for (var tick = 0; tick < 3; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var final = world.ExportState();
        Assert.Equal("blocked", Order(world, receipt).Status);
        Assert.Empty(final.Knowledge!.WritingProjects);
        Assert.Empty(final.Knowledge.Artifacts);
        Assert.Equal(0, Order(world, receipt).CompletedUnits);
        AssertUnmovedLot(inventory.GetLot("ordered-paper"), final.Society.Society.Inventory.GetLot("ordered-paper"));
        AssertReload(final);
    }

    [Theory]
    [InlineData("write 2 maps", false)]
    [InlineData("keep drawing maps", true)]
    public async Task MoreWritingWaitsForNewPersonallyLearnedFactsAndPaysForEachArtifact(string text, bool repeat)
    {
        var state = await Prepared();
        var actor = state.Inhabitants[0].InhabitantId;
        var target = state.Knowledge!.Facts.First(fact => fact.OwnerId == actor && fact.Position != state.Inhabitants[0].Position).Position;
        state = Supplies(state, actor, 3, 0);
        state = FarmFieldTests.WithInventory(state, InventoryFixture.AddLot(state.Society.Society.Inventory,
            "writing-observation-seed", TreeGrowthRules.SeedItem(TreeGrowthRules.Orchard), actor, 1, state.Society.Society.WorldTick));
        using var world = Restore(state);
        var receipt = Submit(world, "more-writing", text);
        for (var tick = 0; tick < 12 && Order(world, receipt).CompletedUnits == 0; tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var first = Assert.Single(world.ExportState().Knowledge!.Artifacts);
        Assert.Equal(1, Order(world, receipt).CompletedUnits);
        for (var tick = 0; tick < 3; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal("blocked", Order(world, receipt).Status);
        Assert.Contains("already been written", Order(world, receipt).BlockedReason!);
        Assert.Single(world.ExportState().Knowledge!.Artifacts);
        Assert.Equal(2, Quantity(world.ExportState(), actor, "paper"));
        AssertReload(world.ExportState());
        Assert.True(world.PlantTree(actor, TreeGrowthRules.Orchard, "writing-observation-seed", target).Planted);
        for (var tick = 0; tick < 12 && Order(world, receipt).CompletedUnits < 2; tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var final = world.ExportState();
        Assert.Equal(2, Order(world, receipt).CompletedUnits);
        Assert.Equal(repeat ? "doing" : "finished", Order(world, receipt).Status);
        Assert.Equal(1, Quantity(final, actor, "paper"));
        Assert.Equal(2, final.Knowledge!.Artifacts.Count);
        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(final.Knowledge.Artifacts[0]));
        Assert.Contains(final.Knowledge.Artifacts[1].Facts, fact => fact.Position == target && fact.ResourceKinds.Contains("fruit"));
        AssertReload(final);
    }

    [Fact]
    public async Task StrictLoadRejectsBrokenWritingBindingsAndInventedCompletion()
    {
        var state = await Prepared();
        var actor = state.Inhabitants[0].InhabitantId;
        using var world = Restore(Supplies(state, actor, 2, 1));
        var receipt = Submit(world, "strict-writing", "bind a book");
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var working = world.ExportState();
        var instruction = working.Instructions!.Single(item => item.InstructionId == receipt.InstructionId);
        var project = Assert.Single(working.Knowledge!.WritingProjects);
        foreach (var broken in new[]
        {
            working with { Knowledge = working.Knowledge with { WritingProjects = [project with { OrderInstructionId = "missing-order" }] } },
            working with { Knowledge = working.Knowledge with { WritingProjects = [project with { OrderInstructionId = null }] } },
            working with { Instructions = working.Instructions!.Select(item => item == instruction ? item with
                { Order = item.Order! with { TargetKnowledgeKind = "field_map" } } : item).ToArray() },
            working with { Instructions = working.Instructions!.Select(item => item == instruction ? item with
                { Order = item.Order! with { CompletedUnits = 1, LastEffectId = "writing:artifact:knowledge-artifact-000001", RepeatUntilCancelled = true } } : item).ToArray() },
        }) Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(broken));
        AssertReload(working);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WritingCollectsPermittedHouseStockButRefusesAnotherHouseholdsStock(bool foreign)
    {
        var state = await Prepared();
        var actor = state.Inhabitants[0].InhabitantId;
        var household = state.Society.Society.GetInhabitant(actor).HouseholdId!;
        if (foreign) household = state.Society.Society.Inhabitants.First(person => person.HouseholdId is { } home && home != household).HouseholdId!;
        var house = state.WorldSimulation!.Buildings.First(building => building.HouseholdId == household &&
            state.WorldContent!.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId).Tags.Contains("house"));
        var start = state.Map.Tiles.Select(tile => tile.Position).First(point =>
            state.Map.FootDistance(point, house.Position) == 2 && state.Map.IsReachableOnFoot(point, house.Position) &&
            state.Inhabitants.All(person => person.Position != point));
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "house-writing-paper", "paper", household,
            2, state.Society.Society.WorldTick, storageBuildingId: house.InstanceId);
        state = FarmFieldTests.WithInventory(state, inventory) with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor ? person with
            { Position = start, TravelCooldownTicks = 0, LastDecisionContext = null } : person).ToArray(),
        };
        using var world = Restore(state);
        var receipt = Submit(world, "house-writing", "draw a map");
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Empty(world.ExportState().Knowledge!.Artifacts);
        if (foreign)
        {
            Assert.Equal("blocked", Order(world, receipt).Status);
            AssertUnmovedLot(inventory.GetLot("house-writing-paper"), world.Society.Inventory.GetLot("house-writing-paper"));
            Assert.Equal(start, world.Inhabitants[0].Position);
        }
        else
        {
            Assert.NotEqual(start, world.Inhabitants[0].Position);
            await FinishTogether(world, receipt);
            Assert.Single(world.ExportState().Knowledge!.Artifacts);
            Assert.Equal((household, house.InstanceId, 1), (world.Society.Inventory.GetLot("house-writing-paper").OwnerId,
                world.Society.Inventory.GetLot("house-writing-paper").StorageBuildingId,
                world.Society.Inventory.GetLot("house-writing-paper").Quantity));
            Assert.Contains(world.ExportState().Events, item => item.Kind == "agent_knowledge_material_collected" &&
                item.Detail == actor + "|paper|1");
        }
        AssertReload(world.ExportState());
    }

    [Theory]
    [InlineData("draw a map", true)]
    [InlineData("bind a book", false)]
    public async Task MatchingOrdinaryWritingCanBeAdoptedWhileDifferentWorkIsPreserved(string text, bool matching)
    {
        var state = await Prepared();
        var actor = state.Inhabitants[0].InhabitantId;
        state = Supplies(state, actor, 3, 2);
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)),
            id => id == actor ? new Choose("knowledge_write:field_map") : new Idle());
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var original = Assert.Single(world.ExportState().Knowledge!.WritingProjects);
        Assert.Null(original.OrderInstructionId);
        var receipt = Submit(world, "adopt-writing", text);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var current = Assert.Single(world.ExportState().Knowledge!.WritingProjects);
        Assert.Equal(original.Id, current.Id);
        if (matching)
        {
            Assert.Equal(receipt.InstructionId, current.OrderInstructionId);
            // Continue through the normal built-in order path after admission.
            using var resumed = Restore(world.ExportState());
            await FinishTogether(resumed, receipt);
            Assert.Equal(original.Id, Assert.Single(resumed.ExportState().Knowledge!.Artifacts).WritingProjectId);
        }
        else
        {
            Assert.Equal(JsonSerializer.Serialize(original), JsonSerializer.Serialize(current));
            Assert.Equal("blocked", Order(world, receipt).Status);
            Assert.Contains("Another writing job", Order(world, receipt).BlockedReason!);
            world.CancelOrder(new("preserve-other-writing", "owner:test", world.Society.WorldId, actor, receipt.InstructionId));
            Assert.Equal(JsonSerializer.Serialize(original), JsonSerializer.Serialize(Assert.Single(world.ExportState().Knowledge!.WritingProjects)));
            Assert.All(original.Materials, input => Assert.Equal(InventoryReservationState.Reserved,
                world.Society.Inventory.GetReservation(input.ReservationId).State));
        }
        AssertReload(world.ExportState());
    }

    [Fact]
    public async Task UrgentFoodInterruptsWritingWithoutLosingItsPaidWork()
    {
        var state = await Prepared();
        var actor = state.Inhabitants[0].InhabitantId;
        using var starting = Restore(Supplies(state, actor, 2, 1));
        var receipt = Submit(starting, "hungry-writing", "bind a book");
        Assert.True((await starting.AdvanceOneTickAsync()).Advanced);
        state = starting.ExportState();
        var original = Assert.Single(state.Knowledge!.WritingProjects);
        state = FarmFieldTests.WithInventory(state, InventoryFixture.AddLot(state.Society.Society.Inventory,
            "writing-urgent-food", "berries", actor, 4, state.Society.Society.WorldTick)) with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor ? person with
            { HungerBasisPoints = 1000, LastDecisionContext = null } : person).ToArray(),
        };
        using var world = Restore(state);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal("interrupted", Order(world, receipt).Status);
        Assert.Equal(JsonSerializer.Serialize(original), JsonSerializer.Serialize(Assert.Single(world.ExportState().Knowledge!.WritingProjects)));
        Assert.True(Quantity(world.ExportState(), actor, "berries") < 4);
        await FinishTogether(world, receipt);
        Assert.Equal(original.Id, Assert.Single(world.ExportState().Knowledge!.Artifacts).WritingProjectId);
        AssertReload(world.ExportState());
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
        id => id == state.Inhabitants[0].InhabitantId ? new DeterministicDecisionProvider() : new Idle());

    private static OwnerInstructionReceipt Submit(PrivateWorldRuntime world, string key, string text, bool queue = false) =>
        world.SubmitInstruction(new(key, "owner:test", world.Inhabitants[0].InhabitantId, OwnerInstructionKind.MustDo, text, Queue: queue));

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

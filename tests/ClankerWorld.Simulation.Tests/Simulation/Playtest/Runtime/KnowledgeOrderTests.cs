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

    private static async Task<PrivateWorldRuntimeState> Prepared(bool learn = true)
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
                Position = stand, HungerBasisPoints = 9500, TravelCooldownTicks = 0, LastDecisionContext = null, Project = null,
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
}

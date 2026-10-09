using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class PrivateWorldCartOrderTests
{
    private static readonly Lazy<Task<byte[]>> Fixture = new(async () =>
    {
        using var setup = NormalPathWorld.CreateGenerated("cart-order-targets", _ => new IdleProvider());
        Assert.True((await setup.AdvanceOneTickAsync()).Advanced);
        var state = SettlementWeatherTestFixture.WithWeather(setup.ExportState(), WeatherKind.Clear);
        return PrivateWorldRuntimeCodec.Encode(state with
        {
            Inhabitants = state.Inhabitants.Select(person => person with
            { HungerBasisPoints = 9_500, Survival = new(), LastDecisionContext = null, Project = null, TravelCooldownTicks = 0 }).ToArray(),
        });
    });

    [Fact]
    public async Task NamedCartAttachmentAndQueuedParkingKeepTheirPhysicalTargetAcrossReplay()
    {
        var state = await CartState();
        var actor = state.Inhabitants[0].InhabitantId;
        using var world = PrivateWorldRuntime.Restore(state, _ => new IdleProvider());
        var attach = world.SubmitInstruction(Request("attach", actor, "Pull handcart z-cart"));
        world.SubmitInstruction(Request("park", actor, "Park handcart z-cart", queue: true));
        Assert.Equal("attach_handcart", world.ExportState().Instructions![0].Order!.Action);
        Assert.All(world.ExportState().Instructions!, instruction => Assert.Equal("z-cart", instruction.Order!.TargetCartLotId));
        Assert.All(new OwnerWorldObservationStore(world).GetSnapshot().Instructions, instruction => Assert.Equal("z-cart", instruction.Order!.TargetCartLotId));
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), _ => new IdleProvider());
        Assert.False((await world.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        var sawAttachment = false;
        for (var tick = 0; tick < 16 && world.ExportState().Instructions!.Any(item => item.Order!.Status != "finished"); tick++)
        {
            var before = world.Inhabitants[0].Position;
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
            var after = world.Inhabitants[0].Position;
            if (before != after) Assert.True(state.Map.CanFootStep(before, after));
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
            if (world.ExportState().HandcartHitches!.Count > 0)
            {
                Assert.Equal(new HandcartHitch("z-cart", actor), Assert.Single(world.ExportState().HandcartHitches!));
                sawAttachment = true;
                using var reloaded = PrivateWorldRuntime.Restore(world.ExportState(), _ => new IdleProvider());
                Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(reloaded.ExportState()));
            }
        }
        Assert.True(sawAttachment);
        var final = world.ExportState();
        Assert.All(final.Instructions!, item => { Assert.Equal("finished", item.Order!.Status); Assert.Equal(1, item.Order.CompletedUnits); });
        Assert.Empty(final.HandcartHitches!);
        Assert.Single(final.Events, item => item.Kind == "handcart_attached" && item.Detail == actor + ":z-cart");
        Assert.Single(final.Events, item => item.Kind == "handcart_parked" && item.Detail == actor + ":z-cart:parked");
        Assert.Equal(2, final.Events.Count(item => item.Kind == "instruction_order_finished"));
        Assert.Equal(attach, world.SubmitInstruction(Request("attach", actor, "Pull handcart z-cart")));
        Assert.Equal(state.Society.Society.Inventory.GetLot("a-cart") with { LastProcessedTick = final.Society.Society.WorldTick }, world.Society.Inventory.GetLot("a-cart"));
        Assert.Equal(state.Society.Society.Inventory.GetLot("cargo") with { LastProcessedTick = final.Society.Society.WorldTick }, world.Society.Inventory.GetLot("cargo"));
        Assert.Equal(actor, world.Society.Inventory.GetLot("z-cart").OwnerId);
        Assert.Equal(new InventoryGroundPosition(world.Inhabitants[0].Position.X, world.Inhabitants[0].Position.Y),
            world.Society.Inventory.GetLot("z-cart").GroundPosition);
        using var loaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(final)), _ => new IdleProvider());
        Assert.True((await loaded.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(2, loaded.ExportState().Events.Count(item => item.Kind == "instruction_order_finished"));
    }

    [Theory]
    [InlineData("foreign", "own")]
    [InlineData("broken", "broken")]
    [InlineData("reserved", "reserved")]
    [InlineData("occupied", "route")]
    public async Task CartOrderReportsTheActualBlockerWithoutAttachingAnotherCart(string blocker, string reason)
    {
        var state = await CartState();
        var actor = state.Inhabitants[0].InhabitantId;
        var inventory = state.Society.Society.Inventory;
        if (blocker is "foreign" or "broken") inventory = inventory with
        {
            Lots = inventory.Lots.Select(lot => lot.Id == "z-cart" || blocker == "foreign" && lot.Id == "cargo" ? lot with
            { OwnerId = blocker == "foreign" ? state.Inhabitants[1].InhabitantId : actor, ConditionBasisPoints = blocker == "broken" && lot.Id == "z-cart" ? 0 : lot.ConditionBasisPoints } : lot).ToArray(),
        };
        if (blocker == "reserved") inventory = InventoryFixture.Reserve(inventory, "cart-reservation", actor, "cargo", 1,
            "other_work", inventory.WorldTick + 50);
        var site = inventory.GetLot("z-cart").GroundPosition!.Value;
        state = state with
        {
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
            Inhabitants = state.Inhabitants.Select((person, index) => blocker == "occupied" && index == 1
                ? person with { Position = new(site.X, site.Y) } : person).ToArray(),
        };
        using var world = PrivateWorldRuntime.Restore(state, _ => new IdleProvider());
        world.SubmitInstruction(Request("blocked-cart", actor, "Attach handcart z-cart"));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var order = Assert.Single(world.ExportState().Instructions!).Order!;
        Assert.Equal("blocked", order.Status);
        Assert.Contains(reason, order.BlockedReason!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, order.CompletedUnits);
        Assert.Empty(world.ExportState().HandcartHitches!);
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "handcart_attached");
        using var loaded = PrivateWorldRuntime.Restore(world.ExportState(), _ => new IdleProvider());
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(loaded.ExportState()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelOrReplaceCartOrderPreventsItsAttachmentButATypoDoesNotReplaceIt(bool replace)
    {
        var state = await CartState();
        var actor = state.Inhabitants[0].InhabitantId;
        using var world = PrivateWorldRuntime.Restore(state, _ => new IdleProvider());
        var receipt = world.SubmitInstruction(Request("pending-cart", actor, "Attach my handcart z-cart"));
        world.SubmitInstruction(Request("typo", actor, "Attach my handcart z-cart tomorrow"));
        Assert.Equal("waiting", world.ExportState().Instructions!.Single(item => item.InstructionId == receipt.InstructionId).Order!.Status);
        if (replace) world.SubmitInstruction(Request("replacement", actor, "Park cart a-cart"));
        else world.CancelOrder(new("cancel-cart", "owner:test", state.Society.Society.WorldId, actor, receipt.InstructionId));
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), _ => new IdleProvider());
        for (var tick = 0; tick < 3; tick++) Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
        var old = restored.ExportState().Instructions!.Single(item => item.InstructionId == receipt.InstructionId).Order!;
        Assert.Equal("cancelled", old.Status);
        Assert.Equal(0, old.CompletedUnits);
        Assert.Equal("z-cart", old.TargetCartLotId);
        Assert.Empty(restored.ExportState().HandcartHitches!);
        Assert.DoesNotContain(restored.ExportState().Events, item => item.Kind is "handcart_attached" or "handcart_parked");
    }

    [Fact]
    public async Task UnspecifiedCartOrderKeepsItsChosenCartWhenOwnershipChangesDuringTravel()
    {
        var state = await CartState();
        var actor = state.Inhabitants[0].InhabitantId;
        using var world = PrivateWorldRuntime.Restore(state, _ => new IdleProvider());
        world.SubmitInstruction(Request("choose-cart", actor, "Please pull my cart"));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var walking = world.ExportState();
        Assert.Equal("a-cart", Assert.Single(walking.Instructions!).Order!.TargetCartLotId);
        Assert.Empty(walking.HandcartHitches!);
        var inventory = walking.Society.Society.Inventory with
        {
            Lots = walking.Society.Society.Inventory.Lots.Select(lot => lot.Id == "a-cart"
                ? lot with { OwnerId = state.Inhabitants[1].InhabitantId } : lot).ToArray(),
        };
        using var transferred = PrivateWorldRuntime.Restore(walking with
        { Society = walking.Society with { Society = walking.Society.Society with { Inventory = inventory } } }, _ => new IdleProvider());
        Assert.True((await transferred.AdvanceOneTickAsync()).Advanced);
        var order = Assert.Single(transferred.ExportState().Instructions!).Order!;
        Assert.Equal("blocked", order.Status);
        Assert.Equal("a-cart", order.TargetCartLotId);
        Assert.Equal(0, order.CompletedUnits);
        Assert.Empty(transferred.ExportState().HandcartHitches!);
        Assert.Equal(actor, transferred.Society.Inventory.GetLot("z-cart").OwnerId);
    }

    [Fact]
    public async Task CartTargetsAndCompletionReceiptsRejectMalformedSavedOrders()
    {
        var state = await CartState();
        var actor = state.Inhabitants[0].InhabitantId;
        using var world = PrivateWorldRuntime.Restore(state, _ => new IdleProvider());
        world.SubmitInstruction(Request("validate-cart", actor, "Attach cart z-cart"));
        var queued = world.ExportState();
        foreach (var target in new[] { "cargo", "missing-cart", " z-cart", "z-cart\n" })
        {
            var malformed = queued with
            {
                Instructions = queued.Instructions!.Select(item => item with
                { Order = item.Order! with { TargetCartLotId = target } }).ToArray()
            };
            Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(malformed));
        }
        for (var tick = 0; tick < 8 && world.ExportState().Instructions![0].Order!.Status != "finished"; tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var finished = world.ExportState();
        Assert.Equal("finished", finished.Instructions![0].Order!.Status);
        var forged = finished with
        {
            Instructions = finished.Instructions.Select(item => item with
            { Order = item.Order! with { LastEffectId = "cart-order:" + new string('0', 64) } }).ToArray()
        };
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(forged));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CartTileTargetBindsTheUniqueOwnedCartAndRefusesAmbiguousSelection(bool ambiguous)
    {
        var state = await CartState();
        var actor = state.Inhabitants[0].InhabitantId;
        var position = state.Society.Society.Inventory.GetLot("z-cart").GroundPosition!.Value;
        if (!ambiguous) state = state with
        {
            Society = state.Society with
            {
                Society = state.Society.Society with
                {
                    Inventory = state.Society.Society.Inventory with
                    {
                        Lots = state.Society.Society.Inventory.Lots.Select(lot => lot.Id == "a-cart"
                        ? lot with { GroundPosition = new(state.Inhabitants[0].Position.X, state.Inhabitants[0].Position.Y) } : lot).ToArray()
                    },
                }
            },
        };
        using var world = PrivateWorldRuntime.Restore(state, _ => new IdleProvider());
        world.SubmitInstruction(Request("cart-tile", actor, $"Attach my cart at ({position.X}, {position.Y})"));
        var submitted = Assert.Single(world.ExportState().Instructions!).Order!;
        if (ambiguous)
        {
            Assert.Equal("not_understood", submitted.Status);
            Assert.Null(submitted.TargetCartLotId);
            return;
        }
        Assert.Equal("z-cart", submitted.TargetCartLotId);
        using var loaded = PrivateWorldRuntime.Restore(world.ExportState(), _ => new IdleProvider());
        for (var tick = 0; tick < 8 && loaded.ExportState().HandcartHitches!.Count == 0; tick++)
            Assert.True((await loaded.AdvanceOneTickAsync()).Advanced);
        Assert.Equal("z-cart", Assert.Single(loaded.ExportState().HandcartHitches!).CartLotId);
        Assert.Equal("finished", Assert.Single(loaded.ExportState().Instructions!).Order!.Status);
    }

    [Fact]
    public async Task UrgentFoodInterruptsCartAttachmentThenResumesTheSameTarget()
    {
        var state = await CartState();
        var actor = state.Inhabitants[0].InhabitantId;
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "cart-emergency-food", "berries", actor, 4);
        state = state with
        {
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { HungerBasisPoints = 0 } : person).ToArray(),
        };
        using var world = PrivateWorldRuntime.Restore(state, _ => new IdleProvider());
        world.SubmitInstruction(Request("hungry-cart", actor, "Pull cart z-cart"));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var interrupted = Assert.Single(world.ExportState().Instructions!).Order!;
        Assert.Equal("interrupted", interrupted.Status);
        Assert.Equal("z-cart", interrupted.TargetCartLotId);
        Assert.Equal(0, interrupted.CompletedUnits);
        Assert.Empty(world.ExportState().HandcartHitches!);
        using var loaded = PrivateWorldRuntime.Restore(world.ExportState(), _ => new IdleProvider());
        for (var tick = 0; tick < 12 && loaded.ExportState().HandcartHitches!.Count == 0; tick++)
            Assert.True((await loaded.AdvanceOneTickAsync()).Advanced);
        Assert.Equal("z-cart", Assert.Single(loaded.ExportState().HandcartHitches!).CartLotId);
        Assert.Equal(1, Assert.Single(loaded.ExportState().Instructions!).Order!.CompletedUnits);
        Assert.True(loaded.Society.Inventory.GetLot("cart-emergency-food").Quantity < 4);
    }

    private static async Task<PrivateWorldRuntimeState> CartState()
    {
        var state = PrivateWorldRuntimeCodec.Decode(await Fixture.Value);
        var actor = state.Inhabitants[0].InhabitantId;
        var position = state.Map.FootNeighbors(state.Inhabitants[0].Position)
            .First(point => state.Map.IsPassable(point) && state.Inhabitants.All(person => person.Position != point));
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "a-cart", "handcart", actor, 1,
            groundPosition: new(position.X, position.Y));
        inventory = InventoryFixture.AddLot(inventory, "z-cart", "handcart", actor, 1,
            groundPosition: new(position.X, position.Y));
        inventory = InventoryFixture.AddLot(inventory, "cargo", "stone", actor, 4, containerLotId: "z-cart");
        return state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } };
    }

    private static OwnerInstructionRequest Request(string key, string actor, string text, bool queue = false) =>
        new(key, "owner:test", actor, OwnerInstructionKind.MustDo, text, queue);

    private sealed class IdleProvider : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default) =>
            new DeterministicDecisionProvider().DecideAsync(request with
            { Observation = request.Observation with { Candidates = [request.Observation.Candidates.Single(item => item.Id == "safe_idle")] } }, cancellationToken);
    }
}

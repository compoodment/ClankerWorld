using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed class PrivateWorldCartCargoOrderTests
{
    private static readonly Lazy<Task<byte[]>> Fixture = new(async () =>
    {
        using var world = NormalPathWorld.CreateGenerated("cart-cargo-orders", _ => new IdleProvider());
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        return PrivateWorldRuntimeCodec.Encode(SettlementWeatherTestFixture.WithWeather(world.ExportState(), WeatherKind.Clear));
    });

    [Theory]
    [InlineData("Load 3 wood into my cart z-cart", false, 3, false)]
    [InlineData("Load 3 wood into my cart z-cart", false, 3, true)]
    [InlineData("Unload 2 wood from my cart z-cart", true, 2, false)]
    [InlineData("Unload 2 wood from my cart z-cart onto the ground", true, 2, false)]
    public async Task ExactQuantityUsesSelectedCartAndSurvivesRollbackReloadAndReplay(string text, bool loaded, int moved, bool approach)
    {
        var state = await CargoState(loaded);
        var actor = state.Inhabitants[0].InhabitantId;
        if (approach)
        {
            var site = state.Map.FootNeighbors(state.Inhabitants[0].Position)
                .First(point => state.Map.IsPassable(point) && state.Inhabitants.All(person => person.Position != point));
            state = WithInventory(state, state.Society.Society.Inventory with
            {
                Lots = state.Society.Society.Inventory.Lots.Select(lot => lot.Id is "z-cart" or "cargo"
                ? lot with { GroundPosition = new(site.X, site.Y) } : lot).ToArray()
            });
        }
        using var world = PrivateWorldRuntime.Restore(state, _ => new IdleProvider());
        var receipt = world.SubmitInstruction(Request("cargo", actor, text));
        var submitted = Assert.Single(world.ExportState().Instructions!).Order!;
        Assert.Equal("z-cart", submitted.TargetCartLotId);
        Assert.Equal(moved, submitted.RequestedUnits);
        Assert.Equal("wood", submitted.TargetItemKind);
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), _ => new IdleProvider());
        Assert.False((await world.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        for (var tick = 0; tick < (approach ? 8 : 4); tick++)
        {
            var before = world.Inhabitants[0].Position;
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
            var after = world.Inhabitants[0].Position;
            if (before != after) Assert.True(state.Map.CanFootStep(before, after));
            if (Assert.Single(world.ExportState().Instructions!).Order!.CompletedUnits > 0)
                Assert.Equal(new InventoryGroundPosition(after.X, after.Y), world.Society.Inventory.GetLot("z-cart").GroundPosition);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        }
        var order = Assert.Single(world.ExportState().Instructions!).Order!;
        Assert.Equal("finished", order.Status);
        Assert.Equal(moved, order.CompletedUnits);
        Assert.Equal(loaded ? 4 - moved : moved, CartCargo(world, "z-cart"));
        Assert.Equal(0, CartCargo(world, "a-cart"));
        Assert.Equal(4, world.Society.Inventory.Lots.Where(lot => lot.ItemKind == "wood").Sum(lot => lot.Quantity));
        Assert.Single(world.ExportState().Events, item => item.Kind == (loaded ? "handcart_unloaded" : "handcart_loaded"));
        Assert.Equal(receipt, world.SubmitInstruction(Request("cargo", actor, text)));
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())), _ => new IdleProvider());
        Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(moved, Assert.Single(restored.ExportState().Instructions!).Order!.CompletedUnits);
        if (text.EndsWith("ground", StringComparison.Ordinal))
            Assert.Equal(moved, world.Society.Inventory.Lots.Where(lot => lot.ItemKind == "wood" && lot.GroundPosition is not null).Sum(lot => lot.Quantity));
        else if (loaded)
            Assert.Equal(moved, world.Society.Inventory.Lots.Where(lot => lot.ItemKind == "wood" && PersonalEquipmentRules.IsCarried(lot, actor)).Sum(lot => lot.Quantity));
    }

    [Theory]
    [InlineData("resume")]
    [InlineData("cancel")]
    [InlineData("replace")]
    public async Task PartialUnloadingRetainsItsSourceAndProgressWhenReloadedOrStopped(string next)
    {
        var state = await CargoState(true);
        var actor = state.Inhabitants[0].InhabitantId;
        state = WithInventory(state, InventoryFixture.AddLot(state.Society.Society.Inventory, "ballast", "fiber", actor, 6));
        using var world = PrivateWorldRuntime.Restore(state, _ => new IdleProvider());
        var receipt = world.SubmitInstruction(Request("partial", actor, "Unload 3 wood from cart z-cart"));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(2, Assert.Single(world.ExportState().Instructions!).Order!.CompletedUnits);
        Assert.Equal(2, CartCargo(world, "z-cart"));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var partial = world.ExportState();
        var order = Assert.Single(partial.Instructions!).Order!;
        Assert.Equal("blocked", order.Status);
        Assert.Contains("carrying space", order.BlockedReason!);
        Assert.Equal("cargo", order.TargetLotId);
        Assert.Equal(2, order.CompletedUnits);
        using var loaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(partial)), _ => new IdleProvider());
        if (next == "cancel") loaded.CancelOrder(new("cancel", "owner:test", partial.Society.Society.WorldId, actor, receipt.InstructionId));
        if (next == "replace") loaded.SubmitInstruction(Request("replacement", actor, "Park cart a-cart"));
        if (next == "resume")
        {
            var inventory = loaded.ExportState().Society.Society.Inventory;
            using var freed = PrivateWorldRuntime.Restore(WithInventory(loaded.ExportState(), inventory with
            { Lots = inventory.Lots.Where(lot => lot.Id != "ballast").ToArray() }), _ => new IdleProvider());
            Assert.True((await freed.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(3, Assert.Single(freed.ExportState().Instructions!).Order!.CompletedUnits);
            Assert.Equal("finished", Assert.Single(freed.ExportState().Instructions!).Order!.Status);
            Assert.Equal(1, CartCargo(freed, "z-cart"));
            Assert.Equal(2, freed.ExportState().Events.Count(item => item.Kind == "handcart_unloaded"));
        }
        else
        {
            Assert.True((await loaded.AdvanceOneTickAsync()).Advanced);
            var cancelled = loaded.ExportState().Instructions!.Single(item => item.InstructionId == receipt.InstructionId).Order!;
            Assert.Equal("cancelled", cancelled.Status);
            Assert.Equal("cargo", cancelled.TargetLotId);
            Assert.Equal(2, cancelled.CompletedUnits);
            Assert.Equal(2, CartCargo(loaded, "z-cart"));
            Assert.Single(loaded.ExportState().Events, item => item.Kind == "handcart_unloaded");
            using var stopped = PrivateWorldRuntime.Restore(loaded.ExportState(), _ => new IdleProvider());
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(loaded.ExportState()), PrivateWorldRuntimeCodec.Encode(stopped.ExportState()));
        }
    }

    [Fact]
    public async Task PartialLoadingCountsAvailableSpaceAndResumesItsBoundStackAfterUrgentFood()
    {
        var state = await CargoState(false);
        var actor = state.Inhabitants[0].InhabitantId;
        state = WithInventory(state, InventoryFixture.AddLot(state.Society.Society.Inventory, "cart-ballast", "fiber", actor, 30, containerLotId: "z-cart"));
        using var world = PrivateWorldRuntime.Restore(state, _ => new IdleProvider());
        world.SubmitInstruction(Request("partial-load", actor, "Load 3 wood into cart z-cart"));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(2, Assert.Single(world.ExportState().Instructions!).Order!.CompletedUnits);
        Assert.Equal("cargo", Assert.Single(world.ExportState().Instructions!).Order!.TargetLotId);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Contains("space in", Assert.Single(world.ExportState().Instructions!).Order!.BlockedReason!);
        var partial = world.ExportState();
        var inventory = partial.Society.Society.Inventory;
        inventory = inventory with { Lots = inventory.Lots.Where(lot => lot.Id != "cart-ballast").ToArray() };
        inventory = InventoryFixture.AddLot(inventory, "emergency-food", "berries", actor, 4);
        partial = WithInventory(partial, inventory) with
        { Inhabitants = partial.Inhabitants.Select(person => person.InhabitantId == actor ? person with { HungerBasisPoints = 0 } : person).ToArray() };
        using var hungry = PrivateWorldRuntime.Restore(partial, _ => new IdleProvider());
        Assert.True((await hungry.AdvanceOneTickAsync()).Advanced);
        Assert.Equal("interrupted", Assert.Single(hungry.ExportState().Instructions!).Order!.Status);
        Assert.Equal(2, Assert.Single(hungry.ExportState().Instructions!).Order!.CompletedUnits);
        Assert.Equal("cargo", Assert.Single(hungry.ExportState().Instructions!).Order!.TargetLotId);
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(hungry.ExportState())), _ => new IdleProvider());
        for (var tick = 0; tick < 12 && restored.ExportState().Instructions![0].Order!.Status != "finished"; tick++)
            Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(3, Assert.Single(restored.ExportState().Instructions!).Order!.CompletedUnits);
        Assert.Equal("finished", Assert.Single(restored.ExportState().Instructions!).Order!.Status);
        Assert.Equal(3, CartCargo(restored, "z-cart"));
        Assert.Single(restored.Society.Inventory.Lots, lot => lot.Id == "cargo" && lot.Quantity == 1 && lot.ContainerLotId is null);
    }

    [Theory]
    [InlineData(false, "foreign-stock")]
    [InlineData(false, "carried-stock")]
    [InlineData(false, "reserved-stock")]
    [InlineData(false, "broken-cart")]
    [InlineData(false, "missing-goods")]
    [InlineData(false, "stored-personal-stock")]
    [InlineData(true, "foreign-cart")]
    [InlineData(true, "reserved-cargo")]
    [InlineData(true, "reserved-other-cargo")]
    public async Task NativeAccessAndReservationGuardsBlockWithoutFalseProgress(bool loaded, string blocker)
    {
        var state = await CargoState(loaded);
        var actor = state.Inhabitants[0].InhabitantId;
        var other = state.Inhabitants[1].InhabitantId;
        var inventory = state.Society.Society.Inventory;
        inventory = inventory with
        {
            Lots = inventory.Lots.Select(lot =>
            blocker == "foreign-cart" && (lot.Id == "z-cart" || lot.Id == "cargo") || blocker == "foreign-stock" && lot.Id == "cargo"
                ? lot with { OwnerId = other } :
            blocker == "carried-stock" && lot.Id == "cargo" ? lot with { GroundPosition = null, CarrierId = other } :
            blocker == "broken-cart" && lot.Id == "z-cart" ? lot with { ConditionBasisPoints = 0 } :
            blocker == "missing-goods" && lot.Id == "cargo" ? lot with { ItemKind = "stone" } : lot).ToArray()
        };
        if (blocker == "stored-personal-stock")
        {
            var household = state.Society.Society.GetInhabitant(actor).HouseholdId;
            var house = state.WorldSimulation!.Buildings.First(building => building.HouseholdId == household &&
                state.WorldContent!.Buildings.Any(definition => definition.CanonicalId == building.DefinitionId && HouseholdBuildingKinds.KindOf(definition) == "house"));
            inventory = inventory with
            {
                Lots = inventory.Lots.Select(lot => lot.Id == "cargo"
                ? lot with { StorageBuildingId = house.InstanceId, GroundPosition = null }
                : lot.ItemKind == "handcart" ? lot with { GroundPosition = new(house.Position.X, house.Position.Y) } : lot).ToArray()
            };
            state = state with { Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor ? person with { Position = house.Position } : person).ToArray() };
        }
        if (blocker == "reserved-other-cargo")
            inventory = InventoryFixture.AddLot(inventory, "held-other-cargo", "fiber", actor, 1, containerLotId: "z-cart");
        if (blocker.StartsWith("reserved", StringComparison.Ordinal)) inventory = InventoryFixture.Reserve(inventory, "held", actor,
            blocker == "reserved-other-cargo" ? "held-other-cargo" : "cargo", blocker == "reserved-stock" ? 4 : 1, "other_work", inventory.WorldTick + 1000);
        using var world = PrivateWorldRuntime.Restore(WithInventory(state, inventory), _ => new IdleProvider());
        world.SubmitInstruction(Request("guard", actor, loaded ? "Unload 2 wood from cart z-cart onto the ground" : "Load 3 wood into cart z-cart"));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var order = Assert.Single(world.ExportState().Instructions!).Order!;
        Assert.Equal("blocked", order.Status);
        Assert.Equal(0, order.CompletedUnits);
        Assert.Equal("z-cart", order.TargetCartLotId);
        Assert.NotNull(order.BlockedReason);
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind is "handcart_loaded" or "handcart_unloaded");
        Assert.Equal(4, world.Society.Inventory.GetLot("cargo").Quantity);
        if (blocker.StartsWith("reserved", StringComparison.Ordinal)) Assert.Equal(InventoryReservationState.Reserved, world.Society.Inventory.GetReservation("held").State);
        using var restored = PrivateWorldRuntime.Restore(world.ExportState(), _ => new IdleProvider());
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BrokenCartRecoveryPreservesPhysicallyDamagedCargo(bool spoiledFood)
    {
        var state = await CargoState(true);
        var actor = state.Inhabitants[0].InhabitantId;
        state = WithInventory(state, state.Society.Society.Inventory with
        { Lots = state.Society.Society.Inventory.Lots.Select(lot => lot.Id is "z-cart" or "cargo" ? lot with { ConditionBasisPoints = 0, ItemKind = lot.Id == "cargo" && spoiledFood ? "berries" : lot.ItemKind, FreshnessBasisPoints = lot.Id == "cargo" && spoiledFood ? 0 : lot.FreshnessBasisPoints } : lot).ToArray() });
        using var world = PrivateWorldRuntime.Restore(state, _ => new IdleProvider());
        world.SubmitInstruction(Request("recover", actor, spoiledFood ? "Unload 2 berries from cart z-cart onto the ground" : "Unload 2 wood from cart z-cart onto the ground"));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal("finished", Assert.Single(world.ExportState().Instructions!).Order!.Status);
        Assert.Equal(2, CartCargo(world, "z-cart"));
        var family = world.Society.Inventory.Lots.Where(lot => lot.ItemKind == (spoiledFood ? "berries" : "wood")).ToArray();
        Assert.Equal(4, family.Sum(lot => lot.Quantity));
        Assert.All(family, lot => { Assert.Equal(0, lot.ConditionBasisPoints); Assert.Equal(actor, lot.OwnerId); });
        Assert.Equal(0, world.Society.Inventory.GetLot("z-cart").ConditionBasisPoints);
        using var restored = PrivateWorldRuntime.Restore(world.ExportState(), _ => new IdleProvider());
        restored.Validate();
    }

    [Theory]
    [InlineData("essential_first", 1, 0)]
    [InlineData("open", 1, 1)]
    [InlineData("essential_first", 6, 2)]
    public async Task HouseholdFoodPolicyLimitsActualLoadingAndFilledVesselsStaySeparate(string policy, int stock, int allowed)
    {
        var state = await CargoState(false);
        var actor = state.Inhabitants[0].InhabitantId;
        var household = state.Society.Society.GetInhabitant(actor).HouseholdId!;
        var house = state.WorldSimulation!.Buildings.First(building => building.HouseholdId == household &&
            state.WorldContent!.Buildings.Any(definition => definition.CanonicalId == building.DefinitionId && HouseholdBuildingKinds.KindOf(definition) == "house"));
        var inventory = state.Society.Society.Inventory;
        inventory = inventory with
        {
            Lots = inventory.Lots.Where(lot => lot.Id != "cargo").Select(lot =>
            lot.ItemKind == "handcart" ? lot with { GroundPosition = new(house.Position.X, house.Position.Y) } : lot).ToArray()
        };
        inventory = InventoryFixture.AddLot(inventory, "protected-food", "berries", household, stock, groundPosition: new(house.Position.X, house.Position.Y));
        inventory = InventoryFixture.AddLot(inventory, "filled-pot", "storage_pot", actor, 1, groundPosition: new(house.Position.X, house.Position.Y));
        inventory = InventoryFixture.AddLot(inventory, "pot-food", "berries", actor, 2, containerLotId: "filled-pot");
        state = WithInventory(state, inventory) with
        {
            Council = new(actor, policy, inventory.WorldTick),
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
            ? person with { Position = house.Position, HungerBasisPoints = 6_000 } : person).ToArray()
        };
        using var world = PrivateWorldRuntime.Restore(state, _ => new IdleProvider());
        var food = world.SubmitInstruction(Request("food", actor, $"Load {(stock == 6 ? 3 : 1)} berries into cart z-cart"));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var status = allowed == 1 ? "finished" : "blocked";
        Assert.Equal(status, Assert.Single(world.ExportState().Instructions!).Order!.Status);
        Assert.Equal(allowed, Assert.Single(world.ExportState().Instructions!).Order!.CompletedUnits);
        world.SubmitInstruction(Request("pot", actor, "Load 1 storage pot into cart z-cart"));
        var instructions = world.ExportState().Instructions!;
        Assert.Equal("not_understood", instructions.Single(item => item.IdempotencyKey == "pot").Order!.Status);
        Assert.Equal(status, instructions.Single(item => item.InstructionId == food.InstructionId).Order!.Status);
        Assert.Equal(allowed, CartCargo(world, "z-cart"));
        Assert.Equal(0, CartCargo(world, "a-cart"));
        var source = world.Society.Inventory.GetLot("protected-food");
        Assert.Equal(stock == allowed ? actor : household, source.OwnerId);
        Assert.Equal(stock == allowed ? stock : stock - allowed, source.Quantity);
        Assert.All(world.Society.Inventory.Lots.Where(lot => lot.ContainerLotId == "z-cart"), lot => Assert.Equal(actor, lot.OwnerId));
        Assert.Equal("filled-pot", world.Society.Inventory.GetLot("pot-food").ContainerLotId);
    }

    [Fact]
    public async Task QuantityCanSpanRealStacksButMalformedSavedBindingsAndReceiptsAreRefused()
    {
        var state = await CargoState(false);
        var actor = state.Inhabitants[0].InhabitantId;
        var inventory = state.Society.Society.Inventory;
        inventory = inventory with { Lots = inventory.Lots.Select(lot => lot.Id == "cargo" ? lot with { Quantity = 2 } : lot).ToArray() };
        inventory = InventoryFixture.AddLot(inventory, "next-cargo", "wood", actor, 2, groundPosition: inventory.GetLot("z-cart").GroundPosition);
        using var world = PrivateWorldRuntime.Restore(WithInventory(state, inventory), _ => new IdleProvider());
        world.SubmitInstruction(Request("stacks", actor, "Load 3 wood into cart z-cart"));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(2, Assert.Single(world.ExportState().Instructions!).Order!.CompletedUnits);
        Assert.Null(Assert.Single(world.ExportState().Instructions!).Order!.TargetLotId);
        using var loaded = PrivateWorldRuntime.Restore(world.ExportState(), _ => new IdleProvider());
        Assert.True((await loaded.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(3, Assert.Single(loaded.ExportState().Instructions!).Order!.CompletedUnits);
        Assert.Equal(3, CartCargo(loaded, "z-cart"));
        var completed = loaded.ExportState();
        var valid = Assert.Single(completed.Instructions!).Order!;
        foreach (var invalid in new[] { valid with { CompletedUnits = 2 }, valid with { LastEffectId = "cart-cargo-order:" + new string('0', 64) },
            valid with { TargetItemKind = "storage_pot" }, valid with { TargetLotId = " bad " }, valid with { TargetCartLotId = "cargo" } })
        {
            Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(completed with
            { Instructions = completed.Instructions!.Select(item => item with { Order = invalid }).ToArray() }));
        }
    }

    private static PrivateWorldRuntimeState WithInventory(PrivateWorldRuntimeState state, InventoryCheckpoint inventory) =>
        state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } };

    private static int CartCargo(PrivateWorldRuntime world, string cart) =>
        world.Society.Inventory.Lots.Where(lot => lot.ContainerLotId == cart).Sum(lot => lot.Quantity);

    private static async Task<PrivateWorldRuntimeState> CargoState(bool loaded)
    {
        var state = PrivateWorldRuntimeCodec.Decode(await Fixture.Value);
        var actor = state.Inhabitants[0].InhabitantId;
        var position = state.Inhabitants[0].Position;
        var inventory = state.Society.Society.Inventory with { Lots = [], Reservations = [] };
        inventory = InventoryFixture.AddLot(inventory, "a-cart", "handcart", actor, 1, groundPosition: new(position.X, position.Y));
        inventory = InventoryFixture.AddLot(inventory, "z-cart", "handcart", actor, 1, groundPosition: new(position.X, position.Y));
        inventory = InventoryFixture.AddLot(inventory, "cargo", "wood", actor, 4,
            containerLotId: loaded ? "z-cart" : null, groundPosition: loaded ? null : new(position.X, position.Y));
        return state with
        {
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
            Inhabitants = state.Inhabitants.Select(person => person with
            { HungerBasisPoints = 9_500, Survival = new(), Equipment = null, LastDecisionContext = null, Project = null, TravelCooldownTicks = 0 }).ToArray(),
        };
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

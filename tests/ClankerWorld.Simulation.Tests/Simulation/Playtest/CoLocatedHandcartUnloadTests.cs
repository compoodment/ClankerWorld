using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class CoLocatedHandcartUnloadTests
{
    private static readonly Lazy<byte[]> Generated = new(() =>
    {
        using var world = NormalPathWorld.CreateGenerated("co-located-cart-audit", new MarketRulesPolicy().CreateProvider);
        world.AdvanceOneTickAsync().AsTask().GetAwaiter().GetResult();
        return PrivateWorldRuntimeCodec.Encode(world.ExportState());
    });

    [Theory]
    [InlineData(true, "ground", 4)]
    [InlineData(false, "ground", 4)]
    [InlineData(true, "carried", 1)]
    [InlineData(true, "reserved", 0)]
    [InlineData(true, "foreign", 0)]
    [InlineData(true, "distant", 0)]
    public async Task UnloadUsesTheReachedCargosOwnCartRegardlessOfAnEmptyCartSortingFirst(bool emptyFirst, string mode, int unloaded)
    {
        var state = PrivateWorldRuntimeCodec.Decode(Generated.Value);
        var actor = state.Inhabitants[0].InhabitantId;
        var household = state.Society.Society.GetInhabitant(actor).HouseholdId;
        var position = state.WorldSimulation!.Buildings.First(building => building.HouseholdId == household &&
            state.WorldContent!.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId).Tags.Contains("house")).Position;
        var loadedPosition = mode == "distant" ? state.Map.FootNeighbors(position).First() : position;
        var owner = mode == "foreign" ? state.Inhabitants[1].InhabitantId : actor;
        var emptyId = emptyFirst ? "a-empty-cart" : "z-empty-cart";
        var inventory = state.Society.Society.Inventory with { Lots = [], Reservations = [] };
        inventory = InventoryFixture.AddLot(inventory, emptyId, "handcart", actor, 1, groundPosition: new(position.X, position.Y));
        inventory = InventoryFixture.AddLot(inventory, "m-loaded-cart", "handcart", owner, 1,
            groundPosition: new(loadedPosition.X, loadedPosition.Y));
        inventory = InventoryFixture.AddLot(inventory, "cart-wood", "wood", owner, 4, containerLotId: "m-loaded-cart");
        if (mode == "carried") inventory = InventoryFixture.AddLot(inventory, "ballast", "fiber", actor, 7);
        if (mode == "reserved") inventory = InventoryFixture.Reserve(inventory, "held-cargo", owner, "cart-wood", 1,
            "pending_trade", inventory.WorldTick + 1_000);
        inventory = inventory with { Lots = inventory.Lots.Select(lot => lot.ItemKind == "handcart" ? lot with { ConditionBasisPoints = 0 } : lot).ToArray() };
        state = state with
        {
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
            Inhabitants = state.Inhabitants.Select(person => person with
            {
                Position = person.InhabitantId == actor ? position : person.Position,
                HungerBasisPoints = 9_500,
                Equipment = null,
                Project = null,
                Survival = new(),
                LastDecisionContext = null,
            }).ToArray(),
        };
        var target = mode == "carried" ? "unload_handcart:cart-wood" : "unload_handcart_ground:cart-wood";
        var choices = Choices(actor, target);
        var bytes = PrivateWorldRuntimeCodec.Encode(state);
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), choices.CreateProvider);
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), Choices(actor, target).CreateProvider);
        world.Validate();
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        Assert.False((await world.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        for (var tick = 0; tick < 12; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
            world.Validate();
            replay.Validate();
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        }
        var cargo = world.Society.Inventory.Lots.Where(lot => lot.Id == "cart-wood" || lot.ProvenanceLotId == "cart-wood").ToArray();
        Assert.Equal(4, cargo.Sum(lot => lot.Quantity));
        Assert.All(cargo, lot => Assert.Equal(owner, lot.OwnerId));
        Assert.Equal(4 - unloaded, cargo.Where(lot => lot.ContainerLotId == "m-loaded-cart").Sum(lot => lot.Quantity));
        Assert.Equal(unloaded > 0, choices.OfferedTo(actor).Any(candidate => candidate.Id == target));
        Assert.Equal(unloaded > 0 ? 1 : 0, world.ExportState().Events.Count(item => item.Kind == "handcart_unloaded"));
        Assert.Equal(mode == "carried" ? unloaded : 0, cargo.Where(lot => PersonalEquipmentRules.IsCarried(lot, actor)).Sum(lot => lot.Quantity));
        Assert.Equal(mode == "carried" ? 0 : unloaded, cargo.Where(lot => lot.GroundPosition == new InventoryGroundPosition(position.X, position.Y)).Sum(lot => lot.Quantity));
        Assert.All(world.Society.Inventory.Lots.Where(lot => lot.ItemKind == "handcart"), cart => Assert.Equal(0, cart.ConditionBasisPoints));
        Assert.Empty(world.ExportState().HandcartHitches!);
        if (mode == "reserved") Assert.Equal(InventoryReservationState.Reserved, world.Society.Inventory.GetReservation("held-cargo").State);
        bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes));
        restored.Validate();
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }

    private static MarketRulesPolicy Choices(string actor, string target) => new()
    {
        Choose = (id, candidates) => id == actor
            ? candidates.FirstOrDefault(candidate => candidate.Id == target) ?? candidates.Single(candidate => candidate.Id == "safe_idle")
            : candidates.Single(candidate => candidate.Id == "safe_idle"),
    };
}

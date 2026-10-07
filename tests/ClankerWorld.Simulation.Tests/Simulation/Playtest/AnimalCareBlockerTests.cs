using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class AnimalPipelineTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CareWithoutPhysicalSuppliesReportsABlocker(bool withSupplies)
    {
        var (state, actor, home, yard) = CreateYard("animal-horse-cargo");
        var day = state.WorldSystems!.Config.TicksPerDay;
        var cow = new AnimalState("care-blocker-cow", "Fern", "cow", "female", -(long)AnimalRules.Definition("cow").AdultDays * day,
            yard.Position, "household:" + home, home, yard.InstanceId);
        var inventory = state.Society.Society.Inventory;
        inventory = inventory with
        {
            Lots = inventory.Lots.Where(lot => lot.OwnerId != actor &&
                (lot.OwnerId != home || !AnimalRules.IsFeed(lot.ItemKind) && lot.ItemKind != "fresh_water")).ToArray()
        };
        if (withSupplies)
        {
            inventory = InventoryFixture.AddLot(inventory, "care-blocker-feed", "grain", actor, 2);
            inventory = InventoryFixture.AddLot(inventory, "care-blocker-jug", "water_jug", actor, 1);
            inventory = InventoryFixture.AddLot(inventory, "care-blocker-water", "fresh_water", actor, 2, containerLotId: "care-blocker-jug");
        }
        state = At(state, actor, yard.Position, inventory, [cow]);
        state = state with { Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor ? person with { Equipment = null } : person).ToArray() };
        var decisions = new CareBlockerChooser();
        IDecisionProvider Provider(string id) => id == actor ? decisions : new AnimalChooser();
        using var preparing = PrivateWorldRuntime.Restore(state, Provider);
        var instruction = preparing.SubmitInstruction(new("care-blocker-order", "owner", actor, OwnerInstructionKind.MustDo, "care for Fern"));
        var bytes = PrivateWorldRuntimeCodec.Encode(preparing.ExportState());
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), Provider);
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), Provider);
        Assert.False((await world.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        for (var tick = 0; tick < 4; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        }
        var order = world.ExportState().Instructions!.Single(item => item.InstructionId == instruction.InstructionId).Order!;
        Assert.Equal(withSupplies ? "finished" : "blocked", order.Status);
        Assert.Equal(withSupplies ? 1 : 0, order.CompletedUnits);
        if (withSupplies)
        {
            Assert.True(world.Animals.Single().CareUntilTick > world.WorldTick);
            Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.Id is "care-blocker-feed" or "care-blocker-water");
        }
        else
        {
            Assert.Equal(0, world.Animals.Single().CareUntilTick);
            Assert.Equal("Waiting for safe feed and jug water to care for Fern.", order.BlockedReason);
            Assert.Empty(world.ExportState().AnimalWorld.SupplyTrips);
            Assert.InRange(decisions.Calls, 0, 4);
        }
        using var loaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())), Provider);
        loaded.Validate();
        world.Validate();
        if (withSupplies) return;

        var blocked = loaded.ExportState();
        Assert.Equal("blocked", blocked.Instructions!.Single(item => item.InstructionId == instruction.InstructionId).Order!.Status);
        var house = blocked.WorldSimulation!.Buildings.First(building => building.HouseholdId == home &&
            blocked.WorldContent!.Buildings.Any(definition => definition.CanonicalId == building.DefinitionId && definition.Tags.Contains("house")));
        var ground = new InventoryGroundPosition(house.Position.X, house.Position.Y);
        inventory = InventoryFixture.AddLot(blocked.Society.Society.Inventory, "retry-care-feed", "grain", home, 4, groundPosition: ground);
        inventory = InventoryFixture.AddLot(inventory, "retry-care-jug", "water_jug", home, 1, groundPosition: ground);
        inventory = InventoryFixture.AddLot(inventory, "retry-care-water", "fresh_water", home, 4, containerLotId: "retry-care-jug");
        blocked = blocked with { Society = blocked.Society with { Society = blocked.Society.Society with { Inventory = inventory } } };
        bytes = PrivateWorldRuntimeCodec.Encode(blocked);
        using var recovering = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), Provider);
        using var recoveringReplay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), Provider);
        Assert.False((await recovering.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(recovering.ExportState()));
        var fetched = false;
        for (var tick = 0; tick < 70 && recovering.ExportState().Instructions!.Single(item => item.InstructionId == instruction.InstructionId).Order!.Status != "finished"; tick++)
        {
            Assert.True((await recovering.AdvanceOneTickAsync()).Advanced);
            Assert.True((await recoveringReplay.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(recovering.ExportState()), PrivateWorldRuntimeCodec.Encode(recoveringReplay.ExportState()));
            if (recovering.ExportState().AnimalWorld.SupplyTrips.Any(trip => trip.ActorId == actor && trip.AnimalId == cow.Id))
            {
                fetched = true;
                Assert.Equal("doing", recovering.ExportState().Instructions!.Single(item => item.InstructionId == instruction.InstructionId).Order!.Status);
            }
        }
        order = recovering.ExportState().Instructions!.Single(item => item.InstructionId == instruction.InstructionId).Order!;
        Assert.Equal(("finished", 1), (order.Status, order.CompletedUnits));
        Assert.True(fetched);
        Assert.True(recovering.Animals.Single().CareUntilTick > recovering.WorldTick);
        Assert.Equal(2, recovering.Society.Inventory.Lots.Where(lot => lot.Id.StartsWith("retry-care-feed", StringComparison.Ordinal)).Sum(lot => lot.Quantity));
        Assert.Equal((home, 2), (recovering.Society.Inventory.GetLot("retry-care-water").OwnerId, recovering.Society.Inventory.GetLot("retry-care-water").Quantity));
        Assert.Equal((home, actor), (recovering.Society.Inventory.GetLot("retry-care-jug").OwnerId, recovering.Society.Inventory.GetLot("retry-care-jug").CarrierId));
        Assert.Equal(recovering.Animals.Single().Position, recovering.Inhabitants.Single(person => person.InhabitantId == actor).Position);
        Assert.Empty(recovering.ExportState().AnimalWorld.SupplyTrips);
        using var recoveredReload = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(recovering.ExportState())), Provider);
        recoveredReload.Validate();
        recovering.Validate();
    }

    private sealed class CareBlockerChooser : IDecisionProvider
    {
        private readonly AnimalChooser inner = new("animal_order");
        public int Calls { get; private set; }
        public DecisionProviderKind Kind => inner.Kind;
        public long ProviderEpoch => inner.ProviderEpoch;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            return inner.DecideAsync(request, cancellationToken);
        }
    }
}

using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class AnimalPipelineTests
{
    [Theory]
    [InlineData("split")]
    [InlineData("combined")]
    [InlineData("carried")]
    [InlineData("partial")]
    [InlineData("split-lots")]
    [InlineData("satisfied-carried")]
    [InlineData("insufficient")]
    [InlineData("reserved")]
    [InlineData("broken")]
    [InlineData("broken-carried")]
    [InlineData("capacity")]
    public async Task CareCollectsTheRequiredWaterAcrossPhysicalJugs(string scenario)
    {
        var (state, actor, home, yard) = CreateYard("animal-split-water-audit");
        var house = state.WorldSimulation!.Buildings.First(building => building.HouseholdId == home &&
            state.WorldContent!.Buildings.Any(definition => definition.CanonicalId == building.DefinitionId && definition.Tags.Contains("house")));
        var ground = new InventoryGroundPosition(house.Position.X, house.Position.Y);
        var day = state.WorldSystems!.Config.TicksPerDay;
        var cow = new AnimalState("split-water-cow", "Moss", "cow", "female",
            -(long)AnimalRules.Definition("cow").AdultDays * day, yard.Position,
            "household:" + home, home, yard.InstanceId);
        var inventory = state.Society.Society.Inventory with
        {
            Lots = state.Society.Society.Inventory.Lots.Where(lot => lot.OwnerId != actor &&
                (lot.OwnerId != home || !AnimalRules.IsFeed(lot.ItemKind) && lot.ItemKind != "fresh_water")).ToArray(),
        };
        inventory = InventoryFixture.AddLot(inventory, "split-feed", "grain", home, 2, groundPosition: ground);
        inventory = InventoryFixture.AddLot(inventory, "split-jug-a", "water_jug", home, 1, groundPosition: ground);
        inventory = InventoryFixture.AddLot(inventory, "split-jug-b", "water_jug", home, 1, groundPosition: ground);
        inventory = InventoryFixture.AddLot(inventory, "split-water-a", "fresh_water", home, scenario is "combined" or "broken-carried" ? 2 : 1,
            containerLotId: "split-jug-a");
        if (scenario is not ("combined" or "insufficient"))
            inventory = InventoryFixture.AddLot(inventory, "split-water-b", "fresh_water", home, scenario == "broken-carried" ? 2 : 1,
                containerLotId: scenario == "split-lots" ? "split-jug-a" : "split-jug-b");
        if (scenario == "reserved")
            inventory = InventoryFixture.Reserve(inventory, "split-water-reservation", home, "split-water-b", 1, "controlled-other-task",
                state.Society.Society.WorldTick + 100);
        if (scenario is "broken" or "broken-carried")
        {
            var brokenJug = scenario == "broken" ? "split-jug-b" : "split-jug-a";
            inventory = inventory with { Lots = inventory.Lots.Select(lot => lot.Id == brokenJug ? lot with { ConditionBasisPoints = 0 } : lot).ToArray() };
        }
        if (scenario == "capacity")
            inventory = InventoryFixture.AddLot(inventory, "split-personal-load", "wood", actor, 3);
        if (scenario == "satisfied-carried")
        {
            inventory = InventoryFixture.AddLot(inventory, "split-jug-c", "water_jug", home, 1, groundPosition: ground);
            inventory = InventoryFixture.AddLot(inventory, "split-water-c", "fresh_water", home, 1, containerLotId: "split-jug-c");
        }
        var carried = scenario switch
        {
            "carried" => new[] { "split-feed", "split-jug-a", "split-jug-b" },
            "satisfied-carried" => new[] { "split-jug-a", "split-jug-b" },
            "partial" or "broken-carried" => new[] { "split-jug-a" },
            _ => [],
        };
        foreach (var id in carried)
            inventory = InventoryFixture.Relocate(inventory, "carry-" + id, id, home, inventory.GetLot(id).Quantity, carrierId: actor);
        var start = scenario == "carried" ? yard.Position : house.Position;
        state = At(state, actor, start, inventory, [cow]);
        state = state with { Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor ? person with { Equipment = null } : person).ToArray() };
        using var world = RestoreYardCareSupply(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)), actor);
        var instruction = world.SubmitInstruction(new("split-water-order", "owner", actor, OwnerInstructionKind.MustDo, "care for Moss"));
        var initialBytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False((await world.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(initialBytes, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        var canCare = scenario is not ("insufficient" or "reserved" or "broken" or "capacity");
        var checkedPartialReload = false;
        byte[]? expectedNext = null;
        for (var tick = 0; tick < (canCare ? 20 : 12) && world.ExportState().Instructions!.Single(item => item.InstructionId == instruction.InstructionId).Order!.Status != "finished"; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            var current = world.ExportState();
            var currentInventory = current.Society.Society.Inventory;
            Assert.InRange(PersonalEquipmentRules.CarriedQuantity(currentInventory, actor, null), 0, 8);
            Assert.All(currentInventory.Lots.Where(lot => lot.Id.StartsWith("split-water-", StringComparison.Ordinal)),
                lot => Assert.Equal(home, lot.OwnerId));
            if (expectedNext is not null)
            {
                Assert.Equal(expectedNext, PrivateWorldRuntimeCodec.Encode(current));
                expectedNext = null;
            }
            if (scenario == "split" && !checkedPartialReload && currentInventory.GetLot("split-jug-a").CarrierId == actor &&
                currentInventory.GetLot("split-jug-b").CarrierId is null)
            {
                checkedPartialReload = true;
                using var partial = RestoreYardCareSupply(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(current)), actor);
                Assert.True((await partial.AdvanceOneTickAsync()).Advanced);
                expectedNext = PrivateWorldRuntimeCodec.Encode(partial.ExportState());
            }
        }
        world.Validate();
        var final = world.ExportState();
        using var loaded = RestoreYardCareSupply(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(final)), actor);
        loaded.Validate();
        var order = final.Instructions!.Single(item => item.InstructionId == instruction.InstructionId).Order!;
        Assert.Equal(canCare ? ("finished", 1) : ("blocked", 0), (order.Status, order.CompletedUnits));
        Assert.Equal(canCare, world.Animals.Single().CareUntilTick > world.WorldTick);
        Assert.Equal(canCare ? 1 : 0, final.Events.Count(item => item.Kind == "animal_cared"));
        Assert.Equal(canCare ? 0 : 2, world.Society.Inventory.Lots.Where(lot => lot.Id.StartsWith("split-feed", StringComparison.Ordinal)).Sum(lot => lot.Quantity));
        var initialWater = scenario switch { "insufficient" => 1, "satisfied-carried" => 3, "broken-carried" => 4, _ => 2 };
        Assert.Equal(initialWater - (canCare ? 2 : 0),
            world.Society.Inventory.Lots.Where(lot => lot.Id.StartsWith("split-water-", StringComparison.Ordinal)).Sum(lot => lot.Quantity));
        foreach (var id in new[] { "split-jug-a", "split-jug-b" })
            Assert.Equal((home, 1), (world.Society.Inventory.GetLot(id).OwnerId, world.Society.Inventory.GetLot(id).Quantity));
        if (scenario == "split")
        {
            Assert.True(checkedPartialReload);
            Assert.Null(expectedNext);
            Assert.Equal(actor, world.Society.Inventory.GetLot("split-jug-a").CarrierId);
            Assert.Equal(actor, world.Society.Inventory.GetLot("split-jug-b").CarrierId);
        }
        if (scenario is "reserved" or "broken" or "capacity")
            AssertUnmovedLot("split-jug-b");
        if (scenario == "reserved")
            Assert.Equal(inventory.Reservations.Single(item => item.Id == "split-water-reservation"),
                world.Society.Inventory.Reservations.Single(item => item.Id == "split-water-reservation"));
        if (scenario == "satisfied-carried")
        {
            AssertUnmovedLot("split-jug-c");
            AssertUnmovedLot("split-water-c");
        }
        if (scenario == "broken-carried")
        {
            AssertUnmovedLot("split-jug-a");
            AssertUnmovedLot("split-water-a");
            Assert.Equal(actor, world.Society.Inventory.GetLot("split-jug-b").CarrierId);
        }
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.True((await loaded.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(loaded.ExportState()));

        void AssertUnmovedLot(string id)
        {
            var actual = final.Society.Society.Inventory.GetLot(id);
            Assert.Equal(inventory.GetLot(id) with { LastProcessedTick = actual.LastProcessedTick }, actual);
        }
    }
}

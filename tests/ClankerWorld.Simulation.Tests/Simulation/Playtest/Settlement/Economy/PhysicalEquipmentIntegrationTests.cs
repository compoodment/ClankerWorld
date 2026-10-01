using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class PhysicalEquipmentIntegrationTests
{
    [Fact]
    public async Task ASharedHoeIsOfferedOnlyAfterRealDeliveryFreesOneCargoUnit()
    {
        const string household = "household:camp-alpha";
        const string house = "first-town-house-a";
        using var generated = NormalPathWorld.CreateGenerated("full-carrier-field-tool",
            _ => new ActionCoverageRecorder(chooseIdle: true));
        var state = generated.ExportState();
        var farmhouse = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == "first-town-farmhouse");
        Assert.Equal(household, farmhouse.HouseholdId);
        var actor = state.Society.Society.Inhabitants.First(person => person.HouseholdId == household).Id;
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "shared-field-hoe", "wooden_hoe",
            household, 1, storageBuildingId: farmhouse.InstanceId);
        inventory = InventoryFixture.AddLot(inventory, "full-field-load", "wood", actor, 8);
        state = state with
        {
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { HungerBasisPoints = 9_000 } : person).ToArray(),
        };
        var fullRecorder = new ActionCoverageRecorder(chooseIdle: true);
        using var full = PrivateWorldRuntime.Restore(state, _ => fullRecorder);
        Assert.True((await full.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(0, PersonalEquipmentRules.FreeCapacity(full.Society.Inventory, actor, null));
        Assert.DoesNotContain(fullRecorder.OfferedByAgent[actor].Keys,
            candidate => candidate.StartsWith("farm:hoe:", StringComparison.Ordinal));
        Assert.Equal((household, 1, farmhouse.InstanceId),
            (full.Society.Inventory.GetLot("shared-field-hoe").OwnerId,
                full.Society.Inventory.GetLot("shared-field-hoe").Quantity,
                full.Society.Inventory.GetLot("shared-field-hoe").StorageBuildingId));

        state = full.ExportState();
        inventory = InventoryFixture.Transfer(full.Society.Inventory, "real-field-room-delivery", actor, household,
            "full-field-load", 1, "household_stock_delivered", house);
        state = state with
        {
            Society = state.Society with
            { Society = state.Society.Society with { Inventory = inventory } }
        };
        var availableRecorder = new ActionCoverageRecorder(chooseIdle: true);
        using var available = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(
            PrivateWorldRuntimeCodec.Encode(state)), _ => availableRecorder);
        Assert.True((await available.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(1, PersonalEquipmentRules.FreeCapacity(available.Society.Inventory, actor, null));
        Assert.Contains("farm:hoe:wooden_hoe", availableRecorder.OfferedByAgent[actor].Keys);
        Assert.Equal(8, available.Society.Inventory.Lots.Where(lot => lot.Id == "full-field-load" ||
            lot.ProvenanceLotId == "full-field-load").Sum(lot => lot.Quantity));
        Assert.DoesNotContain(available.Society.Inventory.Lots, lot => lot.OwnerId == actor && lot.ItemKind == "wooden_hoe");
    }

    [Fact]
    public void VesselContentsCountOnceWhileTransportCargoCannotFillPersonalEquipmentSlots()
    {
        var inventory = InventoryFixture.CreateGenesis([
            new("coat", "leather_coat", "person", 1, 10_000, 10_000, 0),
            new("aid", "leather_satchel", "person", 1, 10_000, 10_000, 0),
            new("jug", "water_jug", "person", 1, 10_000, 10_000, 0, ContainerCapacity: 8),
            new("water", "water", "person", 8, 10_000, 10_000, 0, ContainerLotId: "jug"),
            new("sword", "sword", "person", 1, 10_000, 10_000, 0),
            new("cart-sack", "sack", "person", 1, 10_000, 10_000, 0,
                GroundPosition: new(2, 3), CartId: "cart"),
            new("horse-coat", "padded_coat", "person", 1, 10_000, 10_000, 0,
                GroundPosition: new(4, 3), AnimalId: "horse"),
        ]);
        var equipment = new PersonalEquipment("coat", "aid", WeaponLotId: "sword");
        Assert.Equal(24, PersonalEquipmentRules.Capacity(inventory, "person", equipment));
        Assert.Equal(10, PersonalEquipmentRules.CarriedQuantity(inventory, "person", equipment));
        Assert.Equal(14, PersonalEquipmentRules.FreeCapacity(inventory, "person", equipment));
        Assert.True(PersonalEquipmentRules.IsCarried(inventory.GetLot("water"), "person"));
        Assert.False(PersonalEquipmentRules.IsCarriedRoot(inventory.GetLot("water"), "person"));
        Assert.Null(PersonalEquipmentRules.EquippedUnit(inventory, "person", "cart-sack"));
        Assert.Null(PersonalEquipmentRules.EquippedUnit(inventory, "person", "horse-coat"));
        Assert.Null(PersonalEquipmentRules.EquippedUnit(inventory, "person", "water"));
    }

    [Fact]
    public void OneSavedEquipmentModelRetainsAllSixSlotsAndRefusesReservedOrContainedGarments()
    {
        using var generated = NormalPathWorld.CreateGenerated("physical-six-slot-equipment",
            _ => new ActionCoverageRecorder(chooseIdle: true));
        var state = generated.ExportState();
        var actor = state.Inhabitants[0].InhabitantId;
        var inventory = state.Society.Society.Inventory;
        foreach (var (id, kind) in new[] { ("own-coat", "leather_coat"), ("own-aid", "leather_satchel"),
            ("own-weapon", "sword"), ("own-shield", "shield"), ("own-armor", "basic_armor"),
            ("own-ornament", "gold_ornament") })
            inventory = InventoryFixture.AddLot(inventory, id, kind, actor, 1);
        var equipment = new PersonalEquipment("own-coat", "own-aid", WeaponLotId: "own-weapon",
            ShieldLotId: "own-shield", ArmorLotId: "own-armor", OrnamentLotId: "own-ornament");
        state = state with
        {
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { Equipment = equipment } : person).ToArray(),
        };
        var saved = PrivateWorldRuntimeCodec.Encode(state);
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved));
        Assert.Equal(equipment, restored.Inhabitants.Single(person => person.InhabitantId == actor).Equipment);
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));

        var reserved = InventoryFixture.Reserve(inventory, "garment-reservation", actor, "own-coat", 1,
            "trade", state.Society.Society.WorldTick + 10);
        var invalid = state with
        {
            Society = state.Society with
            { Society = state.Society.Society with { Inventory = reserved } }
        };
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(invalid));
        inventory = InventoryFixture.AddLot(inventory, "gear-container", "storage_pot", actor, 1, containerCapacity: 8);
        inventory = inventory with
        {
            Lots = inventory.Lots.Select(lot => lot.Id == "own-coat"
            ? lot with { ContainerLotId = "gear-container" } : lot).ToArray()
        };
        invalid = state with
        {
            Society = state.Society with
            { Society = state.Society.Society with { Inventory = inventory } }
        };
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(invalid));
    }
}

using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class EquipmentSchemaTests
{
    [Theory]
    [InlineData("clothing", 31)]
    [InlineData("spear", 31)]
    [InlineData("clothing", 32)]
    [InlineData("spear", 32)]
    [InlineData("clothing", 33)]
    [InlineData("spear", 33)]
    [InlineData("clothing", 34)]
    [InlineData("spear", 34)]
    [InlineData("clothing", 36)]
    [InlineData("spear", 36)]
    [InlineData("clothing", 37)]
    [InlineData("spear", 37)]
    public void CurrentEquipmentRoundTripsWhileOlderAlphaFormatsAreRefused(string kind, int schema)
    {
        using var initial = new PrivateWorldRuntime("equipment-version");
        var state = initial.ExportState();
        var actor = state.Inhabitants[0].InhabitantId;
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "versioned-gear", kind, actor, 1);
        state = state with
        {
            SchemaVersion = schema,
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor ? person with
            { Equipment = kind == "clothing" ? new("versioned-gear", null) : new(null, null, WeaponLotId: "versioned-gear") } : person).ToArray()
        };
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(state));
        using var current = PrivateWorldRuntime.Restore(state with { SchemaVersion = PrivateWorldRuntime.StateSchemaVersion });
        current.Validate();
    }

    [Fact]
    public void CurrentAlphaKeepsCanonicalGroundStockAndRequiresExplicitFields()
    {
        using var initial = new PrivateWorldRuntime("ground-format");
        var state = initial.ExportState();
        var household = state.Society.Society.GetInhabitant(state.Inhabitants[0].InhabitantId).HouseholdId!;
        var point = state.Map.Tiles.First(tile => state.Map.IsBuildable(tile.Position) &&
            !state.Map.Resources.Any(resource => resource.Position == tile.Position) &&
            !state.Map.CampObjects.Any(item => item.Position == tile.Position)).Position;
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "canonical-ground-wood", "wood", household, 2,
            groundPosition: new(point.X, point.Y));
        state = state with
        {
            SchemaVersion = PrivateWorldRuntime.StateSchemaVersion,
            Fields = [],
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
        };
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)));
        var saved = restored.ExportState();
        Assert.Equal(PrivateWorldRuntime.StateSchemaVersion, saved.SchemaVersion);
        Assert.Empty(saved.Fields!);
        Assert.Equal(inventory.GetLot("canonical-ground-wood"), restored.Society.Inventory.GetLot("canonical-ground-wood"));
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(state with { SchemaVersion = 33 }));
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(state with { SchemaVersion = 34 }));
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(state with { Fields = null }));
        restored.Validate();
    }

    [Fact]
    public void CurrentAlphaKeepsSavedSkillsWithoutPhysicalEquipment()
    {
        using var initial = new PrivateWorldRuntime("skills-version");
        var state = initial.ExportState();
        var actor = state.Inhabitants[0].InhabitantId;
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor ? person with
            { Skills = [new(SettlementSkillKind.Crafting, 0)], Equipment = new(null, null) } : person).ToArray()
        };
        using var loaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)));
        Assert.Equal(SettlementSkillKind.Crafting, Assert.Single(loaded.Inhabitants.Single(person => person.InhabitantId == actor).Skills!).Kind);
        Assert.Equal(new PersonalEquipment(null, null), loaded.Inhabitants.Single(person => person.InhabitantId == actor).Equipment);
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(state with { SchemaVersion = 31 }));
        loaded.Validate();
    }
}

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
    public void CurrentEquipmentCannotBeStoredUnderTheOlderSkillsOrHousingSchema(string kind, int schema)
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
    public void SkillsKeepTheirExistingSchemaWithoutRequiringPhysicalEquipment()
    {
        using var initial = new PrivateWorldRuntime("skills-version");
        var state = initial.ExportState() with { SchemaVersion = 31 };
        var actor = state.Inhabitants[0].InhabitantId;
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor ? person with
            { Skills = [new(SettlementSkillKind.Crafting, 0)], Equipment = new(null, null) } : person).ToArray()
        };
        using var loaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)));
        Assert.Equal(SettlementSkillKind.Crafting, Assert.Single(loaded.Inhabitants.Single(person => person.InhabitantId == actor).Skills!).Kind);
        Assert.Equal(new EquipmentState(null, null), loaded.Inhabitants.Single(person => person.InhabitantId == actor).Equipment);
        loaded.Validate();
    }
}

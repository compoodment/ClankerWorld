using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Tests;

public sealed class PrivateWorldWillStorageTests
{
    [Fact]
    public async Task NaturalDeathPreservesHouseStockLivingCustodyAndWholeCarriedVesselsAcrossReload()
    {
        var (state, actor, carrier, house) = DeathState();
        using var world = PrivateWorldRuntime.Restore(state, _ => new ActionCoverageRecorder(chooseIdle: true));
        var position = state.Inhabitants.Single(person => person.InhabitantId == actor).Position;
        var ground = new InventoryGroundPosition(position.X, position.Y);

        Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);

        var estate = Assert.Single(world.Society.Estates);
        Assert.Equal(actor, estate.DeceasedId);
        Assert.Equal("default", estate.WillStatus);
        Assert.Equal(house, world.Society.Inventory.GetLot("stored-wood").StorageBuildingId);
        Assert.Equal(house, estate.FrozenLots!.Single(lot => lot.LotId == "stored-wood").StorageBuildingId);
        Assert.Equal(carrier, world.Society.Inventory.GetLot("living-held-stone").CarrierId);
        Assert.Equal(ground, world.Society.Inventory.GetLot("carried-pot").GroundPosition);
        Assert.Null(world.Society.Inventory.GetLot("carried-berries").GroundPosition);
        Assert.Equal("carried-pot", world.Society.Inventory.GetLot("carried-berries").ContainerLotId);
        Assert.Equal(2, world.Society.Inventory.GetLot("carried-berries").Quantity);
        Assert.Equal(carrier, world.Society.Inventory.GetLot("borrowed-wood").OwnerId);
        Assert.Equal(ground, world.Society.Inventory.GetLot("borrowed-wood").GroundPosition);
        Assert.Null(world.Society.Inventory.GetLot("borrowed-wood").CarrierId);
        world.Validate();

        var saved = world.ExportState();
        var encoded = PrivateWorldRuntimeCodec.Encode(saved);
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(encoded),
            _ => new ActionCoverageRecorder(chooseIdle: true));
        Assert.Equal(encoded, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        var shortened = saved with
        {
            Society = saved.Society with
            {
                Society = saved.Society.Society with
                {
                    Estates = saved.Society.Society.Estates.Select(item => item with
                    { ExpiryTick = saved.Society.Society.WorldTick + 1 }).ToArray(),
                },
            },
        };
        using var settled = PrivateWorldRuntime.Restore(shortened, _ => new ActionCoverageRecorder(chooseIdle: true));
        Assert.True((await settled.AdvanceOneTickNonBlockingAsync()).Advanced);
        Assert.True(settled.Society.GetEstate(estate.Id).Settled);
        var wood = Assert.Single(settled.Society.Inventory.Lots, lot => lot.ProvenanceLotId == "stored-wood");
        Assert.Equal((house, 4), (wood.StorageBuildingId, wood.Quantity));
        Assert.Equal(ground, settled.Society.Inventory.GetLot("carried-pot").GroundPosition);
        Assert.Null(settled.Society.Inventory.GetLot("carried-berries").GroundPosition);
        Assert.Equal(2, settled.Society.Inventory.GetLot("carried-berries").Quantity);
        settled.Validate();
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(settled.ExportState()),
            PrivateWorldRuntimeCodec.Encode(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(settled.ExportState()))));
    }

    [Fact]
    public async Task ADefaultEstateCannotClaimAnotherHouseAsItsFrozenStorage()
    {
        var (state, _, _, house) = DeathState();
        using var world = PrivateWorldRuntime.Restore(state, _ => new ActionCoverageRecorder(chooseIdle: true));
        Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
        Assert.Equal("default", Assert.Single(world.Society.Estates).WillStatus);
        var saved = world.ExportState();
        var otherHouse = saved.WorldSimulation!.Buildings.First(building => building.InstanceId != house &&
            saved.WorldContent!.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId)
                .Tags.Contains("house", StringComparer.Ordinal)).InstanceId;
        var checkpoint = saved.Society.Society;
        var forged = saved with
        {
            Society = saved.Society with
            {
                Society = checkpoint with
                {
                    Inventory = checkpoint.Inventory with
                    {
                        Lots = checkpoint.Inventory.Lots.Select(lot => lot.Id == "stored-wood"
                            ? lot with { StorageBuildingId = otherHouse } : lot).ToArray(),
                    },
                },
            },
        };
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(forged));
    }

    private static (PrivateWorldRuntimeState State, string Actor, string Carrier, string House) DeathState()
    {
        using var generated = NormalPathWorld.CreateGenerated("will-physical-death",
            _ => new ActionCoverageRecorder(chooseIdle: true));
        var state = generated.ExportState();
        var actor = state.Inhabitants[0].InhabitantId;
        var carrier = state.Inhabitants[1].InhabitantId;
        var checkpoint = state.Society.Society;
        var household = checkpoint.GetInhabitant(actor).HouseholdId;
        var house = state.WorldSimulation!.Buildings.First(building => building.HouseholdId == household &&
            state.WorldContent!.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId)
                .Tags.Contains("house", StringComparer.Ordinal)).InstanceId;
        var inventory = InventoryFixture.AddLot(checkpoint.Inventory, "stored-wood", "wood", actor, 4,
            storageBuildingId: house);
        inventory = InventoryFixture.AddLot(inventory, "carried-pot", InventoryContainerRules.StoragePot, actor, 1);
        inventory = InventoryFixture.AddLot(inventory, "carried-berries", "berries", actor, 2, containerLotId: "carried-pot");
        inventory = InventoryFixture.AddLot(inventory, "living-held-stone", "stone", actor, 3);
        inventory = InventoryFixture.Relocate(inventory, "living-custody", "living-held-stone", actor, 3, carrierId: carrier);
        inventory = InventoryFixture.AddLot(inventory, "borrowed-wood", "wood", carrier, 1);
        inventory = InventoryFixture.Relocate(inventory, "borrowed-custody", "borrowed-wood", carrier, 1, carrierId: actor);
        var lastDay = Assert.IsType<SocietyDayLifecycle>(checkpoint.Config.DayLifecycle).MaximumDay;
        var birth = checkpoint.LifeTickAt(checkpoint.WorldTick + 1) - lastDay * checkpoint.Config.TicksPerLifecycleAge;
        checkpoint = checkpoint with
        {
            Inventory = inventory,
            Inhabitants = checkpoint.Inhabitants.Select(person => person.Id == actor ? person with
            {
                BirthTick = checkpoint.LifeClock is null ? birth : person.BirthTick,
                BirthLifeTick = checkpoint.LifeClock is null ? null : birth,
                AgeBand = SocietyAgeBand.Elder,
                LastLifecycleYearChecked = lastDay - 1,
            } : person).ToArray(),
        };
        return (state with { Society = state.Society with { Society = checkpoint } }, actor, carrier, house);
    }
}

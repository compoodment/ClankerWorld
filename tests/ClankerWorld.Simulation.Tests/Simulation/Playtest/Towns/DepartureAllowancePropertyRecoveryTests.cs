using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class TownLandHearingRuntimeTests
{
    [Fact]
    public async Task ReclaimingAStorePreservesDepartedOwnersAllowancesAndTheirPhysicalCollection()
    {
        // Actual election, adult departures, notices, personal consent and ruling;
        // only normal Store costs and its three-portion source stock are fixtures.
        var election = new HearingProvider();
        using var elected = NewWorld(election);
        await UntilAsync(elected, () => elected.Towns[0].Government!.Offices.Any(), Day * 3, election);
        var state = elected.ExportState();
        var household = elected.Society.GetInhabitant(Filer).HouseholdId!;
        var definition = elected.WorldContent.Buildings.Single(item => item.LocalId == "store-1x1");
        var inventory = state.Society.Society.Inventory;
        foreach (var cost in definition.BuildCosts)
            inventory = InventoryFixture.AddLot(inventory, "allowance-store-cost-" + cost.ResourceId,
                cost.ResourceId, household, cost.Amount, storageBuildingId: PropertyHouse);
        state = state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } };
        using var placing = PrivateWorldRuntime.Restore(state, _ => election);
        const string storeId = "allowance-property-store";
        var house = placing.WorldSimulation.Buildings.Single(item => item.InstanceId == PropertyHouse);
        Assert.Contains(state.Map.Tiles.OrderBy(tile => state.Map.FootDistance(tile.Position, house.Position)),
            tile => placing.PlaceBuilding(storeId, definition.CanonicalId, tile.Position, household).Applied);
        state = placing.ExportState();
        inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "000-allowance-property-food", "food",
            household, 3, storageBuildingId: storeId);
        state = state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } };
        using var leaving = PrivateWorldRuntime.Restore(state, _ => election);
        Assert.True(leaving.DisplaceAdult(Filer));
        Assert.True(leaving.DisplaceAdult(Waiver));
        Assert.Empty(leaving.Society.GetHousehold(household).MemberIds);
        var provider = new PropertyProvider { BuildingId = storeId, Request = true, Agree = true, Rule = true };
        var original = PrivateWorldRuntimeCodec.Encode(leaving.ExportState());
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(original), _ => provider);
        for (var tick = 0; tick < 80 && !world.Towns[0].LandHearings.Cases.Any(item =>
                 item.Property is { Transfer: not null } property && property.Request.BuildingId == storeId); tick++)
        {
            PropertyPromptAll(world);
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        }
        var item = Assert.Single(world.Towns[0].LandHearings.Cases, item => item.Property?.Request.BuildingId == storeId);
        Assert.Equal("settled", item.Status);
        var transfer = Assert.IsType<TownPropertyTransfer>(item.Property!.Transfer);
        Assert.Equal(household, transfer.PriorBuilding.HouseholdId);
        Assert.Null(transfer.ResultBuilding.HouseholdId);
        Assert.All(new[] { Filer, Waiver }, actor => Assert.Contains(item.Property.Consents, consent => consent.AgentId == actor && consent.Agreed));
        var personal = world.Society.Inventory.Lots.Where(lot => lot.StorageBuildingId == storeId &&
            (lot.OwnerId == Filer || lot.OwnerId == Waiver)).OrderBy(lot => lot.Id, StringComparer.Ordinal).ToArray();
        Assert.Equal(3, personal.Sum(lot => lot.Quantity));
        Assert.All(personal, lot => Assert.Null(lot.CarrierId));
        world.Validate();
        var reclaimed = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var reload = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(reclaimed), _ => new PropertyProvider());
        Assert.Equal(reclaimed, PrivateWorldRuntimeCodec.Encode(reload.ExportState()));
        var unrelated = reload.ExportState();
        inventory = InventoryFixture.AddLot(unrelated.Society.Society.Inventory, "unrecorded-recovered-personal", "food", Filer, 1,
            storageBuildingId: storeId);
        unrelated = unrelated with { Society = unrelated.Society with { Society = unrelated.Society.Society with { Inventory = inventory } } };
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(unrelated));
        // Receiving-household fixture uses the ordinary Society formation
        // primitive; no building, permission or grant is supplied by it.
        const string recipient = "household:allowance-store-recipient";
        state = reload.ExportState();
        var formation = SocietyFixture.CreateHousehold(state.Society.Society, recipient, "Receiving household", [Waiver]);
        state = state with { Society = state.Society with { Society = formation.Checkpoint } };
        var onward = new PropertyProvider { BuildingId = storeId, Request = true, Target = recipient, Agree = true, Rule = true };
        using var granting = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)), _ => onward);
        for (var tick = 0; tick < 80 && !granting.Towns[0].LandHearings.Cases.Any(item =>
                 item.Property is { Transfer: not null } property && property.Request.BuildingId == storeId &&
                 property.Request.TargetHouseholdId == recipient); tick++)
        {
            PropertyPromptAll(granting);
            Assert.True((await granting.AdvanceOneTickAsync()).Advanced);
        }
        var grant = Assert.Single(granting.Towns[0].LandHearings.Cases, item =>
            item.Property?.Request.BuildingId == storeId && item.Property.Request.TargetHouseholdId == recipient);
        Assert.Equal("settled", grant.Status);
        Assert.Equal(recipient, Assert.IsType<TownPropertyTransfer>(grant.Property!.Transfer).ResultBuilding.HouseholdId);
        Assert.Contains(grant.Property.Consents, consent => consent.AgentId == Waiver && consent.Agreed);
        Assert.All(personal, lot => Assert.Equal((lot.OwnerId, lot.Quantity, storeId),
            (granting.Society.Inventory.GetLot(lot.Id).OwnerId, granting.Society.Inventory.GetLot(lot.Id).Quantity,
             granting.Society.Inventory.GetLot(lot.Id).StorageBuildingId)));
        granting.Validate();
        var granted = PrivateWorldRuntimeCodec.Encode(granting.ExportState());
        var allowance = Assert.Single(personal, lot => lot.OwnerId == Filer);
        var choices = new PropertyProvider { Collect = allowance.Id };
        using var collecting = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(granted), _ => choices);
        for (var tick = 0; tick < 80 && !PersonalEquipmentRules.IsCarried(collecting.Society.Inventory.GetLot(allowance.Id), Filer); tick++)
        {
            PropertyPromptAll(collecting);
            Assert.True((await collecting.AdvanceOneTickAsync()).Advanced);
        }
        var carried = collecting.Society.Inventory.GetLot(allowance.Id);
        Assert.True(PersonalEquipmentRules.IsCarried(carried, Filer));
        Assert.Equal((Filer, 2, (string?)null), (carried.OwnerId, carried.Quantity, carried.StorageBuildingId));
        var remaining = collecting.Society.Inventory.GetLot(Assert.Single(personal, lot => lot.OwnerId == Waiver).Id);
        Assert.Equal((Waiver, storeId, 1), (remaining.OwnerId, remaining.StorageBuildingId, remaining.Quantity));
        Assert.Single(collecting.Inhabitants.Single(person => person.InhabitantId == Filer).Departures!);
        var saved = PrivateWorldRuntimeCodec.Encode(collecting.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved), _ => new PropertyProvider { Collect = allowance.Id });
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        for (var tick = 0; tick < 2; tick++)
        {
            Assert.True((await collecting.AdvanceOneTickAsync()).Advanced);
            Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(collecting.ExportState()), PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        }
    }
}

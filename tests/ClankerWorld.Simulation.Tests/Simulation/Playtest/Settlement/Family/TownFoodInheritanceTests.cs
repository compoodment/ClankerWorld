using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Tests;

public sealed class TownFoodInheritanceTests
{
    [Theory]
    [InlineData("eggs", null, true)]
    [InlineData("milk", "water_jug", true)]
    [InlineData("cooked_eggs", null, true)]
    [InlineData("milk_porridge", "storage_pot", true)]
    [InlineData("rich_meal", "storage_pot", true)]
    [InlineData("berry_porridge", "storage_pot", true)]
    [InlineData("fresh_water", "water_jug", false)]
    [InlineData("wood", null, false)]
    public async Task NativeTownBequestsKeepFoodWithHouseholdBeneficiariesAndVessels(string kind, string? vesselKind, bool household)
    {
        const string deceased = "founder:00000000000000000000000000000001";
        var will = new PostDeathWillTests.WillProvider(CognitionWillContext.HeirsCandidateId, observation =>
            new CognitionWillChoice([observation.Will!.Heirs.Single(heir => heir.Key.StartsWith("will:town:", StringComparison.Ordinal)).Key],
                CognitionWillContext.EqualSplit));
        IDecisionProvider Provider(string id) => id == deceased ? will : new DeterministicDecisionProvider();
        using var generated = NormalPathWorld.CreateGenerated("new-food-town-will-audit", Provider);
        var state = generated.ExportState();
        var society = state.Society.Society;
        var lastDay = Assert.IsType<SocietyDayLifecycle>(society.Config.DayLifecycle).MaximumDay;
        var birth = society.LifeTickAt(society.WorldTick + 1) - lastDay * society.Config.TicksPerLifecycleAge;
        var inventory = society.Inventory with { Lots = society.Inventory.Lots.Where(lot => lot.OwnerId != deceased).ToArray() };
        if (vesselKind is not null) inventory = InventoryFixture.AddLot(inventory, "will-vessel", vesselKind, deceased, 1);
        inventory = InventoryFixture.AddLot(inventory, "will-content", kind, deceased, 2,
            containerLotId: vesselKind is not null ? "will-vessel" : null);
        society = society with
        {
            Inventory = inventory,
            Inhabitants = society.Inhabitants.Select(person => person.Id == deceased ? person with
            {
                BirthTick = society.LifeClock is null ? birth : person.BirthTick,
                BirthLifeTick = society.LifeClock is null ? null : birth,
                AgeBand = SocietyAgeBand.Elder,
                LastLifecycleYearChecked = lastDay - 1
            } : person).ToArray()
        };
        state = state with
        {
            Society = state.Society with { Society = society },
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == deceased
                ? person with { Equipment = null } : person).ToArray()
        };
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)), Provider);
        Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
        var estate = Assert.Single(world.Society.Estates);
        for (var attempt = 0; attempt < 40 && world.Society.GetEstate(estate.Id).WillStatus is null or "pending"; attempt++)
        {
            Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
            await Task.Delay(10);
        }
        estate = world.Society.GetEstate(estate.Id);
        Assert.Equal("accepted", estate.WillStatus);
        Assert.Equal(TownBorderRules.FirstTownId, Assert.Single(estate.WillHeirIds!));
        Assert.All(estate.WillBequests!, bequest => Assert.Equal(TownBorderRules.FirstTownId, bequest.HeirId));
        Assert.Single(will.Observations);
        var saved = world.ExportState();
        // Bound only escrow duration after native death and personal will acceptance.
        // All stock, beneficiary selection and settlement remain native.
        saved = saved with
        {
            Society = saved.Society with
            {
                Society = saved.Society.Society with
                { Estates = saved.Society.Society.Estates.Select(item => item with { ExpiryTick = saved.Society.Society.WorldTick + 1 }).ToArray() }
            }
        };
        var bytes = PrivateWorldRuntimeCodec.Encode(saved);
        using var settling = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), Provider);
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), Provider);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(settling.ExportState()));
        Assert.False((await settling.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(settling.ExportState()));
        Assert.True((await settling.AdvanceOneTickAsync()).Advanced);
        Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(settling.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        Assert.True(settling.Society.GetEstate(estate.Id).Settled);
        var contents = settling.Society.Inventory.Lots.Where(lot => lot.Id == "will-content" || lot.ProvenanceLotId == "will-content").ToArray();
        Assert.Equal(2, contents.Sum(lot => lot.Quantity));
        Assert.All(contents, lot =>
        {
            Assert.Equal(kind, lot.ItemKind);
            Assert.True(lot.FreshnessBasisPoints > 0);
            if (household) Assert.Contains(lot.OwnerId, estate.BeneficiaryIds);
            else Assert.Equal(TownBorderRules.FirstTownId, lot.OwnerId);
            Assert.Equal(household ? null : "first-town-warehouse", lot.StorageBuildingId);
            Assert.Null(lot.CarrierId);
        });
        if (vesselKind is not null)
        {
            var vessel = settling.Society.Inventory.GetLot("will-vessel");
            Assert.Equal((vesselKind, 1), (vessel.ItemKind, vessel.Quantity));
            Assert.Equal(household ? null : "first-town-warehouse", vessel.StorageBuildingId);
            Assert.All(contents, lot => Assert.Equal((vessel.OwnerId, vessel.Id), (lot.OwnerId, lot.ContainerLotId)));
        }
        settling.Validate();
        replay.Validate();
        var final = PrivateWorldRuntimeCodec.Encode(settling.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(final), Provider);
        Assert.Equal(final, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        if (household)
        {
            var invalid = settling.ExportState();
            var stock = invalid.Society.Society.Inventory;
            stock = stock with
            {
                Lots = stock.Lots.Select(lot => contents.Any(content => content.Id == lot.Id) || lot.Id == "will-vessel"
                ? lot with { OwnerId = TownBorderRules.FirstTownId, StorageBuildingId = "first-town-warehouse", GroundPosition = null }
                : lot).ToArray()
            };
            invalid = invalid with { Society = invalid.Society with { Society = invalid.Society.Society with { Inventory = stock } } };
            Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(invalid, Provider));
        }
    }
}

using System.Collections.Concurrent;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class SettlementParenthoodTests
{
    [Theory]
    [InlineData(false, false, 0, false)]
    [InlineData(true, false, 0, true)]
    [InlineData(false, true, 0, false)]
    [InlineData(false, false, 8, true)]
    [InlineData(false, false, 4, false)]
    public async Task BirthProtectsActiveMarketFoodInItsGatePaymentAndGuidance(bool leaveFirst, bool removeStock, int homePortions, bool canBirth)
    {
        var state = await PaidMarketWorld.StateAsync();
        var seller = state.Inhabitants[0].InhabitantId;
        var household = PaidMarketWorld.HouseholdOf(state, seller);
        var members = state.Society.Society.GetHousehold(household).MemberIds;
        Assert.Equal(2, members.Count);
        var caregiver = members.Single(id => id != seller);
        var house = PaidMarketWorld.HouseOf(state, household);
        var inventory = state.Society.Society.Inventory with
        {
            Lots = state.Society.Society.Inventory.Lots.Where(lot =>
                !InventoryContainerRules.IsFood(lot.ItemKind) || lot.OwnerId != household && !members.Contains(lot.OwnerId)).ToArray(),
        };
        inventory = InventoryFixture.AddLot(inventory, "z-birth-home", "bread", household, 8, storageBuildingId: house.InstanceId);
        for (var index = 0; index < 2; index++)
        {
            var id = "a-birth-sale-" + index;
            inventory = InventoryFixture.AddLot(inventory, id, "bread", household, 4);
            inventory = InventoryFixture.Relocate(inventory, "birth-seller-bread-" + index, id, household, 4, carrierId: seller);
        }
        var market = PaidMarketWorld.Market(state);
        state = PaidMarketWorld.At(PaidMarketWorld.WithInventory(state, inventory), seller, MarketContent.StallEntrance(market.Site, 0)) with
        { JevEnabled = false, RoutineHelper = RoutineHelperSettings.Off };
        var deposit = new MarketRulesPolicy
        {
            Choose = (id, candidates) => id == seller ? candidates.FirstOrDefault(candidate =>
                candidate.Id.StartsWith("market_borrow:", StringComparison.Ordinal)) ?? candidates.FirstOrDefault(candidate =>
                candidate.Id.StartsWith("market_deposit:", StringComparison.Ordinal) && candidate.Description.Contains(" bread ", StringComparison.Ordinal)) ??
                candidates.Single(candidate => candidate.Id == "safe_idle") : candidates.Single(candidate => candidate.Id == "safe_idle"),
        };
        using var stocking = PrivateWorldRuntime.Restore(state, deposit.CreateProvider);
        for (var tick = 0; tick < 80 && PaidMarketWorld.Market(stocking).StockReceipts.Sum(receipt => receipt.Quantity) < 8; tick++)
            Assert.True((await stocking.AdvanceOneTickAsync()).Advanced);
        var receipts = PaidMarketWorld.Market(stocking).StockReceipts;
        Assert.NotEmpty(receipts);
        Assert.True(receipts.Sum(receipt => receipt.Quantity) == 8, System.Text.Json.JsonSerializer.Serialize(new
        { Receipts = receipts, Chosen = deposit.Chosen.Where(item => item.Actor == seller), Stock = stocking.Society.Inventory.Lots.Where(lot => lot.OwnerId == household) }));
        Assert.All(receipts, receipt => Assert.Equal((household, "bread"), (receipt.OwnerId, receipt.ItemKind)));
        state = stocking.ExportState();
        using var society = SocietyWorldRuntime.Restore(state.Society);
        society.Apply(checkpoint => SocietyFixture.ProposeRelationship(checkpoint,
            new("market-birth-parents", 1, SocietyRelationshipType.Partnership, seller, caregiver, checkpoint.WorldTick)));
        society.Apply(checkpoint => SocietyFixture.AcceptRelationship(checkpoint, "market-birth-parents", 1, caregiver));
        state = state with
        {
            Society = society.ExportState(),
            Inhabitants = state.Inhabitants.Select(person => person with { HungerBasisPoints = 10_000, LastDecisionContext = null, Project = null }).ToArray(),
        };
        using var preparing = PrivateWorldRuntime.Restore(state, id => new ParentProvider(id == seller ? "parent_propose:" + caregiver :
            id == caregiver ? "parent_accept:" + seller + ":acceptor" : "safe_idle"));
        for (var tick = 0; tick < 10 && preparing.Inhabitants.Single(person => person.InhabitantId == seller).Parenthood?.Stage != "preparing"; tick++)
            Assert.True((await preparing.AdvanceOneTickAsync()).Advanced);
        var plan = preparing.Inhabitants.Single(person => person.InhabitantId == seller).Parenthood!;
        Assert.Equal(("preparing", caregiver, household), (plan.Stage, plan.PrimaryCaregiverId, plan.IntendedHouseholdId));
        state = preparing.ExportState();
        if (leaveFirst)
        {
            state = state with
            {
                Inhabitants = state.Inhabitants.Select(person => person with
                {
                    Position = person.InhabitantId == seller ? MarketContent.StallEntrance(market.Site, 0) : person.Position,
                    HungerBasisPoints = 10_000,
                    LastDecisionContext = null,
                    Project = null,
                }).ToArray(),
            };
            var leave = new MarketRulesPolicy
            {
                Choose = (id, candidates) => id == seller ? candidates.FirstOrDefault(candidate =>
                    candidate.Id.StartsWith("market_leave:", StringComparison.Ordinal)) ?? candidates.Single(candidate => candidate.Id == "safe_idle") :
                    candidates.Single(candidate => candidate.Id == "safe_idle"),
            };
            using var leaving = PrivateWorldRuntime.Restore(state, leave.CreateProvider);
            for (var tick = 0; tick < 80 && PaidMarketWorld.Market(leaving).Occupancies.Any(item => item.EndedTick is null); tick++)
                Assert.True((await leaving.AdvanceOneTickAsync()).Advanced);
            Assert.True(leaving.ExportState().Events.Any(item => item.Kind == "market_stall_left"),
                System.Text.Json.JsonSerializer.Serialize(new
                {
                    Seller = leaving.Inhabitants.Single(person => person.InhabitantId == seller),
                    Choices = leave.OfferedTo(seller).Where(candidate => candidate.Id.StartsWith("market_", StringComparison.Ordinal)),
                    Chosen = leave.Chosen.Where(item => item.Actor == seller).Select(item => new { item.Id, item.Tick }),
                }));
            Assert.DoesNotContain(PaidMarketWorld.Market(leaving).Occupancies, item => item.EndedTick is null);
            Assert.Empty(leaving.Society.Births);
            state = leaving.ExportState();
        }
        using var timing = PrivateWorldRuntime.Restore(state, _ => new ParentProvider("safe_idle"));
        PositionFamilyFixtureAt(timing, plan.LastTransitionTick + 598);
        state = timing.ExportState();
        bool SaleLot(InventoryLot lot) => receipts.Any(receipt => MarketTradeRules.IsReceiptLot(receipt, lot));
        inventory = state.Society.Society.Inventory with
        {
            Lots = state.Society.Society.Inventory.Lots.Where(lot => (lot.Id != "z-birth-home" || homePortions > 0) &&
                (!removeStock || !SaleLot(lot))).Select(lot => lot.Id == "z-birth-home" ? lot with { Quantity = homePortions } : lot).ToArray(),
        };
        state = PaidMarketWorld.WithInventory(state, inventory) with
        {
            Inhabitants = state.Inhabitants.Select(person => person with { HungerBasisPoints = 10_000, LastDecisionContext = null, Project = null }).ToArray(),
        };
        var observations = new ConcurrentQueue<CognitionDecisionRequest>();
        var initial = PrivateWorldRuntimeCodec.Encode(state);
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(initial), _ => new ParentProvider("safe_idle", observations.Enqueue));
        Assert.Equal(initial, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        var notes = new OwnerWorldObservationStore(world).GetSnapshot().Inhabitants
            .Where(person => members.Contains(person.Id)).SelectMany(person => person.SocialNotes).ToArray();
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(initial), _ => new ParentProvider("safe_idle"));
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False((await world.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        for (var tick = 0; tick < 3; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        }
        world.Validate();
        var saved = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var reload = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved));
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(reload.ExportState()));
        Assert.Equal(canBirth ? 1 : 0, world.Society.Births.Count);
        var saleLeft = removeStock ? 0 : leaveFirst ? 4 : 8;
        Assert.Equal(saleLeft, world.Society.Inventory.Lots.Where(SaleLot).Sum(lot => lot.Quantity));
        Assert.All(world.Society.Inventory.Lots.Where(SaleLot), lot => Assert.Equal(household, lot.OwnerId));
        Assert.Equal(canBirth && homePortions > 0 ? homePortions - 4 : homePortions,
            world.Society.Inventory.Lots.Where(lot => lot.Id == "z-birth-home").Sum(lot => lot.Quantity));
        Assert.Equal(leaveFirst ? 0 : 1, PaidMarketWorld.Market(world).Occupancies.Count(item => item.EndedTick is null));
        var ready = homePortions + (leaveFirst ? 8 : 0);
        Assert.Contains(notes, note => note.Contains($"{ready}/8 ready-to-eat portions", StringComparison.Ordinal));
        if (canBirth)
        {
            var birth = Assert.Single(world.Society.Births);
            Assert.Equal((caregiver, household), (birth.PrimaryCaregiverId, birth.HouseholdId));
            var payment = world.Society.Inventory.GetReservation($"birth:{birth.RequestId}:food:00");
            Assert.Equal((household, 4, InventoryReservationState.Completed), (payment.OwnerId, payment.Quantity, payment.State));
            Assert.Equal(homePortions > 0 ? "z-birth-home" : "a-birth-sale-0", payment.LotId);
            Assert.Equal("completed", world.Inhabitants.Single(person => person.InhabitantId == seller).Parenthood!.Stage);
        }
        else
        {
            Assert.Equal("preparing", world.Inhabitants.Single(person => person.InhabitantId == seller).Parenthood!.Stage);
            Assert.Contains(observations, request => request.Observation.Self?.OwnerId == caregiver &&
                request.Observation.Self.ContinuityNote?.Contains($"{ready}/8 ready-to-eat portions", StringComparison.Ordinal) == true);
        }
    }
}

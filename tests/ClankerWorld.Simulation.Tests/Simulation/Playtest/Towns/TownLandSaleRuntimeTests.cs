using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.World;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Viewer.Observation;
using ClankerWorld.GodotClient.UI;
using System.Text.Json;
using System.Globalization;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class TownLandTransferRuntimeTests
{
    [Fact]
    public async Task AHouseholdCanPublishAPaidUseRightOfferWithoutGivingAwayTheRight()
    {
        var provider = new TransferProvider { Sell = true, AllowPropose = true };
        using var world = NewWorld(provider);
        Configure(provider, world);
        var before = Permissions(world.ExportState());
        await UntilAsync(world, () => world.Towns[0].LandHearings.Transfers.Count == 1, 12, provider);
        var request = Assert.Single(world.Towns[0].LandHearings.Transfers);
        var price = Assert.IsType<TownLandSalePrice>(request.Price);
        Assert.Equal(provider.SourceHouseholdId, price.SellerHouseholdId);
        Assert.Equal("wood", price.ItemKind);
        Assert.Equal(2, price.Quantity);
        Assert.Equal("pending", request.Status);
        Assert.Equal(before, Permissions(world.ExportState()));
        Assert.Null(request.Receipt);
    }
    [Fact]
    public async Task ActualGoodsPaymentAndPermissionTransferSurvivePendingAndCompletedReload()
    {
        var provider = new TransferProvider { Sell = true, AllowPropose = true, PaySales = false };
        using var world = NewWorld(provider);
        Configure(provider, world);
        var before = world.ExportState();
        var sellerWood = world.Society.Inventory.Lots.Where(lot => lot.OwnerId == provider.SourceHouseholdId && lot.ItemKind == "wood").Sum(lot => lot.Quantity);
        var totalWood = world.Society.Inventory.Lots.Where(lot => lot.ItemKind == "wood").Sum(lot => lot.Quantity);
        await UntilAsync(world, () => world.Towns[0].LandHearings.Transfers.Count > 0 && world.Towns[0].LandHearings.Transfers[0].Responses.Count == 4, 40, provider);
        var pending = Assert.Single(world.Towns[0].LandHearings.Transfers);
        Assert.Equal("pending", pending.Status);
        Assert.Null(pending.Receipt);
        Assert.Equal(Permissions(before), Permissions(world.ExportState()));
        var saved = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved), _ => provider);
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        var pendingClient = SaleClient(restored);
        Assert.NotNull(pendingClient.Price);
        Assert.Contains("awaiting goods payment", LandTransferText.Summary(pendingClient), StringComparison.Ordinal);
        Assert.Contains(LandTransferText.Details(pendingClient, tick => tick.ToString(CultureInfo.InvariantCulture)), line => line.Contains("Goods price: 2 wood", StringComparison.Ordinal));
        provider.PaySales = true;
        foreach (var actor in pending.Parties.SelectMany(party => party.AdultIds))
            restored.SubmitInstruction(new("sale-payment-" + actor, "owner:test", actor, OwnerInstructionKind.Suggestive,
                "Consider bringing the actual agreed goods to the notice place to finish the accepted land-use sale."));
        for (var tick = 0; tick < 100 && restored.Towns[0].LandHearings.Transfers[0].Status == "pending"; tick++)
        {
            var unchanged = PrivateWorldRuntimeCodec.Encode(restored.ExportState());
            Assert.False((await restored.AdvanceOneTickAsync(() => false)).Advanced);
            Assert.Equal(unchanged, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
            Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
        }
        Assert.True(restored.Towns[0].LandHearings.Transfers[0].Status == "transferred",
            string.Join("\n", provider.Selected.TakeLast(24)) + "\n" + JsonSerializer.Serialize(restored.ExportState().Inhabitants.Select(person => new { person.InhabitantId, person.Position, person.MoveWaitTicks })) +
            "\n" + JsonSerializer.Serialize(restored.Society.Inventory.Lots.Where(lot => lot.ItemKind == "wood")) + "\n" + JsonSerializer.Serialize(restored.ExportState().Events.TakeLast(16)));
        var completed = Assert.Single(restored.Towns[0].LandHearings.Transfers);
        var payment = Assert.IsType<TownLandSalePayment>(completed.Receipt!.Payment);
        Assert.Equal(2, payment.Lots.Sum(lot => lot.Quantity));
        Assert.Equal(sellerWood + 2, restored.Society.Inventory.Lots.Where(lot => lot.OwnerId == provider.SourceHouseholdId && lot.ItemKind == "wood").Sum(lot => lot.Quantity));
        Assert.Equal(totalWood, restored.Society.Inventory.Lots.Where(lot => lot.ItemKind == "wood").Sum(lot => lot.Quantity));
        Assert.All(provider.Plot, tile => Assert.Contains(restored.HouseholdLandUseRights, right => right.HouseholdId == provider.TargetHouseholdId && right.Tiles.Contains(tile)));
        Assert.Equal(JsonSerializer.Serialize(before.TownLandTitles), JsonSerializer.Serialize(restored.ExportState().TownLandTitles));
        Assert.Contains(provider.Selected, choice => choice.Contains("|land_transfer_collect_payment|", StringComparison.Ordinal));
        Assert.Contains(provider.Selected, choice => choice.Contains("|land_transfer_pay|", StringComparison.Ordinal));
        var completedClient = SaleClient(restored);
        Assert.Equal("wood", completedClient.Price!.ItemKind);
        Assert.Equal(payment.BuyerAgentId, completedClient.Payment!.BuyerAgentId);
        Assert.Equal(payment.SellerAgentId, completedClient.Payment.SellerAgentId);
        Assert.Contains(LandTransferText.Details(completedClient, tick => tick.ToString(CultureInfo.InvariantCulture)), line => line.StartsWith("Paid by ", StringComparison.Ordinal));
        restored.Validate();
        var final = PrivateWorldRuntimeCodec.Encode(restored.ExportState());
        using var reloaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(final), _ => provider.ReplayPolicy());
        Assert.Equal(final, PrivateWorldRuntimeCodec.Encode(reloaded.ExportState()));
        var completedState = restored.ExportState();
        foreach (var forgedPayment in new[]
        {
            payment with { Lots = payment.Lots.Select((lot, index) => index == 0 ? lot with { Quantity = lot.Quantity + 1 } : lot).ToArray() },
            payment with { BuyerAgentId = payment.SellerAgentId },
            payment with { Lots = payment.Lots.Select(lot => lot with { TransferEventId = long.MaxValue }).ToArray() }
        })
        {
            var changed = completed with { Receipt = completed.Receipt with { Payment = forgedPayment } };
            Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(completedState with
            {
                Towns = completedState.Towns!.Select(town => town with
                {
                    LandHearings = town.LandHearings with { Transfers = town.LandHearings.Transfers.Select(item => item.Id == changed.Id ? changed : item).ToArray() }
                }).ToArray()
            }, _ => provider.ReplayPolicy()));
        }
        for (var tick = 0; tick < 8; tick++)
        {
            Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
            Assert.True((await reloaded.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(restored.ExportState()), PrivateWorldRuntimeCodec.Encode(reloaded.ExportState()));
        }
    }

    [Theory]
    [InlineData("decline", "rejected")]
    [InlineData("withdraw", "withdrawn")]
    public async Task RefusingAPaidSaleLeavesGoodsAndPermissionsWithTheirOwners(string mode, string status)
    {
        var provider = new TransferProvider { Sell = true, AllowPropose = true, Mode = mode };
        using var world = NewWorld(provider);
        Configure(provider, world);
        var before = world.ExportState();
        await UntilAsync(world, () => world.Towns[0].LandHearings.Transfers.Count == 1, 20, provider);
        await UntilAsync(world, () => world.Towns[0].LandHearings.Transfers[0].Status != "pending", 80, provider);
        var rejected = Assert.Single(world.Towns[0].LandHearings.Transfers);
        Assert.Equal(status, rejected.Status);
        Assert.NotNull(rejected.Price);
        Assert.Null(rejected.Receipt);
        Assert.Equal(Permissions(before), Permissions(world.ExportState()));
        Assert.Equal(PrivateProperty(before), PrivateProperty(world.ExportState()));
        world.Validate();
        var saved = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var reloaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved), _ => provider.ReplayPolicy());
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(reloaded.ExportState()));
    }
    [Theory]
    [InlineData("missing")]
    [InlineData("reserved")]
    [InlineData("borrowed")]
    [InlineData("full")]
    public async Task ConsentCannotSpendMissingReservedBorrowedGoodsOrExceedCarrySpace(string barrier)
    {
        var provider = new TransferProvider { Sell = true, AllowPropose = true };
        using var source = NewWorld(provider);
        Configure(provider, source);
        var state = source.ExportState();
        var inventory = state.Society.Society.Inventory;
        var buyers = state.Society.Society.GetHousehold(provider.TargetHouseholdId).MemberIds;
        var stock = inventory.Lots.Where(lot => lot.OwnerId == provider.TargetHouseholdId && lot.ItemKind == "wood").ToArray();
        Assert.NotEmpty(stock);
        foreach (var lot in stock)
        {
            inventory = barrier == "reserved"
                ? InventoryFixture.Reserve(inventory, "sale-reserved:" + lot.Id, lot.OwnerId, lot.Id, lot.Quantity, "another-real-use", 1000)
                : barrier is "missing" or "borrowed"
                    ? InventoryFixture.Discard(inventory, lot.OwnerId, lot.Id, lot.Quantity) : inventory;
        }
        foreach (var buyer in buyers)
        {
            if (barrier == "borrowed")
            {
                var lotId = "sale-borrowed:" + buyer;
                inventory = InventoryFixture.AddLot(inventory, lotId, "wood", provider.SourceHouseholdId, 2,
                    groundPosition: new(state.Towns![0].OriginSite!.Value.X, state.Towns[0].OriginSite!.Value.Y));
                inventory = InventoryFixture.Relocate(inventory, "sale-loan:" + buyer, lotId, provider.SourceHouseholdId, 2, buyer);
            }
            if (barrier == "full")
            {
                var person = state.Inhabitants.Single(item => item.InhabitantId == buyer);
                var free = PersonalEquipmentRules.FreeCapacity(inventory, buyer, person.Equipment);
                if (free > 0) inventory = InventoryFixture.AddLot(inventory, "sale-full:" + buyer, "stone", buyer, free);
                Assert.Equal(0, PersonalEquipmentRules.FreeCapacity(inventory, buyer, person.Equipment));
            }
        }
        using var world = PrivateWorldRuntime.Restore(state with
        {
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } }
        }, _ => provider);
        var before = world.ExportState();
        await UntilAsync(world, () => world.Towns[0].LandHearings.Transfers.Count == 1 && world.Towns[0].LandHearings.Transfers[0].Responses.Count == 4, 40, provider);
        for (var tick = 0; tick < 8; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var pending = Assert.Single(world.Towns[0].LandHearings.Transfers);
        Assert.Equal("pending", pending.Status);
        Assert.Null(pending.Receipt);
        Assert.Equal(Permissions(before), Permissions(world.ExportState()));
        Assert.Equal(PrivateProperty(before), PrivateProperty(world.ExportState()));
        Assert.DoesNotContain(provider.Selected, choice => choice.Contains("|land_transfer_pay|", StringComparison.Ordinal));
        world.Validate();
        var saved = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved), _ => provider.ReplayPolicy());
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }

    [Fact]
    public async Task CarriedGoodsCannotPayASellerWhoIsNotPhysicallyAtTheMeeting()
    {
        var provider = new TransferProvider { Sell = true, AllowPropose = true, PaySales = false };
        using var source = NewWorld(provider);
        Configure(provider, source);
        await UntilAsync(source, () => source.Towns[0].LandHearings.Transfers.Count == 1 && source.Towns[0].LandHearings.Transfers[0].Responses.Count == 4, 40, provider);
        var state = source.ExportState();
        var stock = state.Society.Society.Inventory.Lots.First(lot => lot.OwnerId == provider.TargetHouseholdId && lot.ItemKind == "wood" && lot.Quantity >= 2);
        var inventory = InventoryFixture.Transfer(state.Society.Society.Inventory, "sale-prepared-buyer", stock.OwnerId,
            Beneficiary, stock.Id, 2, "household_goods_collected");
        var board = state.Towns![0].OriginSite!.Value;
        var far = Enumerable.Range(0, state.Map.Width * state.Map.Height).Select(index => new GridPoint(index % state.Map.Width, index / state.Map.Width))
            .First(point => state.Map.IsPassable(point) && state.Map.FootDistance(board, point) > 12);
        provider.PaySales = true;
        provider.HoldSellerMeeting = true;
        using var world = PrivateWorldRuntime.Restore(state with
        {
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId is Filer or SourcePartner ? person with { Position = far } : person).ToArray()
        }, _ => provider);
        var before = world.ExportState();
        world.SubmitInstruction(new("sale-meeting-payment", "owner:test", Beneficiary, OwnerInstructionKind.Suggestive,
            "Consider paying the accepted price to the seller adult at the notice place."));
        for (var tick = 0; tick < 8; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Contains(provider.Selected, choice => choice.Contains("|land_transfer_pay|", StringComparison.Ordinal));
        var unpaid = Assert.Single(world.Towns[0].LandHearings.Transfers);
        Assert.Equal("pending", unpaid.Status);
        Assert.Null(unpaid.Receipt);
        Assert.Equal(Permissions(before), Permissions(world.ExportState()));
        Assert.Equal(before.Society.Society.Inventory.Lots.Where(lot => lot.OwnerId == provider.SourceHouseholdId && lot.ItemKind == "wood").Sum(lot => lot.Quantity),
            world.Society.Inventory.Lots.Where(lot => lot.OwnerId == provider.SourceHouseholdId && lot.ItemKind == "wood").Sum(lot => lot.Quantity));
        Assert.DoesNotContain(world.Society.Inventory.Events.Skip(before.Society.Society.Inventory.Events.Count),
            item => item.Detail.EndsWith(":land_use_right_payment", StringComparison.Ordinal));
        world.Validate();
    }

    [Theory]
    [InlineData(6, true)]
    [InlineData(8, false)]
    public async Task SplitCarriedFoodPaymentKeepsTheBuyersNeededReserve(int price, bool completes)
    {
        var provider = new TransferProvider
        {
            Sell = true,
            AllowPropose = true,
            PaySales = false,
            SaleItemKind = "food",
            SaleQuantity = price,
            CollectSalePayment = false
        };
        using var source = NewWorld(provider);
        Configure(provider, source);
        await UntilAsync(source, () => source.Towns[0].LandHearings.Transfers.Count == 1 &&
            source.Towns[0].LandHearings.Transfers[0].Responses.Count == 4, 40, provider);
        var state = source.ExportState();
        var inventory = state.Society.Society.Inventory;
        foreach (var lot in inventory.Lots.Where(lot => lot.OwnerId == Beneficiary && lot.ItemKind == "food").ToArray())
            inventory = InventoryFixture.Discard(inventory, Beneficiary, lot.Id, lot.Quantity);
        inventory = InventoryFixture.AddLot(inventory, "sale-food-a", "food", Beneficiary, 4);
        inventory = InventoryFixture.AddLot(inventory, "sale-food-b", "food", Beneficiary, 4);
        provider.PaySales = true;
        using var world = PrivateWorldRuntime.Restore(state with
        {
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
            Inhabitants = state.Inhabitants.Select(person => person with
            { Position = state.Towns![0].OriginSite!.Value, HungerBasisPoints = 10_000 }).ToArray()
        }, _ => provider);
        var before = world.ExportState();
        var sellerFood = inventory.Lots.Where(lot => lot.OwnerId == provider.SourceHouseholdId && lot.ItemKind == "food").Sum(lot => lot.Quantity);
        world.SubmitInstruction(new("split-food-payment", "owner:test", Beneficiary, OwnerInstructionKind.Suggestive,
            "Consider paying the accepted food price from your actual carried surplus, keeping your needed food."));
        for (var tick = 0; tick < 8 && world.Towns[0].LandHearings.Transfers[0].Status == "pending"; tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var result = Assert.Single(world.Towns[0].LandHearings.Transfers);
        Assert.Equal(completes ? "transferred" : "pending", result.Status);
        Assert.True(world.Society.Inventory.Lots.Where(lot => lot.OwnerId == Beneficiary && lot.ItemKind == "food" &&
            PersonalEquipmentRules.IsCarried(lot, Beneficiary)).Sum(lot => lot.Quantity) >= 2);
        if (completes)
        {
            var payment = Assert.IsType<TownLandSalePayment>(result.Receipt!.Payment);
            Assert.Equal(price, payment.Lots.Sum(lot => lot.Quantity));
            Assert.Equal(2, payment.Lots.Count);
            Assert.Equal(sellerFood + price, world.Society.Inventory.Lots.Where(lot =>
                lot.OwnerId == provider.SourceHouseholdId && lot.ItemKind == "food").Sum(lot => lot.Quantity));
        }
        else
        {
            Assert.Null(result.Receipt);
            Assert.Equal(Permissions(before), Permissions(world.ExportState()));
            Assert.DoesNotContain(provider.Selected, choice => choice.StartsWith(Beneficiary + ":", StringComparison.Ordinal) &&
                choice.Contains("|land_transfer_pay|", StringComparison.Ordinal));
            Assert.DoesNotContain(world.Society.Inventory.Events.Skip(inventory.Events.Count),
                item => item.Detail.EndsWith(":land_use_right_payment", StringComparison.Ordinal));
        }
        world.Validate();
        var saved = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved), _ => provider.ReplayPolicy());
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CollectingLandSaleGoodsKeepsTheLoadedReturnToTheNoticePlacePossible(bool swimOnly)
    {
        var provider = new TransferProvider
        { Sell = true, AllowPropose = true, PaySales = false, SaleQuantity = 4, PaymentActor = Beneficiary };
        using var source = NewWorld(provider, seed: "cart-set-unfinished");
        Configure(provider, source);
        await UntilAsync(source, () => source.Towns[0].LandHearings.Transfers.Count == 1 &&
            source.Towns[0].LandHearings.Transfers[0].Responses.Count == 4, 40, provider);
        var state = source.ExportState();
        var board = state.Towns![0].OriginSite!.Value;
        var approaches = state.Map.Tiles.Select(tile => tile.Position)
            .Where(point => state.Map.IsPassable(point) && state.Map.FootDistance(point, board) <= 1).ToArray();
        var pickup = state.Map.Tiles.Select(tile => tile.Position).First(point => state.Map.IsBuildable(point) &&
            state.Map.FootDistance(point, board) > 1 && SwimmingRules.IsReachable(state.Map, point, board) &&
            approaches.All(destination => !state.Map.IsReachableOnFoot(point, destination)) == swimOnly);
        Assert.NotEmpty(approaches);
        Assert.Equal(swimOnly, approaches.All(destination => !state.Map.IsReachableOnFoot(pickup, destination)));
        var inventory = state.Society.Society.Inventory;
        foreach (var lot in inventory.Lots.Where(lot => lot.OwnerId == Beneficiary ||
            lot.OwnerId == provider.TargetHouseholdId && lot.ItemKind == "wood").ToArray())
            inventory = InventoryFixture.Discard(inventory, lot.OwnerId, lot.Id, lot.Quantity);
        inventory = InventoryFixture.AddLot(inventory, "sale-return-stone", "stone", Beneficiary, 1);
        inventory = InventoryFixture.AddLot(inventory, "sale-return-wood", "wood", provider.TargetHouseholdId, 4,
            groundPosition: new(pickup.X, pickup.Y));
        provider.PaySales = true;
        using var world = PrivateWorldRuntime.Restore(state with
        {
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == Beneficiary ? person with
            {
                Position = pickup,
                HungerBasisPoints = 10_000,
                Equipment = null,
                Project = null,
                LastDecisionContext = null,
                Survival = new SurvivalCondition(10_000)
            } : person).ToArray()
        }, _ => provider);
        world.SubmitInstruction(new("sale-loaded-return", "owner:test", Beneficiary, OwnerInstructionKind.Suggestive,
            "Consider collecting your household's agreed goods while retaining a route back to the notice place."));
        for (var tick = 0; tick < 8 && !world.Society.Inventory.Lots.Any(lot => lot.OwnerId == Beneficiary && lot.ItemKind == "wood"); tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var collected = Assert.Single(world.Society.Inventory.Lots, lot => lot.OwnerId == Beneficiary && lot.ItemKind == "wood");
        Assert.Equal(swimOnly ? 3 : 4, collected.Quantity);
        Assert.Equal(swimOnly ? 4 : 5, PersonalEquipmentRules.CarriedQuantity(world.Society.Inventory, Beneficiary, null));
        Assert.Equal(4, world.Society.Inventory.Lots.Where(lot => lot.ItemKind == "wood" &&
            (lot.OwnerId == Beneficiary || lot.OwnerId == provider.TargetHouseholdId)).Sum(lot => lot.Quantity));
        Assert.Contains(provider.Selected, choice => choice.StartsWith(Beneficiary + ":", StringComparison.Ordinal) &&
            choice.Contains("|land_transfer_collect_payment|", StringComparison.Ordinal));
        var sale = Assert.Single(world.Towns[0].LandHearings.Transfers);
        Assert.Equal("pending", sale.Status);
        Assert.Null(sale.Receipt);
        if (swimOnly)
        {
            var remaining = world.Society.Inventory.GetLot("sale-return-wood");
            Assert.Equal((provider.TargetHouseholdId, 1, new InventoryGroundPosition(pickup.X, pickup.Y)),
                (remaining.OwnerId, remaining.Quantity, remaining.GroundPosition));
        }
        var saved = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False((await world.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved), _ => provider.ReplayPolicy());
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        world.Validate();
        restored.Validate();
    }

    private static OwnerLandTransfer SaleClient(PrivateWorldRuntime world)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var snapshot = new OwnerWorldObservationStore(world).GetSnapshot();
        var client = JsonSerializer.Deserialize<ClankerWorld.GodotClient.UI.OwnerWorldSnapshot>(JsonSerializer.Serialize(snapshot, options), options)!;
        return Assert.Single(Assert.Single(client.Towns).LandTransfers);
    }
}

using System.Text;
using System.Text.Json.Nodes;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class MarketLotHistoryCheckpointTests
{
    private const string Seller = "founder:00000000000000000000000000000001";
    private static readonly Lazy<Task<byte[]>> LongDeposit = new(async () =>
        PrivateWorldRuntimeCodec.Encode(await DepositThroughHostAsync(5, false)));

    [Theory]
    [InlineData(4, false)]
    [InlineData(5, false)]
    [InlineData(4, true)]
    [InlineData(5, true)]
    public async Task NativeLongLotDepositsRemainSaveableAcrossHostResumeAndReplay(int splits, bool partial)
    {
        var state = await DepositThroughHostAsync(splits, partial);
        var idle = new MarketRulesPolicy();
        using var original = PrivateWorldRuntime.Restore(state, idle.CreateProvider);
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(
            PrivateWorldRuntimeCodec.Encode(state)), idle.CreateProvider);
        for (var tick = 0; tick < 3; tick++)
        {
            Assert.True((await original.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(original.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        }
    }

    [Theory]
    [InlineData("source")]
    [InlineData("owner")]
    [InlineData("destination")]
    public async Task LongReceiptsStillRequireTheirExactDepositAuthority(string damage)
    {
        var state = PrivateWorldRuntimeCodec.Decode(await LongDeposit.Value);
        var receipt = Assert.Single(PaidMarketWorld.Market(state).StockReceipts);
        var source = damage == "source" ? receipt.SourceLotId + "#forged" : receipt.SourceLotId;
        var owner = damage == "owner" ? PaidMarketWorld.HouseholdOf(state, Seller) : receipt.OwnerId;
        var id = MarketTradeRules.ReceiptId(receipt.OccupancyId, source, owner, receipt.Quantity,
            receipt.DepositedTick, receipt.Sequence, receipt.TradeOfferId);
        var root = JsonNode.Parse(PrivateWorldRuntimeCodec.Encode(state))!;
        var saved = root["state"]!["towns"]![0]!["markets"]![0]!["stockReceipts"]![0]!;
        saved["id"] = id;
        saved["sourceLotId"] = source;
        saved["ownerId"] = owner;
        saved["lotId"] = damage == "destination" ? source + "#forged" : source;
        var failure = Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Decode(
            Encoding.UTF8.GetBytes(root.ToJsonString())));
        Assert.Contains(damage == "destination" ? "deposit authority" : "actual inventory deposit", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectedLongDepositPreservesItsLotAndReceiptSequenceUntilRetry()
    {
        var (state, _) = await PreparedAsync(5, false);
        var policy = DepositPolicy();
        using var control = PrivateWorldRuntime.Restore(state, policy.CreateProvider);
        using var rejected = PrivateWorldRuntime.Restore(state, policy.CreateProvider);
        for (var tick = 0; tick < 8; tick++)
        {
            var before = PrivateWorldRuntimeCodec.Encode(rejected.ExportState());
            Assert.True((await control.AdvanceOneTickAsync()).Advanced);
            if (PaidMarketWorld.Market(control).StockReceipts.Count > 0)
            {
                Assert.False((await rejected.AdvanceOneTickAsync(() => false)).Advanced);
                Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(rejected.ExportState()));
                Assert.Empty(PaidMarketWorld.Market(rejected).StockReceipts);
                Assert.True((await rejected.AdvanceOneTickAsync()).Advanced);
                Assert.Equal(PrivateWorldRuntimeCodec.Encode(control.ExportState()), PrivateWorldRuntimeCodec.Encode(rejected.ExportState()));
                return;
            }
            Assert.True((await rejected.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(control.ExportState()), PrivateWorldRuntimeCodec.Encode(rejected.ExportState()));
        }
        Assert.Fail("The native offered deposit never committed.");
    }

    private static async Task<PrivateWorldRuntimeState> DepositThroughHostAsync(int splits, bool partial)
    {
        var (state, sourceId) = await PreparedAsync(splits, partial);
        var policy = DepositPolicy();
        using var world = PrivateWorldRuntime.Restore(state, policy.CreateProvider);
        var woodBefore = world.Society.Inventory.Lots.Where(lot => lot.ItemKind == "wood").Sum(lot => lot.Quantity);
        var directory = Directory.CreateTempSubdirectory("market-lot-history-");
        try
        {
            var file = new PrivateWorldStateFile(Path.Combine(directory.FullName, "world.json"), policy.CreateProvider);
            file.Save(world);
            var presence = new OwnerClientPresenceLease(TimeSpan.FromMinutes(5));
            presence.RecordAuthenticatedReconnect("test-owner");
            using var service = new PrivateWorldRuntimeService(world, file, presence);
            for (var tick = 0; tick < 8 && PaidMarketWorld.Market(world).StockReceipts.Count == 0; tick++)
            {
                Assert.True(await service.TryAdvanceOnceAsync());
                Assert.False(world.Society.IsPaused);
                Assert.Equal(world.WorldTick, PrivateWorldRuntimeCodec.Decode(File.ReadAllBytes(file.Path)).Society.Society.WorldTick);
            }
            var receipt = Assert.Single(PaidMarketWorld.Market(world).StockReceipts);
            Assert.Contains(policy.Chosen, choice => choice.Actor == Seller && choice.Id.StartsWith("market_deposit:", StringComparison.Ordinal));
            Assert.Equal(sourceId, receipt.SourceLotId);
            Assert.Equal(4, receipt.Quantity);
            Assert.Equal(Seller, receipt.OwnerId);
            Assert.Equal("wood", receipt.ItemKind);
            Assert.Equal(partial ? sourceId + "#move:" + receipt.Id + ":deposit" : sourceId, receipt.LotId);
            if (partial)
            {
                Assert.True(receipt.LotId.Length > 2048);
                if (splits == 4) Assert.True(sourceId.Length <= 2048);
                Assert.Equal(1, world.Society.Inventory.GetLot(sourceId).Quantity);
                Assert.True(PersonalEquipmentRules.IsCarried(world.Society.Inventory.GetLot(sourceId), Seller));
            }
            var deposited = world.Society.Inventory.GetLot(receipt.LotId);
            Assert.Equal(4, deposited.Quantity);
            Assert.True(MarketTradeRules.IsAt(deposited, PaidMarketWorld.StallTile(state, 0)));
            Assert.Equal(woodBefore, world.Society.Inventory.Lots.Where(lot => lot.ItemKind == "wood").Sum(lot => lot.Quantity));
            policy.Choose = (_, _) => null;
            world.Pause();
            world.Resume();
            Assert.True(await service.TryAdvanceOnceAsync());
            Assert.False(world.Society.IsPaused);
            using var reloaded = file.LoadOrCreate(state.WorldSeed);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(reloaded.ExportState()));
            Assert.Equal(receipt, Assert.Single(PaidMarketWorld.Market(reloaded).StockReceipts));
            return reloaded.ExportState();
        }
        finally { directory.Delete(recursive: true); }
    }

    private static MarketRulesPolicy DepositPolicy() => new()
    {
        Choose = (actor, candidates) => actor == Seller
            ? candidates.FirstOrDefault(candidate => candidate.Id.StartsWith("market_deposit:", StringComparison.Ordinal)) : null,
    };

    private static async Task<(PrivateWorldRuntimeState State, string SourceId)> PreparedAsync(int splits, bool partial)
    {
        var state = await PaidMarketWorld.StateAsync();
        var house = PaidMarketWorld.HouseOf(state, PaidMarketWorld.HouseholdOf(state, Seller));
        var sourceId = partial && splits == 4 ? "market-lineage".PadRight(70, 'x') : "market-lineage";
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, sourceId, "wood", Seller,
            splits + (partial ? 5 : 4), storageBuildingId: house.InstanceId);
        for (var sequence = 0; sequence < splits; sequence++)
        {
            var source = inventory.GetLot(sourceId);
            var operation = $"personal:{Seller}:{sequence}:{sourceId}";
            inventory = InventoryFixture.Relocate(inventory, operation, sourceId, Seller, source.Quantity - 1, carrierId: Seller);
            sourceId += "#move:" + operation;
            if (sequence + 1 < splits)
                inventory = InventoryFixture.Relocate(inventory, $"return:{sequence}", sourceId, Seller,
                    inventory.GetLot(sourceId).Quantity, storageBuildingId: house.InstanceId);
        }
        state = PaidMarketWorld.WithInventory(state, inventory);
        state = PaidMarketWorld.At(PaidMarketWorld.Borrowing(state, Seller, 0), Seller, PaidMarketWorld.StallTile(state, 0));
        var bytes = PrivateWorldRuntimeCodec.Encode(state);
        using var verified = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(verified.ExportState()));
        return (verified.ExportState(), sourceId);
    }
}

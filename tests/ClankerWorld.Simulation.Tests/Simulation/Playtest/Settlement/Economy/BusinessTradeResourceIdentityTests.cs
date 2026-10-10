using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class BusinessTradeTests
{
    [Theory]
    [InlineData(128)]
    [InlineData(129)]
    public async Task ModProducedStorePaymentSurvivesHostCheckpointAndSettlement(int resourceLength)
    {
        var (state, buyer, seller, shopId) = CreateShopState("store");
        var shop = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == shopId);
        var kind = new string('x', resourceLength);
        var version = ContentVersion.Parse("1.0.0");
        var packageId = "long-trade-payment";
        var digest = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(packageId)));
        var recipe = new RecipeDefinition(digest, "payment", version, "Make payment",
            [new("wood", 1)], [new(kind, 1)], 1, shop.DefinitionId, []);
        var definition = new ContentDefinition(RecipeDefinition.SchemaKind, recipe.LocalId, version,
            recipe.DisplayName, recipe.PayloadDigest, JsonSerializer.Serialize(new
            {
                schema = "recipe/v1",
                recipe.Inputs,
                recipe.Outputs,
                recipe.DurationTicks,
                recipe.WorkstationBuildingId,
                recipe.Tags,
            }, JsonSerializerOptions.Web));
        var package = new ContentPackageManifest(packageId, version, digest,
            [new(BusinessContent.PackageId, new(version, ContentVersion.Parse("2.0.0")))], [definition], []);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "payment-production-wood",
            "wood", shop.HouseholdId!, 1, storageBuildingId: shopId);
        using (var producing = PrivateWorldRuntime.Restore(WithInventory(state, inventory), _ => new ShopProvider("safe_idle")))
        {
            producing.ProposeContent(package);
            var resolution = producing.ResolveContent(packageId);
            Assert.True(resolution.IsSuccess, resolution.Diagnostic);
            producing.ValidateContent(packageId, resolution);
            producing.ApproveContent(packageId);
            producing.StageContent(packageId);
            Assert.True((await producing.AdvanceOneTickAsync()).Advanced);
            var started = producing.StartProduction(recipe.CanonicalId, shopId, seller);
            Assert.True(started.Applied, started.Failure);
            for (var tick = 0; tick < 8 && !producing.Society.Inventory.Lots.Any(lot => lot.ItemKind == kind); tick++)
                Assert.True((await producing.AdvanceOneTickAsync()).Advanced);
            state = producing.ExportState();
        }
        var produced = Assert.Single(state.Society.Society.Inventory.Lots, lot => lot.ItemKind == kind);
        // Controlled ownership arrangement after genuine package activation and production;
        // this transfer is not a claim that a resident simulated a gift journey.
        inventory = InventoryFixture.Transfer(state.Society.Society.Inventory, "controlled-payment-transfer",
            shop.HouseholdId!, buyer, produced.Id, 1, "gift");
        inventory = inventory with { Lots = inventory.Lots.Where(lot => lot.Id != "buyer-payment").ToArray() };
        state = WithInventory(state, inventory) with
        {
            Inhabitants = state.Inhabitants.Select(person => person with { LastDecisionContext = null }).ToArray(),
        };
        using var offering = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)),
            actor => actor == buyer ? new ShopProvider("business_shop:" + shopId) : new ShopProvider("safe_idle"));
        var directory = Directory.CreateTempSubdirectory("long-business-payment-");
        try
        {
            var file = new PrivateWorldStateFile(Path.Combine(directory.FullName, "world.json"));
            file.Save(offering);
            var presence = new OwnerClientPresenceLease(TimeSpan.FromMinutes(5));
            presence.RecordAuthenticatedReconnect("test-owner");
            using (var host = new PrivateWorldRuntimeService(offering, file, presence))
            {
                for (var tick = 0; tick < 10 && offering.BusinessTrades.Count == 0; tick++)
                {
                    Assert.True(await host.TryAdvanceOnceAsync());
                    Assert.Equal(PrivateWorldRuntimeCodec.Encode(offering.ExportState()), File.ReadAllBytes(file.Path));
                }
                var trade = Assert.Single(offering.BusinessTrades);
                Assert.Equal(kind, trade.PaymentKind);
                var offer = offering.Society.Inventory.GetOffer(trade.OfferId);
                Assert.Equal(DirectBarterState.Open, offer.State);
                Assert.Equal(kind, offering.Society.Inventory.GetLot(offer.SecondLotId).ItemKind);
                var altered = offering.ExportState() with
                {
                    BusinessTrades = [trade with { PaymentKind = kind[..^1] + "z" }],
                };
                Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(altered));
                offering.Resume();
                Assert.True(await host.TryAdvanceOnceAsync());
                Assert.False(offering.Society.IsPaused);
            }
            var savedBytes = File.ReadAllBytes(file.Path);
            var pendingState = PrivateWorldRuntimeCodec.Decode(savedBytes);
            using (var pendingReload = PrivateWorldRuntime.Restore(pendingState))
                Assert.Equal(savedBytes, PrivateWorldRuntimeCodec.Encode(pendingReload.ExportState()));
            // The seller's changed test policy needs a fresh turn after the completed quote phase.
            using var restored = PrivateWorldRuntime.Restore(pendingState with
            {
                Inhabitants = pendingState.Inhabitants.Select(person => person.InhabitantId == seller
                    ? person with { LastDecisionContext = null } : person).ToArray(),
            }, actor => actor == seller ? new ShopProvider("business_continue:") : new ShopProvider("safe_idle"));
            var pending = Assert.Single(restored.BusinessTrades);
            using var resumedHost = new PrivateWorldRuntimeService(restored, file, presence);
            for (var tick = 0; tick < 20 && restored.Society.Inventory.GetOffer(pending.OfferId).State != DirectBarterState.Settled; tick++)
            {
                Assert.True(await resumedHost.TryAdvanceOnceAsync());
                Assert.Equal(PrivateWorldRuntimeCodec.Encode(restored.ExportState()), File.ReadAllBytes(file.Path));
            }
            Assert.Equal(DirectBarterState.Settled, restored.Society.Inventory.GetOffer(pending.OfferId).State);
            var paid = Assert.Single(restored.Society.Inventory.Lots, lot => lot.ItemKind == kind);
            Assert.Equal((shop.HouseholdId, shopId, 1), (paid.OwnerId, paid.StorageBuildingId, paid.Quantity));
            using var settled = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(File.ReadAllBytes(file.Path)));
            Assert.Equal(kind, Assert.Single(settled.BusinessTrades).PaymentKind);
            Assert.Equal(File.ReadAllBytes(file.Path), PrivateWorldRuntimeCodec.Encode(settled.ExportState()));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}

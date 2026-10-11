using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class BusinessTradeTests
{
    [Fact]
    public async Task NativeMilkResalesRetainFullSourceThroughHostReloadResolutionAndRollback()
    {
        var (state, buyer, seller, shop) = CreateShopState("store");
        var people = new[] { seller, buyer };
        var homes = people.Select(id => state.Society.Society.GetInhabitant(id).HouseholdId!).ToArray();
        var store = state.WorldContent!.Buildings.Where(definition => definition.Tags.Contains("store"))
            .OrderBy(definition => definition.Width * definition.Height).First();
        var buyerHouse = state.WorldSimulation!.Buildings.First(building => building.HouseholdId == homes[1] &&
            state.WorldContent.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId).Tags.Contains("house"));
        var inventory = state.Society.Society.Inventory;
        foreach (var cost in store.BuildCosts)
            inventory = InventoryFixture.AddLot(inventory, "resale-store-cost-" + cost.ResourceId, cost.ResourceId,
                homes[1], cost.Amount, storageBuildingId: buyerHouse.InstanceId);
        using (var placing = PrivateWorldRuntime.Restore(WithInventory(state, inventory), _ => new ShopProvider("safe_idle")))
        {
            _ = state.Map.Tiles.OrderBy(tile => state.Map.FootDistance(tile.Position, buyerHouse.Position))
                .First(tile => placing.PlaceBuilding("resale-second-store", store.CanonicalId, tile.Position, homes[1]).Applied);
            state = placing.ExportState();
        }
        var stores = new[] { shop, "resale-second-store" };
        var jugs = new[] { "resale-first-jug", "resale-second-jug" };
        inventory = state.Society.Society.Inventory;
        inventory = inventory with
        {
            Lots = inventory.Lots.Select(lot => lot.Id == "buyer-payment"
            ? lot with { Quantity = 32 } : lot).ToArray()
        };
        inventory = InventoryFixture.AddLot(inventory, "resale-seller-payment", "wood", seller, 32);
        inventory = InventoryFixture.AddLot(inventory, jugs[0], "water_jug", homes[0], 1, storageBuildingId: stores[0]);
        inventory = InventoryFixture.AddLot(inventory, "resale-milk", "milk", homes[0], 1,
            storageBuildingId: stores[0], containerLotId: jugs[0]);
        inventory = InventoryFixture.AddLot(inventory, jugs[1], "water_jug", buyer, 1);
        state = WithInventory(state, inventory) with { RoutineHelper = RoutineHelperSettings.Off, JevEnabled = false };
        using var files = new MilkResaleFiles();

        // Stock, payment, legal paid Stores and nearby positions are controlled preparation.
        // Every new milk identity below comes from an actually offered and admitted sale.
        for (var round = 1; round <= 16; round++)
        {
            var source = (round - 1) % 2;
            var destination = 1 - source;
            inventory = state.Society.Society.Inventory;
            var milk = Assert.Single(inventory.Lots, lot => lot.ItemKind == "milk");
            var jug = inventory.GetLot(milk.ContainerLotId!);
            if (jug.OwnerId == people[source])
                inventory = InventoryFixture.Transfer(inventory, "resale-donate-" + round, jug.OwnerId,
                    homes[source], jug.Id, 1, "milk-resale-stock");
            jug = inventory.GetLot(jug.Id);
            inventory = InventoryFixture.Relocate(inventory, "resale-stock-" + round, jug.Id, jug.OwnerId, 1,
                storageBuildingId: stores[source]);
            var receiving = inventory.GetLot(jugs.Single(id => id != jug.Id));
            inventory = InventoryFixture.Relocate(inventory, "resale-receive-" + round, receiving.Id, receiving.OwnerId, 1,
                carrierId: people[destination]);
            var building = state.WorldSimulation!.Buildings.Single(item => item.InstanceId == stores[source]);
            var occupied = state.Inhabitants.Where(person => !people.Contains(person.InhabitantId)).Select(person => person.Position).ToHashSet();
            var neighbor = state.Map.FootNeighbors(building.Position).First(point => state.Map.IsPassable(point) && !occupied.Contains(point));
            state = WithInventory(state, inventory) with
            {
                Inhabitants = state.Inhabitants.Select(person => person with
                {
                    Position = person.InhabitantId == people[source] ? building.Position
                        : person.InhabitantId == people[destination] ? neighbor : person.Position,
                    LastDecisionContext = null,
                    Project = null,
                }).ToArray(),
            };
            var choices = new MilkHistoryChoices(people[source], people[destination]);
            using var world = PrivateWorldRuntime.Restore(state, _ => choices);
            using var host = new MilkHistoryHost(world, files.CheckpointPath);
            world.Resume();
            for (var step = 0; step < 12 && world.ExportState().AnimalWorld.MilkOffers.Count == 0; step++)
                await host.Advance();
            var pendingState = world.ExportState();
            var offer = Assert.Single(pendingState.AnimalWorld.MilkOffers);
            Assert.Equal((milk.Id, people[source], people[destination], receiving.Id, stores[source]),
                (offer.MilkLotId, offer.SellerId, offer.BuyerId, offer.ReceivingJugId, offer.BuildingId));
            Assert.Equal(round >= 14, offer.MilkLotId.Length > 512);
            var pending = host.Saved();
            if (round == 14)
            {
                using var reload = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(pending), _ => choices);
                Assert.Equal(pending, PrivateWorldRuntimeCodec.Encode(reload.ExportState()));
                using var reloadHost = new MilkHistoryHost(reload);
                AssertInvalidBindings(pendingState, offer);
                Assert.False((await world.AdvanceOneTickAsync(() => false)).Advanced);
                Assert.Equal(pending, PrivateWorldRuntimeCodec.Encode(world.ExportState()));

                // A separate continuation refuses this very same long-source offer.
                var declineChoices = new MilkHistoryChoices(people[source], people[destination]) { Mode = "decline" };
                using var declined = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(pending), _ => declineChoices);
                using var declineHost = new MilkHistoryHost(declined);
                declined.Pause();
                declined.Resume();
                for (var step = 0; step < 12 && declined.ExportState().AnimalWorld.MilkOffers.Count != 0; step++)
                    await declineHost.Advance();
                Assert.Empty(declined.ExportState().AnimalWorld.MilkOffers);
                Assert.Equal(InventoryReservationState.Released, declined.Society.Inventory.GetReservation(offer.Id + "-milk").State);
                var refusedMilk = Assert.Single(declined.Society.Inventory.Lots, lot => lot.ItemKind == "milk");
                Assert.Equal((milk.Id, jug.OwnerId, 1, jug.Id),
                    (refusedMilk.Id, refusedMilk.OwnerId, refusedMilk.Quantity, refusedMilk.ContainerLotId));
                Assert.Equal(inventory.GetLot(offer.PaymentLotId).Quantity, declined.Society.Inventory.GetLot(offer.PaymentLotId).Quantity);
                Assert.Equal((jug.OwnerId, receiving.OwnerId),
                    (declined.Society.Inventory.GetLot(jug.Id).OwnerId, declined.Society.Inventory.GetLot(receiving.Id).OwnerId));
                Assert.Single(declined.ExportState().Events, item => item.Kind == "milk_sale_declined" && item.Detail == offer.Id);
                using var declinedReload = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(declineHost.Saved()), _ => declineChoices);
                Assert.Equal(declineHost.Saved(), PrivateWorldRuntimeCodec.Encode(declinedReload.ExportState()));

                choices.Mode = "accept";
                world.Pause();
                reload.Pause();
                world.Resume();
                reload.Resume();
                for (var step = 0; step < 12 && world.ExportState().AnimalWorld.MilkOffers.Count != 0; step++)
                {
                    await host.Advance();
                    await reloadHost.Advance();
                    Assert.Equal(host.Saved(), reloadHost.Saved());
                }
                choices.Mode = "idle";
                await host.Advance();
                await reloadHost.Advance();
                Assert.Equal(host.Saved(), reloadHost.Saved());
            }
            else
            {
                choices.Mode = "accept";
                world.Pause();
                world.Resume();
                for (var step = 0; step < 12 && world.ExportState().AnimalWorld.MilkOffers.Count != 0; step++)
                    await host.Advance();
            }
            state = world.ExportState();
            Assert.Empty(state.AnimalWorld.MilkOffers);
            Assert.Single(state.Events, item => item.Kind == "milk_sale_completed" && item.Detail == offer.Id);
            Assert.Equal(InventoryReservationState.Released, world.Society.Inventory.GetReservation(offer.Id + "-milk").State);
            var transferred = Assert.Single(world.Society.Inventory.Lots, lot => lot.ItemKind == "milk");
            Assert.Equal((milk.Id + "#milk:" + offer.Id, milk.Id, 1, receiving.Id, receiving.OwnerId),
                (transferred.Id, transferred.ProvenanceLotId, transferred.Quantity, transferred.ContainerLotId, transferred.OwnerId));
            Assert.Equal(inventory.GetLot(offer.PaymentLotId).Quantity - 1, world.Society.Inventory.GetLot(offer.PaymentLotId).Quantity);
            Assert.Equal((jug.OwnerId, receiving.OwnerId),
                (world.Society.Inventory.GetLot(jug.Id).OwnerId, world.Society.Inventory.GetLot(receiving.Id).OwnerId));
            using var completed = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(host.Saved()), _ => choices);
            Assert.Equal(host.Saved(), PrivateWorldRuntimeCodec.Encode(completed.ExportState()));
        }
    }

    private static void AssertInvalidBindings(PrivateWorldRuntimeState state, MilkSaleOffer offer)
    {
        foreach (var invalid in new[] { "", " ", "bad\nlot", offer.MilkLotId + "-missing", offer.PaymentLotId })
            Refuse(state with { AnimalWorld = state.AnimalWorld with { MilkOffers = [offer with { MilkLotId = invalid }] } });
        var duplicate = state with
        {
            AnimalWorld = state.AnimalWorld with
            {
                MilkOffers =
            [offer, offer with { Id = offer.Id + "-duplicate", SellerId = offer.BuyerId, BuyerId = offer.SellerId }]
            }
        };
        Assert.Contains("unique, well-formed lot identities", Assert.Throws<InvalidDataException>(() =>
            PrivateWorldRuntimeCodec.Encode(duplicate)).Message);
        Refuse(state with { AnimalWorld = state.AnimalWorld with { MilkOffers = [offer with { Id = new string('x', 513) }] } });
        Refuse(state with { AnimalWorld = state.AnimalWorld with { MilkOffers = [offer with { BuildingId = offer.BuildingId + "-missing" }] } });
        Refuse(state with { AnimalWorld = state.AnimalWorld with { MilkOffers = [offer with { ReceivingJugId = offer.MilkLotId }] } });
        var inventory = state.Society.Society.Inventory;
        Refuse(WithInventory(state, inventory with
        {
            Reservations = inventory.Reservations.Where(item => item.Id != offer.Id + "-milk").ToArray(),
        }));
        Refuse(WithInventory(state, inventory with
        {
            Reservations = inventory.Reservations.Select(item => item.Id == offer.Id + "-milk"
                ? item with { Purpose = "unrelated-reservation" } : item).ToArray(),
        }));
        Refuse(WithInventory(state, inventory with
        {
            Lots = inventory.Lots.Select(item => item.Id == offer.MilkLotId || item.Id == inventory.GetLot(offer.MilkLotId).ContainerLotId
                ? item with { OwnerId = offer.BuyerId } : item).ToArray(),
            Reservations = inventory.Reservations.Select(item => item.Id == offer.Id + "-milk"
                ? item with { OwnerId = offer.BuyerId } : item).ToArray(),
        }));

        static void Refuse(PrivateWorldRuntimeState invalid) =>
            Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(invalid));
    }

    private sealed class MilkResaleFiles : IDisposable
    {
        private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("clanker-milk-resales-");
        public string CheckpointPath => Path.Combine(directory.FullName, "world.json");
        public void Dispose() => directory.Delete(recursive: true);
    }
}

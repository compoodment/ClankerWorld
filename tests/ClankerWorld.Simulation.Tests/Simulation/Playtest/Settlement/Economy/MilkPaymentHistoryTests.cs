using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class BusinessTradeTests
{
    [Theory]
    [InlineData(2, false)]
    [InlineData(2, true)]
    [InlineData(4, false)]
    [InlineData(4, true)]
    public async Task MilkOffersRetainMovedPaymentIdentityThroughHostReloadAndResolution(int partialMoves, bool accept)
    {
        var (state, buyer, seller, shop) = CreateShopState("store");
        var inventory = state.Society.Society.Inventory;
        var home = state.Society.Society.GetInhabitant(seller).HouseholdId!;
        inventory = InventoryFixture.AddLot(inventory, "history-milk-jug", "water_jug", home, 1, storageBuildingId: shop);
        inventory = InventoryFixture.AddLot(inventory, "history-milk", "milk", home, 2,
            storageBuildingId: shop, containerLotId: "history-milk-jug");
        inventory = InventoryFixture.AddLot(inventory, "history-buyer-jug", "water_jug", buyer, 1);
        var buyerHome = state.Society.Society.GetInhabitant(buyer).HouseholdId!;
        var house = state.WorldSimulation!.Buildings.First(building => building.HouseholdId == buyerHome &&
            state.WorldContent!.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId).Tags.Contains("house"));
        var paymentId = "buyer-payment";
        inventory = InventoryFixture.Relocate(inventory, "history-start", paymentId, buyer, 6, storageBuildingId: house.InstanceId);
        // Controlled earlier stock transactions; the native offer and resolution below run through the host.
        for (var index = 1; index <= partialMoves; index++)
        {
            var quantity = inventory.GetLot(paymentId).Quantity - 1;
            var moveId = $"personal:{buyer}:{index}:{paymentId}";
            inventory = InventoryFixture.Relocate(inventory, moveId, paymentId, buyer, quantity, carrierId: buyer);
            paymentId += "#move:" + moveId;
            if (index < partialMoves)
                inventory = InventoryFixture.Relocate(inventory, "history-return-" + index, paymentId, buyer, quantity,
                    storageBuildingId: house.InstanceId);
        }
        Assert.Equal(partialMoves == 4, paymentId.Length > 512);
        state = WithInventory(state, inventory) with { RoutineHelper = RoutineHelperSettings.Off, JevEnabled = false };
        var initial = PrivateWorldRuntimeCodec.Encode(state);
        var choices = new MilkHistoryChoices(seller, buyer);
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(initial), _ => choices);
        Assert.Equal(initial, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        using var host = new MilkHistoryHost(world);
        world.Resume();
        for (var step = 0; step < 20 && world.ExportState().AnimalWorld.MilkOffers.Count == 0; step++)
            await host.Advance();
        state = world.ExportState();
        var offer = Assert.Single(state.AnimalWorld.MilkOffers);
        Assert.Equal((buyer, seller, "history-milk", "history-buyer-jug", paymentId, shop),
            (offer.BuyerId, offer.SellerId, offer.MilkLotId, offer.ReceivingJugId, offer.PaymentLotId, offer.BuildingId));
        var held = world.Society.Inventory.GetReservation(offer.Id + "-milk");
        Assert.Equal((offer.MilkLotId, home, 1, true, InventoryReservationState.Reserved),
            (held.LotId, held.OwnerId, held.Quantity, held.IsExclusive, held.State));
        var pending = host.Saved();
        using var reload = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(pending), _ => choices);
        Assert.Equal(pending, PrivateWorldRuntimeCodec.Encode(reload.ExportState()));
        using var reloadHost = new MilkHistoryHost(reload);
        AssertInvalidOffer(offer with { PaymentLotId = paymentId + "-missing" });
        AssertInvalidOffer(offer with { PaymentLotId = "buyer-payment" });
        AssertInvalidOffer(offer with { ReceivingJugId = "history-milk-jug" });
        AssertInvalidOffer(offer with { BuyerId = seller });
        var forgedReservation = WithInventory(state, state.Society.Society.Inventory with
        {
            Reservations = state.Society.Society.Inventory.Reservations.Select(item => item.Id == held.Id
                ? item with { Purpose = "unrelated-reservation" } : item).ToArray(),
        });
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(forgedReservation));
        Assert.False((await world.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(pending, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        choices.Mode = accept ? "accept" : "decline";
        world.Pause();
        reload.Pause();
        world.Resume();
        reload.Resume();
        for (var step = 0; step < 20 && world.ExportState().AnimalWorld.MilkOffers.Count > 0; step++)
        {
            await host.Advance();
            await reloadHost.Advance();
            Assert.Equal(host.Saved(), reloadHost.Saved());
        }
        Assert.Empty(world.ExportState().AnimalWorld.MilkOffers);
        Assert.Equal(InventoryReservationState.Released, world.Society.Inventory.GetReservation(held.Id).State);
        Assert.Equal(6 - partialMoves - (accept ? 1 : 0), world.Society.Inventory.GetLot(paymentId).Quantity);
        Assert.Equal(accept ? 1 : 2, world.Society.Inventory.GetLot("history-milk").Quantity);
        Assert.Equal(accept ? 1 : 0, world.Society.Inventory.Lots.Where(lot => lot.ContainerLotId == "history-buyer-jug")
            .Sum(lot => lot.Quantity));
        if (accept)
        {
            var payment = Assert.Single(world.Society.Inventory.Lots, lot => lot.ProvenanceLotId == paymentId);
            Assert.Equal((home, "wood", 1, new InventoryGroundPosition(offer.Position.X, offer.Position.Y)),
                (payment.OwnerId, payment.ItemKind, payment.Quantity, payment.GroundPosition));
        }
        else Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.ProvenanceLotId == paymentId);
        Assert.Equal((home, buyer), (world.Society.Inventory.GetLot("history-milk-jug").OwnerId,
            world.Society.Inventory.GetLot("history-buyer-jug").OwnerId));
        choices.Mode = "idle";
        await host.Advance();
        await reloadHost.Advance();
        Assert.Equal(host.Saved(), reloadHost.Saved());
        using var finished = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(host.Saved()), _ => choices);
        Assert.Equal(host.Saved(), PrivateWorldRuntimeCodec.Encode(finished.ExportState()));

        void AssertInvalidOffer(MilkSaleOffer forged) => Assert.Throws<InvalidDataException>(() =>
            PrivateWorldRuntimeCodec.Encode(state with { AnimalWorld = state.AnimalWorld with { MilkOffers = [forged] } }));
    }

    private sealed class MilkHistoryChoices(string seller, string buyer) : IDecisionProvider
    {
        public string Mode { get; set; } = "offer";
        public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var observation = request.Observation;
            var prefix = Mode == "offer" && observation.InhabitantId == seller ? "animal:milk_offer:"
                : observation.InhabitantId == buyer && Mode is "accept" or "decline" ? "animal:milk_" + Mode + ":" : "safe_idle";
            var choice = observation.Candidates.FirstOrDefault(candidate => candidate.Id.StartsWith(prefix, StringComparison.Ordinal))
                ?? observation.Candidates.Single(candidate => candidate.Id == "safe_idle");
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, observation.InhabitantId, Kind, 0,
                observation.RunEpoch, observation.DecisionGeneration, observation.ObservationDigest, choice.Id, 1,
                observation.Candidates.ToDictionary(candidate => candidate.Id, candidate => candidate.Id == choice.Id ? 1d : 0d)));
        }
    }

    private sealed class MilkHistoryHost : IDisposable
    {
        private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("clanker-milk-history-");
        private readonly PrivateWorldRuntime world;
        private readonly PrivateWorldStateFile file;
        private readonly PrivateWorldRuntimeService service;
        private readonly RecordingLogger<PrivateWorldRuntimeService> logger = new();

        public MilkHistoryHost(PrivateWorldRuntime world)
        {
            this.world = world;
            file = new PrivateWorldStateFile(Path.Combine(directory.FullName, "world.json"));
            file.Save(world);
            var presence = new OwnerClientPresenceLease(TimeSpan.FromMinutes(5));
            presence.RecordAuthenticatedReconnect("test-owner");
            service = new PrivateWorldRuntimeService(world, file, presence, logger);
        }

        public byte[] Saved() => File.ReadAllBytes(file.Path);
        public async Task Advance()
        {
            // Compare the same admitted replies, rather than thread-pool timing.
            // The playable host intentionally runs personal calls between ticks.
            foreach (var fieldName in new[] { "pendingHosted", "pendingIdentityMoments" })
            {
                var field = typeof(PrivateWorldRuntime).GetField(fieldName,
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                var pending = Assert.IsAssignableFrom<System.Collections.IDictionary>(field!.GetValue(world));
                var tasks = pending.Values.Cast<object>().Select(item =>
                    Assert.IsAssignableFrom<Task>(item.GetType().GetProperty("Task")!.GetValue(item)!)).ToArray();
                await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(30));
            }
            Assert.True(await service.TryAdvanceOnceAsync(), string.Join("\n", logger.Messages));
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), Saved());
        }
        public void Dispose()
        {
            service.Dispose();
            directory.Delete(recursive: true);
        }
    }
}

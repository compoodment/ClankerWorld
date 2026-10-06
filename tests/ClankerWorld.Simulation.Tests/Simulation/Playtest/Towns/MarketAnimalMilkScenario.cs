using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Tests;

internal static class MarketAnimalMilkScenario
{
    internal static async Task AssertPipelineAsync(PrivateWorldRuntimeState boundary)
    {
        var town = boundary.Towns![0];
        var market = town.Markets.Single();
        var stall = market.Stalls[0];
        var site = MarketContent.StallSite(market.Site, stall.SlotIndex);
        var seller = town.ResidentIds.First(id => boundary.Society.Society.GetInhabitant(id).AgeBand == SocietyAgeBand.Adult);
        var home = boundary.Society.Society.GetInhabitant(seller).HouseholdId!;
        var buyer = town.ResidentIds.First(id => boundary.Society.Society.GetInhabitant(id).AgeBand == SocietyAgeBand.Adult &&
            boundary.Society.Society.GetInhabitant(id).HouseholdId != home);
        var inventory = InventoryFixture.AddLot(boundary.Society.Society.Inventory, "market-milk-jug", "water_jug", home, 1);
        inventory = InventoryFixture.AddLot(inventory, "market-milk", "milk", home, 2, containerLotId: "market-milk-jug");
        inventory = InventoryFixture.Relocate(inventory, "milk-fixture-carried", "market-milk-jug", home, 1, carrierId: seller);
        inventory = InventoryFixture.AddLot(inventory, "market-milk-buyer-jug", "water_jug", buyer, 1);
        inventory = InventoryFixture.AddLot(inventory, "market-milk-payment", "stone", buyer, 2);
        var state = boundary with
        {
            Society = boundary.Society with { Society = boundary.Society.Society with { Inventory = inventory } },
            Inhabitants = boundary.Inhabitants.Select(person => person with
            {
                Position = person.InhabitantId == seller ? site : person.InhabitantId == buyer ? new(site.X + 1, site.Y) : person.Position,
                HungerBasisPoints = 9500,
                LastDecisionContext = null,
                Project = null,
            }).ToArray(),
        };
        var choices = new Choices(seller, buyer);
        using var world = PrivateWorldRuntime.Restore(state, _ => choices);
        await Until(() => world.Towns[0].Markets[0].Occupancies.Any(item => item.EndedTick is null && item.SellerAgentId == seller));
        choices.Mode = "stock";
        await Until(() => world.Towns[0].Markets[0].StockReceipts.Any(receipt => receipt.LotId == "market-milk-jug"));
        Assert.Equal((home, new InventoryGroundPosition(site.X, site.Y)), (world.Society.Inventory.GetLot("market-milk-jug").OwnerId,
            world.Society.Inventory.GetLot("market-milk-jug").GroundPosition));
        choices.Mode = "sale";
        await Until(() => world.Society.Inventory.Lots.Any(lot => lot.ContainerLotId == "market-milk-buyer-jug"));
        Assert.Equal((home, buyer, 1), (world.Society.Inventory.GetLot("market-milk-jug").OwnerId,
            world.Society.Inventory.GetLot("market-milk-buyer-jug").OwnerId, world.Society.Inventory.GetLot("market-milk").Quantity));
        var payment = Assert.Single(world.Society.Inventory.Lots, lot => lot.ProvenanceLotId == "market-milk-payment");
        Assert.Equal(home, payment.OwnerId);
        Assert.Contains(world.Towns[0].Markets[0].StockReceipts, receipt => receipt.LotId == payment.Id);
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var reload = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), _ => choices);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(reload.ExportState()));
        choices.Mode = "leave";
        await Until(() => world.Towns[0].Markets[0].Occupancies.All(item => item.EndedTick is not null));
        choices.Mode = "recover";
        await Until(() => PersonalEquipmentRules.IsCarried(world.Society.Inventory.GetLot("market-milk-jug"), seller));
        Assert.Equal(home, world.Society.Inventory.GetLot("market-milk").OwnerId);
        Assert.True(PersonalEquipmentRules.IsPhysicallyCarried(world.Society.Inventory, world.Society.Inventory.GetLot("market-milk"), seller));
        world.Validate();

        async Task Until(Func<bool> reached)
        {
            for (var tick = 0; tick < 40 && !reached(); tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True(reached(), "Milk Market phase did not complete: " + choices.Mode);
        }
    }

    private sealed class Choices(string seller, string buyer) : IDecisionProvider
    {
        public string Mode { get; set; } = "borrow";
        public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var observation = request.Observation;
            var candidates = observation.Candidates;
            var choice = candidates.FirstOrDefault(candidate => observation.InhabitantId == seller && (Mode switch
            {
                "borrow" => candidate.Description.Contains("borrow this empty", StringComparison.Ordinal),
                "stock" => candidate.Id.StartsWith("animal:milk_stock:", StringComparison.Ordinal),
                "sale" => candidate.Id.StartsWith("animal:milk_offer:", StringComparison.Ordinal),
                "leave" => candidate.Description.StartsWith("Give up this stall", StringComparison.Ordinal),
                "recover" => candidate.Description.StartsWith("Collect 1 of your water_jug", StringComparison.Ordinal),
                _ => false,
            }) || observation.InhabitantId == buyer && Mode == "sale" && candidate.Id.StartsWith("animal:milk_accept:", StringComparison.Ordinal)) ??
                candidates.Single(candidate => candidate.Id == "safe_idle");
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, observation.InhabitantId, Kind, 0,
                observation.RunEpoch, observation.DecisionGeneration, observation.ObservationDigest, choice.Id, 1,
                candidates.ToDictionary(candidate => candidate.Id, candidate => candidate.Id == choice.Id ? 1d : 0d)));
        }
    }
}

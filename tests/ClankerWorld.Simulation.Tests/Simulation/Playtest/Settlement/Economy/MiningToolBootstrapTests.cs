using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class MiningToolBootstrapTests
{
    private const string Alpha = "household:camp-alpha";
    private const string Beta = "household:camp-beta";
    private const string Smith = "first-town-blacksmith";

    [Fact]
    public async Task FarmingHouseholdRequestsPaysForAndUsesAPickWithoutGainingPrivateSmithRights()
    {
        using var seed = NormalPathWorld.CreateGenerated("probe-a", _ => new LaneChoice([]));
        var state = seed.ExportState();
        var buyer = seed.Society.Inhabitants.First(person => person.HouseholdId == Alpha).Id;
        var smith = seed.Society.Inhabitants.First(person => person.HouseholdId == Beta).Id;
        var building = seed.WorldSimulation.Buildings.Single(item => item.InstanceId == Smith);
        var inventory = InventoryFixture.Transfer(state.Society.Society.Inventory, "claim-starter-pick",
            building.TownId!, smith, "first-town-wooden-pickaxe", 1, "equipment_collected");
        var smithWood = inventory.Lots.First(lot => lot.OwnerId == Beta && lot.ItemKind == "wood");
        inventory = InventoryFixture.Transfer(inventory, "stock-order-wood", Beta, smith, smithWood.Id, 3, "picked_up");
        var carried = inventory.Lots.Single(lot => lot.OwnerId == smith && lot.ItemKind == "wood");
        inventory = InventoryFixture.Transfer(inventory, "deliver-order-wood", smith, Beta, carried.Id, 3, "delivered", Smith);
        var payment = inventory.Lots.First(lot => lot.OwnerId == Alpha && lot.ItemKind == "wood");
        inventory = InventoryFixture.Transfer(inventory, "collect-tool-payment", Alpha, buyer, payment.Id, 1, "picked_up");
        state = state with
        {
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
            Inhabitants = state.Inhabitants.Select(person => person with
            {
                Position = person.InhabitantId == smith ? building.Position : person.InhabitantId == buyer
                    ? state.Map.FootNeighbors(building.Position).First() : person.Position,
                HungerBasisPoints = 9_500,
                LastDecisionContext = null,
            }).ToArray(),
        };
        var buyerChoice = new LaneChoice(["business_request_tool:", "business_collect:", "business_buy:", "business_payment:", "gather_building_material:stone"]);
        var smithChoice = new LaneChoice(["business_make_tool:", "business_serve:", "business_list:" + Smith]);
        using var world = PrivateWorldRuntime.Restore(state, actor => actor == buyer ? buyerChoice : actor == smith ? smithChoice : new LaneChoice([]));
        var woodenRecipe = world.WorldContent.Recipes.Single(recipe => recipe.LocalId == "wooden-pickaxe");
        Assert.False(world.StartProduction(woodenRecipe.CanonicalId, Smith, buyer).Applied);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var order = Assert.Single(world.BusinessTrade.ToolOrders);
        Assert.Equal((buyer, Beta, Smith, "wooden_pickaxe"), (order.BuyerId, order.HouseholdId, order.BuildingId, order.ToolKind));
        var requestedPick = "business_request_tool:" + Smith + "|wooden_pickaxe";
        Assert.Contains(requestedPick, buyerChoice.Offered);
        Assert.Contains(requestedPick, buyerChoice.Selected);
        Assert.Contains("farm:hoe:wooden_hoe", buyerChoice.DeterministicFirst);
        Assert.True(buyerChoice.FirstPriorities["farm:hoe:wooden_hoe"] <
            buyerChoice.FirstPriorities[requestedPick]);
        for (var tick = 0; tick < 120 && !world.BusinessTrade.ToolOrders.Any(item => item.State == "ready"); tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var ready = world.BusinessTrade.ToolOrders.Single(item => item.Id == order.Id);
        Assert.Equal("ready", ready.State);
        var made = world.Society.Inventory.Lots.Single(lot => lot.ItemKind == "wooden_pickaxe" && lot.StorageBuildingId == Smith);
        Assert.Equal(Beta, made.OwnerId);
        Assert.False(world.WithdrawBusinessStock(buyer, Smith, made.Id, 1).Applied);
        var job = world.WorldSimulation.ProductionJobs.Single(item => item.JobId == ready.JobId);
        Assert.Equal(3, job.InputReservationIds.Sum(id => world.Society.Inventory.GetReservation(id).Quantity));
        Assert.All(job.InputReservationIds, id => Assert.Equal(InventoryReservationState.Completed, world.Society.Inventory.GetReservation(id).State));
        for (var tick = 0; tick < 180 && !world.BusinessTrade.Offers.Any(offer => offer.GoodsKind == "wooden_pickaxe" && offer.State == BusinessOfferState.Settled); tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var exchange = Assert.Single(world.BusinessTrade.Offers, offer => offer.GoodsKind == "wooden_pickaxe" && offer.State == BusinessOfferState.Settled);
        Assert.Equal((buyer, smith, 1, "wood", 1), (exchange.BuyerId, exchange.SellerId, exchange.GoodsQuantity, exchange.PaymentKind, exchange.PaymentQuantity));
        var ownedPick = world.Society.Inventory.Lots.Single(lot => lot.OwnerId == buyer && lot.ItemKind == "wooden_pickaxe");
        Assert.Null(ownedPick.StorageBuildingId);
        Assert.Contains(world.Society.Inventory.Lots, lot => lot.ItemKind == "wood" && lot.OwnerId == Beta && lot.StorageBuildingId == Smith &&
            (lot.Id == exchange.PaymentLotId || lot.ProvenanceLotId == exchange.PaymentLotId));
        for (var tick = 0; tick < 160 && !world.Society.Inventory.Lots.Any(lot => lot.OwnerId == buyer && lot.ItemKind == "stone"); tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Contains(world.Society.Inventory.Lots, lot => lot.OwnerId == buyer && lot.ItemKind == "stone" && lot.Quantity > 0);
        Assert.True(world.Society.Inventory.GetLot(ownedPick.Id).ConditionBasisPoints < 10_000);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Single(world.BusinessTrade.ToolOrders);
        Assert.Contains(buyerChoice.Offered, id => id.StartsWith("build:building:", StringComparison.Ordinal) && id.Contains("/building/blacksmith-1x2@", StringComparison.Ordinal));
        Assert.False(world.StartProduction(woodenRecipe.CanonicalId, Smith, buyer).Applied);
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())), _ => new LaneChoice([]));
        restored.Validate();
    }

    [Fact]
    public async Task StockedSmithPrioritizesTheMissingStonePickAndOffersRealOreWork()
    {
        using var seed = NormalPathWorld.CreateGenerated("identity-pause", _ => new LaneChoice([]));
        var state = seed.ExportState();
        var actor = seed.Society.Inhabitants.First(person => person.HouseholdId == Beta).Id;
        var building = seed.WorldSimulation.Buildings.Single(item => item.InstanceId == Smith);
        Assert.Contains(state.Map.Resources, resource => resource.Kind == "iron_ore" &&
            state.Map.IsReachableOnFoot(building.Position, resource.Position));
        var inventory = InventoryFixture.Transfer(state.Society.Society.Inventory, "claim-starter-pick",
            building.TownId!, actor, "first-town-wooden-pickaxe", 1, "equipment_collected");
        inventory = InventoryFixture.AddLot(inventory, "mined-smith-stone", "stone", Beta, 2, storageBuildingId: Smith);
        inventory = InventoryFixture.AddLot(inventory, "smith-wood", "wood", Beta, 2, storageBuildingId: Smith);
        state = state with
        {
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { Position = building.Position, HungerBasisPoints = 9_500, LastDecisionContext = null } : person).ToArray(),
        };
        var recorder = new ActionCoverageRecorder();
        using var world = PrivateWorldRuntime.Restore(state, id => id == actor ? recorder : new LaneChoice([]));
        // Initial clothing collection remains ahead of optional work.
        for (var tick = 0; tick < 12 && world.Inhabitants.Single(person => person.InhabitantId == actor).Project is null; tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.True(world.Inhabitants.Single(person => person.InhabitantId == actor).Project is not null,
            "Chosen: " + string.Join(",", recorder.Chosen.Select(choice => choice.Key + "=" + choice.Value)) +
            "; offered: " + string.Join(",", recorder.OfferedByAgent.GetValueOrDefault(actor)?.Keys ?? []));
        Assert.Equal("build:recipe:" + world.WorldContent.Recipes.Single(recipe => recipe.LocalId == "stone-pickaxe").CanonicalId,
            world.Inhabitants.Single(person => person.InhabitantId == actor).Project!.CandidateId);
        for (var tick = 0; tick < 70 && !world.Society.Inventory.Lots.Any(lot => lot.OwnerId == actor && lot.ItemKind == "stone_pickaxe"); tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var pick = Assert.Single(world.Society.Inventory.Lots, lot => lot.OwnerId == actor && lot.ItemKind == "stone_pickaxe");
        Assert.Null(pick.StorageBuildingId);
        Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.Id is "mined-smith-stone" or "smith-wood");
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Contains("gather_smith_ore", recorder.FamiliesOfferedTo(actor, world.WorldContent));
        var oreChoice = new LaneChoice(["gather_smith_ore", "deliver_smith_ore", "haul_household_stock"]);
        using var mining = PrivateWorldRuntime.Restore(world.ExportState(), id => id == actor ? oreChoice : new LaneChoice([]));
        for (var tick = 0; tick < 220 && !mining.Society.Inventory.Lots.Any(lot => lot.OwnerId == Beta && lot.ItemKind == "iron_ore" && lot.StorageBuildingId == Smith); tick++)
            Assert.True((await mining.AdvanceOneTickAsync()).Advanced);
        Assert.True(mining.Society.Inventory.Lots.Any(lot => lot.OwnerId == Beta && lot.ItemKind == "iron_ore" && lot.StorageBuildingId == Smith && lot.Quantity > 0),
            "Position=" + mining.Inhabitants.Single(person => person.InhabitantId == actor).Position + "; carried=" +
            string.Join(';', mining.Society.Inventory.Lots.Where(lot => lot.OwnerId == actor).Select(lot => lot.ItemKind + "=" + lot.Quantity + " delivery=" + lot.DeliveryBuildingId)));
        Assert.True(mining.Society.Inventory.GetLot(pick.Id).ConditionBasisPoints < 10_000);
        mining.Validate();
    }

    private sealed class LaneChoice(string[] prefixes) : IDecisionProvider
    {
        public HashSet<string> Offered { get; } = new(StringComparer.Ordinal);
        public HashSet<string> DeterministicFirst { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Selected { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, int> FirstPriorities { get; } = new(StringComparer.Ordinal);
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            Offered.UnionWith(request.Observation.Candidates.Select(candidate => candidate.Id));
            foreach (var candidate in request.Observation.Candidates)
                FirstPriorities.TryAdd(candidate.Id, candidate.DeterministicPriority);
            DeterministicFirst.Add(request.Observation.Candidates.OrderBy(candidate => candidate.DeterministicPriority)
                .ThenBy(candidate => candidate.Id, StringComparer.Ordinal).First().Id);
            var selected = prefixes.Select(prefix => request.Observation.Candidates.FirstOrDefault(candidate =>
                    candidate.Id.StartsWith(prefix, StringComparison.Ordinal))).FirstOrDefault(candidate => candidate is not null)
                ?? request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle");
            Selected.Add(selected.Id);
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, request.Observation.InhabitantId, Kind,
                ProviderEpoch, request.Observation.RunEpoch, request.Observation.DecisionGeneration,
                request.Observation.ObservationDigest, selected.Id, 1, new Dictionary<string, double> { [selected.Id] = 1 }));
        }
    }
}

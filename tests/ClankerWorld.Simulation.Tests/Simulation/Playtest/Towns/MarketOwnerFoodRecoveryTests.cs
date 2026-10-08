using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed class MarketOwnerFoodRecoveryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HungryOwnersRetrieveAndEatTheirActuallyDepositedMarketFood(bool urgent)
    {
        var state = await PaidMarketWorld.StateAsync();
        var seller = state.Inhabitants.OrderBy(person => person.InhabitantId, StringComparer.Ordinal).First().InhabitantId;
        var market = PaidMarketWorld.Market(state);
        var inventory = state.Society.Society.Inventory with
        {
            Lots = state.Society.Society.Inventory.Lots.Where(lot => !FoodKinds.Contains(lot.ItemKind)).ToArray(),
        };
        inventory = InventoryFixture.AddLot(inventory, "hungry-market-owner-berries", "berries", seller, 6);
        inventory = InventoryFixture.AddLot(inventory, "hungry-market-owner-wood", "wood", seller, 2);
        var foodSources = state.Map.Resources.Where(resource => resource.Kind is "food" or "fruit")
            .Select(resource => resource.Id).ToHashSet();
        state = PaidMarketWorld.At(PaidMarketWorld.WithInventory(state, inventory), seller,
            MarketContent.StallEntrance(market.Site, 0)) with
        {
            JevEnabled = false,
            RoutineHelper = RoutineHelperSettings.Off,
            Resources = state.Resources.Select(resource => foodSources.Contains(resource.ResourceId)
                ? resource with { State = ResourceState.Depleted } : resource).ToArray(),
            WorldSystems = state.WorldSystems! with
            {
                Ecology = state.WorldSystems.Ecology with
                {
                    Resources = state.WorldSystems.Ecology.Resources.Select(resource => foodSources.Contains(resource.Id)
                        ? resource with { Quantity = 0, State = EcologyResourceState.Depleted, NextRegenerationDay = 100 }
                        : resource).ToArray(),
                },
            },
        };
        var stocking = new MarketRulesPolicy
        {
            Choose = (actor, candidates) => actor == seller
                ? candidates.FirstOrDefault(candidate => candidate.Id.StartsWith("market_borrow:", StringComparison.Ordinal)) ??
                    candidates.FirstOrDefault(candidate => candidate.Id.StartsWith("market_deposit:", StringComparison.Ordinal) &&
                        (candidate.Description.Contains(" berries ", StringComparison.Ordinal) ||
                         candidate.Description.Contains(" wood ", StringComparison.Ordinal))) ??
                    candidates.Single(candidate => candidate.Id == "safe_idle")
                : candidates.Single(candidate => candidate.Id == "safe_idle"),
        };
        using var setup = PrivateWorldRuntime.Restore(state, stocking.CreateProvider);
        setup.Validate();
        for (var tick = 0; tick < 20 && PaidMarketWorld.Market(setup).StockReceipts.Count < 2; tick++)
            Assert.True((await setup.AdvanceOneTickAsync()).Advanced);
        var deposits = PaidMarketWorld.Market(setup).StockReceipts;
        var food = Assert.Single(deposits, receipt => receipt.ItemKind == "berries");
        var wood = Assert.Single(deposits, receipt => receipt.ItemKind == "wood");
        Assert.Equal((seller, 4), (food.OwnerId, food.Quantity));
        Assert.Equal((seller, 2), (wood.OwnerId, wood.Quantity));
        var ready = setup.ExportState();
        ready = ready with
        {
            Inhabitants = ready.Inhabitants.Select(person => person.InhabitantId == seller
                ? person with { HungerBasisPoints = 1_000, LastDecisionContext = null }
                : person).ToArray(),
        };
        var eating = new MarketRulesPolicy
        {
            Choose = (actor, candidates) => candidates.FirstOrDefault(candidate =>
                actor == seller && candidate.Id == "consume_food") ?? candidates.Single(candidate => candidate.Id == "safe_idle"),
        };
        using var consumed = PrivateWorldRuntime.Restore(ready, eating.CreateProvider);
        var mealsBefore = Meals(consumed, seller);
        for (var tick = 0; tick < 12 && Meals(consumed, seller) < mealsBefore + 2; tick++)
            Assert.True((await consumed.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(mealsBefore + 2, Meals(consumed, seller));
        Assert.DoesNotContain(consumed.Society.Inventory.Lots, lot => lot.OwnerId == seller && lot.ItemKind == "berries" &&
            PersonalEquipmentRules.IsCarried(lot, seller));
        ready = consumed.ExportState();
        ready = ready with
        {
            Inhabitants = ready.Inhabitants.Select(person => person.InhabitantId == seller
                ? person with { HungerBasisPoints = urgent ? 1_900 : 2_100, LastDecisionContext = null }
                : person).ToArray(),
        };
        var choices = RecoveryChoices(seller);
        using var world = PrivateWorldRuntime.Restore(ready, choices.CreateProvider);
        using var replay = PrivateWorldRuntime.Restore(ready, RecoveryChoices(seller).CreateProvider);
        world.Validate();
        var before = Meals(world, seller);
        for (var tick = 0; tick < 12; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        }
        Assert.Contains(choices.OfferedTo(seller), candidate => candidate.Id.StartsWith("market_collect:", StringComparison.Ordinal) &&
            candidate.Description.Contains(" berries ", StringComparison.Ordinal));
        Assert.True(Meals(world, seller) > before);
        Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.Id == food.LotId && lot.GroundPosition is not null);
        Assert.Equal((seller, 2), (world.Society.Inventory.GetLot(wood.LotId).OwnerId, world.Society.Inventory.GetLot(wood.LotId).Quantity));
        if (urgent)
            Assert.DoesNotContain(choices.Offered.First(item => item.Actor == seller).Candidates, candidate => candidate.Id.StartsWith("market_collect:", StringComparison.Ordinal) &&
                candidate.Description.Contains(" wood ", StringComparison.Ordinal));
        world.Validate();
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        restored.Validate();
    }

    private static int Meals(PrivateWorldRuntime world, string actor) => world.ExportState().Events.Count(item =>
        item.Kind == "meal_eaten" && item.Detail.StartsWith(actor + ":", StringComparison.Ordinal));

    private static readonly HashSet<string> FoodKinds = new(StringComparer.Ordinal)
    {
        "food", "fruit", "berries", "wild_greens", "cultivated_greens", "simple_meal", "porridge", "berry_porridge",
        "fruit_porridge", "bread", "stew", "restaurant_meal", "cooked_eggs", "milk_porridge", "rich_meal",
    };

    private static MarketRulesPolicy RecoveryChoices(string seller) => new()
    {
        Choose = (actor, candidates) => actor == seller
            ? candidates.FirstOrDefault(candidate => candidate.Id == "consume_food") ??
                candidates.FirstOrDefault(candidate => candidate.Id.StartsWith("market_collect:", StringComparison.Ordinal) &&
                    candidate.Description.Contains(" berries ", StringComparison.Ordinal)) ??
                candidates.Single(candidate => candidate.Id == "safe_idle")
            : candidates.Single(candidate => candidate.Id == "safe_idle"),
    };
}

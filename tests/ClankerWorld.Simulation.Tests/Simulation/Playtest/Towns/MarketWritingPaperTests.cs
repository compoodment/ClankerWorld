using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class MarketWritingPaperTests
{
    private const string SalePaper = "writer-market-paper";
    private const string Fiber = "writer-paper-fiber";
    private const string Jug = "writer-paper-jug";
    private const string Water = "writer-paper-water";
    private static readonly Lazy<Task<byte[]>> Prepared = new(PrepareAsync);

    [Theory]
    [InlineData("active", 1, 4)]
    [InlineData("missing", 1, 0)]
    [InlineData("left", 0, 3)]
    public async Task HouseholdMakesReplacementPaperWithoutTakingItsMarketStock(
        string boundary, int expectedJobs, int expectedSalePaper)
    {
        var state = PrivateWorldRuntimeCodec.Decode(await Prepared.Value);
        var seller = state.Inhabitants[0].InhabitantId;
        var household = PaidMarketWorld.HouseholdOf(state, seller);
        var writer = Writer(state, seller, household);
        var receipt = Assert.Single(PaidMarketWorld.Market(state).StockReceipts);
        if (boundary == "missing")
            state = PaidMarketWorld.WithInventory(state, state.Society.Society.Inventory with
            {
                Lots = state.Society.Society.Inventory.Lots.Where(lot => !MarketTradeRules.IsReceiptLot(receipt, lot)).ToArray(),
            });
        if (boundary == "left")
        {
            using var leaving = PrivateWorldRuntime.Restore(state, Policy(seller, "market_leave:").CreateProvider);
            for (var tick = 0; tick < 12 && PaidMarketWorld.Market(leaving).Occupancies.Any(item => item.EndedTick is null); tick++)
                Assert.True((await leaving.AdvanceOneTickAsync()).Advanced);
            Assert.DoesNotContain(PaidMarketWorld.Market(leaving).Occupancies, item => item.EndedTick is null);
            state = FreshDecisions(leaving.ExportState());
        }
        var recipe = state.WorldContent!.Recipes.Single(item => item.LocalId == "house-paper");
        var recipeCandidate = TownConstructionCandidateIds.Recipe(recipe.CanonicalId);
        var choices = Policy(writer, recipeCandidate, "knowledge_write:field_record", "knowledge_continue");
        using var world = PrivateWorldRuntime.Restore(state, choices.CreateProvider);
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)),
            Policy(writer, recipeCandidate, "knowledge_write:field_record", "knowledge_continue").CreateProvider);
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False((await world.AdvanceOneTickAsync(commitPermitted: () => false)).Advanced);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        for (var tick = 0; tick < 70 && world.ExportState().Knowledge!.Artifacts.Count == 0; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        }
        var final = world.ExportState();
        var record = Assert.Single(final.Knowledge!.Artifacts);
        Assert.Equal((writer, "field_record"), (record.CreatorId, record.Kind));
        Assert.Contains(choices.OfferedTo(writer), item => item.Id == "knowledge_write:field_record");
        Assert.All(record.Facts, fact => Assert.Contains(final.Knowledge.Facts,
            learned => learned.OwnerId == writer && AgentKnowledgeRules.SameDiscovery(learned, fact)));
        var material = Assert.Single(record.Materials);
        Assert.Equal(("paper", 1), (material.ItemKind, material.Quantity));
        Assert.Equal(InventoryReservationState.Completed, final.Society.Society.Inventory.GetReservation(material.ReservationId).State);
        Assert.Equal(1, final.Society.Society.Inventory.GetLot(record.LotId).Quantity);
        var jobs = final.WorldSimulation!.ProductionJobs.Where(item => item.RecipeId == recipe.CanonicalId).ToArray();
        Assert.Equal(expectedJobs, jobs.Length);
        Assert.All(jobs, job => Assert.Equal(WorldProductionJobState.Completed, job.State));
        Assert.Equal(expectedSalePaper, final.Society.Society.Inventory.Lots.Where(lot => MarketTradeRules.IsReceiptLot(receipt, lot)).Sum(lot => lot.Quantity));
        Assert.Equal(boundary == "left" ? 0 : 1, PaidMarketWorld.Market(final).Occupancies.Count(item => item.EndedTick is null));
        Assert.Equal(1, final.Society.Society.Inventory.GetLot(Jug).Quantity);
        Assert.Equal(expectedJobs == 0 ? 2 : 0, final.Society.Society.Inventory.Lots.Where(lot => lot.Id == Fiber).Sum(lot => lot.Quantity));
        Assert.Equal(expectedJobs == 0 ? 1 : 0, final.Society.Society.Inventory.Lots.Where(lot => lot.Id == Water).Sum(lot => lot.Quantity));
        Assert.Equal(expectedSalePaper + expectedJobs, final.Society.Society.Inventory.Lots.Where(lot => lot.ItemKind == "paper").Sum(lot => lot.Quantity));
        Assert.Equal(boundary != "left", choices.OfferedTo(writer).Any(item => item.Id == recipeCandidate));
        AssertReload(world);
    }

    [Theory]
    [InlineData("house")]
    [InlineData("collected")]
    [InlineData("reserved")]
    public async Task AvailableCollectedAndReservedSheetsStillPreventReplacementProduction(string reserve)
    {
        var state = PrivateWorldRuntimeCodec.Decode(await Prepared.Value);
        var seller = state.Inhabitants[0].InhabitantId;
        var household = PaidMarketWorld.HouseholdOf(state, seller);
        var writer = Writer(state, seller, household);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "writer-reserve-paper", "paper",
            reserve == "house" ? household : writer, 2,
            storageBuildingId: reserve == "house" ? PaidMarketWorld.HouseOf(state, household).InstanceId : null);
        if (reserve == "reserved")
            inventory = InventoryFixture.Reserve(inventory, "writer-reserve-commitment", writer, "writer-reserve-paper", 2,
                "committed writing sheets", state.Society.Society.WorldTick + 200);
        state = PaidMarketWorld.WithInventory(state, inventory);
        var recipe = state.WorldContent!.Recipes.Single(item => item.LocalId == "house-paper");
        var candidate = TownConstructionCandidateIds.Recipe(recipe.CanonicalId);
        var choices = Policy(writer, candidate);
        using var world = PrivateWorldRuntime.Restore(state, choices.CreateProvider);
        for (var tick = 0; tick < 8; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.DoesNotContain(choices.OfferedTo(writer), item => item.Id == candidate);
        Assert.DoesNotContain(world.WorldSimulation.ProductionJobs, item => item.RecipeId == recipe.CanonicalId);
        Assert.Equal(2, world.Society.Inventory.GetLot("writer-reserve-paper").Quantity);
        Assert.Equal(2, world.Society.Inventory.GetLot(Fiber).Quantity);
        Assert.Equal(1, world.Society.Inventory.GetLot(Water).Quantity);
        Assert.Empty(world.Knowledge.Artifacts);
        if (reserve == "reserved")
            Assert.Equal(InventoryReservationState.Reserved, world.Society.Inventory.GetReservation("writer-reserve-commitment").State);
        AssertReload(world);
    }

    private static async Task<byte[]> PrepareAsync()
    {
        var state = await PaidMarketWorld.StateAsync();
        var seller = state.Inhabitants[0].InhabitantId;
        var household = PaidMarketWorld.HouseholdOf(state, seller);
        var writer = Writer(state, seller, household);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, SalePaper, "paper", household, 4);
        inventory = InventoryFixture.Relocate(inventory, "writer-market-load", SalePaper, household, 4, carrierId: seller);
        state = PaidMarketWorld.At(PaidMarketWorld.WithInventory(state, inventory), seller,
            MarketContent.StallEntrance(PaidMarketWorld.Market(state).Site, 0)) with
        { JevEnabled = false, RoutineHelper = RoutineHelperSettings.Off };
        using (var stocking = PrivateWorldRuntime.Restore(state, Policy(seller, "market_borrow:", "market_deposit:").CreateProvider))
        {
            for (var tick = 0; tick < 30 && PaidMarketWorld.Market(stocking).StockReceipts.Count == 0; tick++)
                Assert.True((await stocking.AdvanceOneTickAsync()).Advanced);
            var receipt = Assert.Single(PaidMarketWorld.Market(stocking).StockReceipts);
            Assert.Equal((household, "paper", 4), (receipt.OwnerId, receipt.ItemKind, receipt.Quantity));
            state = FreshDecisions(stocking.ExportState());
        }
        using (var exploring = PrivateWorldRuntime.Restore(state, Policy(writer, "explore").CreateProvider))
        {
            for (var tick = 0; tick < 80 && !exploring.ExportState().Knowledge!.Facts.Any(fact => fact.OwnerId == writer); tick++)
                Assert.True((await exploring.AdvanceOneTickAsync()).Advanced);
            state = FreshDecisions(exploring.ExportState());
            Assert.Contains(state.Knowledge!.Facts, fact => fact.OwnerId == writer && fact.Acquisition == "firsthand");
        }
        var house = PaidMarketWorld.HouseOf(state, household);
        inventory = state.Society.Society.Inventory;
        inventory = InventoryFixture.AddLot(inventory, Fiber, "fiber", household, 2, storageBuildingId: house.InstanceId);
        inventory = InventoryFixture.AddLot(inventory, Jug, "water_jug", household, 1, storageBuildingId: house.InstanceId);
        inventory = InventoryFixture.AddLot(inventory, Water, "fresh_water", household, 1,
            storageBuildingId: house.InstanceId, containerLotId: Jug);
        state = PaidMarketWorld.At(PaidMarketWorld.WithInventory(state, inventory), writer, house.Position);
        using var validated = PrivateWorldRuntime.Restore(state);
        validated.Validate();
        return PrivateWorldRuntimeCodec.Encode(validated.ExportState());
    }

    private static string Writer(PrivateWorldRuntimeState state, string seller, string household) =>
        state.Society.Society.Inhabitants.First(person => person.HouseholdId == household && person.Id != seller).Id;

    private static PrivateWorldRuntimeState FreshDecisions(PrivateWorldRuntimeState state) => state with
    {
        Inhabitants = state.Inhabitants.Select(person => person with
        { HungerBasisPoints = 9_500, Survival = new SurvivalCondition(), LastDecisionContext = null, Project = null }).ToArray(),
    };

    private static MarketRulesPolicy Policy(string selectedActor, params string[] actions) => new()
    {
        Choose = (actor, candidates) => actor == selectedActor
            ? actions.Select(prefix => candidates.FirstOrDefault(candidate => candidate.Id.StartsWith(prefix, StringComparison.Ordinal)))
                .FirstOrDefault(candidate => candidate is not null) ?? candidates.Single(candidate => candidate.Id == "safe_idle")
            : candidates.Single(candidate => candidate.Id == "safe_idle"),
    };

    private static void AssertReload(PrivateWorldRuntime world)
    {
        world.Validate();
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes));
        restored.Validate();
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }
}

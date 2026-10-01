using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed class ClothingCarryTests
{
    private const string Alpha = "household:camp-alpha";
    private const string House = "first-town-house-a";

    [Fact]
    public async Task HouseholdMakesRopeAndBasketAndEquipsItThroughOrdinaryChoicesAcrossReload()
    {
        var state = Initial("carry-house-flow");
        var actor = Actor(state);
        state = Stock(state, "house-craft-fiber", "fiber", Alpha, 6, House);
        var rope = state.WorldContent!.Recipes.Single(recipe => recipe.LocalId == "twist-rope");
        var basket = state.WorldContent.Recipes.Single(recipe => recipe.LocalId == "weave-basket");
        var allowed = new[] { "equip_carry:basket", "build:recipe:" + basket.CanonicalId,
            "build:recipe:" + rope.CanonicalId, "supply_workstation:fiber", "haul_household_stock" };
        IDecisionProvider Provider(string id) => new Preferred(id == actor ? allowed : []);
        var world = PrivateWorldRuntime.Restore(state, Provider);
        try
        {
            var reloaded = false;
            for (var tick = 0; tick < 800 && world.ExportState().Inhabitants.Single(person => person.InhabitantId == actor).Equipment?.CarryAidLotId is null; tick++)
            {
                Assert.True((await world.AdvanceOneTickAsync()).Advanced);
                if (!reloaded && world.WorldSimulation.ProductionJobs.Any(job => job.RecipeId == rope.CanonicalId &&
                    job.State == WorldProductionJobState.Running))
                {
                    var saved = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState()));
                    world.Dispose();
                    world = PrivateWorldRuntime.Restore(saved, Provider);
                    reloaded = true;
                }
            }
            Assert.True(reloaded);
            var person = world.ExportState().Inhabitants.Single(person => person.InhabitantId == actor);
            var aid = world.Society.Inventory.GetLot(Assert.IsType<string>(person.Equipment?.CarryAidLotId));
            Assert.Equal("basket", aid.ItemKind);
            Assert.Equal(actor, aid.OwnerId);
            Assert.Null(aid.StorageBuildingId);
            Assert.Equal(48, CarryEquipmentRules.Capacity(world.Society.Inventory, person));
            Assert.Contains(world.WorldSimulation.ProductionJobs, job => job.RecipeId == rope.CanonicalId && job.State == WorldProductionJobState.Completed);
            Assert.Contains(world.WorldSimulation.ProductionJobs, job => job.RecipeId == basket.CanonicalId && job.State == WorldProductionJobState.Completed);
            Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.OwnerId == "household:camp-beta" && lot.ItemKind is "rope" or "basket");
            world.Validate();
        }
        finally { world.Dispose(); }
    }

    [Fact]
    public async Task TailorMakesCoatCloakAndSackFromItsOwnStock()
    {
        var initial = Stock(Initial("carry-tailor-products"), "shop-cost-fiber", "fiber", Alpha, 2, House);
        using var setup = PrivateWorldRuntime.Restore(initial, _ => new Preferred([]));
        var tailor = setup.WorldContent.Buildings.Single(building => building.LocalId == "tailor-shop-1x1");
        var house = setup.WorldSimulation.Buildings.Single(building => building.InstanceId == House);
        Assert.Contains(Enumerable.Range(-5, 11).SelectMany(dy => Enumerable.Range(-5, 11).Select(dx =>
                new GridPoint(house.Position.X + dx, house.Position.Y + dy)))
, site => setup.PlaceBuilding("carry-tailor", tailor.CanonicalId, site, Alpha).Applied);
        var state = Stock(Stock(Stock(setup.ExportState(), "tailor-cloth", "cloth", Alpha, 8, "carry-tailor"),
            "tailor-fiber", "fiber", Alpha, 1, "carry-tailor"), "tailor-rope", "rope", Alpha, 1, "carry-tailor");
        var actor = Actor(state);
        var shop = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == "carry-tailor");
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
            ? person with { Position = shop.Position } : person).ToArray()
        };
        using var world = PrivateWorldRuntime.Restore(state, _ => new Preferred([]));
        foreach (var localId in new[] { "sew-padded-coat", "sew-rain-cloak", "sew-sack" })
        {
            var recipe = world.WorldContent.Recipes.Single(recipe => recipe.LocalId == localId);
            var started = world.StartProduction(recipe.CanonicalId, shop.InstanceId, actor);
            Assert.True(started.Applied, started.Failure);
            for (var tick = 0; tick < recipe.DurationTicks; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(WorldProductionJobState.Completed, world.WorldSimulation.ProductionJobs.Single(job => job.JobId == started.JobId).State);
        }
        Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.Id is "tailor-cloth" or "tailor-fiber" or "tailor-rope");
        foreach (var kind in new[] { "padded_coat", "rain_cloak", "sack" })
            Assert.Single(world.Society.Inventory.Lots, lot => lot.OwnerId == Alpha && lot.StorageBuildingId == shop.InstanceId && lot.ItemKind == kind);
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }

    [Fact]
    public void SmallerAidAndRemovalAreRefusedUntilCargoIsDeliveredWithoutDiscardingGoods()
    {
        var state = Initial("carry-slot");
        var actor = Actor(state);
        state = Stock(Stock(Stock(state, "carried-sack", "sack", actor, 1), "carried-basket", "basket", actor, 1),
            "carried-wood", "wood", actor, 50);
        using var world = PrivateWorldRuntime.Restore(state, _ => new Preferred([]));
        Assert.True(world.EquipItem(actor, "carried-sack").Applied);
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False(world.EquipItem(actor, "carried-basket").Applied);
        Assert.False(world.RemoveCarryAid(actor).Applied);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        Assert.Equal(50, world.Society.Inventory.GetLot("carried-wood").Quantity);
        var after = world.ExportState();
        var moved = InventoryFixture.Transfer(after.Society.Society.Inventory, "reduce-cargo", actor, Alpha,
            "carried-wood", 20, "stored", House);
        after = after with { Society = after.Society with { Society = after.Society.Society with { Inventory = moved } } };
        using var unloaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(after)));
        Assert.True(unloaded.RemoveCarryAid(actor).Applied);
        Assert.Equal(50, unloaded.Society.Inventory.Lots.Where(lot => lot.ItemKind == "wood" &&
            (lot.Id == "carried-wood" || lot.ProvenanceLotId == "carried-wood")).Sum(lot => lot.Quantity));
        Assert.Equal(20, unloaded.Society.Inventory.Lots.Single(lot => lot.ProvenanceLotId == "carried-wood").Quantity);
    }

    [Fact]
    public async Task FullHandsLeaveFoodAtItsSourceAndAnEquippedSackMakesRoom()
    {
        var state = Initial("carry-harvest");
        var actor = Actor(state);
        var source = state.Map.GetResource("berry-patch");
        state = Stock(state, "full-load", "wood", actor, CarryEquipmentRules.BasicCapacity);
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
            ? person with { Position = source.Position, HungerBasisPoints = 3_000 } : person).ToArray()
        };
        IDecisionProvider Provider(string id) => new Preferred(id == actor ? ["harvest_food"] : []);
        var oldQuantity = state.WorldSystems!.Ecology.GetResource(source.Id).Quantity;
        using var full = PrivateWorldRuntime.Restore(state, Provider);
        for (var tick = 0; tick < 3; tick++) Assert.True((await full.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(oldQuantity, full.ExportState().WorldSystems!.Ecology.GetResource(source.Id).Quantity);
        Assert.DoesNotContain(full.Society.Inventory.Lots, lot => lot.OwnerId == actor && lot.ItemKind == "food");
        state = Stock(full.ExportState(), "new-sack", "sack", actor, 1);
        using var equipped = PrivateWorldRuntime.Restore(state, Provider);
        Assert.True(equipped.EquipItem(actor, "new-sack").Applied);
        for (var tick = 0; tick < 3 && !equipped.Society.Inventory.Lots.Any(lot => lot.OwnerId == actor && lot.ItemKind == "food"); tick++)
            Assert.True((await equipped.AdvanceOneTickAsync()).Advanced);
        Assert.Contains(equipped.Society.Inventory.Lots, lot => lot.OwnerId == actor && lot.ItemKind == "food" && lot.Quantity == 4);
        Assert.Equal(32, equipped.Society.Inventory.GetLot("full-load").Quantity);
        Assert.Equal(1, equipped.Society.Inventory.GetLot("new-sack").Quantity);
        equipped.Validate();
    }

    [Fact]
    public async Task CarriedClothingProtectsOnlyWhenWornAndRepairConsumesOnSiteCloth()
    {
        var state = Initial("carry-warmth");
        var actor = Actor(state);
        var outside = state.Map.Tiles.First(tile => state.Map.IsPassable(tile.Position) &&
            !state.WorldSimulation!.Buildings.Any(building => building.Position == tile.Position) &&
            state.Inhabitants.All(person => person.Position != tile.Position)).Position;
        state = WithWeather(Stock(state, "worn-coat", "padded_coat", actor, 1), WeatherKind.Snow);
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
            ? person with { Position = outside, Survival = new SurvivalCondition(WarmthBasisPoints: 5_000) } : person).ToArray()
        };
        using var carried = PrivateWorldRuntime.Restore(state, _ => new Preferred([]));
        using var worn = PrivateWorldRuntime.Restore(state, _ => new Preferred([]));
        Assert.True(worn.EquipItem(actor, "worn-coat").Applied);
        Assert.True((await carried.AdvanceOneTickAsync()).Advanced);
        Assert.True((await worn.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(4_940, carried.ExportState().Inhabitants.Single(person => person.InhabitantId == actor).Survival!.WarmthBasisPoints);
        Assert.Equal(5_020, worn.ExportState().Inhabitants.Single(person => person.InhabitantId == actor).Survival!.WarmthBasisPoints);

        state = Stock(worn.ExportState(), "repair-cloth", "cloth", Alpha, 1, House);
        var home = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == House);
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor ? person with { Position = home.Position } : person).ToArray(),
            Society = state.Society with
            {
                Society = state.Society.Society with
                {
                    Inventory = state.Society.Society.Inventory with
                    { Lots = state.Society.Society.Inventory.Lots.Select(lot => lot.Id == "worn-coat" ? lot with { ConditionBasisPoints = 3_000 } : lot).ToArray() }
                }
            },
        };
        using var repair = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)),
            id => new Preferred(id == actor ? ["repair_gear:"] : []));
        for (var tick = 0; tick < 60 && !repair.ExportState().Events.Any(item => item.Kind == "equipment_repaired"); tick++)
            Assert.True((await repair.AdvanceOneTickAsync()).Advanced);
        Assert.DoesNotContain(repair.Society.Inventory.Lots, lot => lot.Id == "repair-cloth");
        Assert.InRange(repair.Society.Inventory.GetLot("worn-coat").ConditionBasisPoints, 9_999, 10_000);
        Assert.Equal("worn-coat", repair.ExportState().Inhabitants.Single(person => person.InhabitantId == actor).Equipment!.WornClothingLotId);
    }

    [Fact]
    public void SaveRejectsEquipmentTakenFromAnotherOwnerOrTheWrongSlot()
    {
        var state = Initial("carry-invalid-equipment");
        var actor = Actor(state);
        state = Stock(state, "other-sack", "sack", Alpha, 1, House);
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
            ? person with { Equipment = new EquipmentState(CarryAidLotId: "other-sack") } : person).ToArray()
        };
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(state));
        state = Stock(state, "personal-clothes", "clothing", actor, 1);
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
            ? person with { Equipment = new EquipmentState(CarryAidLotId: "personal-clothes") } : person).ToArray()
        };
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(state));
    }

    private static PrivateWorldRuntimeState Initial(string seed)
    {
        using var world = NormalPathWorld.CreateGenerated(seed, _ => new Preferred([]));
        return WithWeather(world.ExportState() with
        {
            Survival = new SettlementSurvivalState(world.WorldTick, []),
            Inhabitants = world.ExportState().Inhabitants.Select(person => person with
            { HungerBasisPoints = 9_000, Survival = new SurvivalCondition() }).ToArray(),
        }, WeatherKind.Clear);
    }

    private static string Actor(PrivateWorldRuntimeState state) => state.Society.Society.Inhabitants.First(person => person.HouseholdId == Alpha).Id;

    private static PrivateWorldRuntimeState Stock(PrivateWorldRuntimeState state, string id, string kind, string owner, int quantity, string? building = null) =>
        state with
        {
            Society = state.Society with
            {
                Society = state.Society.Society with
                { Inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, id, kind, owner, quantity, storageBuildingId: building) }
            }
        };

    private static PrivateWorldRuntimeState WithWeather(PrivateWorldRuntimeState state, WeatherKind weather)
    {
        var profiles = Enum.GetValues<SeasonKind>().Select(season => new WeatherProfile(season,
            weather == WeatherKind.Clear ? 1 : 0, 0, weather == WeatherKind.Rain ? 1 : 0,
            weather == WeatherKind.Storm ? 1 : 0, weather == WeatherKind.Snow ? 1 : 0)).ToArray();
        return state with
        {
            WorldSystems = state.WorldSystems! with
            {
                RegionalWeather = null,
                Config = state.WorldSystems.Config with { WeatherProfiles = profiles },
                Climate = state.WorldSystems.Climate with { Weather = weather },
            }
        };
    }

    private sealed class Preferred(IReadOnlyList<string> prefixes) : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var candidate = prefixes.Select(prefix => request.Observation.Candidates.FirstOrDefault(item =>
                item.Id.StartsWith(prefix, StringComparison.Ordinal))).FirstOrDefault(item => item is not null)
                ?? request.Observation.Candidates.Single(item => item.Id == "safe_idle");
            return new DeterministicDecisionProvider().DecideAsync(request with
            { Observation = request.Observation with { Candidates = [candidate] } }, cancellationToken);
        }
    }
}

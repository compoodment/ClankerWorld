using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed class TailorContentTests
{
    private const string Alpha = "household:camp-alpha";
    private const string Beta = "household:camp-beta";

    [Fact]
    public void NewWorldsHaveTheTailorShopAndNoWeavingFrame()
    {
        using var world = NormalPathWorld.CreateGenerated("tailor-content", _ => new ActionCoverageRecorder(chooseIdle: true));
        var content = world.WorldContent;
        Assert.Contains(content.Buildings, building => building.LocalId == "tailor-shop-1x1" && building.Tags.Contains("tailor"));
        Assert.Contains(content.Recipes, recipe => recipe.LocalId == "weave-cloth" &&
            recipe.Outputs.Single().ResourceId == "cloth");
        Assert.Contains(content.Recipes, recipe => recipe.LocalId == "sew-clothing" &&
            recipe.Inputs.Single().ResourceId == "cloth" && recipe.Outputs.Single().ResourceId == "clothing");
        Assert.DoesNotContain(content.Buildings, building => building.LocalId == "weaving-frame");
        Assert.DoesNotContain(content.Recipes, recipe => recipe.DisplayName == "Woven clothing");

        // Each starting agent's garment waits in their household's House.
        foreach (var (household, house) in new[] { (Alpha, "first-town-house-a"), (Beta, "first-town-house-b") })
        {
            var members = world.Society.Inhabitants.Count(person => person.HouseholdId == household);
            Assert.Equal(2, members);
            Assert.Equal(members, world.Society.Inventory.Lots.Where(lot => lot.OwnerId == household &&
                lot.ItemKind == "clothing" && lot.StorageBuildingId == house).Sum(lot => lot.Quantity));
        }
    }

    [Fact]
    public async Task HouseholdTurnsFiberIntoClothAndClothingThroughOrdinaryChoicesAcrossReload()
    {
        var (state, shopId) = WorldWithTailorShop("tailor-flow", fiberInHouse: 6);
        var actor = state.Society.Society.Inhabitants.First(person => person.HouseholdId == Alpha).Id;
        var weave = state.WorldContent!.Recipes.Single(item => item.LocalId == "weave-cloth");
        var sew = state.WorldContent.Recipes.Single(item => item.LocalId == "sew-clothing");
        // The shop also stocks wood for leather tanning. Let ordinary supply
        // finish that input before another fiber load becomes the offered choice.
        var allowed = new[] { "haul_household_stock", "supply_workstation:fiber", "supply_workstation:wood",
            "build:recipe:" + weave.CanonicalId, "build:recipe:" + sew.CanonicalId };
        IDecisionProvider Provider(string id) => new AllowedChoices(id == actor ? allowed : []);

        var world = PrivateWorldRuntime.Restore(state, Provider);
        try
        {
            var reloaded = false;
            for (var tick = 0; tick < 1_500 && !TailorOutputs(world, shopId).Any(lot => lot.ItemKind == "clothing"); tick++)
            {
                Assert.True((await world.AdvanceOneTickAsync()).Advanced);
                if (!reloaded && world.WorldSimulation.ProductionJobs.Any(job => job.RecipeId == weave.CanonicalId &&
                        job.State == WorldProductionJobState.Running))
                {
                    // Interrupted work: save and load while cloth is being woven.
                    var saved = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState()));
                    world.Dispose();
                    world = PrivateWorldRuntime.Restore(saved, Provider);
                    reloaded = true;
                }
            }
            Assert.True(reloaded);
            Assert.Contains(world.ExportState().Events, item => item.Kind == "workstation_input_picked_up");

            var jobs = world.WorldSimulation.ProductionJobs.Where(job => job.State == WorldProductionJobState.Completed).ToArray();
            var woven = jobs.Count(job => job.RecipeId == weave.CanonicalId);
            var sewn = jobs.Count(job => job.RecipeId == sew.CanonicalId);
            Assert.InRange(woven, 2, 3);
            Assert.Equal(1, sewn);
            Assert.All(jobs, job => Assert.Equal(actor, job.WorkerId));
            // Exact accounting: the House's fiber plus any the agent gathered
            // once it ran out, three per cloth and two cloth per garment.
            var gathered = world.ExportState().Events.Where(item => item.Kind == "material_gathered" &&
                    item.Detail.StartsWith(actor + ":fiber:", StringComparison.Ordinal))
                .Sum(item => int.Parse(item.Detail[(actor.Length + ":fiber:".Length)..], System.Globalization.CultureInfo.InvariantCulture));
            Assert.Equal(6 + gathered - 3 * woven, HouseholdTotal(world, "fiber"));
            Assert.Equal(woven - 2 * sewn, HouseholdTotal(world, "cloth"));
            Assert.Equal(sewn, TailorOutputs(world, shopId).Where(lot => lot.ItemKind == "clothing").Sum(lot => lot.Quantity));
            Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.OwnerId == Beta && lot.ItemKind is "cloth" or "fiber");
            world.Validate();
        }
        finally
        {
            world.Dispose();
        }
    }

    [Fact]
    public async Task AnAdultWithoutClothingCollectsAGarmentFromTheShop()
    {
        var (state, shopId) = WorldWithTailorShop("tailor-wear", fiberInHouse: 0);
        var actor = state.Society.Society.Inhabitants.First(person => person.HouseholdId == Alpha).Id;
        var inventory = state.Society.Society.Inventory;
        foreach (var lot in inventory.Lots.Where(lot => lot.ItemKind == "clothing" && lot.Quantity > 0).ToArray())
        {
            inventory = InventoryFixture.Reserve(inventory, "worn-out-" + lot.Id, lot.OwnerId, lot.Id, lot.Quantity, "worn_out", 10_000);
            inventory = InventoryFixture.ConsumeReservation(inventory, "worn-out-" + lot.Id);
        }
        inventory = InventoryFixture.AddLot(inventory, "shop-garment", "clothing", Alpha, 1, storageBuildingId: shopId);
        state = WithSnow(state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } });
        using var world = PrivateWorldRuntime.Restore(state, id => new AllowedChoices(id == actor ? ["wear_clothing"] : []));
        for (var tick = 0; tick < 200 && !world.Society.Inventory.Lots.Any(lot => lot.OwnerId == actor &&
                 lot.ItemKind == "clothing" && lot.Quantity > 0); tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        // The whole garment moves from the shop's stock to the agent.
        var garment = world.Society.Inventory.GetLot("shop-garment");
        Assert.Equal(actor, garment.OwnerId);
        Assert.Null(garment.StorageBuildingId);
        Assert.Equal(1, garment.Quantity);
    }

    [Fact]
    public async Task OnlyTheHoldingHouseholdWorksAtItsTailorShop()
    {
        var (state, shopId) = WorldWithTailorShop("tailor-access", fiberInHouse: 0);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "shop-fiber", "fiber", Alpha, 6,
            storageBuildingId: shopId);
        state = state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } };
        var shop = state.WorldSimulation!.Buildings.Single(item => item.InstanceId == shopId);
        var outsider = state.Society.Society.Inhabitants.First(person => person.HouseholdId == Beta).Id;
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == outsider
                ? person with { Position = shop.Position, HungerBasisPoints = 9_000 } : person).ToArray(),
        };
        var recorder = new ActionCoverageRecorder(chooseIdle: true);
        using var world = PrivateWorldRuntime.Restore(state, _ => recorder);
        var weave = world.WorldContent.Recipes.Single(item => item.LocalId == "weave-cloth");
        var refused = world.StartProduction(weave.CanonicalId, shopId, outsider);
        Assert.False(refused.Applied);
        Assert.Contains("household", refused.Failure, StringComparison.Ordinal);

        for (var tick = 0; tick < 60; tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var betaFamilies = world.Society.Inhabitants.Where(person => person.HouseholdId == Beta)
            .SelectMany(person => recorder.FamiliesOfferedTo(person.Id, world.WorldContent)).ToHashSet();
        var alphaFamilies = world.Society.Inhabitants.Where(person => person.HouseholdId == Alpha)
            .SelectMany(person => recorder.FamiliesOfferedTo(person.Id, world.WorldContent)).ToHashSet();
        Assert.DoesNotContain("recipe:weave-cloth", betaFamilies);
        Assert.Contains("recipe:weave-cloth", alphaFamilies);
        Assert.Equal(6, world.Society.Inventory.GetLot("shop-fiber").Quantity);
    }

    [Fact]
    public async Task WithoutATailorShopNoClothIsMadeAndNothingPretendsToBe()
    {
        var recorder = new ActionCoverageRecorder(chooseIdle: true);
        using var world = NormalPathWorld.CreateGenerated("tailor-none", _ => recorder);
        for (var tick = 0; tick < 240; tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var families = recorder.FamiliesOffered(world.WorldContent);
        Assert.DoesNotContain(world.WorldSimulation.Buildings, building =>
            world.WorldContent.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId).Tags.Contains("tailor"));
        Assert.DoesNotContain("recipe:weave-cloth", families);
        Assert.DoesNotContain("recipe:sew-clothing", families);
        Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.ItemKind == "cloth");
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "recipe_completed" &&
            item.Detail.Contains("sew-clothing", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MissingFiberBlocksWeavingUntilItIsBroughtIn()
    {
        var (state, shopId) = WorldWithTailorShop("tailor-missing", fiberInHouse: 0);
        var shop = state.WorldSimulation!.Buildings.Single(item => item.InstanceId == shopId);
        var worker = state.Society.Society.Inhabitants.First(person => person.HouseholdId == Alpha).Id;
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == worker
                ? person with { Position = shop.Position, HungerBasisPoints = 9_000 } : person).ToArray(),
        };
        using var world = PrivateWorldRuntime.Restore(state, _ => new ActionCoverageRecorder(chooseIdle: true));
        var weave = world.WorldContent.Recipes.Single(item => item.LocalId == "weave-cloth");
        var refused = world.StartProduction(weave.CanonicalId, shopId, worker);
        Assert.False(refused.Applied);
        Assert.Contains("on-site", refused.Failure, StringComparison.Ordinal);
        Assert.Empty(world.WorldSimulation.ProductionJobs);
        await world.AdvanceOneTickAsync();
        world.Validate();
    }

    private static (PrivateWorldRuntimeState State, string ShopId) WorldWithTailorShop(string seed, int fiberInHouse)
    {
        using var generated = NormalPathWorld.CreateGenerated(seed, _ => new ActionCoverageRecorder(chooseIdle: true));
        var initial = generated.ExportState();
        var inventory = InventoryFixture.AddLot(initial.Society.Society.Inventory, "shop-cost-fiber", "fiber", Alpha, 2,
            storageBuildingId: "first-town-house-a");
        if (fiberInHouse > 0)
            inventory = InventoryFixture.AddLot(inventory, "house-fiber", "fiber", Alpha, fiberInHouse,
                storageBuildingId: "first-town-house-a");
        using var setup = PrivateWorldRuntime.Restore(initial with
        {
            Society = initial.Society with { Society = initial.Society.Society with { Inventory = inventory } },
        }, _ => new ActionCoverageRecorder(chooseIdle: true));
        var shop = setup.WorldContent.Buildings.Single(item => item.LocalId == "tailor-shop-1x1");
        var house = setup.WorldSimulation.Buildings.Single(item => item.InstanceId == "first-town-house-a");
        var placed = Enumerable.Range(-4, 9).SelectMany(dy => Enumerable.Range(-4, 9)
                .Select(dx => new GridPoint(house.Position.X + dx, house.Position.Y + dy)))
            .OrderBy(point => Math.Abs(point.X - house.Position.X) + Math.Abs(point.Y - house.Position.Y))
            .Any(point => setup.PlaceBuilding("alpha-tailor", shop.CanonicalId, point, Alpha).Applied);
        Assert.True(placed);
        return (PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(setup.ExportState())), "alpha-tailor");
    }

    private static PrivateWorldRuntimeState WithSnow(PrivateWorldRuntimeState state)
    {
        var systems = state.WorldSystems!;
        var profiles = Enum.GetValues<SeasonKind>().Select(season => new WeatherProfile(season, 0, 0, 0, 0, 1)).ToArray();
        return state with
        {
            WorldSystems = systems with
            {
                RegionalWeather = null,
                Config = systems.Config with { WeatherProfiles = profiles },
                Climate = systems.Climate with { Weather = WeatherKind.Snow },
            },
        };
    }

    private static IEnumerable<InventoryLot> TailorOutputs(PrivateWorldRuntime world, string shopId) =>
        world.Society.Inventory.Lots.Where(lot => lot.OwnerId == Alpha && lot.StorageBuildingId == shopId && lot.Quantity > 0);

    private static int HouseholdTotal(PrivateWorldRuntime world, string kind) => world.Society.Inventory.Lots
        .Where(lot => lot.ItemKind == kind && (lot.OwnerId == Alpha ||
            world.Society.Inhabitants.Any(person => person.Id == lot.OwnerId && person.HouseholdId == Alpha)))
        .Sum(lot => lot.Quantity);

    /// <summary>Chooses among the allowed candidates by the built-in rules, otherwise stays idle.</summary>
    private sealed class AllowedChoices(IReadOnlyList<string> allowed) : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            var permitted = request.Observation.Candidates
                .Where(candidate => allowed.Any(prefix => candidate.Id.StartsWith(prefix, StringComparison.Ordinal)))
                .ToArray();
            var choices = permitted.Length > 0 ? permitted
                : [request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle")];
            return new DeterministicDecisionProvider().DecideAsync(request with
            {
                Observation = request.Observation with { Candidates = choices },
            }, cancellationToken);
        }
    }
}

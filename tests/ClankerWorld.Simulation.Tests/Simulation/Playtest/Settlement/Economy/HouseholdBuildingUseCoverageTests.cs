using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Tests;

/// <summary>
/// The offline coverage check from #437: worlds built the normal way, run by
/// the built-in rule-based chooser with no model calls, record what each
/// agent is offered. Work at a household building follows who holds it, and a
/// household plans only the buildings it lacks.
/// </summary>
public sealed class HouseholdBuildingUseCoverageTests
{
    // Two normal generated days at 360 ticks per day, with an early exit only
    // after all independent work and ownership milestones have been observed.
    private const int CoverageTicks = 720;

    private static readonly string[] SharedBuildingFamilies =
        ["building:warehouse-2x2", "building:workshop", "building:weaving-frame", "building:shelter",
         "building:storage", "building:fire", "building:stone-hearth"];

    private static readonly string[] FarmWorkFamilies =
        ["farm:Till", "farm:Plant", "farm:Harvest", "farm:collect", "recipe:mill-grain", "building:blacksmith-1x2"];

    private static readonly string[] SmithWorkFamilies =
        ["recipe:wooden-axe", "recipe:wooden-pickaxe", "haul_smith_input", "building:farmhouse-1x1"];

    [Theory]
    [InlineData("probe-a")]
    public async Task HouseholdsWorkAtTheBuildingsTheyHoldAndPlanOnlyWhatTheyLack(string seed)
    {
        var recorder = new ActionCoverageRecorder();
        using var setup = NormalPathWorld.CreateGenerated(seed, _ => recorder);
        var state = setup.ExportState();
        var farmingHousehold = setup.WorldSimulation.Buildings.Single(item => item.InstanceId == "first-town-farmhouse").HouseholdId!;
        // Work is demand-driven. Start with a real shortage rather than
        // requiring unnecessary crop work while starter rations are plentiful.
        using var world = PrivateWorldRuntime.Restore(FarmFieldTests.FeedHouseholdFromAvailableStock(state, farmingHousehold),
            _ => recorder);
        for (var tick = 0; tick < CoverageTicks; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            if (RequiredWorkOccurred(world, recorder)) break;
        }

        var content = world.WorldContent;
        var farmhouse = world.WorldSimulation.Buildings.Single(item => item.InstanceId == "first-town-farmhouse");
        var blacksmith = world.WorldSimulation.Buildings.Single(item => item.InstanceId == "first-town-blacksmith");
        Assert.NotNull(farmhouse.HouseholdId);
        Assert.NotNull(blacksmith.HouseholdId);
        Assert.NotEqual(farmhouse.HouseholdId, blacksmith.HouseholdId);
        Assert.All(world.Society.Inhabitants, person => Assert.Equal(SocietyWorkRole.Unassigned, person.CurrentRole));

        var farmFamilies = FamiliesForHousehold(world, recorder, farmhouse.HouseholdId!);
        var smithFamilies = FamiliesForHousehold(world, recorder, blacksmith.HouseholdId!);
        foreach (var family in new[] { "farm:Till", "farm:Plant", "farm:Harvest", "farm:collect" })
            Assert.Contains(family, farmFamilies);
        Assert.Contains("recipe:mill-grain", farmFamilies);
        Assert.Contains("recipe:wooden-axe", smithFamilies);
        Assert.Contains("recipe:wooden-pickaxe", smithFamilies);
        Assert.Contains("haul_smith_input", smithFamilies);

        // Iron ore depends on the map, so mining is expected only where an
        // outcrop can be walked to from the Blacksmith.
        var map = world.ExportState().Map;
        if (map.Resources.Any(resource => resource.Kind == "iron_ore" &&
                map.IsReachableOnFoot(blacksmith.Position, resource.Position)))
            Assert.Contains("gather_smith_ore", smithFamilies);

        // No claim means no access: a household that never holds a kind of
        // building is never offered its work.
        var farmhouseRecipes = content.Recipes.Where(item => item.WorkstationBuildingId == farmhouse.DefinitionId)
            .Select(item => "recipe:" + item.LocalId).ToArray();
        var blacksmithRecipes = content.Recipes.Where(item => item.WorkstationBuildingId == blacksmith.DefinitionId)
            .Select(item => "recipe:" + item.LocalId).ToArray();
        var tailorShop = content.Buildings.Single(item => item.Tags.Contains("tailor"));
        var tailorRecipes = content.Recipes.Where(item => item.WorkstationBuildingId == tailorShop.CanonicalId)
            .Select(item => "recipe:" + item.LocalId).ToArray();
        foreach (var household in world.Society.Households)
        {
            var families = FamiliesForHousehold(world, recorder, household.Id);
            if (!Holds(world, household.Id, "farmhouse"))
                Assert.DoesNotContain(families, family => farmhouseRecipes.Contains(family) ||
                    family.StartsWith("farm:", StringComparison.Ordinal));
            if (!Holds(world, household.Id, "blacksmith"))
                Assert.DoesNotContain(families, blacksmithRecipes.Contains);
            if (!Holds(world, household.Id, "tailor"))
                Assert.DoesNotContain(families, tailorRecipes.Contains);
        }
        // Supply actions now also serve Houses and other household workstations.
        // Check their actual destination ownership instead of treating every supply as tailoring.
        Assert.All(recorder.WorkstationSupplySites.Keys, site => Assert.Equal(
            world.Society.GetInhabitant(site.AgentId).HouseholdId,
            world.WorldSimulation.Buildings.Single(building => building.InstanceId == site.BuildingId).HouseholdId));
        Assert.All(world.Fields, field => Assert.True(Holds(world, field.HouseholdId, "farmhouse")));
        Assert.Contains(world.ExportState().Events, item => item.Kind == "field_harvest_collected");
        Assert.Contains(world.Fields, field => field.Crop == FarmFieldRules.Grain && field.Cycle > 0);
        Assert.Contains(world.WorldSimulation.ProductionJobs, job => job.State == WorldProductionJobState.Completed &&
            job.BuildingInstanceId == farmhouse.InstanceId && content.Recipes.Any(recipe =>
                recipe.CanonicalId == job.RecipeId && recipe.LocalId == "mill-grain"));

        // Each household is offered the productive building it lacks, never a
        // second House and never a building the Town shares.
        await VerifyOwnBlacksmithPlanWithMaterialsInHand(world, farmhouse.HouseholdId!, farmFamilies);
        Assert.Contains("building:farmhouse-1x1", smithFamilies);
        foreach (var families in new[] { farmFamilies, smithFamilies })
        {
            Assert.DoesNotContain("building:house-1x1", families);
            Assert.DoesNotContain(families, SharedBuildingFamilies.Contains);
        }

        // Household buildings keep their household; civic buildings keep their Town.
        var definitions = content.Buildings.ToDictionary(item => item.CanonicalId, StringComparer.Ordinal);
        Assert.All(world.WorldSimulation.Buildings.Where(item => HouseholdBuildingKinds.KindOf(definitions[item.DefinitionId]) is not null),
            building => Assert.Contains(world.Society.Households, household => household.Id == building.HouseholdId));
        Assert.All(world.WorldSimulation.Buildings, building =>
        {
            var town = Assert.Single(world.Towns, town => town.Id == building.TownId);
            Assert.Contains(building.InstanceId, town.AssignedBuildingIds);
            Assert.NotEmpty(town.ResidentIds);
            Assert.All(town.ResidentIds, id => Assert.Contains(world.Society.Inhabitants, person => person.Id == id));
            if (HouseholdBuildingKinds.KindOf(definitions[building.DefinitionId]) is null)
                Assert.Null(building.HouseholdId);
            else
                Assert.Contains(world.Society.Inhabitants, person => person.HouseholdId == building.HouseholdId && town.ResidentIds.Contains(person.Id));
        });
        foreach (var household in world.Society.Households)
        {
            var kinds = world.WorldSimulation.Buildings.Where(item => item.HouseholdId == household.Id)
                .Select(item => HouseholdBuildingKinds.KindOf(definitions[item.DefinitionId]))
                .ToArray();
            Assert.Equal(kinds.Length, kinds.Distinct(StringComparer.Ordinal).Count());
        }
    }

    private static async Task VerifyOwnBlacksmithPlanWithMaterialsInHand(PrivateWorldRuntime world,
        string householdId, HashSet<string> normalFamilies)
    {
        if (normalFamilies.Contains("building:blacksmith-1x2")) return;
        // The normal two-day trace includes actual supply trips, crops and milling.
        // A distant initial clay trip can finish without gathering stone yet.
        // Check the planner's positive stock prerequisite in a separate restored
        // snapshot; MiningToolBootstrapTests proves paid pick -> mined stone -> plan.
        var state = world.ExportState();
        var house = world.WorldSimulation.Buildings.Single(building => building.HouseholdId == householdId &&
            world.WorldContent.Buildings.Any(definition => definition.CanonicalId == building.DefinitionId &&
                definition.Tags.Contains("house", StringComparer.Ordinal)));
        var members = world.Society.Inhabitants.Where(person => person.HouseholdId == householdId)
            .Select(person => person.Id).ToHashSet(StringComparer.Ordinal);
        Assert.True(world.Society.Inventory.Lots.Where(lot => lot.ItemKind == "wood" &&
            (lot.OwnerId == householdId || members.Contains(lot.OwnerId))).Sum(lot => lot.Quantity) >= 12);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "planner-owned-stone", "stone", householdId, 4,
            storageBuildingId: house.InstanceId);
        state = state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } };
        var recorder = new ActionCoverageRecorder(chooseIdle: true);
        using var planning = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)), _ => recorder);
        for (var tick = 0; tick < 40; tick++) Assert.True((await planning.AdvanceOneTickAsync()).Advanced);
        var families = FamiliesForHousehold(planning, recorder, householdId);
        Assert.Contains("building:blacksmith-1x2", families);
        Assert.DoesNotContain("building:house-1x1", families);
        Assert.DoesNotContain(families, SharedBuildingFamilies.Contains);
        Assert.Equal(householdId, planning.Society.Inventory.GetLot("planner-owned-stone").OwnerId);
        Assert.Equal(house.InstanceId, planning.Society.Inventory.GetLot("planner-owned-stone").StorageBuildingId);
        var blacksmith = planning.WorldContent.Buildings.Single(definition => definition.LocalId == "blacksmith-1x2");
        var sites = recorder.OfferedByAgent.Where(entry => members.Contains(entry.Key)).SelectMany(entry => entry.Value.Keys)
            .Where(id => TownConstructionCandidateIds.TryParse(id, out var selection) && selection.IsBuilding &&
                selection.DefinitionId == blacksmith.CanonicalId)
            .Select(id => TownConstructionCandidateIds.TryParse(id, out var selection) ? selection.SitePosition!.Value : default).ToArray();
        Assert.NotEmpty(sites);
        var town = planning.Towns.Single(town => town.Id == house.TownId);
        Assert.All(sites, site => Assert.True(TownBorderRules.IsWithinOrAdjacent(town, site,
            blacksmith.Width, blacksmith.Height)));
        planning.Validate();
    }

    private static bool RequiredWorkOccurred(PrivateWorldRuntime world, ActionCoverageRecorder recorder)
    {
        var farmhouse = world.WorldSimulation.Buildings.Single(item => item.InstanceId == "first-town-farmhouse");
        var smith = world.WorldSimulation.Buildings.Single(item => item.InstanceId == "first-town-blacksmith");
        var farmFamilies = FamiliesForHousehold(world, recorder, farmhouse.HouseholdId!);
        var smithFamilies = FamiliesForHousehold(world, recorder, smith.HouseholdId!);
        if (!FarmWorkFamilies.All(farmFamilies.Contains) || !SmithWorkFamilies.All(smithFamilies.Contains)) return false;
        var map = world.ExportState().Map;
        if (map.Resources.Any(resource => resource.Kind == "iron_ore" && map.IsReachableOnFoot(smith.Position, resource.Position)) &&
            !smithFamilies.Contains("gather_smith_ore")) return false;
        return world.ExportState().Events.Any(item => item.Kind == "field_harvest_collected") &&
            world.Fields.Any(field => field.Crop == FarmFieldRules.Grain && field.Cycle > 0) &&
            world.WorldSimulation.ProductionJobs.Any(job => job.State == WorldProductionJobState.Completed && job.BuildingInstanceId == farmhouse.InstanceId &&
                world.WorldContent.Recipes.Any(recipe => recipe.CanonicalId == job.RecipeId && recipe.LocalId == "mill-grain"));
    }

    private static bool Holds(PrivateWorldRuntime world, string householdId, string tag) =>
        world.WorldSimulation.Buildings.Any(building => building.HouseholdId == householdId &&
            world.WorldContent.Buildings.Any(definition => definition.CanonicalId == building.DefinitionId &&
                definition.Tags.Contains(tag, StringComparer.Ordinal)));

    private static HashSet<string> FamiliesForHousehold(PrivateWorldRuntime world, ActionCoverageRecorder recorder,
        string householdId) => world.Society.Inhabitants
        .Where(person => person.HouseholdId == householdId)
        .SelectMany(person => recorder.FamiliesOfferedTo(person.Id, world.WorldContent))
        .ToHashSet(StringComparer.Ordinal);
}

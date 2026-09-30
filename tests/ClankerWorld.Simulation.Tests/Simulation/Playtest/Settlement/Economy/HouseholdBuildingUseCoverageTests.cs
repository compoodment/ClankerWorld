using ClankerWorld.Simulation.Harness;
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
    // Two world days at the current 360-tick day: every family asserted below
    // appears within this horizon on these seeds, and it keeps CI short.
    private const int CoverageTicks = 720;

    private static readonly string[] SharedBuildingFamilies =
        ["building:warehouse-2x2", "building:workshop", "building:weaving-frame", "building:shelter",
         "building:storage", "building:fire", "building:stone-hearth"];

    [Fact]
    public async Task CompatibilityWorldWithoutAFarmhouseOffersNoCrops()
    {
        var recorder = new ActionCoverageRecorder();
        using var world = new PrivateWorldRuntime("probe-world", _ => recorder, startPace: WorldStartPace.FounderSetup);
        var positions = new[] { new GridPoint(0, 0), new GridPoint(1, 2), new GridPoint(2, 2), new GridPoint(3, 2) };
        for (var index = 0; index < positions.Length; index++)
            world.PlaceFounder($"founder:{index + 1:D32}", positions[index]);
        world.StartWorld();
        world.StageStarterContent();
        for (var tick = 0; tick < CoverageTicks; tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);

        var families = recorder.FamiliesOffered(world.WorldContent);
        Assert.Contains("harvest_food", families);
        Assert.Contains("explore", families);
        // A household may build a Farmhouse during the run; one that never
        // holds one is never offered a crop.
        var cropRecipes = world.WorldContent.Recipes.Where(item => item.IsCrop).Select(item => "recipe:" + item.LocalId).ToArray();
        foreach (var household in world.Society.Households.Where(household => !Holds(world, household.Id, "farmhouse")))
            Assert.DoesNotContain(FamiliesForHousehold(world, recorder, household.Id), cropRecipes.Contains);
    }

    [Theory]
    [InlineData("probe-a")]
    [InlineData("probe-b")]
    public async Task HouseholdsWorkAtTheBuildingsTheyHoldAndPlanOnlyWhatTheyLack(string seed)
    {
        var recorder = new ActionCoverageRecorder();
        using var world = NormalPathWorld.CreateGenerated(seed, _ => recorder);
        for (var tick = 0; tick < CoverageTicks; tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);

        var content = world.WorldContent;
        var farmhouse = world.WorldSimulation.Buildings.Single(item => item.InstanceId == "first-town-farmhouse");
        var blacksmith = world.WorldSimulation.Buildings.Single(item => item.InstanceId == "first-town-blacksmith");
        Assert.NotNull(farmhouse.HouseholdId);
        Assert.NotNull(blacksmith.HouseholdId);
        Assert.NotEqual(farmhouse.HouseholdId, blacksmith.HouseholdId);
        Assert.All(world.Society.Inhabitants, person => Assert.Equal(SocietyWorkRole.Unassigned, person.CurrentRole));

        var farmFamilies = FamiliesForHousehold(world, recorder, farmhouse.HouseholdId!);
        var smithFamilies = FamiliesForHousehold(world, recorder, blacksmith.HouseholdId!);
        Assert.Contains("recipe:universal-grain-field", farmFamilies);
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
        var cropRecipes = content.Recipes.Where(item => item.IsCrop).Select(item => "recipe:" + item.LocalId).ToArray();
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
                Assert.DoesNotContain(families, family => cropRecipes.Contains(family) || farmhouseRecipes.Contains(family));
            if (!Holds(world, household.Id, "blacksmith"))
                Assert.DoesNotContain(families, blacksmithRecipes.Contains);
            if (!Holds(world, household.Id, "tailor"))
                Assert.DoesNotContain(families, family => tailorRecipes.Contains(family) || family == "supply_workstation");
        }

        // Each household is offered the productive building it lacks, never a
        // second House and never a building the Town shares.
        Assert.Contains("building:blacksmith-1x2", farmFamilies);
        Assert.Contains("building:farmhouse-1x1", smithFamilies);
        foreach (var families in new[] { farmFamilies, smithFamilies })
        {
            Assert.DoesNotContain("building:house-1x1", families);
            Assert.DoesNotContain(families, SharedBuildingFamilies.Contains);
        }

        // Whatever was built is held by its household, with no duplicates.
        var definitions = content.Buildings.ToDictionary(item => item.CanonicalId, StringComparer.Ordinal);
        Assert.All(world.WorldSimulation.Buildings.Where(item => !item.InstanceId.StartsWith("first-town-", StringComparison.Ordinal)),
            building => Assert.NotNull(building.HouseholdId));
        foreach (var household in world.Society.Households)
        {
            var kinds = world.WorldSimulation.Buildings.Where(item => item.HouseholdId == household.Id)
                .Select(item => HouseholdBuildingKinds.KindOf(definitions[item.DefinitionId]))
                .ToArray();
            Assert.Equal(kinds.Length, kinds.Distinct(StringComparer.Ordinal).Count());
        }
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

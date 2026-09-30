using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Tests;

/// <summary>
/// The offline coverage check from #437: worlds built the normal way, run by
/// the built-in rule-based chooser with no model calls, record what each
/// agent is offered. Work at a household building follows who holds it.
/// </summary>
public sealed class HouseholdBuildingUseCoverageTests
{
    // Two world days at the current 360-tick day: every gated family below
    // appears within this horizon on these seeds, and it keeps CI short.
    private const int CoverageTicks = 720;

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
        Assert.DoesNotContain(world.WorldSimulation.Buildings, building => world.WorldContent.Buildings.Any(definition =>
            definition.CanonicalId == building.DefinitionId && definition.Tags.Contains("farmhouse", StringComparer.Ordinal)));
        Assert.DoesNotContain(world.WorldContent.Recipes.Where(item => item.IsCrop),
            recipe => families.Contains("recipe:" + recipe.LocalId));
    }

    [Theory]
    [InlineData("probe-a")]
    [InlineData("probe-b")]
    public async Task HouseholdsAreOfferedWorkOnlyAtTheBuildingsTheyHold(string seed)
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
        var cropRecipes = content.Recipes.Where(item => item.IsCrop).Select(item => "recipe:" + item.LocalId).ToArray();
        var farmhouseRecipes = content.Recipes.Where(item => item.WorkstationBuildingId == farmhouse.DefinitionId)
            .Select(item => "recipe:" + item.LocalId).ToArray();
        var blacksmithRecipes = content.Recipes.Where(item => item.WorkstationBuildingId == blacksmith.DefinitionId)
            .Select(item => "recipe:" + item.LocalId).ToArray();

        Assert.Contains("recipe:universal-grain-field", farmFamilies);
        Assert.Contains("recipe:mill-grain", farmFamilies);
        Assert.Contains("recipe:wooden-axe", smithFamilies);
        Assert.Contains("recipe:wooden-pickaxe", smithFamilies);
        Assert.Contains("haul_smith_input", smithFamilies);

        // No claim means no access, in both directions.
        Assert.DoesNotContain(smithFamilies, family => cropRecipes.Contains(family) || farmhouseRecipes.Contains(family));
        Assert.DoesNotContain(farmFamilies, family => blacksmithRecipes.Contains(family));

        // Iron ore depends on the map, so mining is expected only where an
        // outcrop can be walked to from the Blacksmith.
        var map = world.ExportState().Map;
        if (map.Resources.Any(resource => resource.Kind == "iron_ore" &&
                map.IsReachableOnFoot(blacksmith.Position, resource.Position)))
            Assert.Contains("gather_smith_ore", smithFamilies);
    }

    private static HashSet<string> FamiliesForHousehold(PrivateWorldRuntime world, ActionCoverageRecorder recorder,
        string householdId) => world.Society.Inhabitants
        .Where(person => person.HouseholdId == householdId)
        .SelectMany(person => recorder.FamiliesOfferedTo(person.Id, world.WorldContent))
        .ToHashSet(StringComparer.Ordinal);
}

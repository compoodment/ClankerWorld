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
    // Two world days at the current 360-tick day: every family asserted below
    // appears within this horizon on these seeds, and it keeps CI short.
    private const int CoverageTicks = 720;

    private static readonly string[] SharedBuildingFamilies =
        ["building:warehouse-2x2", "building:workshop", "building:weaving-frame", "building:shelter",
         "building:storage", "building:fire", "building:stone-hearth"];

    [Theory]
    [InlineData("probe-a")]
    public async Task HouseholdsWorkAtTheBuildingsTheyHoldAndPlanOnlyWhatTheyLack(string seed)
    {
        var recorder = new ActionCoverageRecorder();
        using var setup = NormalPathWorld.CreateGenerated(seed, _ => recorder);
        var state = setup.ExportState();
        var farmingHousehold = setup.WorldSimulation.Buildings.Single(item => item.InstanceId == "first-town-farmhouse").HouseholdId!;
        // This checks permission to mill available grain, independently of
        // which crop the normal chooser grows to meet the food shortage.
        var inventory = InventoryFixture.AddLot(
            state.Society.Society.Inventory, "coverage-milling-grain", "grain", farmingHousehold, 1,
            storageBuildingId: "first-town-farmhouse");
        state = state with
        {
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
        };
        // Work is demand-driven. Start with a real shortage rather than
        // requiring unnecessary crop work while starter rations are plentiful.
        using var world = PrivateWorldRuntime.Restore(FarmFieldTests.FeedFarmTownFromAvailableStock(state, farmingHousehold),
            _ => recorder);
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
        Assert.Contains("farm", farmFamilies);
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
        var farmhouseRecipes = RecipesForFamily("farmhouse");
        var blacksmithRecipes = RecipesForFamily("blacksmith");
        var tailorRecipes = RecipesForFamily("tailor");
        foreach (var household in world.Society.Households)
        {
            var families = FamiliesForHousehold(world, recorder, household.Id);
            if (!Holds(world, household.Id, "farmhouse"))
                Assert.DoesNotContain(families, family => family == "farm" || farmhouseRecipes.Contains(family));
            if (!Holds(world, household.Id, "blacksmith"))
                Assert.DoesNotContain(families, blacksmithRecipes.Contains);
            if (!Holds(world, household.Id, "tailor"))
                Assert.DoesNotContain(families, tailorRecipes.Contains);
        }

        // Pottery also uses supply trips. Every offered destination must still
        // belong to the supplying adult's household, whatever recipe needs it.
        Assert.All(recorder.WorkstationSupplyOffers.Keys, offer =>
        {
            var building = Assert.Single(world.WorldSimulation.Buildings,
                item => item.InstanceId == offer.DestinationId);
            Assert.Equal(world.Society.GetInhabitant(offer.AgentId).HouseholdId, building.HouseholdId);
        });

        // Building offers need materials in hand; HouseholdBuildingPlanTests
        // checks the positive offers with that prerequisite supplied. This
        // free-running world must never plan a second House or Town buildings.
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

        string[] RecipesForFamily(string tag)
        {
            var familyIds = content.Buildings.Where(item => item.Tags.Contains(tag, StringComparer.Ordinal))
                .Select(item => item.CanonicalId).ToHashSet(StringComparer.Ordinal);
            Assert.NotEmpty(familyIds);
            return content.Recipes.Where(item => item.WorkstationBuildingId is { } id && familyIds.Contains(id))
                .Select(item => "recipe:" + item.LocalId).ToArray();
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

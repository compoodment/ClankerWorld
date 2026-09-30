using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

/// <summary>A household plans only buildings it needs for itself, once it has the materials in hand.</summary>
public sealed class HouseholdBuildingPlanTests
{
    private const string Alpha = "household:camp-alpha";
    private const string Beta = "household:camp-beta";
    [Fact]
    public async Task PlansWaitForMaterialsInHand()
    {
        var idle = new ActionCoverageRecorder(chooseIdle: true);
        using var setup = NormalPathWorld.CreateGenerated("probe-a", _ => idle);
        var state = setup.ExportState();
        Assert.DoesNotContain(state.Society.Society.Inventory.Lots, lot => lot.ItemKind == "stone" && lot.Quantity > 0);

        using (var withoutStone = PrivateWorldRuntime.Restore(state, _ => idle))
        {
            for (var tick = 0; tick < 40; tick++)
                Assert.True((await withoutStone.AdvanceOneTickAsync()).Advanced);
            var families = idle.FamiliesOffered(withoutStone.WorldContent);
            Assert.DoesNotContain(families, family => family.StartsWith("building:", StringComparison.Ordinal));
            Assert.Contains(FamiliesForHousehold(withoutStone, idle, Alpha), family => family == "gather_building_material");
        }

        var stocked = new ActionCoverageRecorder(chooseIdle: true);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "alpha-stone", "stone", Alpha, 4,
            storageBuildingId: "first-town-house-a");
        state = state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } };
        using var withStone = PrivateWorldRuntime.Restore(state, _ => stocked);
        for (var tick = 0; tick < 40; tick++)
            Assert.True((await withStone.AdvanceOneTickAsync()).Advanced);
        Assert.Contains("building:blacksmith-1x2", FamiliesForHousehold(withStone, stocked, Alpha));
        Assert.DoesNotContain("building:farmhouse-1x1", FamiliesForHousehold(withStone, stocked, Beta));
    }

    [Fact]
    public async Task OnlyTheFarmhouseHouseholdPlansASiloAndOnlyBesideItsFarmhouse()
    {
        using var setup = NormalPathWorld.CreateGenerated("probe-a", _ => new ActionCoverageRecorder(chooseIdle: true));
        var state = setup.ExportState();
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "alpha-stone", "stone", Alpha, 2,
            storageBuildingId: "first-town-house-a");
        inventory = InventoryFixture.AddLot(inventory, "beta-stone", "stone", Beta, 2, storageBuildingId: "first-town-house-b");
        state = state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } };
        var recorder = new ActionCoverageRecorder(chooseIdle: true);
        using var world = PrivateWorldRuntime.Restore(state, _ => recorder);
        for (var tick = 0; tick < 40; tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);

        var silo = world.WorldContent.Buildings.Single(item => item.LocalId == "silo-1x1");
        var farmhouse = world.WorldSimulation.Buildings.Single(item => item.InstanceId == "first-town-farmhouse");
        var siloSites = recorder.OfferedByAgent
            .Where(entry => world.Society.GetInhabitant(entry.Key).HouseholdId == Alpha)
            .SelectMany(entry => entry.Value.Keys)
            .Where(id => TownConstructionCandidateIds.TryParse(id, out var selection) &&
                selection.IsBuilding && selection.DefinitionId == silo.CanonicalId)
            .Select(id => TownConstructionCandidateIds.TryParse(id, out var selection) ? selection.SitePosition!.Value : default)
            .ToArray();
        Assert.NotEmpty(siloSites);
        // Near means within two tiles, counting diagonals; this Farmhouse has
        // free corners, so a touching site is offered too.
        int Distance(GridPoint site) => Math.Max(Math.Abs(site.X - farmhouse.Position.X), Math.Abs(site.Y - farmhouse.Position.Y));
        Assert.All(siloSites, site => Assert.InRange(Distance(site), 1, TownLayoutContext.NeighborReach));
        Assert.Contains(siloSites, site => Distance(site) == 1);
        Assert.DoesNotContain("building:silo-1x1", FamiliesForHousehold(world, recorder, Beta));
    }

    [Fact]
    public async Task HouseholdHarvestIsStoredInItsSilo()
    {
        using var generated = NormalPathWorld.CreateGenerated("probe-a", _ => new ActionCoverageRecorder(chooseIdle: true));
        var initial = generated.ExportState();
        var stone = InventoryFixture.AddLot(initial.Society.Society.Inventory, "alpha-stone", "stone", Alpha, 2,
            storageBuildingId: "first-town-house-a");
        using var setup = PrivateWorldRuntime.Restore(initial with
        {
            Society = initial.Society with { Society = initial.Society.Society with { Inventory = stone } },
        }, _ => new ActionCoverageRecorder(chooseIdle: true));
        var silo = setup.WorldContent.Buildings.Single(item => item.LocalId == "silo-1x1");
        var farmhouse = setup.WorldSimulation.Buildings.Single(item => item.InstanceId == "first-town-farmhouse");
        var placed = Enumerable.Range(-2, 5).SelectMany(dy => Enumerable.Range(-2, 5)
                .Select(dx => new GridPoint(farmhouse.Position.X + dx, farmhouse.Position.Y + dy)))
            .Any(point => setup.PlaceBuilding("alpha-silo", silo.CanonicalId, point, Alpha).Applied);
        Assert.True(placed);

        var state = setup.ExportState();
        var farmer = state.Society.Society.Inhabitants.First(person => person.HouseholdId == Alpha).Id;
        var field = state.Map.GetResource(SeededMapGenerator.FertileLandResourceId).Position;
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == farmer
                ? person with { Position = field, HungerBasisPoints = 9_000 } : person).ToArray(),
        };
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)),
            _ => new ActionCoverageRecorder(chooseIdle: true));
        var crop = world.WorldContent.Recipes.Single(item => item.LocalId == "universal-grain-field");
        var started = world.StartProduction(crop.CanonicalId, WorldBuildSiteRules.FertileLandSiteId(field), farmer);
        Assert.True(started.Applied, started.Failure);
        for (var tick = 0; tick < crop.DurationTicks; tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);

        var harvest = world.Society.Inventory.Lots.Where(lot =>
            lot.Id.StartsWith(started.JobId + ":output:", StringComparison.Ordinal)).ToArray();
        Assert.Contains(harvest, lot => lot.ItemKind == "grain");
        Assert.All(harvest, lot =>
        {
            Assert.Equal(Alpha, lot.OwnerId);
            Assert.Equal("alpha-silo", lot.StorageBuildingId);
        });
        world.Validate();
        _ = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState()));
    }

    private static HashSet<string> FamiliesForHousehold(PrivateWorldRuntime world, ActionCoverageRecorder recorder,
        string householdId) => world.Society.Inhabitants
        .Where(person => person.HouseholdId == householdId)
        .SelectMany(person => recorder.FamiliesOfferedTo(person.Id, world.WorldContent))
        .ToHashSet(StringComparer.Ordinal);
}

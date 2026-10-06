using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

/// <summary>A household plans only buildings it needs for itself, once it has the materials in hand.</summary>
public sealed class HouseholdBuildingPlanTests
{
    private const string Alpha = "household:camp-alpha";
    private const string Beta = "household:camp-beta";

    [Theory]
    [InlineData(Alpha, "blacksmith-1x2", "farmhouse-1x1", "first-town-house-a")]
    public async Task MissingProductiveBuildingIsOfferedWhenItsMaterialsAreInHand(
        string household, string missingBuilding, string heldBuilding, string house)
    {
        using var setup = NormalPathWorld.CreateGenerated("probe-a", _ => new ActionCoverageRecorder(chooseIdle: true));
        var state = setup.ExportState();
        var empty = new ActionCoverageRecorder(chooseIdle: true);
        using (var withoutMaterials = PrivateWorldRuntime.Restore(state, _ => empty))
        {
            for (var tick = 0; tick < 40; tick++)
                Assert.True((await withoutMaterials.AdvanceOneTickAsync()).Advanced);
            Assert.DoesNotContain("building:" + missingBuilding,
                FamiliesForHousehold(withoutMaterials, empty, household));
        }

        // Real House stock supplies the prerequisite. Free-running adults may
        // otherwise spend their materials on food, pottery or another project.
        var definition = setup.WorldContent.Buildings.Single(item => item.LocalId == missingBuilding);
        var inventory = state.Society.Society.Inventory;
        foreach (var cost in definition.BuildCosts)
            inventory = InventoryFixture.AddLot(inventory, "plan-material-" + cost.ResourceId,
                cost.ResourceId, household, cost.Amount, storageBuildingId: house);
        state = state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } };
        var stocked = new ActionCoverageRecorder(chooseIdle: true);
        using var withMaterials = PrivateWorldRuntime.Restore(state, _ => stocked);
        for (var tick = 0; tick < 40; tick++)
            Assert.True((await withMaterials.AdvanceOneTickAsync()).Advanced);
        var families = FamiliesForHousehold(withMaterials, stocked, household);
        Assert.Contains("building:" + missingBuilding, families);
        Assert.DoesNotContain("building:" + heldBuilding, families);
        Assert.DoesNotContain("building:house-1x1", families);
        withMaterials.Validate();
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
    public async Task HouseholdHarvestMustBeCarriedFromTheFieldToItsSilo()
    {
        var (state, actor, household, point) = await FarmFieldTests.ReadyFarmer("probe-a");
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "farmhouse-full", "grain", household, 96,
            storageBuildingId: "first-town-farmhouse");
        var siloDefinition = state.WorldContent!.Buildings.Single(item => item.LocalId == "silo-1x1");
        var house = state.WorldSimulation!.Buildings.First(building => building.HouseholdId == household &&
            state.WorldContent.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId).Tags.Contains("house")).InstanceId;
        foreach (var cost in siloDefinition.BuildCosts)
            inventory = InventoryFixture.AddLot(inventory, "silo-material-" + cost.ResourceId, cost.ResourceId, household, cost.Amount,
                storageBuildingId: house);
        using var setup = FarmFieldTests.Restore(FarmFieldTests.WithInventory(state, inventory));
        var silo = setup.WorldContent.Buildings.Single(item => item.LocalId == "silo-1x1");
        var farmhouse = setup.WorldSimulation.Buildings.Single(item => item.InstanceId == "first-town-farmhouse");
        var placed = Enumerable.Range(-2, 5).SelectMany(dy => Enumerable.Range(-2, 5)
            .Select(dx => new GridPoint(farmhouse.Position.X + dx, farmhouse.Position.Y + dy)))
            .Any(tile => setup.PlaceBuilding("harvest-silo", silo.CanonicalId, tile, household).Applied);
        Assert.True(placed);
        Assert.True(setup.StartFieldWork(actor, point, FarmWorkKind.Harvest).Accepted);
        for (var tick = 0; tick < 4; tick++) Assert.True((await setup.AdvanceOneTickAsync()).Advanced);
        var harvest = Assert.Single(setup.Society.Inventory.Lots, lot => lot.ItemKind == "grain" && lot.Id.StartsWith("field-", StringComparison.Ordinal));
        Assert.Equal(household, harvest.OwnerId);
        Assert.Equal(new InventoryGroundPosition(point.X, point.Y), harvest.GroundPosition);
        Assert.Null(harvest.StorageBuildingId);
        state = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(setup.ExportState()));
        using var hauling = PrivateWorldRuntime.Restore(state, id => id == actor ? FarmFieldTests.HaulProvider() : new ActionCoverageRecorder(chooseIdle: true));
        for (var tick = 0; tick < 160 && hauling.Society.Inventory.Lots.Where(lot => lot.StorageBuildingId == "harvest-silo" && lot.ItemKind == "grain").Sum(lot => lot.Quantity) < harvest.Quantity; tick++)
            Assert.True((await hauling.AdvanceOneTickAsync()).Advanced);
        var stored = hauling.Society.Inventory.Lots.Where(lot => lot.StorageBuildingId == "harvest-silo" && lot.ItemKind == "grain").ToArray();
        Assert.True(harvest.Quantity == stored.Sum(lot => lot.Quantity), string.Join("\n",
            hauling.ExportState().Events.TakeLast(30).Select(item => $"{item.Kind}: {item.Detail}")
                .Concat(hauling.Society.Inventory.Lots.Where(lot => lot.ItemKind == "grain")
                    .Select(lot => $"{lot.Id}: {lot.OwnerId} {lot.Quantity} at {lot.GroundPosition} storage {lot.StorageBuildingId} delivery {lot.DeliveryBuildingId}"))));
        Assert.All(stored, lot => { Assert.Equal(household, lot.OwnerId); Assert.Null(lot.GroundPosition); });
        Assert.Equal(96 + harvest.Quantity, hauling.Society.Inventory.Lots.Where(lot => lot.OwnerId == household && lot.ItemKind == "grain").Sum(lot => lot.Quantity));
        Assert.Contains(hauling.ExportState().Events, item => item.Kind == "farm_grain_picked_up");
        using var restored = FarmFieldTests.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(hauling.ExportState())));
        Assert.Equal(harvest.Quantity, restored.Society.Inventory.Lots.Where(lot => lot.StorageBuildingId == "harvest-silo" && lot.ItemKind == "grain").Sum(lot => lot.Quantity));
    }

    private static HashSet<string> FamiliesForHousehold(PrivateWorldRuntime world, ActionCoverageRecorder recorder,
        string householdId) => world.Society.Inhabitants
        .Where(person => person.HouseholdId == householdId)
        .SelectMany(person => recorder.FamiliesOfferedTo(person.Id, world.WorldContent))
        .ToHashSet(StringComparer.Ordinal);
}

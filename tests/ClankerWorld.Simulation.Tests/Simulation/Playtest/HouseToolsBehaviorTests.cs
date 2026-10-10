using System.Globalization;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class HouseToolsBehaviorTests
{
    [Fact]
    public async Task CrudePickWearsOutThroughRealStoneHarvestsAndStaysBrokenAfterReload()
    {
        using var setup = await CampAsync();
        var state = setup.ExportState();
        var actor = state.Inhabitants[0].InhabitantId;
        var source = state.Map.GetResource("settlement-stone");
        var originalStock = state.WorldSystems!.Ecology.GetResource(source.Id).Quantity;
        state = BesideSource(state, actor, source, HouseToolsContent.CrudeWoodenPickaxe);

        using (var gathering = Restore(state))
        {
            for (var harvest = 0; harvest < 2; harvest++)
            {
                var receipt = Gather(gathering, actor, source, "stone", "wear-" + harvest);
                Assert.True((await gathering.AdvanceOneTickAsync()).Advanced);
                Assert.Equal("finished", Order(gathering, receipt).Status);
                Assert.Equal(10_000 - (harvest + 1) * 4_000,
                    gathering.Society.Inventory.GetLot("crude-tool").ConditionBasisPoints);
            }
            state = gathering.ExportState();
        }

        using var resumed = Restore(state);
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(state), PrivateWorldRuntimeCodec.Encode(resumed.ExportState()));
        var last = Gather(resumed, actor, source, "stone", "last-use");
        Assert.True((await resumed.AdvanceOneTickAsync()).Advanced);
        Assert.Equal("finished", Order(resumed, last).Status);
        Assert.Equal(0, resumed.Society.Inventory.GetLot("crude-tool").ConditionBasisPoints);
        Assert.Equal(12, PersonalQuantity(resumed, actor, "stone"));
        Assert.Equal(originalStock - 3, resumed.WorldSystems.Ecology.GetResource(source.Id).Quantity);

        using var broken = Restore(resumed.ExportState());
        var refused = Gather(broken, actor, source, "stone", "broken-use");
        Assert.True((await broken.AdvanceOneTickAsync()).Advanced);
        Assert.Equal("blocked", Order(broken, refused).Status);
        Assert.Contains("usable gathering tool", Order(broken, refused).BlockedReason, StringComparison.Ordinal);
        Assert.Equal(0, Order(broken, refused).CompletedUnits);
        Assert.Equal(12, PersonalQuantity(broken, actor, "stone"));
        Assert.Equal(originalStock - 3, broken.WorldSystems.Ecology.GetResource(source.Id).Quantity);
        Assert.Equal(0, broken.Society.Inventory.GetLot("crude-tool").ConditionBasisPoints);
        broken.Validate();
    }

    [Theory]
    [InlineData(false, 6, 10_000, 8_000)]
    [InlineData(true, 4, 6_000, 10_000)]
    public async Task GatheringPrefersTheBetterCarriedPickOnlyWhenItIsAvailable(
        bool reserveBetterPick, int expectedStone, int crudeCondition, int betterCondition)
    {
        using var setup = await CampAsync();
        var state = setup.ExportState();
        var actor = state.Inhabitants[0].InhabitantId;
        var source = state.Map.GetResource("settlement-stone");
        var originalStock = state.WorldSystems!.Ecology.GetResource(source.Id).Quantity;
        state = BesideSource(state, actor, source, HouseToolsContent.CrudeWoodenPickaxe);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory,
            "better-pick", "wooden_pickaxe", actor, 1);
        if (reserveBetterPick)
            inventory = InventoryFixture.Reserve(inventory, "held-better-pick", actor, "better-pick", 1,
                "pending_trade", state.Society.Society.WorldTick + 120);
        state = WithInventory(state, inventory);

        using var world = Restore(state);
        var receipt = Gather(world, actor, source, "stone", "preferred-pick");
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal("finished", Order(world, receipt).Status);
        Assert.Equal(expectedStone, PersonalQuantity(world, actor, "stone"));
        Assert.Equal(crudeCondition, world.Society.Inventory.GetLot("crude-tool").ConditionBasisPoints);
        Assert.Equal(betterCondition, world.Society.Inventory.GetLot("better-pick").ConditionBasisPoints);
        Assert.Equal(originalStock - 1, world.WorldSystems.Ecology.GetResource(source.Id).Quantity);
        if (reserveBetterPick)
            Assert.Equal(InventoryReservationState.Reserved,
                world.Society.Inventory.GetReservation("held-better-pick").State);
        world.Validate();
    }

    [Fact]
    public async Task CrudeAxeFellsAnActualGeneratedTreeAndPreservesItsStumpSeedAndWear()
    {
        using var setup = NormalPathWorld.CreateGenerated("smith-whole-load", _ => new IdleProvider());
        Assert.True((await setup.AdvanceOneTickAsync()).Advanced);
        var state = setup.ExportState();
        var actor = state.Inhabitants[0].InhabitantId;
        var source = state.Map.Resources.Where(resource => TreeGrowthRules.IsWoodTree(resource.TreeKind) &&
                state.Resources.Single(item => item.ResourceId == resource.Id).State == ResourceState.Available &&
                state.WorldSystems!.Ecology.GetResource(resource.Id).Quantity == 1 &&
                FreeNeighbors(state, actor, resource).Any())
            .OrderBy(resource => resource.Id, StringComparer.Ordinal).First();
        state = BesideSource(state, actor, source, HouseToolsContent.CrudeWoodenAxe);
        using var world = Restore(state);
        var receipt = Gather(world, actor, source, "wood", "fell-native-tree");
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal("finished", Order(world, receipt).Status);
        Assert.Equal(4, PersonalQuantity(world, actor, "wood"));
        Assert.Equal(1, PersonalQuantity(world, actor, "tree_seed"));
        Assert.Equal(6_000, world.Society.Inventory.GetLot("crude-tool").ConditionBasisPoints);
        Assert.Equal(0, world.WorldSystems.Ecology.GetResource(source.Id).Quantity);
        Assert.Equal("stump", TreeGrowthRules.StageOf(source.TreeKind,
            world.WorldSystems.Ecology.GetResource(source.Id), SeasonKind.Spring));
        Assert.Contains(world.ExportState().Events, item => item.Kind == "tree_harvested" &&
            item.Detail == actor + ":" + source.Id + ":" + source.TreeKind + ":stump");
        Assert.Contains(world.ExportState().Events, item => item.Kind == "tree_seed_collected" &&
            item.Detail == actor + ":" + source.Id + ":1");

        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = Restore(PrivateWorldRuntimeCodec.Decode(bytes));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        Assert.Equal("stump", TreeGrowthRules.StageOf(source.TreeKind,
            restored.WorldSystems.Ecology.GetResource(source.Id), SeasonKind.Spring));
        restored.Validate();
    }

    [Theory]
    [InlineData("foreign-worker", "Only a member")]
    [InlineData("worker-away", "standing at the build site")]
    [InlineData("offsite-inputs", "on-site")]
    [InlineData("private-inputs", "on-site")]
    [InlineData("reserved-inputs", "on-site")]
    public async Task HouseToolProductionRequiresItsOwnWorkerAndUnreservedOnsiteHouseholdWood(
        string obstruction, string failure)
    {
        using var setup = await CampAsync();
        var state = setup.ExportState();
        var actor = state.Society.Society.Inhabitants.First(person => person.HouseholdId == "household:camp-alpha").Id;
        var outsider = state.Society.Society.Inhabitants.First(person => person.HouseholdId == "household:camp-beta").Id;
        var owner = state.Society.Society.GetInhabitant(actor).HouseholdId!;
        var site = state.Map.Tiles.Select(tile => tile.Position).First(point => state.Map.IsBuildable(point) &&
            !state.Map.CampObjects.Any(item => item.Position == point) &&
            !state.Map.Resources.Any(item => item.Position == point) &&
            !state.Inhabitants.Any(person => person.Position == point));
        var placed = setup.PlaceBuilding("crude-tool-house", HouseContent.House1x1().CanonicalId, site, owner);
        Assert.True(placed.Applied, placed.Failure);
        state = setup.ExportState();
        var recipe = state.WorldContent!.Recipes.Single(item =>
            item.Outputs.Any(output => output.ResourceId == HouseToolsContent.CrudeWoodenAxe));
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "house-tool-wood", "wood",
            obstruction == "private-inputs" ? outsider : owner, 3,
            storageBuildingId: obstruction == "offsite-inputs" ? null : placed.InstanceId);
        if (obstruction == "reserved-inputs")
            inventory = InventoryFixture.Reserve(inventory, "held-house-wood", owner, "house-tool-wood", 3,
                "pending_trade", state.Society.Society.WorldTick + 120);
        state = WithInventory(state, inventory) with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor && obstruction != "worker-away"
                ? person with { Position = site } : person).ToArray(),
        };
        using (var refused = Restore(state))
        {
            var before = PrivateWorldRuntimeCodec.Encode(refused.ExportState());
            var result = refused.StartProduction(recipe.CanonicalId, placed.InstanceId,
                obstruction == "foreign-worker" ? outsider : actor);
            Assert.False(result.Applied);
            Assert.Contains(failure, result.Failure, StringComparison.Ordinal);
            Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(refused.ExportState()));
        }

        // Correct only the blocked authority, location or reservation; the same
        // House and exact three wood must now create a real reserved job.
        inventory = inventory with
        {
            Lots = inventory.Lots.Select(lot => lot.Id == "house-tool-wood"
                ? lot with { OwnerId = owner, StorageBuildingId = placed.InstanceId } : lot).ToArray(),
            Reservations = inventory.Reservations.Where(item => item.Id != "held-house-wood").ToArray(),
        };
        state = WithInventory(state, inventory) with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { Position = site } : person).ToArray(),
        };
        using var allowed = Restore(state);
        var started = allowed.StartProduction(recipe.CanonicalId, placed.InstanceId, actor);
        Assert.True(started.Applied, started.Failure);
        var job = Assert.Single(allowed.WorldSimulation.ProductionJobs, item => item.JobId == started.JobId);
        Assert.Equal(recipe.CanonicalId, job.RecipeId);
        Assert.Equal(placed.InstanceId, job.BuildingInstanceId);
        Assert.Equal(24, job.CompletionTick - job.StartedTick);
        var reservation = allowed.Society.Inventory.GetReservation(Assert.Single(job.InputReservationIds));
        Assert.Equal((owner, "house-tool-wood", 3), (reservation.OwnerId, reservation.LotId, reservation.Quantity));
        Assert.Equal(3, allowed.Society.Inventory.GetLot("house-tool-wood").Quantity);
        Assert.DoesNotContain(allowed.Society.Inventory.Lots, lot => lot.ItemKind == HouseToolsContent.CrudeWoodenAxe);
        allowed.Validate();
    }

    [Fact]
    public async Task OneAdultsPersonalAndBorrowedAxesDoNotCoverAnotherHouseholdAdult()
    {
        using var setup = await CampAsync();
        var state = setup.ExportState();
        const string owner = "household:camp-alpha";
        var members = state.Society.Society.Inhabitants.Where(person => person.HouseholdId == owner).ToArray();
        Assert.Equal(2, members.Length);
        var actor = members[0].Id;
        var site = state.Map.Tiles.Select(tile => tile.Position).First(point => state.Map.IsBuildable(point) &&
            !state.Map.CampObjects.Any(item => item.Position == point) &&
            !state.Map.Resources.Any(item => item.Position == point) &&
            !state.Inhabitants.Any(person => person.Position == point));
        var placed = setup.PlaceBuilding("family-tool-house", HouseContent.House1x1().CanonicalId, site, owner);
        Assert.True(placed.Applied, placed.Failure);
        state = setup.ExportState();
        var recipe = state.WorldContent!.Recipes.Single(item =>
            item.Outputs.Any(output => output.ResourceId == HouseToolsContent.CrudeWoodenAxe));
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory,
            "family-tool-wood", "wood", owner, 3, storageBuildingId: placed.InstanceId);
        inventory = InventoryFixture.AddLot(inventory, "borrowed-family-axe", "wooden_axe", owner, 1,
            storageBuildingId: placed.InstanceId);
        inventory = InventoryFixture.Relocate(inventory, "borrow-family-axe", "borrowed-family-axe", owner, 1,
            carrierId: actor);
        inventory = InventoryFixture.AddLot(inventory, "personal-family-axe", "wooden_axe", actor, 1);
        state = WithInventory(state, inventory) with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { Position = site, LastDecisionContext = null } : person).ToArray(),
        };
        var first = new IdleProvider();
        using (var uncovered = PrivateWorldRuntime.Restore(
            PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)),
            id => id == actor ? first : new IdleProvider()))
        {
            Assert.True((await uncovered.AdvanceOneTickAsync()).Advanced);
            Assert.Contains("build:recipe:" + recipe.CanonicalId, first.Offered);
            Assert.Equal((owner, actor), (uncovered.Society.Inventory.GetLot("borrowed-family-axe").OwnerId,
                uncovered.Society.Inventory.GetLot("borrowed-family-axe").CarrierId));
        }

        inventory = InventoryFixture.AddLot(inventory, "free-family-axe", "wooden_axe", owner, 1,
            storageBuildingId: placed.InstanceId);
        var second = new IdleProvider();
        using var covered = PrivateWorldRuntime.Restore(
            PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(WithInventory(state, inventory))),
            id => id == actor ? second : new IdleProvider());
        Assert.True((await covered.AdvanceOneTickAsync()).Advanced);
        Assert.NotEmpty(second.Offered);
        Assert.DoesNotContain("build:recipe:" + recipe.CanonicalId, second.Offered);
        Assert.Empty(covered.WorldSimulation.ProductionJobs);
        covered.Validate();
    }

    private static async Task<PrivateWorldRuntime> CampAsync()
    {
        var world = new PrivateWorldRuntime("house-tools-behavior", _ => new IdleProvider(),
            startPace: WorldStartPace.FounderSetup);
        var positions = new[] { new GridPoint(0, 0), new GridPoint(1, 2), new GridPoint(2, 2), new GridPoint(3, 2) };
        for (var index = 0; index < positions.Length; index++)
            world.PlaceFounder("founder:" + (index + 1).ToString("x32", CultureInfo.InvariantCulture), positions[index]);
        world.StartWorld();
        Assert.True(world.StageStarterContent());
        for (var tick = 0; tick < 8; tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        return world;
    }

    private static PrivateWorldRuntimeState BesideSource(PrivateWorldRuntimeState state,
        string actor, MapResource source, string toolKind)
    {
        var inventory = state.Society.Society.Inventory;
        var retained = inventory.Lots.Where(lot => lot.OwnerId != actor && lot.CarrierId != actor).ToArray();
        inventory = inventory with
        {
            Lots = retained,
            Reservations = inventory.Reservations.Where(item => retained.Any(lot => lot.Id == item.LotId)).ToArray(),
        };
        inventory = InventoryFixture.AddLot(inventory, "gathering-basket", "basket", actor, 1);
        inventory = InventoryFixture.AddLot(inventory, "crude-tool", toolKind, actor, 1);
        var stand = FreeNeighbors(state, actor, source).First();
        return WithInventory(state, inventory) with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor ? person with
            {
                Position = stand,
                HungerBasisPoints = 10_000,
                TravelCooldownTicks = 0,
                Project = null,
                LastDecisionContext = null,
                Equipment = new(CarryAidLotId: "gathering-basket"),
                Exploration = null,
            } : person).ToArray(),
        };
    }

    private static IEnumerable<GridPoint> FreeNeighbors(PrivateWorldRuntimeState state, string actor, MapResource source)
    {
        var occupied = state.Inhabitants.Where(person => person.InhabitantId != actor).Select(person => person.Position)
            .Concat(state.WorldSimulation!.Buildings.SelectMany(building => WorldContentSimulationRules.Footprint(
                state.WorldContent!.Buildings.Single(item => item.CanonicalId == building.DefinitionId), building)))
            .ToHashSet();
        return state.Map.FootNeighbors(source.Position).Where(point => !occupied.Contains(point));
    }

    private static PrivateWorldRuntimeState WithInventory(PrivateWorldRuntimeState state, InventoryCheckpoint inventory) =>
        state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } };

    private static PrivateWorldRuntime Restore(PrivateWorldRuntimeState state) =>
        PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)), _ => new IdleProvider());

    private static OwnerInstructionReceipt Gather(PrivateWorldRuntime world, string actor,
        MapResource source, string kind, string key) => world.SubmitInstruction(new OwnerInstructionRequest(
            key, "owner:test", actor, OwnerInstructionKind.MustDo, $"gather {kind} from {source.Id}"));

    private static OwnerInstructionOrder Order(PrivateWorldRuntime world, OwnerInstructionReceipt receipt) =>
        world.ExportState().Instructions!.Single(item => item.InstructionId == receipt.InstructionId).Order!;

    private static int PersonalQuantity(PrivateWorldRuntime world, string actor, string kind) =>
        world.Society.Inventory.Lots.Where(lot => lot.OwnerId == actor && lot.ItemKind == kind).Sum(lot => lot.Quantity);

    private sealed class IdleProvider : IDecisionProvider
    {
        public HashSet<string> Offered { get; } = new(StringComparer.Ordinal);
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;

        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            Offered.UnionWith(request.Observation.Candidates.Select(candidate => candidate.Id));
            return new DeterministicDecisionProvider().DecideAsync(request with
            {
                Observation = request.Observation with
                {
                    Candidates = [request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle")],
                },
            }, cancellationToken);
        }
    }
}

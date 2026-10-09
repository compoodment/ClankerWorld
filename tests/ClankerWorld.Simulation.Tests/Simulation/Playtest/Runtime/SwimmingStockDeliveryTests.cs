using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class SwimmingStockDeliveryTests
{
    [Fact]
    public async Task SwimmingHouseHaulLeavesAnOversizedFamilyIntactAndFindsTheLaterFittingJug()
    {
        using var generated = NormalPathWorld.CreateGenerated("cart-set-unfinished", _ => new ActionCoverageRecorder(chooseIdle: true));
        Assert.True((await generated.AdvanceOneTickAsync()).Advanced);
        var state = generated.ExportState();
        var house = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == FoodCapacityTestFixture.House);
        var actor = state.Society.Society.Inhabitants.First(person => person.HouseholdId == house.HouseholdId).Id;
        var source = new GridPoint(103, 47);
        Assert.True(state.Map.IsBuildable(source));
        Assert.False(state.Map.IsReachableOnFoot(source, house.Position));
        Assert.True(SwimmingRules.IsReachable(state.Map, source, house.Position));
        var inventory = state.Society.Society.Inventory with
        {
            Lots = state.Society.Society.Inventory.Lots.Where(lot => lot.OwnerId != actor &&
                (lot.OwnerId != house.HouseholdId || lot.StorageBuildingId is not null)).ToArray(),
        };
        inventory = InventoryFixture.AddLot(inventory, "swim-family-tool", "stone_pickaxe", actor, 1);
        inventory = InventoryFixture.AddLot(inventory, "0-swim-big-jug", InventoryContainerRules.WaterJug,
            house.HouseholdId!, 1, groundPosition: new(source.X, source.Y));
        inventory = InventoryFixture.AddLot(inventory, "swim-big-water", InventoryContainerRules.FreshWater,
            house.HouseholdId!, 4, containerLotId: "0-swim-big-jug");
        inventory = InventoryFixture.AddLot(inventory, "1-swim-small-jug", InventoryContainerRules.WaterJug,
            house.HouseholdId!, 1, groundPosition: new(source.X, source.Y));
        inventory = InventoryFixture.AddLot(inventory, "swim-small-water", InventoryContainerRules.FreshWater,
            house.HouseholdId!, 2, containerLotId: "1-swim-small-jug");
        state = FarmFieldTests.WithInventory(state, inventory) with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor ? person with
            {
                Position = source,
                HungerBasisPoints = 10_000,
                Project = null,
                LastDecisionContext = null,
                Equipment = null,
                Survival = new SurvivalCondition(10_000)
            } : person).ToArray(),
        };
        var originalFamily = inventory.Lots.Where(lot => lot.Id == "0-swim-big-jug" ||
            lot.ContainerLotId == "0-swim-big-jug").ToArray();
        using var world = PrivateWorldRuntime.Restore(state, id => new PickupProvider(id == actor));
        for (var tick = 0; tick < 8 && world.Society.Inventory.GetLot("1-swim-small-jug").OwnerId != actor; tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var result = world.Society.Inventory;
        Assert.Equal(actor, result.GetLot("1-swim-small-jug").OwnerId);
        Assert.Equal(house.InstanceId, result.GetLot("1-swim-small-jug").DeliveryBuildingId);
        Assert.Equal((actor, "1-swim-small-jug", 2, house.InstanceId),
            (result.GetLot("swim-small-water").OwnerId, result.GetLot("swim-small-water").ContainerLotId,
                result.GetLot("swim-small-water").Quantity, result.GetLot("swim-small-water").DeliveryBuildingId));
        Assert.Equal(4, PersonalEquipmentRules.CarriedQuantity(result, actor, null));
        Assert.Equal(originalFamily.Select(lot => (lot.Id, lot.OwnerId, lot.Quantity, lot.GroundPosition, lot.ContainerLotId)),
            result.Lots.Where(lot => lot.Id == "0-swim-big-jug" || lot.ContainerLotId == "0-swim-big-jug")
                .Select(lot => (lot.Id, lot.OwnerId, lot.Quantity, lot.GroundPosition, lot.ContainerLotId)));
        Assert.DoesNotContain(result.Lots, lot => lot.ItemKind == InventoryContainerRules.FreshWater && lot.ContainerLotId is null);
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False((await world.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), id => new PickupProvider(id == actor));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        world.Validate();
        replay.Validate();
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public async Task NativeOrePickupKeepsTheLoadedReturnPossibleWithoutReducingFootLoads(bool ordered, bool swimOnly)
    {
        using var generated = NormalPathWorld.CreateGenerated("cart-set-unfinished", _ => new ActionCoverageRecorder(chooseIdle: true));
        Assert.True((await generated.AdvanceOneTickAsync()).Advanced);
        var state = generated.ExportState();
        var smith = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == "first-town-blacksmith");
        var actor = state.Society.Society.Inhabitants.First(person => person.HouseholdId == smith.HouseholdId).Id;
        var source = swimOnly ? new GridPoint(103, 47) : state.Map.FootNeighbors(smith.Position)
            .First(point => state.Map.IsBuildable(point) && state.Inhabitants.All(person => person.Position != point));
        Assert.True(state.Map.IsBuildable(source));
        Assert.Equal(!swimOnly, state.Map.IsReachableOnFoot(source, smith.Position));
        Assert.True(SwimmingRules.IsReachable(state.Map, source, smith.Position));
        var inventory = state.Society.Society.Inventory with
        {
            Lots = state.Society.Society.Inventory.Lots.Where(lot => lot.OwnerId != actor &&
                (lot.OwnerId != smith.HouseholdId || lot.ItemKind != "iron_ore")).ToArray(),
        };
        inventory = InventoryFixture.AddLot(inventory, "swim-return-tool", "stone_pickaxe", actor, 1);
        inventory = InventoryFixture.AddLot(inventory, "swim-return-ore", "iron_ore", smith.HouseholdId!, 4,
            groundPosition: new(source.X, source.Y));
        state = FarmFieldTests.WithInventory(state, inventory) with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor ? person with
            {
                Position = source,
                HungerBasisPoints = 10_000,
                Project = null,
                LastDecisionContext = null,
                Equipment = null,
                Survival = new SurvivalCondition(10_000)
            } : person).ToArray(),
        };
        using var world = PrivateWorldRuntime.Restore(state, id => new PickupProvider(id == actor));
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(state), PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        if (ordered)
            world.SubmitInstruction(new("swim-return-order", "owner:test", actor, OwnerInstructionKind.MustDo,
                "supply four iron ore to my Blacksmith"));
        for (var tick = 0; tick < 8 && !world.Society.Inventory.Lots.Any(lot => lot.OwnerId == actor &&
                 lot.ItemKind == "iron_ore" && lot.DeliveryBuildingId == smith.InstanceId); tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var cargo = Assert.Single(world.Society.Inventory.Lots, lot => lot.OwnerId == actor && lot.ItemKind == "iron_ore");
        Assert.Equal(swimOnly ? 3 : 4, cargo.Quantity);
        Assert.Equal(smith.InstanceId, cargo.DeliveryBuildingId);
        Assert.Equal(swimOnly ? 4 : 5, world.Society.Inventory.Lots.Where(lot => lot.OwnerId == actor).Sum(lot => lot.Quantity));
        Assert.Equal(4, world.Society.Inventory.Lots.Where(lot => lot.ItemKind == "iron_ore" &&
            (lot.OwnerId == actor || lot.OwnerId == smith.HouseholdId)).Sum(lot => lot.Quantity));
        if (swimOnly)
        {
            var left = world.Society.Inventory.GetLot("swim-return-ore");
            Assert.Equal(1, left.Quantity);
            Assert.Equal(smith.HouseholdId, left.OwnerId);
            Assert.Equal(new InventoryGroundPosition(source.X, source.Y), left.GroundPosition);
        }
        if (ordered) Assert.Equal(0, Assert.Single(world.ExportState().Instructions!).Order!.CompletedUnits);
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False((await world.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), id => new PickupProvider(id == actor));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        var sawSwimming = false;
        for (var tick = 0; tick < 120 && world.Society.Inventory.Lots.Any(lot => lot.OwnerId == actor &&
                 lot.ItemKind == "iron_ore" && lot.DeliveryBuildingId == smith.InstanceId); tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
            sawSwimming |= SwimmingRules.IsSwimmingWater(world.ExportState().Map, world.Inhabitants.Single(person => person.InhabitantId == actor).Position);
        }
        Assert.Equal(swimOnly, sawSwimming);
        Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.OwnerId == actor &&
            lot.ItemKind == "iron_ore" && lot.DeliveryBuildingId == smith.InstanceId);
        Assert.Equal(swimOnly ? 3 : 4, world.Society.Inventory.Lots.Where(lot => lot.ItemKind == "iron_ore" &&
            lot.OwnerId == smith.HouseholdId && lot.StorageBuildingId == smith.InstanceId).Sum(lot => lot.Quantity));
        if (ordered) Assert.Equal(swimOnly ? 3 : 4, Assert.Single(world.ExportState().Instructions!).Order!.CompletedUnits);
        world.Validate();
        replay.Validate();
    }

    private sealed class PickupProvider(bool active) : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var choice = request.Observation.Candidates.FirstOrDefault(candidate => active && candidate.Id is "haul_smith_input" or "haul_household_stock") ??
                request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle");
            return new DeterministicDecisionProvider().DecideAsync(request with
            {
                Observation = request.Observation with { Candidates = [choice] },
            }, cancellationToken);
        }
    }
}

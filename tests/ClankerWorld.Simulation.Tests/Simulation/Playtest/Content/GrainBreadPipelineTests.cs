using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class GrainBreadPipelineTests
{
    [Fact]
    public async Task FieldGrainIsPhysicallyMilledCarriedHomeBakedWithFreshWaterAndEatenAcrossReplay()
    {
        using var generated = NormalPathWorld.CreateGenerated("probe-a", _ => new Goal());
        var state = generated.ExportState();
        var farmhouse = generated.WorldSimulation.Buildings.Single(building => building.InstanceId == "first-town-farmhouse");
        var house = generated.WorldSimulation.Buildings.Single(building => building.HouseholdId == farmhouse.HouseholdId &&
            generated.WorldContent.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId).Tags.Contains("house"));
        var actor = generated.Society.Inhabitants.First(person => person.HouseholdId == farmhouse.HouseholdId).Id;
        var occupied = generated.WorldSimulation.Buildings.SelectMany(building => WorldContentSimulationRules.Footprint(
            generated.WorldContent.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId), building.Position)).ToHashSet();
        var point = generated.Towns.Single().BorderTiles.OrderBy(tile => state.Map.FootDistance(tile, farmhouse.Position))
            .First(tile => new ClankerWorld.Simulation.World.LandFertility(state.Map, state.WorldSeed).CanFarm(tile) && !occupied.Contains(tile) &&
                !generated.RoadTiles.Contains(tile) && !state.Map.Resources.Any(resource => resource.Position == tile) &&
                !state.Inhabitants.Any(person => person.Position == tile));
        state = FarmTestFields.Prepare(state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor ? person with { Position = point } : person).ToArray(),
        }, actor, point);
        var inventory = state.Society.Society.Inventory;
        Assert.DoesNotContain(inventory.Lots, lot => lot.OwnerId == farmhouse.HouseholdId && lot.ItemKind is "grain" or "flour");
        inventory = InventoryFixture.AddLot(inventory, "pipeline-jug", "water_jug", house.HouseholdId!, 1,
            storageBuildingId: house.InstanceId, containerCapacity: 8);
        inventory = InventoryFixture.AddLot(inventory, "pipeline-fuel", "wood", house.HouseholdId!, 1, storageBuildingId: house.InstanceId);
        state = WithInventory(state, inventory);
        using var growing = Load(state);
        await FarmTestFields.PlantAndGrow(growing, actor, point);
        await FarmTestFields.Harvest(growing, actor, point);
        var grainId = FarmFieldRules.FieldId(point) + ":harvest:1:crop";
        var grain = growing.Society.Inventory.GetLot(grainId);
        Assert.Equal("grain", grain.ItemKind);
        Assert.True(grain.Quantity >= 2);
        Assert.Equal(farmhouse.HouseholdId, grain.OwnerId);
        Assert.Equal(new InventoryGroundPosition(point.X, point.Y), grain.GroundPosition);
        Assert.Null(grain.StorageBuildingId);
        Assert.Null(grain.DeliveryBuildingId);
        using var milling = Load(FreshChoice(growing.ExportState(), actor), actor, "haul_farm_grain", "haul_household_stock");
        await AdvanceUntil(milling, () => milling.Society.Inventory.Lots.Any(lot => From(lot.Id, grainId) &&
            lot.OwnerId == farmhouse.HouseholdId && lot.StorageBuildingId == farmhouse.InstanceId && lot.Quantity >= 2), 96);
        Assert.Contains(milling.ExportState().Events, item => item.Kind == "farm_grain_picked_up" && item.Detail.Contains(grainId, StringComparison.Ordinal));
        Assert.All(milling.Society.Inventory.Lots.Where(lot => From(lot.Id, grainId) && lot.StorageBuildingId == farmhouse.InstanceId), lot => Assert.Null(lot.GroundPosition));
        var millRecipe = milling.WorldContent.Recipes.Single(item => item.LocalId == "mill-grain");
        using var processing = Load(FreshChoice(milling.ExportState(), actor), actor, "build:recipe:" + millRecipe.CanonicalId);
        await AdvanceUntil(processing, () => processing.WorldSimulation.ProductionJobs.Count(job =>
            job.RecipeId == millRecipe.CanonicalId && job.State == WorldProductionJobState.Completed) >= 2, 96);
        var flourIds = new List<string>();
        foreach (var job in processing.WorldSimulation.ProductionJobs.Where(job =>
            job.RecipeId == millRecipe.CanonicalId && job.State == WorldProductionJobState.Completed).Take(2))
        {
            Assert.Equal(actor, job.WorkerId);
            Assert.Equal(farmhouse.InstanceId, job.BuildingInstanceId);
            Assert.All(job.InputReservationIds, id => Assert.True(From(processing.Society.Inventory.GetReservation(id).LotId, grainId)));
            Assert.All(job.InputReservationIds, id => Assert.Equal(InventoryReservationState.Completed, processing.Society.Inventory.GetReservation(id).State));
            var flourId = job.JobId + ":output:00";
            Assert.Equal("flour", processing.Society.Inventory.GetLot(flourId).ItemKind);
            Assert.Equal(farmhouse.InstanceId, processing.Society.Inventory.GetLot(flourId).StorageBuildingId);
            flourIds.Add(flourId);
        }
        using var hauling = Load(FreshChoice(processing.ExportState(), actor), actor, "haul_farm_flour", "haul_household_stock");
        await AdvanceUntil(hauling, () => hauling.Society.Inventory.Lots.Where(lot => flourIds.Any(id => From(lot.Id, id)) &&
            lot.OwnerId == house.HouseholdId && lot.StorageBuildingId == house.InstanceId).Sum(lot => lot.Quantity) == 2, 96);
        Assert.Contains(hauling.ExportState().Events, item => item.Kind == "farm_flour_picked_up");
        using var filling = Load(FreshChoice(hauling.ExportState(), actor), actor, "water_collect_jug", "water_fill", "water_deliver");
        await AdvanceUntil(filling, () => filling.Society.Inventory.Lots.Any(lot => lot.ContainerLotId == "pipeline-jug" &&
            lot.ItemKind == "water" && lot.Quantity == 8 && lot.StorageBuildingId == house.InstanceId), 160);
        Assert.Contains(filling.ExportState().Events, item => item.Kind == "water_collected");
        var waterId = filling.Society.Inventory.Lots.Single(lot => lot.ContainerLotId == "pipeline-jug" && lot.ItemKind == "water").Id;
        using var baking = Load(FreshChoice(filling.ExportState(), actor));
        var breadRecipe = baking.WorldContent.Recipes.Single(item => item.LocalId == "bread" && item.Tags.Contains("house-cooking"));
        var baked = baking.StartProduction(breadRecipe.CanonicalId, house.InstanceId, actor);
        Assert.True(baked.Applied, baked.Failure);
        var breadJob = baking.WorldSimulation.ProductionJobs.Single(item => item.JobId == baked.JobId);
        var inputs = breadJob.InputReservationIds.Select(baking.Society.Inventory.GetReservation).ToArray();
        Assert.Equal(2, inputs.Where(input => flourIds.Any(id => From(input.LotId, id))).Sum(input => input.Quantity));
        Assert.Contains(inputs, input => input.LotId == waterId && input.Quantity == 1);
        Assert.Contains(inputs, input => input.LotId == "pipeline-fuel" && input.Quantity == 1);
        using var replay = Load(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(baking.ExportState())));
        for (var tick = 0; tick < 32 && baking.WorldSimulation.ProductionJobs.Single(item => item.JobId == baked.JobId).State != WorldProductionJobState.Completed; tick++)
        {
            Assert.True((await baking.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
        }
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(baking.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        Assert.Equal(WorldProductionJobState.Completed, baking.WorldSimulation.ProductionJobs.Single(item => item.JobId == baked.JobId).State);
        Assert.Equal(7, baking.Society.Inventory.GetLot(waterId).Quantity);
        Assert.Equal("pipeline-jug", baking.Society.Inventory.GetLot(waterId).ContainerLotId);
        Assert.Equal(house.InstanceId, baking.Society.Inventory.GetLot("pipeline-jug").StorageBuildingId);
        var breadId = baked.JobId + ":output:00";
        Assert.Equal(2, baking.Society.Inventory.GetLot(breadId).Quantity);
        Assert.Equal(house.InstanceId, baking.Society.Inventory.GetLot(breadId).StorageBuildingId);
        state = FreshChoice(baking.ExportState(), actor);
        state = state with { Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor ? person with { HungerBasisPoints = 1_000 } : person).ToArray() };
        using var eating = Load(state, actor, "collect_shared_food", "consume_food");
        await AdvanceUntil(eating, () => eating.Society.Inventory.Reservations.Any(reservation => reservation.Purpose == "direct_consumption" &&
            reservation.State == InventoryReservationState.Completed && From(reservation.LotId, breadId)), 16);
        Assert.Equal("bread", eating.Inhabitants.Single(person => person.InhabitantId == actor).Survival!.LastMealKind);
        Assert.Equal(1, eating.Society.Inventory.Lots.Where(lot => From(lot.Id, breadId)).Sum(lot => lot.Quantity));
        using var saved = Load(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(eating.ExportState())));
        saved.Validate();
    }

    private static bool From(string id, string source) => id == source || id.StartsWith(source + "#transfer:", StringComparison.Ordinal);

    private static async Task AdvanceUntil(PrivateWorldRuntime world, Func<bool> done, int ticks)
    {
        for (var tick = 0; tick < ticks && !done(); tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.True(done(), $"Tick {world.WorldTick}: " + string.Join("; ", world.ExportState().Events.TakeLast(10)));
    }

    private static PrivateWorldRuntimeState WithInventory(PrivateWorldRuntimeState state, InventoryCheckpoint inventory) =>
        state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } };

    private static PrivateWorldRuntimeState FreshChoice(PrivateWorldRuntimeState state, string actor) => state with
    {
        Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor ? person with { LastDecisionContext = null } : person).ToArray(),
        Society = state.Society with
        {
            Cognition = state.Society.Cognition with
            {
                Runtimes = state.Society.Cognition.Runtimes.Select(runtime => runtime.InhabitantId == actor ? runtime with { CurrentIntention = null } : runtime).ToArray(),
            }
        },
    };

    private static PrivateWorldRuntime Load(PrivateWorldRuntimeState state, string? actor = null, params string[] choices) =>
        PrivateWorldRuntime.Restore(state, id => new Goal(id == actor ? choices : []));

    private sealed class Goal(params string[] choices) : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var selected = request.Observation.Candidates.Where(candidate => choices.Contains(candidate.Id))
                .OrderBy(candidate => candidate.DeterministicPriority).ThenBy(candidate => candidate.Id, StringComparer.Ordinal).FirstOrDefault()
                ?? request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle");
            return new DeterministicDecisionProvider().DecideAsync(request with
            {
                Observation = request.Observation with { Candidates = [selected] },
            }, cancellationToken);
        }
    }
}

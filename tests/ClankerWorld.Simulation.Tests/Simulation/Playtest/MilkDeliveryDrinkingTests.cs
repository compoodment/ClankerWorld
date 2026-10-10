using System.Collections.Concurrent;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class MilkDeliveryDrinkingTests
{
    [Theory]
    [InlineData("milk", true, false)]
    [InlineData("bread", true, false)]
    [InlineData("milk", false, false)]
    [InlineData("milk", true, true)]
    public async Task ActualHouseDeliveriesKeepTheirFoodWhileUnassignedMilkRemainsDrinkable(
        string kind, bool assigned, bool deliver)
    {
        using var generated = NormalPathWorld.CreateGenerated("milk-delivery-drinking-audit", _ => new ActionCoverageRecorder(chooseIdle: true));
        Assert.True((await generated.AdvanceOneTickAsync()).Advanced);
        var state = generated.ExportState();
        var actor = state.Inhabitants[0].InhabitantId;
        var household = state.Society.Society.GetInhabitant(actor).HouseholdId!;
        var house = state.WorldSimulation!.Buildings.Single(building => building.HouseholdId == household &&
            generated.WorldContent.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId).Tags.Contains("house"));
        var point = state.Map.Tiles.Select(tile => tile.Position).First(position => state.Map.IsBuildable(position) &&
            state.Map.IsReachableOnFoot(house.Position, position) && state.Map.FootDistance(house.Position, position) is >= 40 and <= 42 &&
            !state.Inhabitants.Any(person => person.Position == position));
        var inventory = state.Society.Society.Inventory with { Lots = [], Reservations = [] };
        var ground = new InventoryGroundPosition(point.X, point.Y);
        var owner = assigned ? household : actor;
        if (kind == "milk")
            inventory = InventoryFixture.AddLot(inventory, "promised-jug", "water_jug", owner, 1, groundPosition: assigned ? ground : null);
        inventory = InventoryFixture.AddLot(inventory, "promised-food", kind, owner, 2,
            groundPosition: assigned && kind != "milk" ? ground : null, containerLotId: kind == "milk" ? "promised-jug" : null);
        var prepared = state with
        {
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
            JevEnabled = false,
            RoutineHelper = RoutineHelperSettings.Off,
            Inhabitants = state.Inhabitants.Select(person => person with
            {
                Equipment = null,
                Position = person.InhabitantId == actor ? point : person.Position,
                HungerBasisPoints = 9_500,
            }).ToArray(),
        };
        if (assigned)
        {
            using var pickup = Restore(prepared, actor, new ChoiceProvider("haul_household_stock"));
            for (var tick = 0; tick < 12 && !pickup.ExportState().Events.Any(item => item.Kind == "household_stock_picked_up"); tick++)
                Assert.True((await pickup.AdvanceOneTickAsync()).Advanced);
            Assert.Single(pickup.ExportState().Events, item => item.Kind == "household_stock_picked_up");
            prepared = pickup.ExportState();
            Assert.Equal(house.InstanceId, pickup.Society.Inventory.GetLot("promised-food").DeliveryBuildingId);
            Assert.Equal(actor, pickup.Society.Inventory.GetLot("promised-food").OwnerId);
        }
        prepared = prepared with
        {
            Inhabitants = prepared.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { HungerBasisPoints = 3_500 } : person).ToArray(),
        };
        var choice = deliver ? "haul_household_stock" : kind == "milk" ? "drink_milk" : "consume_food";
        var policy = new ChoiceProvider(choice);
        using var world = Restore(prepared, actor, policy);
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False((await world.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        for (var tick = 0; tick < (deliver ? 160 : 80) && !world.ExportState().Events.Any(item => item.Kind == "milk_drunk") &&
             !(deliver && world.Society.Inventory.GetLot("promised-jug").StorageBuildingId == house.InstanceId); tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);

        Assert.NotEmpty(policy.Offered);
        var canDrink = kind == "milk" && !assigned;
        if (!deliver) Assert.Equal(canDrink, policy.Offered.Any(candidates => candidates.Contains(choice)));
        Assert.Equal(canDrink ? 1 : 0, world.ExportState().Events.Count(item => item.Kind == "milk_drunk"));
        Assert.Equal(canDrink ? 1 : 2, world.Society.Inventory.GetLot("promised-food").Quantity);
        if (kind == "milk")
        {
            var jug = world.Society.Inventory.GetLot("promised-jug");
            Assert.Equal(1, jug.Quantity);
            Assert.True(jug.ConditionBasisPoints > 0);
            Assert.Equal(jug.Id, world.Society.Inventory.GetLot("promised-food").ContainerLotId);
            if (deliver)
            {
                Assert.Equal(household, jug.OwnerId);
                Assert.Equal(house.InstanceId, jug.StorageBuildingId);
                Assert.Null(jug.DeliveryBuildingId);
                Assert.Equal(house.InstanceId, world.Society.Inventory.GetLot("promised-food").StorageBuildingId);
                Assert.Single(world.ExportState().Events, item => item.Kind == "household_stock_delivered");
            }
        }
        if (canDrink) Assert.True(world.ExportState().Inhabitants.Single(person => person.InhabitantId == actor).HungerBasisPoints > 3_500);
        world.Validate();
        var saved = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var reload = Restore(PrivateWorldRuntimeCodec.Decode(saved), actor, new ChoiceProvider(choice));
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(reload.ExportState()));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.True((await reload.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(reload.ExportState()));
    }

    private static PrivateWorldRuntime Restore(PrivateWorldRuntimeState state, string actor, ChoiceProvider policy) =>
        PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)),
            id => id == actor ? policy : new ActionCoverageRecorder(chooseIdle: true));

    private sealed class ChoiceProvider(string choice) : IDecisionProvider
    {
        public ConcurrentQueue<string[]> Offered { get; } = new();
        public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
        public long ProviderEpoch => 1;

        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var observation = request.Observation;
            Offered.Enqueue(observation.Candidates.Select(candidate => candidate.Id).ToArray());
            var selected = observation.Candidates.FirstOrDefault(candidate => candidate.Id == choice) ??
                observation.Candidates.Single(candidate => candidate.Id == "safe_idle");
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, observation.InhabitantId, Kind, ProviderEpoch,
                observation.RunEpoch, observation.DecisionGeneration, observation.ObservationDigest, selected.Id, 1,
                observation.Candidates.ToDictionary(candidate => candidate.Id, candidate => candidate.Id == selected.Id ? 1d : 0d, StringComparer.Ordinal)));
        }
    }
}

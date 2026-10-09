using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed class FarmFoodReserveTests
{
    [Theory]
    [InlineData("broken", true)]
    [InlineData("healthy", false)]
    [InlineData("absent", true)]
    [InlineData("loose", false)]
    [InlineData("reserved", true)]
    public async Task PlantingDemandIgnoresFoodInBrokenPotsAndPreservesUsableFoodReserves(string pot, bool shouldPlant)
    {
        var (state, actor, household, point) = FarmFieldTests.PreparedFarmer("broken-pot-farm-audit");
        state = SettlementWeatherTestFixture.WithWeather(state, WeatherKind.Clear) with
        {
            Survival = state.Survival ?? new SettlementSurvivalState(0, []),
            Inhabitants = state.Inhabitants.Select(person => person with
            {
                HungerBasisPoints = 10_000,
                Survival = (person.Survival ?? new SurvivalCondition()) with
                { WarmthBasisPoints = 10_000, NutritionBasisPoints = 10_000, IllnessBasisPoints = 0 },
            }).ToArray(),
        };
        using var tilling = FarmFieldTests.Restore(state);
        Assert.True(tilling.StartFieldWork(actor, point, FarmWorkKind.Till).Accepted);
        for (var tick = 0; tick < 4; tick++) Assert.True((await tilling.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(FarmFieldStage.Prepared, Assert.Single(tilling.Fields).Stage);
        state = FarmFieldTests.FeedHouseholdFromAvailableStock(tilling.ExportState(), household);
        var house = state.WorldSimulation!.Buildings.Single(building => building.HouseholdId == household &&
            state.WorldContent!.Buildings.Any(definition => definition.CanonicalId == building.DefinitionId && definition.Tags.Contains("house")));
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "reserve-planting-seed", "grain_seed", actor, 1);
        if (pot != "absent")
        {
            if (pot != "loose")
                inventory = InventoryFixture.AddLot(inventory, "reserve-pot", InventoryContainerRules.StoragePot, household, 1,
                    storageBuildingId: house.InstanceId);
            inventory = InventoryFixture.AddLot(inventory, "reserve-food", "food", household, 8,
                storageBuildingId: house.InstanceId, containerLotId: pot == "loose" ? null : "reserve-pot");
            if (pot == "broken")
                inventory = inventory with
                { Lots = inventory.Lots.Select(lot => lot.Id == "reserve-pot" ? lot with { ConditionBasisPoints = 0 } : lot).ToArray() };
            if (pot == "reserved")
                inventory = InventoryFixture.Reserve(inventory, "reserve-held-food", household, "reserve-food", 8,
                    "farm-food-control", long.MaxValue);
        }
        state = FarmFieldTests.WithInventory(state, inventory) with
        {
            Inhabitants = state.Inhabitants.Select(person => person with { LastDecisionContext = null }).ToArray(),
        };
        var candidate = $"farm:Plant:{point.X}:{point.Y}:grain";
        var choice = new PlantingChoice(actor, candidate);
        var replayChoice = new PlantingChoice(actor, candidate);
        var bytes = PrivateWorldRuntimeCodec.Encode(state);
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), _ => choice);
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), _ => replayChoice);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        Assert.False((await world.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        for (var tick = 0; tick < 8; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        }
        var field = Assert.Single(world.Fields);
        Assert.Equal(shouldPlant, choice.Offered);
        Assert.Equal(shouldPlant ? FarmFieldStage.Growing : FarmFieldStage.Prepared, field.Stage);
        Assert.Equal(shouldPlant ? "grain" : null, field.Crop);
        Assert.Equal(shouldPlant ? 0 : 1, world.Society.Inventory.Lots.Where(lot => lot.Id == "reserve-planting-seed").Sum(lot => lot.Quantity));
        Assert.Equal(shouldPlant ? 1 : 0, world.ExportState().Events.Count(item => item.Kind == "field_planted"));
        if (pot != "absent")
        {
            var food = world.Society.Inventory.GetLot("reserve-food");
            Assert.Equal((household, 8, pot == "loose" ? null : "reserve-pot", house.InstanceId),
                (food.OwnerId, food.Quantity, food.ContainerLotId, food.StorageBuildingId));
            if (pot != "loose")
                Assert.Equal(pot == "broken" ? 0 : 10_000, world.Society.Inventory.GetLot("reserve-pot").ConditionBasisPoints);
            if (pot == "reserved")
                Assert.Equal((8, InventoryReservationState.Reserved), (world.Society.Inventory.GetReservation("reserve-held-food").Quantity,
                    world.Society.Inventory.GetReservation("reserve-held-food").State));
        }
        bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var reload = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), _ => new PlantingChoice(actor, candidate));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(reload.ExportState()));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.True((await reload.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(reload.ExportState()));
        world.Validate();
        reload.Validate();
    }

    private sealed class PlantingChoice(string actor, string candidateId) : IDecisionProvider
    {
        public bool Offered { get; private set; }
        public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var observation = request.Observation;
            var choice = observation.InhabitantId == actor ? observation.Candidates.FirstOrDefault(item => item.Id == candidateId) : null;
            if (choice is not null) Offered = true;
            choice ??= observation.Candidates.Single(item => item.Id == "safe_idle");
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, observation.InhabitantId, Kind,
                ProviderEpoch, observation.RunEpoch, observation.DecisionGeneration, observation.ObservationDigest,
                choice.Id, 1, observation.Candidates.ToDictionary(item => item.Id, item => item.Id == choice.Id ? 1d : 0d)));
        }
    }
}

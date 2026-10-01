using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed class CaregiverVesselTests
{
    [Theory]
    [InlineData("berries", "storage_pot")]
    [InlineData("milk", "water_jug")]
    public async Task CaregiverCollectsARealServingFromHouseholdVesselStockAndFeedingReplays(string food, string vesselKind)
    {
        using var seed = NormalPathWorld.CreateGenerated("probe-a", _ => new ActionCoverageRecorder(chooseIdle: true));
        var state = seed.ExportState();
        var house = seed.WorldSimulation.Buildings.Single(building => building.InstanceId == "first-town-house-a");
        var parents = state.Society.Society.Inhabitants.Where(person => person.HouseholdId == house.HouseholdId).Take(2).ToArray();
        var actor = parents[0].Id;
        var society = SocietyFixture.ProposeRelationship(state.Society.Society,
            new("vessel-care-parents", 1, SocietyRelationshipType.Partnership, actor, parents[1].Id, 0)).Checkpoint;
        society = SocietyFixture.AcceptRelationship(society, "vessel-care-parents", 1, parents[1].Id).Checkpoint;
        var birthFood = society.Inventory.Lots.Single(lot => lot.Id == "food:camp-alpha");
        var birth = SocietyFixture.CommitBirth(society, new("family:vessel-care", 1,
            actor, parents[1].Id, house.HouseholdId!, [actor, parents[1].Id], [actor, parents[1].Id], birthFood.Id, 4, 0, ChildName: "Ari"));
        var child = Assert.IsType<string>(birth.CreatedId);
        society = birth.Checkpoint;
        var inventory = society.Inventory;
        foreach (var priorFood in inventory.Lots.Where(lot => lot.OwnerId == house.HouseholdId && FoodItems.IsEdible(lot.ItemKind)).ToArray())
        {
            inventory = InventoryFixture.Reserve(inventory, "earlier-family-meals:" + priorFood.Id,
                priorFood.OwnerId, priorFood.Id, priorFood.Quantity, "earlier-meals", 100);
            inventory = InventoryFixture.ConsumeReservation(inventory, "earlier-family-meals:" + priorFood.Id);
        }
        inventory = InventoryFixture.AddLot(inventory, "care-vessel", vesselKind, house.HouseholdId!, 1,
            storageBuildingId: house.InstanceId, containerCapacity: 8);
        inventory = InventoryFixture.AddLot(inventory, "care-contents", food, house.HouseholdId!, 2,
            storageBuildingId: house.InstanceId, containerLotId: "care-vessel");
        var childPoint = state.Map.FootNeighbors(house.Position).First(point => state.Map.IsPassable(point) &&
            !state.Inhabitants.Any(person => person.Position == point));
        state = state with
        {
            Society = state.Society with { Society = society with { Inventory = inventory } },
            Inhabitants = state.Inhabitants.Select(person => person with
            {
                Position = person.InhabitantId == actor ? house.Position : person.Position,
                HungerBasisPoints = 9_000,
                Survival = new SurvivalCondition(WarmthBasisPoints: 10_000),
            }).Append(new PlaytestInhabitantState(child, childPoint, 1_000, 0,
                "curious", "grow", Survival: new SurvivalCondition(WarmthBasisPoints: 10_000))).ToArray(),
            Towns = state.Towns!.Select(town => town.ResidentIds.Contains(actor) ? town with
            {
                ResidentIds = town.ResidentIds.Append(child).Order(StringComparer.Ordinal).ToArray(),
            } : town).ToArray(),
            Survival = new SettlementSurvivalState(0, []),
            WorldSystems = state.WorldSystems! with
            {
                RegionalWeather = null,
                Config = state.WorldSystems.Config with
                {
                    WeatherProfiles = Enum.GetValues<SeasonKind>().Select(season => new WeatherProfile(season, 1, 0, 0, 0, 0)).ToArray(),
                },
                Climate = state.WorldSystems.Climate with { Weather = WeatherKind.Clear },
            },
        };
        using var world = PrivateWorldRuntime.Restore(state, id => new CareChoice(id == actor));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())),
            id => new CareChoice(id == actor));
        for (var tick = 0; tick < 8 && !world.ExportState().Events.Any(item => item.Kind == "child_cared_for" && item.Detail == child); tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
        }
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        Assert.Contains(world.ExportState().Events, item => item.Kind == "child_cared_for" && item.Detail == child);
        Assert.Contains(world.Society.Inventory.Reservations, reservation => reservation.State == InventoryReservationState.Completed &&
            reservation.OwnerId == actor && reservation.Purpose == "direct_consumption");
        var remaining = world.Society.Inventory.GetLot("care-contents");
        var vessel = world.Society.Inventory.GetLot("care-vessel");
        Assert.Equal(1, remaining.Quantity);
        Assert.Equal("care-vessel", remaining.ContainerLotId);
        Assert.Equal(1, vessel.Quantity);
        Assert.Equal(food == "milk" ? actor : house.HouseholdId, vessel.OwnerId);
        Assert.Equal(vessel.OwnerId, remaining.OwnerId);
        Assert.Equal(food == "milk" ? null : house.InstanceId, vessel.StorageBuildingId);
        Assert.Equal(vessel.StorageBuildingId, remaining.StorageBuildingId);
        Assert.True(world.Inhabitants.Single(person => person.InhabitantId == child).HungerBasisPoints > 2_000);
        world.Validate();
    }

    private sealed class CareChoice(bool active) : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var choice = active ? request.Observation.Candidates.FirstOrDefault(candidate => candidate.Id.StartsWith("care:", StringComparison.Ordinal)) : null;
            choice ??= request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle");
            return new DeterministicDecisionProvider().DecideAsync(request with
            {
                Observation = request.Observation with { Candidates = [choice] },
            }, cancellationToken);
        }
    }
}

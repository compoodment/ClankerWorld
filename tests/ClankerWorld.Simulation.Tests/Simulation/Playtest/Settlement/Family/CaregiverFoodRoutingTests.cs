using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed class CaregiverFoodRoutingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CaregiverFeedsInfantWhenStoredFoodCannotBeReached(bool blockStorage)
    {
        var options = new GeographyOptions("island-fuel-review-0", WorldSizePreset.Small);
        using var initial = new PrivateWorldRuntime(options.Seed, startPace: WorldStartPace.FounderSetup, geographyOptions: options);
        initial.InitializeFirstTownContent();
        initial.AcceptFirstTownLayout(new GridPoint(136, 14));
        var setupMap = initial.ExportState().Map;
        var storagePosition = initial.WorldSimulation.Buildings.Single(building => building.InstanceId == "first-town-house-a").Position;
        var localFood = setupMap.Resources.Where(resource => resource.Kind is "food" or "fruit" &&
            setupMap.IsReachableOnFoot(storagePosition, resource.Position) && setupMap.FootDistance(storagePosition, resource.Position) > 3)
            .OrderBy(resource => setupMap.FootDistance(storagePosition, resource.Position))
            .First(resource => setupMap.Tiles.Any(tile => setupMap.IsBuildable(tile.Position) && setupMap.FootDistance(tile.Position, resource.Position) <= 1 &&
                !setupMap.Resources.Any(item => item.Position == tile.Position))).Position;
        var actorPosition = setupMap.Tiles.First(tile => setupMap.IsBuildable(tile.Position) &&
            setupMap.FootDistance(tile.Position, storagePosition) > 1 && setupMap.FootDistance(tile.Position, localFood) <= 1 &&
            !setupMap.Resources.Any(resource => resource.Position == tile.Position)).Position;
        var positions = new[] { actorPosition, new GridPoint(131, 9), new GridPoint(132, 9), new GridPoint(133, 9) };
        var ids = Enumerable.Range(1, 4).Select(index => $"founder:{index:D32}").ToArray();
        for (var index = 0; index < 4; index++) initial.PlaceFounder(ids[index], positions[index]);
        initial.StartWorld();
        if (blockStorage)
        {
            var map = initial.ExportState().Map;
            var storage = initial.WorldSimulation.Buildings.Single(building => building.InstanceId == "first-town-house-a").Position;
            var blockers = map.Tiles.Where(tile => map.FootDistance(tile.Position, storage) == 1 &&
                map.IsBuildable(tile.Position) && !map.Resources.Any(resource => resource.Position == tile.Position) &&
                !initial.Inhabitants.Any(person => person.Position == tile.Position)).ToArray();
            Assert.NotEmpty(blockers);
            for (var index = 0; index < blockers.Length; index++)
                initial.AddAgent($"agent:{index + 100:D32}", blockers[index].Position);
            Assert.All(map.Tiles.Where(tile => map.FootDistance(tile.Position, storage) == 1 && map.IsPassable(tile.Position)),
                tile => Assert.Contains(initial.Inhabitants, person => person.Position == tile.Position));
        }
        var state = initial.ExportState();
        var household = state.Society.Society.GetInhabitant(ids[0]).HouseholdId!;
        var society = state.Society.Society;
        society = SocietyFixture.ProposeRelationship(society, new("care-parents", 1,
            SocietyRelationshipType.Partnership, ids[0], ids[1], society.WorldTick)).Checkpoint;
        society = SocietyFixture.AcceptRelationship(society, "care-parents", 1, ids[1]).Checkpoint;
        var birthFood = society.Inventory.Lots.First(lot => lot.OwnerId == household && lot.ItemKind == "food" && lot.Quantity >= 4);
        var birth = SocietyFixture.CommitBirth(society, new($"family:{ids[0]}:{society.WorldTick}", 1,
            ids[0], ids[1], household, [ids[0], ids[1]], [ids[0], ids[1]], birthFood.Id, 4, society.WorldTick, ChildName: "Ari"));
        Assert.Equal("first-town-house-a", birthFood.StorageBuildingId);
        var childId = Assert.IsType<string>(birth.CreatedId);
        society = birth.Checkpoint;
        var childPosition = state.Map.Tiles.First(tile => state.Map.IsBuildable(tile.Position) &&
            state.Map.FootDistance(tile.Position, actorPosition) == 1 &&
            !state.Inhabitants.Any(person => person.Position == tile.Position)).Position;
        state = state with
        {
            Inhabitants = state.Inhabitants.Append(new PlaytestInhabitantState(childId, childPosition, 1_000, 0,
                "curious", "grow", Survival: new SurvivalCondition())).ToArray(),
            Towns = state.Towns!.Select(town => town.ResidentIds.Contains(ids[0])
                ? town with { ResidentIds = town.ResidentIds.Append(childId).Order(StringComparer.Ordinal).ToArray() } : town).ToArray(),
        };
        var storedFood = society.Inventory.Lots.Where(lot => lot.OwnerId == household && lot.ItemKind == "food").Sum(lot => lot.Quantity);
        Assert.True(storedFood > 0);
        var systems = state.WorldSystems!;
        state = state with
        {
            Society = state.Society with { Society = society },
            Survival = new SettlementSurvivalState(0, []),
            Inhabitants = state.Inhabitants.Select(person => person with
            {
                HungerBasisPoints = person.InhabitantId == childId ? 1_000 : 9_000,
                Survival = new SurvivalCondition(WarmthBasisPoints: 10_000),
            }).ToArray(),
            WorldSystems = systems with
            {
                RegionalWeather = null,
                Config = systems.Config with { WeatherProfiles = Enum.GetValues<SeasonKind>().Select(season => new WeatherProfile(season, 1, 0, 0, 0, 0)).ToArray() },
                Climate = systems.Climate with { Weather = WeatherKind.Clear },
            },
        };
        var provider = new CareProvider(ids[0]);
        var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)), _ => provider);
        try
        {
            for (var tick = 0; tick < 24; tick++)
            {
                Assert.True((await world.AdvanceOneTickAsync()).Advanced);
                if (tick == 5)
                {
                    var saved = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState()));
                    world.Dispose();
                    world = PrivateWorldRuntime.Restore(saved, _ => provider);
                }
            }
            var result = world.ExportState();
            var child = result.Inhabitants.Single(person => person.InhabitantId == childId);
            Assert.True(child.HungerBasisPoints >= 3_000,
                $"child fullness={child.HungerBasisPoints}; care={result.Events.Count(item => item.Kind == "child_cared_for")}; blocked={result.Events.Count(item => item.Kind == "movement_blocked")}; parent={result.Inhabitants.Single(person => person.InhabitantId == ids[0]).Position}; care choices={provider.CareChoices}; food site={localFood}; home={storagePosition}");
            Assert.Contains(result.Events, item => item.Kind == "child_cared_for" && item.Detail == childId);
            var remainingFood = result.Society.Society.Inventory.Lots.Where(lot => lot.OwnerId == household && lot.ItemKind == "food").Sum(lot => lot.Quantity);
            if (blockStorage)
            {
                Assert.Equal(storedFood, remainingFood);
                Assert.Contains(result.Events, item => item.Kind == "food_harvested" && item.Detail.StartsWith(ids[0] + ":", StringComparison.Ordinal));
            }
            else Assert.True(remainingFood < storedFood);
            using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(result)));
            restored.Validate();
        }
        finally { world.Dispose(); }
    }

    private sealed class CareProvider(string actor) : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public int CareChoices { get; private set; }
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var selected = request.Observation.InhabitantId == actor
                ? request.Observation.Candidates.FirstOrDefault(candidate => candidate.Id.StartsWith("care:", StringComparison.Ordinal)) : null;
            if (selected is not null) CareChoices++;
            selected ??= request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle");
            return new DeterministicDecisionProvider().DecideAsync(request with
            {
                Observation = request.Observation with { Candidates = [selected] },
            }, cancellationToken);
        }
    }
}

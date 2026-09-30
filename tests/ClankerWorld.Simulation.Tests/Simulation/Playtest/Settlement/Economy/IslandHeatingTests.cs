using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed class IslandHeatingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IslandHouseholdHarvestsLocalFuelAndLightsItsHearth(bool reload)
    {
        var options = new GeographyOptions("island-fuel-review-0", WorldSizePreset.Small);
        using var initial = new PrivateWorldRuntime(options.Seed, startPace: WorldStartPace.FounderSetup, geographyOptions: options);
        initial.InitializeFirstTownContent();
        initial.AcceptFirstTownLayout(new GridPoint(136, 14));
        var positions = new[] { new GridPoint(135, 14), new GridPoint(131, 9), new GridPoint(132, 9), new GridPoint(133, 9) };
        var ids = Enumerable.Range(1, 4).Select(index => $"founder:{index:D32}").ToArray();
        for (var index = 0; index < 4; index++) initial.PlaceFounder(ids[index], positions[index]);
        initial.StartWorld();
        var state = initial.ExportState();
        var household = state.Society.Society.GetInhabitant(ids[0]).HouseholdId!;
        var society = state.Society.Society;
        foreach (var lot in society.Inventory.Lots.Where(lot => lot.OwnerId == household && lot.ItemKind == "wood").ToArray())
            society = SocietyFixture.ConsumeInventory(society, household, lot.Id, lot.Quantity, "fuel-depletion-fixture").Checkpoint;
        var systems = state.WorldSystems!;
        state = state with
        {
            Society = state.Society with { Society = society },
            Survival = new SettlementSurvivalState(0, []),
            Inhabitants = state.Inhabitants.Select(person => person with
            {
                HungerBasisPoints = 9_000,
                Survival = new SurvivalCondition(WarmthBasisPoints: 4_000),
            }).ToArray(),
            WorldSystems = systems with
            {
                RegionalWeather = null,
                Config = systems.Config with { WeatherProfiles = Enum.GetValues<SeasonKind>().Select(season => new WeatherProfile(season, 0, 0, 0, 0, 1)).ToArray() },
                Climate = systems.Climate with { Weather = WeatherKind.Snow },
            },
        };
        var local = state.Map.Resources.Single(resource => resource.Id == "wild-128-16");
        Assert.True(state.Map.IsReachableOnFoot(positions[0], local.Position));
        Assert.False(state.Map.IsReachableFromCampOnFoot(local.Position));
        var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)), _ => new HeatProvider(ids[0]));
        try
        {
            for (var tick = 0; tick < 24; tick++)
            {
                Assert.True((await world.AdvanceOneTickAsync()).Advanced);
                if (reload && tick == 5)
                {
                    var saved = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState()));
                    world.Dispose();
                    world = PrivateWorldRuntime.Restore(saved, _ => new HeatProvider(ids[0]));
                }
            }
            var result = world.ExportState();
            Assert.Contains(result.Events, item => item.Kind == "material_gathered" && item.Detail.StartsWith(ids[0] + ":", StringComparison.Ordinal));
            Assert.Contains(result.Survival!.Fires, fire => fire.BuildingId == "first-town-house-a");
            Assert.DoesNotContain(result.Events, item => item.Kind == "movement_blocked" && item.Detail.StartsWith(ids[0] + ":no_route", StringComparison.Ordinal));
            using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(result)));
            Assert.NotEmpty(restored.ExportState().Survival!.Fires);
        }
        finally { world.Dispose(); }
    }

    private sealed class HeatProvider(string actor) : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var selected = request.Observation.InhabitantId == actor
                ? request.Observation.Candidates.FirstOrDefault(candidate => candidate.Id == "tend_fire") : null;
            selected ??= request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle");
            return new DeterministicDecisionProvider().DecideAsync(request with
            {
                Observation = request.Observation with { Candidates = [selected] },
            }, cancellationToken);
        }
    }
}

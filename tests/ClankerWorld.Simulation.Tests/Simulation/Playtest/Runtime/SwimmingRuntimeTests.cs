using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class SwimmingRuntimeTests
{
    private const string Actor = "founder:00000000000000000000000000000001";


    [Theory]
    [InlineData(TerrainKind.River)]
    [InlineData(TerrainKind.Lake)]
    public async Task LightHealthyAgentCrossesWideWaterSlowlyLosesWarmthAndResumesAcrossReload(TerrainKind water)
    {
        var start = water == TerrainKind.River ? new GridPoint(42, 4) : new GridPoint(4, 2);
        var destination = water == TerrainKind.River ? new GridPoint(46, 4) : new GridPoint(8, 2);
        var firstWater = new GridPoint(start.X + 1, start.Y);
        var state = CrossingState(start);
        using var world = Restore(state);
        world.SubmitInstruction(new("cross-water", "owner:test", Actor, OwnerInstructionKind.MustDo,
            $"Move to tile ({firstWater.X}, {firstWater.Y})"));
        var crossedWater = false;
        for (var tick = 0; tick < 64 && Position(world) != firstWater; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            if (Position(world) == firstWater)
            {
                crossedWater = true;
                break;
            }
        }
        Assert.True(crossedWater, "The native movement order never entered the wide river or lake.");
        var swimming = world.ExportState();
        var physical = swimming.Inhabitants.Single(person => person.InhabitantId == Actor);
        Assert.True(physical.TravelCooldownTicks > 2, "Swimming must be much slower than two-tile wading.");
        Assert.Equal("swim", new OwnerWorldObservationStore(world).GetSnapshot().Inhabitants.Single(person => person.Id == Actor).Route.Status);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(firstWater, Position(world));
        swimming = world.ExportState();
        Assert.True(physical.Survival!.WarmthBasisPoints -
            swimming.Inhabitants.Single(person => person.InhabitantId == Actor).Survival!.WarmthBasisPoints >= 75,
            "The wait between slow swimming steps must also lose warmth.");
        var before = PrivateWorldRuntimeCodec.Encode(swimming);
        Assert.False((await world.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        using var reloaded = Restore(PrivateWorldRuntimeCodec.Decode(before));
        for (var x = firstWater.X + 1; x <= destination.X; x++)
        {
            var next = new GridPoint(x, start.Y);
            var text = $"Move to tile ({next.X}, {next.Y})";
            world.SubmitInstruction(new($"cross-{x}", "owner:test", Actor, OwnerInstructionKind.MustDo, text));
            reloaded.SubmitInstruction(new($"cross-{x}", "owner:test", Actor, OwnerInstructionKind.MustDo, text));
            for (var tick = 0; tick < 32 && Position(world) != next; tick++)
            {
                Assert.True((await world.AdvanceOneTickAsync()).Advanced);
                Assert.True((await reloaded.AdvanceOneTickAsync()).Advanced);
                Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(reloaded.ExportState()));
            }
            Assert.Equal(next, Position(world));
        }
        Assert.Equal(destination, Position(world));
        Assert.All(world.ExportState().Instructions!, order =>
        {
            Assert.Equal("finished", order.Order!.Status);
            Assert.Equal(1, order.Order.CompletedUnits);
        });
        Assert.Empty(world.ExportState().DeceasedInhabitants ?? []);
    }

    [Fact]
    public async Task NativeLandDestinationUsesSwimmingWhenItIsTheCheapestRoute()
    {
        var start = new GridPoint(84, 80);
        var destination = new GridPoint(89, 80);
        using var world = Restore(CrossingState(start, "traffic-bridge-0"));
        world.SubmitInstruction(new("cross-lake", "owner:test", Actor, OwnerInstructionKind.MustDo, "Move to tile (89, 80)"));
        var sawSwimming = false;
        for (var tick = 0; tick < 96 && Position(world) != destination; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            sawSwimming |= !world.ExportState().Map.IsPassable(Position(world));
        }
        Assert.True(sawSwimming, "The native route to the opposite bank must use this four-tile lake crossing.");
        Assert.Equal(destination, Position(world));
        var order = Assert.Single(world.ExportState().Instructions!).Order!;
        Assert.Equal("finished", order.Status);
        Assert.Equal(1, order.CompletedUnits);
    }

    [Theory]
    [InlineData(1_000, 0, 0)]
    [InlineData(10_000, 3_000, 0)]
    [InlineData(10_000, 0, 5)]
    public async Task ColdIllOrHeavilyLoadedAgentDoesNotStartSwimming(int warmth, int illness, int load)
    {
        var start = new GridPoint(4, 2);
        var state = CrossingState(start);
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == Actor
                ? person with { Survival = new(warmth, illness) } : person).ToArray(),
        };
        if (load > 0)
            state = WithLoad(state, load);
        using var world = Restore(state);
        world.SubmitInstruction(new("unsafe-swim", "owner:test", Actor, OwnerInstructionKind.MustDo, "Move to tile (5, 2)"));
        for (var tick = 0; tick < 8; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True(world.ExportState().Map.IsPassable(Position(world)));
        }
        Assert.NotEqual("finished", Assert.Single(world.ExportState().Instructions!).Order!.Status);
        Assert.Empty(world.ExportState().DeceasedInhabitants ?? []);
    }

    [Fact]
    public async Task SeaStillRequiresABoatAndCannotBeSavedAsAnUnaccompaniedSwimmer()
    {
        var map = GeographyCandidateSelector.GenerateCandidate(new GeographyOptions("swimming-native", WorldSizePreset.Small));
        var bank = map.Tiles.Select(tile => tile.Position).First(point => map.IsBuildable(point) &&
            map.Contains(new(point.X + 1, point.Y)) && map.HydrologyAt(new(point.X + 1, point.Y)) == WaterKind.Ocean);
        var sea = new GridPoint(bank.X + 1, bank.Y);
        using var world = Restore(CrossingState(bank));
        world.SubmitInstruction(new("sea-swim", "owner:test", Actor, OwnerInstructionKind.MustDo,
            $"Move to tile ({sea.X}, {sea.Y})"));
        for (var tick = 0; tick < 8; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True(map.IsPassable(Position(world)));
        }
        var state = world.ExportState();
        Assert.NotEqual("finished", Assert.Single(state.Instructions!).Order!.Status);
        Assert.Throws<InvalidDataException>(() => Restore(state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == Actor
                ? person with { Position = sea } : person).ToArray(),
        }));
    }

    [Theory]
    [InlineData(1_000, 0, 0)]
    [InlineData(10_000, 3_000, 0)]
    [InlineData(10_000, 0, 5)]
    public async Task SwimmerCanStillLeaveWaterAfterTheirConditionChangesAcrossReload(int warmth, int illness, int load)
    {
        using var setup = Restore(CrossingState(new(4, 2)));
        setup.SubmitInstruction(new("enter-water", "owner:test", Actor, OwnerInstructionKind.MustDo, "Move to tile (5, 2)"));
        for (var tick = 0; tick < 16 && Position(setup) != new GridPoint(5, 2); tick++)
            Assert.True((await setup.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(new GridPoint(5, 2), Position(setup));
        var state = setup.ExportState();
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == Actor
                ? person with { Survival = new(warmth, illness) } : person).ToArray(),
        };
        if (load > 0) state = WithLoad(state, load);
        using var world = Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)));
        world.SubmitInstruction(new("leave-water", "owner:test", Actor, OwnerInstructionKind.MustDo, "Move to tile (4, 2)"));
        for (var tick = 0; tick < 24 && !world.ExportState().Map.IsPassable(Position(world)); tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.True(world.ExportState().Map.IsPassable(Position(world)));
        Assert.Empty(world.ExportState().DeceasedInhabitants ?? []);
    }

    [Fact]
    public async Task ColdSwimmerReachesShoreRatherThanWaitingBesideAPublicShelterAcrossReload()
    {
        const string actor = "agent:00000000000000000000000000000042";
        var shore = new GridPoint(4, 2);
        var water = new GridPoint(5, 2);
        using var setup = Restore(CrossingState(new(3, 2)));
        var household = setup.AddAgent(actor, shore);
        Assert.Equal("household:" + actor, household);
        Assert.DoesNotContain(setup.WorldSimulation.Buildings, building => building.HouseholdId == household);
        setup.SubmitInstruction(new("shelter-enter-water", "owner:test", actor, OwnerInstructionKind.MustDo, "Move to tile (5, 2)"));
        for (var tick = 0; tick < 16 && Position(setup, actor) != water; tick++)
            Assert.True((await setup.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(water, Position(setup, actor));
        var shelter = setup.WorldContent.Buildings.Single(building => building.LocalId == "shelter");
        var placed = setup.PlaceBuilding("swimmer-public-shelter", shelter.CanonicalId, shore);
        Assert.True(placed.Applied, placed.Failure);
        Assert.Null(setup.WorldSimulation.Buildings.Single(building => building.InstanceId == placed.InstanceId).HouseholdId);
        var state = setup.ExportState();
        Assert.True(SwimmingRules.IsSwimmingWater(state.Map, water));
        // A real unhoused swimmer is urgently cold, with public cover one tile away.
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { Survival = new SurvivalCondition(1_000) } : person).ToArray(),
        };
        state = SettlementWeatherTestFixture.WithWeather(state, WeatherKind.Rain);
        var provider = new WarmthProvider(actor);
        using var world = Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)), provider);
        world.SubmitInstruction(new("shelter-seek-warmth", "owner:test", actor, OwnerInstructionKind.Suggestive, "Consider seeking nearby warmth."));
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False((await world.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        using var replay = Restore(PrivateWorldRuntimeCodec.Decode(before), new WarmthProvider(actor));
        for (var tick = 0; tick < 24 && !world.ExportState().Map.IsPassable(Position(world, actor)); tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        }
        Assert.True(provider.ChoseWarmth, "The real personal decision must choose the offered native warmth action.");
        Assert.Equal(shore, Position(world, actor));
        var warmth = world.Inhabitants.Single(person => person.InhabitantId == actor).Survival!.WarmthBasisPoints;
        for (var tick = 0; tick < 3; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        }
        Assert.True(world.Inhabitants.Single(person => person.InhabitantId == actor).Survival!.WarmthBasisPoints > warmth);
        Assert.Contains(world.ExportState().Events, item => item.Kind == "inhabitant_moved" && item.Detail.EndsWith(":warmth", StringComparison.Ordinal));
        var saved = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var loaded = Restore(PrivateWorldRuntimeCodec.Decode(saved));
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(loaded.ExportState()));
        Assert.Empty(world.ExportState().DeceasedInhabitants ?? []);
    }

    private static PrivateWorldRuntimeState WithLoad(PrivateWorldRuntimeState state, int units) => state with
    {
        Society = state.Society with
        {
            Society = state.Society.Society with
            {
                Inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "swim-cargo", "wood", Actor, units),
            },
        },
    };

    private static PrivateWorldRuntimeState CrossingState(GridPoint start, string seed = "swimming-native")
    {
        var options = new GeographyOptions(seed, WorldSizePreset.Small);
        using var setup = new PrivateWorldRuntime(options.Seed, _ => new IdleProvider(),
            startPace: WorldStartPace.FounderSetup, geographyOptions: options);
        setup.InitializeFirstTownContent();
        var map = setup.ExportState().Map;
        setup.AcceptFirstTownLayout(map.GetResource("berry-patch").Position);
        map = setup.ExportState().Map;
        Assert.True(map.IsBuildable(start));
        Assert.DoesNotContain(map.Resources, resource => resource.Position == start);
        Assert.DoesNotContain(map.CampObjects, item => item.Position == start);
        var definitions = setup.WorldContent.Buildings.ToDictionary(item => item.CanonicalId);
        var occupied = map.Resources.Select(item => item.Position)
            .Concat(map.CampObjects.Select(item => item.Position)).Concat(setup.RoadTiles)
            .Concat(setup.WorldSimulation.Buildings.SelectMany(building =>
                WorldContentSimulationRules.Footprint(definitions[building.DefinitionId], building.Position))).ToHashSet();
        var otherStarts = map.Tiles.Select(tile => tile.Position)
            .Where(point => map.IsBuildable(point) && point != start && !occupied.Contains(point))
            .OrderBy(point => map.FootDistance(point, map.GetResource("berry-patch").Position))
            .ThenBy(point => point.Y).ThenBy(point => point.X).Take(3).ToArray();
        GridPoint[] starts = [start, .. otherStarts];
        for (var index = 0; index < starts.Length; index++)
            setup.PlaceFounder($"founder:0000000000000000000000000000000{index + 1}", starts[index]);
        setup.StartWorld();
        var state = setup.ExportState();
        return SettlementWeatherTestFixture.WithWeather(state with
        {
            Survival = new SettlementSurvivalState(0, []),
            Inhabitants = state.Inhabitants.Select(person => person with
            {
                HungerBasisPoints = 10_000,
                Survival = new SurvivalCondition(WarmthBasisPoints: 10_000),
            }).ToArray(),
        }, WeatherKind.Clear);
    }

    private static PrivateWorldRuntime Restore(PrivateWorldRuntimeState state, IDecisionProvider? provider = null) =>
        PrivateWorldRuntime.Restore(state, _ => provider ?? new IdleProvider());
    private static GridPoint Position(PrivateWorldRuntime world, string actor = Actor) => world.Inhabitants.Single(person => person.InhabitantId == actor).Position;

    private sealed class WarmthProvider(string actor) : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        internal bool ChoseWarmth { get; private set; }

        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var choice = request.Observation.Candidates.Any(candidate => candidate.Id == "seek_warmth") ? "seek_warmth" : "safe_idle";
            if (request.Observation.InhabitantId == actor && choice == "seek_warmth") ChoseWarmth = true;
            return new DeterministicDecisionProvider().DecideAsync(request with
            {
                Observation = request.Observation with { Candidates = request.Observation.Candidates.Where(candidate => candidate.Id == choice).ToArray() },
            }, cancellationToken);
        }
    }

    private sealed class IdleProvider : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default) =>
            new DeterministicDecisionProvider().DecideAsync(request with
            {
                Observation = request.Observation with { Candidates = request.Observation.Candidates.Where(item => item.Id == "safe_idle").ToArray() },
            }, cancellationToken);
    }
}

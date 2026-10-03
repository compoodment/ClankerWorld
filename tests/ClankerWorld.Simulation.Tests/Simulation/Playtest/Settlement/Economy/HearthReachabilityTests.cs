using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed class HearthReachabilityTests(HearthReachabilityTests.Fixture fixture)
    : IClassFixture<HearthReachabilityTests.Fixture>
{
    private const string Actor = "founder:00000000000000000000000000000001";
    private const string Fuel = "hearth-control-fuel";
    private const string House = "first-town-house-a";

    [Fact]
    public async Task ABlockedHearthLeavesReachableTreeCoverAvailableAcrossReload()
    {
        var choices = new Choices(true);
        using var world = Restore(fixture.CreateState(blocked: true), choices);
        for (var tick = 0; tick < 6; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.DoesNotContain("tend_fire", choices.Offered);
        Assert.Contains("seek_warmth", choices.Offered);
        using var replay = Restore(RoundTrip(world.ExportState()), new Choices(true));
        for (var tick = 6; tick < 24; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
        }

        var actor = world.Inhabitants.Single(person => person.InhabitantId == Actor);
        Assert.Equal(new GridPoint(128, 16), actor.Position);
        Assert.Equal(0, actor.MoveWaitTicks);
        Assert.Empty(world.ExportState().Survival!.Fires);
        Assert.Equal(1, world.Society.Inventory.GetLot(Fuel).Quantity);
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "movement_blocked" &&
            item.Detail.StartsWith(Actor + ":", StringComparison.Ordinal));
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        world.Validate();
    }

    [Fact]
    public async Task AReachableHouseIsFuelledOnlyAfterTheAgentArrives()
    {
        var choices = new Choices(true);
        using var world = Restore(fixture.CreateState(blocked: false), choices);
        await UntilFuelled(world, House, interactionRange: 0);
        Assert.Contains("tend_fire", choices.Offered);
        Assert.Equal(fixture.HousePosition, world.Inhabitants.Single(person => person.InhabitantId == Actor).Position);
        using var restored = Restore(RoundTrip(world.ExportState()), new Choices(true));
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        restored.Validate();
    }

    [Fact]
    public async Task ABlockedHouseDoesNotHideASecondReachableHearth()
    {
        using var world = Restore(fixture.CreateState(blocked: true), new Choices(true));
        var state = world.ExportState();
        var start = state.Inhabitants.Single(person => person.InhabitantId == Actor).Position;
        var fire = world.WorldContent.Buildings.Single(definition => definition.LocalId == "fire");
        var site = state.Map.Tiles.Select(tile => tile.Position).Where(point =>
                state.Map.IsBuildable(point) && state.Map.FootDistance(start, point) is >= 2 and <= 4 &&
                !state.Map.Resources.Any(resource => resource.Position == point) &&
                !state.Map.CampObjects.Any(item => item.Position == point) &&
                !state.Inhabitants.Any(person => person.Position == point) &&
                !world.WorldSimulation.Buildings.Any(building => building.Position == point) &&
                state.Map.IsReachableOnFoot(start, point))
            .OrderBy(point => state.Map.FootDistance(start, point)).ThenBy(point => point.Y).ThenBy(point => point.X).First();
        // Existing cooking fires remain supported. Public placement prepares
        // this legacy-building fixture and pays its cost from household stock.
        var placed = world.PlaceBuilding("z-reachable-fire", fire.CanonicalId, site);
        Assert.True(placed.Applied, placed.Failure);
        Assert.Equal(1, world.Society.Inventory.GetLot(Fuel).Quantity);
        await UntilFuelled(world, "z-reachable-fire", interactionRange: 1);
        Assert.DoesNotContain(world.ExportState().Survival!.Fires, item => item.BuildingId == House);
        world.Validate();
    }

    [Fact]
    public async Task ATendingIntentionRechecksTheRouteWhenOccupantsMoveIntoTheWay()
    {
        using var walking = Restore(fixture.CreateState(blocked: false), new Choices(true));
        var step = await walking.AdvanceOneTickAsync();
        Assert.Contains(step.Decisions, decision => decision.InhabitantId == Actor &&
            decision.Admission.Intention?.CandidateId == "tend_fire");
        Assert.Empty(walking.ExportState().Survival!.Fires);
        // The saved intention stays intact while the position fixture models the new crowd.
        var crowded = fixture.WithCrowd(walking.ExportState(), blocked: true);
        var choices = new Choices(true);
        using var world = Restore(RoundTrip(crowded), choices);
        for (var tick = 0; tick < 4; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);

        Assert.DoesNotContain("tend_fire", choices.Offered);
        Assert.Contains("seek_warmth", choices.Offered);
        Assert.Equal(0, world.Inhabitants.Single(person => person.InhabitantId == Actor).MoveWaitTicks);
        Assert.Empty(world.ExportState().Survival!.Fires);
        Assert.Equal(1, world.Society.Inventory.GetLot(Fuel).Quantity);
        world.Validate();
    }

    private static async Task UntilFuelled(PrivateWorldRuntime world, string buildingId, int interactionRange)
    {
        var destination = world.WorldSimulation.Buildings.Single(building => building.InstanceId == buildingId).Position;
        for (var tick = 0; tick < 24 && world.ExportState().Survival!.Fires.Count == 0; tick++)
        {
            Assert.Equal(1, world.Society.Inventory.GetLot(Fuel).Quantity);
            var before = world.Inhabitants.Single(person => person.InhabitantId == Actor).Position;
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            if (world.ExportState().Survival!.Fires.Count > 0)
                Assert.InRange(world.ExportState().Map.FootDistance(before, destination), 0, interactionRange);
        }
        Assert.Equal(buildingId, Assert.Single(world.ExportState().Survival!.Fires).BuildingId);
        Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.Id == Fuel);
        Assert.Single(world.ExportState().Events, item => item.Kind == "fire_fuelled" && item.Detail == buildingId);
        world.Validate();
    }

    private static PrivateWorldRuntimeState RoundTrip(PrivateWorldRuntimeState state) =>
        PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state));

    private static PrivateWorldRuntime Restore(PrivateWorldRuntimeState state, Choices choices) =>
        PrivateWorldRuntime.Restore(state, id => id == Actor ? choices : new Choices(false));

    private sealed class Choices(bool active) : IDecisionProvider
    {
        public HashSet<string> Offered { get; } = new(StringComparer.Ordinal);
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            Offered.UnionWith(request.Observation.Candidates.Select(candidate => candidate.Id));
            if (active) return new DeterministicDecisionProvider().DecideAsync(request, cancellationToken);
            var idle = request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle");
            return new DeterministicDecisionProvider().DecideAsync(request with
            { Observation = request.Observation with { Candidates = [idle] } }, cancellationToken);
        }
    }

    public sealed class Fixture : IAsyncLifetime
    {
        private byte[] baseline = [];
        private Dictionary<string, GridPoint> originalPositions = [];
        public GridPoint HousePosition { get; private set; }
        public Task DisposeAsync() => Task.CompletedTask;
        public PrivateWorldRuntimeState CreateState(bool blocked) => WithCrowd(PrivateWorldRuntimeCodec.Decode(baseline), blocked);
        public PrivateWorldRuntimeState WithCrowd(PrivateWorldRuntimeState state, bool blocked)
        {
            var ring = Enumerable.Range(-1, 3).SelectMany(dy => Enumerable.Range(-1, 3)
                    .Select(dx => new GridPoint(HousePosition.X + dx, HousePosition.Y + dy)))
                .Where(point => point != HousePosition).ToArray();
            var index = 0;
            return state with
            {
                Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == Actor ? person : person with
                { Position = blocked ? ring[index++] : originalPositions[person.InhabitantId] }).ToArray(),
            };
        }

        public async Task InitializeAsync()
        {
            var options = new GeographyOptions("island-fuel-review-0", WorldSizePreset.Small);
            using var initial = new PrivateWorldRuntime(options.Seed, _ => new Choices(false),
                startPace: WorldStartPace.FounderSetup, geographyOptions: options);
            initial.InitializeFirstTownContent();
            initial.AcceptFirstTownLayout(new(136, 14));
            GridPoint[] starts = [new(135, 14), new(131, 9), new(132, 9), new(133, 9)];
            for (var index = 0; index < 4; index++) initial.PlaceFounder($"founder:{index + 1:D32}", starts[index]);
            initial.StartWorld();
            Assert.True((await initial.AdvanceOneTickAsync()).Advanced);
            for (var index = 1; index <= 5; index++)
            {
                var current = initial.ExportState();
                var point = current.Map.Tiles.Select(tile => tile.Position).First(position => current.Map.IsBuildable(position) &&
                    !current.Towns!.Any(town => town.BorderTiles.Contains(position)) &&
                    !current.Map.Resources.Any(resource => resource.Position == position) &&
                    !current.Map.CampObjects.Any(item => item.Position == position) &&
                    !current.Inhabitants.Any(person => person.Position == position));
                initial.AddAgent($"agent:{index:D32}", point);
            }
            var state = initial.ExportState();
            HousePosition = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == House).Position;
            originalPositions = state.Inhabitants.ToDictionary(person => person.InhabitantId, person => person.Position, StringComparer.Ordinal);
            var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, Fuel, "wood", Actor, 1);
            var systems = state.WorldSystems!;
            state = state with
            {
                Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
                Survival = new(0, []),
                WorldSystems = systems with
                {
                    RegionalWeather = null,
                    Config = systems.Config with
                    { WeatherProfiles = Enum.GetValues<SeasonKind>().Select(season => new WeatherProfile(season, 0, 0, 0, 1, 0)).ToArray() },
                    Climate = systems.Climate with { Weather = WeatherKind.Storm },
                },
                Inhabitants = state.Inhabitants.Select(person => person with
                {
                    Position = person.InhabitantId == Actor ? new(134, 8) : person.Position,
                    HungerBasisPoints = 9_000,
                    TravelCooldownTicks = 0,
                    LastDecisionContext = null,
                    Survival = new(person.InhabitantId == Actor ? 3_400 : 10_000),
                }).ToArray(),
            };
            baseline = PrivateWorldRuntimeCodec.Encode(state);
            _ = RoundTrip(WithCrowd(state, blocked: true));
        }
    }
}

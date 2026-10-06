using System.Collections.Concurrent;
using System.Globalization;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.World;
using static ClankerWorld.Simulation.Tests.ShelterOrderTestFixture;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class PrivateWorldShelterOrderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AColdAdultOrChildFinishesOnlyAfterEnteringItsOccupiedHouseAcrossReplay(bool child)
    {
        var state = WithStorm(Prepared());
        var actor = Actor(state);
        var home = House(state);
        state = AwayFromHouse(state, actor, home);
        state = WithCondition(state, actor, warmth: 3_000);
        if (child) state = AsChild(state, actor);
        var occupant = state.Inhabitants.First(person => person.InhabitantId != actor &&
            state.Society.Society.GetInhabitant(person.InhabitantId).HouseholdId == Alpha).InhabitantId;
        state = At(state, occupant, home.Position);
        using var world = Restore(state);
        if (child)
        {
            Assert.Equal(SocietyAgeBand.Child, world.Society.GetInhabitant(actor).AgeBand);
            Assert.All(world.ExportState().Towns!, town => Assert.DoesNotContain(actor, town.Governance!.Members));
        }
        var receipt = Submit(world, actor, "occupied-house", AtText("shelter in my House", home.Position));
        await Tick(world);
        Assert.Equal(("seek_shelter", "shelters", 0),
            (Order(world, receipt).Action, Order(world, receipt).ProgressUnit, Order(world, receipt).CompletedUnits));
        Assert.NotEqual(home.Position, Person(world, actor).Position);
        AssertBuildingBinding(Order(world, receipt), home);
        Assert.Null(Order(world, receipt).ShelterCompletion);
        Assert.Null(Order(world, receipt).LastEffectId);
        using var replay = Reload(world);
        await FinishTogether(world, replay, receipt);
        Assert.Equal(home.Position, Person(world, actor).Position);
        Assert.Equal(home.Position, Person(world, occupant).Position);
        Assert.InRange(Person(world, actor).Survival!.WarmthBasisPoints, 1, 3_499);
        Assert.Equal(("finished", 1), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
        var proof = Assert.IsType<OwnerShelterCompletion>(Order(world, receipt).ShelterCompletion);
        Assert.Equal((world.WorldTick, home.Position), (proof.WorldTick, proof.Position));
        Assert.Null(proof.FuelReservationId);
        Assert.NotEmpty(Order(world, receipt).LastEffectId!);
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "age_action_rejected");
        await TickTogether(world, replay);
        Assert.Equal(proof, Order(world, receipt).ShelterCompletion);
        Assert.Equal(1, Order(world, receipt).CompletedUnits);
    }

    [Theory]
    [InlineData("arrive")]
    [InlineData("revoke")]
    [InlineData("storm_ends")]
    public async Task InvitedStormCoverRequiresCurrentPermissionUntilArrival(string change)
    {
        var state = WithStorm(Prepared(), change == "storm_ends" ? 2 : null);
        var host = Actor(state);
        var guest = Actor(state, Beta);
        var home = House(state);
        state = AwayFromHouse(state, guest, home);
        using var world = Restore(state);
        Assert.True(world.SetHouseGuestInvitation(host, home.InstanceId, guest, true).Applied);
        var receipt = Submit(world, guest, "guest-cover", AtText("seek shelter", home.Position));
        await Tick(world);
        AssertBuildingBinding(Order(world, receipt), home);
        Assert.Equal(0, Order(world, receipt).CompletedUnits);
        if (change == "revoke") Assert.True(world.SetHouseGuestInvitation(host, home.InstanceId, guest, false).Applied);
        using var replay = Reload(world);
        if (change == "arrive")
        {
            await FinishTogether(world, replay, receipt);
            Assert.Equal(home.Position, Person(world, guest).Position);
            Assert.Equal(home.Position, Person(world, host).Position);
            var completed = Order(world, receipt);
            Assert.True(world.SetHouseGuestInvitation(host, home.InstanceId, guest, false).Applied);
            using var historical = Reload(world);
            await Tick(historical);
            Assert.Equal(("finished", 1, completed.ShelterCompletion, completed.LastEffectId),
                (Order(historical, receipt).Status, Order(historical, receipt).CompletedUnits,
                    Order(historical, receipt).ShelterCompletion, Order(historical, receipt).LastEffectId));
        }
        else
        {
            for (var tick = 0; tick < 3; tick++) await TickTogether(world, replay);
            Assert.Equal(("blocked", 0), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
            Assert.NotEqual(home.Position, Person(world, guest).Position);
            AssertBuildingBinding(Order(world, receipt), home);
            Assert.Null(Order(world, receipt).ShelterCompletion);
            Assert.NotEmpty(Order(world, receipt).BlockedReason!);
            if (change == "storm_ends")
                Assert.All(world.WorldSystems.RegionalWeather!.Episodes, episode => Assert.NotEqual(WeatherKind.Storm, episode.Weather));
        }
    }

    [Fact]
    public async Task AnUninvitedForeignHouseDoesNotBecomeShelterByRequestingItsCoordinates()
    {
        var state = WithStorm(Prepared());
        var actor = Actor(state, Beta);
        var home = House(state);
        state = At(state, actor, Approach(state, home.Position, 1));
        using var world = Restore(state);
        var receipt = Submit(world, actor, "no-invitation", AtText("seek shelter", home.Position));
        for (var tick = 0; tick < 3; tick++) await Tick(world);
        Assert.Equal(("blocked", 0), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
        Assert.NotEqual(home.Position, Person(world, actor).Position);
        Assert.Null(Order(world, receipt).ShelterCompletion);
        Assert.NotEmpty(Order(world, receipt).BlockedReason!);
        using var replay = Reload(world);
    }

    [Fact]
    public async Task AnExplicitDistantCoverTileIsObservedBeforeBindingAndOnlyArrivalCompletesIt()
    {
        var state = WithStorm(Prepared());
        var actor = Actor(state);
        var cover = ForestAwayFromTown(state);
        var origin = Approach(state, cover, 9);
        state = At(state, actor, origin);
        Assert.DoesNotContain(state.Knowledge!.Facts, fact => fact.OwnerId == actor && fact.Position == cover);
        using var world = Restore(state);
        var receipt = Submit(world, actor, "distant-cover", AtText("take cover", cover));
        await Tick(world);
        Assert.Equal(0, Order(world, receipt).CompletedUnits);
        Assert.Null(Order(world, receipt).ShelterBinding);
        Assert.Null(Order(world, receipt).ShelterCompletion);
        Assert.NotEqual(origin, Person(world, actor).Position);
        using var replay = Reload(world);
        await FinishTogether(world, replay, receipt);
        Assert.Equal(cover, Person(world, actor).Position);
        Assert.Equal(VegetationCover.Forest, state.Map.VegetationAt(cover));
        var binding = Assert.IsType<OwnerShelterBinding>(Order(world, receipt).ShelterBinding);
        Assert.Equal(("natural", cover), (binding.Kind, binding.Position));
        Assert.Null(binding.BuildingInstanceId);
        Assert.Equal(cover, Order(world, receipt).ShelterCompletion!.Position);
        Assert.Equal(1, Order(world, receipt).CompletedUnits);
    }

    [Fact]
    public async Task StandingBesideAHouseDoesNotCompleteAnOrderForThatUncoveredTile()
    {
        var state = WithClearWeather(Prepared());
        var actor = Actor(state);
        var home = House(state);
        var outside = Approach(state, home.Position, 1);
        using var world = Restore(At(state, actor, outside));
        var receipt = Submit(world, actor, "adjacent-not-inside", AtText("seek shelter", outside));
        await Tick(world);
        Assert.Equal(("blocked", 0), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
        Assert.Null(Order(world, receipt).ShelterCompletion);
    }

    [Fact]
    public async Task NamedHouseCoordinatesCanTargetARealExpandedInteriorTileRatherThanItsAnchor()
    {
        var state = ExpandedHouseState();
        var actor = Actor(state);
        var home = House(state);
        var interior = new GridPoint(home.Position.X, home.Position.Y + 1);
        Assert.NotEqual(home.Position, interior);
        Assert.Contains(interior, WorldContentSimulationRules.Footprint(
            state.WorldContent!.Buildings.Single(definition => definition.CanonicalId == home.DefinitionId), home));
        state = At(state, actor, Approach(state, interior));
        using var world = Restore(state);
        var receipt = Submit(world, actor, "expanded-interior", AtText("shelter in my House", interior));
        await Tick(world);
        Assert.Equal(0, Order(world, receipt).CompletedUnits);
        AssertBuildingBinding(Order(world, receipt), home);
        Assert.Equal(interior, Order(world, receipt).ShelterBinding!.Position);
        using var replay = Reload(world);
        await FinishTogether(world, replay, receipt);
        Assert.Equal(interior, Person(world, actor).Position);
        Assert.Equal(interior, Order(world, receipt).ShelterCompletion!.Position);
    }

    private static PrivateWorldRuntimeState ExpandedHouseState()
    {
        var state = WithClearWeather(Prepared());
        var actor = Actor(state);
        var home = House(state);
        var definition = state.WorldContent!.Buildings.Single(item => item.CanonicalId == home.DefinitionId);
        var occupied = state.WorldSimulation!.Buildings.Where(item => item.InstanceId != home.InstanceId)
            .SelectMany(item => WorldContentSimulationRules.Footprint(state.WorldContent.Buildings.Single(value => value.CanonicalId == item.DefinitionId), item))
            .Concat(state.Map.Resources.Select(item => item.Position)).Concat(state.Map.CampObjects.Select(item => item.Position))
            .Concat(state.RoadTiles!).Concat(state.Fields!.Select(item => item.Position))
            .Concat(state.Inhabitants.Where(person => person.InhabitantId != actor).Select(person => person.Position)).ToHashSet();
        foreach (var site in state.Map.Tiles.Select(tile => tile.Position)
            .OrderBy(point => state.Map.FootDistance(home.Position, point)).ThenBy(point => point.Y).ThenBy(point => point.X))
        {
            var envelope = Enumerable.Range(-1, 4).SelectMany(y => Enumerable.Range(-1, 3)
                .Select(x => new GridPoint(site.X + x, site.Y + y)));
            if (envelope.Any(point => !state.Map.IsBuildable(point) || occupied.Contains(point))) continue;
            var entrance = new GridPoint(site.X - 1, site.Y);
            if (!WorldContentSimulationRules.IsEntrance(definition, site, entrance) || !state.Map.CanFootStep(site, entrance)) continue;
            var expanded = home with { Position = site, Entrance = entrance, Footprint = new(1, 2, 1) };
            var candidate = At(state, actor, site) with
            {
                WorldSimulation = state.WorldSimulation with
                {
                    Buildings = state.WorldSimulation.Buildings.Select(item => item.InstanceId == home.InstanceId ? expanded : item).ToArray(),
                },
                Towns = state.Towns!.Select(town => town.Id == home.TownId ? town with
                {
                    BorderTiles = TownBorderRules.ExpandForBuilding(state.Map, town, site, 1, 2),
                } : town).ToArray(),
            };
            return PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(candidate));
        }
        throw new InvalidOperationException("The shelter fixture needs a legal expanded House interior.");
    }

    private static PrivateWorldRuntimeState AwayFromHouse(PrivateWorldRuntimeState state, string actor, PlacedBuilding home) =>
        At(state, actor, Approach(state, home.Position));

    private static PrivateWorldRuntimeState WithCondition(PrivateWorldRuntimeState state, string actor, int warmth = 10_000, int fullness = 10_000) =>
        state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { HungerBasisPoints = fullness, Survival = new(warmth), LastDecisionContext = null } : person).ToArray(),
        };

    private static PrivateWorldRuntimeState AsChild(PrivateWorldRuntimeState state, string actor)
    {
        var society = state.Society.Society;
        var birth = society.LifeTickAt(society.WorldTick) - 4 * society.Config.TicksPerLifecycleAge;
        return state with
        {
            Society = state.Society with
            {
                Society = society with
                {
                    Inhabitants = society.Inhabitants.Select(person => person.Id == actor ? person with
                    {
                        BirthTick = birth,
                        BirthLifeTick = society.LifeClock is null ? null : birth,
                        AgeBand = SocietyAgeBand.Child,
                        LastLifecycleYearChecked = 4,
                        CurrentRole = SocietyWorkRole.Unassigned,
                    } : person).ToArray(),
                },
            },
            Towns = state.Towns!.Select(town => town with
            {
                Governance = TownGovernanceRules.Advance(town.Governance!, town.Id, state.WorldSeed,
                    town.ResidentIds.Where(id => id != actor && society.GetInhabitant(id) is
                    { Status: SocietyInhabitantStatus.Active, AgeBand: SocietyAgeBand.Adult or SocietyAgeBand.Elder }),
                    society.WorldTick, state.WorldSystems!.Config.TicksPerDay),
            }).ToArray(),
        };
    }

    private static GridPoint ForestAwayFromTown(PrivateWorldRuntimeState state) => state.Map.Tiles
        .Where(tile => state.Map.VegetationAt(tile.Position) == VegetationCover.Forest && state.Map.IsPassable(tile.Position) &&
            state.WorldSimulation!.Buildings.All(building => state.Map.FootDistance(building.Position, tile.Position) > 14) &&
            state.Inhabitants.All(person => person.Position != tile.Position))
        .OrderBy(tile => tile.Position.Y).ThenBy(tile => tile.Position.X).First().Position;

    private static void AssertBuildingBinding(OwnerInstructionOrder order, PlacedBuilding home)
    {
        var binding = Assert.IsType<OwnerShelterBinding>(order.ShelterBinding);
        Assert.Equal(("building", home.InstanceId, home.DefinitionId, home.HouseholdId, (GridPoint?)home.Position, (long?)home.PlacedTick),
            (binding.Kind, binding.BuildingInstanceId, binding.DefinitionId, binding.OwnerId, binding.BuildingPosition, binding.BuildingPlacedTick));
    }

    private static PrivateWorldRuntime Restore(PrivateWorldRuntimeState state) => PrivateWorldRuntime.Restore(state, _ => new ShelterChoices());
    private static PrivateWorldRuntime Reload(PrivateWorldRuntime world)
    {
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        var restored = Restore(PrivateWorldRuntimeCodec.Decode(bytes));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        return restored;
    }
    private static OwnerInstructionReceipt Submit(PrivateWorldRuntime world, string actor, string key, string text, bool queue = false) =>
        world.SubmitInstruction(new(key, "owner:test", actor, OwnerInstructionKind.MustDo, text, Queue: queue));
    private static OwnerInstructionOrder Order(PrivateWorldRuntime world, OwnerInstructionReceipt receipt) =>
        world.ExportState().Instructions!.Single(item => item.InstructionId == receipt.InstructionId).Order!;
    private static PlaytestInhabitantState Person(PrivateWorldRuntime world, string actor) => world.Inhabitants.Single(item => item.InhabitantId == actor);
    private static string AtText(string command, GridPoint position) => string.Create(CultureInfo.InvariantCulture,
        $"{command} at ({position.X}, {position.Y})");
    private static async Task Tick(PrivateWorldRuntime world) => Assert.True((await world.AdvanceOneTickAsync()).Advanced);
    private static async Task TickTogether(PrivateWorldRuntime world, PrivateWorldRuntime replay)
    {
        await Tick(world);
        await Tick(replay);
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
    }
    private static async Task FinishTogether(PrivateWorldRuntime world, PrivateWorldRuntime replay, OwnerInstructionReceipt receipt)
    {
        for (var tick = 0; tick < 40 && Order(world, receipt).Status != "finished"; tick++) await TickTogether(world, replay);
        Assert.Equal("finished", Order(world, receipt).Status);
    }

    private sealed class ShelterChoices(DecisionProviderKind kind = DecisionProviderKind.Deterministic, bool hold = false) : IDecisionProvider
    {
        public DecisionProviderKind Kind => kind;
        public long ProviderEpoch => 0;
        public ConcurrentQueue<InhabitantObservation> Requests { get; } = new();
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Returned { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            request.Validate();
            var observation = request.Observation;
            Requests.Enqueue(observation);
            if (hold && !Started.Task.IsCompleted && observation.OperativeOrderInstructionId is not null)
            {
                Started.TrySetResult(true);
                await Release.Task;
                Returned.TrySetResult(true);
            }
            var selected = observation.OperativeOrderInstructionId is null ? "safe_idle" :
                observation.Candidates.Any(candidate => candidate.Id == "seek_shelter") ? "seek_shelter" :
                observation.Candidates.Any(candidate => candidate.Id == "inspect_shelter_site") ? "inspect_shelter_site" : "safe_idle";
            return new(request.RequestId, observation.InhabitantId, Kind, request.ProviderEpoch, observation.RunEpoch,
                observation.DecisionGeneration, observation.ObservationDigest, selected, 1, new Dictionary<string, double> { [selected] = 1 });
        }
    }
}

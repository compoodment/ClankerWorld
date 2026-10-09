using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;
using static ClankerWorld.Simulation.Tests.ShelterOrderTestFixture;

namespace ClankerWorld.Simulation.Tests;

public sealed class TownHallStormRefugeTests
{
    private static readonly Lazy<Task<byte[]>> Baseline = new(CreateBaseline);

    private static async Task<PrivateWorldRuntimeState> PreparedHall() => PrivateWorldRuntimeCodec.Decode(await Baseline.Value);

    private static async Task<byte[]> CreateBaseline()
    {
        using var scenario = await TownProjectScenario.ApprovedAsync(initialTownStock: true);
        scenario.Policy.Supply = true;
        await scenario.UntilAsync(() => scenario.Project.Stage == "completed", 240);
        scenario.World.Pause();
        var state = scenario.World.ExportState();
        state = state with
        {
            Survival = new(state.Society.Society.WorldTick, []),
            Inhabitants = state.Inhabitants.Select(person => person with
            {
                HungerBasisPoints = 10_000,
                Survival = new(),
                Equipment = null,
                Project = null,
                LastDecisionContext = null,
            }).ToArray(),
        };
        // Empty the real Houses without destroying stock, so native removal can model loss of a home.
        var inventory = state.Society.Society.Inventory;
        foreach (var lot in inventory.Lots.Where(lot => lot.StorageBuildingId is not null && lot.ContainerLotId is null).ToArray())
        {
            var building = state.WorldSimulation!.Buildings.Single(item => item.InstanceId == lot.StorageBuildingId);
            if (building.HouseholdId is null) continue;
            inventory = InventoryFixture.Relocate(inventory, "refuge-fixture:" + lot.Id, lot.Id, lot.OwnerId, lot.Quantity,
                groundPosition: new(building.Position.X, building.Position.Y));
        }
        state = WithInventory(state, inventory);
        using var ready = Restore(state);
        ready.Resume();
        return PrivateWorldRuntimeCodec.Encode(ready.ExportState());
    }
    [Theory]
    [InlineData("refuge", true)]
    [InlineData("has_home", false)]
    [InlineData("rain", false)]
    [InlineData("no_hall", false)]
    [InlineData("outsider", false)]
    public async Task OnlyAHomelessOwnTownResidentGetsHallProtection(string scenario, bool protectedByHall)
    {
        var state = await PreparedHall();
        var actor = Actor(state);
        var home = House(state);
        var hall = Hall(state);
        using (var setup = Restore(state))
        {
            if (scenario != "has_home")
            {
                var removed = setup.RemoveBuilding(home.InstanceId, home.TownId, home.HouseholdId);
                Assert.True(removed.Applied, removed.Failure);
            }
            if (scenario == "no_hall") Assert.True(setup.RemoveBuilding(hall.InstanceId, hall.TownId, hall.HouseholdId).Applied);
            state = setup.ExportState();
        }
        if (scenario == "outsider")
        {
            using var setup = Restore(state);
            actor = "agent:00000000000000000000000000000005";
            var outside = state.Map.Tiles.First(tile => state.Map.IsBuildable(tile.Position) &&
                !state.Map.Resources.Any(site => site.Position == tile.Position) &&
                state.Inhabitants.All(person => person.Position != tile.Position) &&
                state.Towns!.All(town => !town.BorderTiles.Contains(tile.Position)) &&
                state.Map.IsReachableOnFoot(tile.Position, hall.Position)).Position;
            setup.AddAgent(actor, outside);
            state = setup.ExportState();
            Assert.All(state.Towns!, town => Assert.DoesNotContain(actor, town.ResidentIds));
        }
        state = SettlementWeatherTestFixture.WithWeather(state, scenario == "rain" ? WeatherKind.Rain : WeatherKind.Storm);
        var bare = state.Map.Tiles.First(tile => state.Map.IsPassable(tile.Position) &&
            state.Map.VegetationAt(tile.Position) != VegetationCover.Forest &&
            !state.Map.Resources.Any(site => site.Position == tile.Position) &&
            state.WorldSimulation!.Buildings.All(building => state.Map.FootDistance(building.Position, tile.Position) > 4)).Position;
        var bareWarmth = await WarmthAfterTick(state, actor, bare);
        // The far corner proves coverage of the hall footprint, rather than a radius around its anchor.
        var corner = new GridPoint(hall.Position.X + 2, hall.Position.Y + 3);
        var warmth = await WarmthAfterTick(state, actor, corner);
        Assert.Equal(protectedByHall ? 45 : 0, warmth - bareWarmth);
        using var world = Restore(At(state, actor, hall.Position));
        var receipt = world.SubmitInstruction(new("hall-shelter", "owner:test", actor, OwnerInstructionKind.MustDo,
            $"seek shelter at ({hall.Position.X}, {hall.Position.Y})"));
        for (var tick = 0; tick < 3; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var order = world.ExportState().Instructions!.Single(item => item.InstructionId == receipt.InstructionId).Order!;
        Assert.Equal(protectedByHall ? "finished" : "blocked", order.Status);
        Assert.Equal(protectedByHall ? 1 : 0, order.CompletedUnits);
        if (protectedByHall)
        {
            var fire = world.SubmitInstruction(new("hall-fire", "owner:test", actor, OwnerInstructionKind.MustDo,
                $"tend fire at ({hall.Position.X}, {hall.Position.Y})"));
            for (var tick = 0; tick < 3; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            var fireOrder = world.ExportState().Instructions!.Single(item => item.InstructionId == fire.InstructionId).Order!;
            Assert.Equal("blocked", fireOrder.Status);
            Assert.Equal(0, fireOrder.CompletedUnits);
            Assert.Empty(world.ExportState().Survival!.Fires);
        }
        var saved = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var reloaded = Restore(PrivateWorldRuntimeCodec.Decode(saved));
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(reloaded.ExportState()));
    }

    [Fact]
    public async Task RoutineWarmthLeavesTreesForTheHallAndOrdersRecheckStormPermission()
    {
        var state = WithStorm(await PreparedHall());
        var actor = Actor(state);
        var home = House(state);
        var hall = Hall(state);
        using (var setup = Restore(state))
        {
            var removed = setup.RemoveBuilding(home.InstanceId, home.TownId, home.HouseholdId);
            Assert.True(removed.Applied, removed.Failure);
            state = setup.ExportState();
        }
        var cover = state.Map.Resources.Where(site => site.TreeKind is "broadleaf" or "conifer" &&
            state.WorldSystems!.Ecology.GetResource(site.Id).Quantity > 0 && state.Map.IsPassable(site.Position) &&
            state.Map.IsReachableOnFoot(site.Position, hall.Position) &&
            state.Map.FootDistance(site.Position, hall.Position) > 4 && state.Map.FootDistance(site.Position, hall.Position) < 16)
            .OrderBy(site => state.Map.FootDistance(site.Position, hall.Position)).First().Position;
        state = At(state, actor, cover) with
        {
            Inhabitants = At(state, actor, cover).Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { Survival = new(3_400), TravelCooldownTicks = 0 } : person).ToArray(),
        };
        var occupant = state.Inhabitants.First(person => person.InhabitantId != actor).InhabitantId;
        state = At(state, occupant, hall.Position);
        using var world = Restore(state, actor);
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False((await world.AdvanceOneTickAsync(commitPermitted: () => false)).Advanced);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var moving = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var replay = Restore(PrivateWorldRuntimeCodec.Decode(moving), actor);
        for (var tick = 0; tick < 32; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        }
        Assert.Contains(world.Inhabitants.Single(person => person.InhabitantId == actor).Position,
            WorldContentSimulationRules.Footprint(TownHallContent.Hall3x4(), hall));
        Assert.Empty(world.ExportState().Survival!.Fires);
        Assert.DoesNotContain(world.ExportState().Events.Skip(state.Events.Count), item => item.Kind == "fire_fuelled");

        state = At(state, actor, cover);
        var orderPoint = WorldContentSimulationRules.Footprint(TownHallContent.Hall3x4(), hall)
            .Where(point => state.Inhabitants.All(person => person.Position != point))
            .OrderByDescending(point => state.Map.FootDistance(cover, point)).First();
        using var ordered = Restore(state);
        var receipt = ordered.SubmitInstruction(new("hall-travel", "owner:test", actor, OwnerInstructionKind.MustDo,
            $"seek shelter at ({orderPoint.X}, {orderPoint.Y})"));
        for (var tick = 0; tick < 4; tick++) Assert.True((await ordered.AdvanceOneTickAsync()).Advanced);
        var underway = ordered.ExportState();
        var order = underway.Instructions!.Single(item => item.InstructionId == receipt.InstructionId).Order!;
        Assert.Equal(hall.InstanceId, order.ShelterBinding?.BuildingInstanceId);
        Assert.Equal(0, order.CompletedUnits);
        using var ended = Restore(SettlementWeatherTestFixture.WithWeather(underway, WeatherKind.Clear));
        for (var tick = 0; tick < 3; tick++) Assert.True((await ended.AdvanceOneTickAsync()).Advanced);
        Assert.Equal("blocked", ended.ExportState().Instructions!.Single(item => item.InstructionId == receipt.InstructionId).Order!.Status);
        var saved = PrivateWorldRuntimeCodec.Encode(ended.ExportState());
        using var restored = Restore(PrivateWorldRuntimeCodec.Decode(saved));
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }

    private static PlacedBuilding Hall(PrivateWorldRuntimeState state) => state.WorldSimulation!.Buildings.Single(building =>
        building.DefinitionId == TownHallContent.Hall3x4().CanonicalId);

    private static async Task<int> WarmthAfterTick(PrivateWorldRuntimeState state, string actor, GridPoint position)
    {
        state = At(state, actor, position);
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor ? person with { Survival = new(5_000) } : person).ToArray(),
        };
        using var world = Restore(state);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        return world.Inhabitants.Single(person => person.InhabitantId == actor).Survival!.WarmthBasisPoints;
    }

    private static PrivateWorldRuntime Restore(PrivateWorldRuntimeState state, string? seeker = null) =>
        PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)), id => new Choices(id == seeker));

    private sealed class Choices(bool seekWarmth) : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default) =>
            new DeterministicDecisionProvider().DecideAsync(request with
            {
                Observation = request.Observation with
                {
                    Candidates = [request.Observation.Candidates.FirstOrDefault(candidate =>
                        request.Observation.OperativeOrderInstructionId is not null && candidate.Id is "seek_shelter" or "inspect_shelter_site" ||
                        seekWarmth && candidate.Id == "seek_warmth") ??
                        request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle")],
                },
            }, cancellationToken);
    }
}

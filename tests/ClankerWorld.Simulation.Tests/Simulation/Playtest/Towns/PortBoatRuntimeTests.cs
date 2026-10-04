using System.Collections.Concurrent;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class PortBoatRuntimeTests
{
    private static readonly Lazy<Task<byte[]>> PaidBoat = new(BuildPaidBoatAsync);
    private static readonly string[] ReservedKinds = ["wood", "stone", "rope", "iron"];

    [Fact]
    public async Task CouncilProjectsBuildPortsAndOnePhysicalBoatUsingUnreservedTownStock()
    {
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(await PaidBoat.Value));
        var town = Assert.Single(world.Towns);
        var boat = Assert.Single(world.Boats);
        Assert.Equal(town.Id, boat.TownId);
        Assert.Equal(3, town.Projects.Count);
        Assert.All(town.Projects, project =>
        {
            Assert.Equal("completed", project.Stage);
            var proposal = town.Governance!.Proposals.Single(proposal => proposal.Id == project.ProposalId);
            Assert.Equal("passed", proposal.Status);
            Assert.True(proposal.Votes.Count(vote => vote.Yes) >= proposal.RequiredYes);
            Assert.All(project.Deliveries, delivery => Assert.Equal(InventoryReservationState.Completed,
                world.Society.Inventory.GetReservation(delivery.ReservationId!).State));
        });
        Assert.Equal(boat.Id, town.Projects.Single(project => project.Plan.BoatPortId is not null).CompletedBoatId);
        Assert.All(ReservedKinds, kind =>
        {
            var reservation = world.Society.Inventory.GetReservation("unrelated-" + kind);
            Assert.Equal(InventoryReservationState.Reserved, reservation.State);
            Assert.Equal(reservation.Quantity, world.Society.Inventory.GetLot(reservation.LotId).Quantity);
        });
        Assert.False(world.PlaceBuilding("unpaid-port", PortContent.Definitions[0].CanonicalId, new(1, 1)).Applied);
        world.Validate();
    }

    [Fact]
    public async Task PersonalTripMovesOneBoatPassengerAndCargoAcrossMidVoyageReload()
    {
        var policy = new BoatPolicy { Trips = true };
        using var scenario = new BoatScenario(PrivateWorldRuntimeCodec.Decode(await PaidBoat.Value), policy);
        var traveler = BoatPolicy.Author;
        var before = scenario.World.Society.Inventory.Lots.Where(lot => PersonalEquipmentRules.IsCarried(lot, traveler))
            .Select(lot => (lot.Id, lot.OwnerId, lot.ItemKind, lot.Quantity)).ToArray();
        var membership = scenario.World.Towns.Select(town => (town.Id, Members: town.ResidentIds.ToArray())).ToArray();
        await scenario.UntilAsync(() => scenario.World.Boats[0].Journey is not null, 120);
        var boat = scenario.World.Boats[0];
        Assert.Equal(traveler, boat.Journey!.PassengerId);
        Assert.Equal(boat.Position, scenario.World.Inhabitants.Single(person => person.InhabitantId == traveler).Position);
        Assert.Single(scenario.World.BoatRequests, request => request.Status == "underway");
        Assert.Contains(boat.Journey.ReservedDock, PortNavigationRules.Geometry(scenario.World.ExportState().Map,
            scenario.World.WorldContent.Buildings.Single(definition => definition.CanonicalId == scenario.World.WorldSimulation.Buildings
                .Single(port => port.InstanceId == boat.Journey.DestinationPortId).DefinitionId),
            scenario.World.WorldSimulation.Buildings.Single(port => port.InstanceId == boat.Journey.DestinationPortId).Position).DockingTiles);
        policy.Trips = false;
        var saved = PrivateWorldRuntimeCodec.Encode(scenario.World.ExportState());
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved), policy.CreateProvider);
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        for (var step = 0; scenario.World.Boats[0].Journey is not null && step < 160; step++)
        {
            await scenario.World.AdvanceOneTickAsync();
            await replay.AdvanceOneTickAsync();
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(scenario.World.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        }
        Assert.Null(scenario.World.Boats[0].Journey);
        Assert.Equal("arrived", Assert.Single(scenario.World.BoatRequests).Status);
        foreach (var (id, members) in membership) Assert.Equal(members, scenario.World.Towns.Single(town => town.Id == id).ResidentIds);
        // Meals may be consumed aboard; other carried ownership and quantities stay intact.
        foreach (var expected in before.Where(lot => lot.ItemKind != "food"))
        {
            var actual = scenario.World.Society.Inventory.GetLot(expected.Id);
            Assert.Equal((expected.OwnerId, expected.ItemKind, expected.Quantity), (actual.OwnerId, actual.ItemKind, actual.Quantity));
            Assert.True(PersonalEquipmentRules.IsCarried(actual, traveler));
        }
        scenario.World.Validate();
    }

    [Theory]
    [InlineData(PortFacing.North)]
    [InlineData(PortFacing.East)]
    [InlineData(PortFacing.South)]
    [InlineData(PortFacing.West)]
    public void EveryRotationRequiresLandApproachAndSixClearWaterDocks(PortFacing facing)
    {
        using var world = new PrivateWorldRuntime("probe-a", startPace: WorldStartPace.FounderSetup,
            geographyOptions: new ClankerWorld.Simulation.World.GeographyOptions("probe-a", ClankerWorld.Simulation.World.WorldSizePreset.Small));
        var map = world.ExportState().Map;
        var definition = PortContent.Definitions.Single(definition => PortNavigationRules.Facing(definition) == facing);
        var site = map.Tiles.Select(tile => tile.Position).First(site => PortNavigationRules.Fits(map, definition, site, new HashSet<GridPoint>(), out _));
        var geometry = PortNavigationRules.Geometry(map, definition, site);
        Assert.Equal(2, geometry.LandTiles.Count);
        Assert.Equal(6, geometry.WaterTiles.Count);
        Assert.Equal(6, geometry.DockingTiles.Distinct().Count());
        foreach (var dock in geometry.DockingTiles)
            Assert.False(PortNavigationRules.Fits(map, definition, site, new HashSet<GridPoint> { dock }, out _));
        Assert.False(PortNavigationRules.Fits(map, definition, site, geometry.ApproachTiles.ToHashSet(), out _));
    }

    private static async Task<byte[]> BuildPaidBoatAsync()
    {
        var policy = new BoatPolicy { Build = true };
        using var created = new PrivateWorldRuntime("probe-a", policy.CreateProvider,
            startPace: WorldStartPace.FounderSetup,
            geographyOptions: new ClankerWorld.Simulation.World.GeographyOptions("probe-a", ClankerWorld.Simulation.World.WorldSizePreset.Small));
        created.InitializeFirstTownContent();
        created.AcceptFirstTownLayout(new(187, 9));
        // These are genuine paused founder placements beside the notice place.
        // Keep voters in the same land component as the coastal Town.
        var map = created.ExportState().Map;
        var board = created.Towns[0].OriginSite!.Value;
        var placements = map.Tiles.Where(tile => map.IsBuildable(tile.Position) &&
                !map.Resources.Any(resource => resource.Position == tile.Position) &&
                !map.CampObjects.Any(item => item.Position == tile.Position))
            .OrderBy(tile => map.FootDistance(tile.Position, board)).ThenBy(tile => tile.Position.Y).ThenBy(tile => tile.Position.X)
            .Take(PrivateWorldRuntime.RequiredFounders).ToArray();
        for (var index = 0; index < placements.Length; index++)
            created.PlaceFounder($"founder:{index + 1:D32}", placements[index].Position);
        created.StartWorld();
        var state = created.ExportState();
        Assert.Equal(0, created.WorldTick);
        var warehouse = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == "first-town-warehouse");
        var inventory = state.Society.Society.Inventory;
        foreach (var (kind, quantity, held) in new[] { ("wood", 44, 4), ("stone", 12, 4), ("rope", 3, 1), ("iron", 3, 1) })
        {
            inventory = InventoryFixture.AddLot(inventory, "boat-stock-" + kind, kind, TownBorderRules.FirstTownId,
                quantity, storageBuildingId: warehouse.InstanceId);
            inventory = InventoryFixture.Reserve(inventory, "unrelated-" + kind, TownBorderRules.FirstTownId,
                "boat-stock-" + kind, held, "unrelated-town-work", long.MaxValue);
        }
        foreach (var person in state.Inhabitants)
        {
            inventory = InventoryFixture.AddLot(inventory, "travel-food:" + person.InhabitantId, "food", person.InhabitantId, 4);
            inventory = InventoryFixture.AddLot(inventory, "travel-clothing:" + person.InhabitantId, "clothing", person.InhabitantId, 1);
        }
        state = state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } };
        using var scenario = new BoatScenario(state, policy);
        await scenario.UntilAsync(() => scenario.World.Boats.Count == 1, 600);
        policy.Build = false;
        return PrivateWorldRuntimeCodec.Encode(scenario.World.ExportState());
    }

    private sealed class BoatScenario : IDisposable
    {
        internal PrivateWorldRuntime World { get; }
        internal BoatPolicy Policy { get; }
        internal BoatScenario(PrivateWorldRuntimeState state, BoatPolicy policy)
        {
            Policy = policy;
            World = PrivateWorldRuntime.Restore(state, policy.CreateProvider);
            policy.World = World;
        }
        internal async Task UntilAsync(Func<bool> done, int limit)
        {
            for (var step = 0; !done() && step < limit; step++) Assert.True((await World.AdvanceOneTickAsync()).Advanced);
            Assert.True(done(), $"Expected boat phase was not reached at {World.WorldTick}; " +
                string.Join("; ", World.Towns[0].Projects.Select(project => project.Plan.Name + ":" + project.Stage + ":" + project.Blocker)) +
                "; civic events: " + string.Join("; ", World.ExportState().Events.Where(item => item.Kind != "tick_advanced").TakeLast(30).Select(item => item.Kind + ":" + item.Detail)) +
                "; recent choices: " + string.Join("; ", Policy.Choices.TakeLast(16)));
        }
        public void Dispose() => World.Dispose();
    }

    private sealed class BoatPolicy
    {
        internal const string Author = "founder:00000000000000000000000000000001";
        internal PrivateWorldRuntime? World { get; set; }
        internal bool Build { get; set; }
        internal bool Trips { get; set; }
        internal ConcurrentQueue<string> Choices { get; } = new();
        internal ConcurrentQueue<CognitionObservation> Observations { get; } = new();
        internal IDecisionProvider CreateProvider(string actor) => new Provider(this, actor);
        private sealed class Provider(BoatPolicy policy, string actor) : IDecisionProvider
        {
            public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
            public long ProviderEpoch => 1;
            public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
            {
                var candidates = request.Observation.Candidates;
                policy.Observations.Enqueue(request.Observation);
                var selected = candidates.Where(candidate => candidate.DeterministicPriority <= 5 && candidate.Id is
                        "consume_food" or "collect_shared_food" or "take_food_from_pot" or "harvest_food" or "seek_food" or "seek_warmth" or "wear_clothing")
                    .OrderBy(candidate => candidate.DeterministicPriority).FirstOrDefault() ??
                    candidates.FirstOrDefault(candidate => candidate.Id.Contains("|yes|", StringComparison.Ordinal)) ??
                    candidates.FirstOrDefault(candidate => candidate.Id.Contains("|read|", StringComparison.Ordinal));
                string? text = null;
                var projects = policy.World?.Towns[0].Projects ?? [];
                if (selected is null && policy.Build)
                {
                    var prefix = projects.Count(project => project.Stage == "completed" && project.Plan.BoatPortId is null) switch
                    {
                        0 => "|project|port-west|183,6",
                        1 => "|project|port-south|189,14",
                        _ => "|boat_project|",
                    };
                    selected = candidates.FirstOrDefault(candidate => candidate.Id.Contains(prefix, StringComparison.Ordinal));
                    text = prefix.Contains("boat_project", StringComparison.Ordinal) ? "Passage boat" : "Port " + (projects.Count + 1);
                }
                if (selected is null && policy.Build)
                    foreach (var prefix in new[] { "town_project_deliver:", "town_project_supply:", "town_project_work:" })
                    {
                        selected = candidates.FirstOrDefault(candidate => candidate.Id.StartsWith(prefix, StringComparison.Ordinal));
                        if (selected is not null) break;
                    }
                if (selected is null && policy.Trips && actor == Author)
                    selected = candidates.FirstOrDefault(candidate => candidate.Id.StartsWith("boat_trip:", StringComparison.Ordinal));
                selected ??= candidates.FirstOrDefault(candidate => candidate.Id.Contains("|visit|", StringComparison.Ordinal));
                selected ??= candidates.Single(candidate => candidate.Id == "safe_idle");
                policy.Choices.Enqueue(actor + ":" + selected.Id);
                return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, actor, Kind, ProviderEpoch,
                    request.Observation.RunEpoch, request.Observation.DecisionGeneration, request.Observation.ObservationDigest,
                    selected.Id, 1, candidates.ToDictionary(candidate => candidate.Id, candidate => candidate.Id == selected.Id ? 1d : 0d, StringComparer.Ordinal),
                    CivicProposal: text));
            }
        }
    }
}

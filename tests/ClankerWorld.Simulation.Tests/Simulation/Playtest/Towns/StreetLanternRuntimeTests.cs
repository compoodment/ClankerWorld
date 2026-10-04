using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Viewer.Observation;
using GodotSnapshot = ClankerWorld.GodotClient.UI.OwnerWorldSnapshot;

namespace ClankerWorld.Simulation.Tests;

public sealed class StreetLanternRuntimeTests
{
    [Theory]
    [InlineData("stone")]
    [InlineData("hanging")]
    public async Task PersonalCouncilTurnsCarryAndSpendTheExactLanternBudgetWithoutChangingRoadsOrPrivateProperty(string style)
    {
        using var scenario = StreetLanternScenario.Create(style);
        var definition = style == "stone" ? StreetLanternContent.Stone() : StreetLanternContent.Hanging();
        ContentQuantity[] expected = style == "stone" ? [new("stone", 4)] : [new("iron", 1), new("wood", 4)];
        var roads = scenario.World.RoadTiles.ToArray();
        var privateProperty = PrivateBuildingMaterials(scenario.World);
        var unpaid = scenario.World.PlaceBuilding("unpaid-street-lantern", definition.CanonicalId, new(0, 0));
        Assert.False(unpaid.Applied);
        Assert.Contains("Council-approved project", unpaid.Failure);
        Assert.Equal(privateProperty, PrivateBuildingMaterials(scenario.World));
        Assert.DoesNotContain(scenario.World.WorldSimulation.Buildings, building => building.DefinitionId == definition.CanonicalId);
        await scenario.UntilAsync(() => scenario.World.Towns[0].Projects.Count == 1, 80);
        var project = scenario.Project;
        var proposal = Assert.Single(scenario.World.Towns[0].Governance!.Proposals);
        Assert.Equal(("project", "passed", TownProjectScenario.Author, 3),
            (proposal.Kind, proposal.Status, proposal.AuthorId, proposal.RequiredYes));
        Assert.Equal(3, proposal.Votes.Count(vote => vote.Yes));
        Assert.Equal((definition.CanonicalId, scenario.Policy.Name), (project.Plan.DefinitionId, project.Plan.Name));
        Assert.Equal(expected, project.Plan.Budget);
        Assert.NotNull(proposal.Project);
        Assert.Equal((project.Plan.Name, project.Plan.DefinitionId, project.Plan.Site, project.Plan.Entrance),
            (proposal.Project.Name, proposal.Project.DefinitionId, proposal.Project.Site, proposal.Project.Entrance));
        Assert.Equal(expected, proposal.Project.Budget);
        Assert.Empty(project.Deliveries);
        Assert.Equal(0, project.WorkDone);
        Assert.DoesNotContain(scenario.World.WorldSimulation.Buildings, building => building.DefinitionId == definition.CanonicalId);
        Assert.Contains(scenario.Decisions, decision => decision.InhabitantId == TownProjectScenario.Author &&
            decision.Admission is { Accepted: true, FellBack: false } &&
            decision.Admission.Intention is { Provider: DecisionProviderKind.LargeLanguageModel } intention &&
            intention.CandidateId == scenario.Policy.ProposalCandidate);
        var coordinates = scenario.Policy.ProposalCandidate!.Split('|')[4].Split(',')
            .Select(value => int.Parse(value, CultureInfo.InvariantCulture)).ToArray();
        Assert.Equal(new[] { project.Plan.Site.X, project.Plan.Site.Y, project.Plan.Entrance.X, project.Plan.Entrance.Y }, coordinates);
        Assert.Equal(1, Math.Abs(project.Plan.Site.X - project.Plan.Entrance.X) + Math.Abs(project.Plan.Site.Y - project.Plan.Entrance.Y));
        Assert.DoesNotContain(project.Plan.Site, roads);
        Assert.Contains(project.Plan.Entrance, roads);
        Assert.All(proposal.Votes, vote => Assert.Contains(scenario.Decisions, decision => decision.InhabitantId == vote.AgentId &&
            decision.Admission is { Accepted: true, FellBack: false } &&
            decision.Admission.Intention is { Provider: DecisionProviderKind.LargeLanguageModel } intention &&
            intention.CandidateId.Contains("|yes|" + proposal.Id + "|", StringComparison.Ordinal)));
        Assert.Equal(privateProperty, PrivateBuildingMaterials(scenario.World));

        scenario.Policy.Supply = true;
        await scenario.UntilAsync(() => scenario.Project.Deliveries.Any(delivery => delivery.DeliveredTick is null && delivery.ReleasedTick is null), 100);
        var load = scenario.Project.Deliveries.First(delivery => delivery.DeliveredTick is null && delivery.ReleasedTick is null);
        var carried = scenario.World.Society.Inventory.GetLot(load.LotId);
        Assert.Equal((TownBorderRules.FirstTownId, load.ContributorId, load.ItemKind, load.Quantity),
            (carried.OwnerId, carried.CarrierId, carried.ItemKind, carried.Quantity));
        Assert.Null(carried.StorageBuildingId);
        Assert.Null(carried.GroundPosition);
        Assert.Contains(load.SourceLotId, new[] { TownProjectScenario.WoodStock, TownProjectScenario.StoneStock, StreetLanternScenario.IronStock });
        foreach (var cost in expected)
            Assert.Equal(StreetLanternScenario.InitialQuantity(cost.ResourceId), TownQuantity(scenario.World, cost.ResourceId));

        scenario.World.Pause();
        var transit = PrivateWorldRuntimeCodec.Encode(scenario.World.ExportState());
        Assert.False((await scenario.World.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(transit, PrivateWorldRuntimeCodec.Encode(scenario.World.ExportState()));
        AssertUnpaidGeometryDamageRefused(transit, scenario.World);
        scenario.Reload(transit);
        Assert.Equal(transit, PrivateWorldRuntimeCodec.Encode(scenario.World.ExportState()));
        Assert.Equal(carried, scenario.World.Society.Inventory.GetLot(carried.Id));
        var replayPolicy = new StreetLanternPolicy(style) { Proposed = true, Supply = true };
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(transit), _ => replayPolicy);
        scenario.World.Resume();
        replay.Resume();
        await scenario.UntilAsync(() => scenario.Project.Stage == "completed", 180, replay);
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(scenario.World.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));

        project = scenario.Project;
        Assert.Equal(10, project.WorkDone);
        Assert.Contains(scenario.World.ExportState().Events, item => item.Kind == "town_project_worked" && item.Position == project.Plan.Site);
        var fixture = Assert.Single(scenario.World.WorldSimulation.Buildings, building => building.DefinitionId == definition.CanonicalId);
        Assert.Equal((project.CompletedBuildingId, TownBorderRules.FirstTownId, (string?)null, project.Plan.Site, (GridPoint?)project.Plan.Entrance),
            (fixture.InstanceId, fixture.TownId, fixture.HouseholdId, fixture.Position, fixture.Entrance));
        Assert.Equal(roads, scenario.World.RoadTiles);
        Assert.Equal(privateProperty, PrivateBuildingMaterials(scenario.World));
        Assert.All(project.Deliveries, delivery => Assert.Equal(delivery.ItemKind switch
        {
            "wood" => TownProjectScenario.WoodStock,
            "stone" => TownProjectScenario.StoneStock,
            "iron" => StreetLanternScenario.IronStock,
            _ => throw new InvalidDataException("A lantern may spend only its exact supplied construction materials."),
        }, delivery.SourceLotId));
        foreach (var cost in expected)
        {
            var deliveries = project.Deliveries.Where(delivery => delivery.ItemKind == cost.ResourceId && delivery.ReleasedTick is null).ToArray();
            Assert.Equal(cost.Amount, deliveries.Sum(delivery => delivery.Quantity));
            Assert.All(deliveries, delivery =>
            {
                Assert.NotNull(delivery.DeliveredTick);
                var receipt = scenario.World.Society.Inventory.GetReservation(delivery.ReservationId!);
                Assert.Equal((TownBorderRules.FirstTownId, delivery.LotId, delivery.Quantity, InventoryReservationState.Completed),
                    (receipt.OwnerId, receipt.LotId, receipt.Quantity, receipt.State));
            });
        }
        foreach (var kind in new[] { "wood", "stone", "iron" })
            Assert.Equal(StreetLanternScenario.InitialQuantity(kind) - expected.SingleOrDefault(cost => cost.ResourceId == kind).Amount,
                TownQuantity(scenario.World, kind));
        Assert.All(scenario.World.Society.Inventory.Reservations.Where(receipt => receipt.Purpose == "unrelated-town-work"),
            receipt => Assert.Equal(InventoryReservationState.Reserved, receipt.State));
        AssertProjection(scenario.World, project, definition, style);
        AssertPaidHistoryDamageRefused(scenario.World.ExportState(), scenario.World);
        scenario.World.Validate();
    }

    private static int TownQuantity(PrivateWorldRuntime world, string kind) => world.Society.Inventory.Lots
        .Where(lot => lot.OwnerId == TownBorderRules.FirstTownId && lot.ItemKind == kind).Sum(lot => lot.Quantity);

    private static string PrivateBuildingMaterials(PrivateWorldRuntime world) => JsonSerializer.Serialize(new
    {
        Buildings = world.WorldSimulation.Buildings.Where(building => building.HouseholdId is not null).OrderBy(building => building.InstanceId, StringComparer.Ordinal),
        Lots = world.Society.Inventory.Lots.Where(lot => lot.OwnerId != TownBorderRules.FirstTownId && lot.ItemKind is "wood" or "stone" or "iron")
            .OrderBy(lot => lot.Id, StringComparer.Ordinal).Select(lot => new
            { lot.Id, lot.OwnerId, lot.ItemKind, lot.Quantity, lot.StorageBuildingId, lot.CarrierId, lot.GroundPosition }),
    });

    private static void AssertProjection(PrivateWorldRuntime world, TownConstructionProject project, BuildingDefinition definition, string style)
    {
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        var snapshot = new OwnerWorldObservationStore(world).GetSnapshot();
        var client = JsonSerializer.Deserialize<GodotSnapshot>(JsonSerializer.Serialize(snapshot, TownProjectScenario.JsonOptions), TownProjectScenario.JsonOptions)!;
        var visible = Assert.Single(snapshot.PlacedBuildings, building => building.InstanceId == project.CompletedBuildingId);
        var received = Assert.Single(client.PlacedBuildings, building => building.InstanceId == project.CompletedBuildingId);
        Assert.Equal((definition.CanonicalId, definition.DisplayName, TownBorderRules.FirstTownId, (string?)null, 1, 1),
            (visible.DefinitionId, visible.DisplayName, visible.TownId, visible.HouseholdId, visible.Width, visible.Height));
        Assert.Contains("street_lantern", visible.Tags!);
        Assert.Contains(style + "_lantern", visible.Tags!);
        Assert.Equal((project.Plan.Site.X, project.Plan.Site.Y, project.Plan.Entrance.X, project.Plan.Entrance.Y),
            (visible.Position.X, visible.Position.Y, visible.Entrance!.X, visible.Entrance.Y));
        Assert.Equal((visible.DefinitionId, visible.DisplayName, visible.Position.X, visible.Position.Y, visible.Entrance.X, visible.Entrance.Y),
            (received.DefinitionId, received.DisplayName, received.Position.X, received.Position.Y, received.Entrance!.X, received.Entrance.Y));
        Assert.Equal(visible.Tags, received.Tags);
        var projected = Assert.Single(snapshot.Towns[0].Projects);
        var mirrored = Assert.Single(client.Towns[0].Projects);
        Assert.Contains("street_lantern", projected.Tags);
        Assert.Contains(style + "_lantern", projected.Tags);
        Assert.Equal(projected.Tags, mirrored.Tags);
        Assert.Equal(projected.Tags, mirrored.Approval.Project!.Tags);
        Assert.Equal((1, 1, 10, 10, "completed", project.Plan.Name),
            (projected.Width, projected.Height, projected.WorkDone, projected.WorkRequired, projected.Stage, projected.Name));
        Assert.Equal((projected.Width, projected.Height, projected.Name, projected.Site.X, projected.Site.Y, projected.Entrance.X, projected.Entrance.Y),
            (mirrored.Width, mirrored.Height, mirrored.Name, mirrored.Site.X, mirrored.Site.Y, mirrored.Entrance.X, mirrored.Entrance.Y));
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
    }

    private static void AssertUnpaidGeometryDamageRefused(byte[] source, PrivateWorldRuntime world)
    {
        foreach (var damage in new[] { "budget", "nonadjacent", "missing-road", "on-road" })
        {
            var document = JsonNode.Parse(source)!;
            var state = document["state"]!;
            var project = state["towns"]![0]!["projects"]![0]!;
            var proposal = state["towns"]![0]!["governance"]!["proposals"]![0]!;
            if (damage == "budget")
            {
                foreach (var plan in new[] { project["plan"]!, proposal["project"]! })
                    plan["budget"]![0]!["amount"] = plan["budget"]![0]!["amount"]!.GetValue<int>() + 1;
            }
            else if (damage == "nonadjacent")
            {
                foreach (var plan in new[] { project["plan"]!, proposal["project"]! })
                {
                    plan["entrance"]!["x"] = plan["site"]!["x"]!.GetValue<int>() + 2;
                    plan["entrance"]!["y"] = plan["site"]!["y"]!.GetValue<int>();
                }
            }
            else
            {
                var roads = state["roadTiles"]!.AsArray();
                if (damage == "on-road") roads.Add(project["plan"]!["site"]!.DeepClone());
                else
                {
                    var entrance = project["plan"]!["entrance"]!;
                    roads.Remove(roads.Single(point => point!["x"]!.GetValue<int>() == entrance["x"]!.GetValue<int>() &&
                        point["y"]!.GetValue<int>() == entrance["y"]!.GetValue<int>()));
                }
            }
            Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Decode(Encoding.UTF8.GetBytes(document.ToJsonString())));
            Assert.Equal(source, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        }
    }

    private static void AssertPaidHistoryDamageRefused(PrivateWorldRuntimeState state, PrivateWorldRuntime world)
    {
        var source = PrivateWorldRuntimeCodec.Encode(state);
        var project = Assert.Single(state.Towns![0].Projects);
        var damaged = JsonNode.Parse(source)!;
        damaged["state"]!["towns"]![0]!["projects"]![0]!["deliveries"]![0]!["reservationId"] = "unpaid-lantern-input";
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Decode(Encoding.UTF8.GetBytes(damaged.ToJsonString())));
        Assert.Equal(source, PrivateWorldRuntimeCodec.Encode(world.ExportState()));

        // Isolate the orphan-fixture check from generic missing-proposal or orphan-receipt checks.
        var orphanTown = state.Towns[0] with { Projects = [], Governance = state.Towns[0].Governance! with { Proposals = [] } };
        var paidReceipts = project.Deliveries.Select(item => item.ReservationId).ToHashSet(StringComparer.Ordinal);
        var withoutReceipts = state.Society.Society with
        {
            Inventory = state.Society.Society.Inventory with
            { Reservations = state.Society.Society.Inventory.Reservations.Where(receipt => !paidReceipts.Contains(receipt.Id)).ToArray() },
        };
        var error = Assert.Throws<InvalidDataException>(() => TownProjectValidation.Validate([orphanTown], withoutReceipts, state.Map,
            state.WorldSimulation!, state.WorldContent!, state.TownLandTitles!, state.HouseholdLandUseRights!,
            state.HouseholdLandUseRequests!, state.Fields!, state.RoadTiles!, state.Bridges!));
        Assert.Equal("A Town construction receipt or building has no matching paid project.", error.Message);
        Assert.Equal(source, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
    }
}

internal sealed class StreetLanternScenario : IDisposable
{
    internal const string IronStock = "initial-town-lantern-iron";
    internal PrivateWorldRuntime World { get; private set; }
    internal StreetLanternPolicy Policy { get; }
    internal List<SocietyCognitionDispatchResult> Decisions { get; } = [];
    internal TownConstructionProject Project => Assert.Single(World.Towns[0].Projects);
    private readonly SeededMap map;

    private StreetLanternScenario(PrivateWorldRuntime world, StreetLanternPolicy policy, SeededMap map)
    { World = world; Policy = policy; this.map = map; }

    internal static StreetLanternScenario Create(string style)
    {
        using var initial = TownProjectScenario.Create(TownProjectScenario.PlayableSeed, initialTownStock: true);
        var state = initial.World.ExportState();
        // Controlled initial stock reuses the existing fixture. Land, roads, positions and approvals are generated.
        var warehouse = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == "first-town-warehouse");
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, IronStock, "iron", TownBorderRules.FirstTownId,
            3, storageBuildingId: warehouse.InstanceId);
        inventory = InventoryFixture.Reserve(inventory, "unrelated-lantern-iron", TownBorderRules.FirstTownId, IronStock, 2, "unrelated-town-work", 512);
        var policy = new StreetLanternPolicy(style);
        var world = PrivateWorldRuntime.Restore(state with
        { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } }, _ => policy);
        return new(world, policy, state.Map);
    }

    internal static int InitialQuantity(string kind) => kind switch { "wood" => 28, "stone" => 16, "iron" => 3, _ => throw new ArgumentOutOfRangeException(nameof(kind)) };

    internal void Reload(byte[] checkpoint)
    {
        World.Dispose();
        World = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(checkpoint), _ => Policy);
    }

    internal async Task UntilAsync(Func<bool> reached, int maximumTicks, PrivateWorldRuntime? replay = null)
    {
        for (var tick = 0; tick < maximumTicks && !reached(); tick++)
        {
            var before = World.Inhabitants.ToDictionary(person => person.InhabitantId, person => person.Position, StringComparer.Ordinal);
            var step = await World.AdvanceOneTickAsync();
            Assert.True(step.Advanced);
            Decisions.AddRange(step.Decisions);
            if (replay is not null) Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
            foreach (var person in World.Inhabitants)
            {
                if (before.TryGetValue(person.InhabitantId, out var old) && old != person.Position)
                    Assert.True(map.CanFootStep(old, person.Position), "Lantern workers must use real foot routes.");
                Assert.InRange(PersonalEquipmentRules.CarriedQuantity(World.Society.Inventory, person.InhabitantId, person.Equipment),
                    0, PersonalEquipmentRules.Capacity(World.Society.Inventory, person.InhabitantId, person.Equipment));
            }
        }
        Assert.True(reached(), $"Lantern phase not reached at tick {World.WorldTick}; " +
            JsonSerializer.Serialize(World.Towns[0].Projects, TownProjectScenario.JsonOptions) + "; offers: " +
            string.Join("; ", Policy.Observations.TakeLast(4).Select(observation => observation.InhabitantId + ":" +
                string.Join(",", observation.Candidates.Select(candidate => candidate.Id)))));
    }

    public void Dispose() => World.Dispose();
}

internal sealed class StreetLanternPolicy(string style) : IDecisionProvider
{
    internal string Name { get; } = "Roadside " + style + " lantern";
    internal bool Proposed { get; set; }
    internal bool Supply { get; set; }
    internal string? ProposalCandidate { get; private set; }
    internal ConcurrentQueue<InhabitantObservation> Observations { get; } = new();
    public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
    public long ProviderEpoch => 1;

    public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
    {
        var observation = request.Observation;
        Observations.Enqueue(observation);
        var candidates = observation.Candidates;
        var selected = candidates.Where(candidate => candidate.DeterministicPriority <= 5 &&
                candidate.Id is "consume_food" or "collect_shared_food" or "take_food_from_pot" or "make_room_for_food" or
                    "harvest_food" or "seek_food" or "wear_clothing" or "seek_warmth")
            .OrderBy(candidate => candidate.DeterministicPriority).ThenBy(candidate => candidate.Id, StringComparer.Ordinal).FirstOrDefault() ??
            candidates.FirstOrDefault(candidate => candidate.Id.Contains("|yes|", StringComparison.Ordinal)) ??
            candidates.FirstOrDefault(candidate => candidate.Id.Contains("|read|", StringComparison.Ordinal));
        string? text = null;
        if (selected is null && observation.InhabitantId == TownProjectScenario.Author && !Proposed)
        {
            selected = candidates.FirstOrDefault(candidate => candidate.Id.Contains("|project|" + style + "-lantern|", StringComparison.Ordinal));
            if (selected is not null) { Proposed = true; ProposalCandidate = selected.Id; text = Name; }
        }
        if (selected is null && Supply)
            foreach (var prefix in new[] { "town_project_deliver:", "town_project_supply:", "town_project_work:" })
            {
                selected = candidates.FirstOrDefault(candidate => candidate.Id.StartsWith(prefix, StringComparison.Ordinal));
                if (selected is not null) break;
            }
        selected ??= candidates.FirstOrDefault(candidate => candidate.Id.Contains("|visit|", StringComparison.Ordinal));
        selected ??= candidates.Single(candidate => candidate.Id == "safe_idle");
        return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, observation.InhabitantId, Kind, ProviderEpoch,
            observation.RunEpoch, observation.DecisionGeneration, observation.ObservationDigest, selected.Id, 1,
            candidates.ToDictionary(candidate => candidate.Id, candidate => candidate.Id == selected.Id ? 1d : 0d, StringComparer.Ordinal), CivicProposal: text));
    }
}

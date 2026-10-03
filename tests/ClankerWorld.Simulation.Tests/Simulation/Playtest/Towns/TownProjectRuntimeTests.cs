using System.Collections.Concurrent;
using System.Text.Json;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Observation;
using GodotSnapshot = ClankerWorld.GodotClient.UI.OwnerWorldSnapshot;

namespace ClankerWorld.Simulation.Tests;

public sealed class TownProjectRuntimeTests
{
    [Fact]
    public async Task GeneratedPersonalTurnsGatherDonateAndBuildAPaidHallAcrossTravelReload()
    {
        using var scenario = await TownProjectScenario.ApprovedAsync("town-project-real-donation");
        var initial = scenario.World.ExportState();
        var project = Assert.Single(initial.Towns![0].Projects);
        var proposal = Assert.Single(initial.Towns[0].Governance!.Proposals);
        Assert.Equal("passed", proposal.Status);
        Assert.Equal(3, proposal.RequiredYes);
        Assert.True(proposal.Votes.Count(vote => vote.Yes) >= 3);
        Assert.Equal(TownProjectScenario.Name, proposal.Project!.Name);
        Assert.Empty(project.Deliveries);
        Assert.DoesNotContain(initial.Society.Society.Inventory.Lots,
            lot => lot.OwnerId == TownBorderRules.FirstTownId && lot.ItemKind is "wood" or "stone");
        Assert.DoesNotContain(scenario.World.WorldSimulation.Buildings,
            building => building.DefinitionId == TownHallContent.Hall3x4().CanonicalId);

        scenario.Policy.Supply = true;
        scenario.Policy.LawAfterHall = true;
        await scenario.UntilAsync(() => scenario.DonationTraveler() is not null, 180);
        var traveler = scenario.DonationTraveler()!.Value;
        var carried = scenario.World.Society.Inventory.GetLot(traveler.LotId);
        Assert.Equal(traveler.Actor, carried.OwnerId);
        Assert.True(PersonalEquipmentRules.IsCarried(carried, traveler.Actor));
        Assert.DoesNotContain(scenario.Project.Deliveries, delivery => delivery.SourceLotId == carried.Id);
        var transit = PrivateWorldRuntimeCodec.Encode(scenario.World.ExportState());
        scenario.Reload();
        Assert.Equal(transit, PrivateWorldRuntimeCodec.Encode(scenario.World.ExportState()));
        Assert.Equal(carried, scenario.World.Society.Inventory.GetLot(carried.Id));

        await scenario.UntilAsync(() => scenario.Project.Stage == "completed", 360);
        AssertPaidHall(scenario);
        Assert.Contains(scenario.Project.Deliveries, delivery => delivery.SourceLotId == carried.Id &&
            delivery.ContributorId == traveler.Actor && delivery.DeliveredTick is not null);
        Assert.Contains(scenario.Policy.Choices, choice => choice.Id.StartsWith("town_project_gather:", StringComparison.Ordinal));
        Assert.Contains(scenario.Policy.Choices, choice => choice.Id.StartsWith("town_project_donate:", StringComparison.Ordinal));
        Assert.Equal(48, scenario.World.Society.Inventory.GetLot("wood:camp-alpha").Quantity);
        Assert.Equal(24, scenario.World.Society.Inventory.GetLot("wood:camp-beta").Quantity);
        Assert.Equal(scenario.InitialLayerDigest,
            MapLayerManifestCodec.Digest(scenario.World.ExportState().Map));

        var hall = scenario.World.WorldSimulation.Buildings.Single(building =>
            building.InstanceId == scenario.Project.CompletedBuildingId);
        await scenario.UntilAsync(() => scenario.World.Towns[0].Governance!.Proposals.Any(p =>
            p.Kind == "law" && p.Text == TownProjectScenario.LaterNotice), 40);
        var law = scenario.World.Towns[0].Governance!.Proposals.Single(p => p.Kind == "law");
        var notice = scenario.World.Towns[0].Governance!.Notices.Single(n =>
            n.Kind == "proposal" && n.SubjectId == law.Id);
        await scenario.UntilAsync(() => scenario.World.Towns[0].Governance!.Knowledge.Any(k =>
            k.NoticeId == notice.Id && k.AgentId != TownProjectScenario.Author && k.SourceAgentId is null), 60);
        Assert.Contains(scenario.World.ExportState().Events, item => item.Kind == "town_civic_action" &&
            item.Detail.EndsWith("|read", StringComparison.Ordinal) &&
            item.Position is { } point && scenario.World.ExportState().Map.FootDistance(point, hall.Entrance!.Value) <= 1);
        Assert.Equal(proposal.Voters.Order(StringComparer.Ordinal),
            scenario.World.Towns[0].Governance!.Members.Order(StringComparer.Ordinal));

        var snapshot = new OwnerWorldObservationStore(scenario.World).GetSnapshot();
        var client = JsonSerializer.Deserialize<GodotSnapshot>(
            JsonSerializer.Serialize(snapshot, TownProjectScenario.JsonOptions), TownProjectScenario.JsonOptions)!;
        var visible = Assert.Single(snapshot.Towns[0].Projects);
        var received = Assert.Single(client.Towns[0].Projects);
        Assert.Equal(TownProjectScenario.Name, visible.Name);
        Assert.Equal(TownProjectScenario.Author, visible.ProposerId);
        Assert.False(string.IsNullOrWhiteSpace(visible.ProposerName));
        Assert.Equal((project.Plan.Site.X, project.Plan.Site.Y), (visible.Site.X, visible.Site.Y));
        Assert.Equal((3, 4, 10, 10, "completed"),
            (visible.Width, visible.Height, visible.WorkDone, visible.WorkRequired, visible.Stage));
        Assert.Equal("passed", visible.Approval.Status);
        Assert.Contains(visible.Materials, material => material.Kind == "wood" && material.Budget == 24 && material.Supplied == 24);
        Assert.Contains(visible.Materials, material => material.Kind == "stone" && material.Budget == 12 && material.Supplied == 12);
        Assert.Equal((visible.Id, visible.ProposalId, visible.Name, visible.ProposerName, visible.CompletedBuildingId),
            (received.Id, received.ProposalId, received.Name, received.ProposerName, received.CompletedBuildingId));
        Assert.Equal(visible.Materials.Select(m => (m.Kind, m.Budget, m.Supplied)),
            received.Materials.Select(m => (m.Kind, m.Budget, m.Supplied)));

        scenario.World.Pause();
        var completed = PrivateWorldRuntimeCodec.Encode(scenario.World.ExportState());
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(completed),
            new TownProjectPolicy { Proposed = true, LawProposed = true }.CreateProvider);
        Assert.Equal(completed, PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        Assert.False((await replay.AdvanceOneTickAsync()).Advanced);
        scenario.Policy.Supply = false;
        scenario.Policy.LawAfterHall = false;
        scenario.World.Resume();
        replay.Resume();
        for (var step = 0; step < 3; step++)
        {
            await scenario.World.AdvanceOneTickAsync();
            await replay.AdvanceOneTickAsync();
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(scenario.World.ExportState()),
                PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        }
        AssertPaidHall(scenario);
    }

    [Fact]
    public async Task InitialTownStockIsCarriedInSeveralLoadsWithoutPrivatizingOrSpendingReservedRemainders()
    {
        using var scenario = await TownProjectScenario.ApprovedAsync("town-project-warehouse-haul", initialTownStock: true);
        Assert.Empty(scenario.Project.Deliveries);
        Assert.Equal(28, scenario.World.Society.Inventory.GetLot(TownProjectScenario.WoodStock).Quantity);
        Assert.Equal(16, scenario.World.Society.Inventory.GetLot(TownProjectScenario.StoneStock).Quantity);
        scenario.Policy.Supply = true;
        await scenario.UntilAsync(() => scenario.Project.Deliveries.Any(d =>
            d.DeliveredTick is null && d.ReleasedTick is null), 80);
        var load = scenario.Project.Deliveries.First(d => d.DeliveredTick is null && d.ReleasedTick is null);
        var inHand = scenario.World.Society.Inventory.GetLot(load.LotId);
        Assert.Equal((TownBorderRules.FirstTownId, load.ContributorId, load.Quantity),
            (inHand.OwnerId, inHand.CarrierId, inHand.Quantity));
        Assert.Null(inHand.StorageBuildingId);
        Assert.Null(inHand.GroundPosition);
        Assert.Contains(load.SourceLotId, new[] { TownProjectScenario.WoodStock, TownProjectScenario.StoneStock });
        Assert.Equal(44, TownProjectScenario.MaterialQuantity(scenario.World.Society.Inventory, TownBorderRules.FirstTownId));
        var carried = PrivateWorldRuntimeCodec.Encode(scenario.World.ExportState());
        scenario.Reload();
        Assert.Equal(carried, PrivateWorldRuntimeCodec.Encode(scenario.World.ExportState()));

        await scenario.UntilAsync(() => scenario.Project.Stage == "completed", 240);
        AssertPaidHall(scenario);
        Assert.True(scenario.Project.Deliveries.Select(d => d.ContributorId).Distinct(StringComparer.Ordinal).Count() > 1);
        Assert.All(scenario.Project.Deliveries, delivery =>
            Assert.Contains(delivery.SourceLotId, new[] { TownProjectScenario.WoodStock, TownProjectScenario.StoneStock }));
        Assert.Equal(4, scenario.World.Society.Inventory.GetLot(TownProjectScenario.WoodStock).Quantity);
        Assert.Equal(4, scenario.World.Society.Inventory.GetLot(TownProjectScenario.StoneStock).Quantity);
        Assert.All(scenario.World.Society.Inventory.Reservations.Where(r => r.Purpose == "unrelated-town-work"),
            reservation => Assert.Equal(InventoryReservationState.Reserved, reservation.State));
        Assert.Equal(48, scenario.World.Society.Inventory.GetLot("wood:camp-alpha").Quantity);
        Assert.Equal(24, scenario.World.Society.Inventory.GetLot("wood:camp-beta").Quantity);
        scenario.World.Validate();

        var hall = scenario.World.WorldSimulation.Buildings.Single(building => building.InstanceId == scenario.Project.CompletedBuildingId);
        var paid = scenario.Project;
        var consumed = scenario.World.Society.Inventory.Reservations.Where(receipt =>
            paid.Deliveries.Any(delivery => delivery.ReservationId == receipt.Id)).ToArray();
        var missing = System.Text.Json.Nodes.JsonNode.Parse(PrivateWorldRuntimeCodec.Encode(scenario.World.ExportState()))!;
        var buildings = missing["state"]!["worldSimulation"]!["buildings"]!.AsArray();
        buildings.Remove(buildings.Single(item => item!["instanceId"]!.GetValue<string>() == hall.InstanceId));
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Decode(System.Text.Encoding.UTF8.GetBytes(missing.ToJsonString())));
        var removed = scenario.World.RemoveBuilding(hall.InstanceId, hall.TownId, hall.HouseholdId);
        Assert.True(removed.Applied, removed.Failure);
        Assert.Equal(scenario.World.WorldTick, scenario.Project.RemovedTick);
        Assert.Equal(paid.LastTransitionTick, scenario.Project.LastTransitionTick);
        Assert.Equal(paid.CompletedBuildingId, scenario.Project.CompletedBuildingId);
        var savedRemoval = PrivateWorldRuntimeCodec.Encode(scenario.World.ExportState());
        scenario.Reload();
        Assert.Equal(savedRemoval, PrivateWorldRuntimeCodec.Encode(scenario.World.ExportState()));
        await scenario.World.AdvanceOneTickAsync();
        Assert.DoesNotContain(scenario.World.WorldSimulation.Buildings, building => building.InstanceId == hall.InstanceId);
        Assert.Equal(consumed, scenario.World.Society.Inventory.Reservations.Where(receipt =>
            paid.Deliveries.Any(delivery => delivery.ReservationId == receipt.Id)).ToArray());
        Assert.Equal("completed", scenario.Project.Stage);
        scenario.World.Validate();
    }

    [Theory]
    [InlineData("delivery")]
    [InlineData("completion")]
    public async Task RejectedPreparedProjectTicksLeaveAuthorityGoodsAndCompletionUnchanged(string phase)
    {
        using var scenario = await TownProjectScenario.ApprovedAsync("town-project-prepared-" + phase, initialTownStock: true);
        scenario.Policy.Supply = true;
        if (phase == "delivery")
            await scenario.UntilAsync(() => scenario.Project.Deliveries.Any(d => d.DeliveredTick is null &&
                d.ReleasedTick is null && scenario.World.Inhabitants.Single(p => p.InhabitantId == d.ContributorId).Position == scenario.Project.Plan.Site), 120);
        else
            await scenario.UntilAsync(() => scenario.Project.WorkDone == 9 && scenario.Project.Stage != "completed", 240);
        var before = PrivateWorldRuntimeCodec.Encode(scenario.World.ExportState());
        var deliveredBefore = scenario.Project.Deliveries.Count(d => d.DeliveredTick is not null);
        Assert.False((await scenario.World.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(scenario.World.ExportState()));
        scenario.Reload();
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(scenario.World.ExportState()));
        if (phase == "delivery")
        {
            await scenario.UntilAsync(() => scenario.Project.Deliveries.Count(d => d.DeliveredTick is not null) > deliveredBefore, 8);
            Assert.Equal(44, TownProjectScenario.MaterialQuantity(scenario.World.Society.Inventory, TownBorderRules.FirstTownId));
        }
        else
        {
            await scenario.UntilAsync(() => scenario.Project.Stage == "completed", 8);
            AssertPaidHall(scenario);
        }
    }

    [Fact]
    public async Task PendingHouseholdLandRequestBlocksAnAlreadyApprovedHallWithoutConfiscation()
    {
        using var scenario = await TownProjectScenario.ApprovedAsync("town-project-live-land-request", initialTownStock: true);
        var titles = scenario.World.TownLandTitles.ToArray();
        var rights = scenario.World.HouseholdLandUseRights.ToArray();
        var requested = scenario.World.RequestHouseholdLandUse("request:hall-site", TownProjectScenario.Author,
            TownBorderRules.FirstTownId, WorldContentSimulationRules.Footprint(TownHallContent.Hall3x4(), scenario.Project.Plan.Site).ToArray());
        Assert.True(requested.Applied, requested.Failure);
        scenario.Policy.Supply = true;
        await scenario.UntilAsync(() => scenario.Project.Blocker is not null, 8);
        Assert.Contains("land", scenario.Project.Blocker!, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(scenario.Project.Deliveries);
        Assert.Equal(titles, scenario.World.TownLandTitles);
        Assert.Equal(rights, scenario.World.HouseholdLandUseRights);
        Assert.Contains(scenario.World.HouseholdLandUseRequests, request => request.Id == "request:hall-site");
        Assert.Equal(44, TownProjectScenario.MaterialQuantity(scenario.World.Society.Inventory, TownBorderRules.FirstTownId));
        Assert.DoesNotContain(scenario.World.WorldSimulation.Buildings, building => building.DefinitionId == TownHallContent.Hall3x4().CanonicalId);
    }

    [Fact]
    public async Task PassedLawNamingAHallCreatesNoConstructionOrMaterialPermission()
    {
        using var scenario = TownProjectScenario.Create("town-project-law-is-not-permission",
            new TownProjectPolicy { OrdinaryLaw = true });
        await scenario.UntilAsync(() => scenario.World.Towns[0].Governance!.Proposals.Any(p => p.Status == "passed"), 80);
        var law = Assert.Single(scenario.World.Towns[0].Governance!.Proposals);
        Assert.Equal("law", law.Kind);
        Assert.Null(law.Project);
        Assert.Empty(scenario.World.Towns[0].Projects);
        Assert.Equal(48, scenario.World.Society.Inventory.GetLot("wood:camp-alpha").Quantity);
        Assert.Equal(24, scenario.World.Society.Inventory.GetLot("wood:camp-beta").Quantity);
        Assert.DoesNotContain(scenario.World.WorldSimulation.Buildings, building => building.DefinitionId == TownHallContent.Hall3x4().CanonicalId);
    }

    [Fact]
    public async Task DelayedPersonalVoteCannotApproveAProjectAfterItsCouncilChanges()
    {
        var policy = new TownProjectPolicy { HoldVote = true };
        using var scenario = TownProjectScenario.Create("town-project-stale-council", policy, initialTownStock: true);
        for (var tick = 0; tick < 80 && !policy.VoteStarted.Task.IsCompleted; tick++)
        {
            await scenario.World.AdvanceOneTickNonBlockingAsync();
            await Task.Delay(2);
        }
        await policy.VoteStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var original = Assert.Single(scenario.World.Towns[0].Governance!.Proposals);
        Assert.Equal("project", original.Kind);
        Assert.Equal("pending", original.Status);
        Assert.Empty(scenario.World.Towns[0].Projects);
        var state = scenario.World.ExportState();
        var site = state.Towns![0].BorderTiles.First(point => state.Map.IsBuildable(point) &&
            !state.Map.Resources.Any(resource => resource.Position == point) &&
            !state.Map.CampObjects.Any(item => item.Position == point) &&
            !state.Inhabitants.Any(person => person.Position == point) &&
            !state.WorldSimulation!.Buildings.Any(building => WorldContentSimulationRules.Footprint(
                state.WorldContent!.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId), building).Contains(point)));
        scenario.World.AddAgent("agent:00000000000000000000000000000099", site);
        Assert.Equal("cancelled", scenario.World.Towns[0].Governance!.Proposals[0].Status);
        policy.ReleaseVote.TrySetResult(true);
        for (var tick = 0; tick < 8; tick++)
        {
            await scenario.World.AdvanceOneTickNonBlockingAsync();
            await Task.Delay(2);
        }
        var refused = scenario.World.Towns[0].Governance!.Proposals.Single(item => item.Id == original.Id);
        Assert.Equal("cancelled", refused.Status);
        Assert.DoesNotContain(refused.Votes, vote => vote.AgentId == TownProjectScenario.Author);
        Assert.Empty(scenario.World.Towns[0].Projects);
        Assert.Equal(44, TownProjectScenario.MaterialQuantity(scenario.World.Society.Inventory, TownBorderRules.FirstTownId));
        Assert.DoesNotContain(scenario.World.WorldSimulation.Buildings, building => building.DefinitionId == TownHallContent.Hall3x4().CanonicalId);
        scenario.World.Validate();
    }

    [Fact]
    public void TypedProjectUsesTheOrdinaryFinalMajorityWindowAndEquivalentNamesCannotForkIt()
    {
        string[] adults = ["a", "b", "c", "d"];
        var hall = TownHallContent.Hall3x4();
        var plan = new TownProjectPayload("Civic Hall", hall.CanonicalId, new(4, 4),
            new(5, 8), [new("wood", 24), new("stone", 12)]);
        var state = TownGovernanceRules.SubmitProposal(TownGovernanceState.Create(adults),
            "town:test", "a", "project", null, "Ignore free-form cost claims.", "unchanged", adults, 0, 10, plan);
        var proposal = Assert.Single(state.Proposals);
        Assert.Equal(10, proposal.DeadlineTick);
        Assert.Equal(3, proposal.RequiredYes);
        var duplicate = TownGovernanceRules.SubmitProposal(state, "town:test", "b", "project", null,
            "Other wording.", "unchanged", adults, 2, 10, plan with { Name = "Renamed Hall" });
        Assert.Single(duplicate.Proposals);
        Assert.Equal(proposal, duplicate.Proposals[0]);
        state = TownGovernanceRules.VoteProposal(state, proposal.Id, "a", true, 2);
        state = TownGovernanceRules.VoteProposal(state, proposal.Id, "b", true, 2);
        Assert.Equal("pending", state.Proposals[0].Status);
        Assert.Throws<InvalidOperationException>(() => TownGovernanceRules.VoteProposal(state, proposal.Id, "a", false, 3));
        state = TownGovernanceRules.VoteProposal(state, proposal.Id, "c", true, 3);
        Assert.Equal("passed", state.Proposals[0].Status);
        Assert.Equal(plan, state.Proposals[0].Project);
        Assert.Throws<InvalidOperationException>(() => TownGovernanceRules.SubmitProposal(state,
            "town:test", "visitor", "project", null, "Hall", "unchanged", adults, 3, 10, plan));
        Assert.Throws<InvalidOperationException>(() => TownGovernanceRules.SubmitProposal(state,
            "town:test", "a", "law", null, "Build a Hall.", "unchanged", adults, 3, 10, plan));
        Assert.Throws<InvalidDataException>(() => TownGovernanceRules.SubmitProposal(state,
            "town:test", "a", "project", null, "Hall", "unchanged", adults, 3, 10,
            plan with { Budget = [new("wood", 25), new("stone", 12)] }));
    }

    [Theory]
    [InlineData("town:first", false, 4)]
    [InlineData("household:camp-alpha", false, 0)]
    [InlineData("town:other", false, 0)]
    [InlineData("town:first", true, 0)]
    public void OnlyReservedDeliveredStockAtTheExactSiteCountsTowardTheTownBudget(string owner, bool remote, int expected)
    {
        var hall = TownHallContent.Hall3x4();
        var plan = new TownProjectPayload("Hall", hall.CanonicalId, new(4, 4), new(5, 8),
            [new("wood", 24), new("stone", 12)]);
        var inventory = InventoryFixture.CreateGenesis([new InventoryLot("load", "wood", owner, 4, 10_000, 10_000, 0,
            GroundPosition: remote ? new(9, 9) : new(4, 4))]);
        inventory = InventoryFixture.Reserve(inventory, "paid-load", owner, "load", 4, "town-project:project", 10);
        var project = new TownConstructionProject("project", "proposal", plan, 0, "supplying", 0, 0,
            [new("delivery", "worker", "source", "load", "wood", 4, 0, 0, "paid-load")]);
        Assert.Equal(expected, TownProjectRules.DeliveredQuantity(project, TownBorderRules.FirstTownId, inventory, "wood"));
        Assert.Equal(4, inventory.GetLot("load").Quantity);
        Assert.Equal(InventoryReservationState.Reserved, inventory.GetReservation("paid-load").State);
    }

    internal static void AssertPaidHall(TownProjectScenario scenario)
    {
        var project = scenario.Project;
        Assert.Equal("completed", project.Stage);
        Assert.Equal(10, project.WorkDone);
        var hall = Assert.Single(scenario.World.WorldSimulation.Buildings, building =>
            building.DefinitionId == TownHallContent.Hall3x4().CanonicalId);
        Assert.Equal((project.CompletedBuildingId, project.Plan.Site, TownBorderRules.FirstTownId, (string?)null),
            (hall.InstanceId, hall.Position, hall.TownId, hall.HouseholdId));
        Assert.Equal(new GridPoint(project.Plan.Site.X + 1, project.Plan.Site.Y + 4), hall.Entrance);
        var payments = scenario.World.Society.Inventory.Reservations.Where(reservation =>
            project.Deliveries.Any(delivery => delivery.ReservationId == reservation.Id)).ToArray();
        Assert.Equal(24, project.Deliveries.Where(d => d.ItemKind == "wood" && d.ReleasedTick is null).Sum(d => d.Quantity));
        Assert.Equal(12, project.Deliveries.Where(d => d.ItemKind == "stone" && d.ReleasedTick is null).Sum(d => d.Quantity));
        Assert.True(project.Deliveries.Count(d => d.ItemKind == "wood") > 1);
        Assert.True(project.Deliveries.Count(d => d.ItemKind == "stone") > 1);
        Assert.Equal(36, payments.Sum(payment => payment.Quantity));
        Assert.All(payments, payment =>
        {
            Assert.Equal(TownBorderRules.FirstTownId, payment.OwnerId);
            Assert.Equal(InventoryReservationState.Completed, payment.State);
        });
        Assert.Single(scenario.World.Towns[0].Projects);
        Assert.All(project.Deliveries.Where(d => d.ReleasedTick is null), delivery =>
        {
            Assert.NotNull(delivery.DeliveredTick);
            Assert.NotNull(delivery.ReservationId);
            Assert.Equal(delivery.Quantity, payments.Single(p => p.Id == delivery.ReservationId).Quantity);
        });
        scenario.World.Validate();
    }
}

internal sealed class TownProjectScenario : IDisposable
{
    internal const string Author = "founder:00000000000000000000000000000001";
    internal const string Name = "Communal Hall";
    internal const string LaterNotice = "Post the next harvest dates at our Hall.";
    internal const string WoodStock = "initial-town-wood";
    internal const string StoneStock = "initial-town-stone";
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    internal PrivateWorldRuntime World { get; private set; }
    internal TownProjectPolicy Policy { get; }
    internal string InitialLayerDigest { get; }
    internal TownConstructionProject Project => Assert.Single(World.Towns[0].Projects);

    private TownProjectScenario(PrivateWorldRuntime world, TownProjectPolicy policy)
    {
        World = world;
        Policy = policy;
        policy.HallCompleted = () => World.Towns[0].Projects.Any(project => project.Stage == "completed");
        InitialLayerDigest = MapLayerManifestCodec.Digest(world.ExportState().Map) ??
            throw new InvalidOperationException("A normal generated fixture must retain all independent map layers.");
    }

    internal static TownProjectScenario Create(string seed, TownProjectPolicy? policy = null, bool initialTownStock = false)
    {
        policy ??= new();
        var world = NormalPathWorld.CreateGenerated(seed, policy.CreateProvider);
        if (initialTownStock)
        {
            // Controlled initial stock only: no tick, proposal or project has run yet.
            var state = world.ExportState();
            Assert.Equal(0, world.WorldTick);
            Assert.Empty(state.Towns![0].Governance!.Proposals);
            var warehouse = state.WorldSimulation!.Buildings.Single(b => b.InstanceId == "first-town-warehouse");
            var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, WoodStock, "wood",
                TownBorderRules.FirstTownId, 28, storageBuildingId: warehouse.InstanceId);
            inventory = InventoryFixture.AddLot(inventory, StoneStock, "stone",
                TownBorderRules.FirstTownId, 16, storageBuildingId: warehouse.InstanceId);
            inventory = InventoryFixture.Reserve(inventory, "unrelated-wood", TownBorderRules.FirstTownId,
                WoodStock, 4, "unrelated-town-work", 512);
            inventory = InventoryFixture.Reserve(inventory, "unrelated-stone", TownBorderRules.FirstTownId,
                StoneStock, 4, "unrelated-town-work", 512);
            world.Dispose();
            world = PrivateWorldRuntime.Restore(state with
            {
                Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
            }, policy.CreateProvider);
        }
        return new(world, policy);
    }

    internal static async Task<TownProjectScenario> ApprovedAsync(string seed, bool initialTownStock = false)
    {
        var scenario = Create(seed, initialTownStock: initialTownStock);
        try
        {
            await scenario.UntilAsync(() => scenario.World.Towns[0].Projects.Count == 1, 80);
            return scenario;
        }
        catch { scenario.Dispose(); throw; }
    }

    internal async Task UntilAsync(Func<bool> reached, int maximumTicks)
    {
        for (var tick = 0; tick < maximumTicks && !reached(); tick++)
        {
            var before = World.Inhabitants.ToDictionary(person => person.InhabitantId, person => person.Position, StringComparer.Ordinal);
            Assert.True((await World.AdvanceOneTickAsync()).Advanced);
            var state = World.ExportState();
            foreach (var person in state.Inhabitants)
            {
                if (before.TryGetValue(person.InhabitantId, out var old) && old != person.Position)
                    Assert.True(state.Map.CanFootStep(old, person.Position), "The project must move by lawful foot steps, without teleporting.");
                Assert.InRange(PersonalEquipmentRules.CarriedQuantity(state.Society.Society.Inventory, person.InhabitantId, person.Equipment),
                    0, PersonalEquipmentRules.Capacity(state.Society.Society.Inventory, person.InhabitantId, person.Equipment));
            }
        }
        Assert.True(reached(), $"Phase not reached by tick {World.WorldTick}: " +
            JsonSerializer.Serialize(World.Towns[0].Projects, JsonOptions) + " Choices: " +
            string.Join(", ", Policy.Choices.TakeLast(12).Select(choice => choice.Id)));
    }

    internal (string Actor, string LotId)? DonationTraveler()
    {
        foreach (var choice in Policy.Choices.Reverse().Where(c => c.Id.StartsWith("town_project_donate:", StringComparison.Ordinal)))
        {
            var person = World.Inhabitants.Single(p => p.InhabitantId == choice.Actor);
            if (person.Position == Project.Plan.Site) continue;
            var lot = World.Society.Inventory.Lots.FirstOrDefault(l =>
                l.OwnerId == choice.Actor && l.ItemKind is "wood" or "stone" &&
                PersonalEquipmentRules.IsCarried(l, choice.Actor) && !Project.Deliveries.Any(d => d.SourceLotId == l.Id));
            if (lot is not null) return (choice.Actor, lot.Id);
        }
        return null;
    }

    internal void Reload()
    {
        var state = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(World.ExportState()));
        World.Dispose();
        World = PrivateWorldRuntime.Restore(state, Policy.CreateProvider);
    }

    internal static int MaterialQuantity(InventoryCheckpoint inventory, string owner) =>
        inventory.Lots.Where(l => l.OwnerId == owner && l.ItemKind is "wood" or "stone").Sum(l => l.Quantity);

    public void Dispose() => World.Dispose();
}

internal sealed class TownProjectPolicy
{
    internal bool Proposed { get; set; }
    internal bool OrdinaryLaw { get; init; }
    internal bool Supply { get; set; }
    internal bool LawAfterHall { get; set; }
    internal bool LawProposed { get; set; }
    internal Func<bool>? HallCompleted { get; set; }
    internal bool HoldVote { get; init; }
    internal TaskCompletionSource<bool> VoteStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource<bool> ReleaseVote { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal ConcurrentQueue<(string Actor, string Id, long Tick)> Choices { get; } = new();

    internal IDecisionProvider CreateProvider(string actor) => new Provider(this, actor);

    private sealed class Provider(TownProjectPolicy policy, string actor) : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
        public long ProviderEpoch => 1;

        public async ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var observation = request.Observation;
            var candidates = observation.Candidates;
            var selected = (!policy.HoldVote || actor == TownProjectScenario.Author
                ? candidates.FirstOrDefault(c => c.Id.Contains("|yes|", StringComparison.Ordinal)) : null) ??
                candidates.FirstOrDefault(c => c.Id.Contains("|read|", StringComparison.Ordinal));
            string? text = null;
            if (selected is null && actor == TownProjectScenario.Author && !policy.Proposed)
            {
                selected = candidates.FirstOrDefault(c => c.Id.Contains(policy.OrdinaryLaw ? "|propose|" : "|project|", StringComparison.Ordinal));
                if (selected is not null)
                {
                    policy.Proposed = true;
                    text = policy.OrdinaryLaw ? "Build a Communal Hall with all the Town's wood." : TownProjectScenario.Name;
                }
            }
            if (selected is null && actor == TownProjectScenario.Author && policy.LawAfterHall && !policy.LawProposed &&
                policy.HallCompleted?.Invoke() == true)
            {
                selected = candidates.FirstOrDefault(c => c.Id.Contains("|propose|", StringComparison.Ordinal));
                if (selected is not null) { policy.LawProposed = true; text = TownProjectScenario.LaterNotice; }
            }
            if (selected is null && policy.Supply)
                foreach (var prefix in new[] { "town_project_deliver:", "town_project_donate:", "town_project_supply:",
                    "town_project_work:", "town_project_gather:", "town_project_return:" })
                {
                    selected = candidates.FirstOrDefault(c => c.Id.StartsWith(prefix, StringComparison.Ordinal));
                    if (selected is not null) break;
                }
            selected ??= candidates.FirstOrDefault(c => c.Id.Contains("|visit|", StringComparison.Ordinal));
            selected ??= candidates.Single(c => c.Id == "safe_idle");
            policy.Choices.Enqueue((actor, selected.Id, observation.WorldTick));
            if (policy.HoldVote && actor == TownProjectScenario.Author && selected.Id.Contains("|yes|", StringComparison.Ordinal))
            {
                policy.VoteStarted.TrySetResult(true);
                // Deliberately ignore cancellation so admission must reject the old Council reply.
                await policy.ReleaseVote.Task;
            }
            return new CognitionDecisionResponse(request.RequestId, observation.InhabitantId,
                Kind, ProviderEpoch, observation.RunEpoch, observation.DecisionGeneration, observation.ObservationDigest,
                selected.Id, 1, candidates.ToDictionary(c => c.Id, c => c.Id == selected.Id ? 1d : 0d, StringComparer.Ordinal),
                CivicProposal: text);
        }
    }
}

using System.Globalization;
using System.Text.Json;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class MarketConstructionRuntimeTests
{
    private static readonly int[] ExpectedStarterSlots = [0, 4];

    [Fact]
    public async Task GeneratedResidentsBuildPaidMarketsAndStallsBeforeLocalTradesAcrossReload()
    {
        var policy = new TownProjectPolicy
        {
            ProjectLocalId = MarketContent.Hall2x2().LocalId,
            ProjectName = "First Market",
        };
        // This is an ordinary selectable first-Town site on the unchanged generated map.
        using var scenario = TownProjectScenario.Create(TownProjectScenario.PlayableSeed, policy,
            roughTownSite: new GridPoint(109, 52));
        var initial = scenario.World.ExportState();
        var townId = Assert.Single(initial.Towns!).Id;
        var ledger = new GatheringLedger(initial);
        Assert.Equal(0, scenario.World.WorldTick);
        Assert.Equal(4, initial.Inhabitants.Count);
        Assert.Empty(initial.Towns![0].Governance!.Proposals);
        Assert.Empty(initial.Towns[0].Projects);
        Assert.Empty(initial.Towns[0].Markets);

        await UntilAsync(scenario, () => scenario.World.Towns[0].Governance!.Proposals.Count == 1, 80, ledger);
        var pending = Assert.Single(scenario.World.Towns[0].Governance!.Proposals);
        Assert.Equal(("project", "pending", policy.ProjectName), (pending.Kind, pending.Status, pending.Project!.Name));
        Assert.Equal(MarketContent.Hall2x2().CanonicalId, pending.Project.DefinitionId);
        var offeredId = $"civic|{townId}|project|market-2x2|{pending.Project.Site.X},{pending.Project.Site.Y}";
        Assert.Contains(policy.Observations, observation => observation.Candidates.Any(candidate => candidate.Id == offeredId));
        Assert.Contains(policy.Choices, choice => choice.Actor == TownProjectScenario.Author && choice.Id == offeredId);
        Assert.Empty(scenario.World.Towns[0].Projects);
        Assert.Empty(scenario.World.Towns[0].Markets);
        Assert.Equal(JsonSerializer.Serialize(initial.WorldSimulation!.Buildings),
            JsonSerializer.Serialize(scenario.World.WorldSimulation.Buildings));
        Assert.Equal(MarketContent.HallEntrance(pending.Project.Site), pending.Project.Entrance);
        Assert.Equal(new[] { ("fiber", 4), ("stone", 8), ("wood", 24) },
            pending.Project.Budget.Select(cost => (cost.ResourceId, cost.Amount)));
        var siteTiles = MarketContent.SiteTiles(pending.Project.Site).ToHashSet();
        Assert.Equal(32, siteTiles.Count);
        Assert.All(siteTiles, tile => Assert.Contains(initial.TownLandTitles!, title => title.TownId == townId && title.Tiles.Contains(tile)));
        Assert.DoesNotContain(initial.HouseholdLandUseRights!, right => right.Tiles.Any(siteTiles.Contains));
        Assert.DoesNotContain(initial.HouseholdLandUseRequests!, request => request.Tiles.Any(siteTiles.Contains));
        AssertByteExactReload(scenario);

        await UntilAsync(scenario, () => scenario.World.Towns[0].Projects.Count == 1, 80, ledger);
        var approval = Assert.Single(scenario.World.Towns[0].Governance!.Proposals);
        Assert.Equal("passed", approval.Status);
        Assert.Equal(3, approval.RequiredYes);
        Assert.True(approval.Votes.Count(vote => vote.Yes) >= approval.RequiredYes);
        var notice = Assert.Single(scenario.World.Towns[0].Governance!.Notices,
            item => item.Kind == "proposal" && item.SubjectId == approval.Id);
        Assert.All(approval.Votes, vote =>
        {
            Assert.Contains(policy.Choices, choice => choice.Actor == vote.AgentId &&
                choice.Id == $"civic|{townId}|{(vote.Yes ? "yes" : "no")}|{approval.Id}|");
            Assert.Contains(scenario.World.Towns[0].Governance!.Knowledge,
                receipt => receipt.AgentId == vote.AgentId && receipt.NoticeId == notice.Id);
        });
        Assert.Empty(scenario.Project.Deliveries);
        Assert.DoesNotContain(scenario.World.Society.Inventory.Lots,
            lot => lot.OwnerId == townId && lot.ItemKind is "wood" or "stone" or "fiber");
        Assert.Empty(scenario.World.Towns[0].Markets);
        Assert.Equal(JsonSerializer.Serialize(initial.WorldSimulation.Buildings),
            JsonSerializer.Serialize(scenario.World.WorldSimulation.Buildings));

        policy.Supply = true;
        policy.CollectTools = true;
        await UntilAsync(scenario, () => scenario.DonationTraveler() is not null, 180, ledger);
        var traveler = scenario.DonationTraveler()!.Value;
        var carried = scenario.World.Society.Inventory.GetLot(traveler.LotId);
        var travelChoice = policy.Choices.Last(choice => choice.Actor == traveler.Actor &&
            choice.Id.StartsWith("town_project_donate:", StringComparison.Ordinal));
        Assert.Equal(traveler.Actor, carried.OwnerId);
        Assert.True(PersonalEquipmentRules.IsCarried(carried, traveler.Actor));
        Assert.DoesNotContain(scenario.Project.Deliveries, delivery => delivery.SourceLotId == carried.Id);
        AssertByteExactReload(scenario);
        Assert.Equal(carried, scenario.World.Society.Inventory.GetLot(carried.Id));

        policy.HoldDonationForActor = traveler.Actor;
        for (var tick = 0; tick < 100 && !policy.DonationStarted.Task.IsCompleted; tick++)
        {
            await scenario.World.AdvanceOneTickNonBlockingAsync();
            ledger.Observe(scenario.World.ExportState());
            await Task.Delay(2);
        }
        await policy.DonationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(scenario.Project.Plan.Site,
            scenario.World.Inhabitants.Single(person => person.InhabitantId == traveler.Actor).Position);
        var freshChoice = policy.Choices.Last(choice => choice.Actor == traveler.Actor &&
            choice.Id.StartsWith("town_project_donate:", StringComparison.Ordinal));
        Assert.NotEqual(travelChoice.Id, freshChoice.Id);
        Assert.True(freshChoice.Tick > travelChoice.Tick);
        for (var tick = 0; tick < 3; tick++)
        {
            await scenario.World.AdvanceOneTickNonBlockingAsync();
            ledger.Observe(scenario.World.ExportState());
            Assert.Equal(traveler.Actor, scenario.World.Society.Inventory.GetLot(carried.Id).OwnerId);
            Assert.DoesNotContain(scenario.Project.Deliveries, delivery => delivery.SourceLotId == carried.Id);
        }
        policy.ReleaseDonation.TrySetResult(true);

        await UntilAsync(scenario, () => scenario.Project.Stage == "working", 420, ledger);
        Assert.InRange(scenario.Project.WorkDone, 0, 9);
        Assert.All(scenario.Project.Plan.Budget, cost => Assert.Equal(cost.Amount,
            TownProjectRules.DeliveredQuantity(scenario.Project, townId, scenario.World.Society.Inventory, cost.ResourceId)));
        Assert.All(scenario.Project.Deliveries, delivery =>
        {
            Assert.NotNull(delivery.DeliveredTick);
            Assert.Null(delivery.ReleasedTick);
            var input = scenario.World.Society.Inventory.GetReservation(delivery.ReservationId!);
            var lot = scenario.World.Society.Inventory.GetLot(delivery.LotId);
            Assert.Equal((townId, delivery.Quantity, InventoryReservationState.Reserved),
                (input.OwnerId, input.Quantity, input.State));
            Assert.Equal(new InventoryGroundPosition(scenario.Project.Plan.Site.X, scenario.Project.Plan.Site.Y), lot.GroundPosition);
            Assert.Equal(townId, lot.OwnerId);
            Assert.Null(lot.CarrierId);
            Assert.Null(lot.StorageBuildingId);
        });
        AssertByteExactReload(scenario);
        await UntilAsync(scenario, () => scenario.Project.Stage == "completed", 40, ledger);

        var completed = scenario.World.ExportState();
        var project = scenario.Project;
        var market = Assert.Single(completed.Towns![0].Markets);
        Assert.Equal((project.Id, project.CompletedBuildingId, project.Plan.Site),
            (market.ProjectId, market.HallBuildingId, market.Site));
        Assert.Equal(10, project.WorkDone);
        Assert.Equal(ExpectedStarterSlots, market.Stalls.Select(stall => stall.SlotIndex).Order());
        var paidIds = market.Stalls.Select(stall => stall.BuildingId).Append(market.HallBuildingId).ToHashSet(StringComparer.Ordinal);
        var originalIds = initial.WorldSimulation.Buildings.Select(building => building.InstanceId).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(paidIds.Order(StringComparer.Ordinal), completed.WorldSimulation!.Buildings
            .Where(building => !originalIds.Contains(building.InstanceId)).Select(building => building.InstanceId).Order(StringComparer.Ordinal));
        Assert.Equal(2, completed.WorldSimulation.Buildings.Count(building => building.DefinitionId == MarketContent.Stall1x1().CanonicalId));
        Assert.All(market.Stalls, stall =>
        {
            var building = Assert.Single(completed.WorldSimulation.Buildings, item => item.InstanceId == stall.BuildingId);
            Assert.Equal((project.Id, MarketContent.StallSite(market.Site, stall.SlotIndex), townId, (string?)null),
                (stall.ProjectId, building.Position, building.TownId, building.HouseholdId));
            Assert.Equal(MarketContent.StallEntrance(market.Site, stall.SlotIndex), building.Entrance);
        });
        var buildingTiles = completed.WorldSimulation.Buildings.SelectMany(building => WorldContentSimulationRules.Footprint(
            completed.WorldContent!.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId), building));
        Assert.DoesNotContain(buildingTiles, tile => scenario.World.RoadTiles.Contains(tile));
        Assert.Single(completed.Events, item => item.Kind == "market_built");
        Assert.Contains(project.Deliveries, delivery => delivery.SourceLotId == carried.Id && delivery.ContributorId == traveler.Actor);
        Assert.All(project.Plan.Budget, cost =>
        {
            var receipts = project.Deliveries.Where(delivery => delivery.ItemKind == cost.ResourceId).ToArray();
            Assert.Equal(cost.Amount, receipts.Sum(delivery => delivery.Quantity));
            if (cost.ResourceId is "wood" or "stone") Assert.True(receipts.Length > 1);
            Assert.All(receipts, delivery =>
            {
                Assert.Contains(delivery.Id, ledger.PersonalDonations);
                var input = scenario.World.Society.Inventory.GetReservation(delivery.ReservationId!);
                Assert.Equal((townId, delivery.Quantity, InventoryReservationState.Completed),
                    (input.OwnerId, input.Quantity, input.State));
            });
            Assert.True(ledger.Outputs.GetValueOrDefault(cost.ResourceId) >= cost.Amount);
            Assert.Equal(MaterialQuantity(initial.Society.Society.Inventory, cost.ResourceId) + ledger.Outputs[cost.ResourceId] - cost.Amount,
                MaterialQuantity(completed.Society.Society.Inventory, cost.ResourceId));
        });
        foreach (var lot in initial.Society.Society.Inventory.Lots.Where(lot => lot.ItemKind is "wood" or "stone" or "fiber"))
            Assert.Equal((lot.OwnerId, lot.Quantity),
                (scenario.World.Society.Inventory.GetLot(lot.Id).OwnerId, scenario.World.Society.Inventory.GetLot(lot.Id).Quantity));
        Assert.Equal(JsonSerializer.Serialize(initial.TownLandTitles), JsonSerializer.Serialize(completed.TownLandTitles));
        Assert.Equal(JsonSerializer.Serialize(initial.HouseholdLandUseRights), JsonSerializer.Serialize(completed.HouseholdLandUseRights));
        Assert.Equal(scenario.InitialLayerDigest, MapLayerManifestCodec.Digest(completed.Map));
        AssertByteExactReload(scenario);
        MarketObservationTests.AssertProjection(scenario.World);
        scenario.World.Validate();

        await MarketAdditionalStallScenario.AssertRealBorrowingEnablesOnlyAnApprovedPaidThirdStallAsync(
            scenario.World.ExportState());

        // Separate controlled stock fixtures reuse this genuinely paid boundary;
        // their axes/payment additions do not claim a natural crafting pipeline.
        await MarketFreshConsentScenario.AssertFreshArrivalConsentAsync(scenario.World);
        await MarketPausedPlanTradeScenario.AssertPausedBandageBuyerTradesWithoutBorrowingAlphaStockAsync(
            scenario.World.ExportState());
        await MarketTradeScenario.AssertPipelineAsync(scenario.World);
    }

    private static int MaterialQuantity(InventoryCheckpoint inventory, string kind) =>
        inventory.Lots.Where(lot => lot.ItemKind == kind).Sum(lot => lot.Quantity);

    private static void AssertByteExactReload(TownProjectScenario scenario)
    {
        var encoded = PrivateWorldRuntimeCodec.Encode(scenario.World.ExportState());
        scenario.Reload();
        Assert.Equal(encoded, PrivateWorldRuntimeCodec.Encode(scenario.World.ExportState()));
    }

    private static async Task UntilAsync(TownProjectScenario scenario, Func<bool> reached, int maximumTicks, GatheringLedger ledger)
    {
        for (var tick = 0; tick < maximumTicks && !reached(); tick++)
        {
            var before = scenario.World.WorldTick;
            await scenario.UntilAsync(() => scenario.World.WorldTick > before, 1);
            ledger.Observe(scenario.World.ExportState());
        }
        await scenario.UntilAsync(reached, 0);
    }

    private sealed class GatheringLedger(PrivateWorldRuntimeState initial)
    {
        private PrivateWorldRuntimeState previous = initial;
        private readonly HashSet<long> seen = initial.Events.Select(item => item.EventId).ToHashSet();
        internal Dictionary<string, int> Outputs { get; } = new(StringComparer.Ordinal);
        internal HashSet<string> PersonalDonations { get; } = new(StringComparer.Ordinal);

        internal void Observe(PrivateWorldRuntimeState current)
        {
            foreach (var delivery in current.Towns!.SelectMany(town => town.Projects)
                         .SelectMany(project => project.Deliveries).Where(delivery => PersonalDonations.Add(delivery.Id)))
            {
                // Observe custody before the transfer. Fully donated lots are later
                // consumed by construction and no longer exist in the completed inventory.
                var source = Assert.Single(previous.Society.Society.Inventory.Lots,
                    lot => lot.Id == delivery.SourceLotId);
                Assert.Equal((delivery.ContributorId, delivery.ItemKind), (source.OwnerId, source.ItemKind));
                Assert.True(PersonalEquipmentRules.IsCarried(source, delivery.ContributorId));
                Assert.InRange(delivery.Quantity, 1, source.Quantity);
            }
            foreach (var item in current.Events.Where(item => seen.Add(item.EventId) && item.Kind == "material_gathered"))
            {
                var parts = item.Detail.Split(':');
                var kind = parts[^2];
                if (kind is not ("wood" or "stone" or "fiber")) continue;
                var actor = string.Join(':', parts[..^2]);
                var quantity = int.Parse(parts[^1], CultureInfo.InvariantCulture);
                var position = current.Inhabitants.Single(person => person.InhabitantId == actor).Position;
                Assert.Contains(previous.Map.Resources, source =>
                    (source.Kind == kind || kind == "wood" && source.Kind == "construction") &&
                    current.Map.FootDistance(position, source.Position) <= 1 &&
                    previous.WorldSystems!.Ecology.GetResource(source.Id).Quantity > current.WorldSystems!.Ecology.GetResource(source.Id).Quantity);
                Assert.True(quantity > 0);
                Outputs[kind] = Outputs.GetValueOrDefault(kind) + quantity;
            }
            previous = current;
        }
    }
}

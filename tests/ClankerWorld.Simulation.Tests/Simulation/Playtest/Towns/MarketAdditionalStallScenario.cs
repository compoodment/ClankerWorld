using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

/// <summary>Continues from a genuinely paid Market; edge branches reuse its real construction history.</summary>
internal static class MarketAdditionalStallScenario
{
    private const string Proposer = TownProjectScenario.Author;
    private const string WoodWorker = "founder:00000000000000000000000000000002";
    private static readonly int[] ExpectedStarterSlots = [0, 4];
    private static readonly string[] Sellers =
    ["founder:00000000000000000000000000000003", "founder:00000000000000000000000000000004"];

    internal static async Task AssertRealBorrowingEnablesOnlyAnApprovedPaidThirdStallAsync(PrivateWorldRuntimeState paidBoundary)
    {
        var paidMarket = Assert.Single(paidBoundary.Towns![0].Markets);
        Assert.Equal(ExpectedStarterSlots, paidMarket.Stalls.Select(item => item.SlotIndex).Order());
        Assert.Equal("completed", Assert.Single(paidBoundary.Towns[0].Projects).Stage);
        // Controlled initial sale goods only, never construction inputs. Retain
        // the genuine map, titles, approvals, placed buildings and actor positions.
        var inventory = paidBoundary.Society.Society.Inventory;
        foreach (var seller in Sellers)
            inventory = InventoryFixture.AddLot(inventory, "extra-stall-sale-cloth:" + seller, "cloth", seller, 1);
        var actors = Sellers.Append(Proposer).Append(WoodWorker).ToHashSet(StringComparer.Ordinal);
        foreach (var actor in actors)
        {
            inventory = InventoryFixture.AddLot(inventory, "extra-stall-cloak:" + actor, "rain_cloak", actor, 1);
            inventory = InventoryFixture.AddLot(inventory, "extra-stall-sack:" + actor, "sack", actor, 1);
        }
        // This continuation isolates expansion authority and physical materials
        // from prior weather and idle decisions. Equipment and fullness are initial
        // controlled fixtures, never sources of the 4wood/2fiber building budget.
        var initial = paidBoundary with
        {
            Society = paidBoundary.Society with { Society = paidBoundary.Society.Society with { Inventory = inventory } },
            Inhabitants = paidBoundary.Inhabitants.Select(person => actors.Contains(person.InhabitantId)
                ? person with
                {
                    HungerBasisPoints = 9_500,
                    Survival = new SurvivalCondition(),
                    LastDecisionContext = null,
                    Equipment = (person.Equipment ?? new PersonalEquipment()) with
                    {
                        ClothingLotId = "extra-stall-cloak:" + person.InhabitantId,
                        CarryAidLotId = "extra-stall-sack:" + person.InhabitantId,
                    },
                }
                : person).ToArray(),
        };
        var policy = new ExpansionPolicy(initial);
        using var phases = new PhaseWorld(initial, policy);
        var world = phases.World;
        world.Validate();
        var originalBuildings = world.WorldSimulation.Buildings.Select(item => item.InstanceId).ToHashSet(StringComparer.Ordinal);

        await UntilAsync(world, policy, () => ActiveBorrowers(world) == 1, 100);
        var partialTick = world.WorldTick;
        await StepsAsync(world, policy, 8);
        Assert.DoesNotContain(policy.Observations.Where(item => item.Tick >= partialTick),
            item => item.Ids.Any(id => id.StartsWith(policy.ProposalPrefix, StringComparison.Ordinal)));
        Assert.Single(world.Towns[0].Projects);
        Assert.Equal(2, world.Towns[0].Markets[0].Stalls.Count);

        world = phases.Resume("two");
        await UntilAsync(world, policy, () => ActiveBorrowers(world) == 2, 100);
        Assert.All(world.Towns[0].Markets[0].Stalls, stall => Assert.Contains(world.Towns[0].Markets[0].Occupancies,
            occupancy => occupancy.StallBuildingId == stall.BuildingId && occupancy.EndedTick is null));
        world = phases.Resume("proposal");
        await UntilAsync(world, policy, () => world.Towns[0].Governance!.Proposals.Any(item =>
            item.Project?.DefinitionId == MarketContent.Stall1x1().CanonicalId), 32);
        var pending = Assert.Single(world.Towns[0].Governance!.Proposals,
            item => item.Project?.DefinitionId == MarketContent.Stall1x1().CanonicalId);
        Assert.Equal(("pending", Proposer, "Third stall"), (pending.Status, pending.AuthorId, pending.Project!.Name));
        Assert.Equal(new[] { ("fiber", 2), ("wood", 4) }, pending.Project.Budget.Select(cost => (cost.ResourceId, cost.Amount)));
        Assert.Equal((policy.StallSite, MarketContent.StallEntrance(paidMarket.Site, policy.Slot)),
            (pending.Project.Site, pending.Project.Entrance));
        Assert.Contains(policy.Choices, choice => choice.Actor == Proposer && choice.Id == policy.ProposalId);
        Assert.Single(world.Towns[0].Projects);
        Assert.Equal(originalBuildings.Order(StringComparer.Ordinal), world.WorldSimulation.Buildings.Select(item => item.InstanceId).Order(StringComparer.Ordinal));
        AssertStrictRoundTrip(world);

        world = phases.Resume("approve");
        await UntilAsync(world, policy, () => world.Towns[0].Projects.Count == 2, 100);
        var approval = world.Towns[0].Governance!.Proposals.Single(item => item.Id == pending.Id);
        Assert.Equal("passed", approval.Status);
        Assert.Equal(3, approval.RequiredYes);
        Assert.Equal(4, approval.Voters.Count);
        Assert.True(approval.Votes.Count(vote => vote.Yes) >= approval.RequiredYes);
        Assert.Equal(pending.CouncilRevision, approval.CouncilRevision);
        var notice = Assert.Single(world.Towns[0].Governance!.Notices, item => item.Kind == "proposal" && item.SubjectId == approval.Id);
        Assert.All(approval.Votes, vote =>
        {
            Assert.Contains(policy.Choices, choice => choice.Actor == vote.AgentId && choice.Id == $"civic|{world.Towns[0].Id}|yes|{approval.Id}|");
            Assert.Contains(world.Towns[0].Governance!.Knowledge, receipt => receipt.AgentId == vote.AgentId && receipt.NoticeId == notice.Id);
        });
        var projectId = Assert.Single(world.Towns[0].Projects, item => item.ProposalId == approval.Id).Id;
        Assert.Empty(Project(world, projectId).Deliveries);
        Assert.Equal(2, world.Towns[0].Markets[0].Stalls.Count);
        AssertStrictRoundTrip(world);

        // Occupants may leave the plaza to read the actual civic board. Occupancy
        // is the need gate at proposal time, not a permanent construction claim.
        policy.ProjectId = projectId;
        world = phases.Resume("supply");
        await UntilAsync(world, policy, () => Project(world, projectId).Stage == "working", 180);
        var working = Project(world, projectId);
        Assert.Equal(0, working.WorkDone); // Provider holds work until this checkpoint.
        foreach (var cost in working.Plan.Budget)
            Assert.Equal(cost.Amount, TownProjectRules.DeliveredQuantity(working, world.Towns[0].Id, world.Society.Inventory, cost.ResourceId));
        Assert.All(working.Deliveries, delivery =>
        {
            Assert.NotNull(delivery.DeliveredTick);
            Assert.Null(delivery.ReleasedTick);
            Assert.Contains(delivery.Id, policy.PersonalDonations);
            var lot = world.Society.Inventory.GetLot(delivery.LotId);
            Assert.Equal(world.Towns[0].Id, lot.OwnerId);
            Assert.Equal(new InventoryGroundPosition(working.Plan.Site.X, working.Plan.Site.Y), lot.GroundPosition);
            Assert.Equal(InventoryReservationState.Reserved, world.Society.Inventory.GetReservation(delivery.ReservationId!).State);
        });
        AssertStrictRoundTrip(world);
        var beforeWork = world.ExportState();
        world = phases.Resume("work");
        await UntilAsync(world, policy, () => Project(world, projectId).Stage == "completed", 30);
        var completed = Project(world, projectId);
        Assert.Equal(3, completed.WorkDone);
        Assert.Equal(3, world.Towns[0].Markets[0].Stalls.Count);
        Assert.Equal(new[] { 0, policy.Slot, 4 }.Order(), world.Towns[0].Markets[0].Stalls.Select(item => item.SlotIndex).Order());
        var extra = Assert.Single(world.Towns[0].Markets[0].Stalls, item => item.ProjectId == projectId);
        var placed = Assert.Single(world.WorldSimulation.Buildings, item => !originalBuildings.Contains(item.InstanceId));
        Assert.Equal((extra.BuildingId, completed.CompletedBuildingId, completed.Plan.Site, world.Towns[0].Id, (string?)null),
            (placed.InstanceId, placed.InstanceId, placed.Position, placed.TownId, placed.HouseholdId));
        Assert.Equal(MarketContent.Stall1x1().CanonicalId, placed.DefinitionId);
        Assert.DoesNotContain(placed.Position, world.RoadTiles);
        Assert.Equal(1, world.ExportState().Events.Count(item => item.Kind == "market_stall_built"));
        foreach (var cost in completed.Plan.Budget)
        {
            Assert.Equal(cost.Amount, completed.Deliveries.Where(item => item.ItemKind == cost.ResourceId).Sum(item => item.Quantity));
            Assert.Equal(Total(beforeWork, cost.ResourceId) - cost.Amount, Total(world.ExportState(), cost.ResourceId));
        }
        Assert.All(completed.Deliveries, delivery => Assert.Equal(InventoryReservationState.Completed,
            world.Society.Inventory.GetReservation(delivery.ReservationId!).State));
        AssertStrictRoundTrip(world);
        MarketObservationTests.AssertProjection(world);
        AssertMalformedPaidStallLedgersRefused(world.ExportState(), extra.BuildingId);
        AssertRemovingAnExtraStallRetainsItsPaidHistory(world.ExportState(), extra.BuildingId);
    }

    private static void AssertRemovingAnExtraStallRetainsItsPaidHistory(PrivateWorldRuntimeState completed, string extraId)
    {
        using var removed = PrivateWorldRuntime.Restore(completed);
        var before = PrivateWorldRuntimeCodec.Encode(removed.ExportState());
        Assert.False(removed.RemoveBuilding(extraId, "other-town", null).Applied);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(removed.ExportState()));
        Assert.True(removed.RemoveBuilding(extraId, completed.Towns![0].Id, null, completed.Society.Society.WorldId).Applied);
        var market = removed.Towns[0].Markets[0];
        var extra = Assert.Single(market.Stalls, item => item.BuildingId == extraId);
        Assert.Equal(3, market.Stalls.Count); // History is retained; not three live drawings.
        Assert.Equal(removed.WorldTick, extra.RemovedTick);
        Assert.Equal(extra.RemovedTick, Project(removed, extra.ProjectId).RemovedTick);
        Assert.DoesNotContain(removed.WorldSimulation.Buildings, item => item.InstanceId == extraId);
        foreach (var kind in new[] { "wood", "fiber" }) Assert.Equal(Total(completed, kind), Total(removed.ExportState(), kind));
        AssertStrictRoundTrip(removed);
        MarketObservationTests.AssertProjection(removed);
        AssertMalformedPaidStallLedgersRefused(removed.ExportState(), extraId);
    }

    private static void AssertMalformedPaidStallLedgersRefused(PrivateWorldRuntimeState healthyState, string extraId)
    {
        var healthy = PrivateWorldRuntimeCodec.Encode(healthyState);
        foreach (var damage in new[] { "missing-starter", "duplicate-slot", "free-extra", "budget", "future-built", "wrong-slot", "removal" })
        {
            var document = JsonNode.Parse(healthy)!;
            var town = document["state"]!["towns"]![0]!;
            var market = town["markets"]![0]!;
            var stalls = market["stalls"]!.AsArray();
            var extra = stalls.Single(item => item!["buildingId"]!.GetValue<string>() == extraId)!;
            var projectId = extra["projectId"]!.GetValue<string>();
            var project = town["projects"]!.AsArray().Single(item => item!["id"]!.GetValue<string>() == projectId)!;
            switch (damage)
            {
                case "missing-starter": stalls.Remove(stalls.Single(item => item!["slotIndex"]!.GetValue<int>() == 0)); break;
                case "duplicate-slot": extra["slotIndex"] = 0; break;
                case "free-extra": extra["projectId"] = market["projectId"]!.GetValue<string>(); break;
                case "budget": project["plan"]!["budget"]![0]!["amount"] = 1; break;
                case "future-built": extra["builtTick"] = healthyState.Society.Society.WorldTick + 1; break;
                case "wrong-slot": extra["slotIndex"] = 7; break;
                case "removal":
                    if (extra["removedTick"] is null) extra["removedTick"] = healthyState.Society.Society.WorldTick;
                    else extra.AsObject().Remove("removedTick");
                    break;
            }
            Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Decode(Encoding.UTF8.GetBytes(document.ToJsonString())));
            Assert.Equal(healthy, PrivateWorldRuntimeCodec.Encode(healthyState));
        }
    }

    private static TownConstructionProject Project(PrivateWorldRuntime world, string id) => world.Towns[0].Projects.Single(item => item.Id == id);
    private static int ActiveBorrowers(PrivateWorldRuntime world) => world.Towns[0].Markets[0].Occupancies.Count(item => item.EndedTick is null);
    private static int Total(PrivateWorldRuntimeState state, string kind) => state.Society.Society.Inventory.Lots.Where(item => item.ItemKind == kind).Sum(item => item.Quantity);
    private static void AssertStrictRoundTrip(PrivateWorldRuntime world)
    {
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }
    private static async Task StepsAsync(PrivateWorldRuntime world, ExpansionPolicy policy, int ticks) => await UntilAsync(world, policy, () => false, ticks, requireReached: false);
    private static async Task UntilAsync(PrivateWorldRuntime world, ExpansionPolicy policy, Func<bool> reached, int ticks, bool requireReached = true)
    {
        var movementMap = world.ExportState().Map;
        for (var tick = 0; tick < ticks && !reached(); tick++)
        {
            var previousInventory = world.Society.Inventory;
            var positions = world.Inhabitants.ToDictionary(person => person.InhabitantId, person => person.Position, StringComparer.Ordinal);
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            foreach (var person in world.Inhabitants)
            {
                var old = positions[person.InhabitantId];
                if (old != person.Position) Assert.True(movementMap.CanFootStep(old, person.Position));
                Assert.InRange(PersonalEquipmentRules.CarriedQuantity(world.Society.Inventory, person.InhabitantId, person.Equipment),
                    0, PersonalEquipmentRules.Capacity(world.Society.Inventory, person.InhabitantId, person.Equipment));
            }
            foreach (var delivery in world.Towns[0].Projects.SelectMany(item => item.Deliveries)
                         .Where(item => policy.PersonalDonations.Add(item.Id)))
            {
                var source = Assert.Single(previousInventory.Lots, item => item.Id == delivery.SourceLotId);
                Assert.Equal(delivery.ContributorId, source.OwnerId);
                Assert.True(PersonalEquipmentRules.IsCarried(source, delivery.ContributorId));
            }
        }
        if (requireReached)
            Assert.True(reached(), $"Additional-stall phase {policy.Phase} not reached by tick {world.WorldTick}: " +
                JsonSerializer.Serialize(policy.Choices.TakeLast(12)) + "; projects: " +
                JsonSerializer.Serialize(world.Towns[0].Projects.Select(project => new
                {
                    project.Id,
                    project.Stage,
                    project.WorkDone,
                    project.Blocker,
                    project.Deliveries,
                })));
    }

    private sealed class PhaseWorld(PrivateWorldRuntimeState initial, ExpansionPolicy policy) : IDisposable
    {
        internal PrivateWorldRuntime World { get; private set; } = PrivateWorldRuntime.Restore(initial, policy.CreateProvider);

        internal PrivateWorldRuntime Resume(string phase)
        {
            var retained = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(World.ExportState()));
            World.Dispose();
            policy.Phase = phase;
            // A changed test policy is not a production event. At completed phase
            // boundaries only, request a fresh personal turn while preserving real
            // position, stock, occupancy, votes and the paid ledger. The first
            // borrow/travel sequence remains uninterrupted.
            World = PrivateWorldRuntime.Restore(retained with
            {
                Inhabitants = retained.Inhabitants.Select(person => person with { LastDecisionContext = null }).ToArray(),
            }, policy.CreateProvider);
            return World;
        }

        public void Dispose() => World.Dispose();
    }

    private sealed class ExpansionPolicy
    {
        private readonly TownMarketState market;
        private readonly string townId;

        internal ExpansionPolicy(PrivateWorldRuntimeState paidBoundary)
        {
            var town = Assert.Single(paidBoundary.Towns!);
            townId = town.Id;
            market = Assert.Single(town.Markets);
            PersonalDonations = town.Projects.SelectMany(item => item.Deliveries).Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        }

        internal string Phase { get; set; } = "one";
        internal string? ProjectId { get; set; }
        internal int Slot { get; } = 1;
        internal GridPoint StallSite => MarketContent.StallSite(market.Site, Slot);
        internal string ProposalPrefix => $"civic|{townId}|project|market-stall-1x1|";
        internal string ProposalId => ProposalPrefix + $"{StallSite.X},{StallSite.Y}";
        internal HashSet<string> PersonalDonations { get; }
        internal ConcurrentQueue<(string Actor, string Id, long Tick)> Choices { get; } = new();
        internal ConcurrentQueue<(string Actor, long Tick, string[] Ids)> Observations { get; } = new();
        internal IDecisionProvider CreateProvider(string actor) => new Provider(this, actor);
        private sealed class Provider(ExpansionPolicy policy, string actor) : IDecisionProvider
        {
            public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
            public long ProviderEpoch => 1;
            public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
            {
                var candidates = request.Observation.Candidates;
                policy.Observations.Enqueue((actor, request.Observation.WorldTick, candidates.Select(item => item.Id).ToArray()));
                CognitionCandidate? selected = candidates.Where(item => item.DeterministicPriority <= 5 &&
                    item.Id is "consume_food" or "collect_shared_food" or "take_food_from_pot" or "make_room_for_food" or
                        "harvest_food" or "seek_food" or "wear_clothing" or "seek_warmth").OrderBy(item => item.DeterministicPriority).FirstOrDefault();
                if (selected is null && (policy.Phase is "one" or "two") && (actor == Sellers[0] || policy.Phase == "two" && actor == Sellers[1]))
                {
                    var target = policy.market.Stalls.Single(item => item.SlotIndex == (actor == Sellers[0] ? 0 : 4)).BuildingId;
                    selected = candidates.FirstOrDefault(item => item.DestinationId == target && item.Id.StartsWith("market_borrow:", StringComparison.Ordinal));
                }
                if (selected is null && policy.Phase == "proposal" && actor == Proposer)
                    selected = candidates.FirstOrDefault(item => item.Id == policy.ProposalId);
                if (selected is null && (policy.Phase is "approve" or "supply" or "work"))
                    selected = candidates.FirstOrDefault(item => item.Id.Contains("|yes|", StringComparison.Ordinal)) ??
                        candidates.FirstOrDefault(item => item.Id.Contains("|read|", StringComparison.Ordinal));
                if (selected is null && (policy.Phase is "supply" or "work"))
                {
                    var material = actor == WoodWorker ? "wood" : actor == Proposer ? "fiber" : null;
                    var scoped = candidates.Where(item => item.DestinationId == policy.ProjectId).ToArray();
                    selected = scoped.FirstOrDefault(item => item.Id.StartsWith("town_project_deliver:", StringComparison.Ordinal)) ??
                        (material is null ? null : scoped.FirstOrDefault(item => item.Id.StartsWith("town_project_donate:", StringComparison.Ordinal) &&
                            item.Description.Contains(" " + material + " ", StringComparison.Ordinal))) ??
                        (material is null ? null : scoped.FirstOrDefault(item => item.Id.StartsWith("town_project_gather:", StringComparison.Ordinal) &&
                            item.Description.StartsWith("Gather personal " + material + " ", StringComparison.Ordinal))) ??
                        (policy.Phase == "work" ? scoped.FirstOrDefault(item => item.Id.StartsWith("town_project_work:", StringComparison.Ordinal)) : null);
                }
                if (selected is null && (policy.Phase is "approve" or "supply" or "work"))
                    selected = candidates.FirstOrDefault(item => item.Id.Contains("|visit|", StringComparison.Ordinal));
                selected ??= candidates.Single(item => item.Id == "safe_idle");
                policy.Choices.Enqueue((actor, selected.Id, request.Observation.WorldTick));
                return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, actor, Kind, ProviderEpoch,
                    request.Observation.RunEpoch, request.Observation.DecisionGeneration, request.Observation.ObservationDigest,
                    selected.Id, 1, candidates.ToDictionary(item => item.Id, item => item.Id == selected.Id ? 1d : 0d, StringComparer.Ordinal),
                    CivicProposal: selected.Id == policy.ProposalId ? "Third stall" : null));
            }
        }
    }
}

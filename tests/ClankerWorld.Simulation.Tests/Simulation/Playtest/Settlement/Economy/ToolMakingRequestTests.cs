using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ClankerWorld.Simulation.Tests;

public sealed class ToolMakingRequestTests
{
    [Fact]
    public async Task RequestedToolUsesSmithInputsAndTheActualFinishedOutputIsBoughtAcrossReplay()
    {
        var (state, buyer, seller, shop) = Prepared();
        var woodBefore = state.Society.Society.Inventory.Lots.Where(lot => lot.ItemKind == "wood").Sum(lot => lot.Quantity);
        var buyerChoices = new RequestChoices("tool_request_place:");
        using var requesting = Restore(state, buyer, seller, buyerChoices, new RequestChoices());
        await Until(requesting, world => world.ToolMakingRequests.Count == 1, 96);
        var request = Assert.Single(requesting.ToolMakingRequests);
        Assert.Equal(ToolMakingRequestStatus.Requested, request.Status);
        Assert.Equal(shop.InstanceId, request.BuildingInstanceId);
        Assert.Empty(requesting.Society.Inventory.Offers);
        Assert.Equal(3, requesting.Society.Inventory.GetLot("request-payment").Quantity);
        Assert.Null(request.JobId);
        state = Roundtrip(requesting);
        using var working = Restore(state, buyer, seller, new RequestChoices(), new RequestChoices("tool_request_accept:", "tool_request_work:"));
        using var replay = Restore(state, buyer, seller, new RequestChoices(), new RequestChoices("tool_request_accept:", "tool_request_work:"));
        for (var step = 0; step < 160 && Assert.Single(working.ToolMakingRequests).Status != ToolMakingRequestStatus.Ready; step++)
        {
            Assert.True((await working.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(working.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        }
        request = Assert.Single(working.ToolMakingRequests);
        Assert.Equal(ToolMakingRequestStatus.Ready, request.Status);
        Assert.Equal(seller, request.WorkerId);
        var job = Assert.Single(working.WorldSimulation.ProductionJobs, job => job.JobId == request.JobId);
        Assert.Equal(WorldProductionJobState.Completed, job.State);
        Assert.Equal(shop.InstanceId, job.BuildingInstanceId);
        Assert.Equal(request.RecipeId, job.RecipeId);
        Assert.True(job.StartedTick > request.AcceptedTick.GetValueOrDefault());
        Assert.Contains(working.ExportState().Events, item => item.Kind == "inhabitant_moved" && item.Detail.StartsWith(seller + ":", StringComparison.Ordinal));
        var receipt = Assert.Single(working.Society.Inventory.Reservations, receipt => job.InputReservationIds.Contains(receipt.Id));
        Assert.Equal("smith-input", receipt.LotId);
        Assert.Equal(shop.HouseholdId, receipt.OwnerId);
        Assert.Equal(3, receipt.Quantity);
        Assert.Equal(InventoryReservationState.Completed, receipt.State);
        var outputId = job.JobId + ":output:00";
        var output = working.Society.Inventory.GetLot(outputId);
        Assert.Equal("wooden_axe", output.ItemKind);
        Assert.Equal(shop.HouseholdId, output.OwnerId);
        Assert.Equal(shop.InstanceId, output.StorageBuildingId);
        Assert.Equal(1, output.Quantity);
        Assert.Empty(working.Society.Inventory.Offers);
        Assert.Equal(3, working.Society.Inventory.GetLot("request-payment").Quantity);
        Assert.Equal(4, working.Society.Inventory.GetLot("existing-private-tools").Quantity);
        state = Roundtrip(working);
        using var quoting = Restore(state, buyer, seller, new RequestChoices("tool_request_collect:"), new RequestChoices());
        await Until(quoting, world => Assert.Single(world.ToolMakingRequests).Status == ToolMakingRequestStatus.Offered, 96);
        request = Assert.Single(quoting.ToolMakingRequests);
        var offer = quoting.Society.Inventory.GetOffer(request.OfferId!);
        Assert.Equal(outputId, offer.FirstLotId);
        Assert.Equal("request-payment", offer.SecondLotId);
        Assert.Equal(1, offer.FirstQuantity);
        Assert.Equal(3, offer.SecondQuantity);
        Assert.Equal(DirectBarterState.Open, offer.State);
        Assert.All(quoting.Society.Inventory.Reservations.Where(receipt => receipt.Purpose == "barter:" + offer.Id),
            receipt => Assert.Equal(InventoryReservationState.Reserved, receipt.State));
        state = Roundtrip(quoting);
        using var collecting = Restore(state, buyer, seller, new RequestChoices("business_continue:"), new RequestChoices("business_continue:"));
        using var collectingReplay = Restore(state, buyer, seller, new RequestChoices("business_continue:"), new RequestChoices("business_continue:"));
        for (var step = 0; step < 96 && Assert.Single(collecting.ToolMakingRequests).Status != ToolMakingRequestStatus.Fulfilled; step++)
        {
            Assert.True((await collecting.AdvanceOneTickAsync()).Advanced);
            Assert.True((await collectingReplay.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(collecting.ExportState()), PrivateWorldRuntimeCodec.Encode(collectingReplay.ExportState()));
        }
        Assert.Equal(ToolMakingRequestStatus.Fulfilled, Assert.Single(collecting.ToolMakingRequests).Status);
        Assert.Equal(DirectBarterState.Settled, collecting.Society.Inventory.GetOffer(offer.Id).State);
        var bought = collecting.Society.Inventory.GetLot(outputId);
        Assert.Equal(buyer, bought.OwnerId);
        Assert.True(PersonalEquipmentRules.IsCarried(bought, buyer));
        Assert.Equal(1, bought.Quantity);
        var payment = collecting.Society.Inventory.GetLot("request-payment");
        Assert.Equal(shop.HouseholdId, payment.OwnerId);
        Assert.Equal(shop.InstanceId, payment.StorageBuildingId);
        Assert.Equal(3, payment.Quantity);
        Assert.Equal(woodBefore - 3, collecting.Society.Inventory.Lots.Where(lot => lot.ItemKind == "wood").Sum(lot => lot.Quantity));
        Assert.Equal(state.Society.Society.GetInhabitant(buyer).HouseholdId, collecting.Society.GetInhabitant(buyer).HouseholdId);
        Assert.False(collecting.StartProduction(request.RecipeId, shop.InstanceId, buyer).Applied);
        _ = Roundtrip(collecting);
    }

    [Theory]
    [InlineData(DecisionProviderKind.Deterministic)]
    [InlineData(DecisionProviderKind.Jev)]
    public async Task RoutineProvidersCannotAcceptARequestOrTakeMaterials(DecisionProviderKind kind)
    {
        var (state, buyer, seller, shop) = Prepared();
        using var placing = Restore(state, buyer, seller, new RequestChoices("tool_request_place:"), new RequestChoices());
        await Until(placing, world => world.ToolMakingRequests.Count == 1, 96);
        state = Roundtrip(placing);
        var sellerChoices = new RequestChoices(kind, "tool_request_accept:");
        using var refusing = Restore(state, buyer, seller, new RequestChoices(), sellerChoices);
        for (var step = 0; step < 64; step++) Assert.True((await refusing.AdvanceOneTickAsync()).Advanced);
        Assert.Contains(sellerChoices.Offered, candidate => candidate.Id.StartsWith("tool_request_accept:", StringComparison.Ordinal));
        Assert.Equal(ToolMakingRequestStatus.Requested, Assert.Single(refusing.ToolMakingRequests).Status);
        Assert.Equal(3, refusing.Society.Inventory.GetLot("smith-input").Quantity);
        Assert.Equal(shop.HouseholdId, refusing.Society.Inventory.GetLot("smith-input").OwnerId);
        Assert.Empty(refusing.Society.Inventory.Offers);
        Assert.DoesNotContain(refusing.WorldSimulation.ProductionJobs, job => job.RecipeId == Assert.Single(refusing.ToolMakingRequests).RecipeId);
        _ = Roundtrip(refusing);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RefusalOrCustomerWithdrawalBeforeWorkChangesNoStock(bool customerWithdraws)
    {
        var (state, buyer, seller, _) = Prepared();
        using var placing = Restore(state, buyer, seller, new RequestChoices("tool_request_place:"), new RequestChoices());
        await Until(placing, world => world.ToolMakingRequests.Count == 1, 96);
        state = Roundtrip(placing);
        using var closing = Restore(state, buyer, seller,
            customerWithdraws ? new RequestChoices("tool_request_withdraw:") : new RequestChoices(),
            customerWithdraws ? new RequestChoices() : new RequestChoices("tool_request_refuse:"));
        await Until(closing, world => ToolMakingRequestRules.IsTerminal(Assert.Single(world.ToolMakingRequests).Status), 301);
        Assert.Equal(customerWithdraws ? ToolMakingRequestStatus.Withdrawn : ToolMakingRequestStatus.Refused,
            Assert.Single(closing.ToolMakingRequests).Status);
        Assert.Equal(state.Society.Society.Inventory.Lots.Select(lot => (lot.Id, lot.OwnerId, lot.ItemKind, lot.Quantity, lot.StorageBuildingId)),
            closing.Society.Inventory.Lots.Select(lot => (lot.Id, lot.OwnerId, lot.ItemKind, lot.Quantity, lot.StorageBuildingId)));
        Assert.Empty(closing.Society.Inventory.Offers);
        Assert.DoesNotContain(closing.Society.Inventory.Reservations, receipt => receipt.LotId == "smith-input");
        Assert.Null(Assert.Single(closing.ToolMakingRequests).JobId);
        _ = Roundtrip(closing);
    }

    [Fact]
    public async Task SavesRejectFutureGoodsDuplicateRequestsAndAnotherWorkPlanBinding()
    {
        var (state, buyer, seller, _) = Prepared();
        using var placing = Restore(state, buyer, seller, new RequestChoices("tool_request_place:"), new RequestChoices());
        await Until(placing, world => world.ToolMakingRequests.Count == 1, 96);
        state = Roundtrip(placing);
        var request = Assert.Single(state.ToolMakingRequests!);
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(state with { ToolMakingRequests = [request, request] }));
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(state with
        {
            ToolMakingRequests = [request with { Status = ToolMakingRequestStatus.Ready }],
        }));
        using var accepting = Restore(state, buyer, seller, new RequestChoices(), new RequestChoices("tool_request_accept:"));
        await Until(accepting, world => Assert.Single(world.ToolMakingRequests).Status == ToolMakingRequestStatus.Accepted, 96);
        state = Roundtrip(accepting);
        request = Assert.Single(state.ToolMakingRequests!);
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(state with
        {
            ToolMakingRequests = [request with { WorkerId = buyer }],
        }));
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(state with
        {
            ToolMakingRequests = [request with { AcceptedTick = state.Society.Society.WorldTick + 1 }],
        }));
    }

    [Fact]
    public async Task SavesBoundTerminalHistoryIndependentlyOfActiveDemand()
    {
        var (state, buyer, seller, _) = Prepared();
        using var placing = Restore(state, buyer, seller, new RequestChoices("tool_request_place:"), new RequestChoices());
        await Until(placing, world => world.ToolMakingRequests.Count == 1, 96);
        while (placing.WorldTick < 40) Assert.True((await placing.AdvanceOneTickAsync()).Advanced);
        state = Roundtrip(placing);
        var request = Assert.Single(state.ToolMakingRequests!);
        ToolMakingRequestState HistoryAt(int tick) => request with
        {
            Id = "tool-making-request:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
                new[] { request.RequesterId, request.BuildingInstanceId, request.RecipeId, tick.ToString(System.Globalization.CultureInfo.InvariantCulture) })))),
            RequestedTick = tick, LastTransitionTick = state.Society.Society.WorldTick,
            Status = ToolMakingRequestStatus.Refused,
        };
        var history = Enumerable.Range(0, 33).Select(HistoryAt).OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
        using var valid = PrivateWorldRuntime.Restore(state with { ToolMakingRequests = history.Take(32).ToArray() });
        Assert.Equal(32, valid.ToolMakingRequests.Count);
        _ = Roundtrip(valid);
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(state with { ToolMakingRequests = history }));
    }

    internal static async Task<(PrivateWorldRuntimeState State, string Buyer, string Seller, PlacedBuilding Shop)> BlockedAcceptedRequest()
    {
        var (state, buyer, seller, shop) = Prepared();
        var input = state.Society.Society.Inventory.GetLot("smith-input");
        var householdHouse = state.WorldSimulation!.Buildings.Single(building => building.HouseholdId == shop.HouseholdId &&
            state.WorldContent!.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId).Tags.Contains("house"));
        state = state with
        {
            Society = state.Society with { Society = state.Society.Society with { Inventory = state.Society.Society.Inventory with
            {
                Lots = state.Society.Society.Inventory.Lots.Select(lot => lot.Id == input.Id
                    ? lot with { StorageBuildingId = householdHouse.InstanceId } : lot).ToArray(),
            } } },
        };
        using var placing = Restore(state, buyer, seller, new RequestChoices("tool_request_place:"), new RequestChoices());
        await Until(placing, world => world.ToolMakingRequests.Count == 1, 96);
        state = Roundtrip(placing);
        using var accepting = Restore(state, buyer, seller, new RequestChoices(), new RequestChoices("tool_request_accept:"));
        await Until(accepting, world => Assert.Single(world.ToolMakingRequests) is
            { Status: ToolMakingRequestStatus.Accepted, Blocker: not null }, 96);
        var request = Assert.Single(accepting.ToolMakingRequests);
        Assert.Contains("wood", request.Blocker!, StringComparison.Ordinal);
        Assert.Null(request.JobId);
        Assert.Empty(accepting.Society.Inventory.Offers);
        Assert.Equal(3, accepting.Society.Inventory.GetLot(input.Id).Quantity);
        Assert.Equal(shop.HouseholdId, accepting.Society.Inventory.GetLot(input.Id).OwnerId);
        Assert.DoesNotContain(accepting.Society.Inventory.Reservations, receipt => receipt.LotId == input.Id);
        return (Roundtrip(accepting), buyer, seller, shop);
    }

    [Fact]
    public async Task AnUnstartedRequestProtectsItsEmptySmithUntilWithdrawnAcrossReload()
    {
        var (state, buyer, seller, shop) = Prepared();
        var house = state.WorldSimulation!.Buildings.Single(building => building.HouseholdId == shop.HouseholdId &&
            state.WorldContent!.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId).Tags.Contains("house"));
        state = state with
        {
            Society = state.Society with { Society = state.Society.Society with { Inventory = state.Society.Society.Inventory with
            {
                Lots = state.Society.Society.Inventory.Lots.Select(lot => lot.Id == "smith-input"
                    ? lot with { StorageBuildingId = house.InstanceId } : lot).ToArray(),
            } } },
        };
        using var placing = Restore(state, buyer, seller, new RequestChoices("tool_request_place:"), new RequestChoices());
        await Until(placing, world => world.ToolMakingRequests.Count == 1, 96);
        state = Roundtrip(placing);
        using var world = Restore(state, buyer, seller, new RequestChoices("tool_request_withdraw:"), new RequestChoices());
        Assert.Empty(world.Society.Inventory.Lots.Where(lot => lot.StorageBuildingId == shop.InstanceId || lot.DeliveryBuildingId == shop.InstanceId));
        Assert.Null(Assert.Single(world.ToolMakingRequests).JobId);
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        var removed = world.RemoveBuilding(shop.InstanceId, shop.TownId, shop.HouseholdId);
        Assert.False(removed.Applied);
        Assert.Contains("tool request", removed.Failure!, StringComparison.Ordinal);
        var reassigned = world.ReassignBuilding(shop.InstanceId, shop.TownId, shop.HouseholdId,
            targetTownId: null, targetHouseholdId: world.Society.GetInhabitant(buyer).HouseholdId);
        Assert.False(reassigned.Applied);
        Assert.Contains("tool request", reassigned.Failure!, StringComparison.Ordinal);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        _ = Roundtrip(world);
        await Until(world, active => Assert.Single(active.ToolMakingRequests).Status == ToolMakingRequestStatus.Withdrawn, 301);
        Assert.True(world.RemoveBuilding(shop.InstanceId, shop.TownId, shop.HouseholdId).Applied);
        Assert.Equal(ToolMakingRequestStatus.Withdrawn, Assert.Single(world.ToolMakingRequests).Status);
        Assert.Equal(3, world.Society.Inventory.GetLot("smith-input").Quantity);
        Assert.Equal(house.InstanceId, world.Society.Inventory.GetLot("smith-input").StorageBuildingId);
        _ = Roundtrip(world);
    }

    [Fact]
    public async Task TwoSmithAdultsAcceptAtMostOneWorkPlanAndDemandDoesNotStartAnExtraJob()
    {
        var (state, buyer, seller, shop) = Prepared();
        using var placing = Restore(state, buyer, seller, new RequestChoices("tool_request_place:"), new RequestChoices());
        await Until(placing, world => world.ToolMakingRequests.Count == 1, 96);
        state = Roundtrip(placing);
        var providers = state.Society.Society.GetHousehold(shop.HouseholdId!).MemberIds
            .ToDictionary(actor => actor, _ => new RequestChoices("tool_request_accept:", "tool_request_work:"), StringComparer.Ordinal);
        using var working = PrivateWorldRuntime.Restore(state, actor => providers.GetValueOrDefault(actor) ?? new RequestChoices());
        await Until(working, world => Assert.Single(world.ToolMakingRequests).Status == ToolMakingRequestStatus.Ready, 160);
        var request = Assert.Single(working.ToolMakingRequests);
        Assert.Contains(request.WorkerId!, providers.Keys);
        Assert.Single(working.ExportState().Events.Where(item => item.Kind == "tool_request_accepted"));
        var job = Assert.Single(working.WorldSimulation.ProductionJobs, item => item.RecipeId == request.RecipeId);
        Assert.Equal(request.JobId, job.JobId);
        Assert.Equal(request.Id, job.ToolMakingRequestId);
        Assert.Equal(request.WorkerId, job.WorkerId);
        Assert.All(providers.Where(pair => pair.Key != request.WorkerId), pair =>
            Assert.DoesNotContain(pair.Value.Offered, candidate => candidate.Id == "build:recipe:" + request.RecipeId));
        Assert.Equal(4, working.Society.Inventory.GetLot("existing-private-tools").Quantity);
        Assert.Equal(1, working.Society.Inventory.GetLot(job.JobId + ":output:00").Quantity);
        _ = Roundtrip(working);
    }

    private static async Task Until(PrivateWorldRuntime world, Func<PrivateWorldRuntime, bool> done, int limit)
    {
        for (var step = 0; step < limit && !done(world); step++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.True(done(world), string.Join("; ", world.ToolMakingRequests.Select(ToolMakingRequestRules.Note)));
    }

    private static PrivateWorldRuntimeState Roundtrip(PrivateWorldRuntime world)
    {
        world.Validate();
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        var state = PrivateWorldRuntimeCodec.Decode(bytes);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(state));
        return state;
    }

    private static (PrivateWorldRuntimeState State, string Buyer, string Seller, PlacedBuilding Shop) Prepared()
    {
        using var setup = NormalPathWorld.CreateGenerated("probe-a", _ => new RequestChoices());
        var state = setup.ExportState();
        var shop = state.WorldSimulation!.Buildings.Single(building => state.WorldContent!.Buildings.Single(definition =>
            definition.CanonicalId == building.DefinitionId).Tags.Contains("blacksmith"));
        var seller = state.Society.Society.GetHousehold(shop.HouseholdId!).MemberIds[0];
        var buyer = state.Society.Society.Inhabitants.First(person => person.HouseholdId != shop.HouseholdId).Id;
        var house = state.WorldSimulation.Buildings.Single(building => building.HouseholdId == shop.HouseholdId &&
            state.WorldContent!.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId).Tags.Contains("house"));
        var occupied = state.Inhabitants.Where(person => person.InhabitantId != buyer && person.InhabitantId != seller)
            .Select(person => person.Position).ToHashSet();
        var customerPosition = state.Map.Tiles.Select(tile => tile.Position).First(position => state.Map.IsPassable(position) &&
            state.Map.FootDistance(position, shop.Position) == 1 && !occupied.Contains(position));
        var sellerPosition = state.Map.Tiles.Select(tile => tile.Position).Where(position => state.Map.IsPassable(position) &&
                state.Map.FootDistance(position, shop.Position) is >= 3 and <= 5 && position != customerPosition && !occupied.Contains(position))
            .First(position => DeterministicRouteFinder.TryFind(state.Map, position, shop.Position, out _));
        var inventory = state.Society.Society.Inventory with
        {
            Lots = state.Society.Society.Inventory.Lots.Where(lot => lot.OwnerId != buyer &&
                lot.StorageBuildingId != shop.InstanceId && lot.OwnerId != shop.HouseholdId).ToArray(),
        };
        inventory = InventoryFixture.AddLot(inventory, "smith-input", "wood", shop.HouseholdId!, 3, storageBuildingId: shop.InstanceId);
        inventory = InventoryFixture.AddLot(inventory, "existing-private-tools", "wooden_axe", shop.HouseholdId!, 4, storageBuildingId: house.InstanceId);
        inventory = InventoryFixture.AddLot(inventory, "request-payment", "wood", buyer, 3);
        state = state with
        {
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
            Inhabitants = state.Inhabitants.Select(person => person with
            {
                Position = person.InhabitantId == buyer ? customerPosition : person.InhabitantId == seller ? sellerPosition : person.Position,
                HungerBasisPoints = 8_000, LastDecisionContext = null, Project = null,
                Equipment = person.InhabitantId == buyer ? null : person.Equipment,
            }).ToArray(),
        };
        return (state, buyer, seller, shop);
    }

    private static PrivateWorldRuntime Restore(PrivateWorldRuntimeState state, string buyer, string seller,
        RequestChoices buyerChoices, RequestChoices sellerChoices) => PrivateWorldRuntime.Restore(state,
        actor => actor == buyer ? buyerChoices : actor == seller ? sellerChoices : new RequestChoices());

    private sealed class RequestChoices : IDecisionProvider
    {
        private readonly string[] prefixes;
        public RequestChoices(params string[] prefixes) : this(DecisionProviderKind.LargeLanguageModel, prefixes) { }
        public RequestChoices(DecisionProviderKind kind, params string[] prefixes) { Kind = kind; this.prefixes = prefixes; }
        public DecisionProviderKind Kind { get; }
        public long ProviderEpoch => 0;
        public List<CognitionCandidate> Offered { get; } = [];
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            Offered.AddRange(request.Observation.Candidates);
            var choice = prefixes.Select(prefix => request.Observation.Candidates.FirstOrDefault(candidate =>
                candidate.Id.StartsWith(prefix, StringComparison.Ordinal) &&
                (!prefix.EndsWith("place:", StringComparison.Ordinal) || candidate.Description.Contains("wooden axe", StringComparison.Ordinal))))
                .FirstOrDefault(candidate => candidate is not null) ?? request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle");
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, request.Observation.InhabitantId, Kind,
                request.ProviderEpoch, request.Observation.RunEpoch, request.Observation.DecisionGeneration,
                request.Observation.ObservationDigest, choice.Id, 1, new Dictionary<string, double> { [choice.Id] = 1 }));
        }
    }
}

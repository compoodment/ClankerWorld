using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class GoodsCollectionTests
{
    [Fact]
    public async Task CollectionUsesActualCarryRoomFreeVesselFamiliesAndCommittedCheckpointChanges()
    {
        using var generated = NormalPathWorld.CreateGenerated("goods-collection-boundaries", _ => new Choice());
        Assert.True((await generated.AdvanceOneTickAsync()).Advanced);
        var state = generated.ExportState();
        var actor = state.Inhabitants[0].InhabitantId;
        var household = PaidMarketWorld.HouseholdOf(state, actor);
        var house = PaidMarketWorld.HouseOf(state, household);
        var inventory = state.Society.Society.Inventory with
        {
            Lots = state.Society.Society.Inventory.Lots.Where(lot => lot.OwnerId != actor &&
                (lot.OwnerId != household || lot.ItemKind is not ("wood" or "water_jug" or "fresh_water"))).ToArray(),
        };
        inventory = InventoryFixture.AddLot(inventory, "collect-carried-stone", "stone", actor, 5);
        inventory = InventoryFixture.AddLot(inventory, "collect-partial-wood", "wood", household, 5, storageBuildingId: house.InstanceId);
        inventory = InventoryFixture.Reserve(inventory, "collect-held-wood", household, "collect-partial-wood", 2, "other-work", inventory.WorldTick + 100);
        foreach (var (id, quantity) in new[] { ("broken", 2), ("reserved", 2), ("fitting", 1), ("oversize", 3) })
        {
            inventory = InventoryFixture.AddLot(inventory, "collect-" + id + "-jug", "water_jug", household, 1, storageBuildingId: house.InstanceId);
            inventory = InventoryFixture.AddLot(inventory, "collect-" + id + "-water", "fresh_water", household, quantity,
                storageBuildingId: house.InstanceId, containerLotId: "collect-" + id + "-jug");
        }
        inventory = InventoryFixture.Reserve(inventory, "collect-held-water", household, "collect-reserved-water", 1, "other-work", inventory.WorldTick + 100);
        inventory = inventory with
        {
            Lots = inventory.Lots.Select(lot => lot.Id == "collect-broken-jug" ? lot with { ConditionBasisPoints = 0 } : lot).ToArray(),
        };
        state = Prepared(PaidMarketWorld.WithInventory(state, inventory), actor, house.Position);
        using var world = PrivateWorldRuntime.Restore(state, _ => new Choice());
        var request = new GoodsRequest(GoodsUse.Collect, actor, GoodsOwners.One(household),
            new GoodsKinds(["wood", "water_jug", "fresh_water"]), Explain: true);
        var saved = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        var answer = world.FindGoods(request);
        Assert.Equal(new[] { ("collect-fitting-jug", 1, 2), ("collect-fitting-water", 1, 2), ("collect-partial-wood", 3, 3) },
            answer.Matches.Select(match => (match.Lot.Id, match.Quantity, match.MoveUnits)).ToArray());
        Assert.Contains(("collect-broken-jug", GoodsReason.Damaged), answer.Excluded);
        Assert.Contains(("collect-broken-water", GoodsReason.Vessel), answer.Excluded);
        Assert.Contains(("collect-reserved-jug", GoodsReason.Reservation), answer.Excluded);
        Assert.Contains(("collect-reserved-water", GoodsReason.Reservation), answer.Excluded);
        Assert.Contains(("collect-oversize-jug", GoodsReason.CarryRoom), answer.Excluded);
        Assert.Contains(("collect-oversize-water", GoodsReason.CarryRoom), answer.Excluded);
        var preparation = world.FindGoods(request with { Use = GoodsUse.ReachableHoldings });
        Assert.Contains(preparation.Matches, match => match.Lot.Id == "collect-oversize-water" && match.Quantity == 3);
        Assert.DoesNotContain(preparation.Matches, match => match.Lot.Id == "collect-broken-water");
        Assert.Equal(preparation.Matches, world.FindGoods(request with { Use = GoodsUse.ReachableHoldings, Explain = false }).Matches);
        Assert.Equal(answer.Matches, world.FindGoods(request with { Explain = false }).Matches);
        Assert.Equal(5, answer.Total);
        foreach (var match in answer.Matches)
        {
            Assert.Equal(match, world.RecheckGoods(request, match.Lot.Id));
            var moving = match.Root;
            var quantity = InventoryContainerRules.IsContainer(moving.ItemKind) ? 1 : match.Quantity;
            var operation = "collect-proof-" + match.Lot.Id;
            var accepted = InventoryFixture.Relocate(inventory, operation,
                moving.Id, moving.OwnerId, quantity, carrierId: actor);
            var movedId = quantity < moving.Quantity ? moving.Id + "#move:" + operation : match.Lot.Id;
            Assert.True(PersonalEquipmentRules.IsPhysicallyCarried(accepted, accepted.GetLot(movedId), actor));
            if (quantity < moving.Quantity)
            {
                Assert.Equal((house.InstanceId, 2), (accepted.GetLot(moving.Id).StorageBuildingId, accepted.GetLot(moving.Id).Quantity));
                Assert.Equal(3, accepted.GetLot(movedId).Quantity);
            }
        }
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        var changed = InventoryFixture.Relocate(inventory, "other-housemate-collected", "collect-fitting-jug", household, 1,
            carrierId: state.Inhabitants.Single(person => person.InhabitantId != actor && PaidMarketWorld.HouseholdOf(state, person.InhabitantId) == household).InhabitantId);
        using var next = PrivateWorldRuntime.Restore(PaidMarketWorld.WithInventory(state, changed), _ => new Choice());
        Assert.Null(next.RecheckGoods(request, "collect-fitting-jug"));
        Assert.Null(next.RecheckGoods(request, "collect-fitting-water"));
        Assert.Contains(("collect-fitting-jug", GoodsReason.Custody), next.FindGoods(request).Excluded);
        world.Validate();
        next.Validate();
        using var reload = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved), _ => new Choice());
        Assert.Equal(answer.Matches, reload.FindGoods(request).Matches);
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(reload.ExportState()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeToolPickupUsesTheSameBoundariesAndKeepsHouseholdOwnership(bool reserved)
    {
        using var generated = NormalPathWorld.CreateGenerated("goods-collection-tool", _ => new Choice());
        Assert.True((await generated.AdvanceOneTickAsync()).Advanced);
        var state = generated.ExportState();
        var actor = state.Inhabitants[0].InhabitantId;
        var home = PaidMarketWorld.HouseholdOf(state, actor);
        var house = PaidMarketWorld.HouseOf(state, home);
        var inventory = state.Society.Society.Inventory with
        {
            Lots = state.Society.Society.Inventory.Lots.Where(lot => lot.OwnerId != actor &&
                (lot.OwnerId != home || ToolProgressionRules.Find(lot.ItemKind) is not { Family: ToolFamily.Axe })).ToArray(),
        };
        inventory = InventoryFixture.AddLot(inventory, "collect-household-axe", "wooden_axe", home, 1, storageBuildingId: house.InstanceId);
        if (reserved) inventory = InventoryFixture.Reserve(inventory, "collect-axe-held", home, "collect-household-axe", 1, "other-work", inventory.WorldTick + 100);
        state = Prepared(PaidMarketWorld.WithInventory(state, inventory), actor, house.Position);
        var chooser = new Choice("collect_wooden_axe");
        using var world = PrivateWorldRuntime.Restore(state, id => id == actor ? chooser : new Choice());
        var saved = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved), id => id == actor ? new Choice("collect_wooden_axe") : new Choice());
        var request = new GoodsRequest(GoodsUse.Collect, actor, GoodsOwners.One(home), GoodsKinds.One("wooden_axe"));
        Assert.Equal(!reserved, world.RecheckGoods(request, "collect-household-axe") is not null);
        Assert.False((await world.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        for (var tick = 0; tick < 4; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        }
        var axe = world.Society.Inventory.GetLot("collect-household-axe");
        Assert.Equal((home, 1), (axe.OwnerId, axe.Quantity));
        Assert.Equal(!reserved, PersonalEquipmentRules.IsCarried(axe, actor));
        Assert.Equal(reserved ? house.InstanceId : null, axe.StorageBuildingId);
        world.Validate();
        var final = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var loaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(final));
        Assert.Equal(final, PrivateWorldRuntimeCodec.Encode(loaded.ExportState()));
    }

    private static PrivateWorldRuntimeState Prepared(PrivateWorldRuntimeState state, string actor, GridPoint position) => state with
    {
        Inhabitants = state.Inhabitants.Select(person => person with
        {
            Position = person.InhabitantId == actor ? position : person.Position,
            Equipment = person.InhabitantId == actor ? null : person.Equipment,
            LastDecisionContext = null,
            Project = null,
            HungerBasisPoints = 9_500,
            Survival = new SurvivalCondition { WarmthBasisPoints = 10_000, NutritionBasisPoints = 10_000 },
        }).ToArray(),
        JevEnabled = false,
        RoutineHelper = RoutineHelperSettings.Off,
    };

    private sealed class Choice(string wanted = "safe_idle") : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(
            CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var selected = request.Observation.Candidates.FirstOrDefault(candidate => candidate.Id == wanted)?.Id ?? "safe_idle";
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId,
                request.Observation.InhabitantId, Kind, request.ProviderEpoch, request.Observation.RunEpoch,
                request.Observation.DecisionGeneration, request.Observation.ObservationDigest, selected, 1,
                request.Observation.Candidates.ToDictionary(candidate => candidate.Id, candidate => candidate.Id == selected ? 1d : 0d)));
        }
    }
}

using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class FarmFlourSourceTests
{
    [Theory]
    [InlineData("broken")]
    [InlineData("broken-with-pot")]
    [InlineData("healthy")]
    [InlineData("absent")]
    [InlineData("reserved")]
    [InlineData("broken-only")]
    [InlineData("full-hands")]
    public async Task FlourHaulingSelectsUsableStockAndDeliversItAcrossReload(string source)
    {
        using var generated = NormalPathWorld.CreateGenerated("audit-town-invariants", _ => new Choices("safe_idle"));
        Assert.True((await generated.AdvanceOneTickAsync()).Advanced);
        var state = generated.ExportState();
        var actor = state.Society.Society.Inhabitants.First(person => person.HouseholdId == "household:camp-alpha").Id;
        const string household = "household:camp-alpha";
        var farmhouse = generated.WorldSimulation.Buildings.Single(building => building.HouseholdId == household &&
            generated.WorldContent.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId).Tags.Contains("farmhouse"));
        var house = generated.WorldSimulation.Buildings.Single(building => building.HouseholdId == household &&
            generated.WorldContent.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId).Tags.Contains("house"));
        var inventory = state.Society.Society.Inventory;
        if (source != "absent")
        {
            inventory = InventoryFixture.AddLot(inventory, "a-flour-pot", InventoryContainerRules.StoragePot,
                household, 1, storageBuildingId: farmhouse.InstanceId);
            inventory = InventoryFixture.AddLot(inventory, "a-flour-contents", "flour", household, 2,
                storageBuildingId: farmhouse.InstanceId, containerLotId: "a-flour-pot");
            if (source is "broken" or "broken-with-pot" or "broken-only")
                inventory = InventoryFixture.WearSingleUnit(inventory, "a-flour-pot", 10_000);
            if (source == "reserved") inventory = InventoryFixture.Reserve(inventory, "held-flour-pot", household,
                "a-flour-contents", 2, "other_use", long.MaxValue);
        }
        if (source == "broken-with-pot")
            inventory = InventoryFixture.AddLot(inventory, "z-flour-pot", InventoryContainerRules.StoragePot,
                household, 1, storageBuildingId: farmhouse.InstanceId);
        if (source != "broken-only") inventory = InventoryFixture.AddLot(inventory, "z-usable-flour", "flour", household, 2,
            storageBuildingId: farmhouse.InstanceId, containerLotId: source == "broken-with-pot" ? "z-flour-pot" : null);
        if (source == "full-hands") inventory = InventoryFixture.AddLot(inventory, "full-cargo", "wood", actor, 8);
        state = FarmFieldTests.WithInventory(state, inventory) with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor ? person with
            {
                Position = farmhouse.Position, HungerBasisPoints = 9_500, Project = null,
                LastDecisionContext = null, TravelCooldownTicks = 0,
            } : person).ToArray(),
        };
        var initial = PrivateWorldRuntimeCodec.Encode(state);
        var chooser = new Choices("haul_farm_flour");
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(initial),
            id => id == actor ? chooser : new Choices("safe_idle"));
        Assert.Equal(initial, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        for (var tick = 0; tick < 4 && !world.Society.Inventory.Lots.Any(lot => lot.OwnerId == actor &&
                 lot.DeliveryBuildingId == house.InstanceId); tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.NotEmpty(chooser.Offered);
        if (source is "broken-only" or "full-hands")
        {
            Assert.DoesNotContain("haul_farm_flour", chooser.Offered);
            Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "farm_flour_picked_up");
            Assert.Equal(2, world.Society.Inventory.GetLot(source == "broken-only" ? "a-flour-contents" : "z-usable-flour").Quantity);
        }
        else
        {
            Assert.Contains("haul_farm_flour", chooser.Offered);
            var rootId = source == "healthy" ? "a-flour-pot" : source == "broken-with-pot" ? "z-flour-pot" : "z-usable-flour";
            var root = world.Society.Inventory.GetLot(rootId);
            Assert.Equal((actor, house.InstanceId), (root.OwnerId, root.DeliveryBuildingId));
            Assert.True(PersonalEquipmentRules.IsPhysicallyCarried(world.Society.Inventory, root, actor));
            Assert.Null(root.StorageBuildingId);
            var contentId = source == "healthy" ? "a-flour-contents" : "z-usable-flour";
            Assert.Equal(2, world.Society.Inventory.GetLot(contentId).Quantity);
            var carried = PrivateWorldRuntimeCodec.Encode(world.ExportState());
            using var delivery = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(carried),
                id => new Choices(id == actor ? "haul_household_stock" : "safe_idle"));
            Assert.Equal(carried, PrivateWorldRuntimeCodec.Encode(delivery.ExportState()));
            for (var tick = 0; tick < 40 && delivery.Society.Inventory.GetLot(rootId).StorageBuildingId != house.InstanceId; tick++)
                Assert.True((await delivery.AdvanceOneTickAsync()).Advanced);
            Assert.Equal((household, house.InstanceId),
                (delivery.Society.Inventory.GetLot(rootId).OwnerId, delivery.Society.Inventory.GetLot(rootId).StorageBuildingId));
            Assert.Equal((household, house.InstanceId, 2),
                (delivery.Society.Inventory.GetLot(contentId).OwnerId, delivery.Society.Inventory.GetLot(contentId).StorageBuildingId,
                    delivery.Society.Inventory.GetLot(contentId).Quantity));
            if (rootId != contentId) Assert.Equal(rootId, delivery.Society.Inventory.GetLot(contentId).ContainerLotId);
            Assert.Single(delivery.ExportState().Events, item => item.Kind == "farm_flour_picked_up");
            Assert.Single(delivery.ExportState().Events, item => item.Kind == "household_stock_delivered");
            AssertProtectedSource(delivery);
            AssertReload(delivery);
        }
        AssertProtectedSource(world);
        AssertReload(world);

        void AssertProtectedSource(PrivateWorldRuntime runtime)
        {
            if (source is "broken" or "broken-with-pot" or "broken-only" or "reserved")
            {
                Assert.Equal((household, farmhouse.InstanceId, 1, source == "reserved" ? 10_000 : 0),
                    (runtime.Society.Inventory.GetLot("a-flour-pot").OwnerId, runtime.Society.Inventory.GetLot("a-flour-pot").StorageBuildingId,
                        runtime.Society.Inventory.GetLot("a-flour-pot").Quantity, runtime.Society.Inventory.GetLot("a-flour-pot").ConditionBasisPoints));
                Assert.Equal((household, farmhouse.InstanceId, "a-flour-pot", 2),
                    (runtime.Society.Inventory.GetLot("a-flour-contents").OwnerId, runtime.Society.Inventory.GetLot("a-flour-contents").StorageBuildingId,
                        runtime.Society.Inventory.GetLot("a-flour-contents").ContainerLotId, runtime.Society.Inventory.GetLot("a-flour-contents").Quantity));
            }
            if (source == "reserved") Assert.Equal(InventoryReservationState.Reserved, runtime.Society.Inventory.GetReservation("held-flour-pot").State);
            if (source == "full-hands") Assert.Equal(8, runtime.Society.Inventory.GetLot("full-cargo").Quantity);
        }
    }

    private static void AssertReload(PrivateWorldRuntime world)
    {
        world.Validate();
        var saved = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var reloaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved));
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(reloaded.ExportState()));
    }

    private sealed class Choices(string wanted) : IDecisionProvider
    {
        public List<string> Offered { get; } = [];
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            Offered.AddRange(request.Observation.Candidates.Select(candidate => candidate.Id));
            var selected = request.Observation.Candidates.FirstOrDefault(candidate => candidate.Id == wanted) ??
                request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle");
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, request.Observation.InhabitantId,
                Kind, ProviderEpoch, request.Observation.RunEpoch, request.Observation.DecisionGeneration,
                request.Observation.ObservationDigest, selected.Id, 1,
                request.Observation.Candidates.ToDictionary(candidate => candidate.Id, candidate => candidate.Id == selected.Id ? 1d : 0d)));
        }
    }
}

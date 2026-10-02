using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed class HouseholdBuildingPreparationDeliveryTests
{
    private const string Household = "household:camp-beta";

    [Fact]
    public async Task BuildingPreparationLoadIsMarkedHauledAndFreesCapacityAcrossReload()
    {
        using var setup = NormalPathWorld.CreateGenerated("probe-a", _ => new IdleProvider());
        var initial = setup.ExportState();
        var initialBuildingIds = initial.WorldSimulation!.Buildings.Select(building => building.InstanceId)
            .ToHashSet(StringComparer.Ordinal);
        var actor = initial.Society.Society.Inhabitants.First(person => person.HouseholdId == Household).Id;
        var house = initial.WorldSimulation!.Buildings.Single(building => building.HouseholdId == Household &&
            setup.WorldContent.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId)
                .Tags.Contains("house", StringComparer.Ordinal));
        var inventory = initial.Society.Society.Inventory;
        var initialHouseStone = inventory.Lots.Where(lot => lot.OwnerId == Household && lot.ItemKind == "stone" &&
            lot.StorageBuildingId == house.InstanceId).Sum(lot => lot.Quantity);
        var actorLots = inventory.Lots.Where(lot => lot.OwnerId == actor || lot.OwnerId == Household && lot.ItemKind == "wood")
            .Select(lot => lot.Id).ToHashSet(StringComparer.Ordinal);
        inventory = inventory with
        {
            Lots = inventory.Lots.Where(lot => !actorLots.Contains(lot.Id)).ToArray(),
            Reservations = inventory.Reservations.Where(item => !actorLots.Contains(item.LotId)).ToArray(),
        };
        inventory = InventoryFixture.AddLot(inventory, "prep-fiber", "fiber", actor, 1);
        inventory = InventoryFixture.AddLot(inventory, "prep-house-tool", "tool", actor, 1);
        inventory = InventoryFixture.AddLot(inventory, "prep-axe", "wooden_axe", actor, 1);
        inventory = InventoryFixture.AddLot(inventory, "prep-pickaxe", "wooden_pickaxe", actor, 1);
        Assert.Equal(4, PersonalEquipmentRules.FreeCapacity(inventory, actor, equipment: null));
        initial = initial with
        {
            Society = initial.Society with { Society = initial.Society.Society with { Inventory = inventory } },
            Inhabitants = initial.Inhabitants.Select(person => person.InhabitantId == actor ? person with
            {
                Position = house.Position,
                HungerBasisPoints = 9_500,
                Equipment = null,
                Project = null,
                LastDecisionContext = null,
                TravelCooldownTicks = 0,
            } : person).ToArray(),
        };

        var chooser = new BuildingPreparationProvider();
        var world = PrivateWorldRuntime.Restore(initial, id => id == actor ? chooser : new IdleProvider());
        try
        {
            var sawStoneDeliveryAtTheCapacityBoundary = false;
            var sawToolStoredAfterStoneDelivery = false;
            for (var tick = 0; tick < 160 && !world.ExportState().Events.Any(item => item.Kind == "material_gathered" &&
                     item.Detail.StartsWith(actor + ":wood:", StringComparison.Ordinal)); tick++)
            {
                Assert.True((await world.AdvanceOneTickAsync()).Advanced);
                var state = world.ExportState();
                var equipment = state.Inhabitants.Single(person => person.InhabitantId == actor).Equipment;
                var carried = PersonalEquipmentRules.CarriedQuantity(state.Society.Society.Inventory, actor, equipment);
                Assert.InRange(carried, 0, PersonalEquipmentRules.BaseCapacity);
                var storedStone = state.Society.Society.Inventory.Lots.Where(lot => lot.OwnerId == Household &&
                    lot.ItemKind == "stone" && lot.StorageBuildingId == house.InstanceId).Sum(lot => lot.Quantity);
                if (!sawStoneDeliveryAtTheCapacityBoundary && storedStone >= initialHouseStone + 4)
                {
                    var free = PersonalEquipmentRules.FreeCapacity(state.Society.Society.Inventory, actor, equipment);
                    Assert.True(free == 4,
                        $"free={free}; carry={string.Join(",", state.Society.Society.Inventory.Lots.Where(lot => lot.OwnerId == actor).Select(lot => lot.ItemKind + ":" + lot.Quantity + ":" + lot.StorageBuildingId + ":" + lot.DeliveryBuildingId))}; chosen={string.Join(",", chooser.ChosenCandidates)}");
                    sawStoneDeliveryAtTheCapacityBoundary = true;
                }

                if (!sawToolStoredAfterStoneDelivery && state.Events.Any(item => item.Kind == "household_preparation_tool_stored" &&
                        item.Detail.StartsWith(actor + ":tool:1:", StringComparison.Ordinal)))
                {
                    Assert.Equal(5, PersonalEquipmentRules.FreeCapacity(state.Society.Society.Inventory, actor, equipment));
                    var storedTool = state.Society.Society.Inventory.GetLot("prep-house-tool");
                    Assert.Equal(Household, storedTool.OwnerId);
                    Assert.Equal(house.InstanceId, storedTool.StorageBuildingId);
                    Assert.DoesNotContain(state.Society.Society.Inventory.Reservations, reservation =>
                        reservation.LotId == storedTool.Id && reservation.State is
                            InventoryReservationState.Reserved or InventoryReservationState.PartiallyConsumed or
                            InventoryReservationState.Committed);
                    Assert.Null(state.Inhabitants.Single(person => person.InhabitantId == actor).Project);
                    sawToolStoredAfterStoneDelivery = true;
                }
            }

            Assert.True(chooser.SeenCandidates.Any(id => id.StartsWith("gather_building_material:wood", StringComparison.Ordinal)),
                $"chosen={string.Join(",", chooser.ChosenCandidates)}; events={string.Join("|", world.ExportState().Events.Select(item => item.Kind + ":" + item.Detail))}; lots={string.Join("|", world.Society.Inventory.Lots.Select(lot => lot.OwnerId + ":" + lot.ItemKind + ":" + lot.Quantity + ":" + lot.StorageBuildingId + ":" + lot.DeliveryBuildingId))}");
            Assert.Contains(chooser.ChosenCandidates, id => id.StartsWith("gather_building_material:wood", StringComparison.Ordinal));
            Assert.Contains(chooser.ChosenCandidates, id => id == "gather_building_material:stone");
            Assert.True(sawStoneDeliveryAtTheCapacityBoundary);
            Assert.True(sawToolStoredAfterStoneDelivery);
            Assert.Contains(world.ExportState().Events, item => item.Kind == "household_preparation_tool_stored" &&
                item.Detail.StartsWith(actor + ":tool:", StringComparison.Ordinal));

            var gatheredState = world.ExportState();
            var woodEvent = Assert.Single(gatheredState.Events, item => item.Kind == "material_gathered" &&
                item.Detail.StartsWith(actor + ":wood:", StringComparison.Ordinal));
            Assert.Contains(gatheredState.Events, item => item.Kind == "material_gathered" &&
                item.Detail == actor + ":stone:4");
            Assert.Equal(actor + ":wood:4", woodEvent.Detail);
            var deliveryLots = gatheredState.Society.Society.Inventory.Lots.Where(lot => lot.OwnerId == actor &&
                lot.DeliveryBuildingId == house.InstanceId).ToArray();
            Assert.Contains(deliveryLots, lot => lot.ItemKind == "wood" && lot.Quantity == 4);
            Assert.All(deliveryLots, lot => Assert.Null(lot.StorageBuildingId));
            var gatheredCarry = PersonalEquipmentRules.CarriedQuantity(gatheredState.Society.Society.Inventory, actor,
                gatheredState.Inhabitants.Single(person => person.InhabitantId == actor).Equipment);
            var collectedTreeSeeds = gatheredState.Society.Society.Inventory.Lots.Where(lot => lot.OwnerId == actor &&
                lot.ItemKind == TreeGrowthRules.TreeSeedItem && lot.DeliveryBuildingId == house.InstanceId)
                .Sum(lot => lot.Quantity);
            Assert.Equal(7 + collectedTreeSeeds, gatheredCarry);

            var saved = PrivateWorldRuntimeCodec.Encode(gatheredState);
            world.Dispose();
            world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved),
                id => id == actor ? chooser : new IdleProvider());
            Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(world.ExportState()));

            var hadToTravel = false;
            for (var tick = 0; tick < 160 && world.Society.Inventory.Lots.Any(lot => lot.OwnerId == actor &&
                     lot.DeliveryBuildingId == house.InstanceId); tick++)
            {
                Assert.True((await world.AdvanceOneTickAsync()).Advanced);
                hadToTravel |= world.ExportState().Events.Any(item => item.Kind == "inhabitant_moved" &&
                    item.Detail.StartsWith(actor + ":", StringComparison.Ordinal) && item.Detail.EndsWith(":household_stock", StringComparison.Ordinal));
                Assert.InRange(PersonalEquipmentRules.CarriedQuantity(world.Society.Inventory, actor,
                    world.Inhabitants.Single(person => person.InhabitantId == actor).Equipment), 0, PersonalEquipmentRules.BaseCapacity);
            }

            Assert.True(hadToTravel);
            Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.OwnerId == actor &&
                lot.DeliveryBuildingId == house.InstanceId);
            Assert.Contains(world.Society.Inventory.Lots, lot => lot.OwnerId == Household && lot.ItemKind == "wood" &&
                lot.Quantity == 4 && lot.StorageBuildingId == house.InstanceId && lot.DeliveryBuildingId is null);
            Assert.Equal(5, PersonalEquipmentRules.FreeCapacity(world.Society.Inventory, actor,
                world.Inhabitants.Single(person => person.InhabitantId == actor).Equipment));
            world.Validate();

            var finalBytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
            using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(finalBytes));
            Assert.Equal(finalBytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));

            for (var tick = 0; tick < 1_000 && !world.WorldSimulation.Buildings.Any(building =>
                     building.HouseholdId == Household && !initialBuildingIds.Contains(building.InstanceId)); tick++)
            {
                Assert.True((await world.AdvanceOneTickAsync()).Advanced);
                Assert.InRange(PersonalEquipmentRules.CarriedQuantity(world.Society.Inventory, actor,
                    world.Inhabitants.Single(person => person.InhabitantId == actor).Equipment),
                    0, PersonalEquipmentRules.BaseCapacity);
            }

            var builtHouseholdBuilding = Assert.Single(world.WorldSimulation.Buildings, building =>
                building.HouseholdId == Household && !initialBuildingIds.Contains(building.InstanceId));
            var completedState = world.ExportState();
            Assert.Contains(completedState.Events, item => item.Kind == "build_completed" &&
                item.Detail.StartsWith(builtHouseholdBuilding.InstanceId + ":", StringComparison.Ordinal));
            Assert.Contains(completedState.Events, item => item.Kind == "project_progress" &&
                item.Detail.StartsWith(actor + ":working:", StringComparison.Ordinal));
            var completedProject = world.Inhabitants.Single(person => person.InhabitantId == actor).Project!;
            Assert.StartsWith("build:building:", completedProject.CandidateId);
            Assert.Equal("completed", completedProject.Stage);
            var spareTool = world.Society.Inventory.GetLot("prep-house-tool");
            Assert.Equal(Household, spareTool.OwnerId);
            Assert.Equal(house.InstanceId, spareTool.StorageBuildingId);
            Assert.Equal(1, spareTool.Quantity);
            Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.Id == "prep-house-tool" &&
                lot.OwnerId == actor);
        }
        finally
        {
            world.Dispose();
        }
    }

    private sealed class BuildingPreparationProvider : IDecisionProvider
    {
        public HashSet<string> SeenCandidates { get; } = new(StringComparer.Ordinal);
        public List<string> ChosenCandidates { get; } = [];
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;

        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            foreach (var candidate in request.Observation.Candidates)
                SeenCandidates.Add(candidate.Id);
            var candidates = request.Observation.Candidates;
            var selected = candidates.FirstOrDefault(candidate => candidate.Id == "haul_household_stock" &&
                    (candidate.Description.StartsWith("Store a carried work tool", StringComparison.Ordinal) ||
                     candidate.Description.StartsWith("Deliver already collected household supplies", StringComparison.Ordinal))) ??
                candidates.FirstOrDefault(candidate => candidate.Id.StartsWith("gather_building_material:", StringComparison.Ordinal)) ??
                candidates.FirstOrDefault(candidate => candidate.Id.StartsWith("build:building:", StringComparison.Ordinal)) ??
                candidates.Single(candidate => candidate.Id == "safe_idle");
            ChosenCandidates.Add(selected.Id);
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId,
                request.Observation.InhabitantId, Kind, ProviderEpoch, request.Observation.RunEpoch,
                request.Observation.DecisionGeneration, request.Observation.ObservationDigest, selected.Id, 1,
                candidates.ToDictionary(candidate => candidate.Id,
                    candidate => candidate.Id == selected.Id ? 1d : 0d, StringComparer.Ordinal)));
        }
    }

    private sealed class IdleProvider : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
            CancellationToken cancellationToken = default) => new DeterministicDecisionProvider().DecideAsync(request with
            {
                Observation = request.Observation with
                {
                    Candidates = [request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle")],
                },
            }, cancellationToken);
    }
}

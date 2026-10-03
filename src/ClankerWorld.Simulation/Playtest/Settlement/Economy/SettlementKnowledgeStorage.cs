using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private const string KnowledgeCollectPrefix = "knowledge_collect:";
    private const string KnowledgeStorePrefix = "knowledge_store:";

    private static int KnowledgeCollectionRange(string actor, InventoryLot lot) => lot.OwnerId == actor
        ? lot.GroundPosition is null ? 1 : ResourceInteractionRange
        : HouseholdStockInteractionRange(lot);

    private InventoryLot? KnowledgeArtifactForCollection(string actor, AgentKnowledgeArtifact artifact)
    {
        var inventory = society.Checkpoint.Inventory;
        var lot = inventory.Lots.FirstOrDefault(item => item.Id == artifact.LotId && item.ItemKind == artifact.Kind &&
            item.Quantity == 1 && item.CarrierId is null && item.ContainerLotId is null &&
            item.DeliveryBuildingId is null && AvailableLotQuantity(item) == 1);
        if (lot is null || PersonalEquipmentRules.IsCarried(lot, actor))
            return null;
        if (lot.OwnerId == actor)
            return PersonalGoodsAwaitingCollection(actor).Any(item => item.Id == lot.Id) ? lot : null;
        var householdId = society.Checkpoint.GetInhabitant(actor).HouseholdId;
        return householdId is not null && lot.OwnerId == householdId &&
            (lot.StorageBuildingId is null || worldSimulation.Buildings.Any(building =>
                building.InstanceId == lot.StorageBuildingId && building.HouseholdId == householdId)) ? lot : null;
    }

    private void AddKnowledgeStorageCandidates(List<CognitionCandidate> candidates,
        string actor, PlaytestInhabitantState person)
    {
        if (!AdultResident(actor)) return;
        if (FreeCarryCapacity(actor) > 0)
        {
            foreach (var artifact in knowledge.Artifacts.OrderBy(item => item.Id, StringComparer.Ordinal))
            {
                if (KnowledgeArtifactForCollection(actor, artifact) is not { } lot)
                    continue;
                var position = HouseholdStockPosition(lot);
                var range = KnowledgeCollectionRange(actor, lot);
                if (!IsWithinInteractionRange(person.Position, position, range) &&
                    FindUnoccupiedRoute(actor, person.Position, position, range).Count == 0)
                    continue;
                // Explicit access stays available without an automatic
                // collect/store cycle when the owner has no current use.
                candidates.Add(new(KnowledgeCollectPrefix + artifact.Id,
                    $"Collect {artifact.Title} to carry, read, copy or trade it.", 190));
            }
        }
        if (society.Checkpoint.GetInhabitant(actor).HouseholdId is not { } householdId ||
            HouseForHousehold(householdId) is not { } house ||
            StorageRoomAfterInboundDeliveries(house.InstanceId) < 1 ||
            person.Position != house.Position && FindUnoccupiedRoute(actor, person.Position, house.Position, 0).Count == 0)
            return;
        foreach (var artifact in HeldKnowledgeArtifacts(actor))
            candidates.Add(new(KnowledgeStorePrefix + artifact.Id,
                $"Store {artifact.Title} in your House while keeping personal ownership.", 190));
    }

    private bool ApplyKnowledgeStorageCandidate(string actor, PlaytestInhabitantState person, string candidateId)
    {
        var collect = candidateId.StartsWith(KnowledgeCollectPrefix, StringComparison.Ordinal);
        var store = candidateId.StartsWith(KnowledgeStorePrefix, StringComparison.Ordinal);
        if (!collect && !store) return false;
        if (!AdultResident(actor)) return true;
        var artifactId = candidateId[(collect ? KnowledgeCollectPrefix.Length : KnowledgeStorePrefix.Length)..];
        var artifact = knowledge.Artifacts.FirstOrDefault(item => item.Id == artifactId);
        if (artifact is null) return true;
        if (collect)
        {
            if (FreeCarryCapacity(actor) < 1 || KnowledgeArtifactForCollection(actor, artifact) is not { } lot)
                return true;
            var position = HouseholdStockPosition(lot);
            var range = KnowledgeCollectionRange(actor, lot);
            if (!IsWithinInteractionRange(person.Position, position, range))
            {
                MoveToward(actor, person, position, "knowledge_collect", range);
                return true;
            }
            ApplyInventoryTransition(inventory => lot.OwnerId == actor
                ? InventoryFixture.Relocate(inventory, $"knowledge-collect:{WorldTick}:{actor}:{artifact.Id}",
                    lot.Id, actor, 1, actor)
                : InventoryFixture.Transfer(inventory, $"knowledge-collect:{WorldTick}:{actor}:{artifact.Id}",
                    lot.OwnerId, actor, lot.Id, 1, "knowledge_collected"));
            AppendEvent("agent_knowledge_artifact_collected", $"{actor}|{artifact.Id}");
            return true;
        }
        if (!HeldKnowledgeArtifacts(actor).Any(item => item.Id == artifact.Id) ||
            society.Checkpoint.GetInhabitant(actor).HouseholdId is not { } householdId ||
            HouseForHousehold(householdId) is not { } house ||
            StorageRoomAfterInboundDeliveries(house.InstanceId) < 1)
            return true;
        if (person.Position != house.Position)
        {
            MoveToward(actor, person, house.Position, "knowledge_store", 0);
            return true;
        }
        ApplyInventoryTransition(inventory => InventoryFixture.Relocate(inventory,
            $"knowledge-store:{WorldTick}:{actor}:{artifact.Id}", artifact.LotId, actor, 1,
            storageBuildingId: house.InstanceId));
        AppendEvent("agent_knowledge_artifact_stored", $"{actor}|{artifact.Id}|{house.InstanceId}");
        return true;
    }
}

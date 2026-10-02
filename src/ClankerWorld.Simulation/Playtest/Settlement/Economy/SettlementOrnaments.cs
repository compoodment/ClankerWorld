using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

public sealed record OrnamentChangeResult(bool Applied, string? LotId = null, string? Failure = null);

public sealed partial class PrivateWorldRuntime
{
    private const string WearOrnamentPrefix = "wear_ornament:";
    private const string RemoveOrnamentCandidate = "remove_ornament";
    private const string GiftOrnamentPrefix = "gift_ornament:";

    private static bool IsOrnamentCandidate(string candidate) => candidate == RemoveOrnamentCandidate ||
        candidate.StartsWith(WearOrnamentPrefix, StringComparison.Ordinal) ||
        candidate.StartsWith(GiftOrnamentPrefix, StringComparison.Ordinal);

    // Preserve complete lot/agent identities without delimiter ambiguity or unbounded action IDs.
    private static string OrnamentChoiceId(string prefix, params string[] identity) => prefix +
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(identity))));

    private bool LivingOrnamentAdult(string actor) => inhabitants.ContainsKey(actor) && AdultResident(actor);

    private bool BusyWithOrnamentIncompatibleWork(string actor) => inhabitants[actor].Equipment?.Repair is not null ||
        inhabitants[actor].Project is { Stage: not ("completed" or "cancelled") } ||
        fields.Any(field => field.Work?.WorkerId == actor);

    private bool IsPersonallyCarriedOrnament(InventoryLot lot, string actor) =>
        PersonalEquipmentRules.IsOrnament(lot.ItemKind) && PersonalEquipmentRules.IsCarried(lot, actor) &&
        lot.ContainerLotId is null && lot.DeliveryBuildingId is null && AvailableLotQuantity(lot) > 0;

    private IEnumerable<InventoryLot> OrnamentSources(string actor)
    {
        if (!LivingOrnamentAdult(actor)) return [];
        var household = society.Checkpoint.GetInhabitant(actor).HouseholdId;
        return society.Checkpoint.Inventory.Lots.Where(lot => PersonalEquipmentRules.IsOrnament(lot.ItemKind) &&
                lot.ContainerLotId is null && lot.DeliveryBuildingId is null && AvailableLotQuantity(lot) > 0 &&
                (IsPersonallyCarriedOrnament(lot, actor) || household is not null && lot.OwnerId == household &&
                    FreeCarryCapacity(actor) > 0 &&
                    (lot.StorageBuildingId is null || worldSimulation.Buildings.Any(building =>
                        building.InstanceId == lot.StorageBuildingId && building.HouseholdId == household)) &&
                    (IsWithinInteractionRange(inhabitants[actor].Position, HouseholdStockPosition(lot),
                         HouseholdStockInteractionRange(lot)) || CanReachSharedItem(actor, lot))))
            .OrderBy(lot => lot.OwnerId == actor ? 0 : 1).ThenBy(lot => lot.Id, StringComparer.Ordinal);
    }

    private IEnumerable<PlaytestInhabitantState> LocalOrnamentRecipients(string actor) => inhabitants.Values
        .Where(person => person.InhabitantId != actor && LivingOrnamentAdult(person.InhabitantId) &&
            IsWithinInteractionRange(inhabitants[actor].Position, person.Position, ResourceInteractionRange))
        .OrderBy(person => person.InhabitantId, StringComparer.Ordinal).Take(8);

    private void AddOrnamentCandidates(List<CognitionCandidate> candidates, string actor)
    {
        if (!LivingOrnamentAdult(actor) || BusyWithOrnamentIncompatibleWork(actor)) return;
        foreach (var lot in OrnamentSources(actor).Where(lot => lot.Id != inhabitants[actor].Equipment?.OrnamentLotId).Take(8))
            candidates.Add(new(OrnamentChoiceId(WearOrnamentPrefix, lot.Id),
                $"Choose and wear an accessible {lot.ItemKind.Replace('_', ' ')}. It stays one carried item.", 180,
                lot.StorageBuildingId));
        if (inhabitants[actor].Equipment?.OrnamentLotId is not null)
            candidates.Add(new(RemoveOrnamentCandidate, "Remove your worn ornament and keep it among your carried goods.", 181));
        var gifts = 0;
        foreach (var lot in society.Checkpoint.Inventory.Lots.Where(lot => IsPersonallyCarriedOrnament(lot, actor))
                     .OrderBy(lot => lot.Id, StringComparer.Ordinal).Take(4))
            foreach (var recipient in LocalOrnamentRecipients(actor).Where(person => FreeCarryCapacity(person.InhabitantId) > 0))
            {
                if (gifts++ >= 16) return;
                var name = society.Checkpoint.GetInhabitant(recipient.InhabitantId).Name;
                candidates.Add(new(OrnamentChoiceId(GiftOrnamentPrefix, lot.Id, recipient.InhabitantId),
                    $"Give one personally carried {lot.ItemKind.Replace('_', ' ')} to {name} nearby.", 190,
                    recipient.InhabitantId, name));
            }
    }

    // These aesthetic/property choices execute only through fresh personal admission.
    // Generic continuing intentions have no wear/remove/gift authority.
    private bool ApplyOrnamentDecision(SocietyCognitionDispatchResult decision)
    {
        var selected = decision.Admission.Intention;
        if (selected is null || !IsOrnamentCandidate(selected.CandidateId)) return false;
        if (PendingInstructionFor(decision.InhabitantId)?.Kind == OwnerInstructionKind.MustDo) return false;
        if (!decision.Admission.Accepted || decision.Admission.FellBack ||
            selected.Provider != DecisionProviderKind.LargeLanguageModel || selected.InhabitantId != decision.InhabitantId ||
            !LivingOrnamentAdult(decision.InhabitantId) || BusyWithOrnamentIncompatibleWork(decision.InhabitantId)) return true;
        var actor = decision.InhabitantId;
        if (selected.CandidateId == RemoveOrnamentCandidate)
        {
            _ = RemoveOrnamentCore(actor);
            return true;
        }
        if (selected.CandidateId.StartsWith(WearOrnamentPrefix, StringComparison.Ordinal))
        {
            var source = OrnamentSources(actor).FirstOrDefault(lot =>
                OrnamentChoiceId(WearOrnamentPrefix, lot.Id) == selected.CandidateId);
            if (source is null) return true;
            var person = inhabitants[actor];
            if (source.OwnerId != actor && !IsWithinInteractionRange(person.Position, HouseholdStockPosition(source),
                    HouseholdStockInteractionRange(source)))
                MoveToward(actor, person, HouseholdStockPosition(source), "ornament", HouseholdStockInteractionRange(source));
            else _ = WearOrnamentCore(actor, source.Id);
            return true;
        }
        foreach (var lot in society.Checkpoint.Inventory.Lots.Where(lot => IsPersonallyCarriedOrnament(lot, actor)))
            foreach (var recipient in LocalOrnamentRecipients(actor))
                if (OrnamentChoiceId(GiftOrnamentPrefix, lot.Id, recipient.InhabitantId) == selected.CandidateId)
                {
                    _ = GiveOrnamentCore(actor, recipient.InhabitantId, lot.Id);
                    return true;
                }
        return true;
    }

    public OrnamentChangeResult WearOrnament(string actor, string lotId)
    {
        gate.Wait();
        try { return WearOrnamentCore(actor, lotId); }
        finally { gate.Release(); }
    }

    private OrnamentChangeResult WearOrnamentCore(string actor, string lotId)
    {
        if (!LivingOrnamentAdult(actor)) return new(false, Failure: "A living adult must choose the ornament.");
        var person = inhabitants[actor];
        if (person.Equipment?.OrnamentLotId == lotId) return new(false, Failure: "This ornament is already worn.");
        var lot = OrnamentSources(actor).FirstOrDefault(item => item.Id == lotId);
        if (lot is null) return new(false, Failure: "Choose a usable, unreserved ornament you carry or your household holds.");
        if (lot.OwnerId != actor && !IsWithinInteractionRange(person.Position, HouseholdStockPosition(lot),
                HouseholdStockInteractionRange(lot)))
            return new(false, Failure: "Walk to the ornament's actual household stock first.");
        var previous = person.Equipment;
        var equippedId = lot.Id;
        try
        {
            ApplyInventoryTransition(inventory =>
            {
                if (lot.OwnerId != actor)
                {
                    var operation = $"ornament-wear:{WorldTick}:{nextEventId}:{actor}:{lot.Id}";
                    inventory = InventoryFixture.Transfer(inventory, operation, lot.OwnerId, actor, lot.Id, 1, "ornament_collected");
                    equippedId = lot.Quantity == 1 ? lot.Id : lot.Id + "#transfer:" + operation;
                }
                else if (lot.Quantity > 1)
                {
                    equippedId = $"ornament-unit:{WorldTick}:{nextEventId}:{actor}:{lot.Id}";
                    inventory = InventoryFixture.SplitLot(inventory, lot.Id, 1, equippedId);
                }
                inhabitants[actor] = person with
                { Equipment = (previous ?? new PersonalEquipment()) with { OrnamentLotId = equippedId } };
                return inventory;
            });
        }
        catch
        {
            inhabitants[actor] = person;
            throw;
        }
        checkpointSchemaVersion = StateSchemaVersion;
        AppendEvent("ornament_worn", $"{actor}:ornament:{equippedId}");
        return new(true, equippedId);
    }

    public OrnamentChangeResult RemoveOrnament(string actor)
    {
        gate.Wait();
        try { return RemoveOrnamentCore(actor); }
        finally { gate.Release(); }
    }

    private OrnamentChangeResult RemoveOrnamentCore(string actor)
    {
        if (!LivingOrnamentAdult(actor) || inhabitants[actor].Equipment?.OrnamentLotId is not { } id)
            return new(false, Failure: "This adult is not wearing an ornament.");
        var person = inhabitants[actor];
        inhabitants[actor] = person with { Equipment = person.Equipment! with { OrnamentLotId = null } };
        checkpointSchemaVersion = StateSchemaVersion;
        AppendEvent("ornament_removed", $"{actor}:ornament:{id}");
        return new(true, id);
    }

    public OrnamentChangeResult GiveOrnament(string actor, string recipientId, string lotId)
    {
        gate.Wait();
        try { return GiveOrnamentCore(actor, recipientId, lotId); }
        finally { gate.Release(); }
    }

    private OrnamentChangeResult GiveOrnamentCore(string actor, string recipientId, string lotId)
    {
        if (!LivingOrnamentAdult(actor) || !LocalOrnamentRecipients(actor).Any(person => person.InhabitantId == recipientId))
            return new(false, Failure: "Choose a nearby living adult to receive the gift.");
        var lot = society.Checkpoint.Inventory.Lots.FirstOrDefault(item => item.Id == lotId && IsPersonallyCarriedOrnament(item, actor));
        if (lot is null) return new(false, Failure: "The giver must personally carry one usable, unreserved ornament.");
        if (FreeCarryCapacity(recipientId) < 1) return new(false, Failure: "The recipient has no room to carry the gift.");
        var person = inhabitants[actor];
        var operation = $"ornament-gift:{WorldTick}:{nextEventId}:{actor}:{recipientId}:{lot.Id}";
        var receivedId = lot.Quantity == 1 ? lot.Id : lot.Id + "#transfer:" + operation;
        try
        {
            ApplyInventoryTransition(inventory =>
            {
                var updated = InventoryFixture.Transfer(inventory, operation, actor, recipientId, lot.Id, 1, "ornament_given");
                if (person.Equipment?.OrnamentLotId == lot.Id)
                    inhabitants[actor] = person with { Equipment = person.Equipment with { OrnamentLotId = null } };
                return updated;
            });
        }
        catch
        {
            inhabitants[actor] = person;
            throw;
        }
        checkpointSchemaVersion = StateSchemaVersion;
        AppendEvent("ornament_given", $"{actor}:ornament_gift:{recipientId}:{lot.Id}:{receivedId}");
        return new(true, receivedId);
    }
}

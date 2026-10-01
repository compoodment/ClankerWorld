using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private static void ValidatePhysicalKnowledgeWriting(PrivateWorldRuntimeState state)
    {
        if (state.Knowledge is not { } ledger) return;
        if (state.SchemaVersion < 36)
        {
            if (ledger.Artifacts is { Count: > 0 })
                throw new InvalidDataException("Older alpha knowledge items have no physical paper-writing proof and cannot be restored.");
            return;
        }
        if (ledger.Artifacts is null || state.Society?.Society?.Inventory is not { } inventory)
            throw new InvalidDataException("Knowledge writing requires a saved artifact ledger and inventory.");
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var artifact in ledger.Artifacts)
        {
            if (artifact is null || artifact.Facts is null || artifact.Facts.Any(fact => fact is null || fact.ResourceKinds is null))
                throw new InvalidDataException("The knowledge artifact ledger contains a missing item or fact.");
            var house = state.WorldSimulation?.Buildings.FirstOrDefault(building => building.InstanceId == artifact.WritingBuildingId);
            var definition = state.WorldContent?.Buildings.FirstOrDefault(building => building.CanonicalId == house?.DefinitionId);
            var purpose = "knowledge-writing:" + artifact.Id;
            if (house?.HouseholdId is null || definition?.Tags.Contains("house", StringComparer.Ordinal) != true ||
                artifact.InputReservationIds is not { Count: > 0 } ids || ids.Distinct(StringComparer.Ordinal).Count() != ids.Count)
                throw new InvalidDataException("A physical knowledge item must retain its writing House and consumed supplies.");
            var quantities = new int[2];
            foreach (var id in ids)
            {
                var reservation = inventory.Reservations.FirstOrDefault(item => item.Id == id);
                var paper = id.StartsWith(purpose + ":quantity:0:lot:", StringComparison.Ordinal);
                var cloth = id.StartsWith(purpose + ":quantity:1:lot:", StringComparison.Ordinal);
                var input = inventory.Lots.FirstOrDefault(lot => lot.Id == reservation?.LotId);
                if (!used.Add(id) || reservation is null || reservation.OwnerId != house.HouseholdId ||
                    reservation.Purpose != purpose || reservation.State != InventoryReservationState.Completed || !paper && !cloth ||
                    input is not null && input.ItemKind != (paper ? "paper" : "cloth"))
                    throw new InvalidDataException("A knowledge item's writing supplies are missing, reused or unconsumed.");
                quantities[paper ? 0 : 1] = checked(quantities[paper ? 0 : 1] + reservation.Quantity);
            }
            if (quantities[0] != (artifact.Kind == "book" ? 2 : 1) || quantities[1] != (artifact.Kind == "book" ? 1 : 0))
                throw new InvalidDataException("A written knowledge item must consume its exact paper and binding costs.");
            if (artifact.CopiedFromArtifactId is { } sourceId)
            {
                var source = ledger.Artifacts.FirstOrDefault(item => item.Id == sourceId);
                if (source?.Facts is null || source.Facts.Any(fact => fact is null || fact.ResourceKinds is null) ||
                    string.CompareOrdinal(source.Id, artifact.Id) >= 0 || source.CreatedTick > artifact.CreatedTick ||
                    source.Kind != artifact.Kind || source.Facts.Count != artifact.Facts.Count ||
                    source.Facts.Any(fact => !artifact.Facts.Any(copy => copy.Position == fact.Position && copy.Terrain == fact.Terrain &&
                        copy.DiscovererId == fact.DiscovererId && copy.ResourceKinds.SequenceEqual(fact.ResourceKinds, StringComparer.Ordinal))))
                    throw new InvalidDataException("A physical copy must preserve the source item's actual discovered sites.");
            }
        }
    }
}

using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

public sealed record MedicalTreatment(string CaregiverId, string SupplyLotId, string DoseReservationId, string Kind,
    long StartedTick, long LastProcessedTick, int RemainingTicks);
public sealed record MedicalCareResult(bool Applied, string? Failure = null);

public sealed partial class PrivateWorldRuntime
{
    private bool HasMedicalPermission(string caregiver, string patient) => caregiver == patient ||
        inhabitants[patient].MedicalCaregiverIds?.Contains(caregiver, StringComparer.Ordinal) == true ||
        society.Checkpoint.Relationships.Any(edge => edge.Type == SocietyRelationshipType.Caregiver &&
            edge.State == SocietyRelationshipState.Accepted && edge.EffectiveTick <= WorldTick &&
            edge.ProposerId == caregiver && edge.TargetId == patient);

    public MedicalCareResult AllowMedicalCare(string patient, string caregiver, bool allowed)
    {
        gate.Wait();
        try
        {
            if (!AdultResident(patient) || patient == caregiver || !AdultResident(caregiver))
                return new(false, "A living adult patient chooses a named adult caregiver.");
            var person = inhabitants[patient];
            var ids = (person.MedicalCaregiverIds ?? []).Where(id => id != caregiver).ToList();
            if (allowed)
            {
                if (ids.Count >= 16) return new(false, "Revoke an earlier care permission first.");
                ids.Add(caregiver);
            }
            inhabitants[patient] = person with
            {
                MedicalCaregiverIds = ids.Count == 0 ? null : ids.Order(StringComparer.Ordinal).ToArray(),
                MedicalTreatment = !allowed && person.MedicalTreatment?.CaregiverId == caregiver ? null : person.MedicalTreatment,
            };
            checkpointSchemaVersion = StateSchemaVersion;
            AppendEvent(allowed ? "medical_care_allowed" : "medical_care_revoked", $"{patient}|{caregiver}");
            return new(true);
        }
        finally { gate.Release(); }
    }

    public MedicalCareResult TreatPatient(string caregiver, string patient, string kind)
    {
        gate.Wait();
        try { return TreatPatientCore(caregiver, patient, kind); }
        finally { gate.Release(); }
    }

    private InventoryLot? MedicalSupplyAtHand(string caregiver, string kind) => society.Checkpoint.Inventory.Lots
        .Where(lot => lot.ItemKind == kind && lot.ContainerLotId is null && lot.GroundPosition is null &&
            lot.DeliveryBuildingId is null && AvailableLotQuantity(lot) > 0 &&
            (lot.OwnerId == caregiver && lot.StorageBuildingId is null ||
             lot.OwnerId == HouseholdFor(caregiver) && lot.StorageBuildingId is { } buildingId &&
             worldSimulation.Buildings.Any(building => building.InstanceId == buildingId &&
                 building.HouseholdId == lot.OwnerId && building.Position == inhabitants[caregiver].Position)))
        .OrderBy(lot => lot.OwnerId == caregiver ? 0 : 1).ThenBy(lot => lot.Id, StringComparer.Ordinal).FirstOrDefault();

    private MedicalCareResult TreatPatientCore(string caregiver, string patient, string kind)
    {
        if (kind is not ("bandage" or "medicine")) return new(false, "Choose a bandage for an injury or medicine for illness.");
        if (!AdultResident(caregiver) || !inhabitants.TryGetValue(patient, out var person))
            return new(false, "A living adult must provide care to a living patient.");
        if (!HasMedicalPermission(caregiver, patient)) return new(false, "The patient has not accepted this caregiver.");
        if (!IsWithinInteractionRange(inhabitants[caregiver].Position, person.Position, 1))
            return new(false, "Bring the care supply to the patient first.");
        if (person.MedicalTreatment is not null) return new(false, "A consumed dose is already being applied gradually.");
        if (kind == "bandage" && society.Checkpoint.GetInhabitant(patient).HealthBasisPoints == 10_000 ||
            kind == "medicine" && (person.Survival?.IllnessBasisPoints ?? 0) == 0)
            return new(false, "This patient does not need that treatment.");
        if (MedicalSupplyAtHand(caregiver, kind) is not { } supply)
            return new(false, $"Bring usable, unreserved {kind}; another household's Clinic stock requires an agreed purchase.");
        var id = $"medical-dose:{WorldTick}:{nextEventId}:{caregiver}:{patient}";
        ApplyInventoryTransition(inventory => InventoryFixture.ConsumeReservation(
            InventoryFixture.Reserve(inventory, id, supply.OwnerId, supply.Id, 1, "medical_treatment", WorldTick), id));
        inhabitants[patient] = person with
        {
            MedicalTreatment = new(caregiver, supply.Id, id, kind, WorldTick, WorldTick, CareContent.TreatmentTicks),
        };
        checkpointSchemaVersion = StateSchemaVersion;
        AppendEvent("medical_treatment_started", $"{patient}|{caregiver}|{kind}|{supply.Id}");
        return new(true);
    }

    private void AdvanceMedicalTreatments()
    {
        foreach (var person in inhabitants.Values.ToArray())
        {
            if (person.MedicalTreatment is not { } treatment || treatment.LastProcessedTick >= WorldTick) continue;
            if (society.Checkpoint.GetInhabitant(person.InhabitantId).Status != SocietyInhabitantStatus.Active)
            {
                inhabitants[person.InhabitantId] = person with { MedicalTreatment = null };
                continue;
            }
            if (!AdultResident(treatment.CaregiverId) || !HasMedicalPermission(treatment.CaregiverId, person.InhabitantId))
            {
                inhabitants[person.InhabitantId] = person with { MedicalTreatment = null };
                AppendEvent("medical_treatment_interrupted", person.InhabitantId);
                continue;
            }
            if (treatment.Kind == "bandage")
                society.Apply(checkpoint => SocietyFixture.RecoverHealth(checkpoint, person.InhabitantId, CareContent.BandageHealthPerTick));
            var survival = person.Survival ?? new();
            if (treatment.Kind == "medicine")
                survival = survival with { IllnessBasisPoints = Math.Max(0, survival.IllnessBasisPoints - CareContent.MedicineIllnessPerTick) };
            var remaining = treatment.RemainingTicks - 1;
            inhabitants[person.InhabitantId] = person with
            {
                Survival = survival,
                MedicalTreatment = remaining == 0 ? null : treatment with { LastProcessedTick = WorldTick, RemainingTicks = remaining },
            };
            if (remaining == 0) AppendEvent("medical_treatment_completed", person.InhabitantId);
        }
    }

    private IEnumerable<(string Patient, string Kind)> MedicalNeeds(string caregiver)
    {
        foreach (var person in inhabitants.Values.OrderBy(person => person.InhabitantId == caregiver ? 0 : 1)
                     .ThenBy(person => person.InhabitantId, StringComparer.Ordinal))
        {
            if (person.MedicalTreatment is not null || !HasMedicalPermission(caregiver, person.InhabitantId)) continue;
            if ((person.Survival?.IllnessBasisPoints ?? 0) >= 2_500) yield return (person.InhabitantId, "medicine");
            if (society.Checkpoint.GetInhabitant(person.InhabitantId).HealthBasisPoints < 8_000) yield return (person.InhabitantId, "bandage");
        }
    }

    private void AddMedicalCareCandidates(List<CognitionCandidate> candidates, string actor)
    {
        if (!AdultResident(actor)) return;
        foreach (var caregiver in inhabitants[actor].MedicalCaregiverIds ?? [])
            candidates.Add(new("medical_revoke:" + caregiver, "Withdraw this person's permission to provide medical care.", 110));
        if (ClinicBandageStockNeed(actor) is { } stockNeed)
            candidates.Add(new("medical_stock:bandage", "Carry household bandages into its Clinic for patients.", 27, stockNeed.Clinic.InstanceId));
        foreach (var need in MedicalNeeds(actor))
        {
            if (MedicalSupplyAtHand(actor, need.Kind) is not null)
                candidates.Add(new($"medical_treat:{need.Patient}:{need.Kind}",
                    $"Bring a {need.Kind} to {society.Checkpoint.GetInhabitant(need.Patient).Name} for gradual recovery.", 7, need.Patient));
            else if (CarryingRoom(actor) > 0 && SharedItem(need.Kind, actor) is { } supply)
                candidates.Add(new("medical_collect:" + need.Kind,
                    $"Visit household care stock and collect a {need.Kind} for the patient.", 8, supply.StorageBuildingId));
        }
        var patient = inhabitants[actor];
        if (patient.MedicalTreatment is null && ((patient.Survival?.IllnessBasisPoints ?? 0) >= 2_500 ||
            society.Checkpoint.GetInhabitant(actor).HealthBasisPoints < 8_000))
            foreach (var other in inhabitants.Values.Where(person => person.InhabitantId != actor && AdultResident(person.InhabitantId) &&
                         HouseholdFor(person.InhabitantId) == HouseholdFor(actor) && !HasMedicalPermission(person.InhabitantId, actor)))
                candidates.Add(new("medical_allow:" + other.InhabitantId, "Ask this household adult to provide medical care.", 32, other.InhabitantId));
    }

    private void ApplyMedicalCandidate(string actor, PlaytestInhabitantState person, string candidate)
    {
        if (candidate.StartsWith("medical_stock:", StringComparison.Ordinal))
        {
            StockClinicBandages(actor, person);
        }
        else if (candidate.StartsWith("medical_revoke:", StringComparison.Ordinal))
        {
            var caregiver = candidate[15..];
            inhabitants[actor] = person with
            {
                MedicalCaregiverIds = person.MedicalCaregiverIds?.Where(id => id != caregiver).ToArray(),
                MedicalTreatment = person.MedicalTreatment?.CaregiverId == caregiver ? null : person.MedicalTreatment,
            };
            AppendEvent("medical_care_revoked", $"{actor}|{caregiver}");
        }
        else if (candidate.StartsWith("medical_allow:", StringComparison.Ordinal))
        {
            // This action is the patient's own ordinary decision, not a caregiver granting themselves access.
            var caregiver = candidate[14..];
            if (!AdultResident(caregiver) || HouseholdFor(caregiver) != HouseholdFor(actor)) return;
            var ids = (person.MedicalCaregiverIds ?? []).Append(caregiver).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Take(16).ToArray();
            inhabitants[actor] = person with { MedicalCaregiverIds = ids };
            checkpointSchemaVersion = StateSchemaVersion;
            AppendEvent("medical_care_allowed", $"{actor}|{caregiver}");
        }
        else if (candidate.StartsWith("medical_collect:", StringComparison.Ordinal))
            CollectEquipment(actor, person, candidate[16..]);
        else if (candidate.StartsWith("medical_treat:", StringComparison.Ordinal))
        {
            var separator = candidate.LastIndexOf(':');
            var patient = candidate[14..separator];
            var kind = candidate[(separator + 1)..];
            if (!inhabitants.TryGetValue(patient, out var target) || !HasMedicalPermission(actor, patient)) return;
            if (!IsWithinInteractionRange(person.Position, target.Position, 1))
                MoveToward(actor, person, target.Position, "medical_care", 1);
            else _ = TreatPatientCore(actor, patient, kind);
        }
    }

    private (PlacedBuilding Clinic, InventoryLot Stock, int Missing)? ClinicBandageStockNeed(string actor)
    {
        if (HouseholdFor(actor) is not { } household || HouseholdBuildingWithTag(household, "clinic") is not { } clinic ||
            StorageRoom(clinic.InstanceId) <= 0) return null;
        var amount = society.Checkpoint.Inventory.Lots.Where(lot => lot.OwnerId == household &&
            lot.StorageBuildingId == clinic.InstanceId && lot.ItemKind == "bandage").Sum(AvailableLotQuantity);
        var incoming = society.Checkpoint.Inventory.Lots.Where(lot => lot.DeliveryBuildingId == clinic.InstanceId &&
            lot.ItemKind == "bandage").Sum(AvailableLotQuantity);
        var missing = 4 - amount - incoming;
        if (missing <= 0) return null;
        var stock = society.Checkpoint.Inventory.Lots.Where(lot => lot.ItemKind == "bandage" &&
            lot.ContainerLotId is null && lot.GroundPosition is null && lot.DeliveryBuildingId is null &&
            AvailableLotQuantity(lot) > 0 && (lot.OwnerId == actor && lot.StorageBuildingId is null ||
                lot.OwnerId == household && lot.StorageBuildingId is { } id && id != clinic.InstanceId &&
                worldSimulation.Buildings.Any(building => building.InstanceId == id && building.HouseholdId == household)))
            .Where(lot => lot.OwnerId == actor || CarryingRoom(actor) > 0 && CanReachSharedItem(actor, lot))
            .OrderBy(lot => lot.OwnerId == actor ? 0 : 1).ThenBy(lot => lot.Id, StringComparer.Ordinal).FirstOrDefault();
        return stock is null ? null : (clinic, stock, missing);
    }

    private void StockClinicBandages(string actor, PlaytestInhabitantState person)
    {
        if (ClinicBandageStockNeed(actor) is not { } need) return;
        var personal = need.Stock.OwnerId == actor;
        var destination = personal ? need.Clinic.Position : HouseholdStockPosition(need.Stock);
        var range = personal ? 0 : HouseholdStockInteractionRange(need.Stock);
        if (!IsWithinInteractionRange(person.Position, destination, range))
        {
            MoveToward(actor, person, destination, "medical_stock", range);
            return;
        }
        var quantity = Math.Min(HouseHaulLoadQuantity, Math.Min(need.Missing, AvailableLotQuantity(need.Stock)));
        quantity = Math.Min(quantity, personal ? StorageRoom(need.Clinic.InstanceId) : CarryingRoom(actor));
        if (quantity <= 0) return;
        ApplyInventoryTransition(inventory => InventoryFixture.Transfer(inventory,
            $"clinic-bandages:{WorldTick}:{actor}", need.Stock.OwnerId, personal ? need.Clinic.HouseholdId! : actor,
            need.Stock.Id, quantity, personal ? "clinic_supplied" : "clinic_supply_collected",
            personal ? need.Clinic.InstanceId : null, personal ? null : need.Clinic.InstanceId));
        AppendEvent(personal ? "clinic_supplied" : "clinic_supply_collected", $"{actor}|bandage|{quantity}|{need.Clinic.InstanceId}");
    }

    private static void ValidateMedicalCare(PrivateWorldRuntimeState state)
    {
        var living = state.Inhabitants.Select(person => person.InhabitantId).ToHashSet(StringComparer.Ordinal);
        foreach (var person in state.Inhabitants)
        {
            if (person.MedicalCaregiverIds is { } ids && (ids.Count > 16 || ids.Count != ids.Distinct(StringComparer.Ordinal).Count() ||
                ids.Any(id => id == person.InhabitantId || !state.Society.Society.Inhabitants.Any(item => item.Id == id))))
                throw new InvalidDataException("Medical permission must name distinct existing caregivers.");
            if (person.MedicalTreatment is not { } treatment) continue;
            if (treatment.Kind is not ("bandage" or "medicine") || !living.Contains(treatment.CaregiverId) ||
                treatment.CaregiverId != person.InhabitantId &&
                person.MedicalCaregiverIds?.Contains(treatment.CaregiverId, StringComparer.Ordinal) != true &&
                !state.Society.Society.Relationships.Any(edge => edge.Type == SocietyRelationshipType.Caregiver &&
                    edge.State == SocietyRelationshipState.Accepted && edge.ProposerId == treatment.CaregiverId &&
                    edge.TargetId == person.InhabitantId && edge.EffectiveTick <= state.Society.Society.WorldTick) ||
                treatment.StartedTick < 0 || treatment.StartedTick > state.Society.Society.WorldTick ||
                treatment.LastProcessedTick < treatment.StartedTick || treatment.LastProcessedTick > state.Society.Society.WorldTick ||
                treatment.RemainingTicks is < 1 or > CareContent.TreatmentTicks ||
                treatment.RemainingTicks != CareContent.TreatmentTicks - (treatment.LastProcessedTick - treatment.StartedTick) ||
                !state.Society.Society.Inventory.Reservations.Any(reservation => reservation.Id == treatment.DoseReservationId &&
                    reservation.LotId == treatment.SupplyLotId && reservation.Quantity == 1 && reservation.State == InventoryReservationState.Completed))
                throw new InvalidDataException("Medical treatment has invalid physical supply, work or patient state.");
        }
        if (state.SchemaVersion < 32 && state.Inhabitants.Any(person => person.MedicalTreatment is not null || person.MedicalCaregiverIds is not null))
            throw new InvalidDataException("Medical treatment and consent require private-world schema 32.");
    }
}

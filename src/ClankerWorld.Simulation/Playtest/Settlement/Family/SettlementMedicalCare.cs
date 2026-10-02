using System.Globalization;
using System.Text.Json;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private const string MedicalAllowPrefix = "medical_allow:";
    private const string MedicalRevokePrefix = "medical_revoke:";
    private const string MedicalTreatPrefix = "medical_treat:";
    private const string MedicalCollectPrefix = "medical_collect:";

    private bool LivingMedicalAdult(string actor) => AdultResident(actor) &&
        society.Checkpoint.GetInhabitant(actor).Status == SocietyInhabitantStatus.Active;

    private bool CanObserveMedicalPatient(string caregiver, string patient) =>
        inhabitants.TryGetValue(caregiver, out var actor) && inhabitants.TryGetValue(patient, out var other) &&
        (caregiver == patient || IsWithinInteractionRange(actor.Position, other.Position, ResourceInteractionRange));

    private bool HasMedicalPermission(string caregiver, string patient) =>
        MedicalPermission(society.Checkpoint, inhabitants[patient], caregiver);

    private static bool MedicalPermission(SocietyCheckpoint checkpoint, PlaytestInhabitantState patient,
        string caregiver)
    {
        var recipient = checkpoint.GetInhabitant(patient.InhabitantId);
        if (recipient.AgeBand is SocietyAgeBand.Adult or SocietyAgeBand.Elder)
            return caregiver == patient.InhabitantId ||
                patient.MedicalConsent?.CaregiverIds.Contains(caregiver, StringComparer.Ordinal) == true;
        return checkpoint.Relationships.Any(edge => edge.Type == SocietyRelationshipType.Caregiver &&
            edge.State == SocietyRelationshipState.Accepted && edge.EffectiveTick <= checkpoint.WorldTick &&
            edge.ProposerId == caregiver && edge.TargetId == patient.InhabitantId);
    }

    private static string MedicalDosePurpose(string caregiver, string patient, string kind, string owner,
        long startedTick) => "medical_treatment:" + JsonSerializer.Serialize(new[]
        { caregiver, patient, kind, owner, startedTick.ToString(CultureInfo.InvariantCulture) });

    // Permission is handled only at fresh admission. Continuing intentions and
    // ApplyMedicalCandidate deliberately have no permission-changing branch.
    private bool ApplyMedicalConsentDecision(SocietyCognitionDispatchResult decision)
    {
        var selected = decision.Admission.Intention;
        if (selected is null || !(selected.CandidateId.StartsWith(MedicalAllowPrefix, StringComparison.Ordinal) ||
            selected.CandidateId.StartsWith(MedicalRevokePrefix, StringComparison.Ordinal))) return false;
        if (!decision.Admission.Accepted || decision.Admission.FellBack ||
            selected.Provider != DecisionProviderKind.LargeLanguageModel ||
            selected.InhabitantId != decision.InhabitantId ||
            PendingInstructionFor(decision.InhabitantId)?.Kind == OwnerInstructionKind.MustDo) return true;
        var allowed = selected.CandidateId.StartsWith(MedicalAllowPrefix, StringComparison.Ordinal);
        var caregiver = selected.CandidateId[(allowed ? MedicalAllowPrefix.Length : MedicalRevokePrefix.Length)..];
        ChangeMedicalConsent(decision.InhabitantId, caregiver, allowed);
        return true;
    }

    private void ChangeMedicalConsent(string patient, string caregiver, bool allowed)
    {
        if (!LivingMedicalAdult(patient) || patient == caregiver) return;
        var person = inhabitants[patient];
        var existing = person.MedicalConsent?.CaregiverIds ?? [];
        if (allowed)
        {
            if (!LivingMedicalAdult(caregiver) || !CanObserveMedicalPatient(patient, caregiver) ||
                existing.Contains(caregiver, StringComparer.Ordinal)) return;
        }
        else if (!existing.Contains(caregiver, StringComparer.Ordinal)) return;
        var ids = existing.Where(id => id != caregiver && (!allowed || LivingMedicalAdult(id))).ToList();
        if (allowed)
        {
            if (ids.Count >= MedicalCareRules.MaximumNamedCaregivers) return;
            ids.Add(caregiver);
        }
        if (!allowed && person.MedicalTreatment is { } interrupted && interrupted.CaregiverId == caregiver)
            CloseMedicalDose(patient, interrupted);
        inhabitants[patient] = person with
        {
            MedicalConsent = ids.Count == 0 ? null : new(ids.Order(StringComparer.Ordinal).ToArray()),
            MedicalTreatment = !allowed && person.MedicalTreatment?.CaregiverId == caregiver
                ? null : person.MedicalTreatment,
        };
        checkpointSchemaVersion = StateSchemaVersion;
        AppendEvent(allowed ? "medical_care_allowed" : "medical_care_revoked", $"{patient}:medical_caregiver:{caregiver}");
    }

    private InventoryLot? MedicalSupplyAtHand(string caregiver) => society.Checkpoint.Inventory.Lots
        .Where(lot => lot.ItemKind == "medicine" && lot.ContainerLotId is null && lot.GroundPosition is null &&
            lot.DeliveryBuildingId is null && AvailableLotQuantity(lot) > 0 &&
            (PersonalEquipmentRules.IsCarried(lot, caregiver) ||
             lot.OwnerId == HouseholdFor(caregiver) && lot.StorageBuildingId is { } buildingId &&
             worldSimulation.Buildings.Any(building => building.InstanceId == buildingId &&
                 building.HouseholdId == lot.OwnerId) &&
             IsWithinInteractionRange(inhabitants[caregiver].Position, HouseholdStockPosition(lot),
                 HouseholdStockInteractionRange(lot))))
        .OrderBy(lot => lot.OwnerId == caregiver ? 0 : 1)
        .ThenBy(lot => lot.Id, StringComparer.Ordinal).FirstOrDefault();

    public MedicalCareResult TreatPatient(string caregiver, string patient, string kind)
    {
        gate.Wait();
        try { return TreatPatientCore(caregiver, patient, kind); }
        finally { gate.Release(); }
    }

    private MedicalCareResult TreatPatientCore(string caregiver, string patient, string kind)
    {
        if (kind != "medicine") return new(false, "Injury treatment is not available yet; medicine supports illness recovery.");
        if (!LivingMedicalAdult(caregiver) || !CanObserveMedicalPatient(caregiver, patient) ||
            society.Checkpoint.GetInhabitant(patient).Status != SocietyInhabitantStatus.Active ||
            !HasMedicalPermission(caregiver, patient))
            return new(false, "Provide care to yourself or an accepted patient beside you.");
        var person = inhabitants[patient];
        if (person.MedicalTreatment is not null) return new(false, "A consumed dose is already being applied gradually.");
        if ((person.Survival?.IllnessBasisPoints ?? 0) == 0) return new(false, "This patient does not need medicine.");
        if (MedicalSupplyAtHand(caregiver) is not { } supply)
            return new(false, "Bring usable, unreserved medicine; another household's stock requires an agreed purchase.");
        var id = $"medical-dose:{WorldTick}:{nextEventId}:{caregiver}:{patient}";
        ApplyInventoryTransition(inventory => InventoryFixture.ConsumeReservation(InventoryFixture.Reserve(inventory,
            id, supply.OwnerId, supply.Id, 1,
            MedicalDosePurpose(caregiver, patient, kind, supply.OwnerId, WorldTick), WorldTick), id));
        inhabitants[patient] = person with
        {
            MedicalTreatment = new(caregiver, supply.Id, supply.OwnerId, id, kind, WorldTick, WorldTick,
                MedicalCareRules.TreatmentTicks),
        };
        checkpointSchemaVersion = StateSchemaVersion;
        AppendEvent("medical_treatment_started", $"{patient}:medicine:{caregiver}:{supply.Id}");
        return new(true);
    }

    // Keep the consumed reservation as the authoritative receipt. Closing its
    // medical purpose never releases or refunds goods, and permanently prevents
    // the spent effect from being reattached after permission is renewed.
    private void CloseMedicalDose(string patient, MedicalTreatmentState treatment)
    {
        var purpose = MedicalDosePurpose(treatment.CaregiverId, patient, treatment.Kind,
            treatment.SupplyOwnerId, treatment.StartedTick);
        ApplyInventoryTransition(inventory =>
        {
            var receipt = inventory.GetReservation(treatment.DoseReservationId);
            if (receipt.State != InventoryReservationState.Completed || receipt.Purpose != purpose)
                throw new InvalidDataException("Only the original consumed medical dose can be closed.");
            return inventory with
            {
                Reservations = inventory.Reservations.Select(item => item.Id == receipt.Id
                    ? item with { Purpose = purpose + ":closed" } : item).ToArray(),
            };
        });
    }

    private void AdvanceMedicalTreatments()
    {
        foreach (var person in inhabitants.Values.OrderBy(person => person.InhabitantId, StringComparer.Ordinal).ToArray())
        {
            if (person.MedicalTreatment is not { } treatment) continue;
            if (society.Checkpoint.GetInhabitant(person.InhabitantId).Status != SocietyInhabitantStatus.Active ||
                !LivingMedicalAdult(treatment.CaregiverId) || !HasMedicalPermission(treatment.CaregiverId, person.InhabitantId))
            {
                CloseMedicalDose(person.InhabitantId, treatment);
                inhabitants[person.InhabitantId] = person with { MedicalTreatment = null };
                AppendEvent("medical_treatment_interrupted", person.InhabitantId);
                continue;
            }
            if (treatment.LastProcessedTick >= WorldTick) continue;
            var survival = person.Survival ?? new();
            var remaining = treatment.RemainingTicks - 1;
            if (remaining == 0) CloseMedicalDose(person.InhabitantId, treatment);
            inhabitants[person.InhabitantId] = person with
            {
                Survival = survival with { IllnessBasisPoints = Math.Max(0,
                    survival.IllnessBasisPoints - MedicalCareRules.MedicineIllnessPerTick) },
                MedicalTreatment = remaining == 0 ? null : treatment with
                { LastProcessedTick = WorldTick, RemainingTicks = remaining },
            };
            if (remaining == 0) AppendEvent("medical_treatment_completed", person.InhabitantId);
        }
    }

    private IEnumerable<string> ObservableMedicalNeeds(string caregiver) => inhabitants.Values
        .Where(person => CanObserveMedicalPatient(caregiver, person.InhabitantId) &&
            HasMedicalPermission(caregiver, person.InhabitantId) && person.MedicalTreatment is null &&
            (person.Survival?.IllnessBasisPoints ?? 0) >= 2_500)
        .OrderBy(person => person.InhabitantId == caregiver ? 0 : 1)
        .ThenBy(person => person.InhabitantId, StringComparer.Ordinal).Select(person => person.InhabitantId);

    private bool MedicalSupplyWanted(string actor, string kind) => kind == "medicine" && LivingMedicalAdult(actor) &&
        ObservableMedicalNeeds(actor).Any() && MedicalSupplyAtHand(actor) is null && SharedItem(kind, actor) is null;

    private void AddMedicalCareCandidates(List<CognitionCandidate> candidates, string actor)
    {
        if (!LivingMedicalAdult(actor)) return;
        foreach (var caregiver in inhabitants[actor].MedicalConsent?.CaregiverIds ?? [])
            candidates.Add(new(MedicalRevokePrefix + caregiver,
                $"Withdraw {society.Checkpoint.GetInhabitant(caregiver).Name}'s permission to provide medical care.", 110));
        var supply = MedicalSupplyAtHand(actor);
        foreach (var patient in ObservableMedicalNeeds(actor))
        {
            if (supply is not null)
                candidates.Add(new(MedicalTreatPrefix + patient + ":medicine",
                    "Apply one dose of medicine to support gradual illness recovery.", 7, patient,
                    society.Checkpoint.GetInhabitant(patient).Name));
        }
        if (supply is null && ObservableMedicalNeeds(actor).Any() && FreeCarryCapacity(actor) > 0 &&
            SharedItem("medicine", actor) is { } shared)
            candidates.Add(new(MedicalCollectPrefix + "medicine", "Visit accessible care stock and collect one real dose of medicine.",
                8, shared.StorageBuildingId));
        var existing = inhabitants[actor].MedicalConsent?.CaregiverIds ?? [];
        if (existing.Count(LivingMedicalAdult) >= MedicalCareRules.MaximumNamedCaregivers) return;
        foreach (var other in inhabitants.Keys.Order(StringComparer.Ordinal).Where(other => other != actor &&
            LivingMedicalAdult(other) && CanObserveMedicalPatient(actor, other) && !existing.Contains(other, StringComparer.Ordinal)))
            candidates.Add(new(MedicalAllowPrefix + other,
                $"Allow {society.Checkpoint.GetInhabitant(other).Name} to provide medical care to you. Your personal choice is required.",
                110, other, society.Checkpoint.GetInhabitant(other).Name));
    }

    private void ApplyMedicalCandidate(string actor, PlaytestInhabitantState person, string candidate)
    {
        if (!LivingMedicalAdult(actor)) return;
        if (candidate == MedicalCollectPrefix + "medicine")
        {
            if (ObservableMedicalNeeds(actor).Any()) CollectEquipment(actor, person, "medicine");
            return;
        }
        if (!candidate.StartsWith(MedicalTreatPrefix, StringComparison.Ordinal)) return;
        var separator = candidate.LastIndexOf(':');
        if (separator <= MedicalTreatPrefix.Length) return;
        _ = TreatPatientCore(actor, candidate[MedicalTreatPrefix.Length..separator], candidate[(separator + 1)..]);
    }

    public string? MedicalCareNote(string actor)
    {
        gate.Wait();
        try { return MedicalCareNoteCore(actor); }
        finally { gate.Release(); }
    }

    private string? MedicalCareNoteCore(string actor)
    {
        if (!inhabitants.TryGetValue(actor, out var person)) return null;
        return MedicalCareRules.Note(person);
    }

    private static void ValidateMedicalCare(PrivateWorldRuntimeState state) =>
        ValidateMedicalCare(state.Inhabitants, state.DeceasedInhabitants ?? [], state.Society.Society);

    private static void ValidateMedicalCare(IEnumerable<PlaytestInhabitantState> living,
        IEnumerable<PlaytestDeceasedInhabitantState> deceased, SocietyCheckpoint checkpoint)
    {
        var people = checkpoint.Inhabitants.ToDictionary(person => person.Id, StringComparer.Ordinal);
        var active = living.ToDictionary(person => person.InhabitantId, StringComparer.Ordinal);
        var doses = new HashSet<string>(StringComparer.Ordinal);
        foreach (var person in active.Values)
        {
            ValidateMedicalConsent(person, people);
            if (person.MedicalTreatment is not { } treatment) continue;
            if (treatment.Kind != "medicine" || !active.ContainsKey(treatment.CaregiverId) ||
                !people.TryGetValue(treatment.CaregiverId, out var provider) || provider.Status != SocietyInhabitantStatus.Active ||
                provider.AgeBand is not (SocietyAgeBand.Adult or SocietyAgeBand.Elder) ||
                people[person.InhabitantId].Status != SocietyInhabitantStatus.Active ||
                !MedicalPermission(checkpoint, person, treatment.CaregiverId) || treatment.StartedTick < 0 ||
                treatment.StartedTick > checkpoint.WorldTick || treatment.LastProcessedTick != checkpoint.WorldTick ||
                treatment.RemainingTicks is < 1 or > MedicalCareRules.TreatmentTicks ||
                treatment.RemainingTicks != MedicalCareRules.TreatmentTicks - (treatment.LastProcessedTick - treatment.StartedTick) ||
                string.IsNullOrWhiteSpace(treatment.SupplyLotId) || string.IsNullOrWhiteSpace(treatment.SupplyOwnerId) ||
                string.IsNullOrWhiteSpace(treatment.DoseReservationId) || !doses.Add(treatment.DoseReservationId) ||
                checkpoint.Inventory.Lots.Any(lot => lot.Id == treatment.SupplyLotId && lot.ItemKind != "medicine") ||
                !checkpoint.Inventory.Reservations.Any(reservation => reservation.Id == treatment.DoseReservationId &&
                    reservation.OwnerId == treatment.SupplyOwnerId && reservation.LotId == treatment.SupplyLotId &&
                    reservation.Quantity == 1 && reservation.ExpiryTick == treatment.StartedTick &&
                    reservation.Purpose == MedicalDosePurpose(treatment.CaregiverId, person.InhabitantId, treatment.Kind,
                        treatment.SupplyOwnerId, treatment.StartedTick) && reservation.State == InventoryReservationState.Completed))
                throw new InvalidDataException("Medical treatment must match one consumed dose and its original patient and progress.");
        }
        foreach (var historical in deceased)
        {
            if (historical.LastPhysical is null)
                throw new InvalidDataException("A deceased person must retain a final physical record.");
            ValidateMedicalConsent(historical.LastPhysical, people);
            if (historical.LastPhysical.MedicalTreatment is not null)
                throw new InvalidDataException("A deceased person cannot retain an active medicine treatment.");
        }
    }

    private static void ValidateMedicalConsent(PlaytestInhabitantState person,
        Dictionary<string, SocietyInhabitant> people)
    {
        if (person.MedicalConsent is not { } consent) return;
        var ids = consent.CaregiverIds;
        if (ids is null || ids.Count is < 1 or > MedicalCareRules.MaximumNamedCaregivers ||
            ids.Count != ids.Distinct(StringComparer.Ordinal).Count() ||
            !ids.SequenceEqual(ids.Order(StringComparer.Ordinal), StringComparer.Ordinal) ||
            !people.TryGetValue(person.InhabitantId, out var recipient) ||
            recipient.AgeBand is not (SocietyAgeBand.Adult or SocietyAgeBand.Elder) ||
            ids.Any(id => id == person.InhabitantId || !people.TryGetValue(id, out var caregiver) ||
                caregiver.AgeBand is not (SocietyAgeBand.Adult or SocietyAgeBand.Elder)))
            throw new InvalidDataException("Medical permission must name distinct known adult caregivers.");
    }
}

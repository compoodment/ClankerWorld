namespace ClankerWorld.Simulation.Playtest;

public sealed record MedicalConsentState(IReadOnlyList<string> CaregiverIds);

/// <summary>A consumed physical dose and its remaining gradual effect, rather than a second supply store.</summary>
public sealed record MedicalTreatmentState(string CaregiverId, string SupplyLotId, string SupplyOwnerId,
    string DoseReservationId, string Kind, long StartedTick, long LastProcessedTick, int RemainingTicks);

public sealed record MedicalCareResult(bool Applied, string? Failure = null);

/// <summary>Provisional medicine effects. Injury treatment is deferred.</summary>
public static class MedicalCareRules
{
    public const int TreatmentTicks = 20;
    public const int MedicineIllnessPerTick = 75;
    public const int MaximumNamedCaregivers = 16;

    /// <summary>Only the person's own care state; safe for a captured owner or personal-model snapshot.</summary>
    public static string? Note(PlaytestInhabitantState person)
    {
        if (person.MedicalTreatment is null && person.MedicalConsent is null &&
            (person.Survival?.IllnessBasisPoints ?? 0) == 0) return null;
        var note = person.MedicalTreatment is null
            ? "No medicine treatment is underway." : "Medicine is being applied gradually.";
        return person.MedicalConsent is { } consent ? note +
            $" Medical care is permitted from {consent.CaregiverIds.Count} named adults." : note;
    }
}

using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Tests;

public sealed class MedicalConsentIntegrityTests
{
    [Fact]
    public void AConsumedDoseCannotAppearInTwoActiveTreatmentRecords()
    {
        using var world = Ready(out var caregiver, out var patient);
        Assert.True(world.AllowMedicalCare(patient, caregiver, true).Applied);
        Assert.True(world.TreatPatient(caregiver, patient, "bandage").Applied);
        var state = world.ExportState();
        var treatment = state.Inhabitants.Single(person => person.InhabitantId == patient).MedicalTreatment!;
        var duplicated = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == caregiver
            ? person with { MedicalTreatment = treatment } : person).ToArray()
        };
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(duplicated));
        Assert.Equal(1, world.Society.Inventory.GetLot("integrity-bandage").Quantity);
    }

    [Fact]
    public async Task ProcessedRecoveryCannotBeRewoundWhileKeepingTheOldConsumedDose()
    {
        using var world = Ready(out var caregiver, out var patient);
        Assert.True(world.AllowMedicalCare(patient, caregiver, true).Applied);
        Assert.True(world.TreatPatient(caregiver, patient, "bandage").Applied);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var state = world.ExportState();
        var treatment = state.Inhabitants.Single(person => person.InhabitantId == patient).MedicalTreatment!;
        Assert.Equal(19, treatment.RemainingTicks);
        var rewound = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == patient
            ? person with { MedicalTreatment = treatment with { RemainingTicks = 20, LastProcessedTick = treatment.StartedTick } }
            : person).ToArray()
        };
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(rewound));
    }

    [Theory]
    [InlineData("kind")]
    [InlineData("patient")]
    [InlineData("start")]
    [InlineData("purpose")]
    public async Task CompletedDoseIsBoundToItsOriginalPatientKindAndStart(string altered)
    {
        using var world = Ready(out var caregiver, out var patient);
        Assert.True(world.AllowMedicalCare(patient, caregiver, true).Applied);
        Assert.True(world.TreatPatient(caregiver, patient, "bandage").Applied);
        if (altered == "start") Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var state = world.ExportState();
        var treatment = state.Inhabitants.Single(person => person.InhabitantId == patient).MedicalTreatment!;
        if (altered == "kind") state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == patient
            ? person with { MedicalTreatment = treatment with { Kind = "medicine" } } : person).ToArray()
        };
        else if (altered == "patient") state = state with
        {
            Inhabitants = state.Inhabitants.Select(person =>
            person.InhabitantId == patient ? person with { MedicalTreatment = null } : person.InhabitantId == caregiver
                ? person with { MedicalTreatment = treatment } : person).ToArray()
        };
        else if (altered == "start") state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == patient
            ? person with { MedicalTreatment = treatment with { StartedTick = world.WorldTick, RemainingTicks = 20 } } : person).ToArray()
        };
        else state = state with
        {
            Society = state.Society with
            {
                Society = state.Society.Society with
                {
                    Inventory = state.Society.Society.Inventory with
                    {
                        Reservations = state.Society.Society.Inventory.Reservations.Select(reservation =>
                    reservation.Id == treatment.DoseReservationId ? reservation with { Purpose = "construction" } : reservation).ToArray()
                    }
                }
            }
        };
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(state));
    }

    [Fact]
    public async Task AnAdultCaregiverRelationshipNeedsExplicitMedicalConsentAndCannotBypassRevocation()
    {
        using var initial = Ready(out var caregiver, out var patient);
        var state = await AcceptedCaregiver(initial.ExportState(), caregiver, patient);
        using var world = PrivateWorldRuntime.Restore(state, _ => new ActionCoverageRecorder(true));
        Assert.False(world.TreatPatient(caregiver, patient, "bandage").Applied);
        Assert.Equal(2, world.Society.Inventory.GetLot("integrity-bandage").Quantity);
        Assert.True(world.AllowMedicalCare(patient, caregiver, true).Applied);
        Assert.True(world.TreatPatient(caregiver, patient, "bandage").Applied);
        Assert.True(world.AllowMedicalCare(patient, caregiver, false).Applied);
        Assert.Null(world.Inhabitants.Single(person => person.InhabitantId == patient).MedicalTreatment);
        Assert.False(world.TreatPatient(caregiver, patient, "bandage").Applied);
        Assert.Equal(1, world.Society.Inventory.GetLot("integrity-bandage").Quantity);
        using var loaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        Assert.False(loaded.TreatPatient(caregiver, patient, "bandage").Applied);
    }

    [Fact]
    public async Task ADependentCanReceiveCareThroughItsActualAcceptedCaregiverRelationship()
    {
        using var initial = Ready(out var caregiver, out var patient);
        var state = initial.ExportState();
        var config = state.Society.Society.Config;
        var age = config.DayLifecycle?.ChildStartDay ?? config.InfantYears;
        var birth = state.Society.Society.LifeTickAt(0) - config.TicksPerLifecycleAge * age;
        state = state with
        {
            Society = state.Society with
            {
                Society = state.Society.Society with
                {
                    Inhabitants = state.Society.Society.Inhabitants.Select(person => person.Id == patient ? person with
                    {
                        AgeBand = SocietyAgeBand.Child,
                        BirthTick = birth,
                        BirthLifeTick = state.Society.Society.LifeClock is null ? null : birth,
                        LastLifecycleYearChecked = age
                    } : person).ToArray()
                }
            },
            TownCouncils = state.TownCouncils!.Select(council => council with
            { MemberIds = council.MemberIds.Where(id => id != patient).ToArray() }).ToArray()
        };
        state = await AcceptedCaregiver(state, caregiver, patient);
        using var world = PrivateWorldRuntime.Restore(state, _ => new ActionCoverageRecorder(true));
        Assert.True(world.TreatPatient(caregiver, patient, "bandage").Applied);
        Assert.Equal(1, world.Society.Inventory.GetLot("integrity-bandage").Quantity);
        using var loaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        Assert.Equal(caregiver, loaded.Inhabitants.Single(person => person.InhabitantId == patient).MedicalTreatment!.CaregiverId);
        loaded.Validate();
    }

    private static PrivateWorldRuntime Ready(out string caregiver, out string patient)
    {
        using var initial = NormalPathWorld.CreateGenerated("medical-consent-integrity", _ => new ActionCoverageRecorder(true));
        var state = initial.ExportState();
        var house = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == "first-town-house-a");
        var members = state.Society.Society.Inhabitants.Where(person => person.HouseholdId == house.HouseholdId).ToArray();
        var caregiverId = caregiver = members[0].Id;
        var patientId = patient = members[1].Id;
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "integrity-bandage", "bandage", caregiverId, 2);
        return PrivateWorldRuntime.Restore(state with
        {
            Survival = new(0, []),
            Society = state.Society with
            {
                Society = state.Society.Society with
                {
                    Inventory = inventory,
                    Inhabitants = state.Society.Society.Inhabitants.Select(person => person.Id == patientId
                        ? person with { HealthBasisPoints = 4_000 } : person).ToArray()
                }
            },
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == caregiverId || person.InhabitantId == patientId
                ? person with { Position = house.Position, HungerBasisPoints = 10_000, Survival = new() } : person).ToArray(),
        }, _ => new ActionCoverageRecorder(true));
    }

    private static async Task<PrivateWorldRuntimeState> AcceptedCaregiver(PrivateWorldRuntimeState state, string caregiver, string patient)
    {
        using var society = SocietyWorldRuntime.Restore(state.Society);
        society.Apply(checkpoint => SocietyFixture.ProposeRelationship(checkpoint,
            new("actual-medical-caregiver", 1, SocietyRelationshipType.Caregiver, caregiver, patient, checkpoint.WorldTick)));
        society.Apply(checkpoint => SocietyFixture.AcceptRelationship(checkpoint, "actual-medical-caregiver", 1, patient));
        Assert.Equal(SocietyRelationshipState.Accepted, society.Checkpoint.GetRelationship("actual-medical-caregiver").State);
        using var world = PrivateWorldRuntime.Restore(state with { Society = society.ExportState() }, _ => new ActionCoverageRecorder(true));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.True(world.Society.GetRelationship("actual-medical-caregiver").EffectiveTick <= world.WorldTick);
        return world.ExportState();
    }
}

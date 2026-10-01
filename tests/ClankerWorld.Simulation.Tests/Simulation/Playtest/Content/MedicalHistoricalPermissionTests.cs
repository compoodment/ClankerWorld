using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Tests;

public sealed class MedicalHistoricalPermissionTests
{
    [Fact]
    public async Task APatientCanRevokeAnActualDeceasedCaregiverAfterReloadButCannotGrantThemCare()
    {
        using var initial = Ready();
        var patient = initial.Inhabitants[0].InhabitantId;
        var caregiver = initial.Inhabitants[1].InhabitantId;
        Assert.True(initial.AllowMedicalCare(patient, caregiver, true).Applied);
        using var world = PrivateWorldRuntime.Restore(DiesNextTick(initial.ExportState(), [caregiver]),
            _ => new ActionCoverageRecorder(true));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(SocietyInhabitantStatus.Dead, world.Society.GetInhabitant(caregiver).Status);
        Assert.DoesNotContain(world.Inhabitants, person => person.InhabitantId == caregiver);
        Assert.Contains(caregiver, world.Inhabitants.Single(person => person.InhabitantId == patient).MedicalCaregiverIds!);
        using var loaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        var before = PrivateWorldRuntimeCodec.Encode(loaded.ExportState());
        Assert.False(loaded.AllowMedicalCare(patient, caregiver, true).Applied);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(loaded.ExportState()));
        var revoked = loaded.AllowMedicalCare(patient, caregiver, false);
        Assert.True(revoked.Applied, revoked.Failure);
        Assert.Null(loaded.Inhabitants.Single(person => person.InhabitantId == patient).MedicalCaregiverIds);
        using var reloaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(loaded.ExportState())));
        Assert.Null(reloaded.Inhabitants.Single(person => person.InhabitantId == patient).MedicalCaregiverIds);
        reloaded.Validate();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeceasedPermissionsCannotExhaustCareSlotsForOwnerOrOrdinaryDecisions(bool ordinary)
    {
        using var initial = Ready();
        var patient = initial.Inhabitants[0].InhabitantId;
        var household = initial.Society.GetInhabitant(patient).HouseholdId;
        var house = initial.WorldSimulation.Buildings.Single(building => building.InstanceId == "first-town-house-a");
        for (var index = 0; index < 15; index++)
            Assert.Equal(household, initial.AddAgent("agent:" + (900 + index).ToString("x32", System.Globalization.CultureInfo.InvariantCulture), house.Position));
        var replacement = "agent:" + 914.ToString("x32", System.Globalization.CultureInfo.InvariantCulture);
        var deceased = initial.Inhabitants.Select(person => person.InhabitantId)
            .Where(id => id != patient && id != replacement).Take(16).ToArray();
        Assert.Equal(16, deceased.Length);
        foreach (var caregiver in deceased) Assert.True(initial.AllowMedicalCare(patient, caregiver, true).Applied);
        var full = PrivateWorldRuntimeCodec.Encode(initial.ExportState());
        Assert.False(initial.AllowMedicalCare(patient, replacement, true).Applied);
        Assert.Equal(full, PrivateWorldRuntimeCodec.Encode(initial.ExportState()));
        using var world = PrivateWorldRuntime.Restore(DiesNextTick(initial.ExportState(), deceased),
            _ => new ActionCoverageRecorder(true));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.All(deceased, id => Assert.Equal(SocietyInhabitantStatus.Dead, world.Society.GetInhabitant(id).Status));
        Assert.Equal(16, world.Inhabitants.Single(person => person.InhabitantId == patient).MedicalCaregiverIds!.Count);
        var state = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        state = state with
        {
            Society = state.Society with
            {
                Society = state.Society.Society with
                {
                    Inhabitants = state.Society.Society.Inhabitants.Select(person => person.Id == patient
                        ? person with { HealthBasisPoints = 7_000 } : person).ToArray()
                }
            }
        };
        using var loaded = PrivateWorldRuntime.Restore(state, _ => new PermissionChooser(patient, replacement));
        if (ordinary) Assert.True((await loaded.AdvanceOneTickAsync()).Advanced);
        else
        {
            var result = loaded.AllowMedicalCare(patient, replacement, true);
            Assert.True(result.Applied, result.Failure);
        }
        Assert.Equal([replacement], loaded.Inhabitants.Single(person => person.InhabitantId == patient).MedicalCaregiverIds);
        Assert.Contains(loaded.ExportState().Events, item => item.Kind == "medical_care_allowed" && item.Detail == $"{patient}|{replacement}");
        using var reloaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(loaded.ExportState())));
        Assert.Equal([replacement], reloaded.Inhabitants.Single(person => person.InhabitantId == patient).MedicalCaregiverIds);
        reloaded.Validate();
    }

    [Fact]
    public void AnAdultStillCannotGrantAnUnknownOrDependentCaregiver()
    {
        using var initial = Ready();
        var state = initial.ExportState();
        var patient = state.Inhabitants[0].InhabitantId;
        var child = state.Inhabitants[1].InhabitantId;
        var society = state.Society.Society;
        var age = society.Config.DayLifecycle!.ChildStartDay;
        var birth = society.LifeTickAt(society.WorldTick) - age * society.Config.TicksPerLifecycleAge;
        state = state with
        {
            Society = state.Society with
            {
                Society = society with
                {
                    Inhabitants = society.Inhabitants.Select(person => person.Id == child ? person with
                    { AgeBand = SocietyAgeBand.Child, BirthTick = birth, BirthLifeTick = society.LifeClock is null ? null : birth, LastLifecycleYearChecked = age }
                        : person).ToArray()
                }
            },
            TownCouncils = state.TownCouncils!.Select(council => council with
            { MemberIds = council.MemberIds.Where(id => id != child).ToArray() }).ToArray()
        };
        using var world = PrivateWorldRuntime.Restore(state);
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False(world.AllowMedicalCare(patient, child, true).Applied);
        Assert.False(world.AllowMedicalCare(patient, "unknown-caregiver", true).Applied);
        Assert.False(world.AllowMedicalCare(patient, "unknown-caregiver", false).Applied);
        Assert.False(world.AllowMedicalCare(child, patient, true).Applied);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
    }

    [Fact]
    public async Task AConsumedDoseStopsWhenItsCaregiverActuallyDiesWithoutRefundingItsPhysicalSupply()
    {
        using var initial = Ready();
        var state = initial.ExportState();
        var patient = state.Inhabitants[0].InhabitantId;
        var caregiver = state.Inhabitants[1].InhabitantId;
        var house = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == "first-town-house-a");
        state = state with
        {
            Society = state.Society with
            {
                Society = state.Society.Society with
                {
                    Inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "death-care-bandage", "bandage", caregiver, 2),
                    Inhabitants = state.Society.Society.Inhabitants.Select(person => person.Id == patient
                        ? person with { HealthBasisPoints = 4_000 } : person).ToArray()
                }
            },
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == patient || person.InhabitantId == caregiver
                ? person with { Position = house.Position } : person).ToArray()
        };
        using var setup = PrivateWorldRuntime.Restore(state, _ => new ActionCoverageRecorder(true));
        Assert.True(setup.AllowMedicalCare(patient, caregiver, true).Applied);
        Assert.True(setup.TreatPatient(caregiver, patient, "bandage").Applied);
        var dose = setup.Inhabitants.Single(person => person.InhabitantId == patient).MedicalTreatment!;
        var consumed = setup.Society.Inventory.GetReservation(dose.DoseReservationId);
        using var world = PrivateWorldRuntime.Restore(DiesNextTick(setup.ExportState(), [caregiver]),
            _ => new ActionCoverageRecorder(true));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(SocietyInhabitantStatus.Dead, world.Society.GetInhabitant(caregiver).Status);
        Assert.Null(world.Inhabitants.Single(person => person.InhabitantId == patient).MedicalTreatment);
        Assert.Equal(4_000, world.Society.GetInhabitant(patient).HealthBasisPoints);
        Assert.Equal(1, world.Society.Inventory.GetLot("death-care-bandage").Quantity);
        Assert.Equal(InventoryReservationState.Completed, world.Society.Inventory.GetReservation(dose.DoseReservationId).State);
        Assert.Equal(consumed, world.Society.Inventory.GetReservation(dose.DoseReservationId));
        using var loaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        loaded.Validate();
        Assert.Equal(consumed, loaded.Society.Inventory.GetReservation(dose.DoseReservationId));
        Assert.True(loaded.AllowMedicalCare(patient, caregiver, false).Applied);
    }

    private static PrivateWorldRuntime Ready()
    {
        using var initial = NormalPathWorld.CreateGenerated("probe-a", _ => new ActionCoverageRecorder(true));
        var state = initial.ExportState();
        return PrivateWorldRuntime.Restore(state with
        {
            Survival = new(0, []),
            Inhabitants = state.Inhabitants.Select(person => person with { HungerBasisPoints = 10_000, Survival = new() }).ToArray()
        }, _ => new ActionCoverageRecorder(true));
    }

    private static PrivateWorldRuntimeState DiesNextTick(PrivateWorldRuntimeState state, IReadOnlyList<string> actors)
    {
        var society = state.Society.Society;
        var maximum = society.Config.DayLifecycle!.MaximumDay;
        var birth = society.LifeTickAt(society.WorldTick) - maximum * society.Config.TicksPerLifecycleAge + 1;
        return state with
        {
            Society = state.Society with
            {
                Society = society with
                {
                    Inhabitants = society.Inhabitants.Select(person => actors.Contains(person.Id) ? person with
                    {
                        BirthTick = birth,
                        BirthLifeTick = society.LifeClock is null ? null : birth,
                        AgeBand = SocietyAgeBand.Elder,
                        LastLifecycleYearChecked = maximum - 1
                    } : person).ToArray()
                }
            }
        };
    }

    private sealed class PermissionChooser(string patient, string caregiver) : IDecisionProvider
    {
        private readonly DeterministicDecisionProvider chooser = new();
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var selected = request.Observation.InhabitantId == patient ? "medical_allow:" + caregiver : "safe_idle";
            var candidate = request.Observation.Candidates.FirstOrDefault(item => item.Id == selected)
                ?? request.Observation.Candidates.Single(item => item.Id == "safe_idle");
            return chooser.DecideAsync(request with { Observation = request.Observation with { Candidates = [candidate] } }, cancellationToken);
        }
    }
}

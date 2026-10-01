using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class MedicalCareTests
{
    [Fact]
    public async Task RealHouseClothBecomesBandagesThenOneCompletedOutputDoseTreatsThePatient()
    {
        using var initial = NormalPathWorld.CreateGenerated("care-production", _ => new ActionCoverageRecorder(true));
        var state = InjuredPair(initial.ExportState(), out var caregiver, out var patient);
        var house = state.WorldSimulation!.Buildings.Single(building => building.HouseholdId ==
            state.Society.Society.GetInhabitant(caregiver).HouseholdId &&
            state.WorldContent!.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId).Tags.Contains("house"));
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "actual-bandage-cloth", "cloth", house.HouseholdId!, 1,
            storageBuildingId: house.InstanceId);
        using var world = PrivateWorldRuntime.Restore(WithInventory(state, inventory), _ => new ActionCoverageRecorder(true));
        var recipe = world.WorldContent.Recipes.Single(item => item.LocalId == "house-bandages");
        var started = world.StartProduction(recipe.CanonicalId, house.InstanceId, caregiver);
        Assert.True(started.Applied, started.Failure);
        Assert.False(world.StartProduction(recipe.CanonicalId, house.InstanceId, patient).Applied);
        for (var tick = 0; tick < 4; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        using var loaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())),
            _ => new ActionCoverageRecorder(true));
        for (var tick = 0; tick < 4; tick++) Assert.True((await loaded.AdvanceOneTickAsync()).Advanced);
        var completed = loaded.WorldSimulation.ProductionJobs.Single(job => job.JobId == started.JobId);
        Assert.Equal(WorldProductionJobState.Completed, completed.State);
        Assert.DoesNotContain(loaded.Society.Inventory.Lots, lot => lot.Id == "actual-bandage-cloth");
        Assert.All(completed.InputReservationIds, id =>
            Assert.Equal(InventoryReservationState.Completed, loaded.Society.Inventory.GetReservation(id).State));
        var output = Assert.Single(loaded.Society.Inventory.Lots, lot => lot.ItemKind == "bandage" &&
            lot.Id.StartsWith(started.JobId + ":output:", StringComparison.Ordinal));
        Assert.Equal(2, output.Quantity);
        Assert.Equal(house.InstanceId, output.StorageBuildingId);
        Assert.True(loaded.AllowMedicalCare(patient, caregiver, true).Applied);
        Assert.True(loaded.TreatPatient(caregiver, patient, "bandage").Applied);
        var treatment = loaded.Inhabitants.Single(person => person.InhabitantId == patient).MedicalTreatment!;
        Assert.Equal(output.Id, treatment.SupplyLotId);
        Assert.Equal(1, loaded.Society.Inventory.GetLot(output.Id).Quantity);
        Assert.Equal(output.Id, loaded.Society.Inventory.GetReservation(treatment.DoseReservationId).LotId);
        Assert.Equal(InventoryReservationState.Completed, loaded.Society.Inventory.GetReservation(treatment.DoseReservationId).State);
        Assert.True((await loaded.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(4_050, loaded.Society.GetInhabitant(patient).HealthBasisPoints);
        loaded.Validate();
    }

    [Fact]
    public async Task AConsumedBandageHealsGraduallyAcrossReloadAndRevocationStopsTheRemainingEffect()
    {
        using var initial = NormalPathWorld.CreateGenerated("care-consent", _ => new ActionCoverageRecorder(true));
        var state = InjuredPair(initial.ExportState(), out var caregiver, out var patient);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "care-bandage", "bandage", caregiver, 2);
        using var world = PrivateWorldRuntime.Restore(WithInventory(state, inventory), _ => new ActionCoverageRecorder(true));
        Assert.False(world.TreatPatient(caregiver, patient, "bandage").Applied);
        Assert.Equal(2, world.Society.Inventory.GetLot("care-bandage").Quantity);
        Assert.True(world.AllowMedicalCare(patient, caregiver, true).Applied);
        Assert.True(world.TreatPatient(caregiver, patient, "bandage").Applied);
        Assert.False(world.TreatPatient(caregiver, patient, "bandage").Applied);
        Assert.Equal(1, world.Society.Inventory.GetLot("care-bandage").Quantity);
        Assert.Equal(4_000, world.Society.GetInhabitant(patient).HealthBasisPoints);
        for (var tick = 0; tick < 5; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(4_250, world.Society.GetInhabitant(patient).HealthBasisPoints);
        var saved = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        using var loaded = PrivateWorldRuntime.Restore(saved, _ => new ActionCoverageRecorder(true));
        loaded.Validate();
        Assert.Equal(15, loaded.Inhabitants.Single(person => person.InhabitantId == patient).MedicalTreatment!.RemainingTicks);
        var observed = new OwnerWorldObservationStore(loaded).GetSnapshot().Inhabitants.Single(person => person.Id == patient);
        Assert.Equal(4_250, observed.MedicalCare!.HealthBasisPoints);
        Assert.Equal("bandage", observed.MedicalCare.TreatmentKind);
        Assert.Equal(15, observed.MedicalCare.RemainingTicks);
        Assert.True(loaded.AllowMedicalCare(patient, caregiver, false).Applied);
        for (var tick = 0; tick < 20; tick++) Assert.True((await loaded.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(4_250, loaded.Society.GetInhabitant(patient).HealthBasisPoints);
        Assert.Null(loaded.Inhabitants.Single(person => person.InhabitantId == patient).MedicalTreatment);
        Assert.Equal(1, loaded.Society.Inventory.GetLot("care-bandage").Quantity);
        Assert.False(loaded.TreatPatient(caregiver, patient, "bandage").Applied);
    }

    [Fact]
    public async Task MedicineUsesOneActualDoseAndAddsGradualRecoveryWithoutChangingHealth()
    {
        using var initial = NormalPathWorld.CreateGenerated("care-medicine", _ => new ActionCoverageRecorder(true));
        var state = initial.ExportState();
        var actor = state.Inhabitants[0].InhabitantId;
        state = state with { Survival = new(0, []), Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
            ? person with { Survival = new(IllnessBasisPoints: 4_000), HungerBasisPoints = 10_000 } : person).ToArray() };
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "care-medicine", "medicine", actor, 1);
        using var world = PrivateWorldRuntime.Restore(WithInventory(state, inventory), _ => new ActionCoverageRecorder(true));
        using var control = PrivateWorldRuntime.Restore(state, _ => new ActionCoverageRecorder(true));
        Assert.True(world.TreatPatient(actor, actor, "medicine").Applied);
        Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.Id == "care-medicine");
        for (var tick = 0; tick < 5; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await control.AdvanceOneTickAsync()).Advanced);
        }
        Assert.Equal(5 * CareContent.MedicineIllnessPerTick,
            control.Inhabitants.Single(person => person.InhabitantId == actor).Survival!.IllnessBasisPoints -
            world.Inhabitants.Single(person => person.InhabitantId == actor).Survival!.IllnessBasisPoints);
        Assert.Equal(control.Society.GetInhabitant(actor).HealthBasisPoints, world.Society.GetInhabitant(actor).HealthBasisPoints);
        using var loaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())),
            _ => new ActionCoverageRecorder(true));
        loaded.Validate();
        Assert.Equal(15, loaded.Inhabitants.Single(person => person.InhabitantId == actor).MedicalTreatment!.RemainingTicks);
        Assert.DoesNotContain(loaded.Society.Inventory.Lots, lot => lot.Id == "care-medicine");
        for (var tick = 0; tick < 15; tick++)
        {
            Assert.True((await loaded.AdvanceOneTickAsync()).Advanced);
            Assert.True((await control.AdvanceOneTickAsync()).Advanced);
        }
        Assert.Null(loaded.Inhabitants.Single(person => person.InhabitantId == actor).MedicalTreatment);
        Assert.Equal(CareContent.TreatmentTicks * CareContent.MedicineIllnessPerTick,
            control.Inhabitants.Single(person => person.InhabitantId == actor).Survival!.IllnessBasisPoints -
            loaded.Inhabitants.Single(person => person.InhabitantId == actor).Survival!.IllnessBasisPoints);
        Assert.Single(loaded.ExportState().Events, item => item.Kind == "medical_treatment_completed" && item.Detail == actor);
    }

    [Fact]
    public void PermissionDoesNotGrantAnotherHouseholdsStockOrReservedDosesAndRemoteCareIsRefused()
    {
        using var initial = NormalPathWorld.CreateGenerated("care-boundaries", _ => new ActionCoverageRecorder(true));
        var state = InjuredPair(initial.ExportState(), out var caregiver, out var patient);
        var foreignHouse = state.WorldSimulation!.Buildings.First(building => building.HouseholdId is not null &&
            building.HouseholdId != state.Society.Society.GetInhabitant(caregiver).HouseholdId &&
            initial.WorldContent.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId).Tags.Contains("house"));
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "foreign-bandage", "bandage",
            foreignHouse.HouseholdId!, 1, storageBuildingId: foreignHouse.InstanceId);
        using var world = PrivateWorldRuntime.Restore(WithInventory(state, inventory), _ => new ActionCoverageRecorder(true));
        Assert.True(world.AllowMedicalCare(patient, caregiver, true).Applied);
        Assert.False(world.TreatPatient(caregiver, patient, "bandage").Applied);
        Assert.Equal(1, world.Society.Inventory.GetLot("foreign-bandage").Quantity);
        state = world.ExportState();
        inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "reserved-bandage", "bandage", caregiver, 1);
        inventory = InventoryFixture.Reserve(inventory, "reserved-care", caregiver, "reserved-bandage", 1, "barter", 100);
        using var reserved = PrivateWorldRuntime.Restore(WithInventory(state, inventory), _ => new ActionCoverageRecorder(true));
        Assert.False(reserved.TreatPatient(caregiver, patient, "bandage").Applied);
        Assert.Equal(InventoryReservationState.Reserved, reserved.Society.Inventory.GetReservation("reserved-care").State);
        inventory = InventoryFixture.ReleaseReservation(inventory, "reserved-care");
        var far = state.Map.Tiles.First(tile => state.Map.IsPassable(tile.Position) &&
            state.Map.FootDistance(tile.Position, state.Inhabitants.Single(person => person.InhabitantId == patient).Position) > 10).Position;
        state = state with { Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == caregiver
            ? person with { Position = far } : person).ToArray() };
        using var remote = PrivateWorldRuntime.Restore(WithInventory(state, inventory), _ => new ActionCoverageRecorder(true));
        Assert.False(remote.TreatPatient(caregiver, patient, "bandage").Applied);
        Assert.Equal(1, remote.Society.Inventory.GetLot("reserved-bandage").Quantity);
    }

    [Fact]
    public void ForgedUnconsumedDoseAndRepeatedProgressAreRefusedBySaveValidation()
    {
        using var initial = NormalPathWorld.CreateGenerated("care-invalid", _ => new ActionCoverageRecorder(true));
        var state = InjuredPair(initial.ExportState(), out var caregiver, out var patient);
        state = WithInventory(state, InventoryFixture.AddLot(state.Society.Society.Inventory, "validation-bandage", "bandage", caregiver, 1));
        using var world = PrivateWorldRuntime.Restore(state, _ => new ActionCoverageRecorder(true));
        Assert.True(world.AllowMedicalCare(patient, caregiver, true).Applied);
        Assert.True(world.TreatPatient(caregiver, patient, "bandage").Applied);
        state = world.ExportState();
        var treatment = state.Inhabitants.Single(person => person.InhabitantId == patient).MedicalTreatment!;
        var forged = state with { Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == patient
            ? person with { MedicalTreatment = treatment with { DoseReservationId = "not-consumed" } } : person).ToArray() };
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(forged));
        forged = state with { Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == patient
            ? person with { MedicalTreatment = treatment with { RemainingTicks = 19 } } : person).ToArray() };
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(forged));
    }

    private static PrivateWorldRuntimeState InjuredPair(PrivateWorldRuntimeState state, out string caregiver, out string patient)
    {
        var house = state.WorldSimulation!.Buildings.First(building => building.HouseholdId is not null &&
            state.WorldContent!.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId).Tags.Contains("house"));
        var members = state.Society.Society.Inhabitants.Where(person => person.HouseholdId == house.HouseholdId).Take(2).ToArray();
        caregiver = members[0].Id;
        patient = members[1].Id;
        var patientId = patient;
        var caregiverId = caregiver;
        return state with
        {
            Survival = new(0, []),
            Society = state.Society with { Society = state.Society.Society with
            { Inhabitants = state.Society.Society.Inhabitants.Select(person => person.Id == patientId ? person with { HealthBasisPoints = 4_000 } : person).ToArray() } },
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == patientId || person.InhabitantId == caregiverId
                ? person with { Position = house.Position, HungerBasisPoints = 10_000, Survival = new() } : person).ToArray(),
        };
    }

    private static PrivateWorldRuntimeState WithInventory(PrivateWorldRuntimeState state, InventoryCheckpoint inventory) =>
        state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } };
}

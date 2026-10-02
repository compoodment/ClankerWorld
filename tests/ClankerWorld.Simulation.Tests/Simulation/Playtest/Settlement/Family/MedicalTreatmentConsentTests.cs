using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed class MedicalTreatmentConsentTests
{
    [Fact]
    public async Task OneActualMedicineDoseAddsGradualIllnessReliefAndReplaysAfterTheSourceLotIsGone()
    {
        var state = PreparedState();
        var patient = state.Inhabitants[0].InhabitantId;
        state = WithMedicine(state, patient, 1);
        using var treated = Restore(state);
        using var untreated = Restore(state);
        var health = treated.Society.GetInhabitant(patient).HealthBasisPoints;

        Assert.True(treated.TreatPatient(patient, patient, "medicine").Applied);
        Assert.Equal(4_000, Physical(treated, patient).Survival!.IllnessBasisPoints);
        Assert.DoesNotContain(treated.Society.Inventory.Lots, lot => lot.Id == "medical-test-dose");
        var treatment = Assert.IsType<MedicalTreatmentState>(Physical(treated, patient).MedicalTreatment);
        var dose = treated.Society.Inventory.GetReservation(treatment.DoseReservationId);
        Assert.Equal((patient, "medical-test-dose", 1, InventoryReservationState.Completed),
            (dose.OwnerId, dose.LotId, dose.Quantity, dose.State));
        Assert.Equal(treated.WorldTick, dose.ExpiryTick);
        Assert.False(treated.TreatPatient(patient, patient, "medicine").Applied);

        for (var tick = 0; tick < 7; tick++)
        {
            Assert.True((await treated.AdvanceOneTickAsync()).Advanced);
            Assert.True((await untreated.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(75 * (tick + 1), Physical(untreated, patient).Survival!.IllnessBasisPoints -
                Physical(treated, patient).Survival!.IllnessBasisPoints);
        }
        var saved = PrivateWorldRuntimeCodec.Encode(treated.ExportState());
        using var resumed = Restore(PrivateWorldRuntimeCodec.Decode(saved));
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(resumed.ExportState()));
        Assert.Equal(13, Physical(resumed, patient).MedicalTreatment!.RemainingTicks);
        Assert.InRange(resumed.MedicalCareNote(patient)!.Length, 1, 256);
        for (var tick = 7; tick < 20; tick++)
        {
            Assert.True((await treated.AdvanceOneTickAsync()).Advanced);
            Assert.True((await resumed.AdvanceOneTickAsync()).Advanced);
            Assert.True((await untreated.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(treated.ExportState()),
                PrivateWorldRuntimeCodec.Encode(resumed.ExportState()));
        }
        Assert.Equal(1_500, Physical(untreated, patient).Survival!.IllnessBasisPoints -
            Physical(resumed, patient).Survival!.IllnessBasisPoints);
        Assert.Null(Physical(resumed, patient).MedicalTreatment);
        Assert.Equal(health, resumed.Society.GetInhabitant(patient).HealthBasisPoints);
        Assert.Single(resumed.ExportState().Events, item => item.Kind == "medical_treatment_completed");
        Assert.Equal(InventoryReservationState.Completed, resumed.Society.Inventory.GetReservation(dose.Id).State);
        Assert.EndsWith(":closed", resumed.Society.Inventory.GetReservation(dose.Id).Purpose, StringComparison.Ordinal);
        resumed.Validate();
    }

    [Theory]
    [InlineData(DecisionProviderKind.Deterministic, false, false)]
    [InlineData(DecisionProviderKind.Jev, false, false)]
    [InlineData(DecisionProviderKind.LargeLanguageModel, true, false)]
    [InlineData(DecisionProviderKind.LargeLanguageModel, false, true)]
    [InlineData(DecisionProviderKind.LargeLanguageModel, false, false)]
    public async Task OnlyAnExactFreshPersonalModelChoiceGrantsAdultMedicalPermission(
        DecisionProviderKind kind, bool fail, bool mustDo)
    {
        var state = PreparedState();
        var patient = state.Inhabitants[0].InhabitantId;
        var caregiver = state.Inhabitants[1].InhabitantId;
        state = WithMedicine(state, caregiver, 2);
        if (mustDo)
        {
            state = WithInventory(state, InventoryFixture.AddLot(state.Society.Society.Inventory,
                "medical-forced-berry", "berries", patient, 1)) with
            {
                Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == patient
                    ? person with { HungerBasisPoints = 9_000 } : person).ToArray(),
            };
        }
        var choice = new MedicalChoiceProvider("medical_allow:" + caregiver, kind, fail);
        using var world = Restore(state, patient, choice);
        Assert.False(world.TreatPatient(caregiver, patient, "medicine").Applied);
        OwnerInstructionReceipt? order = null;
        if (mustDo)
        {
            order = world.SubmitInstruction(new("medical-forced-order", "owner:test", patient,
                OwnerInstructionKind.MustDo, "eat food"));
            Assert.Contains(world.ExportState().Instructions!, item => item.InstructionId == order.InstructionId &&
                item.Kind == OwnerInstructionKind.MustDo && item.State == OwnerInstructionState.Queued);
            Assert.DoesNotContain(order.InstructionId, world.ExportState().CompletedInstructionIds ?? []);
        }
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Contains(choice.Offered, item => item.Id == "medical_allow:" + caregiver);
        if (order is not null)
        {
            Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.Id == "medical-forced-berry");
            Assert.Equal(10_000, Physical(world, patient).HungerBasisPoints);
            Assert.Contains(order.InstructionId, world.ExportState().CompletedInstructionIds!);
            Assert.Contains(world.ExportState().Events, item => item.Kind == "instruction_applied" &&
                item.Detail == order.InstructionId + ":consume_food");
        }
        var allowed = kind == DecisionProviderKind.LargeLanguageModel && !fail && !mustDo;
        Assert.Equal(allowed, Physical(world, patient).MedicalConsent?.CaregiverIds.Contains(caregiver) == true);
        Assert.Equal(allowed, world.TreatPatient(caregiver, patient, "medicine").Applied);
        Assert.Equal(allowed ? 1 : 2, world.Society.Inventory.GetLot("medical-test-dose").Quantity);
        Assert.Equal(allowed ? 1 : 0, world.ExportState().Events.Count(item => item.Kind == "medical_care_allowed"));
        using var replay = Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        for (var tick = 0; tick < 3; tick++) Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(allowed ? 1 : 0, replay.ExportState().Events.Count(item => item.Kind == "medical_care_allowed"));
        replay.Validate();
    }

    [Fact]
    public async Task FreshMedicalConsentAlsoRecordsTheExactOwnerSuggestionAndReplyAcrossReload()
    {
        var state = PreparedState();
        var patient = state.Inhabitants[0].InhabitantId;
        var caregiver = state.Inhabitants[1].InhabitantId;
        const string messageText = "Tell me who you would ask for medical help.";
        const string replyText = "I chose my neighbor as a caregiver.";
        var selected = "medical_allow:" + caregiver;
        var choice = new MedicalChoiceProvider(selected, observerReply: replyText);
        using var world = Restore(state, patient, choice);
        var receipt = world.SubmitInstruction(new("medical-consent-suggestion", "owner:test", patient,
            OwnerInstructionKind.Suggestive, messageText));

        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Contains(choice.Offered, candidate => candidate.Id == selected);
        var prompted = Assert.Single(choice.Guidance, message => message.InstructionId == receipt.InstructionId);
        Assert.Equal((patient, "suggestive", messageText),
            (prompted.TargetInhabitantId, prompted.Kind, prompted.Text));
        Assert.Equal([caregiver], Physical(world, patient).MedicalConsent!.CaregiverIds);
        var result = world.ExportState();
        var observed = Assert.Single(result.Instructions!, message => message.InstructionId == receipt.InstructionId);
        Assert.Equal(world.WorldTick, observed.ObservedTick);
        Assert.Equal(replyText, observed.ObserverReply);
        Assert.Contains(receipt.InstructionId, result.CompletedInstructionIds!);
        Assert.Single(result.Events, item => item.Kind == "instruction_applied" &&
            item.Detail == receipt.InstructionId + ":" + selected);

        var saved = PrivateWorldRuntimeCodec.Encode(result);
        using var replay = Restore(PrivateWorldRuntimeCodec.Decode(saved));
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        for (var tick = 0; tick < 3; tick++) Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
        var restored = replay.ExportState();
        var restoredMessage = Assert.Single(restored.Instructions!, message => message.InstructionId == receipt.InstructionId);
        Assert.Equal(observed, restoredMessage);
        Assert.Equal([caregiver], Physical(replay, patient).MedicalConsent!.CaregiverIds);
        Assert.Single(restored.CompletedInstructionIds!, id => id == receipt.InstructionId);
        Assert.Single(restored.Events, item => item.Kind == "instruction_applied" &&
            item.Detail == receipt.InstructionId + ":" + selected);
        Assert.Single(restored.Events, item => item.Kind == "medical_care_allowed");
        replay.Validate();
    }

    [Fact]
    public async Task RevocationNeedsAFreshPersonalChoiceAndStopsTheConsumedDoseWithoutRefund()
    {
        var state = PreparedState();
        var patient = state.Inhabitants[0].InhabitantId;
        var caregiver = state.Inhabitants[1].InhabitantId;
        using var granting = Restore(WithMedicine(state, caregiver, 1), patient,
            new MedicalChoiceProvider("medical_allow:" + caregiver));
        Assert.True((await granting.AdvanceOneTickAsync()).Advanced);
        Assert.True(granting.TreatPatient(caregiver, patient, "medicine").Applied);
        var active = granting.ExportState();
        var dose = Physical(granting, patient).MedicalTreatment!.DoseReservationId;
        using var routineRevoke = Restore(active, patient,
            new MedicalChoiceProvider("medical_revoke:" + caregiver, DecisionProviderKind.Jev));
        Assert.True((await routineRevoke.AdvanceOneTickAsync()).Advanced);
        Assert.Contains(caregiver, Physical(routineRevoke, patient).MedicalConsent!.CaregiverIds);
        Assert.NotNull(Physical(routineRevoke, patient).MedicalTreatment);

        using var revoking = Restore(active, patient, new MedicalChoiceProvider("medical_revoke:" + caregiver));
        Assert.True((await revoking.AdvanceOneTickAsync()).Advanced);
        Assert.Null(Physical(revoking, patient).MedicalConsent);
        Assert.Null(Physical(revoking, patient).MedicalTreatment);
        Assert.False(revoking.TreatPatient(caregiver, patient, "medicine").Applied);
        Assert.DoesNotContain(revoking.Society.Inventory.Lots, lot => lot.Id == "medical-test-dose");
        Assert.Equal(InventoryReservationState.Completed, revoking.Society.Inventory.GetReservation(dose).State);
        Assert.EndsWith(":closed", revoking.Society.Inventory.GetReservation(dose).Purpose, StringComparison.Ordinal);
        Assert.Single(revoking.ExportState().Events, item => item.Kind == "medical_care_revoked");
        using var replay = Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(revoking.ExportState())),
            patient, new MedicalChoiceProvider("medical_revoke:" + caregiver));
        for (var tick = 0; tick < 4; tick++)
        {
            Assert.True((await revoking.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(revoking.ExportState()),
                PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
            Assert.Null(Physical(replay, patient).MedicalTreatment);
        }
    }

    [Fact]
    public async Task RenewingPermissionCannotResurrectThePreviouslyInterruptedDose()
    {
        var state = PreparedState();
        var patient = state.Inhabitants[0].InhabitantId;
        var caregiver = state.Inhabitants[1].InhabitantId;
        using var grant = Restore(WithMedicine(state, caregiver, 1), patient,
            new MedicalChoiceProvider("medical_allow:" + caregiver));
        Assert.True((await grant.AdvanceOneTickAsync()).Advanced);
        Assert.True(grant.TreatPatient(caregiver, patient, "medicine").Applied);
        var consumed = Physical(grant, patient).MedicalTreatment!;
        var receipt = grant.Society.Inventory.GetReservation(consumed.DoseReservationId);
        using var revoke = Restore(grant.ExportState(), patient, new MedicalChoiceProvider("medical_revoke:" + caregiver));
        Assert.True((await revoke.AdvanceOneTickAsync()).Advanced);
        Assert.Null(Physical(revoke, patient).MedicalTreatment);
        using var renew = Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(revoke.ExportState())),
            patient, new MedicalChoiceProvider("medical_allow:" + caregiver));
        Assert.True((await renew.AdvanceOneTickAsync()).Advanced);
        Assert.Contains(caregiver, Physical(renew, patient).MedicalConsent!.CaregiverIds);
        Assert.Null(Physical(renew, patient).MedicalTreatment);
        var closed = renew.Society.Inventory.GetReservation(receipt.Id);
        Assert.Equal(receipt with { Purpose = receipt.Purpose + ":closed" }, closed);
        Assert.DoesNotContain(renew.Society.Inventory.Lots, lot => lot.Id == receipt.LotId);
        var current = renew.ExportState();
        var forged = current with
        {
            Inhabitants = current.Inhabitants.Select(person => person.InhabitantId == patient ? person with
            {
                MedicalTreatment = consumed with
                {
                    LastProcessedTick = renew.WorldTick,
                    RemainingTicks = checked(20 - (int)(renew.WorldTick - consumed.StartedTick))
                },
            } : person).ToArray(),
        };
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(forged));
        renew.Validate();
    }

    [Fact]
    public async Task EffectiveAcceptedDependentCareAuthorizesMedicineButAnAdultCareEdgeDoesNot()
    {
        var state = PreparedState();
        var patient = state.Inhabitants[0].InhabitantId;
        var caregiver = state.Inhabitants[1].InhabitantId;
        state = WithMedicine(state, caregiver, 1);
        var checkpoint = state.Society.Society;
        checkpoint = SocietyFixture.ProposeRelationship(checkpoint,
            new("medical-dependent-edge", 1, SocietyRelationshipType.Caregiver, caregiver, patient, checkpoint.WorldTick)).Checkpoint;
        checkpoint = SocietyFixture.AcceptRelationship(checkpoint, "medical-dependent-edge", 1, patient).Checkpoint;
        var accepted = WithCheckpoint(state, checkpoint);
        using var adult = Restore(accepted);
        Assert.True((await adult.AdvanceOneTickAsync()).Advanced);
        Assert.False(adult.TreatPatient(caregiver, patient, "medicine").Applied);
        Assert.Null(Physical(adult, patient).MedicalConsent);
        Assert.Equal(1, adult.Society.Inventory.GetLot("medical-test-dose").Quantity);

        var childAge = checkpoint.Config.DayLifecycle?.ChildStartDay ?? checkpoint.Config.InfantYears;
        checkpoint = WithAge(checkpoint, patient, childAge);
        using var dependent = Restore(WithCheckpoint(state, checkpoint));
        Assert.False(dependent.TreatPatient(caregiver, patient, "medicine").Applied);
        Assert.True((await dependent.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(SocietyAgeBand.Child, dependent.Society.GetInhabitant(patient).AgeBand);
        Assert.True(dependent.TreatPatient(caregiver, patient, "medicine").Applied);
        Assert.Null(Physical(dependent, patient).MedicalConsent);
        using var reloaded = Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(dependent.ExportState())));
        Assert.True((await reloaded.AdvanceOneTickAsync()).Advanced);
        Assert.NotNull(Physical(reloaded, patient).MedicalTreatment);
        reloaded.Validate();
    }

    [Fact]
    public async Task LocalAuthorizationDoesNotRevealRemotePatientsOrConsumeAnotherOwnersReservedDose()
    {
        var state = PreparedState();
        var patient = state.Inhabitants[0].InhabitantId;
        var caregiver = state.Inhabitants[1].InhabitantId;
        using var granting = Restore(WithMedicine(state, caregiver, 1), patient,
            new MedicalChoiceProvider("medical_allow:" + caregiver));
        Assert.True((await granting.AdvanceOneTickAsync()).Advanced);
        var authorized = granting.ExportState();
        var far = authorized.Map.Tiles.First(tile => authorized.Map.IsPassable(tile.Position) &&
            Math.Abs(tile.Position.X - Physical(granting, caregiver).Position.X) +
            Math.Abs(tile.Position.Y - Physical(granting, caregiver).Position.Y) > 5 &&
            !authorized.Inhabitants.Any(person => person.Position == tile.Position)).Position;
        var observer = new MedicalChoiceProvider("safe_idle");
        using var remote = Restore(authorized with
        {
            Inhabitants = authorized.Inhabitants.Select(person => person.InhabitantId == patient
                ? person with { Position = far } : person).ToArray(),
        }, caregiver, observer);
        Assert.True((await remote.AdvanceOneTickAsync()).Advanced);
        Assert.DoesNotContain(observer.Offered, candidate => candidate.Id == "medical_treat:" + patient + ":medicine");
        Assert.False(remote.TreatPatient(caregiver, patient, "medicine").Applied);
        Assert.Equal(1, remote.Society.Inventory.GetLot("medical-test-dose").Quantity);

        var inventory = InventoryFixture.Reserve(authorized.Society.Society.Inventory,
            "other-medical-use", caregiver, "medical-test-dose", 1, "independent_reserved_stock", 100);
        using var reserved = Restore(WithInventory(authorized, inventory));
        Assert.False(reserved.TreatPatient(caregiver, patient, "medicine").Applied);
        Assert.Equal(1, reserved.Society.Inventory.GetLot("medical-test-dose").Quantity);
        Assert.Equal(InventoryReservationState.Reserved, reserved.Society.Inventory.GetReservation("other-medical-use").State);
        using var foreign = Restore(WithInventory(authorized,
            authorized.Society.Society.Inventory with
            {
                Lots = authorized.Society.Society.Inventory.Lots.Select(lot => lot.Id == "medical-test-dose"
                    ? lot with { OwnerId = authorized.Inhabitants[2].InhabitantId } : lot).ToArray(),
            }));
        Assert.False(foreign.TreatPatient(caregiver, patient, "medicine").Applied);
        Assert.Equal(1, foreign.Society.Inventory.GetLot("medical-test-dose").Quantity);
    }

    [Fact]
    public async Task WithdrawingTheActualDependentCareEdgeClosesTheDoseBeforeTheTickCanBeSaved()
    {
        var state = PreparedState();
        var patient = state.Inhabitants[0].InhabitantId;
        var caregiver = state.Inhabitants[1].InhabitantId;
        state = WithMedicine(state, caregiver, 1);
        var checkpoint = state.Society.Society;
        checkpoint = WithAge(checkpoint, patient, checkpoint.Config.DayLifecycle?.ChildStartDay ?? checkpoint.Config.InfantYears);
        checkpoint = SocietyFixture.ProposeRelationship(checkpoint,
            new("medical-withdrawn-edge", 1, SocietyRelationshipType.Caregiver, caregiver, patient, checkpoint.WorldTick)).Checkpoint;
        checkpoint = SocietyFixture.AcceptRelationship(checkpoint, "medical-withdrawn-edge", 1, patient).Checkpoint;
        using var initial = Restore(WithCheckpoint(state, checkpoint));
        Assert.True((await initial.AdvanceOneTickAsync()).Advanced);
        Assert.True(initial.TreatPatient(caregiver, patient, "medicine").Applied);
        var dose = Physical(initial, patient).MedicalTreatment!.DoseReservationId;
        using var ending = Restore(initial.ExportState(), caregiver,
            new MedicalChoiceProvider("guardian_end:medical-withdrawn-edge", DecisionProviderKind.Deterministic));
        Assert.True((await ending.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(SocietyRelationshipState.Revoked, ending.Society.GetRelationship("medical-withdrawn-edge").State);
        Assert.Null(Physical(ending, patient).MedicalTreatment);
        Assert.EndsWith(":closed", ending.Society.Inventory.GetReservation(dose).Purpose, StringComparison.Ordinal);
        Assert.Equal(InventoryReservationState.Completed, ending.Society.Inventory.GetReservation(dose).State);
        Assert.DoesNotContain(ending.Society.Inventory.Lots, lot => lot.Id == "medical-test-dose");
        using var saved = Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(ending.ExportState())));
        Assert.Null(Physical(saved, patient).MedicalTreatment);
        Assert.False(saved.TreatPatient(caregiver, patient, "medicine").Applied);
        saved.Validate();
    }

    [Fact]
    public async Task CaregiverNaturalDeathInterruptsTheEffectAndHistoricalPermissionRemainsRevocable()
    {
        var state = PreparedState();
        var patient = state.Inhabitants[0].InhabitantId;
        var caregiver = state.Inhabitants[1].InhabitantId;
        using var granting = Restore(WithMedicine(state, caregiver, 1), patient,
            new MedicalChoiceProvider("medical_allow:" + caregiver));
        Assert.True((await granting.AdvanceOneTickAsync()).Advanced);
        Assert.True(granting.TreatPatient(caregiver, patient, "medicine").Applied);
        var active = granting.ExportState();
        var dose = Physical(granting, patient).MedicalTreatment!.DoseReservationId;
        var checkpoint = active.Society.Society;
        var elderAge = checkpoint.Config.DayLifecycle?.ElderStartDay ?? checkpoint.Config.ElderYears;
        checkpoint = WithAge(checkpoint, caregiver, elderAge, justBeforeNextAge: true) with
        {
            Config = checkpoint.Config with { BaseNaturalMortalityBasisPoints = 10_000, NaturalMortalitySlopeBasisPoints = 0 },
        };
        var doomed = WithCheckpoint(active, checkpoint);
        using var world = Restore(doomed);
        using var untreated = Restore(doomed with
        {
            Inhabitants = doomed.Inhabitants.Select(person => person.InhabitantId == patient
                ? person with { MedicalTreatment = null } : person).ToArray(),
        });
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.True((await untreated.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(SocietyDeathCause.NaturalAge, world.Society.GetInhabitant(caregiver).DeathCause);
        Assert.Null(Physical(world, patient).MedicalTreatment);
        Assert.Equal(Physical(untreated, patient).Survival!.IllnessBasisPoints,
            Physical(world, patient).Survival!.IllnessBasisPoints);
        Assert.Contains(caregiver, Physical(world, patient).MedicalConsent!.CaregiverIds);
        Assert.Single(world.ExportState().Events, item => item.Kind == "medical_treatment_interrupted");
        Assert.Equal(InventoryReservationState.Completed, world.Society.Inventory.GetReservation(dose).State);
        Assert.EndsWith(":closed", world.Society.Inventory.GetReservation(dose).Purpose, StringComparison.Ordinal);
        var dead = Assert.Single(world.ExportState().DeceasedInhabitants!, person => person.InhabitantId == caregiver);
        Assert.Null(dead.LastPhysical.MedicalTreatment);
        var revocation = new MedicalChoiceProvider("medical_revoke:" + caregiver);
        using var revoking = Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())),
            patient, revocation);
        // Reloading with another provider preserves the ordinary idle intention and its 300-tick decision interval.
        for (var tick = 0; tick < 301 && Physical(revoking, patient).MedicalConsent is not null; tick++)
            Assert.True((await revoking.AdvanceOneTickAsync()).Advanced);
        Assert.Contains(revocation.Offered, candidate => candidate.Id == "medical_revoke:" + caregiver);
        Assert.Single(revoking.ExportState().Events, item => item.Kind == "medical_care_revoked");
        Assert.Null(Physical(revoking, patient).MedicalConsent);
        Assert.Equal(world.Society.Inventory.GetReservation(dose), revoking.Society.Inventory.GetReservation(dose));
        Assert.DoesNotContain(revoking.Society.Inventory.Lots, lot => lot.Id == "medical-test-dose");
        Assert.NotEmpty(PrivateWorldRuntimeCodec.Encode(revoking.ExportState()));
        revoking.Validate();

        var archivedTreatment = world.ExportState() with
        {
            DeceasedInhabitants = [dead with { LastPhysical = dead.LastPhysical with
                { MedicalTreatment = Physical(granting, patient).MedicalTreatment } }],
        };
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(archivedTreatment));
        var archivedPermission = world.ExportState() with
        {
            DeceasedInhabitants = [dead with { LastPhysical = dead.LastPhysical with
                { MedicalConsent = new(["unknown-caregiver"]) } }],
        };
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(archivedPermission));
        var missingPhysical = world.ExportState() with { DeceasedInhabitants = [dead with { LastPhysical = null! }] };
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(missingPhysical));
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("patient")]
    [InlineData("progress")]
    [InlineData("start")]
    [InlineData("kind")]
    [InlineData("reservation")]
    [InlineData("duplicate")]
    [InlineData("live-kind")]
    public void CodecRejectsAReboundOrRewoundConsumedDose(string corruption)
    {
        var state = PreparedState();
        var patient = state.Inhabitants[0].InhabitantId;
        var other = state.Inhabitants[1].InhabitantId;
        using var world = Restore(WithMedicine(state, patient, corruption == "live-kind" ? 2 : 1));
        Assert.True(world.TreatPatient(patient, patient, "medicine").Applied);
        var active = world.ExportState();
        Assert.NotEmpty(PrivateWorldRuntimeCodec.Encode(active));
        var original = Physical(world, patient).MedicalTreatment!;
        var changed = corruption switch
        {
            "owner" => original with { SupplyOwnerId = other },
            "patient" => original with { CaregiverId = other },
            "progress" => original with { RemainingTicks = 19 },
            "start" => original with { StartedTick = original.StartedTick + 1 },
            "kind" => original with { Kind = "bandage" },
            "reservation" => original with { DoseReservationId = "not-consumed" },
            _ => original,
        };
        var forged = active with
        {
            Inhabitants = active.Inhabitants.Select(person => person.InhabitantId == patient
                ? person with { MedicalTreatment = changed, MedicalConsent = corruption == "patient" ? new([other]) : null }
                : corruption == "duplicate" && person.InhabitantId == other
                    ? person with { MedicalTreatment = original, MedicalConsent = new([patient]) } : person).ToArray(),
        };
        if (corruption == "live-kind") forged = WithInventory(forged, forged.Society.Society.Inventory with
        {
            Lots = forged.Society.Society.Inventory.Lots.Select(lot => lot.Id == original.SupplyLotId
                ? lot with { ItemKind = "bandage" } : lot).ToArray(),
        });
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(forged));
    }

    [Fact]
    public async Task OrdinarySelfCareConsumesMedicineWhileBandagesHaveNoUndeclaredHealthEffect()
    {
        var state = PreparedState();
        var patient = state.Inhabitants[0].InhabitantId;
        state = WithMedicine(state, patient, 1);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "medical-bandages", "bandage", patient, 2);
        var choice = new MedicalChoiceProvider("medical_treat:" + patient + ":medicine", DecisionProviderKind.Deterministic);
        using var world = Restore(WithInventory(state, inventory), patient, choice);
        Assert.False(world.TreatPatient(patient, patient, "bandage").Applied);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.NotNull(Physical(world, patient).MedicalTreatment);
        Assert.Single(world.ExportState().Events, item => item.Kind == "medical_treatment_started");
        Assert.Equal(2, world.Society.Inventory.GetLot("medical-bandages").Quantity);
        Assert.Equal(10_000, world.Society.GetInhabitant(patient).HealthBasisPoints);
        Assert.DoesNotContain(choice.Offered, candidate => candidate.Id.EndsWith(":bandage", StringComparison.Ordinal));
        Assert.Null(MedicalCareRules.Note(Physical(world, state.Inhabitants[2].InhabitantId)));
        world.Validate();
    }

    [Fact]
    public void TransferringTheRemainingMedicineDoesNotRebindTheDoseAlreadyConsumed()
    {
        var state = PreparedState();
        var patient = state.Inhabitants[0].InhabitantId;
        var recipient = state.Inhabitants[1].InhabitantId;
        using var world = Restore(WithMedicine(state, patient, 2));
        Assert.True(world.TreatPatient(patient, patient, "medicine").Applied);
        var active = world.ExportState();
        var original = Physical(world, patient).MedicalTreatment!;
        var transferred = InventoryFixture.Transfer(active.Society.Society.Inventory, "medicine-remainder-gift",
            patient, recipient, original.SupplyLotId, 1, "gift");
        Assert.Equal((recipient, "medicine", 1), (transferred.GetLot(original.SupplyLotId).OwnerId,
            transferred.GetLot(original.SupplyLotId).ItemKind, transferred.GetLot(original.SupplyLotId).Quantity));
        using var saved = Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(WithInventory(active, transferred))));
        Assert.Equal(original, Physical(saved, patient).MedicalTreatment);
        Assert.Equal(patient, saved.Society.Inventory.GetReservation(original.DoseReservationId).OwnerId);
        saved.Validate();
    }

    private static PrivateWorldRuntimeState PreparedState()
    {
        using var seed = new PrivateWorldRuntime("medical-treatment-boundaries", _ => new MedicalChoiceProvider("safe_idle", DecisionProviderKind.Deterministic));
        var state = seed.ExportState();
        var patient = state.Inhabitants[0].InhabitantId;
        var caregiver = state.Inhabitants[1].InhabitantId;
        var occupied = state.Inhabitants.Where(person => person.InhabitantId != patient && person.InhabitantId != caregiver)
            .Select(person => person.Position).ToHashSet();
        var patientPosition = state.Map.Tiles.First(tile => state.Map.IsPassable(tile.Position) && !occupied.Contains(tile.Position) &&
            state.Map.FootNeighbors(tile.Position).Any(point => state.Map.IsPassable(point) && !occupied.Contains(point))).Position;
        var caregiverPosition = state.Map.FootNeighbors(patientPosition).First(point => state.Map.IsPassable(point) &&
            !occupied.Contains(point) && point != patientPosition);
        return state with
        {
            JevEnabled = true,
            Survival = new(state.Society.Society.WorldTick, []),
            Inhabitants = state.Inhabitants.Select(person => person with
            {
                Position = person.InhabitantId == patient ? patientPosition : person.InhabitantId == caregiver ? caregiverPosition : person.Position,
                HungerBasisPoints = 10_000,
                Survival = new(IllnessBasisPoints: person.InhabitantId == patient ? 4_000 : 0),
                LastDecisionContext = null,
            }).ToArray(),
            WorldSystems = state.WorldSystems! with
            {
                RegionalWeather = null,
                Config = state.WorldSystems.Config with
                {
                    WeatherProfiles = Enum.GetValues<SeasonKind>().Select(season => new WeatherProfile(season, 1, 0, 0, 0, 0)).ToArray(),
                },
                Climate = state.WorldSystems.Climate with { Weather = WeatherKind.Clear },
            },
        };
    }

    private static SocietyCheckpoint WithAge(SocietyCheckpoint checkpoint, string actor, int age,
        bool justBeforeNextAge = false)
    {
        var birth = checkpoint.LifeTickAt(checkpoint.WorldTick) - (age + (justBeforeNextAge ? 1L : 0L)) *
            checkpoint.Config.TicksPerLifecycleAge + (justBeforeNextAge ? 1 : 0);
        return checkpoint with
        {
            Inhabitants = checkpoint.Inhabitants.Select(person => person.Id == actor ? person with
            {
                BirthTick = checkpoint.LifeClock is null ? birth : person.BirthTick,
                BirthLifeTick = checkpoint.LifeClock is null ? null : birth,
                AgeBand = checkpoint.Config.AgeBandAt(age),
                LastLifecycleYearChecked = age,
            } : person).ToArray(),
        };
    }

    private static PrivateWorldRuntimeState WithMedicine(PrivateWorldRuntimeState state, string owner, int quantity) =>
        WithInventory(state, InventoryFixture.AddLot(state.Society.Society.Inventory, "medical-test-dose", "medicine", owner, quantity));

    private static PrivateWorldRuntimeState WithInventory(PrivateWorldRuntimeState state, InventoryCheckpoint inventory) =>
        WithCheckpoint(state, state.Society.Society with { Inventory = inventory });

    private static PrivateWorldRuntimeState WithCheckpoint(PrivateWorldRuntimeState state, SocietyCheckpoint checkpoint) =>
        state with { Society = state.Society with { Society = checkpoint } };

    private static PrivateWorldRuntime Restore(PrivateWorldRuntimeState state, string? actor = null, IDecisionProvider? provider = null) =>
        PrivateWorldRuntime.Restore(state, id => id == actor && provider is not null ? provider :
            new MedicalChoiceProvider("safe_idle", DecisionProviderKind.Deterministic));

    private static PlaytestInhabitantState Physical(PrivateWorldRuntime world, string actor) =>
        world.Inhabitants.Single(person => person.InhabitantId == actor);

    private sealed class MedicalChoiceProvider(string selected, DecisionProviderKind kind = DecisionProviderKind.LargeLanguageModel,
        bool fail = false, string? observerReply = null) : IDecisionProvider
    {
        public DecisionProviderKind Kind => kind;
        public long ProviderEpoch => 0;
        public List<CognitionCandidate> Offered { get; } = [];
        public List<CognitionObserverGuidance> Guidance { get; } = [];

        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            Offered.AddRange(request.Observation.Candidates);
            Guidance.AddRange(request.Observation.ObserverGuidance ?? []);
            if (fail) throw new InvalidOperationException("medical provider fixture unavailable");
            var choice = request.Observation.Candidates.Any(candidate => candidate.Id == selected) ? selected : "safe_idle";
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, request.Observation.InhabitantId,
                Kind, request.ProviderEpoch, request.Observation.RunEpoch, request.Observation.DecisionGeneration,
                request.Observation.ObservationDigest, choice, 1, new Dictionary<string, double> { [choice] = 1 })
            {
                ObserverReplies = observerReply is null ? null : request.Observation.ObserverGuidance?
                    .Where(message => message.ReplyAllowed)
                    .Select(message => new CognitionObserverReply(message.InstructionId, observerReply)).ToArray(),
            });
        }
    }
}

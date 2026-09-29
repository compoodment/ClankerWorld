using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Tests;

public sealed class SocietyTests
{
    [Theory]
    [InlineData("a", false)]
    [InlineData("b", false)]
    [InlineData("a", true)]
    [InlineData("b", true)]
    public void DeathReleasesBothOpenBarterReservationsButPreservesOtherWork(string deceased, bool accepted)
    {
        var state = SocietyFixture.CreateGenesis("death-barter", [SocietyFixture.CreateFounder("a", "A"), SocietyFixture.CreateFounder("b", "B")]);
        state = state with { Inventory = InventoryFixture.AddLot(state.Inventory, "a-food", "food", "a", 2) };
        state = state with { Inventory = InventoryFixture.AddLot(state.Inventory, "b-food", "food", "b", 2) };
        state = SocietyFixture.CreateBarterOffer(state, new DirectBarterProposal("offer", 1, "a", "b", "a-food", 1, "b-food", 1, 120)).Checkpoint;
        if (accepted) state = SocietyFixture.AcceptBarterOffer(state, "offer", 1, "a").Checkpoint;
        var survivor = deceased == "a" ? "b" : "a";
        state = state with { Inventory = InventoryFixture.Reserve(state.Inventory, "unrelated", survivor, survivor + "-food", 1, "work", 120) };
        state = SocietyFixture.Kill(state, deceased, SocietyDeathCause.Accident).Checkpoint;
        Assert.Equal(DirectBarterState.Cancelled, state.Inventory.GetOffer("offer").State);
        Assert.Equal(InventoryReservationState.Released, state.Inventory.GetReservation("offer:first").State);
        Assert.Equal(InventoryReservationState.Released, state.Inventory.GetReservation("offer:second").State);
        Assert.Equal(InventoryReservationState.Reserved, state.Inventory.GetReservation("unrelated").State);
        state = SocietyCheckpointCodec.Decode(SocietyCheckpointCodec.Encode(state));
        state = SocietyFixture.ConsumeInventory(state, survivor, survivor + "-food", 1).Checkpoint;
        Assert.Equal(1, state.Inventory.Lots.Single(lot => lot.Id == survivor + "-food").Quantity);
    }

    [Fact]
    public void DeathDoesNotCancelAnAlreadySettledBarter()
    {
        var state = SocietyFixture.CreateGenesis("settled-barter-death", [SocietyFixture.CreateFounder("a", "A"), SocietyFixture.CreateFounder("b", "B")]);
        state = state with { Inventory = InventoryFixture.AddLot(state.Inventory, "a-food", "food", "a", 1) };
        state = state with { Inventory = InventoryFixture.AddLot(state.Inventory, "b-food", "food", "b", 1) };
        state = SocietyFixture.CreateBarterOffer(state, new DirectBarterProposal("done", 1, "a", "b", "a-food", 1, "b-food", 1, 120)).Checkpoint;
        state = SocietyFixture.AcceptBarterOffer(state, "done", 1, "a").Checkpoint;
        state = SocietyFixture.AcceptBarterOffer(state, "done", 1, "b").Checkpoint;
        state = SocietyFixture.Kill(state, "a", SocietyDeathCause.Accident).Checkpoint;
        Assert.Equal(DirectBarterState.Settled, state.Inventory.GetOffer("done").State);
    }

    [Fact]
    public void SocietyClockExpiresInventoryReservationsExactlyOnce()
    {
        var checkpoint = Genesis(TestConfig(), "alice", "bob");
        checkpoint = checkpoint with
        {
            Inventory = InventoryFixture.Reserve(checkpoint.Inventory, "meal", "alice", "food-lot", 1, "meal", 2),
        };
        var atBoundary = SocietyFixture.AdvanceTo(checkpoint, 2).Checkpoint;
        Assert.Equal(InventoryReservationState.Reserved, atBoundary.Inventory.GetReservation("meal").State);
        var after = SocietyFixture.AdvanceTo(atBoundary, 3).Checkpoint;
        Assert.Equal(InventoryReservationState.Released, after.Inventory.GetReservation("meal").State);
        Assert.Single(after.Inventory.Events, item => item.Kind == "reservation_released");
        var later = SocietyFixture.AdvanceTo(after, 9).Checkpoint;
        Assert.Single(later.Inventory.Events, item => item.Kind == "reservation_released");
    }

    [Fact]
    public void RelationshipConsentAndCardinalityAreAuthoritative()
    {
        var config = TestConfig();
        var checkpoint = Genesis(config, "alice", "bob", "cara");
        checkpoint = SocietyFixture.CreateHousehold(checkpoint, "home", "The Home", ["alice", "bob", "cara"]).Checkpoint;

        checkpoint = SocietyFixture.ProposeRelationship(
            checkpoint,
            new SocietyRelationshipProposal(
                "partnership-1",
                1,
                SocietyRelationshipType.Partnership,
                "alice",
                "bob",
                checkpoint.WorldTick)).Checkpoint;
        checkpoint = SocietyFixture.AcceptRelationship(checkpoint, "partnership-1", 1, "bob").Checkpoint;
        Assert.Equal(SocietyRelationshipState.Accepted, checkpoint.GetRelationship("partnership-1").State);

        checkpoint = SocietyFixture.ProposeRelationship(
            checkpoint,
            new SocietyRelationshipProposal(
                "partnership-2",
                1,
                SocietyRelationshipType.Partnership,
                "bob",
                "cara",
                checkpoint.WorldTick)).Checkpoint;
        var rejected = SocietyFixture.AcceptRelationship(checkpoint, "partnership-2", 1, "cara");

        Assert.Equal(SocietyRelationshipState.Proposed, rejected.Checkpoint.GetRelationship("partnership-2").State);
        Assert.Contains(
            rejected.NewEvents!,
            societyEvent => societyEvent.Kind == "relationship_rejected" &&
                societyEvent.Detail.EndsWith("cardinality_conflict", StringComparison.Ordinal));
    }

    [Fact]
    public void BirthConsumesReservedFoodCreatesOneIdentityAndIsIdempotent()
    {
        var config = TestConfig();
        var checkpoint = Genesis(config, "alice", "bob");
        checkpoint = SocietyFixture.CreateHousehold(checkpoint, "home", "The Home", ["alice", "bob"]).Checkpoint;
        checkpoint = AcceptPartnership(checkpoint, "alice", "bob");
        var request = new SocietyBirthRequest(
            "birth-1",
            1,
            "alice",
            "bob",
            "home",
            ["alice", "bob"],
            ["alice", "bob"],
            "food-lot",
            2,
            checkpoint.WorldTick);

        var committed = SocietyFixture.CommitBirth(checkpoint, request);
        var retried = SocietyFixture.CommitBirth(committed.Checkpoint, request);

        Assert.Equal("world:inhabitant:birth-1", committed.CreatedId);
        Assert.Single(committed.Checkpoint.Births);
        Assert.Equal(1, committed.Checkpoint.Inventory.GetLot("food-lot").Quantity);
        Assert.Equal(committed.Checkpoint, retried.Checkpoint);
        Assert.Equal(1, retried.Checkpoint.Inhabitants.Count(item => item.Id == committed.CreatedId));
        Assert.Contains(
            committed.Checkpoint.Relationships,
            relationship => relationship.Type == SocietyRelationshipType.BiologicalParentage &&
                relationship.TargetId == committed.CreatedId);
    }

    [Theory]
    [InlineData("propose")]
    [InlineData("revoke")]
    public void BiologicalParentageCannotBeEditedThroughOrdinaryRelationshipCommands(string operation)
    {
        var checkpoint = Genesis(TestConfig(), "alice", "bob");
        checkpoint = SocietyFixture.CreateHousehold(checkpoint, "home", "Home", ["alice", "bob"]).Checkpoint;
        checkpoint = AcceptPartnership(checkpoint, "alice", "bob");
        var birth = SocietyFixture.CommitBirth(checkpoint, new("history-child", 1, "alice", "bob", "home",
            ["alice", "bob"], ["alice", "bob"], "food-lot", 2, checkpoint.WorldTick));
        checkpoint = birth.Checkpoint;
        var edge = checkpoint.Relationships.First(item => item.Type == SocietyRelationshipType.BiologicalParentage);
        if (operation == "propose")
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => SocietyFixture.ProposeRelationship(checkpoint,
                new("fake-parentage", 1, SocietyRelationshipType.BiologicalParentage, "bob", "alice", checkpoint.WorldTick)));
            return;
        }
        if (operation == "accept")
        {
            checkpoint = checkpoint with
            {
                Relationships = checkpoint.Relationships.Select(item => item.Id == edge.Id
                ? item with { State = SocietyRelationshipState.Proposed, Consent = SocietyConsentState.Pending } : item).ToArray()
            };
        }
        var result = operation switch
        {
            "accept" => SocietyFixture.AcceptRelationship(checkpoint, edge.Id, edge.Revision, edge.TargetId),
            _ => SocietyFixture.RevokeRelationship(checkpoint, edge.Id, edge.ProposerId),
        };
        Assert.Equal(checkpoint.Relationships, result.Checkpoint.Relationships);
        Assert.Equal(checkpoint.Births, result.Checkpoint.Births);
        Assert.Contains(result.NewEvents!, item => item.Kind == "relationship_rejected");
    }

    [Fact]
    public void NaturalMortalityIsGradualPolicyAndEstateSettlementIsDeterministic()
    {
        var config = TestConfig() with
        {
            BaseNaturalMortalityBasisPoints = 10_000,
            NaturalMortalitySlopeBasisPoints = 0,
            EstateEscrowDays = 2,
        };
        var naturalCheckpoint = Genesis(config, "elder");

        var elderTick = checked(65L * config.TicksPerWorldYear);
        var aged = SocietyFixture.AdvanceTo(naturalCheckpoint, elderTick).Checkpoint;
        var elder = aged.GetInhabitant("elder");

        Assert.Equal(SocietyInhabitantStatus.Dead, elder.Status);
        Assert.Equal(SocietyAgeBand.Elder, elder.AgeBand);
        Assert.Equal(SocietyDeathCause.NaturalAge, elder.DeathCause);
        Assert.Single(aged.Estates);

        var estateCheckpoint = Genesis(config with { BaseNaturalMortalityBasisPoints = 0 }, "alice", "bob");
        estateCheckpoint = SocietyFixture.CreateHousehold(
            estateCheckpoint,
            "home",
            "The Home",
            ["alice", "bob"]).Checkpoint;
        var killed = SocietyFixture.Kill(estateCheckpoint, "alice", SocietyDeathCause.Hazard).Checkpoint;
        var estateId = "estate:alice:0";

        var settled = SocietyFixture.AdvanceTo(killed, 2).Checkpoint;

        Assert.True(settled.GetEstate(estateId).Settled);
        Assert.Contains(
            settled.Inventory.Lots,
            lot => lot.OwnerId == "bob" && lot.ItemKind == "food");
        Assert.Contains(settled.Events, societyEvent => societyEvent.Kind == "estate_settled");
    }

    [Fact]
    public void TransferAndBarterUseTheSameAtomicInventoryLedger()
    {
        var config = TestConfig();
        var checkpoint = Genesis(config, "alice", "bob");
        var transferred = SocietyFixture.TransferInventory(
            checkpoint,
            "gift-1",
            "alice",
            "bob",
            "food-lot",
            2,
            "gift");
        Assert.Equal("bob", transferred.Checkpoint.Inventory.GetLot("food-lot#transfer:gift-1").OwnerId);

        var withBobLot = transferred.Checkpoint with
        {
            Inventory = transferred.Checkpoint.Inventory with
            {
                Lots = transferred.Checkpoint.Inventory.Lots.Append(
                    new InventoryLot("tool-lot", "tool", "bob", 1, 10_000, 10_000, 0))
                    .OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(),
            },
        };
        var offered = SocietyFixture.CreateBarterOffer(
            withBobLot,
            new DirectBarterProposal("barter-1", 1, "bob", "alice", "tool-lot", 1, "food-lot", 1, 10));
        var firstAcceptance = SocietyFixture.AcceptBarterOffer(offered.Checkpoint, "barter-1", 1, "alice");
        var settled = SocietyFixture.AcceptBarterOffer(firstAcceptance.Checkpoint, "barter-1", 1, "bob");

        Assert.Equal(DirectBarterState.Settled, settled.Checkpoint.Inventory.GetOffer("barter-1").State);
        Assert.Equal("alice", settled.Checkpoint.Inventory.Lots.Single(lot => lot.Id == "tool-lot").OwnerId);
        Assert.Contains(
            settled.Checkpoint.Inventory.Lots,
            lot => lot.OwnerId == "bob" && lot.ItemKind == "food");
    }

    [Fact]
    public async Task MultipleInhabitantsHaveIndependentFairCognitionSchedules()
    {
        var config = TestConfig();
        var founders = new[]
        {
            SocietyFixture.CreateFounder("alice", "Alice", config: config),
            SocietyFixture.CreateFounder("bob", "Bob", config: config),
            SocietyFixture.CreateFounder("cara", "Cara", config: config),
        };
        var scheduler = new SocietyCognitionScheduler(founders, maxQueueLength: 2, maxDispatchPerCycle: 2);

        Assert.True(scheduler.Enqueue(Entry("a", "alice", 1, 0)));
        Assert.True(scheduler.Enqueue(Entry("b", "bob", 0, 0)));
        Assert.True(scheduler.Enqueue(Entry("a-newer", "alice", 2, 1)));
        Assert.False(scheduler.Enqueue(Entry("c", "cara", 0, 1)));

        var decisions = await scheduler.DispatchAsync();
        var restored = SocietyCognitionScheduler.Restore(scheduler.ExportState());

        Assert.Equal(2, decisions.Count);
        Assert.All(decisions, decision => Assert.True(decision.Admission.Accepted));
        Assert.Equal("safe_idle", scheduler.CaptureRuntime("alice").CurrentIntention?.CandidateId);
        Assert.Equal("safe_idle", scheduler.CaptureRuntime("bob").CurrentIntention?.CandidateId);
        Assert.Equal(
            scheduler.CaptureRuntime("alice").CurrentIntention,
            restored.CaptureRuntime("alice").CurrentIntention);
    }

    [Fact]
    public async Task CancellationLeavesSelectedAndUnprocessedCognitionEntriesQueued()
    {
        var config = TestConfig();
        var founders = new[]
        {
            SocietyFixture.CreateFounder("alice", "Alice", config: config),
            SocietyFixture.CreateFounder("bob", "Bob", config: config),
        };
        using var cancellation = new CancellationTokenSource();
        var scheduler = new SocietyCognitionScheduler(
            founders,
            _ => new CancellingDecisionProvider(cancellation),
            maxDispatchPerCycle: 2);
        Assert.True(scheduler.Enqueue(Entry("a", "alice", 1, 0)));
        Assert.True(scheduler.Enqueue(Entry("b", "bob", 0, 0)));

        await Assert.ThrowsAsync<OperationCanceledException>(async () => await scheduler.DispatchAsync(cancellation.Token));

        var state = scheduler.ExportState();
        Assert.Equal(["a", "b"], state.Queue.Select(item => item.ScheduleId).OrderBy(item => item, StringComparer.Ordinal));
    }

    [Fact]
    public void SocietyCheckpointRoundTripsWithStableDigests()
    {
        var config = TestConfig();
        var checkpoint = Genesis(config, "alice", "bob");
        checkpoint = SocietyFixture.CreateHousehold(checkpoint, "home", "The Home", ["alice", "bob"]).Checkpoint;
        checkpoint = SocietyFixture.AssignRole(checkpoint, "alice", SocietyWorkRole.Farmer).Checkpoint;

        var bytes = SocietyCheckpointCodec.Encode(checkpoint);
        var restored = SocietyCheckpointCodec.Decode(bytes);

        Assert.Equal(
            SocietyCheckpointCodec.StateDigest(checkpoint),
            SocietyCheckpointCodec.StateDigest(restored));
        Assert.Equal(
            SocietyCheckpointCodec.EventDigest(checkpoint.Events),
            SocietyCheckpointCodec.EventDigest(restored.Events));
    }

    [Fact]
    public async Task SocietyRuntimeReconcilesBirthsAndDeathsWithCognition()
    {
        var config = TestConfig();
        var checkpoint = Genesis(config, "alice", "bob");
        checkpoint = SocietyFixture.CreateHousehold(checkpoint, "home", "The Home", ["alice", "bob"]).Checkpoint;
        checkpoint = AcceptPartnership(checkpoint, "alice", "bob");

        using var runtime = new SocietyWorldRuntime(checkpoint);
        Assert.True(runtime.EnqueueCognition(Entry("alice-start", "alice", 1, 0)));
        var firstDispatch = await runtime.DispatchCognitionAsync();
        Assert.Single(firstDispatch.Decisions);

        var encodedRuntime = SocietyWorldRuntimeCodec.Encode(runtime.ExportState());
        var decodedRuntime = SocietyWorldRuntimeCodec.Decode(encodedRuntime);
        Assert.Equal(
            SocietyWorldRuntimeCodec.Digest(runtime.ExportState()),
            SocietyWorldRuntimeCodec.Digest(decodedRuntime));

        var birth = new SocietyBirthRequest(
            "birth-runtime",
            1,
            "alice",
            "bob",
            "home",
            ["alice", "bob"],
            ["alice", "bob"],
            "food-lot",
            1,
            runtime.Checkpoint.WorldTick);
        runtime.Apply(current => SocietyFixture.CommitBirth(current, birth));
        var childId = "world:inhabitant:birth-runtime";

        Assert.Contains(childId, runtime.Capture().Cognition.Runtimes.Select(item => item.InhabitantId));
        Assert.False(runtime.EnqueueCognition(Entry("infant-start", childId, 0, 0)));
        Assert.DoesNotContain(runtime.Capture().Cognition.Queue, item => item.InhabitantId == childId);
        runtime.AdvanceTo(20); // The fixture's two-year infancy ends here.
        Assert.Equal(SocietyAgeBand.Child, runtime.Checkpoint.GetInhabitant(childId).AgeBand);
        Assert.True(runtime.EnqueueCognition(Entry("child-start", childId, 0, 20)));
        var queued = Assert.Single(runtime.Capture().Cognition.Queue);
        Assert.True(queued.Observation.RequiresPersonalProvider);

        // A restored queue still marks this born child as needing its own
        // selection, including saves written before the observation flag.
        var unmarked = runtime.ExportState();
        unmarked = unmarked with
        {
            Cognition = unmarked.Cognition with
            {
                Queue = unmarked.Cognition.Queue.Select(item => item with
                {
                    Observation = item.Observation with { RequiresPersonalProvider = false },
                }).ToArray(),
            }
        };
        using (var recovered = SocietyWorldRuntime.Restore(unmarked))
        {
            Assert.True(Assert.Single(recovered.Capture().Cognition.Queue).Observation.RequiresPersonalProvider);
            await recovered.DispatchCognitionAsync();
        }

        await runtime.DispatchCognitionAsync();

        runtime.Apply(current => SocietyFixture.Kill(current, "bob", SocietyDeathCause.Hazard));
        var restored = SocietyWorldRuntime.Restore(runtime.ExportState());
        using (restored)
        {
            restored.Validate();
            Assert.DoesNotContain("bob", restored.Capture().Cognition.Runtimes.Select(item => item.InhabitantId));
            Assert.Contains(childId, restored.Capture().Cognition.Runtimes.Select(item => item.InhabitantId));
            Assert.Equal(SocietyInhabitantStatus.Dead, restored.Checkpoint.GetInhabitant("bob").Status);
        }
    }

    [Fact]
    public void DeathEndsRelationshipsCancelsExchangeAndEscrowsInventory()
    {
        var config = TestConfig() with { BaseNaturalMortalityBasisPoints = 0 };
        var founders = new[]
        {
            SocietyFixture.CreateFounder("alice", "Alice", config: config),
            SocietyFixture.CreateFounder("bob", "Bob", config: config),
        };
        var checkpoint = SocietyFixture.CreateGenesis(
            "world",
            founders,
            [
                new InventoryLot("food-lot", "food", "alice", 3, 10_000, 10_000, 0),
                new InventoryLot("tool-lot", "tool", "bob", 1, 10_000, 10_000, 0),
            ],
            config);
        checkpoint = SocietyFixture.CreateHousehold(checkpoint, "home", "The Home", ["alice", "bob"]).Checkpoint;
        checkpoint = AcceptPartnership(checkpoint, "alice", "bob");
        checkpoint = checkpoint with
        {
            Inventory = InventoryFixture.Reserve(
                checkpoint.Inventory,
                "alice-reservation",
                "alice",
                "food-lot",
                1,
                "test",
                10),
        };
        checkpoint = SocietyFixture.CreateBarterOffer(
            checkpoint,
            new DirectBarterProposal("barter-death", 1, "alice", "bob", "food-lot", 1, "tool-lot", 1, 10)).Checkpoint;

        var killed = SocietyFixture.Kill(checkpoint, "alice", SocietyDeathCause.Hazard).Checkpoint;

        Assert.Equal(SocietyRelationshipState.EndedByDeath, killed.GetRelationship("partnership-1").State);
        Assert.Equal(DirectBarterState.Cancelled, killed.Inventory.GetOffer("barter-death").State);
        Assert.Equal(InventoryReservationState.Released, killed.Inventory.GetReservation("alice-reservation").State);
        Assert.Contains(killed.Inventory.Lots, lot => lot.OwnerId == "estate:alice:0");

        var settled = SocietyFixture.AdvanceTo(killed, config.EstateEscrowDays).Checkpoint;
        Assert.True(settled.GetEstate("estate:alice:0").Settled);
        Assert.Contains(settled.Inventory.Lots, lot => lot.OwnerId == "bob");
    }

    [Fact]
    public void NaturalMortalityHasNoHardMaximumAndElderhoodIsNotDeath()
    {
        var config = new SocietyConfig();

        Assert.Equal(0, config.NaturalMortalityRiskBasisPoints(config.AdultYears));
        Assert.Equal(100, config.NaturalMortalityRiskBasisPoints(config.ElderYears));
        Assert.True(config.NaturalMortalityRiskBasisPoints(120) < 10_000);
        Assert.True(config.NaturalMortalityRiskBasisPoints(145) < 10_000);

        var noMortality = config with
        {
            BaseNaturalMortalityBasisPoints = 0,
            NaturalMortalitySlopeBasisPoints = 0,
        };
        var elder = SocietyFixture.CreateFounder("survivor", "Survivor", config: noMortality);
        var checkpoint = SocietyFixture.CreateGenesis("world", [elder], config: noMortality);
        var atElderhood = SocietyFixture.AdvanceTo(
            checkpoint,
            (config.ElderYears - config.AdultYears) * config.TicksPerWorldYear).Checkpoint;

        Assert.Equal(SocietyInhabitantStatus.Active, atElderhood.GetInhabitant("survivor").Status);
        Assert.Equal(SocietyAgeBand.Elder, atElderhood.GetInhabitant("survivor").AgeBand);
    }

    [Fact]
    public void AgeBoundariesAndNewbornProviderResolutionAreVersioned()
    {
        var config = TestConfig() with
        {
            BaseNaturalMortalityBasisPoints = 0,
            NaturalMortalitySlopeBasisPoints = 0,
        };
        var child = new SocietyInhabitant(
            "child",
            "Child",
            0,
            SocietyInhabitantStatus.Active,
            SocietyAgeBand.Infant,
            10_000,
            null,
            null,
            SocietyWorkRole.Unassigned,
            0);
        var aged = SocietyFixture.CreateGenesis("world", [child], config: config);
        aged = SocietyFixture.AdvanceTo(aged, 2L * config.TicksPerWorldYear).Checkpoint;
        Assert.Equal(SocietyAgeBand.Child, aged.GetInhabitant("child").AgeBand);
        aged = SocietyFixture.AdvanceTo(aged, 12L * config.TicksPerWorldYear).Checkpoint;
        Assert.Equal(SocietyAgeBand.Adolescent, aged.GetInhabitant("child").AgeBand);
        aged = SocietyFixture.AdvanceTo(aged, 18L * config.TicksPerWorldYear).Checkpoint;
        Assert.Equal(SocietyAgeBand.Adult, aged.GetInhabitant("child").AgeBand);

        var founders = new[]
        {
            SocietyFixture.CreateFounder("alice", "Alice", "jev", config: config),
            SocietyFixture.CreateFounder("bob", "Bob", "jev", config: config),
        };
        var family = SocietyFixture.CreateGenesis(
            "family",
            founders,
            [new InventoryLot("food-lot", "food", "alice", 2, 10_000, 10_000, 0)],
            config,
            "deterministic");
        family = SocietyFixture.CreateHousehold(family, "home", "The Home", ["alice", "bob"]).Checkpoint;
        family = AcceptPartnership(family, "alice", "bob");
        var birth = SocietyFixture.CommitBirth(
            family,
            new SocietyBirthRequest(
                "provider-birth",
                1,
                "alice",
                "bob",
                "home",
                ["alice", "bob"],
                ["alice", "bob"],
                "food-lot",
                1,
                family.WorldTick,
                NewbornProviderPolicy.Hybrid)).Checkpoint;

        Assert.Equal("jev", birth.GetInhabitant("family:inhabitant:provider-birth").ProviderBindingId);
    }

    [Fact]
    public void CareRolesOrganizationsAndSocialMemoryRemainTyped()
    {
        var config = TestConfig();
        var checkpoint = Genesis(config, "alice", "bob");
        checkpoint = SocietyFixture.CreateHousehold(checkpoint, "home", "The Home", ["alice", "bob"]).Checkpoint;
        checkpoint = SocietyFixture.ProposeRelationship(
            checkpoint,
            new SocietyRelationshipProposal(
                "care-1",
                1,
                SocietyRelationshipType.Caregiver,
                "alice",
                "bob",
                checkpoint.WorldTick,
                "home")).Checkpoint;
        checkpoint = SocietyFixture.AcceptRelationship(checkpoint, "care-1", 1, "bob").Checkpoint;
        checkpoint = SocietyFixture.AssignRole(checkpoint, "alice", SocietyWorkRole.Caregiver).Checkpoint;
        checkpoint = SocietyFixture.RecordSocialMemory(
            checkpoint,
            new SocietySocialMemory("memory-1", "alice", "bob", "Bob helps at home.", "private", checkpoint.WorldTick)).Checkpoint;
        checkpoint = SocietyFixture.CreateOrganization(
            checkpoint,
            "farm-1",
            SocietyOrganizationKind.Farm,
            "alice",
            ["alice"]).Checkpoint;
        checkpoint = SocietyFixture.AddOrganizationMember(checkpoint, "farm-1", "alice", "bob").Checkpoint;

        Assert.Contains("alice", checkpoint.GetHousehold("home").CaregiverIds);
        Assert.Equal(SocietyWorkRole.Caregiver, checkpoint.GetInhabitant("alice").CurrentRole);
        Assert.Equal(["alice", "bob"], checkpoint.Organizations.Single().MemberIds);
        Assert.Single(checkpoint.Memories);

        checkpoint = SocietyFixture.RevokeRelationship(checkpoint, "care-1", "alice").Checkpoint;
        Assert.DoesNotContain("alice", checkpoint.GetHousehold("home").CaregiverIds);
    }

    private static SocietyCheckpoint Genesis(SocietyConfig config, params string[] ids)
    {
        var founders = ids.Select(id => SocietyFixture.CreateFounder(id, id, config: config)).ToArray();
        var lots = new[]
        {
            new InventoryLot("food-lot", "food", ids[0], 3, 10_000, 10_000, 0),
        };
        return SocietyFixture.CreateGenesis("world", founders, lots, config);
    }

    private static SocietyCheckpoint AcceptPartnership(
        SocietyCheckpoint checkpoint,
        string firstId,
        string secondId)
    {
        checkpoint = SocietyFixture.ProposeRelationship(
            checkpoint,
            new SocietyRelationshipProposal(
                "partnership-1",
                1,
                SocietyRelationshipType.Partnership,
                firstId,
                secondId,
                checkpoint.WorldTick)).Checkpoint;
        return SocietyFixture.AcceptRelationship(checkpoint, "partnership-1", 1, secondId).Checkpoint;
    }

    private static SocietyConfig TestConfig() =>
        new(TicksPerWorldDay: 1, DaysPerWorldYear: 10);

    private static SocietyCognitionScheduleEntry Entry(
        string scheduleId,
        string inhabitantId,
        int priority,
        long tick) =>
        new(
            scheduleId,
            inhabitantId,
            priority,
            tick,
            [$"trigger:{scheduleId}"],
            new InhabitantObservation(
                inhabitantId,
                tick,
                0,
                0,
                $"digest:{scheduleId}",
                5_000,
            [new CognitionCandidate("safe_idle", "Continue safely.", 0)]));

    private sealed class CancellingDecisionProvider(CancellationTokenSource cancellation) : IDecisionProvider
    {
        private readonly DeterministicDecisionProvider fallback = new();

        public DecisionProviderKind Kind => fallback.Kind;

        public long ProviderEpoch => fallback.ProviderEpoch;

        public ValueTask<CognitionDecisionResponse> DecideAsync(
            CognitionDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellation.Cancel();
            return fallback.DecideAsync(request, cancellationToken);
        }
    }
}

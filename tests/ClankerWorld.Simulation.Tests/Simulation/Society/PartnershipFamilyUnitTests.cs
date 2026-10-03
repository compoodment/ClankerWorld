using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Tests;

public sealed class PartnershipFamilyUnitTests
{
    private static readonly string[] FounderIds = ["alice", "bob", "cara"];

    [Theory]
    [InlineData("withdraw")]
    [InlineData("target-withdraw")]
    [InlineData("refuse")]
    [InlineData("end-accepted")]
    public void ClosingAProposalPreservesFamilyUnlessPartnershipWasAccepted(string action)
    {
        var (checkpoint, child) = BirthFamily();
        checkpoint = SocietyFixture.AdvanceTo(checkpoint, checkpoint.Config.AdultYears * checkpoint.Config.TicksPerWorldYear).Checkpoint;
        Assert.Equal(SocietyAgeBand.Adult, checkpoint.GetInhabitant(child).AgeBand);
        var originalUnits = Units(checkpoint);
        Assert.Equal(checkpoint.GetInhabitant("alice").DomesticFamilyUnitId, checkpoint.GetInhabitant(child).DomesticFamilyUnitId);
        Assert.Equal(4, Capacity(checkpoint).Limit);
        checkpoint = SocietyFixture.ProposeRelationship(checkpoint,
            new("adult-child-proposal", 1, SocietyRelationshipType.Partnership, child, "cara", checkpoint.WorldTick)).Checkpoint;
        Assert.Equal(originalUnits, Units(checkpoint));
        checkpoint = Roundtrip(checkpoint);
        if (action == "end-accepted")
        {
            checkpoint = SocietyFixture.AcceptRelationship(checkpoint, "adult-child-proposal", 1, "cara").Checkpoint;
            Assert.Equal(checkpoint.GetInhabitant(child).DomesticFamilyUnitId, checkpoint.GetInhabitant("cara").DomesticFamilyUnitId);
            Assert.NotEqual(checkpoint.GetInhabitant("alice").DomesticFamilyUnitId, checkpoint.GetInhabitant(child).DomesticFamilyUnitId);
        }
        var closure = action == "refuse"
            ? SocietyFixture.RefuseRelationship(checkpoint, "adult-child-proposal", 1, "cara")
            : SocietyFixture.RevokeRelationship(checkpoint, "adult-child-proposal", action == "target-withdraw" ? "cara" : child);
        Assert.Equal(action == "refuse" ? "relationship_rejected" : "relationship_revoked", Assert.Single(closure.NewEvents!).Kind);
        checkpoint = closure.Checkpoint;
        checkpoint = Roundtrip(checkpoint);
        Assert.Equal(action == "refuse" ? SocietyRelationshipState.Rejected : SocietyRelationshipState.Revoked,
            checkpoint.GetRelationship("adult-child-proposal").State);
        Assert.All(checkpoint.Inhabitants, person => Assert.Equal("home", person.HouseholdId));
        AssertBirthHistory(checkpoint, child);
        if (action == "end-accepted")
        {
            Assert.NotEqual(checkpoint.GetInhabitant(child).DomesticFamilyUnitId, checkpoint.GetInhabitant("cara").DomesticFamilyUnitId);
            Assert.StartsWith("domestic:separate:", checkpoint.GetInhabitant(child).DomesticFamilyUnitId);
            Assert.StartsWith("domestic:separate:", checkpoint.GetInhabitant("cara").DomesticFamilyUnitId);
            Assert.Equal(3, Capacity(checkpoint).Limit);
        }
        else
        {
            Assert.Equal(4, Capacity(checkpoint).Limit);
            Assert.Equal(originalUnits, Units(checkpoint));
        }
    }

    [Fact]
    public void RevokedPrimaryCarePreservesChildFamilyWhenFormerCaregiverFindsPartner()
    {
        var (checkpoint, child) = BirthFamily();
        checkpoint = SocietyFixture.RevokeRelationship(checkpoint, "parents", "alice").Checkpoint;
        var originalChildUnit = checkpoint.GetInhabitant(child).DomesticFamilyUnitId;
        Assert.Equal(checkpoint.GetInhabitant("alice").DomesticFamilyUnitId, originalChildUnit);
        Assert.NotEqual(checkpoint.GetInhabitant("bob").DomesticFamilyUnitId, originalChildUnit);
        var care = Assert.Single(checkpoint.Relationships, edge => edge.Type == SocietyRelationshipType.Caregiver &&
            edge.ProposerId == "alice" && edge.TargetId == child && edge.State == SocietyRelationshipState.Accepted);
        checkpoint = SocietyFixture.RevokeRelationship(checkpoint, care.Id, "alice").Checkpoint;
        checkpoint = Roundtrip(checkpoint);
        Assert.Null(checkpoint.GetInhabitant(child).PrimaryCaregiverId);
        var originalLimit = Capacity(checkpoint).Limit;
        checkpoint = SocietyFixture.ProposeRelationship(checkpoint,
            new("new-partnership", 1, SocietyRelationshipType.Partnership, "alice", "cara", checkpoint.WorldTick)).Checkpoint;
        checkpoint = SocietyFixture.AcceptRelationship(checkpoint, "new-partnership", 1, "cara").Checkpoint;
        checkpoint = Roundtrip(checkpoint);
        Assert.Equal(originalChildUnit, checkpoint.GetInhabitant(child).DomesticFamilyUnitId);
        Assert.NotEqual(checkpoint.GetInhabitant("alice").DomesticFamilyUnitId, checkpoint.GetInhabitant(child).DomesticFamilyUnitId);
        Assert.Null(checkpoint.GetInhabitant(child).PrimaryCaregiverId);
        Assert.Equal(originalLimit, Capacity(checkpoint).Limit);
        Assert.All(checkpoint.Inhabitants, person => Assert.Equal("home", person.HouseholdId));
        AssertBirthHistory(checkpoint, child);
    }

    [Fact]
    public async Task NormalTickExpiryOfUnacceptedProposalPreservesFamilyUnits()
    {
        using var seed = NormalPathWorld.CreateGenerated("audit-town-invariants", _ => new Idle());
        var state = seed.ExportState();
        var people = state.Society.Society.Inhabitants.Select(person => person.Id).Order(StringComparer.Ordinal).ToArray();
        var proposed = SocietyFixture.ProposeRelationship(state.Society.Society,
            new("expiring-proposal", 1, SocietyRelationshipType.Partnership, people[0], people[1], 0)).Checkpoint;
        state = state with { Society = state.Society with { Society = proposed } };
        var originalUnits = Units(proposed);
        var originalHouseholds = proposed.Inhabitants.Select(person => (person.Id, person.HouseholdId)).ToArray();
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)), _ => new Idle());
        while (world.WorldTick < 120) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(SocietyRelationshipState.Proposed, world.Society.GetRelationship("expiring-proposal").State);
        Assert.Equal(originalUnits, Units(world.Society));
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())), _ => new Idle());
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
        world.Validate();
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        Assert.Equal(SocietyRelationshipState.Revoked, world.Society.GetRelationship("expiring-proposal").State);
        Assert.Equal(people[0], Assert.Single(world.ExportState().Events, item => item.Kind == "partnership_expired").Detail);
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        Assert.Equal(Units(world.Society), Units(restored.Society));
        Assert.Equal(originalUnits, Units(world.Society));
        Assert.Equal(originalHouseholds, world.Society.Inhabitants.Select(person => (person.Id, person.HouseholdId)));
    }

    private static (SocietyCheckpoint Checkpoint, string Child) BirthFamily()
    {
        var config = new SocietyConfig(TicksPerWorldDay: 1, DaysPerWorldYear: 10);
        var people = FounderIds.Select(id => SocietyFixture.CreateFounder(id, id, config: config)).ToArray();
        var checkpoint = SocietyFixture.CreateGenesis("partnership-family", people,
            [new InventoryLot("food", "food", "alice", 3, 10_000, 10_000, 0)], config);
        checkpoint = SocietyFixture.CreateHousehold(checkpoint, "home", "Home", ["alice", "bob", "cara"]).Checkpoint;
        checkpoint = SocietyFixture.ProposeRelationship(checkpoint,
            new("parents", 1, SocietyRelationshipType.Partnership, "alice", "bob", 0)).Checkpoint;
        checkpoint = SocietyFixture.AcceptRelationship(checkpoint, "parents", 1, "bob").Checkpoint;
        var birth = SocietyFixture.CommitBirth(checkpoint, new SocietyBirthRequest("child", 1, "alice", "bob", "home",
            ["alice", "bob"], ["alice", "bob"], "food", 2, checkpoint.WorldTick, PrimaryCaregiverId: "alice"));
        return (Roundtrip(birth.Checkpoint), Assert.IsType<string>(birth.CreatedId));
    }

    private static HouseResidentCapacityRules.Capacity Capacity(SocietyCheckpoint checkpoint) =>
        HouseResidentCapacityRules.Calculate(checkpoint.Inhabitants.Where(person => person.HouseholdId == "home"), 1, 1);

    private static (string Id, string? Family)[] Units(SocietyCheckpoint checkpoint) =>
        checkpoint.Inhabitants.Select(person => (person.Id, person.DomesticFamilyUnitId)).ToArray();

    private static void AssertBirthHistory(SocietyCheckpoint checkpoint, string child)
    {
        var birth = Assert.Single(checkpoint.Births);
        Assert.Equal(child, birth.ChildId);
        Assert.Equal("alice", birth.PrimaryCaregiverId);
        Assert.Equal(FounderIds.Take(2), checkpoint.Relationships
            .Where(edge => edge.Type == SocietyRelationshipType.BiologicalParentage && edge.TargetId == child)
            .Select(edge => edge.ProposerId).Order(StringComparer.Ordinal));
    }

    private static SocietyCheckpoint Roundtrip(SocietyCheckpoint checkpoint)
    {
        SocietyFixture.Validate(checkpoint);
        var bytes = SocietyCheckpointCodec.Encode(checkpoint);
        var restored = SocietyCheckpointCodec.Decode(bytes);
        Assert.Equal(bytes, SocietyCheckpointCodec.Encode(restored));
        return restored;
    }

    private sealed class Idle : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default) =>
            new DeterministicDecisionProvider().DecideAsync(request with
            {
                Observation = request.Observation with { Candidates = [request.Observation.Candidates.Single(item => item.Id == "safe_idle")] },
            }, cancellationToken);
    }
}

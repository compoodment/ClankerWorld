using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Simulation.Tests;

public sealed class SurvivalFixtureTests
{
    [Fact]
    public void StarvationDamagesHealthAndEatingClearsTheHungerBoundary()
    {
        var genesis = Genesis(new SurvivalActor("scout", 100, 1_000, 1));
        var exhausted = SurvivalFixture.Advance(genesis, SurvivalRules.Fixture);
        var recovered = SurvivalFixture.Advance(exhausted, SurvivalRules.Fixture, SurvivalAction.Eat);

        Assert.Equal(0, exhausted.Actor.HungerBasisPoints);
        Assert.Equal(950, exhausted.Actor.HealthBasisPoints);
        Assert.Contains(exhausted.Events, worldEvent => worldEvent is { Kind: "need_exhausted", Detail: "hunger:passive_drain" });
        Assert.Contains(recovered.Events, worldEvent => worldEvent is { Kind: "need_recovered", Detail: "hunger:eat" });
        Assert.True(recovered.Actor.HungerBasisPoints > 0);
    }

    [Fact]
    public void HarvestToZeroSchedulesOnlyRenewablesAndRegeneratesOnRecordedTick()
    {
        var genesis = Genesis();
        var harvested = SurvivalFixture.Advance(genesis, SurvivalRules.Fixture, SurvivalAction.Harvest("berries"));
        var stillRegenerating = SurvivalFixture.Advance(harvested, SurvivalRules.Fixture);
        stillRegenerating = SurvivalFixture.Advance(stillRegenerating, SurvivalRules.Fixture);
        var regenerated = SurvivalFixture.Advance(stillRegenerating, SurvivalRules.Fixture);
        var finite = SurvivalFixture.Advance(regenerated, SurvivalRules.Fixture, SurvivalAction.Harvest("ore"));

        Assert.Equal(SurvivalResourceState.Regenerating, harvested.GetResource("berries").State);
        Assert.Equal(4, harvested.GetResource("berries").RegenerationDueTick);
        Assert.Equal(SurvivalResourceState.Regenerating, stillRegenerating.GetResource("berries").State);
        Assert.Equal(SurvivalResourceState.Available, regenerated.GetResource("berries").State);
        Assert.Equal(1, regenerated.GetResource("berries").Quantity);
        Assert.Contains(regenerated.Events, worldEvent => worldEvent is { Kind: "resource_regenerated", Detail: "berries" });
        Assert.Equal(SurvivalResourceState.Depleted, finite.GetResource("ore").State);
        Assert.Null(finite.GetResource("ore").RegenerationDueTick);
    }

    [Fact]
    public void RepeatedHarvestIsRejectedWithoutMutatingThePriorCommittedCheckpoint()
    {
        var depleted = SurvivalFixture.Advance(
            Genesis(),
            SurvivalRules.Fixture,
            SurvivalAction.Harvest("ore"));
        var priorState = SurvivalDigest.State(depleted);
        var priorEvents = SurvivalDigest.Events(depleted.Events);

        Assert.Throws<InvalidOperationException>(() =>
            SurvivalFixture.Advance(depleted, SurvivalRules.Fixture, SurvivalAction.Harvest("ore")));
        Assert.Equal(priorState, SurvivalDigest.State(depleted));
        Assert.Equal(priorEvents, SurvivalDigest.Events(depleted.Events));
    }

    [Fact]
    public void CanonicalSaveLoadAndActionReplayReachIdenticalDigests()
    {
        var genesis = Genesis(new SurvivalActor("scout", 600, 1_000, 0));
        var current = SurvivalFixture.Advance(genesis, SurvivalRules.Fixture, SurvivalAction.Harvest("berries"));
        current = SurvivalFixture.Advance(current, SurvivalRules.Fixture, SurvivalAction.Eat);
        current = SurvivalFixture.Advance(current, SurvivalRules.Fixture);
        var bytes = SurvivalCheckpointCodec.Encode(current);
        var restored = SurvivalCheckpointCodec.Decode(bytes);
        var replayed = SurvivalFixture.Replay(genesis, SurvivalRules.Fixture, current.Events);

        Assert.Equal(SurvivalDigest.State(current), SurvivalDigest.State(restored));
        Assert.Equal(SurvivalDigest.Events(current.Events), SurvivalDigest.Events(restored.Events));
        Assert.Equal(SurvivalDigest.State(current), SurvivalDigest.State(replayed));
        Assert.Equal(SurvivalDigest.Events(current.Events), SurvivalDigest.Events(replayed.Events));
        Assert.True(bytes.SequenceEqual(SurvivalCheckpointCodec.Encode(restored)));
    }

    private static SurvivalCheckpoint Genesis(SurvivalActor? actor = null) =>
        SurvivalFixture.CreateGenesis(
            actor ?? new SurvivalActor("scout", 5_000, 10_000, 0),
            [
                new SurvivalResource("berries", true, 1, 1, SurvivalResourceState.Available, null),
                new SurvivalResource("ore", false, 1, 1, SurvivalResourceState.Available, null),
            ]);
}

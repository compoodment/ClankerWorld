using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Simulation.Tests;

public sealed class StagedTickKernelTests
{
    [Fact]
    public void TickUsesTheDeclaredTotalPhaseOrderAndIntegerClock()
    {
        var result = StagedTickKernel.Advance(KernelCheckpoint.Genesis, new KernelTickInput(7));
        var checkpoint = Assert.IsType<KernelCheckpoint>(result.Checkpoint);

        Assert.False(checkpoint.State.IsPaused);
        Assert.Equal(7, checkpoint.State.Counter);
        Assert.Equal(1, checkpoint.State.Clock.WorldTick);
        Assert.Equal(0, checkpoint.State.Clock.DayIndex);
        Assert.Equal(0, checkpoint.State.Clock.DayOfYear);
        Assert.Equal(1, checkpoint.State.Clock.MinuteOfDay);
        Assert.Equal(
            Enum.GetValues<KernelPhase>().Select(phase => (int)phase),
            checkpoint.Events.Where(worldEvent => worldEvent.Kind == "phase_completed")
                .Select(worldEvent => (int)worldEvent.Phase!.Value));
        Assert.Equal(
            Enumerable.Range(1, checkpoint.Events.Count).Select(number => (long)number),
            checkpoint.Events.Select(worldEvent => worldEvent.EventId));
        Assert.Equal(
            Enumerable.Repeat(1L, checkpoint.Events.Count),
            checkpoint.Events.Select(worldEvent => worldEvent.WorldTick));
    }

    [Fact]
    public void ClockCalendarUsesOnlySavedIntegralTickArithmetic()
    {
        var afterOneYearAndOneMinute = KernelClock.FromWorldTick((KernelClock.TicksPerDay * KernelClock.DaysPerYear) + 1);

        Assert.Equal(365, afterOneYearAndOneMinute.DayIndex);
        Assert.Equal(0, afterOneYearAndOneMinute.DayOfYear);
        Assert.Equal(1, afterOneYearAndOneMinute.MinuteOfDay);
        Assert.Equal(6, KernelClock.ScheduledTicksPerSecond);
        Assert.Equal(1, KernelClock.TicksPerMinute);
    }

    [Theory]
    [InlineData(KernelPhase.Ingress)]
    [InlineData(KernelPhase.RoutineWorkAndEconomy)]
    [InlineData(KernelPhase.Commit)]
    public void PauseRequestedAtAnyPhaseFinishesThatTickAndResumeIsIdempotent(KernelPhase phase)
    {
        var pausedResult = StagedTickKernel.Advance(
            KernelCheckpoint.Genesis,
            new KernelTickInput(4),
            new KernelTickOptions(PauseRequestAtPhase: phase));
        var paused = Assert.IsType<KernelCheckpoint>(pausedResult.Checkpoint);

        Assert.True(paused.State.IsPaused);
        Assert.Equal(1, paused.State.Clock.WorldTick);
        Assert.Equal(4, paused.State.Counter);
        Assert.Equal(10, paused.Events.Count(worldEvent => worldEvent.Kind == "phase_completed"));
        Assert.Contains(paused.Events, worldEvent =>
            worldEvent.Kind == "pause_requested" && worldEvent.Phase == phase);
        Assert.Contains(paused.Events, worldEvent => worldEvent.Kind == "paused");
        Assert.Throws<InvalidOperationException>(() =>
            StagedTickKernel.Advance(paused, new KernelTickInput(1)));

        var resumed = StagedTickKernel.Resume(paused);
        Assert.False(resumed.State.IsPaused);
        Assert.Equal(1, resumed.State.RunEpoch);
        Assert.Equal(1, resumed.State.Clock.WorldTick);
        Assert.Equal("resumed", resumed.Events[^1].Kind);
        Assert.Same(resumed, StagedTickKernel.Resume(resumed));

        var nextTick = Assert.IsType<KernelCheckpoint>(
            StagedTickKernel.Advance(resumed, new KernelTickInput(-2)).Checkpoint);
        Assert.Equal(2, nextTick.State.Clock.WorldTick);
        Assert.Equal(2, nextTick.State.Counter);
    }

    [Theory]
    [InlineData(KernelPhase.Ingress)]
    [InlineData(KernelPhase.RoutineWorkAndEconomy)]
    [InlineData(KernelPhase.Commit)]
    public void InterruptionAtEveryPhaseLeavesOnlyThePriorCheckpointAndRecoversDeterministically(KernelPhase phase)
    {
        var input = new KernelTickInput(9);
        var options = new KernelTickOptions(PauseRequestAtPhase: KernelPhase.ReservationsAndMovement);
        var complete = Assert.IsType<KernelCheckpoint>(
            StagedTickKernel.Advance(KernelCheckpoint.Genesis, input, options).Checkpoint);
        var interruptedResult = StagedTickKernel.Advance(
            KernelCheckpoint.Genesis,
            input,
            options with { InterruptAfterPhase = phase });
        var interrupted = Assert.IsType<KernelInterruptedTick>(interruptedResult.Interrupted);

        Assert.False(interruptedResult.IsCommitted);
        Assert.Equal(KernelDigest.State(KernelCheckpoint.Genesis.State), KernelDigest.State(interrupted.Checkpoint.State));
        Assert.Equal(KernelDigest.Events(KernelCheckpoint.Genesis.Events), KernelDigest.Events(interrupted.Checkpoint.Events));

        var recovered = Assert.IsType<KernelCheckpoint>(interrupted.Resume().Checkpoint);
        Assert.Equal(KernelDigest.State(complete.State), KernelDigest.State(recovered.State));
        Assert.Equal(KernelDigest.Events(complete.Events), KernelDigest.Events(recovered.Events));
    }

    [Fact]
    public void NamedPcgStreamIsStableAndCannotAccidentallyAliasAnotherStream()
    {
        var first = Pcg32XshRrV1.Create("fixture-seed", "weather");
        var same = Pcg32XshRrV1.Create("fixture-seed", "weather");
        var other = Pcg32XshRrV1.Create("fixture-seed", "resource:berry-patch");
        var actual = Enumerable.Range(0, 5).Select(_ => first.NextUInt()).ToArray();

        Assert.Equal("pcg32-xsh-rr-v1", Pcg32XshRrV1.AlgorithmId);
        Assert.Equal(
            new uint[] { 2_746_376_332, 2_393_862_774, 1_216_660_101, 2_255_332_717, 1_720_709_021 },
            actual);
        Assert.Equal(actual, Enumerable.Range(0, 5).Select(_ => same.NextUInt()).ToArray());
        Assert.NotEqual(actual, Enumerable.Range(0, 5).Select(_ => other.NextUInt()).ToArray());
    }
}

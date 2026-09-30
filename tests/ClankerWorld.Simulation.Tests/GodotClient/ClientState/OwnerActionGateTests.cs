using ClankerWorld.GodotClient.ClientState;

namespace ClankerWorld.Simulation.Tests;

public sealed class OwnerActionGateTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PauseWaitsForBusyActionAndCanBeFollowedByResume(bool firstFails)
    {
        var gate = new OwnerActionGate();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = new List<string>();
        var first = gate.RunAsync(async () =>
        {
            calls.Add("owner");
            await release.Task;
            if (firstFails) throw new IOException("held response failed");
        });
        var pause = gate.RunAsync(() => { calls.Add("pause"); return Task.CompletedTask; }, waitForTurn: true);
        var resume = gate.RunAsync(() => { calls.Add("resume"); return Task.CompletedTask; }, waitForTurn: true);
        Assert.False(await gate.RunAsync(() => throw new InvalidOperationException("Duplicate click must not execute.")));
        Assert.False(pause.IsCompleted);
        release.SetResult();
        if (firstFails) await Assert.ThrowsAsync<IOException>(() => first);
        else Assert.True(await first);
        Assert.True(await pause);
        Assert.True(await resume);
        Assert.Equal(["owner", "pause", "resume"], calls);
    }
}

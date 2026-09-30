namespace ClankerWorld.GodotClient.ClientState;

/// <summary>Pause/resume waits for an existing action; ordinary duplicate clicks do not queue.</summary>
public sealed class OwnerActionGate
{
    private readonly object sync = new();
    private Task tail = Task.CompletedTask;

    public async Task<bool> RunAsync(Func<Task> action, bool waitForTurn = false)
    {
        Task previous;
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (sync)
        {
            if (!waitForTurn && !tail.IsCompleted) return false;
            previous = tail;
            tail = finished.Task;
        }
        try
        {
            await previous;
            await action();
            return true;
        }
        finally { finished.SetResult(); }
    }
}

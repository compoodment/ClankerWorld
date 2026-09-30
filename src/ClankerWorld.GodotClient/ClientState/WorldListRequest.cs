using ClankerWorld.GodotClient.UI;

namespace ClankerWorld.GodotClient.ClientState;

/// <summary>Owns one visible catalog request; late replies cannot replace a newer screen.</summary>
public sealed class WorldListRequest : IDisposable
{
    private CancellationTokenSource? pending;
    public bool IsLoading { get; private set; }
    public WorldCatalogSnapshot? Catalog { get; private set; }
    public Exception? Failure { get; private set; }
    public event Action? Changed;

    public async Task RefreshAsync(Func<CancellationToken, Task<WorldCatalogSnapshot>> fetch)
    {
        Cancel();
        using var request = new CancellationTokenSource();
        pending = request;
        Catalog = null;
        Failure = null;
        IsLoading = true;
        Changed?.Invoke();
        try
        {
            var result = await fetch(request.Token);
            if (ReferenceEquals(pending, request)) Catalog = result;
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (ReferenceEquals(pending, request)) Failure = exception;
        }
        finally
        {
            if (ReferenceEquals(pending, request))
            {
                pending = null;
                IsLoading = false;
                Changed?.Invoke();
            }
        }
    }

    public void Cancel()
    {
        var previous = pending;
        pending = null;
        IsLoading = false;
        previous?.Cancel();
    }

    public void Dispose() => Cancel();
}

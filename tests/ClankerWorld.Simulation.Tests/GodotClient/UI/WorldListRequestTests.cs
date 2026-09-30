using ClankerWorld.GodotClient.ClientState;
using ClankerWorld.GodotClient.UI;

namespace ClankerWorld.Simulation.Tests;

public sealed class WorldListRequestTests
{
    [Fact]
    public async Task SlowFirstReplyPublishesWithoutReopeningAndPreservesCompatibility()
    {
        using var request = new WorldListRequest();
        var reply = new TaskCompletionSource<WorldCatalogSnapshot>();
        var changes = new List<bool>();
        request.Changed += () => changes.Add(request.IsLoading);
        var loading = request.RefreshAsync(_ => reply.Task);
        Assert.True(request.IsLoading);
        Assert.Null(request.Catalog);
        var catalog = Catalog("first");
        reply.SetResult(catalog);
        await loading;
        Assert.Collection(changes, value => Assert.True(value), value => Assert.False(value));
        Assert.Same(catalog, request.Catalog);
        Assert.Equal("incompatible", request.Catalog!.Worlds[0].Compatibility);
        Assert.False(request.IsLoading);
        Assert.Null(request.Failure);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReopenedScreenIgnoresOldCompletionEvenIfTransportIgnoresCancellation(bool oldFails)
    {
        using var request = new WorldListRequest();
        var oldReply = new TaskCompletionSource<WorldCatalogSnapshot>();
        CancellationToken oldToken = default;
        var old = request.RefreshAsync(token => { oldToken = token; return oldReply.Task; });
        request.Cancel();
        Assert.True(oldToken.IsCancellationRequested);
        await request.RefreshAsync(_ => Task.FromResult(Catalog("new")));
        if (oldFails) oldReply.SetException(new IOException("stale reply"));
        else oldReply.SetResult(Catalog("old"));
        await old;
        Assert.Equal("new", request.Catalog!.ActiveId);
        Assert.Null(request.Failure);
    }

    [Fact]
    public async Task ClosingCancelsAndPreventsLateRendering()
    {
        using var request = new WorldListRequest();
        var reply = new TaskCompletionSource<WorldCatalogSnapshot>();
        var rendered = 0;
        request.Changed += () => rendered++;
        var loading = request.RefreshAsync(_ => reply.Task);
        request.Cancel();
        reply.SetResult(Catalog("hidden"));
        await loading;
        Assert.Equal(1, rendered);
        Assert.Null(request.Catalog);
        Assert.False(request.IsLoading);
    }

    private static WorldCatalogSnapshot Catalog(string id) => new(id,
        [new CatalogWorld(id, id, id, "seed", DateTimeOffset.UnixEpoch, [], null,
            "incompatible", "Cannot restore this checkpoint.")]);
}

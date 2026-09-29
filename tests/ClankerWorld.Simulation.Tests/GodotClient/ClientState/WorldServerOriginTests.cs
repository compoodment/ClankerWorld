using ClankerWorld.GodotClient.ClientState;

namespace ClankerWorld.Simulation.Tests;

public sealed class WorldServerOriginTests
{
    [Theory]
    [InlineData("https://clanker.tail87ae72.ts.net:8443/")]
    [InlineData("https://EXAMPLE.test/")]
    [InlineData("http://[::1]:5188/")]
    public void AcceptsAnHttpsOriginOrLiteralLoopbackDevelopmentOrigin(string value)
    {
        var accepted = WorldServerOrigin.TryResolve(value, out var origin);

        Assert.True(accepted);
        Assert.True(origin.IsAbsoluteUri);
        Assert.Equal("/", origin.AbsolutePath);
    }

    [Theory]
    [InlineData("http://example.test:5188/")]
    [InlineData("https://example.test/path")]
    [InlineData("https://example.test/?redirect=https://other.test")]
    [InlineData("not a url")]
    public void RejectsAmbiguousOrUnapprovedOrigins(string value)
    {
        Assert.False(WorldServerOrigin.TryResolve(value, out _));
    }

    [Fact]
    public void ComparesCanonicalServerOriginsRatherThanPresentationText()
    {
        Assert.True(WorldServerOrigin.TryResolve("https://EXAMPLE.test/", out var left));
        Assert.True(WorldServerOrigin.TryResolve("https://example.test:443/", out var same));
        Assert.True(WorldServerOrigin.TryResolve("https://example.test:8443/", out var differentPort));

        Assert.True(WorldServerOrigin.Same(left, same));
        Assert.False(WorldServerOrigin.Same(left, differentPort));
    }
}

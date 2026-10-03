using System.Reflection;
using System.Text.Json;

namespace ClankerWorld.Simulation.Tests;

public sealed class ViewerHttpBuildInformationTests
{
    [Fact]
    public async Task HostStatusReportsItsAssemblyVersionAndFullSourceRevisionWithoutWorldData()
    {
        using var host = new ViewerWebApplicationFactory();
        using var client = host.CreateClient();
        using var response = await client.GetAsync("/api/v1/status");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var status = json.RootElement;
        var revision = status.GetProperty("sourceRevision").GetString()!;
        var version = status.GetProperty("version").GetString()!;
        Assert.Matches("^[0-9a-f]{40}$", revision);
        Assert.False(string.IsNullOrWhiteSpace(version));
        Assert.Equal(version + "+" + revision, typeof(Program).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion);
        Assert.Equal($"Build {version}+{revision[..7]}", status.GetProperty("build").GetString());
        Assert.Equal(["build", "sourceRevision", "version"], status.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
    }
}

using System.Text.Json;

namespace ClankerWorld.Simulation.Tests;

public sealed class RuntimeWriterBoundaryTests
{
    [Fact]
    public void InhabitantComponentsHaveOnlyTheirRecordedWriterFiles()
    {
        var root = RuntimeWriterScan.RepositoryRoot;
        var recorded = JsonSerializer.Deserialize<SortedDictionary<string, string[]>>(File.ReadAllText(Path.Combine(root,
            "tests/ClankerWorld.Simulation.Tests/Architecture/inhabitant-writers.json")))!;
        var actual = RuntimeWriterScan.FindWriters(root);
        Assert.Equal(actual.Keys, recorded.Keys);
        foreach (var (component, files) in actual)
        {
            var added = files.Except(recorded[component], StringComparer.Ordinal).ToArray();
            Assert.True(added.Length == 0, $"New {component} writers require review: {string.Join(", ", added)}");
            var removed = recorded[component].Except(files, StringComparer.Ordinal).ToArray();
            Assert.True(removed.Length == 0, $"Lower the {component} writer baseline: {string.Join(", ", removed)}");
        }
    }
}

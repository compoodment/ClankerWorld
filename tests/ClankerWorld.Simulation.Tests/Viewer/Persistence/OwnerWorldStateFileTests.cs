using ClankerWorld.Simulation.Harness;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class OwnerWorldStateFileTests
{
    [Fact]
    public void SavedRuntimeRefusesAConflictingConfiguredSeed()
    {
        var directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"clankerworld-phase-two-runtime-seed-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var stateFile = new OwnerWorldStateFile(
                System.IO.Path.Combine(directory, "runtime.json"));
            _ = stateFile.LoadOrCreate("camp-alpha");

            var exception = Assert.Throws<InvalidDataException>(() => stateFile.LoadOrCreate("other-world"));

            Assert.Contains("different configured world seed", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}

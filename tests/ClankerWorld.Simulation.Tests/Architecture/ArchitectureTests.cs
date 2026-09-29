using ClankerWorld.Simulation;

namespace ClankerWorld.Simulation.Tests;

public sealed class ArchitectureTests
{
    [Fact]
    public void SimulationAssemblyDoesNotReferenceGodotOrTheViewer()
    {
        var references = typeof(SimulationAssemblyMarker)
            .Assembly
            .GetReferencedAssemblies();

        Assert.DoesNotContain(
            references,
            reference => reference.Name?.StartsWith("Godot", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(
            references,
            reference => reference.Name?.StartsWith("ClankerWorld.Viewer", StringComparison.Ordinal) == true);
    }
}

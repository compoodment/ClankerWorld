using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

internal static class DerivedChecks
{
    [ModuleInitializer]
    [SuppressMessage("Usage", "CA2255:The ModuleInitializer attribute should not be used in libraries",
        Justification = "The test assembly verifies every derived cache hit from process startup.")]
    internal static void Enable() => DerivedVerification.RecomputeOnHit = true;
}

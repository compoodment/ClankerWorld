using System.Runtime.CompilerServices;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>Optional checks for pure snapshot-derived values; never saved.</summary>
public static class DerivedVerification
{
    public static bool RecomputeOnHit { get; set; }
}

/// <summary>Weakly caches one pure value for each immutable snapshot identity.</summary>
public sealed class Derived<TKey, TValue>(Func<TKey, TValue> compute, Func<TValue, TValue, bool> equal)
    where TKey : class where TValue : class
{
    private readonly ConditionalWeakTable<TKey, TValue> values = new();

    public TValue Get(TKey key)
    {
        if (!values.TryGetValue(key, out var value))
            return values.GetValue(key, snapshot => compute(snapshot));
        if (DerivedVerification.RecomputeOnHit && !equal(value, compute(key)))
            throw new InvalidOperationException("A snapshot-derived value differs from its recomputation.");
        return value;
    }
}

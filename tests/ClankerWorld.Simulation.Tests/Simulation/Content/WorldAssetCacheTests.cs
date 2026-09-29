using ClankerWorld.Simulation.Content;

namespace ClankerWorld.Simulation.Tests;

public sealed class WorldAssetCacheTests
{
    [Fact]
    public void CacheEvictsTheDeterministicLeastRecentlyUsedEntry()
    {
        var ledger = Ledger(100);
        var first = Key('a');
        var second = Key('b');
        var third = Key('c');

        Assert.True(ledger.TryEnsureResident(first, 60, 60, 1).IsSuccess);
        Assert.True(ledger.TryEnsureResident(second, 30, 30, 2).IsSuccess);
        var result = ledger.TryEnsureResident(third, 30, 30, 3);

        Assert.True(result.IsSuccess, result.Diagnostic);
        Assert.Equal([first], result.Evicted);
        Assert.Equal(2, result.Totals.EntryCount);
        Assert.Equal(60, result.Totals.DecodedCacheBytes);
        Assert.DoesNotContain(first, ledger.ExportState().Entries.Select(item => item.Key));
    }

    [Fact]
    public void CurrentFramePinsAreNeverEvicted()
    {
        var ledger = Ledger(100);
        var pinned = Key('a');
        var evictable = Key('b');
        var incoming = Key('c');

        Assert.True(ledger.TryEnsureResident(pinned, 60, 60, 1, pinForFrame: true).IsSuccess);
        Assert.True(ledger.TryEnsureResident(evictable, 30, 30, 1).IsSuccess);
        var result = ledger.TryEnsureResident(incoming, 30, 30, 1);

        Assert.True(result.IsSuccess, result.Diagnostic);
        Assert.Equal([evictable], result.Evicted);
        Assert.Contains(pinned, ledger.ExportState().Entries.Select(item => item.Key));
        Assert.Equal(90, result.Totals.DecodedCacheBytes);
    }

    [Fact]
    public void AFullPinnedCacheRejectsWithoutMutatingExistingEntries()
    {
        var ledger = Ledger(100);
        var first = Key('a');
        var second = Key('b');
        var incoming = Key('c');
        Assert.True(ledger.TryEnsureResident(first, 70, 70, 5, pinForFrame: true).IsSuccess);
        Assert.True(ledger.TryEnsureResident(second, 30, 30, 5, pinForFrame: true).IsSuccess);
        var before = ledger.ExportState();

        var result = ledger.TryEnsureResident(incoming, 1, 1, 5);

        Assert.False(result.IsSuccess);
        Assert.Equal("world_cache_breach", result.FailureCode);
        Assert.True(before.Entries.SequenceEqual(ledger.ExportState().Entries));
    }

    [Fact]
    public void ExpiredPinsCanBeClearedAndCacheStateRoundTrips()
    {
        var ledger = Ledger(100);
        var key = Key('a', "indexed");
        Assert.True(ledger.TryEnsureResident(key, 20, 30, 4, pinForFrame: true).IsSuccess);
        Assert.Equal(1, ledger.ClearExpiredPins(5));

        var encoded = System.Text.Json.JsonSerializer.Serialize(ledger.ExportState());
        var restored = WorldAssetCacheLedger.Restore(
            System.Text.Json.JsonSerializer.Deserialize<WorldAssetCacheState>(encoded));

        Assert.Equal(ledger.Totals, restored.Totals);
        Assert.Equal(ledger.ExportState().Entries, restored.ExportState().Entries);
        restored.Validate();
    }

    private static WorldAssetCacheLedger Ledger(long limit) => new(new WorldAssetReservationPolicy
    {
        MaxDecodedCacheBytes = limit,
        MaxGpuBytes = limit,
        MaxDurableStorageBytes = 1,
        MaxRenderUnits = 1,
    });

    private static WorldAssetCacheKey Key(char digestCharacter, string profile = "rgba8") =>
        new("sha256:" + new string(digestCharacter, 64), profile);
}

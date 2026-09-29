using ClankerWorld.Simulation.Content;

namespace ClankerWorld.Simulation.Tests;

public sealed class WorldAssetReservationTests
{
    private const string PackageDigestA =
        "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private const string PackageDigestB =
        "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    private const string SharedNormalizedDigest =
        "sha256:cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";

    [Fact]
    public void SharedNormalizedDigestsAreChargedOnceButReferencesStillChargeRenderWork()
    {
        var ledger = new WorldAssetReservationLedger(new WorldAssetReservationPolicy
        {
            MaxDurableStorageBytes = 100,
            MaxDecodedCacheBytes = 100,
            MaxGpuBytes = 100,
            MaxRenderUnits = 8,
        });

        var first = ledger.TryReservePackage(
            "first",
            [Request(PackageDigestA, "portrait-a", SharedNormalizedDigest, 50, 2)],
            0);
        var second = ledger.TryReservePackage(
            "second",
            [Request(PackageDigestB, "portrait-b", SharedNormalizedDigest, 50, 3)],
            1);

        Assert.True(first.IsSuccess, first.Diagnostic);
        Assert.True(second.IsSuccess, second.Diagnostic);
        Assert.Equal(1, second.Totals.DistinctAssetCount);
        Assert.Equal(50, second.Totals.DurableStorageBytes);
        Assert.Equal(50, second.Totals.DecodedCacheBytes);
        Assert.Equal(50, second.Totals.GpuBytes);
        Assert.Equal(5, second.Totals.RenderUnits);
    }

    [Fact]
    public void WorldReservationBudgetFailureIsAtomic()
    {
        var ledger = new WorldAssetReservationLedger(new WorldAssetReservationPolicy
        {
            MaxDurableStorageBytes = 10,
            MaxDecodedCacheBytes = 10,
            MaxGpuBytes = 10,
            MaxRenderUnits = 4,
        });
        Assert.True(ledger.TryReservePackage("first", [Request(PackageDigestA, "one", PackageDigestA, 10, 1)], 0).IsSuccess);

        var rejected = ledger.TryReservePackage(
            "second",
            [Request(PackageDigestB, "two", PackageDigestB, 1, 1)],
            1);

        Assert.False(rejected.IsSuccess);
        Assert.Equal("world_storage_breach", rejected.FailureCode);
        Assert.Single(ledger.ExportState().Reservations);
        Assert.Empty(ledger.GetPackageReservations("second"));
    }

    [Fact]
    public void WorldReservationLedgerRoundTripsCanonicallyAndReleasesAWholePackage()
    {
        var ledger = new WorldAssetReservationLedger();
        Assert.True(ledger.TryReservePackage("first", [Request(PackageDigestA, "one", PackageDigestA, 1, 1)], 0).IsSuccess);
        Assert.True(ledger.TryReservePackage("second", [Request(PackageDigestB, "two", PackageDigestB, 1, 1)], 1).IsSuccess);

        var encoded = System.Text.Json.JsonSerializer.Serialize(ledger.ExportState());
        var restored = WorldAssetReservationLedger.Restore(
            System.Text.Json.JsonSerializer.Deserialize<WorldAssetReservationLedgerState>(encoded));

        Assert.Equal(ledger.ExportState().Reservations, restored.ExportState().Reservations);
        Assert.Equal(ledger.ExportState().Events, restored.ExportState().Events);
        Assert.True(restored.ReleasePackage("first", 2));
        Assert.Single(restored.ExportState().Reservations);
        restored.Validate();
    }

    [Fact]
    public void PrefixRelatedPackageIdsRestoreInTheSameStructuralOrderAsExport()
    {
        var ledger = new WorldAssetReservationLedger();
        Assert.True(ledger.TryReservePackage("a", [Request(PackageDigestA, "a", PackageDigestA, 1, 1)], 0).IsSuccess);
        Assert.True(ledger.TryReservePackage("aa", [Request(PackageDigestB, "aa", PackageDigestB, 1, 1)], 1).IsSuccess);
        var restored = WorldAssetReservationLedger.Restore(ledger.ExportState());
        Assert.Equal(ledger.ExportState().Reservations, restored.ExportState().Reservations);
    }

    [Theory]
    [InlineData("durable")]
    [InlineData("gpu")]
    public void SharedChargesMustAgreeAndRejectionIsAtomic(string charge)
    {
        var ledger = new WorldAssetReservationLedger();
        Assert.True(ledger.TryReservePackage("a", [Request(PackageDigestA, "a", SharedNormalizedDigest, 1, 1)], 0).IsSuccess);
        var request = Request(PackageDigestB, "b", SharedNormalizedDigest, 1, 1);
        request = charge switch
        {
            "durable" => request with { DurableStorageBytes = 2 },
            "cache" => request with { DecodedCacheBytes = 2 },
            _ => request with { GpuBytes = 2 },
        };
        var before = ledger.ExportState();
        var result = ledger.TryReservePackage("b", [request], 1);
        Assert.False(result.IsSuccess);
        Assert.Equal("asset_reservation_conflicting_charge", result.FailureCode);
        Assert.Equal(before.Reservations, ledger.ExportState().Reservations);
        Assert.Equal(before.Events, ledger.ExportState().Events);
    }

    [Theory]
    [InlineData("durable")]
    [InlineData("gpu")]
    public void AggregateOverflowReturnsADiagnosticWithoutMutation(string charge)
    {
        var ledger = new WorldAssetReservationLedger(new WorldAssetReservationPolicy
        {
            MaxDurableStorageBytes = long.MaxValue,
            MaxDecodedCacheBytes = long.MaxValue,
            MaxGpuBytes = long.MaxValue,
            MaxRenderUnits = 4,
        });
        var requests = new[] { Request(PackageDigestA, "a", PackageDigestA, 1, 1), Request(PackageDigestB, "b", PackageDigestB, 1, 1) }
            .Select(request => charge switch
            {
                "durable" => request with { DurableStorageBytes = long.MaxValue },
                "cache" => request with { DecodedCacheBytes = long.MaxValue },
                _ => request with { GpuBytes = long.MaxValue },
            }).ToArray();
        Assert.Equal("asset_reservation_overflow", ledger.TryReservePackage("large", requests, 0).FailureCode);
        Assert.Empty(ledger.ExportState().Reservations);
        Assert.Empty(ledger.ExportState().Events);
        Assert.True(ledger.TryReservePackage("limit", requests.Take(1), 0).IsSuccess);
    }

    private static WorldAssetReservationRequest Request(
        string packageDigest,
        string localId,
        string normalizedDigest,
        long bytes,
        int renderUnits) => new(
        AssetRules.CanonicalAssetId(packageDigest, localId, ContentVersion.Parse("1.0.0")),
        normalizedDigest,
        "rgba8",
        bytes,
        bytes,
        bytes,
        renderUnits);
}

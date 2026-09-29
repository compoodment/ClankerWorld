using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Control;
using ClankerWorld.Viewer.Observation;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ClankerWorld.Simulation.Tests;

public sealed class ViewerWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string stateDirectory;
    private readonly bool ownsStateDirectory;
    private readonly string? approvedAssetCatalogPath;
    private readonly bool privateWorld;
    private readonly bool legacyPrivateWorld;
    private readonly bool configureProviderUsagePath;

    public ViewerWebApplicationFactory()
        : this(null)
    {
    }

    internal ViewerWebApplicationFactory(
        string? persistedStateDirectory,
        string? approvedAssetCatalogPath = null,
        bool privateWorld = false,
        bool legacyPrivateWorld = true,
        bool configureProviderUsagePath = true)
    {
        ownsStateDirectory = persistedStateDirectory is null;
        stateDirectory = persistedStateDirectory ?? System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"clankerworld-viewer-http-{Guid.NewGuid():N}");
        this.approvedAssetCatalogPath = approvedAssetCatalogPath;
        this.privateWorld = privateWorld;
        this.legacyPrivateWorld = legacyPrivateWorld;
        this.configureProviderUsagePath = configureProviderUsagePath;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(stateDirectory);
        // Existing protocol tests exercise a populated world. New-world setup has its
        // own integration test; seed the older fixture explicitly instead of quietly
        // depending on the production host's default genesis.
        if (privateWorld && legacyPrivateWorld && !File.Exists(System.IO.Path.Combine(stateDirectory, "runtime.json")))
        {
            using var seeded = new PrivateWorldStateFile(System.IO.Path.Combine(stateDirectory, "runtime.json"),
                newWorldPace: WorldStartPace.Legacy).LoadOrCreate(SeededWorldObservationStore.SampleSeed);
        }
        // The compatibility suite uses fixture mode; selected tests opt into
        // the integrated private runtime through the same real host boundary.
        builder.UseSetting("ClankerWorld:Runtime:WorldMode", privateWorld ? "private" : "fixture");
        builder.UseSetting("ClankerWorld:Runtime:AdvanceScript", "false");
        builder.UseSetting("ClankerWorld:Pairing:StatePath", System.IO.Path.Combine(stateDirectory, "authority.json"));
        builder.UseSetting("ClankerWorld:Runtime:StatePath", System.IO.Path.Combine(stateDirectory, "runtime.json"));
        builder.UseSetting(
            "ClankerWorld:Runtime:ProviderStatePath",
            System.IO.Path.Combine(stateDirectory, "provider-configuration.json"));
        if (configureProviderUsagePath)
            builder.UseSetting("ClankerWorld:Runtime:ProviderUsagePath",
                System.IO.Path.Combine(stateDirectory, "provider-usage.json"));
        builder.UseSetting("ClankerWorld:Pairing:ServerAuthorityId", "authority-http-tests");
        if (!string.IsNullOrWhiteSpace(approvedAssetCatalogPath))
        {
            builder.UseSetting("ClankerWorld:Assets:CatalogPath", approvedAssetCatalogPath);
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && ownsStateDirectory && Directory.Exists(stateDirectory))
        {
            Directory.Delete(stateDirectory, recursive: true);
        }
    }
}

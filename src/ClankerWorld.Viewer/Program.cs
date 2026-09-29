using System.Net;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;
using ClankerWorld.Viewer.Control;
using ClankerWorld.Viewer.Endpoints;
using ClankerWorld.Viewer.Observation;

var builder = WebApplication.CreateBuilder(args);
var runtimeSeed = builder.Configuration["ClankerWorld:Runtime:Seed"] ?? SeededWorldObservationStore.SampleSeed;
var configuredWorldMode = builder.Configuration["ClankerWorld:Runtime:WorldMode"] ?? "private";
if (!string.Equals(configuredWorldMode, "private", StringComparison.OrdinalIgnoreCase) &&
    !string.Equals(configuredWorldMode, "fixture", StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException(
        $"Unsupported ClankerWorld:Runtime:WorldMode '{configuredWorldMode}'. Expected private or fixture.");
}

var isPrivateWorld = string.Equals(configuredWorldMode, "private", StringComparison.OrdinalIgnoreCase);
var advanceFixture = builder.Configuration.GetValue<bool>("ClankerWorld:Runtime:AdvanceScript");
var advanceRuntime = isPrivateWorld
    ? builder.Configuration.GetValue("ClankerWorld:Runtime:AdvanceScript", true)
    : advanceFixture;
var clientPresenceTimeoutSeconds = builder.Configuration.GetValue(
    "ClankerWorld:Runtime:ClientPresenceTimeoutSeconds",
    5);
if (clientPresenceTimeoutSeconds is < 2 or > 60)
{
    throw new InvalidOperationException(
        "ClankerWorld:Runtime:ClientPresenceTimeoutSeconds must be between 2 and 60 seconds.");
}
var configuredDecisionProvider = builder.Configuration["ClankerWorld:Runtime:DecisionProvider"] ?? "deterministic";
var configuredJevModel = builder.Configuration["ClankerWorld:Runtime:JevModel"] ?? "jev-1.13.0";
var configuredModel = builder.Configuration["ClankerWorld:Runtime:Model"];
var configuredOpenAiModel = builder.Configuration["ClankerWorld:Runtime:OpenAiModel"] ??
    (configuredDecisionProvider.StartsWith("openai", StringComparison.OrdinalIgnoreCase) ? configuredModel : null);
var configuredOllamaCloudModel = builder.Configuration["ClankerWorld:Runtime:OllamaCloudModel"] ??
    (configuredDecisionProvider.StartsWith("ollama", StringComparison.OrdinalIgnoreCase) ? configuredModel : null);
var publicPort = builder.Configuration.GetValue("ClankerWorld:Http:Port", 5188);
var localApprovalPort = builder.Configuration.GetValue<int?>("ClankerWorld:Pairing:LocalApprovalPort") ?? 0;
if (publicPort is <= 0 or > 65535 || localApprovalPort is < 0 or > 65535 || localApprovalPort == publicPort)
{
    throw new InvalidOperationException("ClankerWorld HTTP ports must be distinct values between 1 and 65535.");
}

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 30_000_000;
    options.Listen(IPAddress.Loopback, publicPort);
    if (localApprovalPort > 0)
    {
        options.Listen(IPAddress.Loopback, localApprovalPort);
    }
});

var authorityStatePath = builder.Configuration["ClankerWorld:Pairing:StatePath"] ??
    Path.Combine(builder.Environment.ContentRootPath, "saves", "owner-authority.json");
var configuredRuntimeStatePath = builder.Configuration["ClankerWorld:Runtime:StatePath"];
var runtimeStatePath = configuredRuntimeStatePath ??
    Path.Combine(
        builder.Environment.ContentRootPath,
        "saves",
        isPrivateWorld ? "private-world.json" : "fixture-runtime.json");
var legacyRuntimeStatePath = builder.Configuration["ClankerWorld:Runtime:LegacyStatePath"] ??
    Path.Combine(builder.Environment.ContentRootPath, "saves", "fixture-runtime.json");
var privateRuntimeStatePath = isPrivateWorld
    ? runtimeStatePath
    : builder.Configuration["ClankerWorld:Runtime:PrivateStatePath"] ??
        Path.Combine(builder.Environment.ContentRootPath, "saves", "private-world.json");
var providerConfigurationPath = builder.Configuration["ClankerWorld:Runtime:ProviderStatePath"] ??
    Path.Combine(builder.Environment.ContentRootPath, "saves", "provider-configuration.json");
var providerUsagePath = builder.Configuration["ClankerWorld:Runtime:ProviderUsagePath"] ??
    Path.Combine(Path.GetDirectoryName(Path.GetFullPath(providerConfigurationPath))!, "provider-usage.json");
var approvedAssetCatalogPath = builder.Configuration["ClankerWorld:Assets:CatalogPath"] ??
    Path.Combine(builder.Environment.ContentRootPath, "approved-assets.json");
var configuredAuthorityId = builder.Configuration["ClankerWorld:Pairing:ServerAuthorityId"] ??
    $"clankerworld-host:{Environment.MachineName}";

// The catalog is loaded only from a host-owned path at startup. Its absence
// intentionally yields an empty, deny-all allow-list; malformed existing
// catalog files stop startup rather than becoming a partial approval set.
var approvedAssetCatalog = ApprovedAssetCatalog.LoadOrDeny(approvedAssetCatalogPath);
builder.Services.AddSingleton<IOwnerApprovedAssetReferencePolicy>(approvedAssetCatalog);
builder.Services.AddSingleton(approvedAssetCatalog);
builder.Services.AddHttpClient("typesafe");
builder.Services.AddHttpClient("model");
builder.Services.AddSingleton<WorldJevPolicy>();
builder.Services.AddSingleton(new ProviderUsageStore(providerUsagePath));
builder.Services.AddSingleton(new ProviderConfigurationStore(
    providerConfigurationPath,
    new ProviderConfigurationSeed(
        configuredDecisionProvider,
        configuredJevModel,
        Environment.GetEnvironmentVariable("TYPESAFE_API_KEY"),
        configuredOpenAiModel,
        Environment.GetEnvironmentVariable("OPENAI_API_KEY"),
        configuredOllamaCloudModel,
        Environment.GetEnvironmentVariable("OLLAMA_API_KEY"))));
builder.Services.AddSingleton<ConfigurableDecisionProvider>();
builder.Services.AddSingleton<IDecisionProvider>(services =>
    services.GetRequiredService<ConfigurableDecisionProvider>());
builder.Services.AddSingleton<OwnerWorldStateFile>(services => new OwnerWorldStateFile(
    isPrivateWorld ? legacyRuntimeStatePath : runtimeStatePath,
    approvedAssetCatalog,
    services.GetRequiredService<IDecisionProvider>()));
builder.Services.AddSingleton<OwnerWorldRuntime>(services => services
    .GetRequiredService<OwnerWorldStateFile>()
    .LoadOrCreate(runtimeSeed));
builder.Services.AddSingleton<PrivateWorldStateFile>(services => new PrivateWorldStateFile(
    privateRuntimeStatePath,
    _ => services.GetRequiredService<IDecisionProvider>(),
    WorldStartPace.FounderSetup,
    allowDifferentSavedSeed: isPrivateWorld));
builder.Services.AddSingleton(services => new ManualWorldSaveStore(privateRuntimeStatePath,
    services.GetRequiredService<ILogger<ManualWorldSaveStore>>()));
builder.Services.AddSingleton<PrivateWorldRuntime>(services =>
{
    var runtime = services.GetRequiredService<PrivateWorldStateFile>().LoadOrCreate(runtimeSeed);
    services.GetRequiredService<WorldJevPolicy>().Initialize(runtime.JevEnabled, runtime.JevPolicyRevision);
    return runtime;
});
builder.Services.AddSingleton<WorldAutosaveStore>(services => new WorldAutosaveStore(
    privateRuntimeStatePath, services.GetRequiredService<PrivateWorldRuntime>().Society.WorldId,
    allowWorldSwitch: isPrivateWorld));
if (isPrivateWorld)
{
    builder.Services.AddSingleton<WorldCatalogStore>(services =>
    {
        var providers = services.GetRequiredService<ProviderConfigurationStore>();
        var autosave = services.GetRequiredService<WorldAutosaveStore>();
        var catalog = new WorldCatalogStore(privateRuntimeStatePath,
            services.GetRequiredService<PrivateWorldRuntime>().ExportState(),
            providers.CaptureRuntimeConfiguration().Assignments ?? [], autosave.Capture());
        var active = catalog.Active();
        if (catalog.RecoveredSelection)
        {
            if (!(providers.CaptureRuntimeConfiguration().Assignments ?? [])
                .SequenceEqual(active.Assignments))
                providers.RestoreWorldAssignments(active.Assignments);
            if (active.AutosaveSettings is { } settings && autosave.Capture() != settings)
                autosave.SelectWorld(active.WorldId, settings);
        }
        return catalog;
    });
    builder.Services.AddSingleton<WorldSelectionCoordinator>(services => new WorldSelectionCoordinator(
        services.GetRequiredService<WorldCatalogStore>(),
        services.GetRequiredService<PrivateWorldRuntime>(),
        services.GetRequiredService<PrivateWorldStateFile>(),
        services.GetRequiredService<ProviderConfigurationStore>(),
        services.GetRequiredService<WorldAutosaveStore>(),
        services.GetRequiredService<WorldJevPolicy>(),
        services.GetRequiredService<ILogger<WorldSelectionCoordinator>>(),
        _ => services.GetRequiredService<IDecisionProvider>()));
}
builder.Services.AddSingleton<OwnerWorldObservationStore>(services => isPrivateWorld
    ? new OwnerWorldObservationStore(services.GetRequiredService<PrivateWorldRuntime>())
    : new OwnerWorldObservationStore(services.GetRequiredService<OwnerWorldRuntime>()));
builder.Services.AddSingleton(new OwnerAuthorityStateFile(authorityStatePath));
var pairingHostOptions = new OwnerPairingHostOptions(localApprovalPort);
builder.Services.AddSingleton(pairingHostOptions);
builder.Services.AddSingleton<PairingRequestBudget>();
builder.Services.AddSingleton<OwnerAuthorityStore>(services =>
{
    var worldId = isPrivateWorld
        ? services.GetRequiredService<PrivateWorldRuntime>().Society.WorldId
        : services.GetRequiredService<OwnerWorldRuntime>().Capture().Snapshot.World.Identity.WorldId;
    return services.GetRequiredService<OwnerAuthorityStateFile>().LoadOrCreate(
        new OwnerAuthorityIdentity(configuredAuthorityId, worldId),
        allowWorldSwitch: isPrivateWorld);
});
builder.Services.AddSingleton<OwnerRequestAuthorizer>();
builder.Services.AddSingleton(new OwnerClientPresenceLease(
    TimeSpan.FromSeconds(clientPresenceTimeoutSeconds)));
if (advanceRuntime)
{
    if (isPrivateWorld)
    {
        builder.Services.AddHostedService(services => new PrivateWorldRuntimeService(
            services.GetRequiredService<PrivateWorldRuntime>(),
            services.GetRequiredService<PrivateWorldStateFile>(),
            services.GetRequiredService<OwnerClientPresenceLease>(),
            services.GetRequiredService<ILogger<PrivateWorldRuntimeService>>(),
            services.GetRequiredService<WorldAutosaveStore>(),
            services.GetRequiredService<ManualWorldSaveStore>(),
            services.GetRequiredService<ProviderConfigurationStore>()));
    }
    else
    {
        builder.Services.AddHostedService<OwnerWorldRuntimeService>();
    }
}

var app = builder.Build();
// Recover an interrupted selection before hosted services, requests or model dispatch.
if (isPrivateWorld) _ = app.Services.GetRequiredService<WorldCatalogStore>();
if (app.Services.GetRequiredService<ProviderUsageStore>().Capture().AccountingError is not null)
    ProviderUsageTelemetry.AccountingBlocked(app.Logger);
ProviderCredentialTelemetry.Ready(app.Logger, OperatingSystem.IsWindows() ? "windows_current_user" : "private_file_permissions");
app.Services.GetRequiredService<ProviderUsageStore>().LimitReached += () =>
{
    if (isPrivateWorld)
    {
        var runtime = app.Services.GetRequiredService<PrivateWorldRuntime>();
        runtime.Pause();
        app.Services.GetRequiredService<PrivateWorldStateFile>().Save(runtime);
        ProviderUsageTelemetry.LimitReached(app.Logger, runtime.WorldTick);
    }
    else
    {
        var runtime = app.Services.GetRequiredService<OwnerWorldRuntime>();
        if (runtime.Pause("provider_usage_limit"))
            app.Services.GetRequiredService<OwnerWorldStateFile>().Save(runtime);
        ProviderUsageTelemetry.LimitReached(app.Logger, 0);
    }
};
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapOwnerEndpoints(isPrivateWorld);

app.Run();

public partial class Program;

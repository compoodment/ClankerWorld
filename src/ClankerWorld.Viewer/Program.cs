using System.Net;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;
using ClankerWorld.Viewer.Control;
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
var founderSetupGate = app.Services.GetRequiredService<ProviderConfigurationStore>().WorldMutationGate;
app.UseDefaultFiles();
app.UseStaticFiles();

// This is deliberately discovery-only. Full owner capabilities are returned
// only inside the authenticated reconnect baseline below.
app.MapGet("/api/v1/handshake", () => Results.Ok(new ViewerHandshake(
    new ProtocolVersion(Major: 1, Minor: 1),
    ["owner-device-pairing.v1"],
    ["owner-device-pairing.v1"])));

// Bound unauthenticated creation before JSON binding. Do not spend this budget
// on signed owner challenges/reconnect/pause or host-local recovery.
app.Use(async (context, next) =>
{
    var pairingRoute = context.Request.Path.StartsWithSegments("/api/v1/pairings") ||
        context.Request.Path.StartsWithSegments("/api/v1/local/pairings");
    if (pairingRoute)
    {
        const long maximumPairingBytes = 16 * 1024;
        if (context.Request.ContentLength > maximumPairingBytes)
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            return;
        }
        var bodyLimit = context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
        if (bodyLimit is { IsReadOnly: false }) bodyLimit.MaxRequestBodySize = maximumPairingBytes;
    }
    if (HttpMethods.IsPost(context.Request.Method) && context.Request.Path == "/api/v1/pairings" &&
        !context.RequestServices.GetRequiredService<PairingRequestBudget>().TryAcquire())
    {
        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.Response.Headers.RetryAfter = "60";
        return;
    }
    await next(context);
});

app.MapPost("/api/v1/local/pairings", (
    StartOwnerPairingHttpRequest request, HttpContext context, OwnerPairingHostOptions options,
    OwnerAuthorityStore authority, OwnerAuthorityStateFile stateFile) =>
{
    if (!options.IsLocalApprovalRequest(context)) return Results.NotFound();
    var result = authority.StartPairingLocally(new OwnerPairingRequest(request?.PublicKeySpkiBase64 ?? string.Empty));
    stateFile.Save(authority);
    PairingRequestBudget.LogLocalRecovery(app.Logger, result.IsSuccess ? "created" : "refused");
    return result.IsSuccess ? Results.Ok(result.Value) : OwnerFailures.ToHttpResult(result.Failure);
});

app.MapPost("/api/v1/pairings", (
    StartOwnerPairingHttpRequest request,
    OwnerAuthorityStore authority,
    OwnerAuthorityStateFile stateFile) =>
{
    var result = authority.StartPairing(new OwnerPairingRequest(request?.PublicKeySpkiBase64 ?? string.Empty));
    // Start/expiry trimming changes operational authority state even when a
    // new pairing is refused for capacity. Persist that bounded state before
    // returning either outcome.
    stateFile.Save(authority);
    if (!result.IsSuccess)
    {
        return OwnerFailures.ToHttpResult(result.Failure);
    }

    return Results.Ok(result.Value);
});

app.MapGet("/api/v1/pairings/{pairingId}", (
    string pairingId,
    OwnerAuthorityStore authority,
    OwnerAuthorityStateFile stateFile) =>
{
    var result = authority.GetPairingStatus(pairingId);
    // Polling can cause a pending record to become expired, so retain that
    // state before replying.
    stateFile.Save(authority);
    return result.IsSuccess
        ? Results.Ok(result.Value)
        : OwnerFailures.ToHttpResult(result.Failure);
});

app.MapPost("/api/v1/pairings/activate", (
    ActivateOwnerPairingHttpRequest request,
    OwnerAuthorityStore authority,
    OwnerAuthorityStateFile stateFile) =>
{
    var result = authority.ActivatePairing(new OwnerPairingActivationRequest(
        request?.PairingId ?? string.Empty,
        request?.CanonicalProof ?? string.Empty,
        request?.SignatureBase64 ?? string.Empty));
    // Expiry is applied while evaluating activation, including rejected
    // activation requests, so persist before returning the result.
    stateFile.Save(authority);
    if (!result.IsSuccess)
    {
        return OwnerFailures.ToHttpResult(result.Failure);
    }

    return Results.Ok(result.Value);
});

// This route is served only by the second loopback-only listener configured by
// the deployment. Tailscale Serve forwards the normal viewer port, never this
// approval port, so a remote client cannot bootstrap itself.
app.MapPost("/api/v1/local/pairings/{pairingId}/approve", (
    string pairingId,
    LocalPairingApprovalHttpRequest request,
    HttpContext context,
    OwnerPairingHostOptions options,
    OwnerAuthorityStore authority,
    OwnerAuthorityStateFile stateFile) =>
{
    if (!options.IsLocalApprovalRequest(context))
    {
        return Results.NotFound();
    }

    var result = authority.ApprovePendingPairingLocally(pairingId, request?.PairingCode ?? string.Empty);
    // A mismatch increments the durable bounded-attempt counter, so a restart
    // must not erase unsuccessful approval attempts.
    stateFile.Save(authority);
    if (!result.IsSuccess)
    {
        return OwnerFailures.ToHttpResult(result.Failure);
    }

    return Results.Ok(result.Value);
});

// Recovery deliberately shares the separate host-local listener with first
// pairing approval. If every paired Windows device is lost or revoked, the
// host can still revoke a stale public key before pairing a replacement; this
// path is not forwarded by Tailscale Serve.
app.MapPost("/api/v1/local/devices/{deviceId}/revoke", (
    string deviceId,
    LocalDeviceRevokeHttpRequest _,
    HttpContext context,
    OwnerPairingHostOptions options,
    OwnerAuthorityStore authority,
    OwnerAuthorityStateFile stateFile) =>
{
    if (!options.IsLocalApprovalRequest(context))
    {
        return Results.NotFound();
    }

    var result = authority.RevokeDeviceLocally(deviceId);
    stateFile.Save(authority);
    if (!result.IsSuccess)
    {
        return OwnerFailures.ToHttpResult(result.Failure);
    }

    return Results.Ok(result.Value);
});

app.MapPost("/api/v1/owner/challenges", (
    IssueOwnerChallengeHttpRequest request,
    OwnerAuthorityStore authority,
    OwnerAuthorityStateFile stateFile) =>
{
    var result = authority.IssueChallenge(new OwnerChallengeIssueRequest(
        request?.DeviceId ?? string.Empty,
        request?.RequestId ?? string.Empty,
        request?.CanonicalProof ?? string.Empty,
        request?.SignatureBase64 ?? string.Empty));
    // Challenge expiry/retention trimming also happens on rejected requests.
    stateFile.Save(authority);
    if (!result.IsSuccess)
    {
        return OwnerFailures.ToHttpResult(result.Failure);
    }

    return Results.Ok(result.Value);
});

app.MapPost("/api/v1/owner/reconnect", (
    OwnerSignedHttpRequest<OwnerReconnectAction> request,
    OwnerRequestAuthorizer authorizer,
    OwnerWorldObservationStore observations,
    OwnerClientPresenceLease clientPresence) =>
{
    if (request?.Action is null || request.Action.AfterEventId < 0)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["afterEventId"] = ["The event cursor cannot be negative."],
        });
    }

    if ((request.Action.KnownTerrainWorldId is null) != (request.Action.KnownTerrainDigest is null) ||
        request.Action.KnownTerrainWorldId is { } worldId && (string.IsNullOrWhiteSpace(worldId) || worldId.Length > 256) ||
        request.Action.KnownTerrainDigest is { } digest &&
            (digest.Length != 64 || digest.Any(character => character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))) ||
        request.Action.KnownMapLayersDigest is { } layersDigest &&
            (request.Action.KnownTerrainWorldId is null || layersDigest.Length != 64 ||
             layersDigest.Any(character => character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["knownTerrainDigest"] = ["The cached terrain world and lowercase manifest digest must be supplied together; a map-layer digest requires that terrain cache claim."],
        });
    }

    var authorization = authorizer.Authorize(
        request,
        "POST",
        "/api/v1/owner/reconnect",
        OwnerHttpBinding.ReconnectPayload(request.Action));
    if (!authorization.IsSuccess)
    {
        return OwnerFailures.ToHttpResult(authorization.Failure);
    }

    clientPresence.RecordAuthenticatedReconnect(authorization.Value!.DeviceId);

    return Results.Ok(new ViewerOwnerReconnect(
        observations.GetOwnerHandshake(),
        observations.GetReconnectBaseline(request.Action.AfterEventId,
            request.Action.KnownTerrainWorldId, request.Action.KnownTerrainDigest,
            request.Action.KnownMapLayersDigest)));
});

app.MapPost("/api/v1/owner/control/life-pace", (
    OwnerSignedHttpRequest<OwnerLifePaceAction> request,
    OwnerRequestAuthorizer authorizer,
    IServiceProvider services,
    OwnerWorldObservationStore observations,
    ILogger<PrivateWorldRuntimeService> logger) =>
{
    if (request.Action is null || request.Action.Rate is not (1 or 365 or 1_460))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["action.rate"] = ["Life pace must be 1, 365 or 1460."] });
    }
    var authorization = authorizer.Authorize(request, "POST", "/api/v1/owner/control/life-pace", OwnerHttpBinding.LifePacePayload(request.Action));
    if (!authorization.IsSuccess) return OwnerFailures.ToHttpResult(authorization.Failure);
    if (!isPrivateWorld) return Results.Conflict(new { error = "Life pacing requires a private world." });
    var runtime = services.GetRequiredService<PrivateWorldRuntime>();
    bool changed;
    try
    {
        changed = runtime.SetLifePace(request.Action.Rate);
    }
    catch (InvalidOperationException)
    {
        return Results.Conflict(new { error = "Pause the world before changing life pace." });
    }
    // A retry must also persist a prior in-memory change whose first save failed.
    services.GetRequiredService<PrivateWorldStateFile>().Save(runtime);
    if (changed) OwnerLifePaceTelemetry.Changed(logger, runtime.WorldTick, request.Action.Rate);
    return Results.Ok(OwnerControlReceipt.From("life_pace", changed, observations.GetSnapshot()));
});

app.MapPost("/api/v1/owner/control/jev-assistance", (
    OwnerSignedHttpRequest<OwnerJevAssistanceAction> request,
    OwnerRequestAuthorizer authorizer,
    IServiceProvider services,
    OwnerWorldObservationStore observations,
    WorldJevPolicy jevPolicy,
    ILogger<PrivateWorldRuntimeService> logger) =>
{
    if (request?.Action is null)
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["action"] = ["A Jev setting is required."] });
    var authorization = authorizer.Authorize(request, "POST", "/api/v1/owner/control/jev-assistance",
        OwnerHttpBinding.JevAssistancePayload(request.Action));
    if (!authorization.IsSuccess) return OwnerFailures.ToHttpResult(authorization.Failure);
    if (!isPrivateWorld) return Results.Conflict(new { error = "Jev assistance requires a private world." });
    var runtime = services.GetRequiredService<PrivateWorldRuntime>();
    bool changed;
    try
    {
        changed = runtime.SetJevEnabled(request.Action.Enabled);
    }
    catch (InvalidOperationException)
    {
        return Results.Conflict(new { error = "Pause the world before changing Jev assistance." });
    }
    jevPolicy.Set(runtime.JevEnabled, runtime.JevPolicyRevision);
    services.GetRequiredService<PrivateWorldStateFile>().Save(runtime);
    if (changed) OwnerJevAssistanceTelemetry.Changed(logger, runtime.WorldTick, runtime.JevEnabled);
    return Results.Ok(OwnerControlReceipt.From("jev_assistance", changed, observations.GetSnapshot()));
});

app.MapPost("/api/v1/owner/control/pause", (
    OwnerSignedHttpRequest<OwnerControlAction> request,
    OwnerRequestAuthorizer authorizer,
    IServiceProvider services) =>
{
    if (!IsControl(request, "pause"))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["action.operation"] = ["This endpoint only accepts the pause operation."],
        });
    }

    var authorization = authorizer.Authorize(
        request,
        "POST",
        "/api/v1/owner/control/pause",
        OwnerHttpBinding.EmptyPayload("pause"));
    if (!authorization.IsSuccess)
    {
        return OwnerFailures.ToHttpResult(authorization.Failure);
    }

    if (isPrivateWorld)
    {
        var privateRuntime = services.GetRequiredService<PrivateWorldRuntime>();
        var privateStateFile = services.GetRequiredService<PrivateWorldStateFile>();
        var wasPaused = privateRuntime.Society.IsPaused;
        privateRuntime.Pause();
        var changed = !wasPaused && privateRuntime.Society.IsPaused;
        // A prior failed write may already have changed memory. Even a no-op
        // retry must durably acknowledge the requested pause.
        privateStateFile.Save(privateRuntime);

        return Results.Ok(OwnerControlReceipt.From("pause", changed, privateRuntime.ExportState()));
    }

    var runtime = services.GetRequiredService<OwnerWorldRuntime>();
    var stateFile = services.GetRequiredService<OwnerWorldStateFile>();
    var legacyChanged = runtime.Pause($"owner-device:{authorization.Value!.DeviceId}");
    if (legacyChanged)
    {
        stateFile.Save(runtime);
    }

    return Results.Ok(OwnerControlReceipt.From("pause", legacyChanged, runtime.Capture().Snapshot));
});

app.MapPost("/api/v1/owner/control/resume", (
    OwnerSignedHttpRequest<OwnerControlAction> request,
    OwnerRequestAuthorizer authorizer,
    IServiceProvider services) =>
{
    if (!IsControl(request, "resume"))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["action.operation"] = ["This endpoint only accepts the resume operation."],
        });
    }

    var authorization = authorizer.Authorize(
        request,
        "POST",
        "/api/v1/owner/control/resume",
        OwnerHttpBinding.EmptyPayload("resume"));
    if (!authorization.IsSuccess)
    {
        return OwnerFailures.ToHttpResult(authorization.Failure);
    }

    if (isPrivateWorld)
    {
        var privateRuntime = services.GetRequiredService<PrivateWorldRuntime>();
        if (privateRuntime.FounderSetup is { Started: false })
            return Results.Conflict(new { message = "Place four configured founders, then select Start World." });
        if (services.GetRequiredService<ProviderUsageStore>().Capture().AccountingError is { } accountingError)
            return Results.Conflict(new { message = accountingError });
        if (services.GetRequiredService<ProviderUsageStore>().Capture().LimitReached)
            return Results.Conflict(new { message = "The paid-call limit is reached. Grant more calls or turn off the limit in World Settings before resuming." });
        var privateStateFile = services.GetRequiredService<PrivateWorldStateFile>();
        var wasPaused = privateRuntime.Society.IsPaused;
        privateRuntime.Resume();
        var changed = wasPaused && !privateRuntime.Society.IsPaused;
        if (changed)
        {
            privateStateFile.Save(privateRuntime);
        }

        return Results.Ok(OwnerControlReceipt.From("resume", changed, privateRuntime.ExportState()));
    }

    var runtime = services.GetRequiredService<OwnerWorldRuntime>();
    var stateFile = services.GetRequiredService<OwnerWorldStateFile>();
    var legacyChanged = runtime.Resume($"owner-device:{authorization.Value!.DeviceId}");
    if (legacyChanged)
    {
        stateFile.Save(runtime);
    }

    return Results.Ok(OwnerControlReceipt.From("resume", legacyChanged, runtime.Capture().Snapshot));
});

app.MapPost("/api/v1/owner/worlds/list", (
    OwnerSignedHttpRequest<OwnerControlAction> request,
    OwnerRequestAuthorizer authorizer,
    IServiceProvider services) =>
{
    if (!IsControl(request, "list-worlds"))
        return Results.BadRequest(new { error = "A world-list action is required." });
    var authorization = authorizer.Authorize(request, "POST", "/api/v1/owner/worlds/list",
        OwnerHttpBinding.EmptyPayload("list-worlds"));
    if (!authorization.IsSuccess) return OwnerFailures.ToHttpResult(authorization.Failure);
    if (!isPrivateWorld) return Results.Conflict(new { error = "World selection requires a private world." });
    return Results.Ok(services.GetRequiredService<WorldSelectionCoordinator>().List());
});

app.MapPost("/api/v1/owner/worlds/create", (
    OwnerSignedHttpRequest<OwnerWorldCreationAction> request,
    OwnerRequestAuthorizer authorizer,
    IServiceProvider services) =>
{
    if (request?.Action is not { } action)
        return Results.BadRequest(new { error = "World options are required." });
    string creationPayload;
    try { creationPayload = OwnerHttpBinding.WorldCreationPayload(action); }
    catch (ArgumentException) { return Results.BadRequest(new { error = "World name, seed, and size are required." }); }
    var authorization = authorizer.Authorize(request, "POST", "/api/v1/owner/worlds/create",
        creationPayload);
    if (!authorization.IsSuccess) return OwnerFailures.ToHttpResult(authorization.Failure);
    if (!isPrivateWorld) return Results.Conflict(new { error = "World creation requires a private world." });
    if (!TryWorldOptions(action, out var options))
        return Results.BadRequest(new { error = "World seed, size, or water choice is invalid." });
    try
    {
        var entry = services.GetRequiredService<WorldSelectionCoordinator>().Create(action.Name, options!);
        return Results.Ok(entry);
    }
    catch (ArgumentException exception) { return Results.BadRequest(new { error = exception.Message }); }
    catch (InvalidOperationException exception) { return Results.Conflict(new { error = exception.Message }); }
});

app.MapPost("/api/v1/owner/worlds/preview", (
    OwnerSignedHttpRequest<OwnerWorldCreationAction> request,
    OwnerRequestAuthorizer authorizer,
    IServiceProvider services) =>
{
    if (request?.Action is not { } action)
        return Results.BadRequest(new { error = "World options are required." });
    string payload;
    try { payload = OwnerHttpBinding.WorldCreationPayload(action); }
    catch (ArgumentException) { return Results.BadRequest(new { error = "World name, seed, and size are required." }); }
    var authorization = authorizer.Authorize(request, "POST", "/api/v1/owner/worlds/preview", payload);
    if (!authorization.IsSuccess) return OwnerFailures.ToHttpResult(authorization.Failure);
    if (!isPrivateWorld) return Results.Conflict(new { error = "World preview requires a private world." });
    if (!TryWorldOptions(action, out var options))
        return Results.BadRequest(new { error = "World seed, size, or water choice is invalid." });
    try { return Results.Ok(services.GetRequiredService<WorldSelectionCoordinator>().Preview(options!)); }
    catch (ArgumentException exception) { return Results.BadRequest(new { error = exception.Message }); }
    catch (InvalidOperationException exception) { return Results.Conflict(new { error = exception.Message }); }
});

app.MapPost("/api/v1/owner/worlds/select", (
    OwnerSignedHttpRequest<OwnerManualSaveAction> request,
    OwnerRequestAuthorizer authorizer,
    IServiceProvider services) =>
{
    if (request?.Action is not { Operation: "select-world" } action)
        return Results.BadRequest(new { error = "A world selection is required." });
    var authorization = authorizer.Authorize(request, "POST", "/api/v1/owner/worlds/select",
        OwnerHttpBinding.ManualSavePayload(action));
    if (!authorization.IsSuccess) return OwnerFailures.ToHttpResult(authorization.Failure);
    if (!isPrivateWorld) return Results.Conflict(new { error = "World selection requires a private world." });
    try { return Results.Ok(services.GetRequiredService<WorldSelectionCoordinator>().Select(action.Value)); }
    catch (FileNotFoundException) { return Results.NotFound(new { error = "The selected world does not exist." }); }
    catch (InvalidOperationException exception) { return Results.Conflict(new { error = exception.Message }); }
    catch (InvalidDataException) { return Results.Conflict(new { error = "The selected world is invalid." }); }
    catch (ArgumentException) { return Results.Conflict(new { error = "This world's model configuration is unavailable." }); }
});

app.MapPost("/api/v1/owner/saves/list", (
    OwnerSignedHttpRequest<OwnerControlAction> request,
    OwnerRequestAuthorizer authorizer,
    ManualWorldSaveStore saves,
    PrivateWorldRuntime runtime) =>
{
    if (!IsControl(request, "list-saves"))
        return Results.BadRequest(new { error = "A save-list action is required." });
    var authorization = authorizer.Authorize(request, "POST", "/api/v1/owner/saves/list",
        OwnerHttpBinding.EmptyPayload("list-saves"));
    if (!authorization.IsSuccess) return OwnerFailures.ToHttpResult(authorization.Failure);
    if (!isPrivateWorld) return Results.Conflict(new { error = "Manual saves require a private world." });
    return Results.Ok(saves.List(runtime.Society.WorldId));
});

app.MapPost("/api/v1/owner/saves/autosave/status", (
    OwnerSignedHttpRequest<OwnerControlAction> request,
    OwnerRequestAuthorizer authorizer,
    WorldAutosaveStore autosave) =>
{
    if (!IsControl(request, "autosave-status"))
        return Results.BadRequest(new { error = "An autosave-status action is required." });
    var authorization = authorizer.Authorize(request, "POST", "/api/v1/owner/saves/autosave/status",
        OwnerHttpBinding.EmptyPayload("autosave-status"));
    if (!authorization.IsSuccess) return OwnerFailures.ToHttpResult(authorization.Failure);
    if (!isPrivateWorld) return Results.Conflict(new { error = "Autosave settings require a private world." });
    return Results.Ok(autosave.Capture());
});

app.MapPost("/api/v1/owner/saves/autosave/configure", (
    OwnerSignedHttpRequest<OwnerAutosaveConfigurationAction> request,
    OwnerRequestAuthorizer authorizer,
    WorldAutosaveStore autosave,
    ManualWorldSaveStore saves,
    PrivateWorldRuntime runtime,
    ILogger<PrivateWorldRuntimeService> logger) =>
{
    if (request?.Action is not { } action)
        return Results.BadRequest(new { error = "Autosave settings are required." });
    var authorization = authorizer.Authorize(request, "POST", "/api/v1/owner/saves/autosave/configure",
        OwnerHttpBinding.AutosaveConfigurationPayload(action));
    if (!authorization.IsSuccess) return OwnerFailures.ToHttpResult(authorization.Failure);
    if (!isPrivateWorld) return Results.Conflict(new { error = "Autosave settings require a private world." });
    if (!runtime.Society.IsPaused)
    {
        ManualWorldSaveTelemetry.Rejected(logger, "autosave_configure", "not_paused");
        return Results.Conflict(new { error = "Pause the world before changing autosave settings." });
    }
    try
    {
        var updated = autosave.Configure(action.Enabled, action.IntervalMinutes, action.RotationCount);
        saves.KeepNewestAutosaves(Math.Max(1, updated.RotationCount), worldId: updated.WorldId);
        ManualWorldSaveTelemetry.AutosaveConfigured(logger, updated.Enabled,
            updated.IntervalMinutes, updated.RotationCount, runtime.WorldTick);
        return Results.Ok(updated);
    }
    catch (ArgumentException)
    {
        ManualWorldSaveTelemetry.Rejected(logger, "autosave_configure", "invalid_option");
        return Results.BadRequest(new { error = "Choose an offered interval and rotation count." });
    }
});

app.MapPost("/api/v1/owner/saves/create", (
    OwnerSignedHttpRequest<OwnerManualSaveAction> request,
    OwnerRequestAuthorizer authorizer,
    ManualWorldSaveStore saves,
    PrivateWorldRuntime runtime,
    ProviderConfigurationStore providers,
    WorldAutosaveStore autosave,
    ILogger<PrivateWorldRuntimeService> logger) =>
{
    if (request?.Action is not { Operation: "create" } action)
        return Results.BadRequest(new { error = "A named save action is required." });
    string payload;
    try { payload = OwnerHttpBinding.ManualSavePayload(action); }
    catch (ArgumentException) { return Results.BadRequest(new { error = "A save name is required." }); }
    var authorization = authorizer.Authorize(request, "POST", "/api/v1/owner/saves/create", payload);
    if (!authorization.IsSuccess) return OwnerFailures.ToHttpResult(authorization.Failure);
    if (!isPrivateWorld) return Results.Conflict(new { error = "Manual saves require a private world." });
    try
    {
        var saved = saves.Create(action.Value, runtime,
            providers.CaptureRuntimeConfiguration().Assignments ?? [], autosave.Capture());
        ManualWorldSaveTelemetry.Created(logger, saved.Id, saved.WorldTick);
        return Results.Ok(saved);
    }
    catch (ArgumentException)
    {
        ManualWorldSaveTelemetry.Rejected(logger, "create", "invalid_name");
        return Results.BadRequest(new { error = "Save name must be 1–80 printable characters." });
    }
    catch (InvalidOperationException)
    {
        ManualWorldSaveTelemetry.Rejected(logger, "create", "not_paused");
        return Results.Conflict(new { error = "Pause the world before saving." });
    }
});

app.MapPost("/api/v1/owner/saves/overwrite", (
    OwnerSignedHttpRequest<OwnerManualSaveAction> request,
    OwnerRequestAuthorizer authorizer,
    ManualWorldSaveStore saves,
    PrivateWorldRuntime runtime,
    ProviderConfigurationStore providers,
    WorldAutosaveStore autosave,
    ILogger<PrivateWorldRuntimeService> logger) =>
{
    if (request?.Action is not { Operation: "overwrite" } action)
        return Results.BadRequest(new { error = "A selected save ID is required." });
    string payload;
    try { payload = OwnerHttpBinding.ManualSavePayload(action); }
    catch (ArgumentException) { return Results.BadRequest(new { error = "A selected save ID is required." }); }
    var authorization = authorizer.Authorize(request, "POST", "/api/v1/owner/saves/overwrite", payload);
    if (!authorization.IsSuccess) return OwnerFailures.ToHttpResult(authorization.Failure);
    if (!isPrivateWorld) return Results.Conflict(new { error = "Manual saves require a private world." });
    try
    {
        var result = saves.Overwrite(action.Value, runtime,
            providers.CaptureRuntimeConfiguration().Assignments ?? [], autosave.Capture());
        ManualWorldSaveTelemetry.Overwritten(logger, result.Saved.Id, result.BackupId,
            result.Saved.WorldTick);
        return Results.Ok(result);
    }
    catch (FileNotFoundException)
    {
        ManualWorldSaveTelemetry.Rejected(logger, "overwrite", "missing");
        return Results.NotFound(new { error = "The selected save no longer exists." });
    }
    catch (ArgumentException)
    {
        ManualWorldSaveTelemetry.Rejected(logger, "overwrite", "invalid_id");
        return Results.BadRequest(new { error = "Invalid save ID." });
    }
    catch (InvalidDataException)
    {
        ManualWorldSaveTelemetry.Rejected(logger, "overwrite", "invalid_checkpoint");
        return Results.Conflict(new { error = "The selected checkpoint is invalid and was preserved." });
    }
    catch (InvalidOperationException)
    {
        ManualWorldSaveTelemetry.Rejected(logger, "overwrite", "not_paused_or_wrong_world");
        return Results.Conflict(new { error = "Pause the world and select one of its named saves." });
    }
});

app.MapPost("/api/v1/owner/saves/load", (
    OwnerSignedHttpRequest<OwnerManualSaveAction> request,
    OwnerRequestAuthorizer authorizer,
    ManualWorldSaveStore saves,
    PrivateWorldRuntime runtime,
    PrivateWorldStateFile stateFile,
    ProviderConfigurationStore providers,
    WorldAutosaveStore autosave,
    WorldJevPolicy jevPolicy,
    ILogger<PrivateWorldRuntimeService> logger) =>
{
    if (request?.Action is not { Operation: "load" } action)
        return Results.BadRequest(new { error = "A save-load action is required." });
    string payload;
    try { payload = OwnerHttpBinding.ManualSavePayload(action); }
    catch (ArgumentException) { return Results.BadRequest(new { error = "A save ID is required." }); }
    var authorization = authorizer.Authorize(request, "POST", "/api/v1/owner/saves/load", payload);
    if (!authorization.IsSuccess) return OwnerFailures.ToHttpResult(authorization.Failure);
    if (!isPrivateWorld) return Results.Conflict(new { error = "Manual saves require a private world." });
    if (!runtime.Society.IsPaused)
    {
        ManualWorldSaveTelemetry.Rejected(logger, "load", "not_paused");
        return Results.Conflict(new { error = "Pause the world before loading." });
    }
    try
    {
        var committed = saves.ReadCommitted(action.Value);
        var checkpoint = committed.Checkpoint;
        stateFile.VerifyRequiredHistory(checkpoint);
        var assignments = committed.Assignments;
        var autosaveSettings = committed.AutosaveSettings;
        if (!string.Equals(checkpoint.WorldSeed, runtime.ExportState().WorldSeed, StringComparison.Ordinal))
        {
            ManualWorldSaveTelemetry.Rejected(logger, "load", "different_world");
            return Results.Conflict(new { error = "This save belongs to a different world." });
        }
        // A rewind must never destroy the current timeline. The backup is a
        // normal named checkpoint, visible in Load Saves immediately.
        var backup = saves.Create("Before loading", runtime,
            providers.CaptureRuntimeConfiguration().Assignments ?? [], autosave.Capture());
        try
        {
            runtime.LoadPausedCheckpoint(checkpoint);
            stateFile.Save(runtime);
            providers.RestoreWorldAssignments(assignments);
            if (autosaveSettings is not null) autosave.RestoreFromCheckpoint(autosaveSettings);
            jevPolicy.Initialize(runtime.JevEnabled, runtime.JevPolicyRevision);
        }
        catch
        {
            runtime.LoadPausedCheckpoint(saves.Read(backup.Id));
            stateFile.Save(runtime);
            providers.RestoreWorldAssignments(saves.ReadAssignments(backup.Id));
            if (saves.ReadAutosaveSettings(backup.Id) is { } previousAutosave)
                autosave.RestoreFromCheckpoint(previousAutosave);
            jevPolicy.Initialize(runtime.JevEnabled, runtime.JevPolicyRevision);
            throw;
        }
        ManualWorldSaveTelemetry.Loaded(logger, action.Value, backup.Id, runtime.WorldTick);
        return Results.Ok(new { loadedId = action.Value, backupId = backup.Id, worldTick = runtime.WorldTick });
    }
    catch (FileNotFoundException)
    {
        ManualWorldSaveTelemetry.Rejected(logger, "load", "missing");
        return Results.NotFound(new { error = "The manual save no longer exists." });
    }
    catch (ArgumentException)
    {
        ManualWorldSaveTelemetry.Rejected(logger, "load", "invalid_id");
        return Results.BadRequest(new { error = "Invalid save ID." });
    }
    catch (InvalidDataException)
    {
        ManualWorldSaveTelemetry.Rejected(logger, "load", "invalid_checkpoint_or_credential");
        return Results.Conflict(new { error = "The save is invalid or its credential slot is unavailable." });
    }
    catch (InvalidOperationException)
    {
        ManualWorldSaveTelemetry.Rejected(logger, "load", "not_paused");
        return Results.Conflict(new { error = "Pause the world before loading." });
    }
});

app.MapPost("/api/v1/owner/town/first-layout", (
    OwnerSignedHttpRequest<OwnerFirstTownLayoutAction> request,
    OwnerRequestAuthorizer authorizer,
    IServiceProvider services,
    ILoggerFactory loggerFactory) =>
{
    if (request?.Action is not { } action)
        return Results.BadRequest(new { error = "Choose a rough Town site." });
    var authorization = authorizer.Authorize(request, "POST", "/api/v1/owner/town/first-layout",
        OwnerHttpBinding.FirstTownLayoutPayload(action));
    if (!authorization.IsSuccess) return OwnerFailures.ToHttpResult(authorization.Failure);
    if (!isPrivateWorld) return Results.Conflict(new { error = "First-Town setup requires a private world." });
    lock (founderSetupGate)
    {
        var runtime = services.GetRequiredService<PrivateWorldRuntime>();
        var before = runtime.ExportState();
        try
        {
            var plan = runtime.AcceptFirstTownLayout(new GridPoint(action.X, action.Y));
            try { services.GetRequiredService<PrivateWorldStateFile>().Save(runtime); }
            catch
            {
                runtime.SwitchPausedWorld(before);
                throw;
            }
            TownTelemetry.LayoutAccepted(loggerFactory.CreateLogger("ClankerWorld.Town"),
                runtime.WorldTick, before.Towns?.Any(town => town.OriginSite is not null) == true
                    ? "redone" : "accepted", action.X, action.Y, plan.Buildings.Count, plan.RoadTiles.Count);
            return Results.Ok(new OwnerFirstTownLayoutReceipt(action.X, action.Y,
                plan.Buildings.Count, plan.RoadTiles.Count));
        }
        catch (ArgumentException exception) { return Results.BadRequest(new { error = exception.Message }); }
        catch (InvalidOperationException exception) { return Results.Conflict(new { error = exception.Message }); }
    }
});

app.MapPost("/api/v1/owner/founders/place", (
    OwnerSignedHttpRequest<OwnerFounderPlacementAction> request,
    OwnerRequestAuthorizer authorizer,
    ProviderConfigurationStore providers,
    ILoggerFactory loggerFactory,
    IServiceProvider services) =>
{
    if (!isPrivateWorld || request?.Action is not { Cognition: { } cognition } action)
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["action"] = ["A founder placement is required in a private world."] });
    string payload;
    try { payload = OwnerHttpBinding.FounderPlacementPayload(action); }
    catch (ArgumentException exception)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["action"] = [exception.Message] });
    }
    var authorization = authorizer.Authorize(request, "POST", "/api/v1/owner/founders/place", payload);
    if (!authorization.IsSuccess)
        return OwnerFailures.ToHttpResult(authorization.Failure);
    if (cognition.InhabitantId != action.FounderId || cognition.Role != PlayerDecisionProviders.PersonalRole ||
        cognition.ForgetCredential || cognition.Provider is not (PlayerDecisionProviders.OpenAi or PlayerDecisionProviders.OllamaCloud))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["cognition"] = ["Choose one personal hosted model for this founder."] });

    lock (founderSetupGate)
    {
        try
        {
            var runtime = services.GetRequiredService<PrivateWorldRuntime>();
            var position = new GridPoint(action.X, action.Y);
            runtime.ValidateFounderPlacement(action.FounderId, position);
            var before = runtime.ExportState();
            string household = "";
            try
            {
                providers.ConfigureWithCommit(cognition, () =>
                {
                    household = runtime.PlaceFounder(action.FounderId, position);
                    services.GetRequiredService<PrivateWorldStateFile>().Save(runtime);
                });
            }
            catch
            {
                runtime.SwitchPausedWorld(before);
                throw;
            }
            var placed = runtime.FounderSetup!.FounderIds.Count;
            var town = runtime.Towns.Single(item => item.Id == TownBorderRules.FirstTownId);
            TownTelemetry.Transition(loggerFactory.CreateLogger("ClankerWorld.Town"), runtime.WorldTick,
                town.Id, TownTransitionKind.ResidentJoined, town.ResidentIds.Count,
                town.AssignedBuildingIds.Count, town.BorderTiles.Count);
            return Results.Ok(new OwnerFounderPlacementReceipt(action.FounderId, household, placed, PrivateWorldRuntime.RequiredFounders));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["action"] = [exception.Message] });
        }
    }
});

app.MapPost("/api/v1/owner/founders/move", (
    OwnerSignedHttpRequest<OwnerFounderMoveAction> request,
    OwnerRequestAuthorizer authorizer,
    IServiceProvider services,
    ILoggerFactory loggerFactory) =>
{
    if (request?.Action is not { } action)
        return Results.BadRequest(new { error = "Choose a placed founder and destination." });
    string payload;
    try { payload = OwnerHttpBinding.FounderMovePayload(action); }
    catch (ArgumentException exception) { return Results.BadRequest(new { error = exception.Message }); }
    var authorization = authorizer.Authorize(request, "POST", "/api/v1/owner/founders/move", payload);
    if (!authorization.IsSuccess) return OwnerFailures.ToHttpResult(authorization.Failure);
    if (!isPrivateWorld) return Results.Conflict(new { error = "Founder setup requires a private world." });
    lock (founderSetupGate)
    {
        var runtime = services.GetRequiredService<PrivateWorldRuntime>();
        var before = runtime.ExportState();
        try
        {
            var changed = runtime.MoveFounder(action.FounderId, new GridPoint(action.X, action.Y));
            if (changed)
            {
                try { services.GetRequiredService<PrivateWorldStateFile>().Save(runtime); }
                catch
                {
                    runtime.SwitchPausedWorld(before);
                    throw;
                }
                TownTelemetry.FounderMoved(loggerFactory.CreateLogger("ClankerWorld.Town"),
                    runtime.WorldTick, action.FounderId, action.X, action.Y);
            }
            return Results.Ok(new OwnerFounderMoveReceipt(action.FounderId, action.X, action.Y, changed));
        }
        catch (ArgumentException exception) { return Results.BadRequest(new { error = exception.Message }); }
        catch (InvalidOperationException exception) { return Results.Conflict(new { error = exception.Message }); }
    }
});

app.MapPost("/api/v1/owner/founders/undo", (
    OwnerSignedHttpRequest<OwnerFounderUndoAction> request,
    OwnerRequestAuthorizer authorizer,
    ProviderConfigurationStore providers,
    IServiceProvider services,
    ILoggerFactory loggerFactory) =>
{
    if (request?.Action is not { } action)
        return Results.BadRequest(new { error = "Choose the last placed founder to undo." });
    string payload;
    try { payload = OwnerHttpBinding.FounderUndoPayload(action); }
    catch (ArgumentException exception) { return Results.BadRequest(new { error = exception.Message }); }
    var authorization = authorizer.Authorize(request, "POST", "/api/v1/owner/founders/undo", payload);
    if (!authorization.IsSuccess) return OwnerFailures.ToHttpResult(authorization.Failure);
    if (!isPrivateWorld) return Results.Conflict(new { error = "Founder setup requires a private world." });
    lock (founderSetupGate)
    {
        var runtime = services.GetRequiredService<PrivateWorldRuntime>();
        var stateFile = services.GetRequiredService<PrivateWorldStateFile>();
        var before = runtime.ExportState();
        var assignments = providers.CaptureRuntimeConfiguration().Assignments ?? [];
        var worldMutated = false;
        var worldSaved = false;
        var providerChanged = false;
        try
        {
            var placed = runtime.UndoLastFounder(action.FounderId);
            worldMutated = true;
            stateFile.Save(runtime);
            worldSaved = true;
            providers.Configure(new OwnerProviderConfigurationAction("personal", "inherit", null, null,
                false, action.FounderId));
            providerChanged = true;
            TownTelemetry.FounderUndone(loggerFactory.CreateLogger("ClankerWorld.Town"),
                runtime.WorldTick, action.FounderId, placed);
            return Results.Ok(new OwnerFounderUndoReceipt(action.FounderId, placed,
                PrivateWorldRuntime.RequiredFounders));
        }
        catch (Exception exception)
        {
            if (worldMutated)
            {
                runtime.SwitchPausedWorld(before);
                if (worldSaved) stateFile.Save(runtime);
            }
            if (providerChanged) providers.RestoreWorldAssignments(assignments);
            if (exception is ArgumentException) return Results.BadRequest(new { error = exception.Message });
            if (exception is InvalidOperationException) return Results.Conflict(new { error = exception.Message });
            throw;
        }
    }
});

app.MapPost("/api/v1/owner/agents/place", (
    OwnerSignedHttpRequest<OwnerAgentPlacementAction> request,
    OwnerRequestAuthorizer authorizer,
    ProviderConfigurationStore providers,
    IServiceProvider services,
    ILoggerFactory loggerFactory) =>
{
    var logger = loggerFactory.CreateLogger("ClankerWorld.AgentPlacement");
    if (!isPrivateWorld || request?.Action is not { Cognition: { } cognition } action)
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["action"] = ["An agent placement is required in a private world."] });
    string payload;
    try { payload = OwnerHttpBinding.AgentPlacementPayload(action); }
    catch (ArgumentException exception)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["action"] = [exception.Message] });
    }
    var authorization = authorizer.Authorize(request, "POST", "/api/v1/owner/agents/place", payload);
    if (!authorization.IsSuccess)
        return OwnerFailures.ToHttpResult(authorization.Failure);
    if (cognition.InhabitantId != action.AgentId || cognition.Role != PlayerDecisionProviders.PersonalRole ||
        cognition.ForgetCredential || cognition.Provider is not (PlayerDecisionProviders.OpenAi or PlayerDecisionProviders.OllamaCloud))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["cognition"] = ["Choose one personal hosted model for this agent."] });

    lock (founderSetupGate)
    {
        try
        {
            var runtime = services.GetRequiredService<PrivateWorldRuntime>();
            var position = new GridPoint(action.X, action.Y);
            runtime.ValidateAgentPlacement(action.AgentId, position);
            providers.Configure(cognition);
            var household = runtime.AddAgent(action.AgentId, position);
            services.GetRequiredService<PrivateWorldStateFile>().Save(runtime);
            AgentPlacementLog.Placed(logger, action.AgentId, household, runtime.WorldTick, action.X, action.Y);
            var town = runtime.Towns.SingleOrDefault(item => item.ResidentIds.Contains(action.AgentId, StringComparer.Ordinal));
            TownTelemetry.Transition(logger, runtime.WorldTick, town?.Id ?? "none",
                town is null ? TownTransitionKind.ResidentUnaffiliated : TownTransitionKind.ResidentJoined,
                town?.ResidentIds.Count ?? 0, town?.AssignedBuildingIds.Count ?? 0, town?.BorderTiles.Count ?? 0);
            return Results.Ok(new OwnerAgentPlacementReceipt(action.AgentId, household));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            var loggedId = action.AgentId is { Length: 38 } id &&
                id.StartsWith("agent:", StringComparison.Ordinal) &&
                Guid.TryParseExact(id["agent:".Length..], "N", out _)
                    ? id : "<invalid-id>";
            AgentPlacementLog.Rejected(logger, loggedId, exception.GetType().Name);
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["action"] = [exception.Message] });
        }
    }
});

app.MapPost("/api/v1/owner/agents/rename", (
    OwnerSignedHttpRequest<OwnerAgentRenameAction> request,
    OwnerRequestAuthorizer authorizer,
    IServiceProvider services,
    ILoggerFactory loggerFactory) =>
{
    if (!isPrivateWorld || request?.Action is not { } action)
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["action"] = ["Choose an agent and name."] });
    string payload;
    try { payload = OwnerHttpBinding.AgentRenamePayload(action); }
    catch (ArgumentException exception)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["action"] = [exception.Message] });
    }
    var authorization = authorizer.Authorize(request, "POST", "/api/v1/owner/agents/rename", payload);
    if (!authorization.IsSuccess)
        return OwnerFailures.ToHttpResult(authorization.Failure);
    try
    {
        var runtime = services.GetRequiredService<PrivateWorldRuntime>();
        var changed = runtime.RenameAgent(action.AgentId, action.Name);
        services.GetRequiredService<PrivateWorldStateFile>().Save(runtime);
        if (changed)
        {
            var logger = loggerFactory.CreateLogger("ClankerWorld.AgentIdentity");
            AgentPlacementLog.Renamed(logger, action.AgentId, runtime.WorldTick);
        }
        return Results.Ok(new OwnerAgentRenameReceipt(action.AgentId,
            runtime.Society.GetInhabitant(action.AgentId).Name, changed));
    }
    catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["action"] = [exception.Message] });
    }
});

app.MapPost("/api/v1/owner/control/start-world", (
    OwnerSignedHttpRequest<OwnerControlAction> request,
    OwnerRequestAuthorizer authorizer,
    ProviderConfigurationStore providers,
    IServiceProvider services,
    OwnerWorldObservationStore observations,
    ILoggerFactory loggerFactory) =>
{
    if (!isPrivateWorld || !IsControl(request, "start-world"))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["action.operation"] = ["This endpoint starts a configured private world."] });
    var authorization = authorizer.Authorize(request, "POST", "/api/v1/owner/control/start-world",
        OwnerHttpBinding.EmptyPayload("start-world"));
    if (!authorization.IsSuccess)
        return OwnerFailures.ToHttpResult(authorization.Failure);

    lock (founderSetupGate)
    {
        var runtime = services.GetRequiredService<PrivateWorldRuntime>();
        var setup = runtime.FounderSetup;
        if (setup is null || setup.Started || setup.FounderIds.Count != PrivateWorldRuntime.RequiredFounders)
            return Results.Conflict(new { message = "Place all four founders before starting the world." });
        var providerStatus = providers.CaptureStatus();
        var assignments = providerStatus.Assignments ?? [];
        foreach (var founderId in setup.FounderIds)
        {
            var personal = assignments.Where(item => item.InhabitantId == founderId).ToArray();
            if (personal.Length != 2 || !personal.Any(item => item.Role == PlayerDecisionProviders.RoutineRole) ||
                !personal.Any(item => item.Role == PlayerDecisionProviders.PlanningRole) ||
                personal.Any(item => item.Provider is not (PlayerDecisionProviders.OpenAi or PlayerDecisionProviders.OllamaCloud)) ||
                personal.Select(item => (item.Provider, item.Model, item.CredentialSlotId)).Distinct().Count() != 1 ||
                personal.Any(item => item.CredentialSlotId is { } slot
                    ? !(providerStatus.CredentialSlots ?? []).Any(saved => saved.Id == slot && saved.Provider == item.Provider)
                    : !providerStatus.Providers.Any(option => option.Provider == item.Provider && option.HasCredential)))
                return Results.Conflict(new { message = "Every founder needs one configured personal model and credential." });
        }
        runtime.StartWorld();
        services.GetRequiredService<PrivateWorldStateFile>().Save(runtime);
        var town = runtime.Towns.Single(item => item.Id == TownBorderRules.FirstTownId);
        TownTelemetry.Transition(loggerFactory.CreateLogger("ClankerWorld.Town"), runtime.WorldTick,
            town.Id, TownTransitionKind.Founded, town.ResidentIds.Count,
            town.AssignedBuildingIds.Count, town.BorderTiles.Count);
        return Results.Ok(OwnerControlReceipt.From("start-world", true, observations.GetSnapshot()));
    }
});

app.MapPost("/api/v1/owner/instructions", (
    OwnerSignedHttpRequest<OwnerInstructionAction> request,
    OwnerRequestAuthorizer authorizer,
    IServiceProvider services) =>
{
    if (request?.Action is null || !TryParseInstructionKind(request.Action.Kind, out var kind))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["action.kind"] = ["Instruction kind must be suggestive or must_do."],
        });
    }

    string payload;
    try
    {
        payload = OwnerHttpBinding.InstructionPayload(request.Action);
    }
    catch (ArgumentException exception)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["action"] = [exception.Message],
        });
    }

    var authorization = authorizer.Authorize(request, "POST", "/api/v1/owner/instructions", payload);
    if (!authorization.IsSuccess)
    {
        return OwnerFailures.ToHttpResult(authorization.Failure);
    }

    if (isPrivateWorld)
    {
        var privateRuntime = services.GetRequiredService<PrivateWorldRuntime>();
        var privateStateFile = services.GetRequiredService<PrivateWorldStateFile>();
        try
        {
            var receipt = privateRuntime.SubmitInstruction(new OwnerInstructionRequest(
                request.Action.IdempotencyKey,
                $"owner-device:{authorization.Value!.DeviceId}",
                request.Action.TargetInhabitantId,
                kind,
                request.Action.Text));
            privateStateFile.Save(privateRuntime);
            return Results.Ok(receipt);
        }
        catch (ArgumentException exception)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["action"] = [exception.Message],
            });
        }
        catch (InvalidOperationException exception)
        {
            return Results.Conflict(new OwnerControlFailure("idempotency_conflict", exception.Message));
        }
    }

    try
    {
        var runtime = services.GetRequiredService<OwnerWorldRuntime>();
        var stateFile = services.GetRequiredService<OwnerWorldStateFile>();
        // The transport has no issuer field. This value is minted from the
        // authenticated server-side device identity rather than accepted from
        // a client payload.
        var receipt = runtime.SubmitInstruction(new OwnerInstructionRequest(
            request.Action.IdempotencyKey,
            $"owner-device:{authorization.Value!.DeviceId}",
            request.Action.TargetInhabitantId,
            kind,
            request.Action.Text));
        stateFile.Save(runtime);
        return Results.Ok(receipt);
    }
    catch (ArgumentException exception)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["action"] = [exception.Message],
        });
    }
    catch (InvalidOperationException exception)
    {
        return Results.Conflict(new OwnerControlFailure("idempotency_conflict", exception.Message));
    }
});

app.MapBuildingDesign(isPrivateWorld);

app.MapPost("/api/v1/owner/content/propose", (
    OwnerSignedHttpRequest<OwnerContentPackageAction> request,
    OwnerRequestAuthorizer authorizer,
    IServiceProvider services) =>
{
    if (!isPrivateWorld)
    {
        return Results.Conflict(new OwnerControlFailure(
            "private_world_content_required",
            "Data-only content governance is available only in the integrated private world."));
    }

    if (!OwnerContentBinding.TryMapManifest(request?.Action, out var manifest, out var failure))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["action"] = [failure],
        });
    }

    string payload;
    try
    {
        payload = OwnerContentBinding.ProposePayload(request!.Action);
    }
    catch (ArgumentException exception)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["action"] = [exception.Message],
        });
    }

    var authorization = authorizer.Authorize(request, "POST", "/api/v1/owner/content/propose", payload);
    if (!authorization.IsSuccess)
    {
        return OwnerFailures.ToHttpResult(authorization.Failure);
    }

    var runtime = services.GetRequiredService<PrivateWorldRuntime>();
    var stateFile = services.GetRequiredService<PrivateWorldStateFile>();
    try
    {
        var record = runtime.ProposeContent(manifest!);
        stateFile.Save(runtime);
        return Results.Ok(OwnerContentPackageReceipt.From("propose", record));
    }
    catch (ArgumentException exception)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["action"] = [exception.Message],
        });
    }
    catch (InvalidOperationException exception)
    {
        return Results.Conflict(new OwnerControlFailure("content_rejected", exception.Message));
    }
});

app.MapPost("/api/v1/owner/content/validate", (
    OwnerSignedHttpRequest<OwnerContentPackageIdAction> request,
    OwnerRequestAuthorizer authorizer,
    IServiceProvider services) =>
{
    if (!isPrivateWorld)
    {
        return Results.Conflict(new OwnerControlFailure(
            "private_world_content_required",
            "Data-only content governance is available only in the integrated private world."));
    }

    if (request?.Action is null)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["action.packageId"] = ["A package ID is required."],
        });
    }

    string payload;
    try
    {
        payload = OwnerContentBinding.PackageIdPayload("validate", request.Action);
    }
    catch (ArgumentException exception)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["action"] = [exception.Message],
        });
    }

    var authorization = authorizer.Authorize(request, "POST", "/api/v1/owner/content/validate", payload);
    if (!authorization.IsSuccess)
    {
        return OwnerFailures.ToHttpResult(authorization.Failure);
    }

    var runtime = services.GetRequiredService<PrivateWorldRuntime>();
    var stateFile = services.GetRequiredService<PrivateWorldStateFile>();
    try
    {
        var resolution = runtime.ResolveContent(request.Action.PackageId);
        var record = runtime.ValidateContent(request.Action.PackageId, resolution);
        stateFile.Save(runtime);
        return Results.Ok(OwnerContentPackageReceipt.From("validate", record));
    }
    catch (ArgumentException exception)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["action"] = [exception.Message],
        });
    }
    catch (InvalidOperationException exception)
    {
        return Results.Conflict(new OwnerControlFailure("content_rejected", exception.Message));
    }
    catch (KeyNotFoundException exception)
    {
        return Results.NotFound(new OwnerControlFailure("content_not_found", exception.Message));
    }
});

app.MapPost("/api/v1/owner/content/approve", (
    OwnerSignedHttpRequest<OwnerContentPackageIdAction> request,
    OwnerRequestAuthorizer authorizer,
    IServiceProvider services) =>
{
    if (!isPrivateWorld)
    {
        return Results.Conflict(new OwnerControlFailure(
            "private_world_content_required",
            "Data-only content governance is available only in the integrated private world."));
    }

    if (request?.Action is null)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["action.packageId"] = ["A package ID is required."],
        });
    }

    string payload;
    try
    {
        payload = OwnerContentBinding.PackageIdPayload("approve", request.Action);
    }
    catch (ArgumentException exception)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["action"] = [exception.Message],
        });
    }

    var authorization = authorizer.Authorize(request, "POST", "/api/v1/owner/content/approve", payload);
    if (!authorization.IsSuccess)
    {
        return OwnerFailures.ToHttpResult(authorization.Failure);
    }

    var runtime = services.GetRequiredService<PrivateWorldRuntime>();
    var stateFile = services.GetRequiredService<PrivateWorldStateFile>();
    try
    {
        var record = runtime.ApproveContent(request.Action.PackageId);
        stateFile.Save(runtime);
        return Results.Ok(OwnerContentPackageReceipt.From("approve", record));
    }
    catch (InvalidOperationException exception)
    {
        return Results.Conflict(new OwnerControlFailure("content_rejected", exception.Message));
    }
    catch (KeyNotFoundException exception)
    {
        return Results.NotFound(new OwnerControlFailure("content_not_found", exception.Message));
    }
});

app.MapPost("/api/v1/owner/content/stage", (
    OwnerSignedHttpRequest<OwnerContentPackageIdAction> request,
    OwnerRequestAuthorizer authorizer,
    IServiceProvider services) =>
{
    if (!isPrivateWorld)
    {
        return Results.Conflict(new OwnerControlFailure(
            "private_world_content_required",
            "Data-only content governance is available only in the integrated private world."));
    }

    if (request?.Action is null)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["action.packageId"] = ["A package ID is required."],
        });
    }

    string payload;
    try
    {
        payload = OwnerContentBinding.PackageIdPayload("stage", request.Action);
    }
    catch (ArgumentException exception)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["action"] = [exception.Message],
        });
    }

    var authorization = authorizer.Authorize(request, "POST", "/api/v1/owner/content/stage", payload);
    if (!authorization.IsSuccess)
    {
        return OwnerFailures.ToHttpResult(authorization.Failure);
    }

    var runtime = services.GetRequiredService<PrivateWorldRuntime>();
    var stateFile = services.GetRequiredService<PrivateWorldStateFile>();
    try
    {
        var record = runtime.StageContent(request.Action.PackageId);
        stateFile.Save(runtime);
        return Results.Ok(OwnerContentPackageReceipt.From("stage", record));
    }
    catch (InvalidOperationException exception)
    {
        return Results.Conflict(new OwnerControlFailure("content_rejected", exception.Message));
    }
    catch (KeyNotFoundException exception)
    {
        return Results.NotFound(new OwnerControlFailure("content_not_found", exception.Message));
    }
});

app.MapPost("/api/v1/owner/content/rollback", (
    OwnerSignedHttpRequest<OwnerContentRollbackAction> request,
    OwnerRequestAuthorizer authorizer,
    IServiceProvider services,
    ILogger<PrivateWorldRuntimeService> logger) =>
{
    if (!isPrivateWorld)
    {
        return Results.Conflict(new OwnerControlFailure(
            "private_world_content_required",
            "Data-only content governance is available only in the integrated private world."));
    }

    if (request?.Action is null)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["action"] = ["A package ID and rollback reason are required."],
        });
    }

    string payload;
    try
    {
        payload = OwnerContentBinding.RollbackPayload(request.Action);
    }
    catch (ArgumentException exception)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["action"] = [exception.Message],
        });
    }

    var authorization = authorizer.Authorize(request, "POST", "/api/v1/owner/content/rollback", payload);
    if (!authorization.IsSuccess)
    {
        return OwnerFailures.ToHttpResult(authorization.Failure);
    }

    var runtime = services.GetRequiredService<PrivateWorldRuntime>();
    var stateFile = services.GetRequiredService<PrivateWorldStateFile>();
    try
    {
        var record = runtime.RollbackContent(request.Action.PackageId, request.Action.Reason);
        stateFile.Save(runtime);
        OwnerContentTelemetry.Rollback(logger, runtime.WorldTick, record.Manifest.PackageId, "quarantined");
        return Results.Ok(OwnerContentPackageReceipt.From("rollback", record));
    }
    catch (ArgumentException exception)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["action"] = [exception.Message],
        });
    }
    catch (InvalidOperationException exception)
    {
        OwnerContentTelemetry.Rollback(logger, runtime.WorldTick, request.Action.PackageId, "rejected");
        return Results.Conflict(new OwnerControlFailure("content_rejected", exception.Message));
    }
    catch (KeyNotFoundException exception)
    {
        return Results.NotFound(new OwnerControlFailure("content_not_found", exception.Message));
    }
});

app.MapPost("/api/v1/owner/buildings/place", (
    OwnerSignedHttpRequest<OwnerBuildingPlacementAction> request,
    OwnerRequestAuthorizer authorizer,
    IServiceProvider services,
    ILoggerFactory loggerFactory) =>
{
    if (!isPrivateWorld)
    {
        return Results.Conflict(new OwnerControlFailure(
            "private_world_required",
            "Building placement is available only in the integrated private world."));
    }

    if (request?.Action is null)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["action"] = ["A building placement action is required."],
        });
    }

    string payload;
    try
    {
        payload = OwnerContentBinding.BuildingPlacementPayload(request.Action);
    }
    catch (ArgumentException exception)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["action"] = [exception.Message],
        });
    }

    var authorization = authorizer.Authorize(request, "POST", "/api/v1/owner/buildings/place", payload);
    if (!authorization.IsSuccess)
    {
        return OwnerFailures.ToHttpResult(authorization.Failure);
    }

    var runtime = services.GetRequiredService<PrivateWorldRuntime>();
    var stateFile = services.GetRequiredService<PrivateWorldStateFile>();
    var previousBorderTiles = runtime.Towns.SingleOrDefault(item => item.Id == TownBorderRules.FirstTownId)?.BorderTiles.Count ?? 0;
    var result = runtime.PlaceBuilding(
        request.Action.InstanceId,
        request.Action.DefinitionId,
        new ClankerWorld.Simulation.Harness.GridPoint(request.Action.X, request.Action.Y));
    if (!result.Applied)
    {
        return Results.Conflict(new OwnerControlFailure("building_rejected", result.Failure ?? "Building placement was rejected."));
    }

    stateFile.Save(runtime);
    var placed = runtime.WorldSimulation.Buildings.Single(item => item.InstanceId == result.InstanceId);
    var town = placed.TownId is { } townId
        ? runtime.Towns.Single(item => item.Id == townId)
        : null;
    var telemetry = loggerFactory.CreateLogger("ClankerWorld.Town");
    TownTelemetry.Transition(telemetry, runtime.WorldTick, town?.Id ?? "none",
        town is null ? TownTransitionKind.BuildingUnassigned : TownTransitionKind.BuildingAssigned,
        town?.ResidentIds.Count ?? 0, town?.AssignedBuildingIds.Count ?? 0, town?.BorderTiles.Count ?? 0);
    if (town is not null && town.BorderTiles.Count != previousBorderTiles)
        TownTelemetry.Transition(telemetry, runtime.WorldTick, town.Id, TownTransitionKind.BorderExpanded,
            town.ResidentIds.Count, town.AssignedBuildingIds.Count, town.BorderTiles.Count);
    return Results.Ok(result);
});

app.MapPost("/api/v1/owner/production/start", (
    OwnerSignedHttpRequest<OwnerProductionStartAction> request,
    OwnerRequestAuthorizer authorizer,
    IServiceProvider services) =>
{
    if (!isPrivateWorld)
    {
        return Results.Conflict(new OwnerControlFailure(
            "private_world_required",
            "Recipe production is available only in the integrated private world."));
    }

    if (request?.Action is null)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["action"] = ["A production-start action is required."],
        });
    }

    string payload;
    try
    {
        payload = OwnerContentBinding.ProductionStartPayload(request.Action);
    }
    catch (ArgumentException exception)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["action"] = [exception.Message],
        });
    }

    var authorization = authorizer.Authorize(request, "POST", "/api/v1/owner/production/start", payload);
    if (!authorization.IsSuccess)
    {
        return OwnerFailures.ToHttpResult(authorization.Failure);
    }

    var runtime = services.GetRequiredService<PrivateWorldRuntime>();
    var stateFile = services.GetRequiredService<PrivateWorldStateFile>();
    var result = runtime.StartProduction(
        request.Action.RecipeId,
        request.Action.BuildingInstanceId,
        request.Action.WorkerId);
    if (!result.Applied)
    {
        return Results.Conflict(new OwnerControlFailure("production_rejected", result.Failure ?? "Recipe production was rejected."));
    }

    stateFile.Save(runtime);
    return Results.Ok(result);
});

app.MapPost("/api/v1/owner/authoring", (
    OwnerSignedHttpRequest<OwnerAuthoringBatchAction> request,
    OwnerRequestAuthorizer authorizer,
    IServiceProvider services) =>
{
    if (request?.Action is null)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["action"] = ["An authoring batch is required."],
        });
    }

    if (isPrivateWorld)
    {
        return Results.Conflict(new OwnerControlFailure(
            "private_world_authoring_pending",
            "Private-world authoring is not migrated yet; content activation remains disabled in the alpha runtime."));
    }

    string payload;
    try
    {
        payload = OwnerHttpBinding.AuthoringPayload(request.Action);
    }
    catch (ArgumentException exception)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["action"] = [exception.Message],
        });
    }

    var authorization = authorizer.Authorize(request, "POST", "/api/v1/owner/authoring", payload);
    if (!authorization.IsSuccess)
    {
        return OwnerFailures.ToHttpResult(authorization.Failure);
    }

    if (!OwnerAuthoringMapper.TryMap(request.Action, out var batch, out var failure))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["action"] = [failure],
        });
    }

    var runtime = services.GetRequiredService<OwnerWorldRuntime>();
    var stateFile = services.GetRequiredService<OwnerWorldStateFile>();
    var receipt = runtime.ApplyAuthoringBatch(batch! with
    {
        IssuerId = $"owner-device:{authorization.Value!.DeviceId}",
    });
    if (receipt.Applied)
    {
        stateFile.Save(runtime);
    }

    return Results.Ok(receipt);
});

app.MapPost("/api/v1/owner/pairings/approve", (
    OwnerSignedHttpRequest<OwnerPairingApprovalAction> request,
    OwnerRequestAuthorizer authorizer,
    OwnerAuthorityStore authority,
    OwnerAuthorityStateFile stateFile) =>
{
    if (request?.Action is null)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["action"] = ["A pairing ID and comparison code are required."],
        });
    }

    string payload;
    try
    {
        payload = OwnerHttpBinding.PairingApprovalPayload(request.Action);
    }
    catch (ArgumentException exception)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["action"] = [exception.Message],
        });
    }

    var authorization = authorizer.Authorize(request, "POST", "/api/v1/owner/pairings/approve", payload);
    if (!authorization.IsSuccess)
    {
        return OwnerFailures.ToHttpResult(authorization.Failure);
    }

    var approved = authority.ApprovePendingPairing(request.Action.PairingId, request.Action.PairingCode);
    // Treat paired-device approval exactly like local bootstrap approval: a
    // failed comparison changes the bounded durable attempt count.
    stateFile.Save(authority);
    if (!approved.IsSuccess)
    {
        return OwnerFailures.ToHttpResult(approved.Failure);
    }

    return Results.Ok(approved.Value);
});

app.MapPost("/api/v1/owner/devices/revoke", (
    OwnerSignedHttpRequest<OwnerDeviceManagementAction> request,
    OwnerRequestAuthorizer authorizer,
    OwnerAuthorityStore authority,
    OwnerAuthorityStateFile stateFile) =>
{
    if (request?.Action is null)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["action.deviceId"] = ["A device ID is required."],
        });
    }

    string payload;
    try
    {
        payload = OwnerHttpBinding.DeviceManagementPayload(request.Action);
    }
    catch (ArgumentException exception)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["action"] = [exception.Message],
        });
    }

    var authorization = authorizer.Authorize(request, "POST", "/api/v1/owner/devices/revoke", payload);
    if (!authorization.IsSuccess)
    {
        return OwnerFailures.ToHttpResult(authorization.Failure);
    }

    var revoked = authority.RevokeDevice(request.Action.DeviceId);
    stateFile.Save(authority);
    if (!revoked.IsSuccess)
    {
        return OwnerFailures.ToHttpResult(revoked.Failure);
    }

    return Results.Ok(revoked.Value);
});

// Paired devices may inspect the public-key registry, but this is still a
// signed, one-use request rather than a bearer-style read endpoint. The
// result contains only OwnerDevice records: public SPKIs/fingerprints and
// lifecycle timestamps, never pairing codes, challenge nonces, or secrets.
app.MapPost("/api/v1/owner/devices/list", (
    OwnerSignedHttpRequest<OwnerDeviceListAction> request,
    OwnerRequestAuthorizer authorizer,
    OwnerAuthorityStore authority) =>
{
    if (request?.Action is null)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["action"] = ["A device-list action is required."],
        });
    }

    var authorization = authorizer.Authorize(
        request,
        "POST",
        "/api/v1/owner/devices/list",
        OwnerHttpBinding.DeviceListPayload());
    if (!authorization.IsSuccess)
    {
        return OwnerFailures.ToHttpResult(authorization.Failure);
    }

    return Results.Ok(authority.GetDevices());
});

// Provider status is owner-only even though it contains no key material. It
// reveals which hosted account integration is active and therefore uses the
// same one-use signed request boundary as every other private-world control.
app.MapPost("/api/v1/owner/providers/status", (
    OwnerSignedHttpRequest<OwnerProviderStatusAction> request,
    OwnerRequestAuthorizer authorizer,
    ProviderConfigurationStore providers) =>
{
    if (request?.Action is null)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["action"] = ["A provider-status action is required."],
        });
    }

    var authorization = authorizer.Authorize(
        request,
        "POST",
        "/api/v1/owner/providers/status",
        OwnerHttpBinding.ProviderStatusPayload());
    if (!authorization.IsSuccess)
    {
        return OwnerFailures.ToHttpResult(authorization.Failure);
    }

    return Results.Ok(providers.CaptureStatus());
});

app.MapPost("/api/v1/owner/usage/status", (
    OwnerSignedHttpRequest<OwnerUsageStatusAction> request,
    OwnerRequestAuthorizer authorizer,
    ProviderUsageStore usage) =>
{
    if (request?.Action is null)
        return Results.BadRequest(new { error = "A usage-status action is required." });
    var authorization = authorizer.Authorize(request, "POST", "/api/v1/owner/usage/status",
        OwnerHttpBinding.UsageStatusPayload());
    return authorization.IsSuccess ? Results.Ok(usage.Capture()) : OwnerFailures.ToHttpResult(authorization.Failure);
});

app.MapPost("/api/v1/owner/usage/limit", (
    OwnerSignedHttpRequest<ProviderUsageLimitAction> request,
    OwnerRequestAuthorizer authorizer,
    ProviderUsageStore usage) =>
{
    if (request?.Action is null)
        return Results.BadRequest(new { error = "A usage-limit action is required." });
    string payload;
    try { payload = OwnerHttpBinding.UsageLimitPayload(request.Action); }
    catch (ArgumentException exception)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["action"] = [exception.Message] });
    }
    var authorization = authorizer.Authorize(request, "POST", "/api/v1/owner/usage/limit", payload);
    if (!authorization.IsSuccess) return OwnerFailures.ToHttpResult(authorization.Failure);
    try
    {
        var status = usage.Configure(request.Action);
        ProviderUsageTelemetry.LimitConfigured(app.Logger, status.AttemptLimit is not null,
            status.AttemptLimit, status.Attempts);
        return Results.Ok(status);
    }
    catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or OverflowException)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["action"] = [exception.Message] });
    }
});

app.MapPost("/api/v1/owner/providers/slots/delete", (
    OwnerSignedHttpRequest<OwnerCredentialSlotDeletionAction> request,
    OwnerRequestAuthorizer authorizer,
    ProviderConfigurationStore providers,
    ILogger<ProviderConfigurationStore> logger) =>
{
    if (request?.Action is null)
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["action"] = ["A credential-slot deletion action is required."],
        });

    string payload;
    try
    {
        payload = OwnerHttpBinding.CredentialSlotDeletionPayload(request.Action);
    }
    catch (ArgumentException exception)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["action"] = [exception.Message],
        });
    }

    var authorization = authorizer.Authorize(
        request, "POST", "/api/v1/owner/providers/slots/delete", payload);
    if (!authorization.IsSuccess)
        return OwnerFailures.ToHttpResult(authorization.Failure);

    try
    {
        var status = providers.DeleteCredentialSlot(request.Action.CredentialSlotId);
        OwnerCredentialSlotTelemetry.Deleted(logger, "deleted", request.Action.CredentialSlotId);
        return Results.Ok(status);
    }
    catch (ArgumentException exception)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["action"] = [exception.Message],
        });
    }
    catch (InvalidOperationException exception)
    {
        OwnerCredentialSlotTelemetry.Deleted(logger, "assigned", request.Action.CredentialSlotId);
        return Results.Conflict(new { error = exception.Message });
    }
});

app.MapPost("/api/v1/owner/providers/configure", (
    OwnerSignedHttpRequest<OwnerProviderConfigurationAction> request,
    OwnerRequestAuthorizer authorizer,
    ProviderConfigurationStore providers,
    OwnerWorldObservationStore observations) =>
{
    if (request?.Action is null)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["action"] = ["A provider-configuration action is required."],
        });
    }

    string payload;
    try
    {
        payload = OwnerHttpBinding.ProviderConfigurationPayload(request.Action);
    }
    catch (ArgumentException exception)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["action"] = [exception.Message],
        });
    }

    var authorization = authorizer.Authorize(
        request,
        "POST",
        "/api/v1/owner/providers/configure",
        payload);
    if (!authorization.IsSuccess)
    {
        return OwnerFailures.ToHttpResult(authorization.Failure);
    }

    try
    {
        if (request.Action.InhabitantId is { } target &&
            !observations.GetSnapshot().Inhabitants.Any(item => item.Id == target))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["inhabitantId"] = ["Choose an inhabitant in this world."],
            });
        }
        return Results.Ok(providers.Configure(request.Action));
    }
    catch (ArgumentException exception)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["action"] = [exception.Message],
        });
    }
});

// Legacy diagnostic routes deliberately remain present only to make their
// read denial explicit (and to retain a 405 response for accidental POSTs).
app.MapGet("/api/v1/world", OwnerFailures.UnpairedObservation);
app.MapGet("/api/v1/events", OwnerFailures.UnpairedObservation);
app.MapGet("/api/v1/reconnect", OwnerFailures.UnpairedObservation);

app.Run();

static bool IsControl(OwnerSignedHttpRequest<OwnerControlAction>? request, string expectedOperation) =>
    request?.Action is not null &&
    string.Equals(request.Action.Operation, expectedOperation, StringComparison.Ordinal);

static bool TryWorldOptions(OwnerWorldCreationAction action, out GeographyOptions? options)
{
    options = null;
    if (action.Name is null || action.Name.Length is < 1 or > 80 || action.Name.Any(char.IsControl) ||
        action.Seed is null || action.Seed.Length is < 1 or > 100 || action.Seed.Any(char.IsControl) ||
        !Enum.TryParse<WorldSizePreset>(action.Size, true, out var size) ||
        !Enum.IsDefined(size) || size is not (WorldSizePreset.Small or WorldSizePreset.Medium) ||
        !Enum.TryParse<ClimateMode>(action.ClimateMode, true, out var climateMode) ||
        !Enum.IsDefined(climateMode) ||
        !Enum.TryParse<ClimateZone>(action.SelectedClimate, true, out var selectedClimate) ||
        !Enum.IsDefined(selectedClimate) ||
        !Enum.TryParse<ResourceAbundance>(action.ResourceAbundance, true, out var abundance) ||
        !Enum.IsDefined(abundance) ||
        action.WaterPercent is < 10 or > 80)
        return false;
    options = new GeographyOptions(action.Seed, size, action.WrapEastWest, action.WaterPercent,
        climateMode, selectedClimate, action.LatitudeCooling, abundance);
    return true;
}

static bool TryParseInstructionKind(string? value, out OwnerInstructionKind kind)
{
    kind = value?.Trim().ToLowerInvariant() switch
    {
        "suggestive" => OwnerInstructionKind.Suggestive,
        "must_do" => OwnerInstructionKind.MustDo,
        _ => default,
    };
    return value is not null && (value.Trim().Equals("suggestive", StringComparison.OrdinalIgnoreCase) ||
        value.Trim().Equals("must_do", StringComparison.OrdinalIgnoreCase));
}

public partial class Program;

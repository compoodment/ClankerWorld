using System.Net;
using System.Text.Json;
using ClankerWorld.Simulation.Cognition;

namespace ClankerWorld.Viewer.Control;

/// <summary>
/// Runs one explicit, metered request through the same personal-model adapter
/// used by agents. It does not change provider configuration or try another
/// request shape when a provider refuses the game's JSON response format.
/// </summary>
public sealed class ProviderSetupCheckService(
    ProviderConfigurationStore configuration,
    ProviderUsageStore usage,
    IHttpClientFactory httpClientFactory)
{
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    private const string ModelName = "setup-check";
    private const string CandidateId = "safe_idle";

    public async Task<OwnerProviderSetupCheckResult> CheckAsync(
        OwnerProviderSetupCheckAction action,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        _ = OwnerHttpBinding.ProviderSetupCheckPayload(action);

        var providerId = PlayerDecisionProviders.Normalize(action.Provider);
        var model = action.Model.Trim();
        var runtime = configuration.CaptureRuntimeConfiguration();
        var apiKey = ResolveApiKey(runtime, action, providerId);
        if (apiKey is null)
            return Result("missing_key", "Choose or add an API key first. Nothing was sent.");
        if (apiKey.Length == 0)
            return Result("missing_key", "Choose or add an API key first. Nothing was sent.");

        string ticket;
        try
        {
            // The reservation is persisted before constructing or sending the
            // one provider request. Every outcome after this point is charged.
            ticket = usage.Begin(providerId, model, "setup");
        }
        catch (ProviderUsageLimitReachedException)
        {
            return Result("usage_limit", "The paid-call limit is reached. No check was sent.");
        }
        catch (InvalidOperationException) when (usage.Capture().AccountingError is not null)
        {
            return Result("usage_limit", "Paid calls are blocked because usage accounting is unavailable. Repair it before testing a model.");
        }

        var request = CreateRequest();
        var endpoint = providerId == PlayerDecisionProviders.OpenAi
            ? PlayerDecisionProviders.OpenAiEndpoint
            : PlayerDecisionProviders.OllamaCloudEndpoint;
        var provider = new OpenAiCompatibleDecisionProvider(
            httpClientFactory.CreateClient("model"),
            () => apiKey,
            endpoint,
            model,
            requestTimeout: RequestTimeout,
            providerEpoch: request.ProviderEpoch);

        CognitionDecisionResponse response;
        try
        {
            response = await provider.DecideAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            usage.Finish(ticket, cancellationToken.IsCancellationRequested ? "abandoned" : "failed");
            if (cancellationToken.IsCancellationRequested)
                throw;
            return Result("timed_out", "The provider did not answer within 15 seconds. This paid check was counted.");
        }
        catch (HttpRequestException exception)
        {
            usage.Finish(ticket, "failed");
            return exception.StatusCode switch
            {
                HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity =>
                    Result("unsupported_format", "The provider rejected the game's required JSON response format. This paid check was counted; choose a supported model."),
                HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout =>
                    Result("timed_out", "The provider did not answer in time. This paid check was counted."),
                _ => Result("unavailable", "The provider could not complete the check. This paid check was counted; check the key, model and connection."),
            };
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            usage.Finish(ticket, "failed");
            return IsUnusableReply(exception)
                ? Result("unusable", "The provider answered, but its reply did not match the game's required format. This paid check was counted.")
                : Result("unavailable", "The provider could not complete the check. This paid check was counted; check the key, model and connection.");
        }

        try
        {
            response.Validate();
            if (response.SelectedCandidateId != CandidateId || response.Probabilities.Keys.Any(key => key != CandidateId))
                throw new InvalidDataException("The provider selected an unoffered setup-check action.");
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or InvalidDataException)
        {
            usage.Finish(ticket, "failed", response.Usage?.InputTokens ?? 0, response.Usage?.OutputTokens ?? 0);
            return Result("unusable", "The provider answered, but its reply did not match the game's required format. This paid check was counted.");
        }

        usage.Finish(ticket, "completed", response.Usage?.InputTokens ?? 0, response.Usage?.OutputTokens ?? 0);
        return new OwnerProviderSetupCheckResult(
            "ready",
            "The provider returned a reply the game can use. This check used one paid call.",
            IsReady: true);
    }

    private static string? ResolveApiKey(
        RuntimeProviderConfiguration runtime,
        OwnerProviderSetupCheckAction action,
        string provider)
    {
        if (!string.IsNullOrWhiteSpace(action.ApiKey))
            return action.ApiKey.Trim();

        if (action.CredentialSlotId is { } slotId)
        {
            var slot = runtime.CredentialSlots?.FirstOrDefault(item => item.Id == slotId && item.Provider == provider);
            return string.IsNullOrWhiteSpace(slot?.ApiKey) ? null : slot.ApiKey.Trim();
        }

        var credential = provider == PlayerDecisionProviders.OpenAi ? runtime.OpenAi : runtime.OllamaCloud;
        return string.IsNullOrWhiteSpace(credential.ApiKey) ? null : credential.ApiKey.Trim();
    }

    private static CognitionDecisionRequest CreateRequest() => new(
        "owner-model-setup-check",
        0,
        new InhabitantObservation(
            ModelName,
            WorldTick: 0,
            RunEpoch: 0,
            DecisionGeneration: 0,
            ObservationDigest: "sha256:owner-model-setup-check",
            HungerBasisPoints: 5_000,
            Candidates: [new CognitionCandidate(CandidateId, "Wait safely for a moment.")]));

    private static bool IsUnusableReply(Exception exception) => exception is
        InvalidDataException or JsonException or ArgumentException or InvalidOperationException or
        KeyNotFoundException or IndexOutOfRangeException or FormatException;

    private static OwnerProviderSetupCheckResult Result(string outcome, string message) =>
        new(outcome, message, IsReady: false);
}

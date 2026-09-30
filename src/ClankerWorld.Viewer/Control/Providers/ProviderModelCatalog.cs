using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ClankerWorld.Viewer.Control;

/// <summary>
/// The game's own short list of models for each hosted provider, so the owner
/// picks from a few current models instead of everything a provider offers.
/// When a key is known, the host also asks the provider which models that key
/// can use and marks the listed models it can't. The host already holds the
/// keys; a key the owner has only just pasted is used for this one check and
/// is not stored. Checking costs no tokens, so it is not a metered model call.
/// </summary>
public sealed class ProviderModelCatalog(
    ProviderConfigurationStore configuration,
    IHttpClientFactory httpClientFactory,
    TimeProvider? time = null)
{
    public static readonly Uri OpenAiModels = new("https://api.openai.com/v1/models", UriKind.Absolute);
    public static readonly Uri OllamaCloudModels = new("https://ollama.com/api/tags", UriKind.Absolute);
    public static readonly Uri OllamaCloudCompatibleModels = new("https://ollama.com/v1/models", UriKind.Absolute);

    /// <summary>
    /// The models offered for each provider, top to bottom: the newest
    /// generation first and, within a generation, the larger models first.
    /// Adding a model means adding its exact API name in its place. Owners can
    /// still type any other model name.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> Curated =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            [PlayerDecisionProviders.OpenAi] = ["gpt-6.1-sol", "gpt-6-astra", "gpt-6-sol", "gpt-6-luna"],
            [PlayerDecisionProviders.OllamaCloud] =
            [
                "glm-5.3-flash:cloud",
                "glm-5.3:cloud",
                "deepseek-v4.1-flash:cloud",
                "deepseek-v4-pro:cloud",
                "minimax-m3:cloud",
                "kimi-k3:cloud",
                "gemma4:cloud",
            ],
        };

    /// <summary>How long a key's check is reused before the provider is asked again.</summary>
    public static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(10);

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(8);
    private const int MaximumBodyBytes = 2 * 1024 * 1024;

    private readonly TimeProvider clock = time ?? TimeProvider.System;
    private readonly object gate = new();
    private readonly Dictionary<string, (DateTimeOffset Expires, IReadOnlySet<string> Offered)> cache = [];

    public async Task<OwnerProviderModelList> ListAsync(OwnerProviderModelListAction action, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        var provider = PlayerDecisionProviders.Normalize(action.Provider);
        if (!Curated.TryGetValue(provider, out var curated))
            throw new ArgumentException("Only OpenAI and Ollama Cloud offer a model list.", nameof(action));
        var defaultModel = PlayerDecisionProviders.DefaultModel(provider);
        var name = DisplayName(provider);
        OwnerProviderModelList Unchecked(string? error) =>
            new(provider, [.. curated.Select(model => new OwnerProviderModelChoice(model, true))], defaultModel, error);

        if (!action.CheckKey) return Unchecked(null);
        if (ResolveKey(provider, action) is not { } apiKey)
            return Unchecked($"Add an API key for {name} first.");

        var cacheKey = provider + ":" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(apiKey)));
        IReadOnlySet<string>? offered = null;
        lock (gate)
        {
            if (cache.TryGetValue(cacheKey, out var cached) && cached.Expires > clock.GetUtcNow())
                offered = cached.Offered;
        }

        try
        {
            if (offered is null)
            {
                var listed = await FetchAsync(provider, apiKey, cancellationToken).ConfigureAwait(false);
                offered = listed.Select(MatchName).ToHashSet(StringComparer.Ordinal);
                lock (gate)
                    cache[cacheKey] = (clock.GetUtcNow() + CacheLifetime, offered);
            }
            return new OwnerProviderModelList(provider,
                [.. curated.Select(model => new OwnerProviderModelChoice(model, offered.Contains(MatchName(model))))],
                defaultModel, null);
        }
        catch (ProviderRefusedKeyException)
        {
            return Unchecked($"{name} refused this key.");
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException or InvalidDataException &&
            !cancellationToken.IsCancellationRequested)
        {
            return Unchecked($"Couldn't check this key with {name}. Check the key or your connection.");
        }
    }

    /// <summary>
    /// The part of a model name that identifies it across a provider's routes.
    /// Ollama names the same cloud model with or without a <c>:cloud</c> or
    /// <c>-cloud</c> ending, and a bare name means <c>:latest</c>.
    /// </summary>
    public static string MatchName(string model)
    {
        var name = model.Trim().ToLowerInvariant();
        foreach (var ending in (string[])[":cloud", "-cloud", ":latest"])
        {
            if (name.EndsWith(ending, StringComparison.Ordinal))
                name = name[..^ending.Length];
        }
        return name;
    }

    /// <summary>A key pasted now, a named key slot, or the provider's default key, in that order.</summary>
    private string? ResolveKey(string provider, OwnerProviderModelListAction action)
    {
        if (!string.IsNullOrWhiteSpace(action.ApiKey)) return action.ApiKey.Trim();
        var runtime = configuration.CaptureRuntimeConfiguration();
        if (action.CredentialSlotId is { } slotId)
        {
            return runtime.CredentialSlots?.FirstOrDefault(slot => slot.Id == slotId && slot.Provider == provider)?.ApiKey is { Length: > 0 } slotKey
                ? slotKey
                : throw new ArgumentException("That named key no longer exists for this provider.", nameof(action));
        }
        var stored = provider == PlayerDecisionProviders.OpenAi ? runtime.OpenAi : runtime.OllamaCloud;
        return string.IsNullOrWhiteSpace(stored.ApiKey) ? null : stored.ApiKey;
    }

    private async Task<IReadOnlyList<string>> FetchAsync(string provider, string apiKey, CancellationToken cancellationToken)
    {
        if (provider == PlayerDecisionProviders.OpenAi)
            return await GetListAsync(OpenAiModels, apiKey, cancellationToken).ConfigureAwait(false) ??
                throw new HttpRequestException("OpenAI has no model list endpoint.");
        // Ollama Cloud lists its models natively; the OpenAI-style route is the fallback.
        return await GetListAsync(OllamaCloudModels, apiKey, cancellationToken).ConfigureAwait(false) ??
            await GetListAsync(OllamaCloudCompatibleModels, apiKey, cancellationToken).ConfigureAwait(false) ??
            throw new HttpRequestException("Ollama Cloud has no model list endpoint.");
    }

    /// <summary>The models at one endpoint, or null when the provider has no such route.</summary>
    private async Task<IReadOnlyList<string>?> GetListAsync(Uri endpoint, string apiKey, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var client = httpClientFactory.CreateClient("model");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new ProviderRefusedKeyException();
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed)
            return null;
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaximumBodyBytes)
            throw new InvalidDataException("The model list is too large.");
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        using var body = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) > 0)
        {
            if (body.Length + read > MaximumBodyBytes)
                throw new InvalidDataException("The model list is too large.");
            body.Write(buffer, 0, read);
        }
        return Parse(body.ToArray());
    }

    /// <summary>
    /// Reads either list shape: OpenAI-style <c>data[].id</c>, or Ollama's
    /// native <c>models[].name</c> (or <c>model</c>).
    /// </summary>
    public static IReadOnlyList<string> Parse(byte[] body)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("The provider returned an unrecognised model list.");
        var models = new List<string>();
        if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in data.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException("The provider returned an unrecognised model list.");
                if (item.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                    models.Add(id.GetString()!);
            }
        }
        else if (root.TryGetProperty("models", out var listed) && listed.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in listed.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException("The provider returned an unrecognised model list.");
                var name = item.TryGetProperty("name", out var named) && named.ValueKind == JsonValueKind.String ? named.GetString()
                    : item.TryGetProperty("model", out var model) && model.ValueKind == JsonValueKind.String ? model.GetString() : null;
                if (name is not null) models.Add(name);
            }
        }
        else
        {
            throw new InvalidDataException("The provider returned an unrecognised model list.");
        }
        return models;
    }

    private static string DisplayName(string provider) => provider == PlayerDecisionProviders.OpenAi ? "OpenAI" : "Ollama Cloud";

    private sealed class ProviderRefusedKeyException : Exception;
}

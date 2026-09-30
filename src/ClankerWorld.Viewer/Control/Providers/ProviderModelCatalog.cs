using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ClankerWorld.Viewer.Control;

/// <summary>
/// Asks a hosted provider which models an API key can use, so the owner picks
/// one from a list instead of typing its name. The host already holds the
/// keys; a key the owner has only just pasted is used for this one lookup and
/// is not stored. Listing costs no tokens, so it is not a metered model call.
/// </summary>
public sealed class ProviderModelCatalog(
    ProviderConfigurationStore configuration,
    IHttpClientFactory httpClientFactory,
    TimeProvider? time = null)
{
    public static readonly Uri OpenAiModels = new("https://api.openai.com/v1/models", UriKind.Absolute);
    public static readonly Uri OllamaCloudModels = new("https://ollama.com/api/tags", UriKind.Absolute);
    public static readonly Uri OllamaCloudCompatibleModels = new("https://ollama.com/v1/models", UriKind.Absolute);

    /// <summary>How long a key's list is reused before the provider is asked again.</summary>
    public static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(10);

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(8);
    private const int MaximumModels = 200;
    private const int MaximumBodyBytes = 2 * 1024 * 1024;

    private readonly TimeProvider clock = time ?? TimeProvider.System;
    private readonly object gate = new();
    private readonly Dictionary<string, (DateTimeOffset Expires, IReadOnlyList<string> Models)> cache = [];

    public async Task<OwnerProviderModelList> ListAsync(OwnerProviderModelListAction action, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        var provider = PlayerDecisionProviders.Normalize(action.Provider);
        if (provider is not (PlayerDecisionProviders.OpenAi or PlayerDecisionProviders.OllamaCloud))
            throw new ArgumentException("Only OpenAI and Ollama Cloud offer a model list.", nameof(action));
        var defaultModel = PlayerDecisionProviders.DefaultModel(provider);
        var name = DisplayName(provider);
        if (ResolveKey(provider, action) is not { } apiKey)
            return new OwnerProviderModelList(provider, [], defaultModel, $"Add an API key for {name} first.");

        var cacheKey = provider + ":" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(apiKey)));
        lock (gate)
        {
            if (cache.TryGetValue(cacheKey, out var cached) && cached.Expires > clock.GetUtcNow())
                return new OwnerProviderModelList(provider, cached.Models, defaultModel, null);
        }

        try
        {
            var listed = await FetchAsync(provider, apiKey, cancellationToken).ConfigureAwait(false);
            var models = Order(provider, listed);
            lock (gate)
                cache[cacheKey] = (clock.GetUtcNow() + CacheLifetime, models);
            return new OwnerProviderModelList(provider, models, defaultModel, null);
        }
        catch (ProviderRefusedKeyException)
        {
            return new OwnerProviderModelList(provider, [], defaultModel, $"{name} refused this key.");
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException or InvalidDataException &&
            !cancellationToken.IsCancellationRequested)
        {
            return new OwnerProviderModelList(provider, [], defaultModel,
                $"Couldn't get the model list from {name}. Check the key or your connection, or type a model name.");
        }
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

    private async Task<IReadOnlyList<ListedModel>> FetchAsync(string provider, string apiKey, CancellationToken cancellationToken)
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
    private async Task<IReadOnlyList<ListedModel>?> GetListAsync(Uri endpoint, string apiKey, CancellationToken cancellationToken)
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
        var body = await response.Content.ReadAsByteArrayAsync(timeout.Token).ConfigureAwait(false);
        if (body.Length > MaximumBodyBytes)
            throw new InvalidDataException("The model list is too large.");
        return Parse(body);
    }

    /// <summary>
    /// Reads either list shape: OpenAI-style <c>data[].id</c> with a
    /// <c>created</c> time, or Ollama's native <c>models[].name</c> with
    /// <c>modified_at</c>.
    /// </summary>
    public static IReadOnlyList<ListedModel> Parse(byte[] body)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var models = new List<ListedModel>();
        if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in data.EnumerateArray())
            {
                if (item.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                {
                    var created = item.TryGetProperty("created", out var time) && time.TryGetInt64(out var seconds)
                        ? DateTimeOffset.FromUnixTimeSeconds(seconds) : DateTimeOffset.MinValue;
                    models.Add(new ListedModel(id.GetString()!, created));
                }
            }
        }
        else if (root.TryGetProperty("models", out var listed) && listed.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in listed.EnumerateArray())
            {
                var name = item.TryGetProperty("name", out var named) && named.ValueKind == JsonValueKind.String ? named.GetString()
                    : item.TryGetProperty("model", out var model) && model.ValueKind == JsonValueKind.String ? model.GetString() : null;
                if (name is null) continue;
                var modified = item.TryGetProperty("modified_at", out var time) && time.ValueKind == JsonValueKind.String &&
                    DateTimeOffset.TryParse(time.GetString(), System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed) ? parsed : DateTimeOffset.MinValue;
                models.Add(new ListedModel(name, modified));
            }
        }
        else
        {
            throw new InvalidDataException("The provider returned an unrecognised model list.");
        }
        return models;
    }

    /// <summary>
    /// Chat models only, oldest to newest. OpenAI lists image, speech,
    /// embedding and other models on the same route, so those are left out by
    /// name. A very long list keeps its newest models.
    /// </summary>
    public static IReadOnlyList<string> Order(string provider, IEnumerable<ListedModel> listed) => listed
        .Where(model => model.Id.Length is > 0 and <= 200 && !model.Id.Any(char.IsControl))
        .Where(model => provider != PlayerDecisionProviders.OpenAi || IsOpenAiChatModel(model.Id))
        .GroupBy(model => model.Id, StringComparer.Ordinal).Select(group => group.First())
        .OrderByDescending(model => model.Created)
        .Take(MaximumModels)
        .OrderBy(model => model.Created)
        .ThenBy(model => model.Id, StringComparer.Ordinal)
        .Select(model => model.Id)
        .ToArray();

    /// <summary>
    /// OpenAI's chat families (<c>gpt-…</c>, <c>chatgpt-…</c> and the
    /// <c>o1</c>/<c>o3</c>/<c>o4</c> reasoning models), without the audio,
    /// realtime, speech, image, search and other variants that cannot answer
    /// the game's chat requests.
    /// </summary>
    public static bool IsOpenAiChatModel(string id)
    {
        var lower = id.ToLowerInvariant();
        var chatFamily = lower.StartsWith("gpt-", StringComparison.Ordinal) ||
            lower.StartsWith("chatgpt-", StringComparison.Ordinal) ||
            lower.Length > 1 && lower[0] == 'o' && char.IsAsciiDigit(lower[1]);
        if (!chatFamily) return false;
        string[] excluded = ["audio", "realtime", "tts", "transcribe", "whisper", "image", "embedding", "moderation",
            "search", "instruct", "dall-e", "codex", "computer-use", "deep-research", "-pro"];
        return !excluded.Any(part => lower.Contains(part, StringComparison.Ordinal));
    }

    private static string DisplayName(string provider) => provider == PlayerDecisionProviders.OpenAi ? "OpenAI" : "Ollama Cloud";

    public sealed record ListedModel(string Id, DateTimeOffset Created);

    private sealed class ProviderRefusedKeyException : Exception;
}

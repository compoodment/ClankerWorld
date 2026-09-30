using System.Net;
using System.Text;
using ClankerWorld.Viewer.Control;

namespace ClankerWorld.Simulation.Tests;

public sealed class ProviderModelCatalogTests
{
    private const string OpenAiList = """
        {"object":"list","data":[
          {"id":"gpt-4o","object":"model","created":1715367049},
          {"id":"gpt-6","object":"model","created":1785000000},
          {"id":"gpt-6-luna","object":"model","created":1784000000},
          {"id":"gpt-6-luna-pro","object":"model","created":1784500000},
          {"id":"gpt-5.6-luna","object":"model","created":1775000000},
          {"id":"gpt-5-mini","object":"model","created":1754425777},
          {"id":"gpt-5","object":"model","created":1754425928},
          {"id":"text-embedding-3-small","object":"model","created":1705948997},
          {"id":"gpt-4o-audio-preview","object":"model","created":1727460443},
          {"id":"gpt-4o-realtime-preview","object":"model","created":1727659998},
          {"id":"gpt-image-1","object":"model","created":1745517030},
          {"id":"dall-e-3","object":"model","created":1698785189},
          {"id":"whisper-1","object":"model","created":1677532384},
          {"id":"o3","object":"model","created":1744225308},
          {"id":"o1-pro","object":"model","created":1742251791},
          {"id":"omni-moderation-latest","object":"model","created":1731689265},
          {"id":"gpt-4o-mini-search-preview","object":"model","created":1741391161},
          {"id":"gpt-3.5-turbo-instruct","object":"model","created":1692901427}
        ]}
        """;

    private const string OllamaTags = """
        {"models":[
          {"name":"qwen3-coder:480b-cloud","model":"qwen3-coder:480b-cloud","modified_at":"2025-09-17T10:00:00Z"},
          {"name":"gpt-oss:120b-cloud","model":"gpt-oss:120b-cloud","modified_at":"2025-08-05T10:00:00Z"},
          {"name":"deepseek-v3.1:671b-cloud","model":"deepseek-v3.1:671b-cloud","modified_at":"2025-09-22T10:00:00Z"}
        ]}
        """;

    [Fact]
    public void OpenAiListKeepsChatModelsWithTheRecommendedModelFirstThenNewest()
    {
        var models = ProviderModelCatalog.Order(PlayerDecisionProviders.OpenAi,
            ProviderModelCatalog.Parse(Encoding.UTF8.GetBytes(OpenAiList)), PlayerDecisionProviders.DefaultOpenAiModel);
        Assert.Equal(["gpt-6-luna", "gpt-6", "gpt-5.6-luna", "gpt-5", "gpt-5-mini", "o3", "gpt-4o"], models);
    }

    [Fact]
    public void OllamaListReadsNativeTagsNewestFirstWithTheRecommendedModelOnTop()
    {
        var models = ProviderModelCatalog.Order(PlayerDecisionProviders.OllamaCloud,
            ProviderModelCatalog.Parse(Encoding.UTF8.GetBytes(OllamaTags)), PlayerDecisionProviders.DefaultOllamaCloudModel);
        Assert.Equal(["gpt-oss:120b-cloud", "deepseek-v3.1:671b-cloud", "qwen3-coder:480b-cloud"], models);
    }

    [Fact]
    public void UnrecognisedListShapeIsRejected()
    {
        Assert.Throws<InvalidDataException>(() => ProviderModelCatalog.Parse(Encoding.UTF8.GetBytes("""{"items":[]}""")));
    }

    [Fact]
    public async Task SavedKeyIsSentOnlyToTheProviderAndTheListIsReusedUntilItExpires()
    {
        using var directory = new TemporaryDirectory();
        var store = directory.Store();
        _ = store.Configure(new("planning", "openai", "gpt-5-mini", "saved-openai-secret", false));
        var handler = new ListHandler(_ => (HttpStatusCode.OK, OpenAiList));
        var clock = new ManualClock();
        var catalog = new ProviderModelCatalog(store, new ClientFactory(handler), clock);

        var first = await catalog.ListAsync(new("openai"), CancellationToken.None);
        Assert.Null(first.Error);
        Assert.Equal("gpt-6-luna", first.Models[0]);
        Assert.Equal("gpt-6-luna", first.Recommended);
        Assert.Equal(ProviderModelCatalog.OpenAiModels, handler.Requests.Single().Uri);
        Assert.Equal("Bearer saved-openai-secret", handler.Requests.Single().Authorization);

        _ = await catalog.ListAsync(new("openai"), CancellationToken.None);
        Assert.Single(handler.Requests);
        clock.Advance(ProviderModelCatalog.CacheLifetime + TimeSpan.FromSeconds(1));
        _ = await catalog.ListAsync(new("openai"), CancellationToken.None);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task PastedKeyIsUsedForTheLookupWithoutBeingSaved()
    {
        using var directory = new TemporaryDirectory();
        var store = directory.Store();
        var handler = new ListHandler(_ => (HttpStatusCode.OK, OllamaTags));
        var catalog = new ProviderModelCatalog(store, new ClientFactory(handler));

        var list = await catalog.ListAsync(new("ollama-cloud", ApiKey: " pasted-secret "), CancellationToken.None);

        Assert.Null(list.Error);
        Assert.Equal(3, list.Models.Count);
        Assert.Equal("Bearer pasted-secret", handler.Requests.Single().Authorization);
        Assert.Null(store.CaptureRuntimeConfiguration().OllamaCloud.ApiKey);
        Assert.DoesNotContain("pasted-secret", File.ReadAllText(directory.ProvidersPath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task NamedKeySlotIsUsedAndAMissingSlotIsRejected()
    {
        using var directory = new TemporaryDirectory();
        var store = directory.Store();
        var slot = Guid.NewGuid().ToString("N");
        _ = store.Configure(new("personal", "openai", "gpt-5", "slot-secret", false, "inhabitant-test", slot, "Second account"));
        var handler = new ListHandler(_ => (HttpStatusCode.OK, OpenAiList));
        var catalog = new ProviderModelCatalog(store, new ClientFactory(handler));

        var list = await catalog.ListAsync(new("openai", CredentialSlotId: slot), CancellationToken.None);
        Assert.Null(list.Error);
        Assert.Equal("Bearer slot-secret", handler.Requests.Single().Authorization);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            catalog.ListAsync(new("openai", CredentialSlotId: Guid.NewGuid().ToString("N")), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            catalog.ListAsync(new("ollama-cloud", CredentialSlotId: slot), CancellationToken.None));
    }

    [Fact]
    public async Task MissingKeyRefusedKeyAndFailuresExplainThemselvesWithoutTheKey()
    {
        using var directory = new TemporaryDirectory();
        var store = directory.Store();

        var none = await new ProviderModelCatalog(store, new ClientFactory(new ListHandler(_ => throw new InvalidOperationException())))
            .ListAsync(new("openai"), CancellationToken.None);
        Assert.Equal("Add an API key for OpenAI first.", none.Error);
        Assert.Empty(none.Models);

        var refused = await new ProviderModelCatalog(store, new ClientFactory(new ListHandler(_ => (HttpStatusCode.Unauthorized, "{}"))))
            .ListAsync(new("openai", ApiKey: "wrong-secret"), CancellationToken.None);
        Assert.Equal("OpenAI refused this key.", refused.Error);

        var broken = await new ProviderModelCatalog(store, new ClientFactory(new ListHandler(_ => (HttpStatusCode.OK, "not json"))))
            .ListAsync(new("openai", ApiKey: "some-secret"), CancellationToken.None);
        Assert.StartsWith("Couldn't get the model list from OpenAI.", broken.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("some-secret", broken.Error, StringComparison.Ordinal);

        var offline = await new ProviderModelCatalog(store, new ClientFactory(new ListHandler(_ => throw new HttpRequestException("offline"))))
            .ListAsync(new("ollama-cloud", ApiKey: "some-secret"), CancellationToken.None);
        Assert.StartsWith("Couldn't get the model list from Ollama Cloud.", offline.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FailedListIsNotCached()
    {
        using var directory = new TemporaryDirectory();
        var store = directory.Store();
        var fail = true;
        var handler = new ListHandler(_ => fail ? (HttpStatusCode.InternalServerError, "{}") : (HttpStatusCode.OK, OpenAiList));
        var catalog = new ProviderModelCatalog(store, new ClientFactory(handler));

        Assert.NotNull((await catalog.ListAsync(new("openai", ApiKey: "retry-secret"), CancellationToken.None)).Error);
        fail = false;
        Assert.Null((await catalog.ListAsync(new("openai", ApiKey: "retry-secret"), CancellationToken.None)).Error);
    }

    [Fact]
    public async Task OllamaFallsBackToTheOpenAiStyleListWhenTheNativeRouteIsMissing()
    {
        using var directory = new TemporaryDirectory();
        var handler = new ListHandler(uri => uri == ProviderModelCatalog.OllamaCloudModels
            ? (HttpStatusCode.NotFound, "{}")
            : (HttpStatusCode.OK, """{"object":"list","data":[{"id":"gpt-oss:120b","created":1754000000}]}"""));
        var catalog = new ProviderModelCatalog(directory.Store(), new ClientFactory(handler));

        var list = await catalog.ListAsync(new("ollama-cloud", ApiKey: "ollama-secret"), CancellationToken.None);

        Assert.Null(list.Error);
        Assert.Equal(["gpt-oss:120b"], list.Models);
        Assert.Equal([ProviderModelCatalog.OllamaCloudModels, ProviderModelCatalog.OllamaCloudCompatibleModels],
            handler.Requests.Select(request => request.Uri));
    }

    [Fact]
    public async Task ProvidersWithoutAModelListAreRejected()
    {
        using var directory = new TemporaryDirectory();
        var catalog = new ProviderModelCatalog(directory.Store(), new ClientFactory(new ListHandler(_ => (HttpStatusCode.OK, "{}"))));
        await Assert.ThrowsAsync<ArgumentException>(() => catalog.ListAsync(new("jev"), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => catalog.ListAsync(new("deterministic"), CancellationToken.None));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("clankerworld-model-list-");

        public string ProvidersPath => Path.Combine(directory.FullName, "providers.json");

        public ProviderConfigurationStore Store() => new(ProvidersPath,
            new ProviderConfigurationSeed("deterministic", null, null, null, null, null, null));

        public void Dispose() => directory.Delete(recursive: true);
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan duration) => now += duration;
    }

    private sealed class ClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class ListHandler(Func<Uri, (HttpStatusCode Status, string Body)> answer) : HttpMessageHandler
    {
        public List<(Uri Uri, string? Authorization)> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.RequestUri!, request.Headers.Authorization?.ToString()));
            var (status, body) = answer(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }
}

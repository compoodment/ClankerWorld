using System.Net;
using System.Text;
using ClankerWorld.Viewer.Control;

namespace ClankerWorld.Simulation.Tests;

public sealed class ProviderModelCatalogTests
{
    // An account that can use Luna and Sol but not the newer or larger models.
    private const string OpenAiList = """
        {"object":"list","data":[
          {"id":"gpt-6-luna","object":"model","created":1790000000},
          {"id":"gpt-6-sol","object":"model","created":1790000000},
          {"id":"gpt-5.6-terra","object":"model","created":1783000000},
          {"id":"text-embedding-3-small","object":"model","created":1705948997}
        ]}
        """;

    // Ollama's native list names cloud models without the ":cloud" the app uses.
    private const string OllamaTags = """
        {"models":[
          {"name":"glm-5.3-flash","model":"glm-5.3-flash","modified_at":"2026-09-20T10:00:00Z"},
          {"name":"deepseek-v4-pro:0813","model":"deepseek-v4-pro:0813","modified_at":"2026-08-13T10:00:00Z"},
          {"name":"kimi-k3:cloud","model":"kimi-k3:cloud","modified_at":"2026-08-20T10:00:00Z"},
          {"name":"gemma4:31b","model":"gemma4:31b","modified_at":"2026-08-21T10:00:00Z"},
          {"name":"gpt-oss:120b","model":"gpt-oss:120b","modified_at":"2025-08-05T10:00:00Z"}
        ]}
        """;

    [Fact]
    public void GameListsKeepTheirChosenOrderAndIncludeEachDefault()
    {
        Assert.Equal(["gpt-6.1-sol", "gpt-6-astra", "gpt-6-sol", "gpt-6-luna"],
            ProviderModelCatalog.Curated[PlayerDecisionProviders.OpenAi]);
        Assert.Equal(["glm-5.3-flash:cloud", "glm-5.3:cloud", "deepseek-v4.1-flash:cloud", "deepseek-v4-pro:0813",
            "minimax-m3:cloud", "kimi-k3:cloud", "gemma4:31b"], ProviderModelCatalog.Curated[PlayerDecisionProviders.OllamaCloud]);
        Assert.Equal("gpt-6-luna", PlayerDecisionProviders.DefaultOpenAiModel);
        Assert.Equal("glm-5.3-flash:cloud", PlayerDecisionProviders.DefaultOllamaCloudModel);
        foreach (var (provider, models) in ProviderModelCatalog.Curated)
        {
            Assert.Contains(PlayerDecisionProviders.DefaultModel(provider), models);
            Assert.Equal(models.Count, models.Distinct(StringComparer.Ordinal).Count());
        }
    }

    [Theory]
    [InlineData("glm-5.3:cloud", "glm-5.3")]
    [InlineData("gpt-oss:120b-cloud", "gpt-oss:120b")]
    [InlineData("Gemma4:latest", "gemma4")]
    [InlineData("gpt-6-luna", "gpt-6-luna")]
    public void MatchingIgnoresOllamaCloudEndings(string listed, string expected) =>
        Assert.Equal(expected, ProviderModelCatalog.MatchName(listed));

    [Fact]
    public void BothListShapesAreReadAndOthersAreRejected()
    {
        Assert.Equal(["gpt-6-luna", "gpt-6-sol", "gpt-5.6-terra", "text-embedding-3-small"],
            ProviderModelCatalog.Parse(Encoding.UTF8.GetBytes(OpenAiList)));
        Assert.Equal(["glm-5.3-flash", "deepseek-v4-pro:0813", "kimi-k3:cloud", "gemma4:31b", "gpt-oss:120b"],
            ProviderModelCatalog.Parse(Encoding.UTF8.GetBytes(OllamaTags)));
        Assert.Throws<InvalidDataException>(() => ProviderModelCatalog.Parse(Encoding.UTF8.GetBytes("""{"items":[]}""")));
    }

    [Fact]
    public async Task SavedKeyMarksListedModelsItCantUseAndTheCheckIsReusedUntilItExpires()
    {
        using var directory = new TemporaryDirectory();
        var store = directory.Store();
        _ = store.Configure(new("planning", "openai", "gpt-6-luna", "saved-openai-secret", false));
        var handler = new ListHandler(_ => (HttpStatusCode.OK, OpenAiList));
        var clock = new ManualClock();
        var catalog = new ProviderModelCatalog(store, new ClientFactory(handler), clock);

        var first = await catalog.ListAsync(new("openai"), CancellationToken.None);
        Assert.Null(first.Error);
        Assert.Equal([new("gpt-6.1-sol", false), new("gpt-6-astra", false), new("gpt-6-sol", true), new("gpt-6-luna", true)],
            first.Models);
        Assert.Equal("gpt-6-luna", first.DefaultModel);
        Assert.Equal(ProviderModelCatalog.OpenAiModels, handler.Requests.Single().Uri);
        Assert.Equal("Bearer saved-openai-secret", handler.Requests.Single().Authorization);

        _ = await catalog.ListAsync(new("openai"), CancellationToken.None);
        Assert.Single(handler.Requests);
        clock.Advance(ProviderModelCatalog.CacheLifetime + TimeSpan.FromSeconds(1));
        _ = await catalog.ListAsync(new("openai"), CancellationToken.None);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task PastedKeyIsCheckedWithoutBeingSavedAndCloudEndingsStillMatch()
    {
        using var directory = new TemporaryDirectory();
        var store = directory.Store();
        var handler = new ListHandler(_ => (HttpStatusCode.OK, OllamaTags));
        var catalog = new ProviderModelCatalog(store, new ClientFactory(handler));

        var list = await catalog.ListAsync(new("ollama-cloud", ApiKey: " pasted-secret "), CancellationToken.None);

        Assert.Null(list.Error);
        Assert.Equal(["glm-5.3-flash:cloud", "deepseek-v4-pro:0813", "kimi-k3:cloud", "gemma4:31b"],
            list.Models.Where(item => item.Available).Select(item => item.Model));
        Assert.Equal(ProviderModelCatalog.Curated[PlayerDecisionProviders.OllamaCloud], list.Models.Select(item => item.Model));
        Assert.Equal("Bearer pasted-secret", handler.Requests.Single().Authorization);
        Assert.Null(store.CaptureRuntimeConfiguration().OllamaCloud.ApiKey);
        Assert.DoesNotContain("pasted-secret", File.ReadAllText(directory.ProvidersPath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListWithoutAKeyCheckAsksNoProvider()
    {
        using var directory = new TemporaryDirectory();
        var store = directory.Store();
        _ = store.Configure(new("planning", "openai", "gpt-6-luna", "saved-openai-secret", false));
        var handler = new ListHandler(_ => throw new InvalidOperationException("No provider call expected."));
        var catalog = new ProviderModelCatalog(store, new ClientFactory(handler));

        var list = await catalog.ListAsync(new("openai", CheckKey: false), CancellationToken.None);

        Assert.Null(list.Error);
        Assert.All(list.Models, item => Assert.True(item.Available));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task NamedKeySlotIsUsedAndAMissingSlotIsRejected()
    {
        using var directory = new TemporaryDirectory();
        var store = directory.Store();
        var slot = Guid.NewGuid().ToString("N");
        _ = store.Configure(new("personal", "openai", "gpt-6-sol", "slot-secret", false, "inhabitant-test", slot, "Second account"));
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
    public async Task KeysThatCantBeCheckedKeepTheWholeListUsableAndExplainWithoutTheKey()
    {
        using var directory = new TemporaryDirectory();
        var store = directory.Store();
        var curated = ProviderModelCatalog.Curated[PlayerDecisionProviders.OpenAi];

        var none = await new ProviderModelCatalog(store, new ClientFactory(new ListHandler(_ => throw new InvalidOperationException())))
            .ListAsync(new("openai"), CancellationToken.None);
        Assert.Equal("Add an API key for OpenAI first.", none.Error);
        Assert.Equal(curated, none.Models.Select(item => item.Model));
        Assert.All(none.Models, item => Assert.True(item.Available));

        var refused = await new ProviderModelCatalog(store, new ClientFactory(new ListHandler(_ => (HttpStatusCode.Unauthorized, "{}"))))
            .ListAsync(new("openai", ApiKey: "wrong-secret"), CancellationToken.None);
        Assert.Equal("OpenAI refused this key.", refused.Error);
        Assert.All(refused.Models, item => Assert.True(item.Available));

        var broken = await new ProviderModelCatalog(store, new ClientFactory(new ListHandler(_ => (HttpStatusCode.OK, "not json"))))
            .ListAsync(new("openai", ApiKey: "some-secret"), CancellationToken.None);
        Assert.StartsWith("Couldn't check this key with OpenAI.", broken.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("some-secret", broken.Error, StringComparison.Ordinal);

        var offline = await new ProviderModelCatalog(store, new ClientFactory(new ListHandler(_ => throw new HttpRequestException("offline"))))
            .ListAsync(new("ollama-cloud", ApiKey: "some-secret"), CancellationToken.None);
        Assert.StartsWith("Couldn't check this key with Ollama Cloud.", offline.Error, StringComparison.Ordinal);
        Assert.Equal(7, offline.Models.Count);
    }

    [Fact]
    public async Task FailedCheckIsNotCached()
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

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("7")]
    [InlineData("{\"data\":[null]}")]
    [InlineData("{\"models\":[\"unexpected\"]}")]
    public async Task MalformedListShapesKeepModelsUsableAndCanBeRetried(string body)
    {
        using var directory = new TemporaryDirectory();
        var broken = true;
        var handler = new ListHandler(_ => (HttpStatusCode.OK, broken ? body : OpenAiList));
        var catalog = new ProviderModelCatalog(directory.Store(), new ClientFactory(handler));
        var action = new OwnerProviderModelListAction("openai", ApiKey: "shape-secret");
        var result = await catalog.ListAsync(action, CancellationToken.None);
        Assert.StartsWith("Couldn't check this key", result.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("shape-secret", result.Error, StringComparison.Ordinal);
        Assert.All(result.Models, item => Assert.True(item.Available));
        broken = false;
        Assert.Null((await catalog.ListAsync(action, CancellationToken.None)).Error);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task UnknownLengthReplyStopsReadingAtTheBodyLimit()
    {
        using var directory = new TemporaryDirectory();
        using var stream = new CountingReplyStream(new byte[10 * 1024 * 1024]);
        var catalog = new ProviderModelCatalog(directory.Store(), new ClientFactory(new ContentHandler(new StreamContent(stream))));
        var result = await catalog.ListAsync(new("openai", ApiKey: "bounded-secret"), CancellationToken.None);
        Assert.NotNull(result.Error);
        Assert.All(result.Models, item => Assert.True(item.Available));
        Assert.InRange(stream.BytesRead, 1, 2 * 1024 * 1024 + 8192);
    }

    [Fact]
    public async Task OllamaFallsBackToTheOpenAiStyleListWhenTheNativeRouteIsMissing()
    {
        using var directory = new TemporaryDirectory();
        var handler = new ListHandler(uri => uri == ProviderModelCatalog.OllamaCloudModels
            ? (HttpStatusCode.NotFound, "{}")
            : (HttpStatusCode.OK, """{"object":"list","data":[{"id":"minimax-m3","created":1754000000}]}"""));
        var catalog = new ProviderModelCatalog(directory.Store(), new ClientFactory(handler));

        var list = await catalog.ListAsync(new("ollama-cloud", ApiKey: "ollama-secret"), CancellationToken.None);

        Assert.Null(list.Error);
        Assert.Equal(["minimax-m3:cloud"], list.Models.Where(item => item.Available).Select(item => item.Model));
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

    private sealed class ContentHandler(HttpContent content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
    }

    private sealed class CountingReplyStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
        public int BytesRead { get; private set; }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await base.ReadAsync(buffer, cancellationToken);
            BytesRead += read;
            return read;
        }
    }
}

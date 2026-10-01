using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClankerWorld.GodotClient.UI;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Control;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ClientSetupCheckAction = ClankerWorld.GodotClient.UI.OwnerProviderSetupCheckAction;
using ServerSetupCheckAction = ClankerWorld.Viewer.Control.OwnerProviderSetupCheckAction;
using ServerSetupCheckResult = ClankerWorld.Viewer.Control.OwnerProviderSetupCheckResult;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class ViewerHttpTests
{
    private const string SetupCheckPath = "/api/v1/owner/providers/setup-check";

    [Fact]
    public async Task SignedSetupCheckUsesTheRealPersonalAdapterOnceAndMetersTheCall()
    {
        var directory = Directory.CreateTempSubdirectory("clankerworld-provider-setup-check-");
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        const string apiKey = "owner-setup-check-test-secret";
        const string model = "test-personal-model";
        var handler = new ControlledSetupCheckHandler((_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.OK,
            """{"model":"test-personal-model","choices":[{"message":{"content":"{\"selected_candidate_id\":\"safe_idle\",\"confidence\":0.31}"}}],"usage":{"prompt_tokens":17,"completion_tokens":4}}""")));
        using var baseHost = new ViewerWebApplicationFactory(directory.FullName, privateWorld: true, legacyPrivateWorld: false);
        using var host = baseHost.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHttpClientFactory>();
            services.AddSingleton<IHttpClientFactory>(new SetupCheckHttpClientFactory(handler));
        }));
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://127.0.0.1/")
        });
        try
        {
            var device = await StartAndActivateAsync(host, client, key);
            var action = new ServerSetupCheckAction("openai", model, ApiKey: apiKey);
            var clientAction = new ClientSetupCheckAction("openai", model, ApiKey: apiKey);
            var payload = OwnerHttpBinding.ProviderSetupCheckPayload(action);
            Assert.Equal(payload, OwnerWorldActionPayload.ProviderSetupCheck(clientAction));
            Assert.DoesNotContain(apiKey, payload, StringComparison.Ordinal);

            var tampered = await CreateSignedRequestAsync(host, client, key, device.DeviceId,
                SetupCheckPath, action, payload);
            using (var rejected = await client.PostAsJsonAsync(SetupCheckPath,
                tampered with { Action = action with { ApiKey = "different-setup-check-key" } }))
                Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
            Assert.Equal(0, handler.RequestCount);

            var identity = host.Services.GetRequiredService<OwnerAuthorityStore>().Identity;
            using var signer = new SettingsKeySigner(key);
            var api = new OwnerWorldApi(client);
            var result = await api.CheckProviderSetupAsync(client.BaseAddress!,
                new(identity.ServerAuthorityId, identity.WorldId), device.DeviceId,
                clientAction, signer, CancellationToken.None);
            Assert.Equal("ready", result.Outcome);
            Assert.True(result.IsReady);
            var resultJson = JsonSerializer.Serialize(result);
            Assert.DoesNotContain(apiKey, resultJson, StringComparison.Ordinal);

            Assert.Equal(1, handler.RequestCount);
            Assert.Equal(new Uri("https://api.openai.com/v1/chat/completions"), handler.LastUri);
            Assert.Equal($"Bearer {apiKey}", handler.LastAuthorization);
            Assert.DoesNotContain(apiKey, handler.LastBody, StringComparison.Ordinal);
            using (var request = JsonDocument.Parse(handler.LastBody!))
            {
                var root = request.RootElement;
                Assert.Equal(model, root.GetProperty("model").GetString());
                Assert.Equal("json_object", root.GetProperty("response_format").GetProperty("type").GetString());
                Assert.Equal(2, root.GetProperty("messages").GetArrayLength());
                Assert.False(root.TryGetProperty("temperature", out _));
                Assert.False(root.TryGetProperty("max_tokens", out _));
                Assert.False(root.TryGetProperty("max_completion_tokens", out _));
            }

            var meter = host.Services.GetRequiredService<ProviderUsageStore>().Capture();
            Assert.Equal(1, meter.Attempts);
            Assert.Equal(1, meter.Completed);
            Assert.Equal(0, meter.Failed);
            Assert.Equal(17, meter.InputTokens);
            Assert.Equal(4, meter.OutputTokens);
            var row = Assert.Single(meter.Rows);
            Assert.Equal(("openai", model, "setup"), (row.Provider, row.Model, row.Role));

            var providers = host.Services.GetRequiredService<ProviderConfigurationStore>();
            Assert.Null(providers.CaptureRuntimeConfiguration().OpenAi.ApiKey);
            Assert.DoesNotContain(apiKey, File.ReadAllText(providers.Path), StringComparison.Ordinal);
        }
        finally
        {
            handler.Dispose();
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task MissingKeyAndUsageLimitDoNotSendTheModelRequest()
    {
        var directory = Directory.CreateTempSubdirectory("clankerworld-provider-setup-limit-");
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var handler = new ControlledSetupCheckHandler((_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.OK,
            """{"model":"test-model","choices":[{"message":{"content":"{\"selected_candidate_id\":\"safe_idle\",\"confidence\":0.8}"}}]}""")));
        using var baseHost = new ViewerWebApplicationFactory(directory.FullName, privateWorld: true, legacyPrivateWorld: false);
        using var host = baseHost.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHttpClientFactory>();
            services.AddSingleton<IHttpClientFactory>(new SetupCheckHttpClientFactory(handler));
        }));
        using var client = host.CreateClient();
        try
        {
            var device = await StartAndActivateAsync(host, client, key);
            var usage = host.Services.GetRequiredService<ProviderUsageStore>();
            var missingKey = new ServerSetupCheckAction("openai", "test-model");
            using (var missingResponse = await SendSignedAsync(host, client, key, device.DeviceId,
                SetupCheckPath, missingKey, OwnerHttpBinding.ProviderSetupCheckPayload(missingKey)))
            {
                var missing = await missingResponse.Content.ReadFromJsonAsync<ServerSetupCheckResult>();
                Assert.Equal("missing_key", missing!.Outcome);
                Assert.Equal(0, usage.Capture().Attempts);
                Assert.Equal(0, handler.RequestCount);
            }

            usage.Configure(new ProviderUsageLimitAction(1));
            var withKey = missingKey with { ApiKey = "metered-setup-secret" };
            using (var allowedResponse = await SendSignedAsync(host, client, key, device.DeviceId,
                SetupCheckPath, withKey, OwnerHttpBinding.ProviderSetupCheckPayload(withKey)))
            {
                var allowed = await allowedResponse.Content.ReadFromJsonAsync<ServerSetupCheckResult>();
                Assert.Equal("ready", allowed!.Outcome);
            }
            using (var cappedResponse = await SendSignedAsync(host, client, key, device.DeviceId,
                SetupCheckPath, withKey, OwnerHttpBinding.ProviderSetupCheckPayload(withKey)))
            {
                var capped = await cappedResponse.Content.ReadFromJsonAsync<ServerSetupCheckResult>();
                Assert.Equal("usage_limit", capped!.Outcome);
                Assert.Contains("No check was sent", capped.Message, StringComparison.Ordinal);
            }
            Assert.Equal(1, handler.RequestCount);
            Assert.Equal(1, usage.Capture().Attempts);
        }
        finally
        {
            handler.Dispose();
            directory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("unsupported", "unsupported_format")]
    [InlineData("timeout", "timed_out")]
    [InlineData("unavailable", "unavailable")]
    [InlineData("unusable", "unusable")]
    public async Task SetupCheckReturnsOnlyBoundedFailureOutcomesAndNeverRetries(string failure, string expectedOutcome)
    {
        var directory = Directory.CreateTempSubdirectory("clankerworld-provider-setup-failure-");
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        const string apiKey = "bounded-setup-key-secret";
        const string privateBody = "provider-private-error-body";
        var handler = new ControlledSetupCheckHandler((_, _) => failure switch
        {
            "unsupported" => Task.FromResult(JsonResponse(HttpStatusCode.BadRequest,
                JsonSerializer.Serialize(new { error = new { message = privateBody } }))),
            "timeout" => Task.FromException<HttpResponseMessage>(new TaskCanceledException(privateBody)),
            "unavailable" => Task.FromException<HttpResponseMessage>(new HttpRequestException(privateBody)),
            _ => Task.FromResult(JsonResponse(HttpStatusCode.OK, privateBody)),
        });
        using var baseHost = new ViewerWebApplicationFactory(directory.FullName, privateWorld: true, legacyPrivateWorld: false);
        using var host = baseHost.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHttpClientFactory>();
            services.AddSingleton<IHttpClientFactory>(new SetupCheckHttpClientFactory(handler));
        }));
        using var client = host.CreateClient();
        try
        {
            var device = await StartAndActivateAsync(host, client, key);
            var action = new ServerSetupCheckAction("openai", "failure-test-model", ApiKey: apiKey);
            using var response = await SendSignedAsync(host, client, key, device.DeviceId,
                SetupCheckPath, action, OwnerHttpBinding.ProviderSetupCheckPayload(action));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var result = await response.Content.ReadFromJsonAsync<ServerSetupCheckResult>();
            Assert.Equal(expectedOutcome, result!.Outcome);
            Assert.False(result.IsReady);
            Assert.DoesNotContain(apiKey, result.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(privateBody, result.Message, StringComparison.Ordinal);
            Assert.Equal(1, handler.RequestCount);
            Assert.Equal(1, host.Services.GetRequiredService<ProviderUsageStore>().Capture().Attempts);
            Assert.Equal(1, host.Services.GetRequiredService<ProviderUsageStore>().Capture().Failed);
        }
        finally
        {
            handler.Dispose();
            directory.Delete(recursive: true);
        }
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private sealed class SetupCheckHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class ControlledSetupCheckHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public Uri? LastUri { get; private set; }
        public string? LastAuthorization { get; private set; }
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            LastUri = request.RequestUri;
            LastAuthorization = request.Headers.Authorization?.ToString();
            LastBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return await respond(request, cancellationToken);
        }
    }
}

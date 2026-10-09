using System.Text.Json;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;
using ClankerWorld.Simulation.Cognition;

namespace ClankerWorld.Viewer.Control;

/// <summary>
/// Sends one agent prompt to Anthropic's Messages API through Anthropic's
/// official client. The game's fixed instructions go in the system prompt with
/// a cache marker, so calls that repeat them cost less; the agent's thinking
/// level becomes the request's effort, and the model default sends none.
/// Only the reply's text comes back: thinking blocks are dropped and never
/// logged or saved. Nothing is retried here, because a retry is another paid
/// call; the cognition boundary already falls back to built-in choices.
/// The owner's key is the only credential sent: the client never reads the
/// host's own Anthropic token or login profile.
/// </summary>
public sealed class AnthropicModelClient(HttpClient httpClient, string apiKey, Uri? baseUrl = null) : IHostedModelClient
{
    /// <summary>Room for the model's thinking as well as the short JSON reply.</summary>
    public const int MaximumOutputTokens = 16_000;

    private const int MaximumReportedTokens = 1_000_000;

    public async Task<HostedModelReply> CompleteAsync(HostedModelCall request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        var client = Create(httpClient, apiKey, baseUrl ?? PlayerDecisionProviders.AnthropicEndpoint, request.Timeout);
        var parameters = new MessageCreateParams
        {
            Model = request.Model,
            MaxTokens = MaximumOutputTokens,
            System = new List<TextBlockParam>
            {
                new() { Text = request.Instructions, CacheControl = new CacheControlEphemeral() },
            },
            Messages = [new() { Role = Role.User, Content = request.Input }],
            OutputConfig = ModelThinking.Normalize(request.Thinking) switch
            {
                null => null,
                ModelThinking.Low => new OutputConfig { Effort = Effort.Low },
                ModelThinking.Medium => new OutputConfig { Effort = Effort.Medium },
                _ => new OutputConfig { Effort = Effort.High },
            },
        };

        string? stopReason;
        string text;
        string? model;
        int inputTokens;
        int outputTokens;
        try
        {
            var message = await client.Messages.Create(parameters, cancellationToken).ConfigureAwait(false);
            // The client reads reply fields lazily, so a malformed reply can fail here too.
            stopReason = message.StopReason?.Raw();
            text = string.Concat(message.Content
                .Select(block => block.TryPickText(out var textBlock) ? textBlock.Text : null)
                .Where(item => item is not null));
            model = message.Model.Raw();
            var usage = message.Usage;
            inputTokens = Bounded(usage.InputTokens + (usage.CacheCreationInputTokens ?? 0) + (usage.CacheReadInputTokens ?? 0));
            outputTokens = Bounded(usage.OutputTokens);
        }
        catch (AnthropicApiException exception)
        {
            throw new HttpRequestException(
                $"Anthropic returned HTTP {(int)exception.StatusCode} ({exception.StatusCode}).", exception, exception.StatusCode);
        }
        catch (Exception exception) when (exception is JsonException or AnthropicInvalidDataException)
        {
            throw new InvalidDataException("Anthropic returned a reply the game could not read.", exception);
        }
        catch (AnthropicException exception)
        {
            throw new HttpRequestException("Anthropic could not complete the request.", exception);
        }

        // These replies were charged, so they keep their token counts.
        if (stopReason == "refusal")
            throw HostedModelUnusableReply.Create("The model declined this request.", inputTokens, outputTokens);
        if (stopReason == "max_tokens")
            throw HostedModelUnusableReply.Create("The model ran out of room before it finished its answer.", inputTokens, outputTokens);
        if (string.IsNullOrWhiteSpace(text))
            throw HostedModelUnusableReply.Create("The model returned no answer text.", inputTokens, outputTokens);
        return new HostedModelReply(WithoutCodeFence(text), model, inputTokens, outputTokens);
    }

    /// <summary>
    /// Anthropic's official client for one owner key. Setting no auth token
    /// stops the client falling back to the host's environment token or login
    /// profile, so the owner's chosen key is the only credential sent.
    /// </summary>
    internal static AnthropicClient Create(HttpClient httpClient, string apiKey, Uri baseUrl, TimeSpan timeout) => new()
    {
        ApiKey = apiKey,
        AuthToken = null,
        HttpClient = httpClient,
        BaseUrl = baseUrl.AbsoluteUri.TrimEnd('/'),
        MaxRetries = 0,
        Timeout = timeout,
    };

    private static int Bounded(long tokens) => tokens is >= 0 and <= MaximumReportedTokens
        ? (int)tokens
        : throw new InvalidDataException("Anthropic reported token usage outside its bound.");

    /// <summary>Removes one surrounding Markdown code fence, which the game's JSON readers do not expect.</summary>
    private static string WithoutCodeFence(string text)
    {
        var trimmed = text.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal)) return trimmed;
        var firstLineEnd = trimmed.IndexOf('\n', StringComparison.Ordinal);
        var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        return firstLineEnd > 0 && lastFence > firstLineEnd ? trimmed[(firstLineEnd + 1)..lastFence].Trim() : trimmed;
    }
}

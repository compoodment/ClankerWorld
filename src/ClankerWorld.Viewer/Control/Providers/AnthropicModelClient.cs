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
        var client = new AnthropicClient
        {
            ApiKey = apiKey,
            HttpClient = httpClient,
            BaseUrl = (baseUrl ?? PlayerDecisionProviders.AnthropicEndpoint).AbsoluteUri.TrimEnd('/'),
            MaxRetries = 0,
            Timeout = request.Timeout,
        };
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

        Message message;
        try
        {
            message = await client.Messages.Create(parameters, cancellationToken).ConfigureAwait(false);
        }
        catch (AnthropicApiException exception)
        {
            throw new HttpRequestException(
                $"Anthropic returned HTTP {(int)exception.StatusCode} ({exception.StatusCode}).", exception, exception.StatusCode);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Anthropic returned a reply the game could not read.", exception);
        }

        var stopReason = message.StopReason?.Raw();
        if (stopReason == "refusal")
            throw new InvalidDataException("The model declined this request.");
        if (stopReason == "max_tokens")
            throw new InvalidDataException("The model ran out of room before it finished its answer.");
        var text = string.Concat(message.Content
            .Select(block => block.TryPickText(out var textBlock) ? textBlock.Text : null)
            .Where(item => item is not null));
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidDataException("The model returned no answer text.");

        var usage = message.Usage;
        var inputTokens = Bounded(usage.InputTokens + (usage.CacheCreationInputTokens ?? 0) + (usage.CacheReadInputTokens ?? 0));
        var outputTokens = Bounded(usage.OutputTokens);
        return new HostedModelReply(WithoutCodeFence(text), message.Model.Raw(), inputTokens, outputTokens);
    }

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

namespace ClankerWorld.Simulation.Cognition;

/// <summary>
/// How much an agent's hosted model reasons before it answers. The owner picks
/// one per agent with its model. <see langword="null"/> keeps the model's own
/// default and sends nothing; each adapter maps the three levels to its
/// provider's setting. A model that refuses the setting fails the request like
/// any other provider error, and the agent falls back to built-in choices.
/// </summary>
public static class ModelThinking
{
    public const string Low = "low";
    public const string Medium = "medium";
    public const string High = "high";

    /// <summary>Returns <see langword="null"/> for the model default, otherwise low, medium or high.</summary>
    public static string? Normalize(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        null or "" or "default" => null,
        Low => Low,
        Medium => Medium,
        High => High,
        _ => throw new ArgumentException("Thinking must be default, low, medium or high.", nameof(value)),
    };
}

/// <summary>One hosted model call: fixed instructions and one input message, answered with one text reply.</summary>
public sealed record HostedModelCall(string Model, string Instructions, string Input, string? Thinking, TimeSpan Timeout);

/// <summary>The reply text and the provider's own token counts, without any hidden reasoning.</summary>
public sealed record HostedModelReply(string Text, string? Model, int InputTokens, int OutputTokens);

/// <summary>
/// A hosted model whose wire format is not chat completions, such as
/// Anthropic's Messages API. The host implements it; the decision and
/// conversation adapters keep the prompts and validate the reply. Failures use
/// the same exceptions as the chat-completions path: an HTTP status becomes an
/// <see cref="HttpRequestException"/> carrying it, an unusable reply an
/// <see cref="InvalidDataException"/>, and a timeout a <see cref="TimeoutException"/>.
/// </summary>
public interface IHostedModelClient
{
    Task<HostedModelReply> CompleteAsync(HostedModelCall request, CancellationToken cancellationToken);
}

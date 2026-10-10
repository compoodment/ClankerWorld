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

/// <summary>
/// A charged reply the game can't use, such as a refusal or an answer cut off
/// at the output limit, is an <see cref="InvalidDataException"/> that keeps the
/// reported token counts, so a failed decision, model check or conversation
/// turn is still metered. Answer parsing can retain usage on its existing error.
/// </summary>
public static class HostedModelUnusableReply
{
    private const string InputTokensKey = "hosted_model_input_tokens";
    private const string OutputTokensKey = "hosted_model_output_tokens";

    public static InvalidDataException Create(string message, int inputTokens, int outputTokens)
    {
        var failure = new InvalidDataException(message);
        RetainTokens(failure, inputTokens, outputTokens);
        return failure;
    }

    public static void RetainTokens(Exception exception, int inputTokens, int outputTokens)
    {
        ArgumentNullException.ThrowIfNull(exception);
        exception.Data[InputTokensKey] = inputTokens;
        exception.Data[OutputTokensKey] = outputTokens;
    }

    public static bool TryGetTokens(Exception exception, out int inputTokens, out int outputTokens)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (exception.Data[InputTokensKey] is int input && exception.Data[OutputTokensKey] is int output)
        {
            (inputTokens, outputTokens) = (input, output);
            return true;
        }
        (inputTokens, outputTokens) = (0, 0);
        return false;
    }
}

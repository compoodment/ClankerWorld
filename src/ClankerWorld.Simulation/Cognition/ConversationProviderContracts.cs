namespace ClankerWorld.Simulation.Cognition;

public enum AgentConversationStatus
{
    Proposed,
    Ready,
    AwaitingSpeaker,
    WrapUp,
    Suspended,
    Closed,
}

public enum AgentConversationInterruption
{
    None,
    OwnerPaused,
    Disconnected,
    UrgentNeed,
    ProviderUnavailable,
    ProviderTimedOut,
    ProviderRejected,
    Restored,
}

public enum AgentConversationDisposition
{
    Continue,
    Withdraw,
}

/// <summary>
/// The only effect ordinary dialogue can propose. It is a small social
/// disposition change, and is applied only after both participants accept the
/// same wrap-up proposal. Speech itself never changes the world.
/// </summary>
public enum AgentConversationEffect
{
    None,
    MutualTrust,
    Marriage,
}

public enum AgentConversationKind
{
    Ordinary,
    MarriageSurname,
}

public sealed record AgentConversationTurn(
    string Id,
    string SpeakerId,
    string Text,
    long WorldTick,
    IReadOnlyList<string> ListenerIds,
    AgentConversationDisposition Disposition,
    bool IsWrapUp = false,
    string? SurnameChoice = null);

/// <summary>Only accepted, bounded public turns and the minimal state needed to resume are saved.</summary>
public sealed record AgentConversation(
    string Id,
    string InitiatorId,
    string InviteeId,
    long CreatedTick,
    long ProposalDeadlineTick,
    long RunEpoch,
    long Revision,
    AgentConversationStatus Status,
    IReadOnlyList<string> AcceptedParticipantIds,
    string? CurrentSpeakerId,
    bool AwaitingWrapUp,
    IReadOnlyList<AgentConversationTurn> Turns,
    AgentConversationEffect WrapUpEffect,
    IReadOnlyList<string> WrapUpAcceptedBy,
    IReadOnlyList<string> ResumeAcceptedBy,
    AgentConversationInterruption Interruption,
    string? Outcome,
    long LastUpdatedTick)
{
    [System.Text.Json.Serialization.JsonRequired]
    public AgentConversationKind Kind { get; init; }
}

/// <summary>One agent's conversation starts or acceptances for the current world day.</summary>
public sealed record AgentConversationDailyBudget(string AgentId, long WorldDay, int Count);

public static class AgentConversationText
{
    public const int MaximumUtteranceCharacters = 500;

    public static bool IsValidUtterance(string? text) =>
        !string.IsNullOrWhiteSpace(text) &&
        text.Length <= MaximumUtteranceCharacters &&
        text == text.Trim() &&
        !text.Any(char.IsControl);
}

public enum AgentConversationPurpose
{
    PublicTurn,
    WrapUp,
    SurnameChoice,
}

public sealed record AgentConversationTurnRequest(
    string RequestId,
    string ConversationId,
    long Revision,
    long RunEpoch,
    long WorldTick,
    AgentConversationPurpose Purpose,
    string SpeakerId,
    string SpeakerName,
    string OtherParticipantId,
    string OtherParticipantName,
    string SpeakerPersonality,
    string SpeakerAspiration,
    IReadOnlyList<AgentConversationTurn> PublicHistory,
    IReadOnlyList<AgentConversationEffect> AllowedEffects)
{
    /// <summary>The personal provider revision captured before dispatching this turn.</summary>
    public long? ExpectedProviderEpoch { get; init; }

    public IReadOnlyList<string> AllowedSurnames { get; init; } = [];

    /// <summary>The outside observer's requested activity for this speaker, separate from public speech and consent.</summary>
    public string? RequestedActivity { get; init; }

    public void Validate()
    {
        ValidateText(RequestId, 128, nameof(RequestId));
        ValidateText(ConversationId, 512, nameof(ConversationId));
        if (Revision < 1 || RunEpoch < 0 || WorldTick < 0 || ExpectedProviderEpoch is < 0 ||
            !Enum.IsDefined(Purpose) ||
            string.Equals(SpeakerId, OtherParticipantId, StringComparison.Ordinal))
            throw new ArgumentException("The conversation request identity is invalid.");
        ValidateText(SpeakerId, 128, nameof(SpeakerId));
        ValidateText(OtherParticipantId, 128, nameof(OtherParticipantId));
        ValidateText(SpeakerName, 128, nameof(SpeakerName));
        ValidateText(OtherParticipantName, 128, nameof(OtherParticipantName));
        ValidateOptionalText(SpeakerPersonality, 256, nameof(SpeakerPersonality));
        ValidateOptionalText(SpeakerAspiration, 256, nameof(SpeakerAspiration));
        ArgumentNullException.ThrowIfNull(PublicHistory);
        ArgumentNullException.ThrowIfNull(AllowedEffects);
        if (RequestedActivity is not (null or "propose_marriage") ||
            Purpose == AgentConversationPurpose.SurnameChoice && RequestedActivity is not null ||
            PublicHistory.Count > 7 || AllowedEffects.Count > 3 || AllowedSurnames is null ||
            (Purpose == AgentConversationPurpose.SurnameChoice
                ? AllowedSurnames.Count is < 1 or > 2 || PublicHistory.Count >= 4 ||
                    AllowedEffects.Count != 1 || AllowedEffects[0] != AgentConversationEffect.None ||
                    AllowedSurnames.Any(surname => string.IsNullOrWhiteSpace(surname) || surname.Length > 128 ||
                        surname.Any(char.IsWhiteSpace) || surname.Any(char.IsControl))
                : AllowedSurnames.Count != 0) ||
            AllowedEffects.Distinct().Count() != AllowedEffects.Count ||
            AllowedEffects.Any(effect => !Enum.IsDefined(effect)) ||
            PublicHistory.Any(turn => !AgentConversationText.IsValidUtterance(turn.Text)))
            throw new ArgumentException("The conversation request exceeds its bounded context.");
    }

    private static void ValidateText(string value, int maximumLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > maximumLength || value != value.Trim() || value.Any(char.IsControl))
            throw new ArgumentException("The conversation request contains invalid text.", parameterName);
    }

    private static void ValidateOptionalText(string value, int maximumLength, string parameterName)
    {
        if (value.Length > maximumLength || value.Any(char.IsControl))
            throw new ArgumentException("The conversation request contains invalid text.", parameterName);
    }
}

public sealed record AgentConversationTurnResponse(
    string RequestId,
    string ConversationId,
    long Revision,
    long RunEpoch,
    string SpeakerId,
    string Text,
    AgentConversationDisposition Disposition,
    AgentConversationEffect Effect = AgentConversationEffect.None,
    int InputTokens = 0,
    int OutputTokens = 0,
    string? ModelId = null,
    string? SurnameChoice = null);

public interface IAgentConversationProvider
{
    long ProviderEpoch { get; }

    /// <summary>True only when this agent has an explicit personal planning assignment.</summary>
    bool CanSpeakAs(string agentId);

    ValueTask<AgentConversationTurnResponse> SpeakAsync(
        AgentConversationTurnRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>Applies one bounded deadline to a personal conversation request.</summary>
public static class AgentConversationProviderExecution
{
    public static async Task<AgentConversationTurnResponse> SpeakAsync(
        IAgentConversationProvider provider,
        AgentConversationTurnRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(request);
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(5))
            throw new ArgumentOutOfRangeException(nameof(timeout));

        using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        requestCancellation.CancelAfter(timeout);
        try
        {
            return await provider.SpeakAsync(request, requestCancellation.Token).AsTask()
                .WaitAsync(requestCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when
            (!cancellationToken.IsCancellationRequested && requestCancellation.IsCancellationRequested)
        {
            requestCancellation.Cancel();
            throw new TimeoutException("The personal conversation provider did not respond before its deadline.", exception);
        }
        catch
        {
            requestCancellation.Cancel();
            throw;
        }
    }
}

/// <summary>Stable, bounded categories for logs and interruption state.</summary>
public static class AgentConversationFailureClassifier
{
    public static AgentConversationInterruption Classify(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception switch
        {
            ProviderConversationUnavailableException => AgentConversationInterruption.ProviderUnavailable,
            TimeoutException => AgentConversationInterruption.ProviderTimedOut,
            OperationCanceledException => AgentConversationInterruption.ProviderTimedOut,
            System.IO.InvalidDataException => AgentConversationInterruption.ProviderRejected,
            _ => AgentConversationInterruption.ProviderUnavailable,
        };
    }
}

public sealed class ProviderConversationUnavailableException(string reason) : InvalidOperationException(reason)
{
}

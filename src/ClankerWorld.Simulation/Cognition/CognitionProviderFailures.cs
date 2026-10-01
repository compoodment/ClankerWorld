using System.Net;
using System.Text.Json;

namespace ClankerWorld.Simulation.Cognition;

/// <summary>Safe outcome codes; exception messages never enter world state.</summary>
public static class CognitionProviderFailures
{
    public static string FromException(Exception exception, CancellationToken cancellationToken) => exception switch
    {
        CognitionProviderUnavailableException unavailable => unavailable.Outcome,
        OperationCanceledException when cancellationToken.IsCancellationRequested => "provider_cancelled",
        OperationCanceledException or TimeoutException => "provider_timed_out",
        InvalidDataException or JsonException or ArgumentException => "unusable_reply",
        HttpRequestException { StatusCode: HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity } => "unsupported_request",
        _ => "model_unavailable",
    };

    public static string Status(string outcome) => outcome switch
    {
        "provider_decision" => "ready",
        "provider_cancelled" => "canceled",
        "provider_timed_out" => "timed_out",
        "missing_key" => "missing_key",
        "usage_limit" => "usage_limit",
        "unusable_reply" or "malformed_response" or "candidate_not_legal" => "unusable_reply",
        _ => "model_unavailable",
    };

    public static bool IsStatus(string status) => status is
        "ready" or "waiting" or "canceled" or "missing_key" or "usage_limit" or
        "unusable_reply" or "timed_out" or "model_unavailable";
}

public class CognitionProviderUnavailableException : InvalidOperationException
{
    public CognitionProviderUnavailableException(string outcome, string message) : base(message)
    {
        if (outcome is not ("missing_key" or "usage_limit" or "unsupported_request"))
            throw new ArgumentOutOfRangeException(nameof(outcome));
        Outcome = outcome;
    }

    public string Outcome { get; }
}

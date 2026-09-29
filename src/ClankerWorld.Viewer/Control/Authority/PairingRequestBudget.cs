using System.Threading.RateLimiting;

namespace ClankerWorld.Viewer.Control;

/// <summary>A shared budget because the Tailnet reverse proxy can hide source IPs.</summary>
public sealed partial class PairingRequestBudget(ILogger<PairingRequestBudget> logger) : IDisposable
{
    private int rejectionLogged;
    private readonly FixedWindowRateLimiter limiter = new(new FixedWindowRateLimiterOptions
    {
        PermitLimit = 8,
        Window = TimeSpan.FromMinutes(1),
        QueueLimit = 0,
        AutoReplenishment = true,
    });

    public bool TryAcquire()
    {
        using var lease = limiter.AttemptAcquire();
        if (lease.IsAcquired) Interlocked.Exchange(ref rejectionLogged, 0);
        else if (Interlocked.Exchange(ref rejectionLogged, 1) == 0) LogRejected(logger);
        return lease.IsAcquired;
    }

    [LoggerMessage(EventId = 2285, Level = LogLevel.Warning,
        Message = "pairing_request_limit outcome=rejected retry_seconds=60")]
    private static partial void LogRejected(ILogger logger);

    [LoggerMessage(EventId = 2286, Level = LogLevel.Information,
        Message = "pairing_local_recovery outcome={Outcome}")]
    public static partial void LogLocalRecovery(ILogger logger, string outcome);

    public void Dispose() => limiter.Dispose();
}

namespace ClankerWorld.Viewer.Control;

public static partial class ProviderCredentialTelemetry
{
    [LoggerMessage(EventId = 2284, Level = LogLevel.Information,
        Message = "provider_credential_storage outcome=ready protection={Protection}")]
    public static partial void Ready(ILogger logger, string protection);
}

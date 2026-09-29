namespace ClankerWorld.Viewer.Control;

public static partial class OwnerCredentialSlotTelemetry
{
    [LoggerMessage(EventId = 2264, Level = LogLevel.Information,
        Message = "provider_credential_slot_delete outcome={Outcome} slot_id={SlotId}")]
    public static partial void Deleted(ILogger logger, string outcome, string slotId);
}

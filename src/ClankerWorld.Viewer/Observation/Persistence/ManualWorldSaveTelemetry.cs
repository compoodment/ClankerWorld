namespace ClankerWorld.Viewer.Observation;

public static partial class ManualWorldSaveTelemetry
{
    [LoggerMessage(EventId = 2257, Level = LogLevel.Warning,
        Message = "manual_save outcome=excluded_from_list save={SaveId} reason={Reason} files_preserved=true")]
    public static partial void InvalidMetadata(ILogger logger, string saveId, string reason);

    [LoggerMessage(EventId = 2250, Level = LogLevel.Information,
        Message = "manual_save outcome=created save={SaveId} tick={WorldTick}")]
    public static partial void Created(ILogger logger, string saveId, long worldTick);

    [LoggerMessage(EventId = 2251, Level = LogLevel.Information,
        Message = "manual_save outcome=loaded save={SaveId} backup={BackupId} tick={WorldTick}")]
    public static partial void Loaded(ILogger logger, string saveId, string backupId, long worldTick);

    [LoggerMessage(EventId = 2256, Level = LogLevel.Information,
        Message = "manual_save outcome=overwritten save={SaveId} backup={BackupId} tick={WorldTick}")]
    public static partial void Overwritten(ILogger logger, string saveId, string backupId, long worldTick);

    [LoggerMessage(EventId = 2252, Level = LogLevel.Warning,
        Message = "manual_save outcome=rejected operation={Operation} reason={Reason}")]
    public static partial void Rejected(ILogger logger, string operation, string reason);

    [LoggerMessage(EventId = 2255, Level = LogLevel.Information,
        Message = "autosave_settings outcome=changed enabled={Enabled} interval_minutes={IntervalMinutes} rotations={RotationCount} tick={WorldTick}")]
    public static partial void AutosaveConfigured(ILogger logger, bool enabled, int intervalMinutes,
        int rotationCount, long worldTick);
}

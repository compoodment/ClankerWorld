namespace ClankerWorld.Simulation.Playtest;

/// <summary>World-owned helper choice and model name. Credentials remain installation-owned.</summary>
public sealed record RoutineHelperSettings(string Provider, string Model, string? CredentialSlotId = null)
{
    public static RoutineHelperSettings Jev { get; } = new("jev", "jev-1.13.0");
    public static RoutineHelperSettings Off { get; } = new("off", string.Empty);

    public void Validate()
    {
        if (Provider is not ("off" or "jev" or "decisions") || Model is null ||
            Model.Length > 128 || Model != Model.Trim() || Model.Any(char.IsControl) ||
            (Provider == "off" ? Model.Length != 0 : Model.Length == 0) ||
            CredentialSlotId is not null && (Provider != "decisions" || !Guid.TryParseExact(CredentialSlotId, "N", out _)))
            throw new ArgumentException("Choose Off, Jev or OpenAI Decisions and a model name of at most 128 characters.");
    }
}

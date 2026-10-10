using System.Globalization;
using System.Text.Json.Serialization;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>A supported share of a default estate, never an override of a valid will.</summary>
public sealed record TownEstateDefaultRule([property: JsonRequired] int TownSharePercent);

public static class TownEstateDefaultRules
{
    public static string Text(TownEstateDefaultRule rule) => "Default estate: Without a valid will, leave " +
        rule.TownSharePercent.ToString(CultureInfo.InvariantCulture) +
        "% of each eligible lot to the Town; living household heirs share the rest. Only later deaths.";

    public static string RequestKey(TownEstateDefaultRule rule) =>
        "estate-default:" + rule.TownSharePercent.ToString(CultureInfo.InvariantCulture);

    public static void Validate(TownEstateDefaultRule? rule, string subject, string wording, string scope)
    {
        if (rule is not null && (rule.TownSharePercent is < 0 or > 100 || scope != TownLawRules.ResidentDuty ||
            TownLawRules.Text(subject, wording) != Text(rule)))
            throw new InvalidDataException("A default estate law needs its bounded typed share and exact prospective rule for Town residents.");
    }

    /// <summary>History fixes the rule at death, even if it is later amended or repealed.</summary>
    public static (TownLaw Law, TownLawVersion Version)? AtDeath(TownGovernmentState? government, long deathTick)
    {
        var applicable = government?.Laws.SelectMany(law => law.Versions
                .Where(version => version.EstateDefault is not null && version.AdoptedTick < deathTick &&
                    (version.EndedTick is null || version.EndedTick >= deathTick))
                .Select(version => (Law: law, Version: version)))
            .ToArray() ?? [];
        if (applicable.Length > 1) throw new InvalidDataException("A death cannot have conflicting default estate laws.");
        return applicable.Length == 0 ? null : (applicable[0].Law, applicable[0].Version!);
    }

    public static bool HasPending(TownGovernmentState government) =>
        government.LawDrafts.Any(draft => draft.Status == "pending" && draft.EstateDefault is not null);
}

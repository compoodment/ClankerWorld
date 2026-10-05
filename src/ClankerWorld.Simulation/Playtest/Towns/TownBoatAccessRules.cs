using System.Security.Cryptography;
using System.Text;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>Null names the standing grant for all visitors; otherwise one exact visitor.</summary>
public sealed record TownBoatAccessGrant(string? VisitorId);

/// <summary>Only a typed, Council-adopted grant can permit a visitor to use a Town boat.</summary>
public static class TownBoatAccessRules
{
    public static string Text(TownBoatAccessGrant grant) => "Boat access: " + (grant.VisitorId is null
        ? "Allow all visitors to use this Town's communal boats."
        : "Allow visitor " + grant.VisitorId[..Math.Min(grant.VisitorId.Length, 120)] + " to use this Town's communal boats.");

    public static string RequestKey(TownBoatAccessGrant grant) => "boat-access:" +
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(grant.VisitorId is null ? "all" : "visitor:" + grant.VisitorId)));

    public static bool Allows(TownRuntimeState town, string actor, long tick) =>
        town.Government?.Laws.Any(law => TownLawRules.InForceAt(law, tick)?.BoatAccess is { } grant &&
            (grant.VisitorId is null || grant.VisitorId == actor)) == true;

    public static void Validate(TownBoatAccessGrant? grant, string subject, string rule, string scope,
        IReadOnlySet<string> known)
    {
        if (grant is null) return;
        if (scope != TownLawRules.Jurisdiction || grant.VisitorId is { } visitor && !known.Contains(visitor) ||
            TownLawRules.Text(subject, rule) != Text(grant))
            throw new InvalidDataException("Boat access must bind a known visitor or the standing visitor grant and its exact supported rule.");
    }
}

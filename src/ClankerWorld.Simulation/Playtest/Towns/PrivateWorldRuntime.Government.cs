using System.Globalization;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    /// <summary>Advances one Town's council, then its laws, government changes and mayor's office, at the current tick.</summary>
    private (TownGovernanceState Council, TownGovernmentState Government) AdvanceCivic(TownRuntimeState town,
        TownGovernanceState council, TownGovernmentState government)
    {
        var adults = TownAdults(town);
        return TownGovernmentRules.Advance(council, government, town.Id, town.Name, worldSeed, adults, WorldTick, CivicDay,
            TownLandHearingElectionBusy(town) || TownNonviolentElectionBusy(town));
    }

    private void AddTownLawCandidates(List<CognitionCandidate> candidates, string actor, TownRuntimeState town)
    {
        const string format = "Write civic_proposal as 'subject: rule'. It needs the current council's votes, applies only from adoption " +
            "and records a social rule; it does not block actions, change ownership or create offices.";
        candidates.Add(new(CivicAction(town.Id, "propose", TownLawRules.Jurisdiction),
            $"Propose a law for everyone on {town.Name}'s claimed land, visitors included. {format}", 190));
        candidates.Add(new(CivicAction(town.Id, "propose", TownLawRules.ResidentDuty),
            $"Propose a duty for {town.Name}'s residents that applies wherever they are. {format}", 191));
        var here = inhabitants[actor].Position;
        if (TownLawRules.SiteAround(map, here, town.Id, townLandTitles).Length > 0)
            candidates.Add(new(CivicAction(town.Id, "propose", SiteToken(here)),
                $"Propose a law for the claimed land within {TownLawRules.SiteRadius} tiles of where you stand in {town.Name}, visitors included. {format}", 192));
        var history = CivicHistory(town);
        foreach (var law in town.Government?.Laws.Where(l => TownLawRules.IsInForce(l) && town.Governance!.Notices.LastOrDefault(n => n.SubjectId == l.Id) is { } latest &&
                history.Known(actor).Contains(latest.Id))
            ?? [])
        {
            var number = TownLawRules.Number(law.Id).ToString(CultureInfo.InvariantCulture);
            var current = TownLawRules.Current(law);
            candidates.Add(new(CivicAction(town.Id, "amend", law.Id, current.Version.ToString(CultureInfo.InvariantCulture)),
                $"Propose amending {town.Name}'s law {number} ({current.Subject}); write the complete new wording in civic_proposal as 'subject: rule'. " +
                "It keeps the law's scope, needs the council's votes and applies only from adoption.", 193));
            candidates.Add(new(CivicAction(town.Id, "repeal", law.Id, current.Version.ToString(CultureInfo.InvariantCulture)),
                $"Propose repealing {town.Name}'s law {number} ({current.Subject}). It needs the council's votes; earlier conduct stays under the law.", 194));
        }
    }

    private static string SiteToken(GridPoint center) =>
        "site:" + center.X.ToString(CultureInfo.InvariantCulture) + "." + center.Y.ToString(CultureInfo.InvariantCulture);

    private GridPoint[] SiteFromToken(string token, string townId)
    {
        var parts = token.Split(':', '.');
        if (parts is not ["site", var x, var y] ||
            !int.TryParse(x, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var column) ||
            !int.TryParse(y, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var row))
            throw new InvalidOperationException("The law site is not a recognized claimed-land site.");
        return TownLawRules.SiteAround(map, new GridPoint(column, row), townId, townLandTitles);
    }

    /// <summary>Applies a structured law action already checked against the actor's current legal candidates.</summary>
    private (TownGovernanceState, TownGovernmentState) ApplyTownLawAction(TownRuntimeState town, string actor, string kind,
        string target, string versionToken, string? text, TownGovernanceState council, TownGovernmentState government)
    {
        var adults = TownAdults(town);
        int? expectedVersion = null;
        if (kind is "amend" or "repeal")
        {
            if (!int.TryParse(versionToken, NumberStyles.None, CultureInfo.InvariantCulture, out var version) || version < 1)
                throw new InvalidOperationException("A law change must identify the version that was chosen.");
            expectedVersion = version;
        }
        switch (kind)
        {
            case "propose":
                if (text is null) throw new InvalidOperationException("A law proposal needs civic_proposal text.");
                var scope = target.StartsWith("site:", StringComparison.Ordinal) ? TownLawRules.Site : target;
                var site = scope == TownLawRules.Site ? SiteFromToken(target, town.Id) : [];
                return TownLawRules.ProposeAdoption(council, government, town.Id, actor, text, scope, site, adults, WorldTick, CivicDay);
            case "amend":
                if (text is null) throw new InvalidOperationException("An amendment needs civic_proposal text.");
                return TownLawRules.ProposeAmendment(council, government, town.Id, actor, target, text, adults, WorldTick, CivicDay, expectedVersion);
            case "repeal":
                return TownLawRules.ProposeRepeal(council, government, town.Id, actor, target, adults, WorldTick, CivicDay, expectedVersion);
            default:
                throw new InvalidOperationException("Unknown Town law action.");
        }
    }
}

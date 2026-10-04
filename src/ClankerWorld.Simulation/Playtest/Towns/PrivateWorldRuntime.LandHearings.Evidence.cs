using System.Globalization;
using System.Text.Json;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private static string LandHearingLawToken(TownLaw law, TownLawVersion version) =>
        law.Id + "@" + version.Version.ToString(CultureInfo.InvariantCulture);

    private (TownLaw Law, TownLawVersion Version)[] LandHearingApplicableLaws(TownRuntimeState town, TownLandCase item)
    {
        if (town.Government is not { } government) return [];
        var revision = TownLandHearingRules.CurrentRevision(item);
        var hasResidentParty = revision.Parties.SelectMany(party => party.AdultIds).Any(id => town.ResidentIds.Contains(id, StringComparer.Ordinal));
        var moments = item.Evidence.Select(evidence => evidence.ObservedTick).Append(WorldTick).Distinct();
        return moments.SelectMany(moment => revision.Tiles.SelectMany(tile => TownLawRules.Applicable(government,
                town.Id, townLandTitles, hasResidentParty, tile, moment)))
            .DistinctBy(pair => LandHearingLawToken(pair.Law, pair.Version)).OrderBy(pair => pair.Law.Id, StringComparer.Ordinal)
            .ThenBy(pair => pair.Version.Version).ToArray();
    }

    private TownLandHearingState LandHearingInspectRecords(TownRuntimeState town, TownLandHearingState hearings,
        string caseId, string actor, TownGovernanceState council)
    {
        var item = hearings.Cases.Single(c => c.Id == caseId);
        var revision = TownLandHearingRules.CurrentRevision(item);
        foreach (var (id, version, text) in LandHearingUnreadRecords(town, item))
        {
            var current = hearings.Cases.Single(c => c.Id == caseId);
            hearings = TownLandHearingRules.AddEvidence(hearings, caseId, revision.Number,
                new(LandHearingEvidenceId(current, actor, "record", id + version), revision.Number, "record", "record_inspection",
                    actor, id, version, WorldTick, actor, WorldTick, text), council.Knowledge);
        }
        return TownLandHearingRules.Inspect(hearings, caseId, revision.Number, actor, WorldTick);
    }

    /// <summary>The plot's current rights, title, applicable law versions and rulings not yet in this notice's file.</summary>
    private (string Id, string Version, string Text)[] LandHearingUnreadRecords(TownRuntimeState town, TownLandCase item)
    {
        var revision = TownLandHearingRules.CurrentRevision(item);
        var records = householdLandUseRights.Where(right => right.TownId == town.Id && right.Tiles.Any(revision.Tiles.Contains))
            .Select(right => (right.Id, TownLandHearingRules.Version(right),
                FormattableString.Invariant($"Recorded household {right.HouseholdId} permission for {right.Tiles.Count} tiles; grant {right.GrantSource}; granted {right.GrantedTick}; agreed end {right.AgreedEndTick?.ToString(CultureInfo.InvariantCulture) ?? "none"}.")))
            .Concat(townLandTitles.Where(title => title.TownId == town.Id && title.Tiles.Any(revision.Tiles.Contains))
                .Select(title => (title.Id, TownLandHearingRules.Digest(JsonSerializer.Serialize(title)),
                    FormattableString.Invariant($"Town {title.TownId} holds formal title to {title.Tiles.Count} recorded tiles from {title.RecordedTick}."))))
            .Concat(LandHearingApplicableLaws(town, item).Select(pair => (LandHearingLawToken(pair.Law, pair.Version),
                TownLandHearingRules.LawVersion(pair.Version), TownLawRules.Text(pair.Version.Subject, pair.Version.Rule))))
            .Concat(item.Rulings.Select(ruling => (ruling.Id, TownLandHearingRules.Digest(JsonSerializer.Serialize(ruling)),
                FormattableString.Invariant($"Recorded ruling {ruling.Id}, hearing {ruling.Revision}, made at {ruling.Tick}: {ruling.Outcome.Kind}."))));
        return records.Where(record => !item.Evidence.Any(evidence => evidence.Revision == revision.Number && evidence.Kind == "record" &&
            evidence.SourceRecordId == record.Item1 && evidence.SourceVersion == record.Item2)).ToArray();
    }

    private static string LandHearingEvidenceId(TownLandCase item, string actor, string kind, string source) =>
        "land-evidence:" + TownLandHearingRules.Digest(item.Id + "|" + TownLandHearingRules.CurrentRevision(item).Number +
            "|" + item.Evidence.Count + "|" + actor + "|" + kind + "|" + source)[..32];

    private TownLandHearingState LandHearingObserve(TownLandHearingState hearings, TownLandCase item,
        string actor, TownGovernanceState council)
    {
        var revision = TownLandHearingRules.CurrentRevision(item);
        var visible = revision.Tiles.Where(tile => IsWithinInteractionRange(inhabitants[actor].Position, tile, ResourceInteractionRange)).ToHashSet();
        if (visible.Count == 0) throw new InvalidOperationException("A plot observation requires actual physical proximity.");
        var buildings = worldSimulation.Buildings.Where(building => visible.Contains(building.Position)).OrderBy(building => building.InstanceId, StringComparer.Ordinal)
            .Select(building => $"{building.DefinitionId} at ({building.Position.X}, {building.Position.Y})");
        var crops = fields.Where(field => visible.Contains(field.Position)).OrderBy(field => field.Position.Y).ThenBy(field => field.Position.X)
            .Select(field => $"field at ({field.Position.X}, {field.Position.Y}), {field.Stage}, crop {field.Crop ?? "none"}");
        var description = "Personally observed nearby plot: " + string.Join("; ", buildings.Concat(crops));
        if (description == "Personally observed nearby plot: ") description += "no building origin or cultivated field on the inspected tiles.";
        if (description.Length > TownLandHearingRules.MaximumTextLength) description = description[..TownLandHearingRules.MaximumTextLength];
        if (item.Evidence.Any(evidence => evidence.Revision == revision.Number && evidence.Kind == "observation" &&
                evidence.SourceAgentId == actor && evidence.Text == description)) return hearings;
        return TownLandHearingRules.AddEvidence(hearings, item.Id, revision.Number,
            new(LandHearingEvidenceId(item, actor, "observation", description), revision.Number, "observation", "firsthand",
                actor, null, null, WorldTick, actor, WorldTick, description), council.Knowledge);
    }

}

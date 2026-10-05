using ClankerWorld.Simulation.Harness;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    /// <summary>Only exact public records this actor actually learned, relevant to the alleged plot.</summary>
    private static TownCaseEvidence[] NonviolentKnownPublicRecords(TownRuntimeState town,
        IReadOnlyList<HouseholdLandUseRight> currentRights, IReadOnlyList<TownLandTitleRecord> titles,
        string actor, GridPoint position, int revision, long tick)
    {
        var hearings = town.LandHearings;
        var rights = hearings.OriginalRights.Select(snapshot => snapshot.Right)
            .Concat(hearings.Adjustments.SelectMany(adjustment => adjustment.ResultRights)).Concat(currentRights)
            .Concat(hearings.Cases.SelectMany(item => item.Revisions.SelectMany(notice => notice.RightVersions).Select(snapshot => snapshot.Right)))
            .Where(right => right.TownId == town.Id && right.Tiles.Contains(position)).ToArray();
        var records = new List<TownCaseEvidence>();
        foreach (var item in hearings.Cases)
            foreach (var read in item.Reads.Where(read => read.AgentId == actor && read.ReadTick <= tick).OrderBy(read => read.ReadTick))
                foreach (var evidence in item.Evidence.Where(evidence => evidence.Kind == "record" &&
                    evidence.Acquisition == "record_inspection" && evidence.SubmittedTick <= read.ReadTick && read.EvidenceIds.Contains(evidence.Id)))
                {
                    string? text = null;
                    var right = rights.FirstOrDefault(right => right.Id == evidence.SourceRecordId &&
                        TownLandHearingRules.Version(right) == evidence.SourceVersion && right.GrantedTick <= evidence.ObservedTick);
                    if (right is not null) text = LandHearingRightText(right);
                    var title = titles.FirstOrDefault(title => title.TownId == town.Id && title.Tiles.Contains(position) &&
                        title.Id == evidence.SourceRecordId && TownLandHearingRules.RecordVersion(title) == evidence.SourceVersion &&
                        title.RecordedTick <= evidence.ObservedTick);
                    if (title is not null) text = LandHearingTitleText(title);
                    var ruling = hearings.Cases.SelectMany(file => file.Rulings.Where(ruling =>
                        file.Revisions.Any(notice => notice.Number == ruling.Revision && notice.Tiles.Contains(position))))
                        .FirstOrDefault(ruling => ruling.Id == evidence.SourceRecordId &&
                            TownLandHearingRules.RecordVersion(ruling) == evidence.SourceVersion && ruling.Tick <= evidence.ObservedTick);
                    if (ruling is not null) text = LandHearingRulingText(ruling);
                    // The land ledger also validates the original inspection/read chain. Matching its
                    // canonical wording prevents a submitted account from becoming a verified record.
                    if (text is null || evidence.Text != text || evidence.ObservedTick > read.ReadTick) continue;
                    records.Add(new("public-record:" + NonviolentToken(actor + "|" + evidence.SourceRecordId + "|" + evidence.SourceVersion + "|" + read.ReadTick),
                        revision, "record", "record_inspection", actor, evidence.SourceRecordId, evidence.SourceVersion,
                        read.ReadTick, actor, tick, text));
                }
        return records.OrderBy(record => record.ObservedTick).ThenBy(record => record.Id, StringComparer.Ordinal)
            .DistinctBy(record => (record.SourceRecordId, record.SourceVersion)).ToArray();
    }
}

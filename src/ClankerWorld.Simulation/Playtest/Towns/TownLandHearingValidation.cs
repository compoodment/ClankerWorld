using System.Diagnostics.CodeAnalysis;
using ClankerWorld.Simulation.Harness;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>Validates durable case provenance, windows, mandates and the exact permission adjustment chain.</summary>
public static class TownLandHearingValidation
{
    public static void Validate(SeededMap map, long tick, string townId, TownLandHearingState state,
        IReadOnlyList<HouseholdLandUseRight> currentRights, IReadOnlyList<TownLandTitleRecord> titles,
        IReadOnlySet<string> knownAgents, IReadOnlySet<string> knownHouseholds,
        TownGovernanceState? council, int day, TownGovernmentState? government = null,
        IReadOnlyList<string>? adultResidents = null, IReadOnlyDictionary<string, string?>? agentHouseholds = null,
        IReadOnlyDictionary<string, IReadOnlyList<TownLandCaseParty>>? currentPartiesByCase = null,
        Func<TownLandCase, IReadOnlyList<TownLandCaseParty>>? currentPartiesResolver = null)
    {
        Check(state is not null && state.Sequence >= 0 && day > 0 && state.Cases is not null && state.OriginalRights is not null && state.Adjustments is not null,
            "Saved land hearings must contain their case file and permission history.");
        Check(state.Cases.All(c => c is not null) && state.OriginalRights.All(r => r is not null) && state.Adjustments.All(a => a is not null),
            "Saved land hearings cannot contain null records.");
        // Evidence may point into another case's historical rights. Check every file's
        // shape before those lookups, while leaving semantic validation to one pass.
        foreach (var item in state.Cases) ValidateCaseFile(tick, townId, item, state.Sequence);
        TownPropertyValidation.ValidateFiles(state, townId, tick, knownAgents, knownHouseholds, council, government);
        Check(!state.Cases.Where(c => c.Status == "pending").SelectMany(c => TownLandHearingRules.CurrentRevision(c).Tiles)
            .GroupBy(tile => tile).Any(group => group.Count() > 1), "Pending land hearings cannot overlap their plots.");
        Unique(state.Cases.Select(c => c.Id));
        Unique(state.Cases.SelectMany(c => c.ReopenRequests).Select(r => r.Id));
        Unique(state.OriginalRights.Select(r => r.Id));
        Unique(state.Adjustments.Select(a => a.Id));
        TownLandTransferValidation.Validate(map, tick, townId, state, currentRights, titles, knownAgents, knownHouseholds, council);
        Check(state.Cases.Count + state.Adjustments.Count + state.Transfers.Count <= state.Sequence, "Saved hearing numbering cannot precede its records.");
        foreach (var original in state.OriginalRights) ValidateRight(map, tick, townId, original, titles, knownHouseholds);
        foreach (var item in state.Cases) ValidateCase(map, tick, townId, item, titles, knownAgents, knownHouseholds, council, day, government, state, currentRights);
        if (adultResidents is not null && agentHouseholds is not null)
            foreach (var item in state.Cases.Where(c => c.Judge is not null))
                Check(adultResidents.Contains(item.Judge!.AgentId, StringComparer.Ordinal) &&
                    !TownLandHearingRules.JudgeConflict(item.Judge.AgentId, agentHouseholds.GetValueOrDefault(item.Judge.AgentId),
                        currentPartiesResolver?.Invoke(item) ?? currentPartiesByCase?.GetValueOrDefault(item.Id) ??
                            TownLandHearingRules.CurrentRevision(item).Parties,
                        item.DirectStakeIds.ToHashSet(StringComparer.Ordinal)),
                    "A live saved case judge must remain an eligible independent adult resident.");
        foreach (var adjustment in state.Adjustments)
        {
            Check(Generated(adjustment.Id, "land-adjustment:", townId, state.Sequence) && adjustment.Tick >= 0 && adjustment.Tick <= tick && adjustment.PriorRights is not null && adjustment.ResultRights is not null &&
                adjustment.Tiles is not null && adjustment.PriorRights.All(r => r is not null) && adjustment.ResultRights.All(r => r is not null),
                "A saved land adjustment is incomplete.");
            Unique(adjustment.PriorRights.Select(r => r.Id));
            Unique(adjustment.ResultRights.Select(r => r.Id));
            Check(adjustment.Tiles.Count > 0 && adjustment.Tiles.SequenceEqual(TownLandRightsRules.OrderTiles(adjustment.Tiles.Distinct())) &&
                adjustment.Tiles.All(t => map.IsLand(t) && TownLandRightsRules.IsCoveredByTownTitle(t, townId, titles)), "A land adjustment must be bounded to titled land.");
            foreach (var right in adjustment.PriorRights) ValidateRight(map, adjustment.Tick, townId, right, titles, knownHouseholds);
            foreach (var right in adjustment.ResultRights) ValidateRight(map, adjustment.Tick, townId, TownLandHearingRules.Snapshot(right), titles, knownHouseholds);
            Check(adjustment.ResultRights.SelectMany(r => r.Tiles).Distinct().Count() == adjustment.ResultRights.Sum(r => r.Tiles.Count),
                "A land adjustment cannot overlap its result permissions.");
            if (adjustment.Kind == "ruling")
            {
                var item = state.Cases.SingleOrDefault(c => c.Id == adjustment.CaseId);
                var ruling = item?.Rulings.SingleOrDefault(r => r.Id == adjustment.RulingId);
                Check(item is not null && ruling is not null && ruling.Tick == adjustment.Tick && ruling.AdjustmentIds.Contains(adjustment.Id, StringComparer.Ordinal) &&
                    adjustment.BuildingId is null && adjustment.TargetHouseholdId is null && adjustment.TransferId is null, "A ruling adjustment needs its exact durable case and ruling.");
                var revision = item.Revisions.Single(r => r.Number == ruling.Revision);
                var prior = adjustment.PriorRights.Select(r => r.Right).ToArray();
                // Free tiles a ruling granted are replayed as recorded; the request receipts prove each one was a heard request.
                var granted = adjustment.ResultRights.SelectMany(r => r.Tiles).Where(t => !prior.Any(p => p.Tiles.Contains(t))).ToArray();
                Check(adjustment.Tiles.SequenceEqual(revision.Tiles) &&
                    (ruling.Outcome.Kind is "reclaim" or "grant" ? item.Property is not null &&
                        TownLandHearingRules.SameRights(adjustment.ResultRights, TownPropertyRules.BoundedRights(map,
                            prior, adjustment.Tiles, item.Property.Request, ruling.Id, ruling.Tick, townId)) :
                        ruling.Outcome.Kind is "renew" or "amend" or "end" &&
                        (ruling.Outcome.Kind == "amend" || prior.Any(r => r.HouseholdId == ruling.Outcome.HouseholdId && r.Tiles.Any(adjustment.Tiles.Contains))) &&
                        TownLandHearingRules.SameRights(adjustment.ResultRights, TownLandHearingRules.BoundedOutcome(map,
                            prior, adjustment.Tiles, ruling.Outcome, ruling.Id, ruling.Tick, townId, granted))),
                    "A saved ruling must reproduce exactly its bounded permission change.");
            }
            else if (adjustment.Kind == "building_transfer")
            {
                Check(adjustment.CaseId is null && adjustment.RulingId is null && adjustment.TransferId is null && Id(adjustment.BuildingId) &&
                    adjustment.TargetHouseholdId is { } target && knownHouseholds.Contains(target), "A building transfer needs its captured footprint and known beneficiary.");
                var prior = adjustment.PriorRights.Select(r => r.Right).ToArray();
                var expected = TownLandRightsRules.ReassignFootprintRights(map, prior, adjustment.Tiles.ToHashSet(), adjustment.TargetHouseholdId!, adjustment.Tick);
                // The actual global reassignment may reserve an ID already used by an untouched piece.
                // Per-tile terms prove its effect; the replay chain separately checks all exact identities.
                Check(SameTileTerms(expected, adjustment.ResultRights), "A saved building adjustment may only reassign existing footprint permissions and retain grant terms.");
            }
            else if (adjustment.Kind == "voluntary_transfer") TownLandTransferValidation.ValidateAdjustment(map, state, adjustment);
            else throw new InvalidDataException("A saved land adjustment has an unsupported source.");
        }
        Check(state.Adjustments.Select(a => a.Tick).SequenceEqual(state.Adjustments.Select(a => a.Tick).Order()), "Land adjustments must retain chronological order.");
        var originals = TownLandHearingRules.OriginalGrantRights(state, currentRights);
        Check(TownLandHearingRules.SameRights(TownLandHearingRules.ApplyAdjustments(state, originals), currentRights),
            "Saved live rights must match their original permissions and every recorded adjustment.");
        Check(state.OriginalRights.All(r => state.Adjustments.Any(a => a.PriorRights.Any(p => p.Id == r.Id && p.Version == r.Version))),
            "An original permission snapshot cannot be orphaned from its adjustment chain.");
    }

    private static void ValidateCaseFile(long tick, string townId, TownLandCase item, long sequence)
    {
        Check(Generated(item.Id, "land-case:", townId, sequence) && item.TownId == townId && item.Kind is "dispute" or "expiry" or "property" && item.Status is "pending" or "settled" &&
            item.FiledTick >= 0 && item.FiledTick <= tick && item.Revisions is { Count: > 0 } && item.Filings is { Count: > 0 } &&
            item.Evidence is not null && item.Responses is not null && item.Reads is not null && item.Rulings is not null &&
            item.JudgeHistory is not null && item.JudgeConsents is not null && item.ContestHistory is not null && item.ReopenRequests is not null && item.DirectStakeIds is not null,
            "A saved land case must retain its complete file.");
        Check(item.Revisions.All(r => r is not null) && item.Filings.All(r => r is not null) && item.Evidence.All(r => r is not null) &&
            item.Responses.All(r => r is not null) && item.Reads.All(r => r is not null) && item.Rulings.All(r => r is not null && r.Judge is not null) &&
            item.JudgeHistory.All(r => r is not null && r.Judge is not null) && item.JudgeConsents.All(r => r is not null) && item.ContestHistory.All(r => r is not null) &&
            item.ReopenRequests.All(r => r is not null), "A saved land case cannot contain null file entries.");
        foreach (var revision in item.Revisions)
            Check(revision.Tiles is { Count: > 0 } && revision.Parties is { Count: > 0 } && revision.Parties.All(p => p is not null) &&
                revision.RightVersions is not null && revision.RightVersions.All(r => r is not null && r.Right is not null),
                "A saved land revision cannot omit its plot, parties or prior rights.");
    }

    private static void ValidateCase(SeededMap map, long tick, string townId, TownLandCase item,
        IReadOnlyList<TownLandTitleRecord> titles, IReadOnlySet<string> agents, IReadOnlySet<string> households,
        TownGovernanceState? council, int day, TownGovernmentState? government, TownLandHearingState state,
        IReadOnlyList<HouseholdLandUseRight> currentRights)
    {
        Check(item.Revisions.Select(r => r.Number).SequenceEqual(Enumerable.Range(1, item.Revisions.Count)) &&
            item.Revisions.Select(r => r.PublishedTick).SequenceEqual(item.Revisions.Select(r => r.PublishedTick).Order()) &&
            item.Key == TownLandHearingRules.CaseKey(townId, item.Kind, item.Revisions[0].Tiles), "Saved land notice revisions and case identity must be canonical.");
        foreach (var revision in item.Revisions)
        {
            Check(TownLandRightsRules.IsValidPlot(map, revision.Tiles, tick, revision.PublishedTick) && revision.PublishedTick >= item.FiledTick &&
                revision.DeadlineTick == checked(revision.PublishedTick + day) && revision.Parties is { Count: > 0 } && revision.RightVersions is not null &&
                TownLandHearingRules.IsValidOutcome(revision.RequestedOutcome, item.FiledTick) &&
                revision.Tiles.All(t => TownLandRightsRules.IsCoveredByTownTitle(t, townId, titles)), "A saved land notice needs its complete titled plot and full response day.");
            Check(council?.Notices.Any(n => n.Id == revision.NoticeId && n.Kind == "land_hearing" && n.SubjectId == item.Id + ":" + revision.Number.ToString(System.Globalization.CultureInfo.InvariantCulture) && n.PostedTick == revision.PublishedTick) == true,
                "A saved land case needs its actual formal notice publication.");
            Check(revision.Parties.All(p => p is not null) && revision.RightVersions.All(r => r is not null), "A saved notice cannot contain null parties or rights.");
            Unique(revision.Parties.Select(p => p.Id));
            Check(revision.Parties.Select(p => p.Id).SequenceEqual(revision.Parties.Select(p => p.Id).Order(StringComparer.Ordinal)), "Case parties must be in canonical order.");
            foreach (var party in revision.Parties)
                Check(party is not null && Id(party.Id) && party.AdultIds is not null && Canonical(party.AdultIds) && party.AdultIds.All(agents.Contains) &&
                    (party.Kind == "household" && party.HouseholdId is { } household && households.Contains(household) && (party.TownId is null || party.TownId == townId) && party.RepresentativeId is null ||
                        party.Kind == "town" && party.TownId == townId && party.HouseholdId is null && party.AdultIds.Count == 0 && (party.RepresentativeId is null || agents.Contains(party.RepresentativeId))),
                    "A saved case party must identify its actual household adults or recorded Town representative.");
            Unique(revision.RightVersions.Select(r => r.Id));
            foreach (var right in revision.RightVersions)
            {
                ValidateRight(map, revision.PublishedTick, townId, right, titles, households);
                Check(right.Right.Tiles.Any(revision.Tiles.Contains), "An inspected prior right must concern the noticed plot.");
            }
        }
        Check(Canonical(item.DirectStakeIds) && item.DirectStakeIds.All(agents.Contains), "Personal stakes must name known agents.");
        foreach (var filing in item.Filings)
            Check(filing.Kind is "dispute" or "expiry" or "town" && TownLandHearingRules.ValidText(filing.Text) && filing.Tick >= item.FiledTick && filing.Tick <= tick &&
                TownLandHearingRules.IsValidOutcome(filing.RequestedOutcome, filing.Tick) &&
                (filing.Kind == "expiry" ? filing.AgentId is null : filing.AgentId is { } filer && agents.Contains(filer)), "A saved filing must retain an eligible source and bounded request.");
        Unique(item.Evidence.Select(e => e.Id));
        foreach (var evidence in item.Evidence)
        {
            Check(TownLandHearingRules.ValidEvidence(evidence) && agents.Contains(evidence.SourceAgentId) && agents.Contains(evidence.SubmittedByAgentId) &&
                evidence.SubmittedTick <= tick && item.Revisions.Any(r => r.Number == evidence.Revision && r.PublishedTick <= evidence.SubmittedTick &&
                    TownLandHearingRules.HasNoticeReceipt(r, evidence.SubmittedByAgentId, evidence.SubmittedTick, council!.Knowledge)), "Saved evidence requires a real informed submission with source and time.");
            if (evidence.Kind == "record")
            {
                var rights = state.OriginalRights.Select(r => r.Right).Concat(state.Adjustments.SelectMany(a => a.ResultRights)).Concat(currentRights)
                    .Concat(state.Cases.SelectMany(c => c.Revisions.SelectMany(r => r.RightVersions).Select(r => r.Right)));
                Check(rights.Any(r => r.Id == evidence.SourceRecordId && TownLandHearingRules.Version(r) == evidence.SourceVersion) ||
                    titles.Any(t => t.Id == evidence.SourceRecordId && TownLandHearingRules.RecordVersion(t) == evidence.SourceVersion) ||
                    government?.Laws.Any(l => l.Versions.Any(v => l.Id + "@" + v.Version.ToString(System.Globalization.CultureInfo.InvariantCulture) == evidence.SourceRecordId &&
                        TownLandHearingRules.LawVersion(v) == evidence.SourceVersion && v.AdoptedTick <= evidence.ObservedTick)) == true ||
                    state.Cases.SelectMany(c => c.Rulings).Any(r => r.Id == evidence.SourceRecordId && TownLandHearingRules.RecordVersion(r) == evidence.SourceVersion && r.Tick <= evidence.ObservedTick) ||
                    state.Cases.Any(c => c.Property?.Snapshots.Any(snapshot => TownPropertyRules.RecordId(c, snapshot.Revision) == evidence.SourceRecordId &&
                        TownLandHearingRules.RecordVersion(snapshot) == evidence.SourceVersion && snapshot.Tick <= evidence.ObservedTick) == true),
                    "Verified evidence must match an actually recorded right, title or law version.");
            }
        }
        foreach (var response in item.Responses)
        {
            var revision = item.Revisions.SingleOrDefault(r => r.Number == response.Revision);
            var party = revision?.Parties.SingleOrDefault(p => p.Id == response.PartyId);
            Check(revision is not null && party is not null && agents.Contains(response.AgentId) && response.Kind is "answer" or "waive" &&
                TownLandHearingRules.ValidText(response.Text) && response.Tick <= tick &&
                (party.Kind == "household" ? party.AdultIds.Contains(response.AgentId, StringComparer.Ordinal) : party.RepresentativeId == response.AgentId ||
                    item.Filings.Any(f => f.Kind == "town" && f.AgentId == response.AgentId && f.Tick <= response.Tick) ||
                    government?.Offices.Any(o => o.Mandates == "ordinary" && o.HolderId == response.AgentId && o.TermStartTick <= response.Tick && o.TermEndTick > response.Tick) == true ||
                    government?.OfficeHistory.Any(o => o.Mandates == "ordinary" && o.HolderId == response.AgentId && o.StartTick <= response.Tick && o.EndTick >= response.Tick) == true) &&
                TownLandHearingRules.HasNoticeReceipt(revision, response.AgentId, response.Tick, council!.Knowledge), "A saved response must be the noticed party's own informed answer.");
        }
        Unique(item.Responses.Select(r => r.Revision + ":" + r.PartyId + ":" + r.AgentId));
        for (var readIndex = 0; readIndex < item.Reads.Count; readIndex++)
        {
            var read = item.Reads[readIndex];
            var earlierReads = item.Reads.Take(readIndex).ToArray();
            var revision = item.Revisions.SingleOrDefault(r => r.Number == read.Revision);
            Check(revision is not null && agents.Contains(read.AgentId) && read.ReadTick >= revision.PublishedTick && read.ReadTick <= tick &&
                read.EvidenceIds is not null && Canonical(read.EvidenceIds) && read.EvidenceIds.All(id => item.Evidence.Any(e => e.Id == id && e.SubmittedTick <= read.ReadTick)) &&
                Canonical(read.ReopenRequestIds) && read.ReopenRequestIds.All(id => item.ReopenRequests.Any(r => r.Id == id && r.Tick <= read.ReadTick)),
                "A saved file read cannot include unknown or future evidence.");
            if (read.SourceAgentId is { } source)
                Check(source != read.AgentId && agents.Contains(source) && earlierReads.Any(r => r.Revision == read.Revision && r.AgentId == source && r.ReadTick <= read.ReadTick) &&
                    read.EvidenceIds.All(id => earlierReads.Any(r => r.Revision == read.Revision && (r.AgentId == source || r.AgentId == read.AgentId) && r.ReadTick <= read.ReadTick && r.EvidenceIds.Contains(id, StringComparer.Ordinal))) &&
                    read.ReopenRequestIds.All(id => earlierReads.Any(r => r.Revision == read.Revision && (r.AgentId == source || r.AgentId == read.AgentId) && r.ReadTick <= read.ReadTick && r.ReopenRequestIds.Contains(id, StringComparer.Ordinal))),
                    "A saved case relay cannot teach evidence its source has not read.");
        }
        Check(item.Reads.Select(r => r.ReadTick).SequenceEqual(item.Reads.Select(r => r.ReadTick).Order()), "Case reads must preserve actual chronological provenance.");
        foreach (var consent in item.JudgeConsents)
            Check(agents.Contains(consent.AgentId) && consent.Tick >= item.FiledTick && consent.Tick <= tick &&
                (consent.WithdrawnTick is null || consent.WithdrawnTick >= consent.Tick && consent.WithdrawnTick <= tick), "Saved case candidacy must retain personal consent for this case.");
        Unique(item.JudgeConsents.Where(c => c.WithdrawnTick is null).Select(c => c.AgentId));
        foreach (var contest in item.ContestHistory) ValidateContest(contest, item, tick, day, agents);
        if (item.Contest is { } live) ValidateContest(live, item, tick, day, agents);
        Check(item.Contest is null || item.Judge is null && (item.Status == "pending" || item.ReopenRequests.Any(r => r.Status == "pending")),
            "A case election cannot run beside an assigned judge or on a closed case.");
        if (item.Judge is { } judge) ValidateJudge(judge, item, tick, agents, government);
        foreach (var term in item.JudgeHistory)
        {
            Check(term.EndedTick >= term.Judge.AssignedTick && term.EndedTick <= tick && TownLandHearingRules.ValidText(term.Reason), "A saved case assignment needs its ending history.");
            ValidateJudge(term.Judge, item, tick, agents, government);
        }
        Unique(item.Rulings.Select(r => r.Id));
        foreach (var ruling in item.Rulings)
        {
            var revision = item.Revisions.SingleOrDefault(r => r.Number == ruling.Revision);
            Check(Generated(ruling.Id, "land-ruling:", townId, state.Sequence) && revision is not null && ruling.Tick >= revision.PublishedTick && ruling.Tick <= tick &&
                TownLandHearingRules.IsValidOutcome(ruling.Outcome, ruling.Tick) && TownLandHearingRules.ValidText(ruling.Reasons) &&
                Canonical(ruling.EvidenceIds) && Canonical(ruling.LawIds) && ruling.AdjustmentIds is not null && ruling.Parties is { Count: > 0 } && ruling.Parties.All(p => p is not null &&
                    p.AdultIds is not null && Canonical(p.AdultIds) && p.AdultIds.All(agents.Contains)) &&
                !TownLandHearingRules.RequiresNewNotice(revision, revision.Tiles, revision.RightVersions.Select(r => r.Right), ruling.Parties) &&
                ruling.EvidenceIds.All(id => item.Evidence.Any(e => e.Id == id && e.SubmittedTick <= ruling.Tick)) &&
                ruling.LawIds.All(id => item.Evidence.Any(e => e.Kind == "record" && e.SourceRecordId == id && e.SubmittedTick <= ruling.Tick) &&
                    government?.Laws.Any(l => l.Versions.Any(v => l.Id + "@" + v.Version.ToString(System.Globalization.CultureInfo.InvariantCulture) == id)) == true) &&
                item.Reads.Any(r => r.AgentId == ruling.Judge.AgentId && r.Revision == ruling.Revision && r.ReadTick <= ruling.Tick &&
                    ruling.EvidenceIds.All(r.EvidenceIds.Contains)) &&
                !TownLandHearingRules.JudgeConflict(ruling.Judge.AgentId, null, ruling.Parties), "A saved ruling requires a reasoned, read and independent adjudication.");
            ValidateJudge(ruling.Judge, item, tick, agents, government);
            Check(AuthorityAt(ruling.Judge, item, ruling.Tick, government), "A saved ruling needs a mandate valid at the actual ruling time.");
            Check(ruling.Tick >= revision.DeadlineTick || ruling.Parties.All(p => p.Kind == "household" ? p.AdultIds.Count > 0 &&
                p.AdultIds.All(id => item.Responses.Any(r => r.Revision == revision.Number && r.PartyId == p.Id && r.AgentId == id && r.Tick <= ruling.Tick)) :
                item.Responses.Any(r => r.Revision == revision.Number && r.PartyId == p.Id && r.AgentId == p.RepresentativeId && r.Tick <= ruling.Tick)), "A saved ruling cannot omit the response window or actual early answers.");
            if (ruling.Outcome.Kind is "renew" or "amend" or "end" or "reclaim" or "grant")
                Check(ruling.AdjustmentIds.Count == 1 && ruling.EvidenceIds.Any(id => item.Evidence.Single(e => e.Id == id).Kind != "allegation") &&
                    (item.Property is not null || !ruling.Parties.Any(p => p.Kind == "household" && p.AdultIds.Count == 0 && state.Adjustments.Where(a => a.RulingId == ruling.Id)
                        .Any(a => TownLandHearingRules.IsAdverseChange(a.PriorRights.Select(v => v.Right), a.ResultRights, p.HouseholdId!)))) &&
                    ruling.AdjustmentIds.All(id => state.Adjustments.Any(a => a.Id == id && a.RulingId == ruling.Id && a.CaseId == item.Id)),
                    "An adverse saved ruling needs supported evidence, represented households and its exact adjustment.");
            else Check(ruling.AdjustmentIds.Count == 0, "Confirmation or rejection cannot secretly change use rights.");
        }
        Check(item.Status == "pending" ? item.SettledTick is null : item.Rulings.Count > 0 && item.SettledTick == item.Rulings[^1].Tick &&
            (item.Judge is null || item.ReopenRequests.Any(r => r.Status == "pending")), "A saved settled case must retain its ruling and closure time.");
        foreach (var request in item.ReopenRequests)
        {
            Check(Generated(request.Id, "land-reopen:", townId, state.Sequence) && agents.Contains(request.AgentId) && request.Tick >= item.FiledTick && request.Tick <= tick &&
                request.Kind is "material_evidence" or "procedural_error" && request.Status is "pending" or "accepted" or "rejected" &&
                Canonical(request.EvidenceIds) && request.EvidenceIds.Count > 0 && request.EvidenceIds.All(id => item.Evidence.Any(e => e.Id == id && e.SubmittedTick <= request.Tick)) &&
                TownLandHearingRules.ValidText(request.Reasons), "A saved reopening must retain identified grounds and evidence.");
            Check(request.Status == "pending" ? request.AssessedBy is null && request.AssessedTick is null && request.Assessment is null :
                request.AssessedBy is not null && request.AssessedTick >= request.Tick && request.AssessedTick <= tick && TownLandHearingRules.ValidText(request.Assessment),
                "A saved reopening assessment requires recorded independent authority and reasons.");
            if (request.AssessedBy is { } assessor)
            {
                ValidateJudge(assessor, item, tick, agents, government);
                Check(AuthorityAt(assessor, item, request.AssessedTick!.Value, government), "A reopening assessment needs authority at its actual decision time.");
                Check(item.Reads.Any(r => r.AgentId == assessor.AgentId && r.ReadTick <= request.AssessedTick &&
                    r.ReopenRequestIds.Contains(request.Id, StringComparer.Ordinal)), "A reopening assessment cannot inherit unread grounds.");
            }
            if (request.Status == "accepted")
                Check(item.Revisions.Any(r => r.PublishedTick == request.AssessedTick) &&
                    (request.Kind == "material_evidence" ? TownLandHearingRules.MaterialNewEvidence(item, request, state) : TownLandHearingRules.DemonstratedProceduralError(item, request)),
                    "An accepted reopening needs established grounds and a fresh full hearing notice.");
        }
    }

    /// <summary>The null checks that let another validator read a ledger before its own full validation has run.</summary>
    internal static bool HasRecords(TownLandHearingState? state) =>
        state is { Cases: not null, OriginalRights: not null, Adjustments: not null } &&
        state.OriginalRights.All(v => v?.Right?.Tiles is not null) &&
        state.Adjustments.All(a => a is { PriorRights: not null, ResultRights: not null } &&
            a.PriorRights.All(v => v?.Right?.Tiles is not null) && a.ResultRights.All(r => r?.Tiles is not null)) &&
        state.Cases.All(c => c is { Rulings: not null, Revisions: not null } && c.Rulings.All(r => r is not null) && c.Revisions.All(r => r?.Tiles is not null));

    public static void ValidateRequestResolutions(long tick, IReadOnlyList<TownLandHearingState> states,
        IReadOnlyList<HouseholdLandUseRequest> requests)
    {
        // This runs before each ledger's own validation, so a duplicated or missing record is refused here, not assumed away.
        Check(states.All(HasRecords), "Saved land hearings must contain their case file and permission history.");
        foreach (var request in requests)
        {
            Check(request.HearingResolutions is not null && request.HearingResolutions.All(r => r is not null), "A land request cannot contain null hearing receipts.");
            var resolved = new HashSet<GridPoint>();
            var identities = new HashSet<string>(StringComparer.Ordinal);
            foreach (var receipt in request.HearingResolutions)
            {
                var cases = states.SelectMany(s => s.Cases).Where(c => c.Id == receipt.CaseId && c.TownId == request.TownId).ToArray();
                var item = cases.Length == 1 ? cases[0] : null;
                var rulings = item?.Rulings.Where(r => r.Id == receipt.RulingId).ToArray();
                var ruling = rulings is { Length: 1 } ? rulings[0] : null;
                var revisions = ruling is null ? null : item!.Revisions.Where(r => r.Number == ruling.Revision).ToArray();
                Check(item is not null && ruling is not null && revisions is { Length: 1 } && receipt.RequestId == request.Id && receipt.TownId == request.TownId &&
                    receipt.Tick == ruling.Tick && receipt.Tick >= request.RequestedTick && receipt.Tick <= tick && identities.Add(receipt.RulingId) &&
                    receipt.Tiles is { Count: > 0 } && receipt.Tiles.SequenceEqual(TownLandRightsRules.OrderTiles(receipt.Tiles.Distinct())),
                    "A hearing receipt must retain its exact household request, case, ruling and settlement time.");
                var plot = revisions[0].Tiles;
                Check(receipt.Tiles.SequenceEqual(TownLandRightsRules.OrderTiles(request.Tiles.Where(t => plot.Contains(t) && !resolved.Contains(t)))),
                    "A hearing can resolve only the still-pending request tiles in its exact noticed plot.");
                foreach (var tile in receipt.Tiles) resolved.Add(tile);
            }
            Check(request.HearingResolutions.Select(r => r.Tick).SequenceEqual(request.HearingResolutions.Select(r => r.Tick).Order()), "Hearing request receipts must retain chronological order.");
            if (request.Status == "hearing_resolved")
                Check(resolved.Count == request.Tiles.Count && request.HearingResolutions.Count > 0 && request.SettledTick == request.HearingResolutions[^1].Tick && request.GrantAdults.Count == 0,
                    "Only complete hearing resolution may close a request, without inventing household grant consent.");
            else if (request.Status == "pending")
                Check(resolved.Count < request.Tiles.Count, "A completely heard request cannot remain a live competing claim.");
        }
        // A ruling may give out free Town land only where it heard a household's pending request for that tile.
        foreach (var adjustment in states.SelectMany(s => s.Adjustments).Where(a => a.Kind == "ruling"))
            Check(adjustment.ResultRights.SelectMany(r => r.Tiles).Where(t => !adjustment.PriorRights.Any(p => p.Right.Tiles.Contains(t)))
                .All(t => requests.Any(r => r.HearingResolutions.Any(receipt => receipt.RulingId == adjustment.RulingId && receipt.Tiles.Contains(t)))),
                "A ruling cannot grant free Town land without a heard household request for it.");
    }

    private static bool AuthorityAt(TownLandCaseJudge judge, TownLandCase item, long tick, TownGovernmentState? government) =>
        judge.Kind == "land_mayor" ? government?.Offices.Any(o => o.Mandates == "land" && o.HolderId == judge.AgentId && o.ElectionId == judge.AuthorityId && o.TermStartTick <= tick && o.TermEndTick > tick) == true ||
            government?.OfficeHistory.Any(o => o.Mandates == "land" && o.HolderId == judge.AgentId && o.ElectionId == judge.AuthorityId && o.StartTick <= tick && o.EndTick >= tick) == true :
            item.JudgeConsents.Any(c => c.AgentId == judge.AgentId && c.Tick <= tick && (c.WithdrawnTick is null || c.WithdrawnTick > tick)) &&
            !item.JudgeHistory.Any(t => t.Judge == judge && t.EndedTick < tick);

    private static void ValidateJudge(TownLandCaseJudge judge, TownLandCase item, long tick, IReadOnlySet<string> agents, TownGovernmentState? government)
    {
        Check(judge is not null && agents.Contains(judge.AgentId) && Id(judge.AuthorityId) && judge.AssignedTick >= item.FiledTick && judge.AssignedTick <= tick &&
            (judge.Kind == "case_elected" && item.ContestHistory.Any(c => c.Id == judge.AuthorityId && c.Stage == "completed" && c.WinnerId == judge.AgentId && c.SettledTick <= judge.AssignedTick) ||
                judge.Kind == "land_mayor" && (government?.Offices.Any(o => o.Mandates == "land" && o.HolderId == judge.AgentId && o.ElectionId == judge.AuthorityId && o.TermStartTick <= judge.AssignedTick && o.TermEndTick >= judge.AssignedTick) == true ||
                    government?.OfficeHistory.Any(o => o.Mandates == "land" && o.HolderId == judge.AgentId && o.ElectionId == judge.AuthorityId && o.StartTick <= judge.AssignedTick && o.EndTick >= judge.AssignedTick) == true)),
            "A saved judge cannot invent a general or case-only mandate.");
    }

    private static void ValidateContest(TownLandCaseJudgeContest contest, TownLandCase item, long tick, int day, IReadOnlySet<string> agents)
    {
        Check(contest is not null && Id(contest.Id) && contest.Stage is "voting" or "waiting" or "completed" or "failed" or "cancelled" && contest.Round >= 0 && contest.Interruptions >= 0 &&
            contest.OpenedTick >= item.FiledTick && contest.OpenedTick <= tick && contest.Rounds is not null && contest.Ballots is not null &&
            Canonical(contest.Voters) && Canonical(contest.Candidates) && Canonical(contest.TiedCandidates) &&
            contest.Voters.All(agents.Contains) && contest.Candidates.All(agents.Contains), "A saved case election needs actual resident candidates and ballots.");
        ValidateBallots(contest.Voters, contest.Candidates, contest.Ballots);
        if (contest.Stage == "voting") Check(contest.RoundOpenedTick is { } opened && opened >= contest.OpenedTick && opened <= tick && contest.RoundDeadlineTick == opened + day,
            "A case election round needs a full unpaused voting day.");
        foreach (var round in contest.Rounds)
        {
            Check(round is not null && round.Number > 0 && round.Number <= contest.Round && round.OpenedTick >= contest.OpenedTick && round.ClosedTick >= round.OpenedTick && round.ClosedTick <= tick &&
                round.Result is "winner" or "tie" or "interrupted" or "failed" or "cancelled" && Canonical(round.Voters) && Canonical(round.Candidates) && Canonical(round.TiedCandidates), "A saved case election must retain each completed or interrupted round.");
            ValidateBallots(round.Voters, round.Candidates, round.Ballots);
            Check(round.Candidates.All(id => item.JudgeConsents.Any(c => c.AgentId == id && c.Tick <= round.OpenedTick && (c.WithdrawnTick is null || c.WithdrawnTick >= round.ClosedTick))),
                "A case candidate must personally consent to that case's mandate.");
        }
        Unique(contest.Rounds.Select(r => r.Number.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        if (contest.Stage == "completed")
        {
            var round = contest.Rounds.Count == 0 ? null : contest.Rounds[^1];
            Check(round is { Result: "winner" } && contest.WinnerId is { } winner && round.Candidates.Contains(winner, StringComparer.Ordinal) && contest.SettledTick == round.ClosedTick &&
                round.Ballots.Any(b => b.CandidateId == winner) && round.Candidates.Where(id => id != winner).All(id =>
                    round.Ballots.Count(b => b.CandidateId == id) < round.Ballots.Count(b => b.CandidateId == winner)), "A case judge must win an actual unique-highest resident vote.");
        }
        else Check(contest.WinnerId is null, "An unfinished case election cannot invent a winner.");
    }

    private static void ValidateBallots(IReadOnlyList<string> voters, IReadOnlyList<string> candidates, IReadOnlyList<TownMayoralBallot> ballots)
    {
        Check(ballots is not null && ballots.All(b => b is not null && voters.Contains(b.AgentId, StringComparer.Ordinal) && candidates.Contains(b.CandidateId, StringComparer.Ordinal)), "A case ballot needs an eligible voter and candidate.");
        Unique(ballots.Select(b => b.AgentId));
    }

    private static void ValidateRight(SeededMap map, long tick, string townId, TownLandRightVersion snapshot,
        IReadOnlyList<TownLandTitleRecord> titles, IReadOnlySet<string> households) =>
        Check(snapshot is not null && snapshot.Right is { } right && snapshot.Id == right.Id && Id(right.Id) && right.TownId == townId &&
            households.Contains(right.HouseholdId) && TownLandHearingRules.ValidText(right.GrantSource, 64) && snapshot.Version == TownLandHearingRules.Version(right) &&
            TownLandRightsRules.IsValidPlot(map, right.Tiles, tick, right.GrantedTick, right.AgreedEndTick) &&
            right.Tiles.All(t => TownLandRightsRules.IsCoveredByTownTitle(t, townId, titles)), "A saved right snapshot must retain its exact known holder, plot, terms and version.");
    private static bool SameTileTerms(IEnumerable<HouseholdLandUseRight> a, IEnumerable<HouseholdLandUseRight> b) =>
        TileTerms(a).SequenceEqual(TileTerms(b));
    private static IEnumerable<string> TileTerms(IEnumerable<HouseholdLandUseRight> rights) => rights.SelectMany(r => r.Tiles.Select(t =>
        System.Text.Json.JsonSerializer.Serialize(new { t.X, t.Y, r.TownId, r.HouseholdId, r.GrantedTick, r.GrantSource, r.AgreedEndTick }))).Order(StringComparer.Ordinal);
    private static bool Id(string? id) => TownLandHearingRules.ValidText(id, 256);
    /// <summary>A ledger identity must be one its own sequence has already issued, or the next record would reuse it.</summary>
    private static bool Generated(string? id, string prefix, string townId, long sequence) =>
        TownGovernmentValidation.ValidId(id, prefix + townId + ":", sequence);
    private static bool Canonical(IReadOnlyList<string>? ids) => ids is not null && ids.All(Id) && ids.SequenceEqual(TownLandHearingRules.Ordered(ids));
    private static void Unique(IEnumerable<string> ids) { var all = ids.ToArray(); Check(all.Distinct(StringComparer.Ordinal).Count() == all.Length, "Saved hearing record identities must be unique."); }
    private static void Check([DoesNotReturnIf(false)] bool valid, string reason) { if (!valid) throw new InvalidDataException(reason); }
}

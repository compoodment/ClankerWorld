using System.Globalization;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>Pure public files for allegations and civil findings, without physical enforcement.</summary>
public static class TownNonviolentRules
{
    public const string CivilStandard = "more_likely_than_not";
    public static TownCaseRevision CurrentRevision(TownViolationCase item) => item.Revisions[^1];
    public static string RevisionToken(TownViolationCase item) => item.Id + ":" + CurrentRevision(item).Number.ToString(CultureInfo.InvariantCulture);
    public static string CaseKey(string townId, TownViolationAllegation allegation) =>
        TownHearingProcedure.Digest(new { Town = townId, allegation.IncidentId, allegation.LawId, allegation.LawVersion });
    internal static TownNonviolentState Replace(TownNonviolentState state, TownViolationCase item) =>
        state with { Cases = state.Cases.Select(c => c.Id == item.Id ? item : c).ToArray() };
    internal static string NextId(TownNonviolentState state, string kind, string? townId = null) =>
        "nonviolent-" + kind + ":" + (townId ?? state.Cases[0].TownId) + ":" + checked(state.Sequence + 1).ToString(CultureInfo.InvariantCulture);
    private static TownViolationCase Exact(TownNonviolentState state, string id, int revision) =>
        state.Cases.SingleOrDefault(c => c.Id == id && CurrentRevision(c).Number == revision) ??
            throw new InvalidOperationException("This case notice has changed.");
    public static bool HasNoticeReceipt(TownCaseRevision revision, string actor, long tick, IReadOnlyList<TownCivicReceipt> receipts) =>
        TownHearingProcedure.HasNotice(revision.NoticeId, revision.PublishedTick, actor, tick, receipts);
    public static string ResponseToken(TownCaseResponse response) => TownHearingProcedure.Digest(response);
    public static bool ReadCurrent(TownViolationCase item, string actor, long tick) => item.Reads.Any(r =>
        r.AgentId == actor && r.Revision == CurrentRevision(item).Number && r.ReadTick <= tick &&
        item.Evidence.All(e => e.SubmittedTick <= r.ReadTick && r.EvidenceIds.Contains(e.Id, StringComparer.Ordinal)) &&
        item.Responses.Where(response => response.Revision == r.Revision).All(response => response.Tick <= r.ReadTick && r.ResponseIds.Contains(ResponseToken(response))) &&
        item.Findings.All(finding => finding.Tick <= r.ReadTick && r.FindingIds.Contains(finding.Id)));
    public static bool JudgeConflict(string actor, string? household, IReadOnlyList<TownCaseParty> parties,
        IReadOnlySet<string>? directStakes = null) => TownHearingProcedure.Conflicted(actor, household,
            parties.SelectMany(p => new[] { p.SubjectId, p.RespondingAdultId ?? p.SubjectId }), parties.Select(p => p.HouseholdId), directStakes);
    public static bool CanCloseResponses(TownViolationCase item, IReadOnlyList<TownCaseParty> parties, long tick) =>
        TownHearingProcedure.ResponsesClosed(CurrentRevision(item).DeadlineTick, tick,
            parties.Select(p => (p.Id, p.RespondingAdultId)), item.Responses.Where(r => r.Revision == CurrentRevision(item).Number)
                .Select(r => (r.PartyId, r.AgentId, r.Tick)));

    public static TownNonviolentState File(TownNonviolentState state, string townId, TownViolationAllegation allegation,
        TownViolationFiling filing, IReadOnlyList<TownCaseParty> parties, long tick, int day, string noticeId)
    {
        if (!ValidAllegation(allegation) || !TownHearingProcedure.Id(townId) || filing is null ||
            !TownHearingProcedure.Id(filing.AgentId) || filing.Kind is not ("witness" or "affected" or "town") ||
            !TownHearingProcedure.Text(filing.Statement) || filing.Tick != tick || allegation.ConductTick > tick ||
            filing.EvidenceIds is null || !filing.EvidenceIds.All(TownHearingProcedure.Id) ||
            filing.Kind == "town" && !TownHearingProcedure.Id(filing.AuthorityId) || !ValidParties(parties) ||
            !parties.Any(p => p.SubjectId == allegation.SubjectId && p.Role == "subject") || day <= 0 || !TownHearingProcedure.Id(noticeId))
            throw new InvalidOperationException("A report needs specific conduct, its applicable law, known sources and affected people.");
        var key = CaseKey(townId, allegation);
        if (state.Cases.SingleOrDefault(c => c.Key == key) is { } old)
        {
            if (old.Allegation.SubjectId != allegation.SubjectId || old.Allegation.ConductTick != allegation.ConductTick ||
                old.Allegation.Position != allegation.Position || old.Allegation.ConductKind != allegation.ConductKind)
                throw new InvalidOperationException("The same incident cannot silently identify different conduct.");
            if (old.Status != "pending") throw new InvalidOperationException("A settled act requires grounded reopening, not another report.");
            return Replace(state, old with { Filings = old.Filings.Contains(filing) ? old.Filings : old.Filings.Append(filing).ToArray() });
        }
        var revision = new TownCaseRevision(1, parties.OrderBy(p => p.Id, StringComparer.Ordinal).ToArray(), noticeId, tick, checked(tick + day));
        var item = new TownViolationCase(NextId(state, "case", townId), key, townId, allegation, tick, "pending", [revision], [filing],
            [], [], [], [], null, [], [], null, [], [], TownHearingProcedure.Ordered(parties.Select(p => p.SubjectId).Append(filing.AgentId)));
        return state with { Sequence = checked(state.Sequence + 1), Cases = state.Cases.Append(item).ToArray() };
    }

    internal static bool ValidAllegation(TownViolationAllegation? a) => a is not null && TownHearingProcedure.Id(a.IncidentId) &&
        TownHearingProcedure.Id(a.SubjectId) && TownHearingProcedure.Id(a.ConductKind) && a.ConductTick >= 0 &&
        TownHearingProcedure.Id(a.LawId) && a.LawVersion > 0 && TownHearingProcedure.Text(a.Statement) &&
        a.SourceEvidenceIds is not null && a.SourceEvidenceIds.All(TownHearingProcedure.Id);
    internal static bool ValidParties(IReadOnlyList<TownCaseParty>? parties) => parties is { Count: > 0 } &&
        parties.All(p => p is not null && TownHearingProcedure.Id(p.Id) && p.Role is "subject" or "affected" or "town" &&
            TownHearingProcedure.Id(p.SubjectId) && (p.RespondingAdultId is null || TownHearingProcedure.Id(p.RespondingAdultId)) &&
            (p.CareRelationshipId is null ? p.CareRevision is null : TownHearingProcedure.Id(p.CareRelationshipId) && p.CareRevision > 0) &&
            (p.HouseholdId is null || TownHearingProcedure.Id(p.HouseholdId))) &&
        parties.Select(p => p.Id).Distinct(StringComparer.Ordinal).Count() == parties.Count;
    public static bool RequiresNewNotice(TownCaseRevision old, IReadOnlyList<TownCaseParty> parties) =>
        parties.Any(p => !old.Parties.Contains(p));
    public static TownNonviolentState ReviseParties(TownNonviolentState state, string caseId,
        IReadOnlyList<TownCaseParty> parties, long tick, int day, string noticeId)
    {
        var item = state.Cases.Single(c => c.Id == caseId && c.Status == "pending");
        if (!ValidParties(parties) || day <= 0 || !TownHearingProcedure.Id(noticeId)) throw new InvalidOperationException("A notice needs current legitimate parties.");
        var old = CurrentRevision(item);
        return !RequiresNewNotice(old, parties) ? state : Replace(state, item with
        {
            Revisions = item.Revisions.Append(
            new TownCaseRevision(old.Number + 1, parties.OrderBy(p => p.Id, StringComparer.Ordinal).ToArray(), noticeId, tick, checked(tick + day))).ToArray()
        });
    }
    public static TownNonviolentState Inspect(TownNonviolentState state, string caseId, int revision, string actor, long tick)
    {
        var item = Exact(state, caseId, revision);
        if (!TownHearingProcedure.Id(actor) || tick < CurrentRevision(item).PublishedTick) throw new InvalidOperationException("Read the published case first.");
        return Replace(state, item with
        {
            Reads = item.Reads.Append(new TownCaseRead(revision, actor, tick,
            TownHearingProcedure.Ordered(item.Evidence.Select(e => e.Id)), TownHearingProcedure.Ordered(item.ReopenRequests.Select(r => r.Id)))
            {
                ResponseIds = TownHearingProcedure.Ordered(item.Responses.Where(r => r.Revision == revision).Select(ResponseToken)),
                FindingIds = TownHearingProcedure.Ordered(item.Findings.Select(f => f.Id))
            }).ToArray()
        });
    }
    public static TownNonviolentState RelayRead(TownNonviolentState state, string caseId, int revision, string source, string recipient, long tick)
    {
        var item = Exact(state, caseId, revision);
        var reads = item.Reads.Where(r => r.Revision == revision && r.ReadTick <= tick && r.AgentId == source).ToArray();
        if (source == recipient || reads.Length == 0) throw new InvalidOperationException("Only an informed person can relay this case.");
        reads = reads.Concat(item.Reads.Where(r => r.Revision == revision && r.ReadTick <= tick && r.AgentId == recipient)).ToArray();
        return Replace(state, item with
        {
            Reads = item.Reads.Append(new TownCaseRead(revision, recipient, tick,
            TownHearingProcedure.Ordered(reads.SelectMany(r => r.EvidenceIds)), TownHearingProcedure.Ordered(reads.SelectMany(r => r.ReopenRequestIds)), source)
            {
                ResponseIds = TownHearingProcedure.Ordered(reads.SelectMany(r => r.ResponseIds)),
                FindingIds = TownHearingProcedure.Ordered(reads.SelectMany(r => r.FindingIds))
            }).ToArray()
        });
    }
    public static TownNonviolentState Respond(TownNonviolentState state, string caseId, int revision, string actor,
        string representedActor, string partyId, string kind, string text, long tick, IReadOnlyList<TownCivicReceipt> receipts,
        IReadOnlyList<TownCaseParty> currentParties)
    {
        var item = Exact(state, caseId, revision);
        var party = currentParties.SingleOrDefault(p => p.Id == partyId && p.SubjectId == representedActor && p.RespondingAdultId == actor);
        if (item.Status != "pending" || party is null || !CurrentRevision(item).Parties.Contains(party) || kind is not ("answer" or "waive") ||
            !TownHearingProcedure.Text(text) || !HasNoticeReceipt(CurrentRevision(item), actor, tick, receipts))
            throw new InvalidOperationException("An answer requires the current person's own informed response or legitimate caregiver support.");
        var response = new TownCaseResponse(revision, partyId, actor, representedActor, kind, text, tick);
        return item.Responses.Contains(response) ? state : Replace(state, item with { Responses = item.Responses.Append(response).ToArray() });
    }
    public static TownNonviolentState AddEvidence(TownNonviolentState state, string caseId, int revision,
        TownCaseEvidence evidence, IReadOnlyList<TownCivicReceipt> receipts)
    {
        var item = Exact(state, caseId, revision);
        if (!ValidEvidence(evidence) || evidence.Revision != revision || item.Evidence.Any(e => e.Id == evidence.Id) ||
            !HasNoticeReceipt(CurrentRevision(item), evidence.SubmittedByAgentId, evidence.SubmittedTick, receipts))
            throw new InvalidOperationException("Evidence requires a sourced, informed submission to this case.");
        return Replace(state, item with { Evidence = item.Evidence.Append(evidence).ToArray() });
    }
    internal static bool ValidEvidence(TownCaseEvidence? e) => e is not null && TownHearingProcedure.Id(e.Id) && e.Revision > 0 &&
        TownHearingProcedure.Text(e.Text) && TownHearingProcedure.Id(e.SourceAgentId) && TownHearingProcedure.Id(e.SubmittedByAgentId) &&
        e.ObservedTick >= 0 && e.SubmittedTick >= e.ObservedTick &&
        (e.Kind == "allegation" && e.Acquisition is "statement" or "relay" &&
            (e.SourceRecordId is null && e.SourceVersion is null || TownHearingProcedure.Id(e.SourceRecordId) && TownHearingProcedure.Id(e.SourceVersion)) ||
         e.Kind == "observation" && e.Acquisition == "firsthand" && e.SourceAgentId == e.SubmittedByAgentId &&
            (e.SourceRecordId is null && e.SourceVersion is null || TownHearingProcedure.Id(e.SourceRecordId) && TownHearingProcedure.Id(e.SourceVersion)) ||
         e.Kind == "record" && e.Acquisition == "record_inspection" && e.SourceAgentId == e.SubmittedByAgentId &&
            TownHearingProcedure.Id(e.SourceRecordId) && TownHearingProcedure.Id(e.SourceVersion));

    internal static bool SupportsConduct(TownViolationCase item, TownCaseEvidence evidence) =>
        evidence.Kind is "observation" or "record" && evidence.SourceRecordId == item.Allegation.IncidentId &&
        TownHearingProcedure.Id(evidence.SourceVersion);

    public static TownNonviolentState AssignJudge(TownNonviolentState state, string caseId, TownCaseJudge judge)
    {
        if (judge.Kind is not ("non_land_mayor" or "case_elected") || !TownHearingProcedure.Id(judge.AuthorityId) || !TownHearingProcedure.Id(judge.AgentId))
            throw new InvalidOperationException("A formal case requires its explicit non-land authority.");
        var item = TownCaseJudgeRules.CancelContest(state.Cases.Single(c => c.Id == caseId), judge.AssignedTick, "authorized_judge_available");
        return Replace(state, item with { Judge = judge });
    }
    public static TownNonviolentState InvalidateJudge(TownNonviolentState state, string caseId, long tick, string reason)
    {
        var item = state.Cases.Single(c => c.Id == caseId);
        return item.Judge is null ? state : Replace(state, item with
        {
            Judge = null,
            JudgeHistory = item.JudgeHistory.Append(new(item.Judge, tick, reason)).ToArray()
        });
    }
    public static TownNonviolentState Rule(TownNonviolentState state, string caseId, int revision, TownCaseJudge judge,
        long tick, string result, string consequence, IReadOnlyList<string> evidenceIds, string reasons, string uncertainty,
        IReadOnlyList<TownCaseParty> currentParties, bool validAuthority, bool crediblePriorNotice = false)
    {
        var item = Exact(state, caseId, revision);
        if (item.Status != "pending" || !validAuthority || item.Judge != judge ||
            JudgeConflict(judge.AgentId, null, currentParties, item.DirectStakeIds.ToHashSet(StringComparer.Ordinal)) ||
            RequiresNewNotice(CurrentRevision(item), currentParties) || !CanCloseResponses(item, currentParties, tick) ||
            !ReadCurrent(item, judge.AgentId, tick) || !TownHearingProcedure.Text(reasons) || !TownHearingProcedure.Text(uncertainty) ||
            result is not ("supported" or "unsupported") || consequence is not ("none" or "explanation" or "warning" or "censure") ||
            consequence == "censure" && !crediblePriorNotice ||
            result == "unsupported" && consequence != "none" || evidenceIds.Any(id => !item.Evidence.Any(e => e.Id == id)) ||
            result == "supported" && (currentParties.Any(p => p.RespondingAdultId is null) || !evidenceIds.Any(id =>
                item.Evidence.Any(e => e.Id == id && SupportsConduct(item, e)))))
            throw new InvalidOperationException("A finding needs current independent authority, a fair hearing and more than unsupported allegations.");
        var finding = new TownViolationFinding(NextId(state, "finding"), revision, judge, tick, result, CivilStandard,
            TownHearingProcedure.Ordered(evidenceIds), reasons, uncertainty, consequence, currentParties.ToArray());
        item = TownCaseJudgeRules.CancelContest(item, tick, "case_closed");
        return Replace(state with { Sequence = checked(state.Sequence + 1) }, item with
        {
            Status = "settled",
            SettledTick = tick,
            Findings = item.Findings.Append(finding).ToArray(),
            Judge = null,
            JudgeHistory = item.JudgeHistory.Append(new(judge, tick, "case_closed")).ToArray()
        });
    }

    public static TownNonviolentState RequestReopen(TownNonviolentState state, string caseId, string actor,
        string kind, IReadOnlyList<string> evidenceIds, string reasons, long tick)
    {
        var item = state.Cases.Single(c => c.Id == caseId && c.Status == "settled");
        if (kind is not ("material_evidence" or "procedural_error") || !TownHearingProcedure.Text(reasons) || evidenceIds.Count == 0 ||
            evidenceIds.Any(id => !item.Evidence.Any(e => e.Id == id))) throw new InvalidOperationException("Reopening needs identified evidence or a demonstrated procedural error.");
        var request = new TownCaseReopenRequest(NextId(state, "reopen"), actor, tick, kind, TownHearingProcedure.Ordered(evidenceIds), reasons);
        return Replace(state with { Sequence = checked(state.Sequence + 1) }, item with { ReopenRequests = item.ReopenRequests.Append(request).ToArray() });
    }
    public static bool MaterialNewEvidence(TownViolationCase item, TownCaseReopenRequest request)
    {
        var finding = item.Findings.LastOrDefault(r => r.Tick <= request.Tick);
        if (finding is null) return false;
        var known = item.Evidence.Where(e => e.SubmittedTick <= finding.Tick).ToArray();
        return request.EvidenceIds.Any(id => item.Evidence.Any(e => e.Id == id && e.Kind is "record" or "observation" &&
            e.SubmittedTick > finding.Tick && e.SubmittedTick <= request.Tick &&
            !known.Any(old => old.Kind == e.Kind && TownHearingProcedure.FactKey(old.Text) == TownHearingProcedure.FactKey(e.Text)) &&
            !item.Findings.Any(f => f.Id == e.SourceRecordId) &&
            !item.ReopenRequests.Any(r => r.Id == e.SourceRecordId) &&
            !known.Any(old => e.SourceRecordId is not null && old.SourceRecordId == e.SourceRecordId && old.SourceVersion == e.SourceVersion &&
                (e.Kind == "record" || old.SourceAgentId == e.SourceAgentId))));
    }
    public static bool DemonstratedProceduralError(TownViolationCase item, TownCaseReopenRequest request) =>
        item.Findings.Where(f => f.Tick <= request.Tick).Any(f =>
            !TownHearingProcedure.ResponsesClosed(item.Revisions.Single(r => r.Number == f.Revision).DeadlineTick, f.Tick,
                f.Parties.Select(p => (p.Id, p.RespondingAdultId)), item.Responses.Where(r => r.Revision == f.Revision).Select(r => (r.PartyId, r.AgentId, r.Tick))) ||
            JudgeConflict(f.Judge.AgentId, null, f.Parties) || !item.Reads.Any(r => r.AgentId == f.Judge.AgentId &&
                r.Revision == f.Revision && r.ReadTick <= f.Tick && f.EvidenceIds.All(r.EvidenceIds.Contains)));
    public static TownNonviolentState Reopen(TownNonviolentState state, string caseId, string requestId, TownCaseJudge judge,
        bool groundsEstablished, string assessment, IReadOnlyList<TownCaseParty> currentParties,
        long tick, int day, string noticeId, bool validAuthority)
    {
        var item = state.Cases.Single(c => c.Id == caseId && c.Status == "settled");
        var request = item.ReopenRequests.Single(r => r.Id == requestId && r.Status == "pending");
        if (!validAuthority || item.Judge != judge || !TownHearingProcedure.Text(assessment) ||
            !ValidParties(currentParties) || day <= 0 || !TownHearingProcedure.Id(noticeId) || tick < request.Tick ||
            !item.Reads.Any(r => r.AgentId == judge.AgentId && r.Revision == CurrentRevision(item).Number &&
                r.ReadTick >= request.Tick && r.ReadTick <= tick && r.ReopenRequestIds.Contains(request.Id, StringComparer.Ordinal)) ||
            groundsEstablished && !(request.Kind == "material_evidence" ? MaterialNewEvidence(item, request) : DemonstratedProceduralError(item, request)))
            throw new InvalidOperationException("An informed independent judge must establish the reopening grounds.");
        request = request with { Status = groundsEstablished ? "accepted" : "rejected", AssessedBy = judge, AssessedTick = tick, Assessment = assessment };
        item = item with { ReopenRequests = item.ReopenRequests.Select(r => r.Id == requestId ? request : r).ToArray() };
        if (groundsEstablished)
            item = item with
            {
                Status = "pending",
                SettledTick = null,
                Revisions = item.Revisions.Append(new TownCaseRevision(
                CurrentRevision(item).Number + 1, currentParties.ToArray(), noticeId, tick, checked(tick + day))).ToArray()
            };
        else if (!item.ReopenRequests.Any(r => r.Status == "pending"))
            item = item with { Judge = null, JudgeHistory = item.JudgeHistory.Append(new(judge, tick, "reopening_rejected")).ToArray() };
        return Replace(state, item);
    }
}

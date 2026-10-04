using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClankerWorld.Simulation.Harness;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>Pure changes to the public hearing file and formal use permissions.</summary>
public static class TownLandHearingRules
{
    public const int MaximumTextLength = 1024;
    public static string Version(HouseholdLandUseRight right) => Digest(JsonSerializer.Serialize(right));
    public static string RecordVersion<T>(T record) => Digest(JsonSerializer.Serialize(record));
    public static string LawVersion(TownLawVersion version) => RecordVersion(version with { EndedTick = null, EndedByProposalId = null });
    public static TownLandRightVersion Snapshot(HouseholdLandUseRight right) => new(right.Id, Version(right), right);
    public static TownLandCaseRevision CurrentRevision(TownLandCase item) => item.Revisions[^1];
    public static string RevisionToken(TownLandCase item) => item.Id + ":" + CurrentRevision(item).Number.ToString(CultureInfo.InvariantCulture);
    public static string CaseKey(string townId, string kind, IEnumerable<GridPoint> tiles) =>
        Digest(townId + "|" + kind + "|" + TownLandClaimRules.DescribeTiles(TownLandRightsRules.OrderTiles(tiles)));
    internal static string Digest(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    internal static bool ValidText(string? text, int max = MaximumTextLength) =>
        !string.IsNullOrWhiteSpace(text) && text.Length <= max && !text.Any(char.IsControl);
    internal static string[] Ordered(IEnumerable<string> ids) => ids.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    private static TownLandHearingState Replace(TownLandHearingState state, TownLandCase item) =>
        state with { Cases = state.Cases.Select(c => c.Id == item.Id ? item : c).ToArray() };

    public static bool IsValidOutcome(TownLandRequestedOutcome outcome, long tick) =>
        outcome is not null && outcome.Kind is "confirm" or "renew" or "amend" or "end" or "reject" &&
        (outcome.Kind is "confirm" or "reject" ? outcome.HouseholdId is null && outcome.AgreedEndTick is null :
            ValidText(outcome.HouseholdId, 128) && (outcome.Kind == "end" ? outcome.AgreedEndTick is null :
                outcome.AgreedEndTick is null || outcome.AgreedEndTick > tick));

    public static TownLandHearingState File(TownLandHearingState state, string townId,
        TownLandCaseFiling filing, IReadOnlyList<GridPoint> tiles, IReadOnlyList<HouseholdLandUseRight> currentRights,
        IReadOnlyList<TownLandCaseParty> parties, long tick, int day, string noticeId)
    {
        if (filing.Kind is not ("dispute" or "expiry" or "town") || !ValidText(filing.Text) || !IsValidOutcome(filing.RequestedOutcome, tick) ||
            filing.Tick != tick || day <= 0 || !ValidText(noticeId, 128) || tiles.Count == 0 ||
            !tiles.SequenceEqual(TownLandRightsRules.OrderTiles(tiles.Distinct())) ||
            filing.Kind == "expiry" && filing.AgentId is not null ||
            filing.Kind == "expiry" && !currentRights.Any(r => r.Id == filing.AuthorityId && r.AgreedEndTick is { } end && end <= tick && r.Tiles.Any(tiles.Contains)) ||
            filing.Kind != "expiry" && !parties.Any(p => p.AdultIds.Contains(filing.AgentId!, StringComparer.Ordinal) || p.RepresentativeId == filing.AgentId))
            throw new InvalidOperationException("A land filing requires an identified plot, eligible filer and bounded requested outcome.");
        var kind = filing.Kind == "expiry" ? "expiry" : "dispute";
        var key = CaseKey(townId, kind, tiles);
        if (FilingRefusal(state, townId, tiles, currentRights, filing.Kind == "expiry" ? filing.AuthorityId : null) is { } refusal)
            throw new InvalidOperationException(refusal);
        if (state.Cases.SingleOrDefault(c => c.TownId == townId && c.Status == "pending" && CurrentRevision(c).Tiles.SequenceEqual(tiles)) is { } existing)
            return Replace(state, existing with { Filings = existing.Filings.Contains(filing) ? existing.Filings : existing.Filings.Append(filing).ToArray() });
        var id = "land-case:" + townId + ":" + (state.Sequence + 1).ToString(CultureInfo.InvariantCulture);
        var revision = Revision(1, tiles, currentRights, parties, noticeId, tick, day, filing.RequestedOutcome);
        var item = new TownLandCase(id, key, kind, tick, "pending", [revision], [filing], [], [], [], null, [], [], null, [], []) { TownId = townId };
        return state with { Sequence = state.Sequence + 1, Cases = state.Cases.Append(item).ToArray() };
    }

    /// <summary>
    /// Why a filing for this plot can neither join nor open a case now, in words a notice can show; null when it can.
    /// The same plot joins a pending case. Any other overlap with a pending case waits for it. A settled case bars its
    /// tiles until their permission terms change; an expiry review is also barred only by a ruling made at or after that end.
    /// </summary>
    public static string? FilingRefusal(TownLandHearingState state, string townId, IReadOnlyList<GridPoint> tiles,
        IReadOnlyList<HouseholdLandUseRight> currentRights, string? expiredRightId = null)
    {
        var overlapping = state.Cases.Where(c => c.TownId == townId && CurrentRevision(c).Tiles.Any(tiles.Contains)).ToArray();
        if (overlapping.Any(c => c.Status == "pending" && CurrentRevision(c).Tiles.SequenceEqual(tiles))) return null;
        if (overlapping.Any(c => c.Status == "pending"))
            return "A pending land hearing already covers part of this plot. Only a filing for exactly its plot can join it.";
        return overlapping.Any(settled =>
        {
            var overlap = CurrentRevision(settled).Tiles.Where(tiles.Contains).ToArray();
            var ruling = settled.Rulings[^1];
            var ruled = ruling.AdjustmentIds.Count == 0 ? CurrentRevision(settled).RightVersions.Select(r => r.Right) :
                state.Adjustments.Where(a => a.RulingId == ruling.Id).SelectMany(a => a.ResultRights);
            return MaterialKey(overlap, ruled, []) == MaterialKey(overlap, currentRights, []) &&
                (expiredRightId is null || currentRights.Any(r => r.Id == expiredRightId && r.AgreedEndTick <= ruling.Tick));
        }) ? "A hearing already settled this plot and its permissions have not changed since. Reopening that case needs new evidence or a procedural error." : null;
    }

    public static TownLandHearingState Revise(TownLandHearingState state, string caseId,
        IReadOnlyList<HouseholdLandUseRight> currentRights, IReadOnlyList<TownLandCaseParty> parties,
        IReadOnlyList<GridPoint> tiles, long tick, int day, string noticeId)
    {
        var item = Pending(state, caseId);
        var old = CurrentRevision(item);
        if (!RequiresNewNotice(old, tiles, currentRights, parties)) return state;
        var next = Revision(old.Number + 1, tiles, currentRights, parties, noticeId, tick, day, old.RequestedOutcome);
        return Replace(state, item with { Revisions = item.Revisions.Append(next).ToArray() });
    }

    private static TownLandCaseRevision Revision(int number, IReadOnlyList<GridPoint> tiles,
        IReadOnlyList<HouseholdLandUseRight> rights, IReadOnlyList<TownLandCaseParty> parties,
        string noticeId, long tick, int day, TownLandRequestedOutcome outcome) =>
        new(number, TownLandRightsRules.OrderTiles(tiles), rights.Where(r => r.Tiles.Any(tiles.Contains))
            .OrderBy(r => r.Id, StringComparer.Ordinal).Select(Snapshot).ToArray(),
            parties.OrderBy(p => p.Id, StringComparer.Ordinal).Select(p => p with { AdultIds = Ordered(p.AdultIds) }).ToArray(),
            noticeId, tick, checked(tick + day), outcome);

    private static string MaterialKey(IEnumerable<GridPoint> tiles, IEnumerable<HouseholdLandUseRight> rights,
        IEnumerable<TownLandCaseParty> parties)
    {
        var plot = tiles.ToHashSet();
        var uses = rights.SelectMany(r => r.Tiles.Where(plot.Contains).Select(t =>
            new { t.X, t.Y, r.TownId, r.HouseholdId, r.GrantedTick, r.GrantSource, r.AgreedEndTick }))
            .OrderBy(x => x.Y).ThenBy(x => x.X);
        return JsonSerializer.Serialize(new
        {
            Tiles = TownLandRightsRules.OrderTiles(plot),
            Uses = uses,
            Parties = parties.OrderBy(p => p.Id, StringComparer.Ordinal).Select(p => p with { AdultIds = Ordered(p.AdultIds) })
        });
    }

    public static bool RequiresNewNotice(TownLandCaseRevision revision, IReadOnlyList<GridPoint> tiles,
        IEnumerable<HouseholdLandUseRight> rights, IReadOnlyList<TownLandCaseParty> parties) =>
        MaterialKey(revision.Tiles, revision.RightVersions.Select(r => r.Right), []) != MaterialKey(tiles, rights, []) ||
        parties.Any(p => revision.Parties.SingleOrDefault(old => old.Id == p.Id) is not { } original ||
            p.Kind != original.Kind || p.HouseholdId != original.HouseholdId || p.TownId != original.TownId ||
            p.AdultIds.Any(id => !original.AdultIds.Contains(id, StringComparer.Ordinal)));

    public static bool HasNoticeReceipt(TownLandCaseRevision revision, string actor, long tick, IReadOnlyList<TownCivicReceipt> receipts) =>
        receipts.Any(r => r.AgentId == actor && r.NoticeId == revision.NoticeId && r.LearnedTick >= revision.PublishedTick && r.LearnedTick <= tick);

    public static TownLandHearingState Inspect(TownLandHearingState state, string caseId, int revision, string actor, long tick)
    {
        var item = Exact(state, caseId, revision);
        if (tick < CurrentRevision(item).PublishedTick || !ValidText(actor, 128)) throw new InvalidOperationException("The current case file must actually be inspected.");
        var read = new TownLandCaseRead(revision, actor, tick, Ordered(item.Evidence.Select(e => e.Id)))
        { ReopenRequestIds = Ordered(item.ReopenRequests.Select(r => r.Id)) };
        return Replace(state, item with { Reads = item.Reads.Append(read).ToArray() });
    }

    public static TownLandHearingState RelayRead(TownLandHearingState state, string caseId, int revision,
        string source, string recipient, long tick)
    {
        var item = Exact(state, caseId, revision);
        var sourceReads = item.Reads.Where(r => r.Revision == revision && r.AgentId == source && r.ReadTick <= tick).ToArray();
        if (source == recipient || sourceReads.Length == 0) throw new InvalidOperationException("A case relay requires an actual informed source.");
        var ids = Ordered(sourceReads.SelectMany(r => r.EvidenceIds).Concat(item.Reads
            .Where(r => r.Revision == revision && r.AgentId == recipient && r.ReadTick <= tick).SelectMany(r => r.EvidenceIds)));
        var requests = Ordered(sourceReads.SelectMany(r => r.ReopenRequestIds).Concat(item.Reads
            .Where(r => r.Revision == revision && r.AgentId == recipient && r.ReadTick <= tick).SelectMany(r => r.ReopenRequestIds)));
        return Replace(state, item with
        {
            Reads = item.Reads.Append(new TownLandCaseRead(revision, recipient, tick, ids, source)
            { ReopenRequestIds = requests }).ToArray()
        });
    }

    public static TownLandHearingState Respond(TownLandHearingState state, string caseId, int revision,
        string actor, string kind, string text, long tick, IReadOnlyList<TownCivicReceipt> receipts,
        IReadOnlyList<TownLandCaseParty> currentParties, string? partyId = null)
    {
        var item = Exact(state, caseId, revision);
        var notice = CurrentRevision(item);
        var party = currentParties.SingleOrDefault(p => (partyId is null || p.Id == partyId) &&
            (p.Kind == "household" ? p.AdultIds.Contains(actor, StringComparer.Ordinal) : p.RepresentativeId == actor));
        if (item.Status != "pending" || party is null || !notice.Parties.Any(p => p.Id == party.Id) || kind is not ("answer" or "waive") ||
            !ValidText(text) || !HasNoticeReceipt(notice, actor, tick, receipts))
            throw new InvalidOperationException("A response requires the current party's own informed answer or waiver.");
        var response = new TownLandCaseResponse(revision, party.Id, actor, kind, text, tick);
        return Replace(state, item with
        {
            Responses = item.Responses.Where(r => r.Revision != revision || r.PartyId != party.Id || r.AgentId != actor)
            .Append(response).OrderBy(r => r.Revision).ThenBy(r => r.PartyId, StringComparer.Ordinal).ThenBy(r => r.AgentId, StringComparer.Ordinal).ToArray()
        });
    }

    public static TownLandHearingState AddEvidence(TownLandHearingState state, string caseId, int revision,
        TownLandEvidence evidence, IReadOnlyList<TownCivicReceipt> receipts)
    {
        var item = Exact(state, caseId, revision);
        if (evidence.Revision != revision || !ValidEvidence(evidence) || item.Evidence.Any(e => e.Id == evidence.Id) ||
            !HasNoticeReceipt(CurrentRevision(item), evidence.SubmittedByAgentId, evidence.SubmittedTick, receipts))
            throw new InvalidOperationException("Case evidence requires an actual sourced submission to the current case.");
        return Replace(state, item with { Evidence = item.Evidence.Append(evidence).ToArray() });
    }

    internal static bool ValidEvidence(TownLandEvidence e) => e is not null && ValidText(e.Id, 128) &&
        e.Revision > 0 && ValidText(e.Text) && ValidText(e.SourceAgentId, 128) && ValidText(e.SubmittedByAgentId, 128) &&
        e.ObservedTick >= 0 && e.SubmittedTick >= e.ObservedTick &&
        (e.Kind == "allegation" && e.Acquisition is "statement" or "relay" && e.SourceRecordId is null && e.SourceVersion is null ||
            e.Kind == "observation" && e.Acquisition == "firsthand" && e.SourceAgentId == e.SubmittedByAgentId && e.SourceRecordId is null && e.SourceVersion is null ||
            e.Kind == "record" && e.Acquisition == "record_inspection" && e.SourceAgentId == e.SubmittedByAgentId && ValidText(e.SourceRecordId, 128) && ValidText(e.SourceVersion, 128));

    public static bool CanCloseResponses(TownLandCase item, IReadOnlyList<TownLandCaseParty> currentParties, long tick)
    {
        var revision = CurrentRevision(item);
        if (tick >= revision.DeadlineTick) return true;
        return currentParties.All(p => p.Kind == "household" ? p.AdultIds.Count > 0 && p.AdultIds.All(id =>
            item.Responses.Any(r => r.Revision == revision.Number && r.PartyId == p.Id && r.AgentId == id && r.Tick <= tick)) :
            p.RepresentativeId is { } representative && item.Responses.Any(r => r.Revision == revision.Number && r.PartyId == p.Id && r.AgentId == representative && r.Tick <= tick));
    }

    public static bool JudgeConflict(string actor, string? householdId, IReadOnlyList<TownLandCaseParty> parties,
        IReadOnlySet<string>? directStakeIds = null) => directStakeIds?.Contains(actor) == true ||
        parties.Any(p => p.HouseholdId is not null && p.HouseholdId == householdId || p.RepresentativeId == actor || p.AdultIds.Contains(actor, StringComparer.Ordinal));

    public static TownLandHearingState AssignJudge(TownLandHearingState state, string caseId, TownLandCaseJudge judge)
    {
        var item = state.Cases.Single(c => c.Id == caseId);
        if (judge.Kind is not ("land_mayor" or "case_elected") || !ValidText(judge.AgentId, 128) || !ValidText(judge.AuthorityId, 128))
            throw new InvalidOperationException("A case needs a recorded land mandate.");
        item = TownLandCaseJudgeRules.CancelContest(item, judge.AssignedTick, "authorized_judge_available");
        return Replace(state, item with { Judge = judge });
    }

    public static TownLandHearingState InvalidateJudge(TownLandHearingState state, string caseId, long tick, string reason)
    {
        var item = state.Cases.Single(c => c.Id == caseId);
        return item.Judge is null ? state : Replace(state, item with
        {
            Judge = null,
            JudgeHistory = item.JudgeHistory.Append(new(item.Judge, tick, reason)).ToArray()
        });
    }

    public static (TownLandHearingState State, IReadOnlyList<HouseholdLandUseRight> Rights) Rule(
        TownLandHearingState state, SeededMap map, string caseId, int revision, TownLandCaseJudge judge, long tick,
        TownLandRequestedOutcome outcome, IReadOnlyList<string> evidenceIds, IReadOnlyList<string> lawIds, string reasons,
        IReadOnlyList<HouseholdLandUseRight> currentRights, IReadOnlyList<TownLandCaseParty> currentParties, bool validAuthority,
        IReadOnlyList<HouseholdLandUseRequest>? pendingRequests = null)
    {
        var item = Exact(state, caseId, revision);
        var notice = CurrentRevision(item);
        if (item.Status != "pending" || !validAuthority || item.Judge != judge || JudgeConflict(judge.AgentId, null, currentParties) || !CanCloseResponses(item, currentParties, tick) ||
            !ValidText(reasons) || !IsValidOutcome(outcome, tick) ||
            RequiresNewNotice(notice, notice.Tiles, currentRights, currentParties) ||
            !item.Reads.Any(r => r.AgentId == judge.AgentId && r.Revision == revision && r.ReadTick <= tick &&
                item.Evidence.All(e => e.SubmittedTick <= r.ReadTick && r.EvidenceIds.Contains(e.Id, StringComparer.Ordinal))) ||
            evidenceIds.Any(id => !item.Evidence.Any(e => e.Id == id)) || lawIds.Any(id => !ValidText(id, 128)))
            throw new InvalidOperationException("The current authorized judge must inspect the file and revalidate the hearing before ruling.");
        var changesRights = outcome.Kind is "renew" or "amend" or "end";
        if (changesRights && (evidenceIds.Count == 0 || evidenceIds.All(id => item.Evidence.Single(e => e.Id == id).Kind == "allegation")))
            throw new InvalidOperationException("Unsupported evidence cannot change formal use permissions.");
        var rulingId = "land-ruling:" + item.TownId + ":" + (state.Sequence + 1).ToString(CultureInfo.InvariantCulture);
        var prior = currentRights.Where(r => r.Tiles.Any(notice.Tiles.Contains)).ToArray();
        // Free tiles join a ruling only where a noticed household's pending request for them is being heard.
        var requested = pendingRequests?.Where(r => r.TownId == item.TownId && r.Status == "pending" && r.RequestedTick <= tick &&
            currentParties.Any(p => p.HouseholdId == r.HouseholdId)).SelectMany(TownLandRightsRules.UnresolvedRequestTiles).ToHashSet();
        var result = changesRights ? BoundedOutcome(map, prior, notice.Tiles, outcome, rulingId, tick,
            item.TownId, requested) : prior;
        if (result.Any(r => r.AgreedEndTick <= tick && r.Tiles.Any(notice.Tiles.Contains)))
            throw new InvalidOperationException("A permission past its agreed end stays provisional until the ruling renews, amends or ends it.");
        if (currentParties.Any(p => p.Kind == "household" && p.AdultIds.Count == 0 && IsAdverseChange(prior, result, p.HouseholdId!)))
            throw new InvalidOperationException("An unrepresented household cannot lose its current or provisional permission.");
        var adjustmentIds = changesRights ? new[] { "land-adjustment:" + item.TownId + ":" + (state.Sequence + 1).ToString(CultureInfo.InvariantCulture) } : [];
        if (changesRights)
            state = RecordAdjustment(state, new(adjustmentIds[0], "ruling", tick, prior.Select(Snapshot).ToArray(), result,
                notice.Tiles, item.Id, rulingId));
        var ruling = new TownLandRuling(rulingId, revision, judge, tick, outcome, Ordered(evidenceIds), Ordered(lawIds), reasons, adjustmentIds)
        { Parties = currentParties.OrderBy(p => p.Id, StringComparer.Ordinal).ToArray() };
        item = TownLandCaseJudgeRules.CancelContest(item, tick, "case_closed");
        item = item with
        {
            Status = "settled",
            SettledTick = tick,
            Rulings = item.Rulings.Append(ruling).ToArray(),
            Judge = null,
            JudgeHistory = item.JudgeHistory.Append(new(judge, tick, "case_closed")).ToArray()
        };
        state = Replace(state with { Sequence = state.Sequence + 1 }, item);
        return (state, currentRights.Where(r => !prior.Any(p => p.Id == r.Id)).Concat(result).OrderBy(r => r.Id, StringComparer.Ordinal).ToArray());
    }

    internal static IReadOnlyList<HouseholdLandUseRight> BoundedOutcome(SeededMap map,
        IReadOnlyList<HouseholdLandUseRight> prior, IReadOnlyList<GridPoint> tiles, TownLandRequestedOutcome outcome,
        string rulingId, long tick, string townId, IReadOnlyCollection<GridPoint>? requested = null)
    {
        var plot = tiles.ToHashSet();
        if (outcome.Kind is "renew" or "end" && !prior.Any(r => r.HouseholdId == outcome.HouseholdId && r.Tiles.Any(plot.Contains)))
            throw new InvalidOperationException("Renewal or ending must identify a current right-holder.");
        var result = new List<HouseholdLandUseRight>();
        var sequence = 0;
        void Pieces(HouseholdLandUseRight original, IEnumerable<GridPoint> subset, string household, long? end)
        {
            foreach (var piece in TownLandRightsRules.ConnectedPlots(map, subset))
                result.Add(original with
                {
                    Id = "household-use:hearing:" + Digest(rulingId + ":" + (sequence++).ToString(CultureInfo.InvariantCulture))[..24],
                    HouseholdId = household,
                    Tiles = piece,
                    AgreedEndTick = end
                });
        }
        foreach (var right in prior.OrderBy(r => r.Id, StringComparer.Ordinal))
        {
            Pieces(right, right.Tiles.Where(t => !plot.Contains(t)), right.HouseholdId, right.AgreedEndTick);
            var inside = right.Tiles.Where(plot.Contains);
            if (outcome.Kind == "end" && right.HouseholdId == outcome.HouseholdId) continue;
            // A renewal also renews every other permission on the plot that has lapsed, each for its own household,
            // so a ruling never has to pick between households merely to bring their lapsed permissions back.
            Pieces(right, inside, outcome.Kind == "amend" ? outcome.HouseholdId! : right.HouseholdId,
                outcome.Kind == "amend" || outcome.Kind == "renew" && (right.HouseholdId == outcome.HouseholdId || right.AgreedEndTick <= tick)
                    ? outcome.AgreedEndTick : right.AgreedEndTick);
        }
        // Free Town land with no heard request stays free: an ordinary grant needs Town approval and household acceptance.
        var free = tiles.Where(t => requested?.Contains(t) == true && prior.All(r => !r.Tiles.Contains(t))).ToArray();
        if (outcome.Kind == "amend" && free.Length > 0)
            Pieces(new("", townId, outcome.HouseholdId!, [], tick, "hearing:" + Digest(rulingId)[..32], outcome.AgreedEndTick), free, outcome.HouseholdId!, outcome.AgreedEndTick);
        return result.OrderBy(r => r.Id, StringComparer.Ordinal).ToArray();
    }

    private static readonly string[] RulingKinds = ["confirm", "renew", "amend", "end", "reject"];

    /// <summary>
    /// The rulings a judge is offered now, and the households whose permission on the plot is past its agreed end.
    /// A lapsed permission must be renewed, amended or ended; renewing covers every lapsed permission on the plot,
    /// and ending is offered for a single lapsed household only while it has an adult to represent it.
    /// </summary>
    public static (string[] Kinds, string[] Lapsed) RulingChoices(IReadOnlyList<HouseholdLandUseRight> currentRights,
        IReadOnlyList<GridPoint> plot, IReadOnlyList<TownLandCaseParty> parties, long tick)
    {
        var lapsed = Ordered(currentRights.Where(r => r.AgreedEndTick <= tick && r.Tiles.Any(plot.Contains)).Select(r => r.HouseholdId));
        var mayEnd = lapsed.Length == 0 || lapsed.Length == 1 && parties.Any(p => p.HouseholdId == lapsed[0] && p.AdultIds.Count > 0);
        return (RulingKinds.Where(kind => lapsed.Length == 0 || kind is "renew" or "amend" || kind == "end" && mayEnd).ToArray(), lapsed);
    }

    public static bool IsAdverseChange(IEnumerable<HouseholdLandUseRight> prior, IEnumerable<HouseholdLandUseRight> result, string householdId)
    {
        var after = result.Where(r => r.HouseholdId == householdId).ToArray();
        return prior.Where(r => r.HouseholdId == householdId).Any(before => before.Tiles.Any(tile =>
            !after.Any(r => r.Tiles.Contains(tile) && (r.AgreedEndTick is null || before.AgreedEndTick is { } end && r.AgreedEndTick >= end))));
    }

    public static TownLandHearingState RecordBuildingTransfer(TownLandHearingState state, SeededMap map,
        string buildingId, IReadOnlyList<GridPoint> footprint, string targetHouseholdId,
        IReadOnlyList<HouseholdLandUseRight> beforeRights, IReadOnlyList<HouseholdLandUseRight> afterRights, long tick)
    {
        var prior = beforeRights.Where(r => r.Tiles.Any(footprint.Contains)).OrderBy(r => r.Id, StringComparer.Ordinal).ToArray();
        var result = afterRights.Where(r => r.Tiles.Any(prior.SelectMany(p => p.Tiles).Contains)).OrderBy(r => r.Id, StringComparer.Ordinal).ToArray();
        var expected = TownLandRightsRules.ReassignFootprintRights(map, beforeRights, footprint.ToHashSet(), targetHouseholdId, tick);
        if (!SameRights(expected, afterRights)) throw new InvalidOperationException("A recorded transfer must reproduce the actual authorized footprint reassignment.");
        if (SameRights(prior, result)) return state;
        var adjustment = new TownLandRightAdjustment("land-adjustment:" + prior[0].TownId + ":" + (state.Sequence + 1).ToString(CultureInfo.InvariantCulture),
            "building_transfer", tick, prior.Select(Snapshot).ToArray(), result, TownLandRightsRules.OrderTiles(footprint),
            BuildingId: buildingId, TargetHouseholdId: targetHouseholdId);
        return RecordAdjustment(state, adjustment) with { Sequence = state.Sequence + 1 };
    }

    private static TownLandHearingState RecordAdjustment(TownLandHearingState state, TownLandRightAdjustment adjustment)
    {
        var produced = state.Adjustments.SelectMany(a => a.ResultRights).Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
        var originals = state.OriginalRights.Concat(adjustment.PriorRights.Where(r => !produced.Contains(r.Id) &&
            !state.OriginalRights.Any(o => o.Id == r.Id))).OrderBy(r => r.Id, StringComparer.Ordinal).ToArray();
        return state with { OriginalRights = originals, Adjustments = state.Adjustments.Append(adjustment).ToArray() };
    }

    /// <summary>Original grant pieces for the existing Council receipt validator, with untouched live pieces.</summary>
    public static IReadOnlyList<HouseholdLandUseRight> OriginalGrantRights(TownLandHearingState state,
        IReadOnlyList<HouseholdLandUseRight> currentRights)
    {
        var touched = state.OriginalRights.Select(r => r.Id).Concat(state.Adjustments.SelectMany(a => a.ResultRights).Select(r => r.Id))
            .ToHashSet(StringComparer.Ordinal);
        return currentRights.Where(r => !touched.Contains(r.Id)).Concat(state.OriginalRights.Select(r => r.Right))
            .OrderBy(r => r.Id, StringComparer.Ordinal).ToArray();
    }

    public static IReadOnlyList<HouseholdLandUseRight> ApplyAdjustments(TownLandHearingState state,
        IReadOnlyList<HouseholdLandUseRight> originalRights)
    {
        var rights = originalRights.ToDictionary(r => r.Id, StringComparer.Ordinal);
        foreach (var adjustment in state.Adjustments)
        {
            foreach (var prior in adjustment.PriorRights)
                if (!rights.TryGetValue(prior.Id, out var current) || Version(current) != prior.Version || Version(prior.Right) != prior.Version)
                    throw new InvalidDataException("A land adjustment has a stale or invented prior right.");
            foreach (var prior in adjustment.PriorRights) rights.Remove(prior.Id);
            foreach (var result in adjustment.ResultRights)
                if (!rights.TryAdd(result.Id, result)) throw new InvalidDataException("A land adjustment duplicates a right identity.");
        }
        return rights.Values.OrderBy(r => r.Id, StringComparer.Ordinal).ToArray();
    }

    public static IReadOnlyList<TownLandRequestResolution> RequestResolutions(TownLandHearingState state,
        string caseId, string rulingId, IReadOnlyList<HouseholdLandUseRequest> requests)
    {
        var item = state.Cases.Single(c => c.Id == caseId);
        var ruling = item.Rulings.Single(r => r.Id == rulingId);
        var plot = item.Revisions.Single(r => r.Number == ruling.Revision).Tiles;
        return requests.Where(r => r.TownId == item.TownId && r.Status == "pending" && r.RequestedTick <= ruling.Tick)
            .OrderBy(r => r.Id, StringComparer.Ordinal).Select(r => new TownLandRequestResolution(r.Id, item.TownId, item.Id, ruling.Id,
                TownLandRightsRules.OrderTiles(r.Tiles.Except(r.HearingResolutions.SelectMany(receipt => receipt.Tiles)).Where(plot.Contains)), ruling.Tick))
            .Where(r => r.Tiles.Count > 0).ToArray();
    }

    internal static bool SameRights(IEnumerable<HouseholdLandUseRight> a, IEnumerable<HouseholdLandUseRight> b) =>
        a.OrderBy(r => r.Id, StringComparer.Ordinal).Select(Version).SequenceEqual(b.OrderBy(r => r.Id, StringComparer.Ordinal).Select(Version));

    public static TownLandHearingState RequestReopen(TownLandHearingState state, string caseId, string actor,
        string kind, IReadOnlyList<string> evidenceIds, string reasons, long tick)
    {
        var item = state.Cases.Single(c => c.Id == caseId && c.Status == "settled");
        if (kind is not ("material_evidence" or "procedural_error") || !ValidText(reasons) ||
            evidenceIds.Count == 0 || evidenceIds.Any(id => !item.Evidence.Any(e => e.Id == id)))
            throw new InvalidOperationException("Reopening requires identified material evidence or proof of a procedural error.");
        var request = new TownLandReopenRequest("land-reopen:" + item.TownId + ":" + (state.Sequence + 1).ToString(CultureInfo.InvariantCulture), actor, tick, kind, Ordered(evidenceIds), reasons);
        return Replace(state with { Sequence = state.Sequence + 1 }, item with { ReopenRequests = item.ReopenRequests.Append(request).ToArray() });
    }

    public static bool ReopeningPlotIsAvailable(TownLandHearingState state, TownLandCase item) =>
        !state.Cases.Any(other => other.Id != item.Id && other.TownId == item.TownId && other.Status == "pending" &&
            CurrentRevision(other).Tiles.Any(CurrentRevision(item).Tiles.Contains));

    public static TownLandHearingState Reopen(TownLandHearingState state, string caseId, string requestId,
        TownLandCaseJudge judge, bool groundsEstablished, string assessment, IReadOnlyList<HouseholdLandUseRight> currentRights,
        IReadOnlyList<TownLandCaseParty> currentParties, long tick, int day, string noticeId, bool validAuthority)
    {
        var item = state.Cases.Single(c => c.Id == caseId && c.Status == "settled");
        var request = item.ReopenRequests.Single(r => r.Id == requestId && r.Status == "pending");
        if (!validAuthority || item.Judge != judge || !ValidText(assessment) ||
            !item.Reads.Any(r => r.AgentId == judge.AgentId && r.Revision == CurrentRevision(item).Number && r.ReadTick >= request.Tick && r.ReadTick <= tick &&
                r.ReopenRequestIds.Contains(request.Id, StringComparer.Ordinal)))
            throw new InvalidOperationException("An authorized independent judge must inspect and assess the reopening grounds.");
        if (groundsEstablished && !(request.Kind == "material_evidence" ? MaterialNewEvidence(item, request, state) : DemonstratedProceduralError(item, request)))
            throw new InvalidOperationException("Disagreement or an unsupported allegation does not establish reopening grounds.");
        if (groundsEstablished && !ReopeningPlotIsAvailable(state, item))
            throw new InvalidOperationException("Another pending land hearing covers this plot; reopening waits for that hearing.");
        request = request with { Status = groundsEstablished ? "accepted" : "rejected", AssessedBy = judge, AssessedTick = tick, Assessment = assessment };
        item = item with { ReopenRequests = item.ReopenRequests.Select(r => r.Id == requestId ? request : r).ToArray() };
        if (groundsEstablished)
            item = item with
            {
                Status = "pending",
                SettledTick = null,
                Revisions = item.Revisions.Append(Revision(CurrentRevision(item).Number + 1,
                CurrentRevision(item).Tiles, currentRights, currentParties, noticeId, tick, day, CurrentRevision(item).RequestedOutcome)).ToArray()
            };
        else if (!item.ReopenRequests.Any(r => r.Status == "pending"))
            item = item with { Judge = null, JudgeHistory = item.JudgeHistory.Append(new(judge, tick, "reopening_rejected")).ToArray() };
        return Replace(state, item);
    }

    public static bool MaterialNewEvidence(TownLandCase item, TownLandReopenRequest request, TownLandHearingState? state = null)
    {
        if (request.Kind != "material_evidence" || AssessedRuling(item, request) is not { } ruling) return false;
        var knownFacts = item.Evidence.Where(e => e.SubmittedTick <= ruling.Tick).ToArray();
        var priorPermissions = item.Revisions.Single(r => r.Number == ruling.Revision).RightVersions;
        var cases = state?.Cases ?? [item];
        var courtPermissions = state?.Adjustments.Where(a => a.Kind == "ruling").SelectMany(a => a.ResultRights).ToArray() ?? [];
        return request.EvidenceIds.Any(id => item.Evidence.Any(e => e.Id == id && ValidEvidence(e) &&
            e.Kind is "record" or "observation" && e.SubmittedTick > ruling.Tick && e.SubmittedTick <= request.Tick &&
            !knownFacts.Any(old => old.Kind == e.Kind && FactText(old.Text) == FactText(e.Text)) &&
            (e.Kind == "observation" ||
                !knownFacts.Any(old => old.Kind == "record" && old.SourceRecordId == e.SourceRecordId && old.SourceVersion == e.SourceVersion) &&
                !priorPermissions.Any(p => p.Id == e.SourceRecordId && p.Version == e.SourceVersion) &&
                !cases.Any(c => c.Rulings.Any(r => r.Id == e.SourceRecordId)) &&
                !courtPermissions.Any(r => r.Id == e.SourceRecordId && Version(r) == e.SourceVersion) &&
                (state is not null || e.SourceRecordId is { } source &&
                    !source.StartsWith("land-ruling:", StringComparison.Ordinal) && !source.StartsWith("household-use:hearing:", StringComparison.Ordinal)))));
    }

    private static string FactText(string text) => string.Join(' ', new string(text.Normalize(NormalizationForm.FormC)
        .Select(c => char.IsWhiteSpace(c) || char.IsPunctuation(c) ? ' ' : char.ToUpperInvariant(c)).ToArray())
        .Split(' ', StringSplitOptions.RemoveEmptyEntries));

    public static bool DemonstratedProceduralError(TownLandCase item, TownLandReopenRequest request)
    {
        if (request.Kind != "procedural_error" || AssessedRuling(item, request) is not { } ruling) return false;
        var revision = item.Revisions.Single(r => r.Number == ruling.Revision);
        var parties = ruling.Parties.Count > 0 ? ruling.Parties : revision.Parties;
        var earlyWithoutResponses = ruling.Tick < revision.DeadlineTick && parties.Any(p => p.Kind == "household" ?
            p.AdultIds.Count == 0 || p.AdultIds.Any(id => !item.Responses.Any(r => r.Revision == revision.Number && r.PartyId == p.Id && r.AgentId == id && r.Tick <= ruling.Tick)) :
            p.RepresentativeId is null || !item.Responses.Any(r => r.Revision == revision.Number && r.PartyId == p.Id && r.AgentId == p.RepresentativeId && r.Tick <= ruling.Tick));
        return earlyWithoutResponses || JudgeConflict(ruling.Judge.AgentId, null, parties) ||
            !item.Reads.Any(r => r.AgentId == ruling.Judge.AgentId && r.Revision == revision.Number && r.ReadTick <= ruling.Tick &&
                ruling.EvidenceIds.All(r.EvidenceIds.Contains));
    }

    /// <summary>
    /// The ruling a request's grounds are judged against: the latest one when it is assessed. A request filed
    /// before that ruling was made cannot ground another rehearing, because the rehearing has already heard it.
    /// A rehearing the request itself opened is published at its assessment, so a ruling on that notice comes after it.
    /// </summary>
    private static TownLandRuling? AssessedRuling(TownLandCase item, TownLandReopenRequest request) =>
        item.Rulings.LastOrDefault(r => request.AssessedTick is not { } assessed || r.Tick < assessed ||
            r.Tick == assessed && item.Revisions.Single(v => v.Number == r.Revision).PublishedTick < assessed) is { } ruling &&
        ruling.Tick <= request.Tick ? ruling : null;

    private static TownLandCase Pending(TownLandHearingState state, string id) =>
        state.Cases.Single(c => c.Id == id && c.Status == "pending");
    private static TownLandCase Exact(TownLandHearingState state, string id, int revision)
    {
        var item = state.Cases.Single(c => c.Id == id);
        if (CurrentRevision(item).Number != revision) throw new InvalidOperationException("The case notice has changed.");
        return item;
    }
}

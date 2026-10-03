using System.Text.Json.Serialization;
using ClankerWorld.Simulation.Harness;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>A bounded permission decision; it never describes a physical or private-property action.</summary>
public sealed record TownLandRequestedOutcome(string Kind, string? HouseholdId = null, long? AgreedEndTick = null);

public sealed record TownLandFilingRequest(IReadOnlyList<GridPoint> Tiles,
    TownLandRequestedOutcome RequestedOutcome, string Statement);

public sealed record TownLandRequestResolution(string RequestId, string TownId, string CaseId,
    string RulingId, IReadOnlyList<GridPoint> Tiles, long Tick);

/// <summary>The exact public right inspected at a particular point in the permission history.</summary>
public sealed record TownLandRightVersion(string Id, string Version, HouseholdLandUseRight Right);

public sealed record TownLandCaseParty(string Id, string Kind, string? HouseholdId,
    string? TownId, IReadOnlyList<string> AdultIds, string? RepresentativeId = null);

/// <summary>Publication opens the response window; it does not give any agent knowledge.</summary>
public sealed record TownLandCaseRevision(int Number, IReadOnlyList<GridPoint> Tiles,
    IReadOnlyList<TownLandRightVersion> RightVersions, IReadOnlyList<TownLandCaseParty> Parties,
    string NoticeId, long PublishedTick, long DeadlineTick, TownLandRequestedOutcome RequestedOutcome);

public sealed record TownLandCaseFiling(string? AgentId, string Kind, string Text,
    TownLandRequestedOutcome RequestedOutcome, long Tick, string? AuthorityId = null);

/// <summary>Public case material, with its source retained independently of the compacted event log.</summary>
public sealed record TownLandEvidence(string Id, int Revision, string Kind, string Acquisition,
    string SourceAgentId, string? SourceRecordId, string? SourceVersion, long ObservedTick,
    string SubmittedByAgentId, long SubmittedTick, string Text);

public sealed record TownLandCaseResponse(int Revision, string PartyId, string AgentId,
    string Kind, string Text, long Tick);

public sealed record TownLandCaseRead(int Revision, string AgentId, long ReadTick,
    IReadOnlyList<string> EvidenceIds, string? SourceAgentId = null)
{
    public IReadOnlyList<string> ReopenRequestIds { get; init; } = [];
}

/// <summary>A case assignment, distinct from the Town's general elected offices.</summary>
public sealed record TownLandCaseJudge(string AgentId, string Kind, string AuthorityId, long AssignedTick);
public sealed record TownLandCaseJudgeTerm(TownLandCaseJudge Judge, long EndedTick, string Reason);
public sealed record TownLandCaseJudgeConsent(string AgentId, long Tick, long? WithdrawnTick = null);

/// <summary>Uses the mayoral ballot and round contracts without installing a general office.</summary>
public sealed record TownLandCaseJudgeContest(string Id, string Stage, int Round,
    long OpenedTick, long? RoundOpenedTick, long? RoundDeadlineTick,
    IReadOnlyList<string> Voters, IReadOnlyList<string> Candidates,
    IReadOnlyList<TownMayoralBallot> Ballots, IReadOnlyList<string> TiedCandidates,
    int Interruptions, IReadOnlyList<TownMayoralRound> Rounds,
    string? WinnerId = null, long? SettledTick = null, string? Reason = null);

public sealed record TownLandRuling(string Id, int Revision, TownLandCaseJudge Judge,
    long Tick, TownLandRequestedOutcome Outcome, IReadOnlyList<string> EvidenceIds,
    IReadOnlyList<string> LawIds, string Reasons, IReadOnlyList<string> AdjustmentIds)
{
    public IReadOnlyList<TownLandCaseParty> Parties { get; init; } = [];
}

/// <summary>Both refused grounds and an accepted reopening remain in the case file.</summary>
public sealed record TownLandReopenRequest(string Id, string AgentId, long Tick,
    string Kind, IReadOnlyList<string> EvidenceIds, string Reasons,
    string Status = "pending", TownLandCaseJudge? AssessedBy = null,
    long? AssessedTick = null, string? Assessment = null);

public sealed record TownLandCase(string Id, string Key, string Kind, long FiledTick,
    string Status, IReadOnlyList<TownLandCaseRevision> Revisions,
    IReadOnlyList<TownLandCaseFiling> Filings, IReadOnlyList<TownLandEvidence> Evidence,
    IReadOnlyList<TownLandCaseResponse> Responses, IReadOnlyList<TownLandRuling> Rulings,
    TownLandCaseJudge? Judge, IReadOnlyList<TownLandCaseJudgeTerm> JudgeHistory,
    IReadOnlyList<TownLandCaseJudgeConsent> JudgeConsents, TownLandCaseJudgeContest? Contest,
    IReadOnlyList<TownLandCaseJudgeContest> ContestHistory,
    IReadOnlyList<TownLandReopenRequest> ReopenRequests, long? SettledTick = null)
{
    public string TownId { get; init; } = "";
    public IReadOnlyList<string> DirectStakeIds { get; init; } = [];
    public IReadOnlyList<TownLandCaseRead> Reads { get; init; } = [];
}

/// <summary>A replayable bounded permission change, or an already authorized building-footprint reassignment.</summary>
public sealed record TownLandRightAdjustment(string Id, string Kind, long Tick,
    IReadOnlyList<TownLandRightVersion> PriorRights, IReadOnlyList<HouseholdLandUseRight> ResultRights,
    IReadOnlyList<GridPoint> Tiles, string? CaseId = null, string? RulingId = null,
    string? BuildingId = null, string? TargetHouseholdId = null, string? TransferId = null);

/// <summary>
/// OriginalRights retains first-touched original grant pieces; requests retain their original Council receipts.
/// Adjustments replay from those pieces to the current rights, including later legitimate building transfers.
/// </summary>
public sealed record TownLandHearingState(long Sequence, IReadOnlyList<TownLandCase> Cases,
    IReadOnlyList<TownLandRightVersion> OriginalRights, IReadOnlyList<TownLandRightAdjustment> Adjustments)
{
    [JsonRequired]
    public IReadOnlyList<TownLandTransferRequest> Transfers { get; init; } = [];
    public static TownLandHearingState Create() => new(0, [], [], []);
    public static TownLandHearingState Empty { get; } = Create();
}

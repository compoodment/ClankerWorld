using ClankerWorld.Simulation.Harness;
using System.Text.Json.Serialization;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>A reported act remains an allegation, separately from the world's physical records.</summary>
public sealed record TownViolationAllegation(string IncidentId, string SubjectId, string ConductKind,
    GridPoint Position, long ConductTick, string LawId, int LawVersion, string Statement,
    IReadOnlyList<string> SourceEvidenceIds);
public sealed record TownViolationFiling(string AgentId, string Kind, long Tick, string Statement,
    IReadOnlyList<string> EvidenceIds, string? AuthorityId = null);
public sealed record TownViolationFilingRequest(TownViolationAllegation Allegation, string Statement);

/// <summary>A person's response rights; household membership records conflict, never collective liability.</summary>
public sealed record TownCaseParty(string Id, string Role, string SubjectId, string? RespondingAdultId,
    string? CareRelationshipId, int? CareRevision, string? HouseholdId);
public sealed record TownCaseEvidence(string Id, int Revision, string Kind, string Acquisition,
    string SourceAgentId, string? SourceRecordId, string? SourceVersion, long ObservedTick,
    string SubmittedByAgentId, long SubmittedTick, string Text);
public sealed record TownCaseRead(int Revision, string AgentId, long ReadTick, IReadOnlyList<string> EvidenceIds,
    IReadOnlyList<string> ReopenRequestIds, string? SourceAgentId = null)
{
    [JsonRequired] public IReadOnlyList<string> ResponseIds { get; init; } = [];
    [JsonRequired] public IReadOnlyList<string> FindingIds { get; init; } = [];
}
public sealed record TownCaseResponse(int Revision, string PartyId, string AgentId, string RepresentedAgentId,
    string Kind, string Text, long Tick);
public sealed record TownCaseRevision(int Number, IReadOnlyList<TownCaseParty> Parties, string NoticeId,
    long PublishedTick, long DeadlineTick);
public sealed record TownCaseJudge(string AgentId, string Kind, string AuthorityId, long AssignedTick);
public sealed record TownCaseJudgeTerm(TownCaseJudge Judge, long EndedTick, string Reason);
public sealed record TownCaseJudgeConsent(string AgentId, long Tick, long? WithdrawnTick = null);
public sealed record TownCaseJudgeContest(string Id, string Stage, int Round, long OpenedTick,
    long? RoundOpenedTick, long? RoundDeadlineTick, IReadOnlyList<string> Voters, IReadOnlyList<string> Candidates,
    IReadOnlyList<TownMayoralBallot> Ballots, IReadOnlyList<string> TiedCandidates, int Interruptions,
    IReadOnlyList<TownMayoralRound> Rounds, string? WinnerId = null, long? SettledTick = null, string? Reason = null);
public sealed record TownCaseReopenRequest(string Id, string AgentId, long Tick, string Kind,
    IReadOnlyList<string> EvidenceIds, string Reasons, string Status = "pending", TownCaseJudge? AssessedBy = null,
    long? AssessedTick = null, string? Assessment = null);

/// <summary>A civil assessment does not replace the facts in the engine or command physical punishment.</summary>
public sealed record TownViolationFinding(string Id, int Revision, TownCaseJudge Judge, long Tick,
    string Result, string Standard, IReadOnlyList<string> EvidenceIds, string Reasons, string Uncertainty,
    string Consequence, IReadOnlyList<TownCaseParty> Parties);
public sealed record TownViolationCase(string Id, string Key, string TownId, TownViolationAllegation Allegation,
    long FiledTick, string Status, IReadOnlyList<TownCaseRevision> Revisions, IReadOnlyList<TownViolationFiling> Filings,
    IReadOnlyList<TownCaseEvidence> Evidence, IReadOnlyList<TownCaseRead> Reads, IReadOnlyList<TownCaseResponse> Responses,
    IReadOnlyList<TownViolationFinding> Findings, TownCaseJudge? Judge, IReadOnlyList<TownCaseJudgeTerm> JudgeHistory,
    IReadOnlyList<TownCaseJudgeConsent> JudgeConsents, TownCaseJudgeContest? Contest,
    IReadOnlyList<TownCaseJudgeContest> ContestHistory, IReadOnlyList<TownCaseReopenRequest> ReopenRequests,
    IReadOnlyList<string> DirectStakeIds, long? SettledTick = null);

/// <summary>Finite named goods or existing physical work, contributed only with personal consent.</summary>
public sealed record TownRemedyTerm(string Id, string Kind, string ContributorId, string? BeneficiaryId,
    string? ItemKind, int Quantity, string? TargetId);
public sealed record TownRemedyResponse(string AgentId, int Revision, string TermsHash, string Kind, long Tick,
    string NoticeId, string? Reason = null);
public sealed record TownRemedyOffer(string Id, string CaseId, string FindingId, int Revision,
    IReadOnlyList<TownRemedyTerm> Terms, string TermsHash, string Reason, string NoticeId, long PublishedTick,
    long ResponseDeadlineTick, long CompletionTicks, string Status, IReadOnlyList<TownRemedyResponse> Responses,
    string? ReplacesOfferId = null, string? AgreementId = null);
public sealed record TownRestorativeAgreement(string Id, string OfferId, int OfferRevision, string TermsHash,
    IReadOnlyList<TownRemedyTerm> Terms, IReadOnlyList<TownRemedyResponse> Consents, long AcceptedTick,
    long DeadlineTick, string Status, string? ReplacesAgreementId = null);

/// <summary>A receipt for actual lawful work or delivery, retained after events or used goods disappear.</summary>
public sealed record TownRemedyEffect(string Id, string AgreementId, string TermId, string ActorId, long Tick,
    string Kind, string? BeneficiaryId, string? ItemKind, int Quantity, string? TargetId,
    string NativeReceiptId, string NativeReceiptVersion);
public sealed partial record TownNonviolentState(long Sequence, IReadOnlyList<TownViolationCase> Cases,
    IReadOnlyList<TownRemedyOffer> Offers, IReadOnlyList<TownRestorativeAgreement> Agreements,
    IReadOnlyList<TownRemedyEffect> Effects)
{
    public static TownNonviolentState Create() => new(0, [], [], [], []);
}

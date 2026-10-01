namespace ClankerWorld.Simulation.Playtest;

public sealed record TownLaw(string Key, string Text, string ProposerId, long AdoptedTick);
public sealed record TownLawBallot(string Key, string Text, string ProposerId, string? FoodPolicy, bool Repeal,
    long ProposedTick, long ExpiryTick, IReadOnlyList<string> Electorate,
    IReadOnlyList<string> Approvals, IReadOnlyList<string> Rejections)
{
    public string GoverningForm { get; init; } = "collective";
}
public sealed record TownElectionVote(string VoterId, IReadOnlyList<string> CandidateIds);
public sealed record TownCouncilCandidacy(string CandidateId, long DeclaredTick)
{
    public bool FullTermWilling { get; init; } = true;
    public bool ReplacementWilling { get; init; }
    public long? ReplacementTermStartedTick { get; init; }
    public long? ReplacementTermExpiryTick { get; init; }
    public long? ReplacementDeclaredTick { get; init; }
}
public sealed record TownProposalCircumstances(string GoverningForm, IReadOnlyList<string> MemberIds,
    long? SameLawAdoptedTick, int? AdultPopulation, int? CommunalFoodQuantity);
public sealed record TownProposalOutcome(string RequestKey, long ClosedTick, TownProposalCircumstances Circumstances);
public sealed record TownElectionOutcome(long StartedTick, long ResolvedTick, string Kind,
    IReadOnlyList<string> SupportedCandidateIds, IReadOnlyList<string> SelectedMemberIds,
    IReadOnlyList<string> DrawnMemberIds)
{
    public IReadOnlyList<TownElectionVote> MainVotes { get; init; } = [];
    public IReadOnlyList<string> MainCandidateIds { get; init; } = [];
    public IReadOnlyList<string> MainElectorate { get; init; } = [];
    public IReadOnlyList<string> DrawSlate { get; init; } = [];
    public IReadOnlyList<string> RetainedMemberIds { get; init; } = [];
    public IReadOnlyList<TownElectionVote> RunoffVotes { get; init; } = [];
    public IReadOnlyList<string> RunoffCandidateIds { get; init; } = [];
    public IReadOnlyList<string> RunoffElectorate { get; init; } = [];
    public IReadOnlyList<string> BeforeRunoffSelectedIds { get; init; } = [];
    public int MainAvailableSeats { get; init; } = 3;
    public IReadOnlyList<string> OriginalMainSelectedMemberIds { get; init; } = [];
    public IReadOnlyList<string> OriginalMainCutoffCandidateIds { get; init; } = [];
    public IReadOnlyList<string> OriginalMainRetainedMemberIds { get; init; } = [];
    public IReadOnlyList<string> AdultIdsAtResolution { get; init; } = [];
    public IReadOnlyList<TownCouncilCandidacy> RegisterAtResolution { get; init; } = [];
}
public sealed record TownCouncilElection(long StartedTick, long ExpiryTick, IReadOnlyList<string> Electorate,
    IReadOnlyList<string> Candidates, IReadOnlyList<TownElectionVote> Votes)
{
    public bool IsRunoff { get; init; }
    public int AvailableSeats { get; init; } = 3;
    public IReadOnlyList<string> SelectedMemberIds { get; init; } = [];
    public IReadOnlyList<TownCouncilCandidacy> Candidacies { get; init; } = [];
    public string Kind { get; init; } = "initial";
    public long TargetTermStartedTick { get; init; }
    public long TargetTermExpiryTick { get; init; }
    public IReadOnlyList<string> RetainedMemberIds { get; init; } = [];
    public IReadOnlyList<TownElectionVote> MainVotes { get; init; } = [];
    public IReadOnlyList<string> MainCandidateIds { get; init; } = [];
    public IReadOnlyList<string> MainElectorate { get; init; } = [];
    public IReadOnlyList<string> MainSupportedCandidateIds { get; init; } = [];
    public int MainAvailableSeats { get; init; } = 3;
    public IReadOnlyList<string> OriginalMainSelectedMemberIds { get; init; } = [];
    public IReadOnlyList<string> OriginalMainCutoffCandidateIds { get; init; } = [];
    public IReadOnlyList<string> OriginalMainRetainedMemberIds { get; init; } = [];
}
public sealed record TownCouncilState(string TownId, string FoodPolicy, long LastResolutionTick,
    IReadOnlyList<string> MemberIds, long? TermStartedTick = null, long? TermExpiryTick = null,
    TownCouncilElection? Election = null, TownLawBallot? Ballot = null, IReadOnlyList<TownLaw>? Laws = null)
{
    public string GoverningForm { get; init; } = "collective";
    public string? FallbackReason { get; init; } = "population";
    public IReadOnlyList<TownCouncilCandidacy> CandidateRegister { get; init; } = [];
    public long? ElectionRetryAfterTick { get; init; }
    public TownElectionOutcome? LastElectionOutcome { get; init; }
    public IReadOnlyList<TownProposalOutcome> ProposalOutcomes { get; init; } = [];
}
public sealed record CivicActionResult(bool Applied, string? Failure = null);

namespace ClankerWorld.Simulation.Playtest;

public sealed record TownLaw(string Key, string Text, string ProposerId, long AdoptedTick);
public sealed record TownLawBallot(string Key, string Text, string ProposerId, string? FoodPolicy, bool Repeal,
    long ProposedTick, long ExpiryTick, IReadOnlyList<string> Electorate,
    IReadOnlyList<string> Approvals, IReadOnlyList<string> Rejections);
public sealed record TownElectionVote(string VoterId, IReadOnlyList<string> CandidateIds);
public sealed record TownCouncilCandidacy(string CandidateId, long DeclaredTick);
public sealed record TownCouncilElection(long StartedTick, long ExpiryTick, IReadOnlyList<string> Electorate,
    IReadOnlyList<string> Candidates, IReadOnlyList<TownElectionVote> Votes)
{
    public bool IsRunoff { get; init; }
    public int AvailableSeats { get; init; } = 3;
    public IReadOnlyList<string> SelectedMemberIds { get; init; } = [];
    public IReadOnlyList<TownCouncilCandidacy> Candidacies { get; init; } = [];
}
public sealed record TownCouncilState(string TownId, string FoodPolicy, long LastResolutionTick,
    IReadOnlyList<string> MemberIds, long? TermStartedTick = null, long? TermExpiryTick = null,
    TownCouncilElection? Election = null, TownLawBallot? Ballot = null, IReadOnlyList<TownLaw>? Laws = null);
public sealed record CivicActionResult(bool Applied, string? Failure = null);

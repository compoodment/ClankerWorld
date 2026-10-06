using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Harness;

namespace ClankerWorld.Simulation.Playtest;

public sealed record TownCandidateAgreement(string AgentId, bool FullTerm, long? RemainderTermEndTick);
public sealed record TownProposalVote(string AgentId, bool Yes);
public sealed record TownProposal(string Id, string RequestKey, string Kind, string AuthorId, string? SubjectId,
    string Text, string Circumstances, long CouncilRevision, long OpenedTick, long DeadlineTick,
    IReadOnlyList<string> Voters, int RequiredYes, IReadOnlyList<TownProposalVote> Votes,
    string Status = "pending", long? SettledTick = null, IReadOnlyList<GridPoint>? LandClaimTiles = null,
    TownLandFilingRequest? LandHearingRequest = null, TownViolationFilingRequest? NonviolentRequest = null)
{
    public TownProjectPayload? Project { get; init; }
}
public sealed record TownElectionBallot(string AgentId, IReadOnlyList<string> Choices);
public sealed record TownElection(string Id, string Kind, string Stage, long OpenedTick, long DeadlineTick,
    long TermEndTick, int Seats, IReadOnlyList<string> Voters, IReadOnlyList<string> Candidates,
    IReadOnlyList<TownElectionBallot> Ballots, IReadOnlyList<string> Supported,
    IReadOnlyList<string> SettledSeats, IReadOnlyList<string> DrawOrder);
public sealed record TownCivicNotice(string Id, string Kind, string SubjectId, string Text, long PostedTick);
public sealed record TownCivicReceipt(string AgentId, string NoticeId, long LearnedTick, string? SourceAgentId = null);
public sealed record TownGovernanceState(string Form, string Fallback, long Revision, IReadOnlyList<string> Members,
    long? TermEndTick, long RetryTick, string RetryCircumstances, long Sequence,
    IReadOnlyList<TownCandidateAgreement> Candidates, IReadOnlyList<TownProposal> Proposals,
    TownElection? Election, IReadOnlyList<TownElection> ElectionHistory,
    IReadOnlyList<TownCivicNotice> Notices, IReadOnlyList<TownCivicReceipt> Knowledge)
{
    public static TownGovernanceState Create(IEnumerable<string> adults) =>
        new("all_adult", "initial", 0, TownGovernanceRules.Ordered(adults), null, 0, "", 0,
            [], [], null, [], [], []);
}

/// <summary>One Town's ordinary civic rules. The world validates and executes approved land claims separately.</summary>
public static class TownGovernanceRules
{
    public const int RepresentationThreshold = 8;
    public const int Seats = 3;
    public const int TermDays = 10;
    public const int MaximumProposalText = 256;

    internal static string[] Ordered(IEnumerable<string> ids) => ids.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    private static bool Has(IEnumerable<string> ids, string id) => ids.Contains(id, StringComparer.Ordinal);
    private static string Circumstances(TownGovernanceState state, string[] adults) =>
        $"{adults.Length}|{state.Candidates.Count(c => c.FullTerm)}|{state.Candidates.Count(c => c.FullTerm || c.RemainderTermEndTick == state.TermEndTick && c.RemainderTermEndTick is not null)}";

    private static bool MateriallyImproved(TownGovernanceState state, string[] adults)
    {
        var previous = state.RetryCircumstances.Split('|');
        if (previous.Length != 3) return false;
        var current = Circumstances(state, adults).Split('|');
        return Enumerable.Range(0, 3).Any(i => int.TryParse(previous[i], out var before) && int.Parse(current[i], System.Globalization.CultureInfo.InvariantCulture) > before);
    }

    public static TownGovernanceState Advance(TownGovernanceState state, string townId, string seed,
        IEnumerable<string> adultResidents, long tick, int ticksPerDay,
        bool allowNewElections = true, bool forceRepresentation = false, bool deferFullHandover = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ticksPerDay);
        var adults = Ordered(adultResidents);
        var candidates = state.Candidates.Where(c => Has(adults, c.AgentId)).ToArray();
        state = state with { Candidates = candidates };
        if ((state.Form == "representative" || state.Fallback == "candidates") && adults.Length <= Seats)
        {
            state = CancelElection(state, tick, "Population fell to three or fewer adults.");
            state = ChangeCouncil(state, adults, "all_adult", "demographic", null, tick);
        }
        else if (state.Form == "all_adult")
            state = ChangeCouncil(state, adults, "all_adult", state.Fallback, state.TermEndTick, tick);
        else
            state = ChangeCouncil(state, state.Members.Where(id => Has(adults, id)).ToArray(),
                state.Form, state.Fallback, state.TermEndTick, tick);

        if (state.Election is { } contest)
        {
            var eligible = contest.Candidates.Where(id => Has(adults, id) && Agreed(state, id, contest.Kind, contest.TermEndTick)).ToArray();
            var voters = contest.Voters.Where(id => Has(adults, id)).ToArray();
            contest = contest with
            {
                Candidates = eligible,
                Voters = voters,
                Ballots = contest.Ballots.Where(b => Has(voters, b.AgentId))
                    .Select(b => b with { Choices = b.Choices.Where(id => Has(eligible, id)).ToArray() }).ToArray(),
                Supported = contest.Supported.Where(id => Has(adults, id) && Agreed(state, id, contest.Kind, contest.TermEndTick)).ToArray(),
                SettledSeats = contest.SettledSeats.Where(id => Has(adults, id) && Agreed(state, id, contest.Kind, contest.TermEndTick)).ToArray(),
            };
            var lostSettledSeat = contest.Kind != "replacement" && contest.SettledSeats.Count < state.Election!.SettledSeats.Count;
            state = state with { Election = contest };
            if (lostSettledSeat) state = FailElection(state, adults, tick, ticksPerDay);
        }

        // A regular full contest has priority over an unresolved vacancy contest.
        if (state.TermEndTick is { } termEnd && tick >= termEnd - ticksPerDay &&
            state.Form == "representative" && state.Election?.Kind == "replacement")
        {
            state = CancelElection(state, tick, "Regular voting superseded the unresolved replacement round.");
            state = OpenElection(state, townId, adults, tick, ticksPerDay, "regular", termEnd + (long)TermDays * ticksPerDay);
        }
        if (state.Election is { Stage: "main" or "runoff" } closing && tick >= closing.DeadlineTick)
            state = CloseRound(state, townId, seed, adults, tick, ticksPerDay);
        if (!deferFullHandover && state.Election is { Stage: "ready" } ready && (ready.Kind != "regular" ||
            state.TermEndTick is null || tick >= state.TermEndTick))
            state = ready.SettledSeats.Count == Seats
                ? SeatFullCouncil(state, ready, tick, ticksPerDay)
                : FailElection(state, adults, tick, ticksPerDay);

        if (state.Form == "representative" && state.TermEndTick is { } ended && tick >= ended &&
            state.Election is not { Stage: "runoff" or "ready" })
            state = ChangeCouncil(state, adults, "all_adult", "candidates", null, tick);

        var canRetry = tick >= state.RetryTick || MateriallyImproved(state, adults);
        if (state.Election is null && canRetry)
        {
            if (state.Form == "representative" && state.TermEndTick is { } end && tick >= end - ticksPerDay)
                state = OpenElection(state, townId, adults, tick, ticksPerDay, "regular", end + (long)TermDays * ticksPerDay);
            else if (allowNewElections && state.Form == "representative" && state.Members.Count < Seats && state.TermEndTick is { } remainder)
                state = OpenElection(state, townId, adults, tick, ticksPerDay, "replacement", remainder);
            else if (allowNewElections && (forceRepresentation && state.Form != "representative" && adults.Length > Seats ||
                state.Form == "all_adult" && (adults.Length >= RepresentationThreshold || state.Fallback == "candidates" && adults.Length > Seats)))
                state = OpenElection(state, townId, adults, tick, ticksPerDay, "initial", tick + (long)(TermDays + 1) * ticksPerDay);
        }
        foreach (var proposal in state.Proposals.Where(p => p.Status == "pending").ToArray())
            state = ResolveProposal(state, proposal.Id, tick);
        return state;
    }

    internal static TownGovernanceState ChangeCouncil(TownGovernanceState state, IReadOnlyList<string> members,
        string form, string fallback, long? termEnd, long tick)
    {
        var ordered = Ordered(members);
        if (state.Form == form && state.Members.SequenceEqual(ordered))
            return state with { Fallback = fallback, TermEndTick = termEnd };
        state = state with
        {
            Form = form,
            Fallback = fallback,
            Members = ordered,
            TermEndTick = termEnd,
            Revision = state.Revision + 1,
            Proposals = state.Proposals.Select(p => p.Status == "pending"
                ? p with { Status = "cancelled", SettledTick = tick } : p).ToArray(),
        };
        return Notice(state, "council", "council:" + state.Revision,
            form == "leader" ? "The elected governing leader now makes ordinary decisions: " + string.Join(", ", ordered)
                : form == "all_adult" ? "Every recorded living adult resident now sits on the Town council."
                : "The Town's current representatives are: " + string.Join(", ", ordered), tick);
    }

    public static TownGovernanceState Nominate(TownGovernanceState state, string actor, string nominee,
        IEnumerable<string> adults, long tick)
    {
        if (actor == nominee || !Has(adults, actor) || !Has(adults, nominee))
            throw new InvalidOperationException("Only adult residents may nominate another eligible adult resident.");
        // Naming someone records a request for their agreement, never agreement on their behalf.
        return Notice(state, "nomination", "nomination:" + actor + "->" + nominee,
            $"{actor} nominated {nominee} for full council terms. The nominee must personally agree before entering the willing-candidate register.", tick);
    }

    public static TownGovernanceState Register(TownGovernanceState state, string actor, bool fullTerm,
        long? remainderEnd, IEnumerable<string> adults, long tick)
    {
        if (!Has(adults, actor) || !fullTerm && (remainderEnd is null || remainderEnd != state.TermEndTick))
            throw new InvalidOperationException("The candidate must personally agree to an eligible full or remainder term.");
        var existing = state.Candidates.SingleOrDefault(c => c.AgentId == actor);
        var agreement = new TownCandidateAgreement(actor, fullTerm || existing?.FullTerm == true,
            remainderEnd ?? existing?.RemainderTermEndTick);
        state = state with { Candidates = state.Candidates.Where(c => c.AgentId != actor).Append(agreement).OrderBy(c => c.AgentId, StringComparer.Ordinal).ToArray() };
        return Notice(state, "candidate", actor, $"{actor} personally agreed to stand for " +
            (fullTerm ? "full council terms." : $"the vacancy ending at tick {remainderEnd}."), tick);
    }

    public static TownGovernanceState WithdrawCandidate(TownGovernanceState state, string actor, long tick)
    {
        state = state with { Candidates = state.Candidates.Where(c => c.AgentId != actor).ToArray() };
        if (state.Election is { } election)
            state = state with
            {
                Election = election with
                {
                    Candidates = election.Candidates.Where(id => id != actor).ToArray(),
                    Supported = election.Supported.Where(id => id != actor).ToArray(),
                    Ballots = election.Ballots.Select(b => b with { Choices = b.Choices.Where(id => id != actor).ToArray() }).ToArray(),
                }
            };
        return Notice(state, "candidate", actor, $"{actor} withdrew their candidacy; other ballot choices remain.", tick);
    }

    private static bool Agreed(TownGovernanceState state, string id, string kind, long end) =>
        state.Candidates.Any(c => c.AgentId == id && (c.FullTerm || kind == "replacement" && c.RemainderTermEndTick == end));

    private static TownGovernanceState OpenElection(TownGovernanceState state, string townId, string[] adults,
        long tick, int day, string kind, long termEnd)
    {
        var seats = kind == "replacement" ? Seats - state.Members.Count : Seats;
        var candidates = adults.Where(id => Agreed(state, id, kind, termEnd) &&
            (kind != "replacement" || !Has(state.Members, id))).ToArray();
        var id = townId + ":election:" + (state.Sequence + 1);
        state = state with
        {
            Sequence = state.Sequence + 1,
            Election = new(id, kind, "main", tick, tick + day, termEnd, seats, adults, candidates, [], [], [], [])
        };
        if (candidates.Length < seats)
            return FailElection(state, adults, tick, day);
        return Notice(state, "election", id, $"{kind} council election: choose up to {seats} distinct willing candidates " +
            $"({string.Join(", ", candidates)}). Voting closes at tick {tick + day}; ballots may change.", tick);
    }

    public static TownGovernanceState VoteElection(TownGovernanceState state, string contestId, string actor,
        IReadOnlyList<string> choices, long tick)
    {
        if (state.Election is not { Stage: "main" or "runoff" } election || election.Id != contestId ||
            tick >= election.DeadlineTick || !Has(election.Voters, actor) || choices.Count > election.Seats ||
            choices.Distinct(StringComparer.Ordinal).Count() != choices.Count || choices.Any(id => !Has(election.Candidates, id)))
            throw new InvalidOperationException("This ballot is stale or contains ineligible choices.");
        return state with
        {
            Election = election with
            {
                Ballots = election.Ballots.Where(b => b.AgentId != actor).Append(new(actor, Ordered(choices))).OrderBy(b => b.AgentId, StringComparer.Ordinal).ToArray(),
            }
        };
    }

    private static TownGovernanceState CloseRound(TownGovernanceState state, string townId, string seed,
        string[] adults, long tick, int day)
    {
        var election = state.Election!;
        var totals = election.Candidates.ToDictionary(id => id,
            id => election.Ballots.Count(b => Has(b.Choices, id)), StringComparer.Ordinal);
        var eligible = election.Stage == "main" ? election.Candidates.Where(id => totals[id] > 0).ToArray()
            : election.Candidates.Where(id => Has(election.Supported, id)).ToArray();
        if (election.Stage == "main") election = election with { Supported = eligible };
        if (eligible.Length < election.Seats)
            return FailElection(state with { Election = election }, adults, tick, day);
        var ranked = eligible.OrderByDescending(id => totals[id]).ThenBy(id => id, StringComparer.Ordinal).ToArray();
        var cutoff = totals[ranked[election.Seats - 1]];
        var definite = ranked.Where(id => totals[id] > cutoff).ToArray();
        var tied = ranked.Where(id => totals[id] == cutoff).ToArray();
        var unresolved = election.Seats - definite.Length;
        if (tied.Length == unresolved)
            return FinishSeats(state, election, definite.Concat(tied).ToArray(), tick);
        if (election.Stage == "main")
        {
            if (election.Kind == "replacement" && definite.Length > 0)
                state = ChangeCouncil(state, state.Members.Concat(definite).ToArray(), "representative", "none", state.TermEndTick, tick);
            election = election with
            {
                Stage = "runoff",
                OpenedTick = tick,
                DeadlineTick = tick + day,
                Seats = unresolved,
                Voters = adults,
                Candidates = tied,
                Ballots = [],
                SettledSeats = election.SettledSeats.Concat(definite).ToArray()
            };
            state = state with { Election = election };
            return Notice(state, "runoff", election.Id, $"Cutoff runoff: choose up to {unresolved} of {string.Join(", ", tied)}. " +
                $"Closes at tick {tick + day}; unresolved ties then use a recorded fair draw.", tick);
        }
        // Fisher-Yates with rejection sampling avoids modulo bias. The saved order is authoritative after this draw.
        var random = Pcg32XshRrV1.Create(seed, $"town-council-draw/{townId}/{election.Id}");
        var draw = tied.ToArray();
        for (var i = draw.Length - 1; i > 0; i--)
        {
            var bound = (uint)(i + 1);
            var threshold = unchecked(0u - bound) % bound;
            uint value;
            do { value = random.NextUInt(); } while (value < threshold);
            var index = (int)(value % bound);
            (draw[i], draw[index]) = (draw[index], draw[i]);
        }
        election = election with { DrawOrder = draw };
        return FinishSeats(state, election, definite.Concat(draw.Take(unresolved)).ToArray(), tick);
    }

    private static TownGovernanceState FinishSeats(TownGovernanceState state, TownElection election,
        string[] winners, long tick)
    {
        election = election with { SettledSeats = Ordered(election.SettledSeats.Concat(winners)), Stage = "ready" };
        state = state with { Election = election };
        if (election.Kind == "replacement")
        {
            state = ChangeCouncil(state, state.Members.Concat(winners).ToArray(), "representative", "none", state.TermEndTick, tick);
            return ArchiveElection(state, election with { Stage = "completed" }, tick);
        }
        return Notice(state, "result", election.Id, "Chosen representatives: " + string.Join(", ", election.SettledSeats) +
            ". They take office when the current term ends, or immediately for the first council.", tick);
    }

    internal static TownGovernanceState SeatFullCouncil(TownGovernanceState state, TownElection election, long tick, int day)
    {
        var termEnd = tick + (long)TermDays * day;
        state = ChangeCouncil(state, election.SettledSeats, "representative", "none", termEnd, tick);
        return ArchiveElection(state, election with { Stage = "completed", TermEndTick = termEnd }, tick);
    }

    private static TownGovernanceState FailElection(TownGovernanceState state, string[] adults, long tick, int day)
    {
        var election = state.Election!;
        state = ArchiveElection(state, election with { Stage = "failed" }, tick) with
        { RetryTick = tick + day, RetryCircumstances = Circumstances(state, adults) };
        // An elected leader keeps ordinary authority until a handover completes.
        if (state.Form != "leader" && (election.Kind == "replacement" || state.Form != "representative" || state.TermEndTick <= tick))
            state = ChangeCouncil(state, adults, "all_adult", "candidates", null, tick);
        return state;
    }

    internal static TownGovernanceState CancelElection(TownGovernanceState state, long tick, string reason)
    {
        if (state.Election is not { } election) return state;
        return ArchiveElection(state, election with { Stage = "cancelled" }, tick, reason);
    }

    private static TownGovernanceState ArchiveElection(TownGovernanceState state, TownElection election, long tick, string? reason = null) =>
        Notice(state with { Election = null, ElectionHistory = state.ElectionHistory.Append(election).ToArray() },
            election.Stage == "cancelled" ? "cancelled" : "result", election.Id,
            $"Council election {election.Stage}: {string.Join(", ", election.SettledSeats)}." +
                (reason is null ? string.Empty : " " + reason), tick);

    public static TownGovernanceState SubmitProposal(TownGovernanceState state, string townId, string actor,
        string kind, string? subject, string text, string circumstances, IEnumerable<string> adults, long tick, int day,
        string? requestKey = null, string? noticeText = null, IReadOnlyList<GridPoint>? landClaimTiles = null,
        HouseholdLandUseRequest? landUseRequest = null, TownLandFilingRequest? landHearingRequest = null, TownProjectPayload? project = null,
        TownViolationFilingRequest? nonviolentRequest = null)
    {
        if (kind == "project")
        {
            TownProjectRules.ValidatePayload(project);
            text = TownProjectRules.ProposalText(project!);
        }
        if (kind is not ("law" or "admission" or "land_claim" or "land_use" or "land_hearing" or "project" or "law_case") || text.Trim().Length is < 1 or > MaximumProposalText || text.Any(char.IsControl) ||
            kind is "law" or "land_claim" or "land_use" or "land_hearing" or "law_case" && !Has(adults, actor) || kind == "admission" && actor != subject && !Has(adults, actor) ||
            kind == "land_use" && (landUseRequest is null || subject != landUseRequest.Id || townId != landUseRequest.TownId) ||
            kind != "land_use" && landUseRequest is not null ||
            kind == "land_claim" && (landClaimTiles is not { Count: > 0 } || subject is not null) ||
            kind != "land_claim" && landClaimTiles is not null ||
            kind == "land_hearing" && (subject is not null || !TownLandGovernmentFilingRules.IsValid(landHearingRequest, tick)) ||
            kind != "land_hearing" && landHearingRequest is not null ||
            kind == "law_case" && (subject is not null || !TownNonviolentGovernmentFilingRules.IsValid(nonviolentRequest, tick)) ||
            kind != "law_case" && nonviolentRequest is not null)
            throw new InvalidOperationException("Only an adult resident or the newcomer requesting admission may submit this proposal.");
        if (kind == "project")
        {
            if (!Has(adults, actor) || subject is not null)
                throw new InvalidOperationException("Only an adult resident may propose a shared Town project.");
        }
        else if (project is not null)
            throw new InvalidOperationException("Construction authority requires a typed Town project proposal.");
        // Structured law proposals supply their own key so scope and the affected law decide equivalence.
        var key = kind == "law_case" ? TownNonviolentGovernmentFilingRules.RequestKey(nonviolentRequest!) :
            kind == "land_hearing" ? TownLandGovernmentFilingRules.RequestKey(landHearingRequest!) :
            kind == "project" ? TownProjectRules.RequestKey(project!) :
            kind == "land_use" ? HouseholdLandGrantRules.RequestKey(landUseRequest!) :
            kind == "land_claim" ? TownLandClaimRules.RequestKey(landClaimTiles!) : requestKey ?? (kind == "admission" ? "admission:" + subject : "law:" + string.Join(' ', text.Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant());
        if (state.Proposals.Any(p => p.RequestKey == key && p.Status == "pending")) return state;
        var previous = state.Proposals.LastOrDefault(p => p.RequestKey == key);
        if (previous is { Status: "rejected" or "withdrawn" } && tick < previous.SettledTick + day &&
            previous.CouncilRevision == state.Revision && previous.Circumstances == circumstances)
            throw new InvalidOperationException("The same request needs one unpaused day or materially changed circumstances before retrying.");
        var id = townId + ":proposal:" + (state.Sequence + 1);
        var proposal = new TownProposal(id, key, kind, actor, subject, text.Trim(), circumstances, state.Revision,
            tick, tick + day, state.Members.ToArray(), state.Form == "representative" ? 2 : state.Members.Count / 2 + 1, [],
            LandClaimTiles: landClaimTiles is null ? null : TownLandRightsRules.OrderTiles(landClaimTiles),
            LandHearingRequest: landHearingRequest, NonviolentRequest: nonviolentRequest)
        { Project = project };
        state = state with { Sequence = state.Sequence + 1, Proposals = state.Proposals.Append(proposal).ToArray() };
        return Notice(state, "proposal", id, $"{kind} proposal by {actor}: {noticeText ?? text.Trim()} " +
            $"Needs {proposal.RequiredYes} yes votes by tick {proposal.DeadlineTick}. A cast vote is final.", tick);
    }

    public static TownGovernanceState VoteProposal(TownGovernanceState state, string proposalId, string actor, bool yes, long tick)
    {
        var proposal = state.Proposals.SingleOrDefault(p => p.Id == proposalId);
        if (proposal is not { Status: "pending" } || tick >= proposal.DeadlineTick || !Has(proposal.Voters, actor) ||
            proposal.Votes.Any(v => v.AgentId == actor) || proposal.Kind == "admission" && proposal.SubjectId == actor)
            throw new InvalidOperationException("This proposal vote is stale, final or ineligible.");
        state = ReplaceProposal(state, proposal with { Votes = proposal.Votes.Append(new(actor, yes)).ToArray() });
        return ResolveProposal(state, proposalId, tick);
    }

    public static TownGovernanceState WithdrawProposal(TownGovernanceState state, string id, string actor, long tick)
    {
        var proposal = state.Proposals.Single(p => p.Id == id);
        if (proposal.Status != "pending" || proposal.AuthorId != actor)
            throw new InvalidOperationException("Only the author may withdraw an unfinished proposal.");
        return SettleProposal(state, proposal, "withdrawn", tick);
    }

    private static TownGovernanceState ResolveProposal(TownGovernanceState state, string id, long tick)
    {
        var p = state.Proposals.Single(p => p.Id == id);
        if (p.Status != "pending") return state;
        var yes = p.Votes.Count(v => v.Yes);
        var remaining = p.Voters.Count - p.Votes.Count;
        return yes >= p.RequiredYes ? SettleProposal(state, p, "passed", tick)
            : tick >= p.DeadlineTick || yes + remaining < p.RequiredYes ? SettleProposal(state, p, "rejected", tick) : state;
    }

    private static TownGovernanceState SettleProposal(TownGovernanceState state, TownProposal p, string status, long tick) =>
        Notice(ReplaceProposal(state, p with { Status = status, SettledTick = tick }), "result", p.Id,
            $"{p.Kind} proposal {status}: {p.Text}", tick);

    internal static TownGovernanceState CancelLandClaim(TownGovernanceState state, TownProposal proposal, long tick) =>
        Notice(ReplaceProposal(state, proposal with { Status = "cancelled", SettledTick = tick }), "result", proposal.Id,
            "Land claim cancelled: the plot is no longer unclaimed land adjoining this Town's title.", tick);
    internal static TownGovernanceState LandUseNotice(TownGovernanceState state, string requestId, string text, long tick) =>
        Notice(state, "land_use", requestId, text, tick);
    internal static TownGovernanceState CancelLandUseProposal(TownGovernanceState state, string proposalId, long tick) =>
        state.Proposals.SingleOrDefault(p => p.Id == proposalId) is { Status: "pending" } proposal
            ? SettleProposal(state, proposal, "cancelled", tick) : state;
    private static TownGovernanceState ReplaceProposal(TownGovernanceState state, TownProposal proposal) =>
        state with { Proposals = state.Proposals.Select(p => p.Id == proposal.Id ? proposal : p).ToArray() };
    /// <summary>Posts an actual notice at the Town's notice place; reading or hearing it is still a separate act.</summary>
    public static TownGovernanceState PostNotice(TownGovernanceState state, string kind, string subject, string text, long tick) =>
        Notice(state, kind, subject, text, tick);

    private static TownGovernanceState Notice(TownGovernanceState state, string kind, string subject, string text, long tick)
    {
        var id = "notice:" + (state.Notices.Count + 1);
        return state with { Notices = state.Notices.Append(new TownCivicNotice(id, kind, subject, text, tick)).ToArray() };
    }

    public static TownGovernanceState LearnNotice(TownGovernanceState state, string actor, string noticeId, long tick, string? source = null)
        => LearnNotices(state, actor, [noticeId], tick, source);

    public static TownGovernanceState LearnNotices(TownGovernanceState state, string actor, IEnumerable<string> noticeIds,
        long tick, string? source = null)
    {
        var existing = state.Notices.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
        var known = state.Knowledge.Where(k => k.AgentId == actor).Select(k => k.NoticeId).ToHashSet(StringComparer.Ordinal);
        var informed = source is null ? null : state.Knowledge.Where(k => k.AgentId == source)
            .Select(k => k.NoticeId).ToHashSet(StringComparer.Ordinal);
        var learned = new List<TownCivicReceipt>();
        foreach (var id in noticeIds)
        {
            if (!existing.Contains(id) || informed is not null && !informed.Contains(id))
                throw new InvalidOperationException("Civic knowledge requires an actual existing notice or informed relay.");
            if (known.Add(id)) learned.Add(new(actor, id, tick, source));
        }
        return learned.Count == 0 ? state : state with { Knowledge = state.Knowledge.Concat(learned).ToArray() };
    }

    /// <summary>The latest passed admission for a subject; only the runtime's admission settlement turns it into membership.</summary>
    public static TownProposal? AdmissionApproval(TownGovernanceState state, string subjectId) =>
        state.Proposals.LastOrDefault(p => p.Kind == "admission" && p.SubjectId == subjectId && p.Status == "passed");
}

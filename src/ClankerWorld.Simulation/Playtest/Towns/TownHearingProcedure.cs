using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>Shared hearing safeguards; none of these procedures changes property or decides truth.</summary>
public static class TownHearingProcedure
{
    public static bool HasNotice(string noticeId, long publishedTick, string actor, long tick,
        IReadOnlyList<TownCivicReceipt> receipts) => receipts.Any(r => r.AgentId == actor &&
            r.NoticeId == noticeId && r.LearnedTick >= publishedTick && r.LearnedTick <= tick);

    public static bool ResponsesClosed(long deadline, long tick,
        IEnumerable<(string PartyId, string? ActorId)> required,
        IEnumerable<(string PartyId, string ActorId, long Tick)> responses) =>
        tick >= deadline || required.All(p => p.ActorId is not null &&
            responses.Any(r => r.PartyId == p.PartyId && r.ActorId == p.ActorId && r.Tick <= tick));

    public static bool Conflicted(string actor, string? household, IEnumerable<string> involvedActors,
        IEnumerable<string?> involvedHouseholds, IReadOnlySet<string>? directStakes = null) =>
        directStakes?.Contains(actor) == true || involvedActors.Contains(actor, StringComparer.Ordinal) ||
        household is not null && involvedHouseholds.Contains(household, StringComparer.Ordinal);

    public static string FactKey(string text) => string.Join(' ', new string(text.Normalize(NormalizationForm.FormC)
        .Select(c => char.IsWhiteSpace(c) || char.IsPunctuation(c) ? ' ' : char.ToUpperInvariant(c)).ToArray())
        .Split(' ', StringSplitOptions.RemoveEmptyEntries));
    public static string Digest<T>(T value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
    internal static bool Id(string? value) => !string.IsNullOrWhiteSpace(value) && value == value.Trim() && !value.Any(char.IsControl);
    internal static bool Text(string? value) => Id(value) && value!.Length <= 1024;
    internal static string[] Ordered(IEnumerable<string> values) => values.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

    /// <summary>One case-election step, shared by land and non-land adapters.</summary>
    public static TownCaseJudgeContest AdvanceContest(TownCaseJudgeContest contest, string[] adults,
        string[] candidates, bool busy, long tick, int day)
    {
        var voters = contest.Voters.Where(adults.Contains).ToArray();
        var remaining = contest.Candidates.Where(candidates.Contains).ToArray();
        contest = contest with
        {
            Voters = voters,
            Candidates = remaining,
            Ballots = contest.Ballots.Where(b => voters.Contains(b.AgentId) && remaining.Contains(b.CandidateId)).ToArray(),
            TiedCandidates = contest.TiedCandidates.Where(candidates.Contains).ToArray()
        };
        if (contest.Stage == "voting" && busy)
            contest = SaveRound(contest, tick, "interrupted") with
            {
                Stage = "waiting",
                Voters = [],
                Candidates = [],
                Ballots = [],
                RoundOpenedTick = null,
                RoundDeadlineTick = null,
                Interruptions = contest.Interruptions + 1
            };
        else if (contest.Stage == "voting" && (contest.Candidates.Count == 0 || tick >= contest.RoundDeadlineTick))
        {
            var most = contest.Candidates.Select(id => contest.Ballots.Count(b => b.CandidateId == id)).DefaultIfEmpty().Max();
            var top = Ordered(contest.Candidates.Where(id => contest.Ballots.Count(b => b.CandidateId == id) == most));
            contest = most == 0 ? SaveRound(contest, tick, "failed") with
            {
                Stage = "failed",
                SettledTick = tick,
                Reason = "No eligible candidate received an actual vote."
            } : top.Length == 1 ?
                SaveRound(contest, tick, "winner") with { Stage = "completed", WinnerId = top[0], SettledTick = tick } :
                SaveRound(contest with { TiedCandidates = top }, tick, "tie") with
                {
                    Stage = "waiting",
                    TiedCandidates = top,
                    Voters = [],
                    Candidates = [],
                    Ballots = [],
                    RoundOpenedTick = null,
                    RoundDeadlineTick = null
                };
        }
        if (contest.Stage == "waiting" && !busy)
        {
            var choices = contest.Rounds.Any(r => r.Result == "tie") ? candidates.Where(contest.TiedCandidates.Contains).ToArray() : candidates;
            contest = contest with
            {
                Stage = "voting",
                Round = contest.Round + 1,
                RoundOpenedTick = tick,
                RoundDeadlineTick = checked(tick + day),
                Voters = adults,
                Candidates = choices,
                Ballots = []
            };
            if (choices.Length == 0)
                contest = SaveRound(contest, tick, "failed") with
                {
                    Stage = "failed",
                    SettledTick = tick,
                    Reason = "No willing independent adult resident is available."
                };
        }
        return contest;
    }

    internal static TownCaseJudgeContest SaveRound(TownCaseJudgeContest contest, long tick, string result) =>
        contest.RoundOpenedTick is not { } opened || contest.Rounds.Any(r => r.Number == contest.Round) ? contest :
            contest with
            {
                Rounds = contest.Rounds.Append(new(contest.Round, opened, tick, result,
                contest.Voters, contest.Candidates, contest.Ballots, contest.TiedCandidates)).ToArray()
            };
    internal static TownCaseJudgeContest CancelContest(TownCaseJudgeContest contest, long tick, string reason) =>
        (contest.Stage == "voting" ? SaveRound(contest, tick, "cancelled") : contest) with
        { Stage = "cancelled", SettledTick = tick, Reason = reason };
}

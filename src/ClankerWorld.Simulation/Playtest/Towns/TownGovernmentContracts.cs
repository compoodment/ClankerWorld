using System.Text.Json.Serialization;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>
/// A Town's declared governing arrangement. <see cref="Ordinary"/> says who
/// makes ordinary decisions (laws and admissions): <c>council</c> (the agreed
/// council, all adults until eight adults elect three representatives),
/// <c>all_adult_council</c> (every adult resident at any population) or
/// <c>mayor</c> (one elected leader). <see cref="Land"/> says whether the
/// elected mayor holds the land mandate (<c>mayor</c>) or no office does
/// (<c>none</c>). <see cref="NonLand"/> separately authorizes nonviolent
/// non-land adjudication. Each mandate has its own term and vacancy record.
/// </summary>
public sealed record TownArrangement(string Ordinary, string Land,
    [property: JsonRequired] string NonLand = TownArrangementRules.NoOffice);

/// <summary>
/// One protected resident government-change process. Statuses: queued,
/// voting, handover, completed, rejected, withdrawn, cancelled. Kinds:
/// <c>arrangement</c> (change to <see cref="Target"/>) or <c>replace_mayor</c>
/// (an early election for the current office under the same arrangement).
/// </summary>
public sealed record TownGovernmentChange(string Id, string RequestKey, string Kind, TownArrangement Target,
    string AuthorId, string Circumstances, long SubmittedTick, string Status,
    long? OpenedTick, long? DeadlineTick, IReadOnlyList<string> Voters, IReadOnlyList<TownProposalVote> Votes,
    long? ApprovedTick = null, long? HandoverDeadlineTick = null, long? SettledTick = null,
    string? SuccessorId = null, string? Reason = null)
{
    public IReadOnlyList<string> OpeningVoters { get; init; } = [];
    public TownNonLandExtension? NonLandExtension { get; init; }
}

/// <summary>The exact elected incumbent residents propose to give added duties without restarting their term.</summary>
public sealed record TownNonLandExtension(string HolderId, string BaseMandate, string BaseElectionId,
    long TermStartTick, long TermEndTick, long? ConsentTick = null);

/// <summary>Historical proof of a protected, personally accepted extension of an existing elected term.</summary>
public sealed record TownNonLandMandateGrant(string Id, string ChangeId, string HolderId, string BaseMandate,
    string BaseElectionId, long ConsentTick, long EffectiveTick, long TermStartTick, long TermEndTick);

/// <summary>One current or historical non-land adjudicator's exact authority and effective interval.</summary>
public sealed record TownNonLandAuthority(string HolderId, string AuthorityId, long EffectiveTick,
    long TermStartTick, long TermEndTick);

/// <summary>
/// One elected mandate: <c>land</c>, <c>ordinary</c> or <c>non_land</c>.
/// An accepted non-land extension inherits its exact base term; its authority
/// starts at the separately recorded grant's effective time.
/// </summary>
public sealed record TownOffice(string Mandates, string? HolderId, long? TermStartTick, long? TermEndTick,
    long? VacantSinceTick, string? VacancyReason, string? ElectionId = null);

/// <summary>A finished mayoral term and why it ended.</summary>
public sealed record TownOfficeTerm(string HolderId, string Mandates, long StartTick, long EndTick, string EndReason, string ElectionId);

/// <summary>A resident's personal agreement to seek the mayor's office with exactly these mandates.</summary>
public sealed record TownMayoralConsent(string AgentId, string Mandates, long Tick);

public sealed record TownMayoralBallot(string AgentId, string CandidateId);

/// <summary>
/// A mayoral election. Purposes: handover (filling an office created or
/// replaced by an approved government change), vacancy and renewal. Stages:
/// voting, waiting (queued behind another Town contest), ready (a renewal
/// winner waiting for the term to end), completed, failed and cancelled.
/// Each round is one unpaused day with a fresh voter roster; tied leaders
/// face further rounds and the recorded tie survives interruptions.
/// </summary>
public sealed record TownMayoralContest(string Id, string Purpose, string Mandates, string? ChangeId,
    string Stage, int Round, long OpenedTick, long? RoundOpenedTick, long? RoundDeadlineTick,
    IReadOnlyList<string> Voters, IReadOnlyList<string> Candidates, IReadOnlyList<TownMayoralBallot> Ballots,
    IReadOnlyList<string> TiedCandidates, int Interruptions, string? WinnerId = null,
    long? SettledTick = null, string? Reason = null)
{
    public IReadOnlyList<TownMayoralRound> Rounds { get; init; } = [];
}

public sealed record TownMayoralRound(int Number, long OpenedTick, long ClosedTick, string Result,
    IReadOnlyList<string> Voters, IReadOnlyList<string> Candidates, IReadOnlyList<TownMayoralBallot> Ballots,
    IReadOnlyList<string> TiedCandidates);

/// <summary>
/// A Town's laws, protected government-change processes and mayor's office.
/// The ordinary council itself stays in <see cref="TownGovernanceState"/>.
/// </summary>
public sealed record TownGovernmentState(TownArrangement Arrangement, long Sequence,
    IReadOnlyList<TownLaw> Laws, IReadOnlyList<TownLawDraft> LawDrafts,
    IReadOnlyList<TownGovernmentChange> Changes, IReadOnlyList<TownOffice> Offices, IReadOnlyList<TownOfficeTerm> OfficeHistory,
    IReadOnlyList<TownMayoralConsent> Consents, TownMayoralContest? Contest, IReadOnlyList<TownMayoralContest> ContestHistory,
    long MayoralRetryTick, string MayoralRetryCircumstances)
{
    [JsonRequired]
    public IReadOnlyList<TownNonLandMandateGrant> NonLandGrants { get; init; } = [];

    public static TownGovernmentState Create() =>
        new(TownArrangementRules.Initial, 0, [], [], [], [], [], [], null, [], 0, "");
}

/// <summary>The supported governing arrangements and their declared authority, selection, tenure and vacancy rules.</summary>
public static class TownArrangementRules
{
    public const string Council = "council";
    public const string ElectedCouncil = "elected_council";
    public const string AllAdultCouncil = "all_adult_council";
    public const string Mayor = "mayor";
    public const string NoOffice = "none";
    public const int MayorTermDays = 20;
    public const int HandoverDays = 3;

    public static TownArrangement Initial { get; } = new(Council, NoOffice);

    /// <summary>Every supported arrangement. Anything else is visibly unsupported and cannot take effect.</summary>
    public static IReadOnlyList<TownArrangement> Supported { get; } = new TownArrangement[]
    {
        new(Council, NoOffice), new(Council, Mayor), new(ElectedCouncil, NoOffice), new(ElectedCouncil, Mayor), new(AllAdultCouncil, NoOffice),
        new(AllAdultCouncil, Mayor), new(Mayor, Mayor), new(Mayor, NoOffice),
    }.SelectMany(arrangement => new[] { arrangement, arrangement with { NonLand = Mayor } }).ToArray();

    public static bool IsSupported(TownArrangement? arrangement) =>
        arrangement is not null && Supported.Contains(arrangement);

    public static string Key(TownArrangement arrangement) => arrangement.Ordinary + "+" + arrangement.Land +
        (arrangement.NonLand == Mayor ? "+non_land" : "");

    public static TownArrangement? Parse(string? key)
    {
        var parts = key?.Split('+');
        if (parts is not { Length: 2 } && parts is not [_, _, "non_land"]) return null;
        var arrangement = new TownArrangement(parts[0], parts[1], parts.Length == 3 ? Mayor : NoOffice);
        return IsSupported(arrangement) ? arrangement : null;
    }

    public static bool HasOffice(TownArrangement arrangement) =>
        arrangement.Ordinary == Mayor || arrangement.Land == Mayor || arrangement.NonLand == Mayor;

    /// <summary>The office's mandates under this arrangement, or null when it has no elected office.</summary>
    public static string? Mandates(TownArrangement arrangement)
    {
        var mandates = new[] { arrangement.Land == Mayor ? "land" : null,
            arrangement.NonLand == Mayor ? "non_land" : null, arrangement.Ordinary == Mayor ? "ordinary" : null }
            .Where(mandate => mandate is not null).ToArray();
        return mandates.Length == 0 ? null : string.Join('+', mandates);
    }

    public static bool IsMandates(string? mandates) => mandates is "land" or "ordinary" or "land+ordinary" or
        "non_land" or "land+non_land" or "non_land+ordinary" or "land+non_land+ordinary";

    public static IReadOnlyList<string> SupportedMandates { get; } =
        ["land", "ordinary", "land+ordinary", "non_land", "land+non_land", "non_land+ordinary", "land+non_land+ordinary"];

    /// <summary>Whether an office with <paramref name="current"/> mandates already covers <paramref name="target"/>.</summary>
    public static bool Covers(string current, string target) =>
        target.Split('+').All(current.Split('+').Contains);

    public static string MandateLabel(string mandates) => string.Join(", and separately ", mandates.Split('+').Select(mandate => mandate switch
    {
        "land" => "land disputes and permission expiries",
        "ordinary" => "ordinary laws and admissions",
        "non_land" => "non-land hearings and voluntary nonviolent remedies",
        _ => "an unsupported mandate",
    }));

    public static string OrdinaryLabel(string ordinary) => ordinary switch
    {
        Council => "the Town council (every adult resident; three elected representatives from eight adults, for ten-day terms)",
        AllAdultCouncil => "every adult resident, at any population",
        ElectedCouncil => "three elected representatives for ten-day terms (every adult resident when there are three or fewer)",
        Mayor => "one elected leader, the mayor",
        _ => "an unsupported authority",
    };

    /// <summary>The complete declaration a proposal spells out: authority, selection, tenure and vacancies.</summary>
    public static string Declaration(TownArrangement arrangement)
    {
        var text = "Ordinary decisions: " + OrdinaryLabel(arrangement.Ordinary) + ". Land decisions: " +
            (arrangement.Land == Mayor ? "the elected mayor" : "no officeholder; land cases stay pending") + ". " +
            "Non-land adjudication: " + (arrangement.NonLand == Mayor ? "a separately authorized elected office" : "no officeholder; formal cases stay pending") + ".";
        if (Mandates(arrangement) is not { } mandates) return text;
        return text + $" Selection: residents elect a willing adult resident as mayor ({MandateLabel(mandates)}), one choice each, " +
            $"with further votes between tied leaders. Tenure: {MayorTermDays} unpaused days, with renewal voting one day before the term ends. " +
            "Vacancy: a prompt election with a fresh full term" +
            (arrangement.Ordinary == Mayor ? "; every adult resident makes ordinary decisions meanwhile." : "; land decisions wait meanwhile.");
    }
}

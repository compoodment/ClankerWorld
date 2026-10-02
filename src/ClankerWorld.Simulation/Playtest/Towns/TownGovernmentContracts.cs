namespace ClankerWorld.Simulation.Playtest;

/// <summary>
/// A Town's declared governing arrangement. <see cref="Ordinary"/> says who
/// makes ordinary decisions (laws and admissions): <c>council</c> (the agreed
/// council, all adults until eight adults elect three representatives),
/// <c>all_adult_council</c> (every adult resident at any population) or
/// <c>mayor</c> (one elected leader). <see cref="Land"/> says whether the
/// elected mayor holds the land mandate (<c>mayor</c>) or no office does
/// (<c>none</c>). One mayor's office holds whichever mandates are declared.
/// </summary>
public sealed record TownArrangement(string Ordinary, string Land);

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
    string? SuccessorId = null, string? Reason = null);

/// <summary>
/// The elected mayor's office. <see cref="Mandates"/> is <c>land</c>,
/// <c>ordinary</c> or <c>land+ordinary</c>; the mandates stay distinct even
/// when one person holds both.
/// </summary>
public sealed record TownOffice(string Mandates, string? HolderId, long? TermStartTick, long? TermEndTick,
    long? VacantSinceTick, string? VacancyReason);

/// <summary>A finished mayoral term and why it ended.</summary>
public sealed record TownOfficeTerm(string HolderId, string Mandates, long StartTick, long EndTick, string EndReason);

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
    long? SettledTick = null, string? Reason = null);

/// <summary>
/// A Town's laws, protected government-change processes and mayor's office.
/// The ordinary council itself stays in <see cref="TownGovernanceState"/>.
/// </summary>
public sealed record TownGovernmentState(TownArrangement Arrangement, long Sequence,
    IReadOnlyList<TownLaw> Laws, IReadOnlyList<TownLawDraft> LawDrafts,
    IReadOnlyList<TownGovernmentChange> Changes, TownOffice? Office, IReadOnlyList<TownOfficeTerm> OfficeHistory,
    IReadOnlyList<TownMayoralConsent> Consents, TownMayoralContest? Contest, IReadOnlyList<TownMayoralContest> ContestHistory,
    long MayoralRetryTick, string MayoralRetryCircumstances)
{
    public static TownGovernmentState Create() =>
        new(TownArrangementRules.Initial, 0, [], [], [], null, [], [], null, [], 0, "");
}

/// <summary>The supported governing arrangements and their declared authority, selection, tenure and vacancy rules.</summary>
public static class TownArrangementRules
{
    public const string Council = "council";
    public const string AllAdultCouncil = "all_adult_council";
    public const string Mayor = "mayor";
    public const string NoOffice = "none";
    public const int MayorTermDays = 20;
    public const int HandoverDays = 3;

    public static TownArrangement Initial { get; } = new(Council, NoOffice);

    /// <summary>Every supported arrangement. Anything else is visibly unsupported and cannot take effect.</summary>
    public static IReadOnlyList<TownArrangement> Supported { get; } =
    [
        new(Council, NoOffice), new(Council, Mayor), new(AllAdultCouncil, NoOffice),
        new(AllAdultCouncil, Mayor), new(Mayor, Mayor), new(Mayor, NoOffice),
    ];

    public static bool IsSupported(TownArrangement? arrangement) =>
        arrangement is not null && Supported.Contains(arrangement);

    public static string Key(TownArrangement arrangement) => arrangement.Ordinary + "+" + arrangement.Land;

    public static TownArrangement? Parse(string? key)
    {
        var parts = key?.Split('+');
        if (parts is not { Length: 2 }) return null;
        var arrangement = new TownArrangement(parts[0], parts[1]);
        return IsSupported(arrangement) ? arrangement : null;
    }

    public static bool HasOffice(TownArrangement arrangement) =>
        arrangement.Ordinary == Mayor || arrangement.Land == Mayor;

    /// <summary>The office's mandates under this arrangement, or null when it has no elected office.</summary>
    public static string? Mandates(TownArrangement arrangement) => (arrangement.Land == Mayor, arrangement.Ordinary == Mayor) switch
    {
        (true, true) => "land+ordinary",
        (true, false) => "land",
        (false, true) => "ordinary",
        _ => null,
    };

    public static bool IsMandates(string? mandates) => mandates is "land" or "ordinary" or "land+ordinary";

    /// <summary>Whether an office with <paramref name="current"/> mandates already covers <paramref name="target"/>.</summary>
    public static bool Covers(string current, string target) =>
        target.Split('+').All(current.Split('+').Contains);

    public static string MandateLabel(string mandates) => mandates switch
    {
        "land" => "land disputes and permission expiries",
        "ordinary" => "ordinary laws and admissions",
        _ => "ordinary laws and admissions, and separately land disputes and permission expiries",
    };

    public static string OrdinaryLabel(string ordinary) => ordinary switch
    {
        Council => "the Town council (every adult resident; three elected representatives from eight adults, for ten-day terms)",
        AllAdultCouncil => "every adult resident, at any population",
        Mayor => "one elected leader, the mayor",
        _ => "an unsupported authority",
    };

    /// <summary>The complete declaration a proposal spells out: authority, selection, tenure and vacancies.</summary>
    public static string Declaration(TownArrangement arrangement)
    {
        var text = "Ordinary decisions: " + OrdinaryLabel(arrangement.Ordinary) + ". Land decisions: " +
            (arrangement.Land == Mayor ? "the elected mayor" : "no officeholder; land cases stay pending") + ".";
        if (Mandates(arrangement) is not { } mandates) return text;
        return text + $" Selection: residents elect a willing adult resident as mayor ({MandateLabel(mandates)}), one choice each, " +
            $"with further votes between tied leaders. Tenure: {MayorTermDays} unpaused days, with renewal voting one day before the term ends. " +
            "Vacancy: a prompt election with a fresh full term" +
            (arrangement.Ordinary == Mayor ? "; every adult resident makes ordinary decisions meanwhile." : "; land decisions wait meanwhile.");
    }
}

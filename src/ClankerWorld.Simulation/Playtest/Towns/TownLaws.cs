using System.Globalization;
using ClankerWorld.Simulation.Harness;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>
/// One recorded wording of a Town law. A version applies from its adoption
/// tick until a later amendment or repeal ends it, so earlier conduct stays
/// associated with the wording then in force.
/// </summary>
public sealed record TownLawVersion(int Version, string Subject, string Rule, string Scope,
    IReadOnlyList<GridPoint> SiteTiles, string ProposalId, long AdoptedTick,
    long? EndedTick = null, string? EndedByProposalId = null);

/// <summary>A Town law and its complete version history. Repealed laws keep their history.</summary>
public sealed record TownLaw(string Id, IReadOnlyList<TownLawVersion> Versions);

/// <summary>
/// The structured content of an ordinary council law proposal. The proposal
/// itself keeps the council votes; this draft says what a passed proposal
/// records. Statuses: pending, enacted, not_passed, stale.
/// </summary>
public sealed record TownLawDraft(string ProposalId, string Action, string? LawId, int? BaseVersion,
    string Subject, string Rule, string Scope, IReadOnlyList<GridPoint> SiteTiles, string Status = "pending");

/// <summary>
/// Town laws are recorded social rules. They never block physical actions,
/// change ownership or create offices; they only say which rule applied to
/// whom, where and when.
/// </summary>
public static class TownLawRules
{
    /// <summary>Applies to everyone, visitors included, on the Town's formally titled land.</summary>
    public const string Jurisdiction = "jurisdiction";
    /// <summary>Applies to everyone on a specified site inside the Town's titled land.</summary>
    public const string Site = "site";
    /// <summary>An explicit duty of the Town's residents that follows them wherever they are.</summary>
    public const string ResidentDuty = "resident_duty";
    public const int SiteRadius = 2;
    public const int MaximumSubjectLength = 64;
    public const int MaximumTextLength = TownGovernanceRules.MaximumProposalText;

    public static bool IsScope(string? scope) => scope is Jurisdiction or Site or ResidentDuty;

    /// <summary>Reads <c>subject: rule</c> law text. Both parts are required so the subject is explicit.</summary>
    public static bool TryParse(string? text, out string subject, out string rule)
    {
        subject = rule = string.Empty;
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > MaximumTextLength || trimmed.Any(char.IsControl)) return false;
        var colon = trimmed.IndexOf(':', StringComparison.Ordinal);
        if (colon <= 0) return false;
        subject = trimmed[..colon].Trim();
        rule = trimmed[(colon + 1)..].Trim();
        return subject.Length is > 0 and <= MaximumSubjectLength && rule.Length > 0;
    }

    public static string Text(string subject, string rule) => subject + ": " + rule;

    /// <summary>Titled tiles of this Town within <see cref="SiteRadius"/> of a titled centre tile.</summary>
    public static GridPoint[] SiteAround(SeededMap map, GridPoint center, string townId,
        IReadOnlyList<TownLandTitleRecord> titles)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(titles);
        if (!TownLandRightsRules.IsCoveredByTownTitle(center, townId, titles)) return [];
        var tiles = new HashSet<GridPoint>();
        for (var dy = -SiteRadius; dy <= SiteRadius; dy++)
            for (var dx = -SiteRadius; dx <= SiteRadius; dx++)
            {
                var tile = map.WrapColumn(new GridPoint(center.X + dx, center.Y + dy));
                if (map.Contains(tile) && TownLandRightsRules.IsCoveredByTownTitle(tile, townId, titles)) tiles.Add(tile);
            }
        return TownLandRightsRules.OrderTiles(tiles);
    }

    public static string ScopeLabel(string scope, string townName, int siteTiles = 0) => scope switch
    {
        Jurisdiction => $"everyone on {townName}'s claimed land, visitors included",
        Site => $"everyone on a {siteTiles.ToString(CultureInfo.InvariantCulture)}-tile site within {townName}'s claimed land, visitors included",
        ResidentDuty => $"{townName}'s residents wherever they are",
        _ => "an unsupported scope",
    };

    public static int Number(string lawId) =>
        int.TryParse(lawId.AsSpan(lawId.LastIndexOf(':') + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : 0;

    public static TownLawVersion Current(TownLaw law) => law.Versions[^1];
    public static bool IsInForce(TownLaw law) => Current(law).EndedTick is null;

    /// <summary>The wording in force at <paramref name="tick"/>, or null before adoption or after repeal.</summary>
    public static TownLawVersion? InForceAt(TownLaw law, long tick) =>
        law.Versions.LastOrDefault(v => v.AdoptedTick <= tick && (v.EndedTick is null || tick < v.EndedTick));

    /// <summary>
    /// The law versions that applied to a person at a position and moment.
    /// Territorial laws follow the Town's formal title, not its drawn border,
    /// and do not follow residents who travel off that land.
    /// </summary>
    public static IReadOnlyList<(TownLaw Law, TownLawVersion Version)> Applicable(TownGovernmentState government,
        string townId, IReadOnlyList<TownLandTitleRecord> titles, bool isResident, GridPoint position, long tick)
    {
        ArgumentNullException.ThrowIfNull(government);
        var applicable = new List<(TownLaw, TownLawVersion)>();
        foreach (var law in government.Laws)
        {
            if (InForceAt(law, tick) is not { } version) continue;
            var applies = version.Scope switch
            {
                Jurisdiction => TownLandRightsRules.IsCoveredByTownTitle(position, townId, titles),
                Site => version.SiteTiles.Contains(position) && TownLandRightsRules.IsCoveredByTownTitle(position, townId, titles),
                ResidentDuty => isResident,
                _ => false,
            };
            if (applies) applicable.Add((law, version));
        }
        return applicable;
    }

    public static (TownGovernanceState Council, TownGovernmentState Government) ProposeAdoption(
        TownGovernanceState council, TownGovernmentState government, string townId, string actor, string text,
        string scope, IReadOnlyList<GridPoint> siteTiles, IEnumerable<string> adults, long tick, int day)
    {
        if (!IsScope(scope) || scope == Site != siteTiles.Count > 0 || !TryParse(text, out var subject, out var rule))
            throw new InvalidOperationException("A law proposal needs 'subject: rule' text and a supported scope.");
        var site = TownLandRightsRules.OrderTiles(siteTiles);
        var key = $"law:{scope}:{SiteKey(site)}:{Normalize(Text(subject, rule))}";
        return Submit(council, government, townId, actor, Text(subject, rule), key, adults, tick, day,
            proposalId => new TownLawDraft(proposalId, "adopt", null, null, subject, rule, scope, site));
    }

    public static (TownGovernanceState Council, TownGovernmentState Government) ProposeAmendment(
        TownGovernanceState council, TownGovernmentState government, string townId, string actor, string lawId,
        string text, IEnumerable<string> adults, long tick, int day, int? expectedVersion = null)
    {
        var law = government.Laws.SingleOrDefault(l => l.Id == lawId);
        if (law is null || !IsInForce(law) || !TryParse(text, out var subject, out var rule))
            throw new InvalidOperationException("An amendment must identify a law in force and give 'subject: rule' text.");
        var current = Current(law);
        if (expectedVersion is { } expected && current.Version != expected)
            throw new InvalidOperationException("The law changed after this amendment was chosen.");
        if (current.Subject == subject && current.Rule == rule)
            throw new InvalidOperationException("An amendment must change the law's wording.");
        var label = Bounded($"Amend law {Number(law.Id).ToString(CultureInfo.InvariantCulture)} to read: {Text(subject, rule)}");
        var key = $"law_amend:{law.Id}:{current.Version.ToString(CultureInfo.InvariantCulture)}:{Normalize(Text(subject, rule))}";
        return Submit(council, government, townId, actor, label, key, adults, tick, day,
            proposalId => new TownLawDraft(proposalId, "amend", law.Id, current.Version, subject, rule, current.Scope, current.SiteTiles));
    }

    public static (TownGovernanceState Council, TownGovernmentState Government) ProposeRepeal(
        TownGovernanceState council, TownGovernmentState government, string townId, string actor, string lawId,
        IEnumerable<string> adults, long tick, int day, int? expectedVersion = null)
    {
        var law = government.Laws.SingleOrDefault(l => l.Id == lawId);
        if (law is null || !IsInForce(law))
            throw new InvalidOperationException("A repeal must identify a law in force.");
        var current = Current(law);
        if (expectedVersion is { } expected && current.Version != expected)
            throw new InvalidOperationException("The law changed after this repeal was chosen.");
        var label = Bounded($"Repeal law {Number(law.Id).ToString(CultureInfo.InvariantCulture)}: {Text(current.Subject, current.Rule)}");
        var key = $"law_repeal:{law.Id}:{current.Version.ToString(CultureInfo.InvariantCulture)}";
        return Submit(council, government, townId, actor, label, key, adults, tick, day,
            proposalId => new TownLawDraft(proposalId, "repeal", law.Id, current.Version, current.Subject, current.Rule,
                current.Scope, current.SiteTiles));
    }

    private static (TownGovernanceState, TownGovernmentState) Submit(TownGovernanceState council,
        TownGovernmentState government, string townId, string actor, string text, string key,
        IEnumerable<string> adults, long tick, int day, Func<string, TownLawDraft> draft)
    {
        var before = council.Proposals.Count;
        var details = draft("");
        council = TownGovernanceRules.SubmitProposal(council, townId, actor, "law", null, text,
            "council:" + council.Revision.ToString(CultureInfo.InvariantCulture), adults, tick, day, key, VoteText(details));
        // An equivalent pending request merges into the open proposal and keeps its window.
        if (council.Proposals.Count == before) return (council, government);
        return (council, government with { LawDrafts = government.LawDrafts.Append(draft(council.Proposals[^1].Id)).ToArray() });
    }

    /// <summary>
    /// Records passed law proposals, in the order they passed. A proposal that
    /// lost its target to an earlier change is kept as stale rather than
    /// rewriting a different wording.
    /// </summary>
    public static (TownGovernanceState Council, TownGovernmentState Government) Enact(TownGovernanceState council,
        TownGovernmentState government, string townId, string townName, long tick)
    {
        var proposals = council.Proposals.ToDictionary(p => p.Id, StringComparer.Ordinal);
        var settled = government.LawDrafts.Where(d => d.Status == "pending" && proposals[d.ProposalId].Status != "pending")
            .OrderBy(d => proposals[d.ProposalId].SettledTick).ThenBy(d => Number(d.ProposalId)).ToArray();
        foreach (var draft in settled)
        {
            var proposal = proposals[draft.ProposalId];
            if (proposal.Status != "passed")
            {
                government = Replace(government, draft with { Status = "not_passed" });
                continue;
            }
            var at = proposal.SettledTick!.Value;
            if (draft.Action == "adopt")
            {
                var id = townId + ":law:" + (government.Sequence + 1).ToString(CultureInfo.InvariantCulture);
                var version = new TownLawVersion(1, draft.Subject, draft.Rule, draft.Scope, draft.SiteTiles, draft.ProposalId, at);
                government = Replace(government with { Sequence = government.Sequence + 1, Laws = government.Laws.Append(new TownLaw(id, [version])).ToArray() },
                    draft with { Status = "enacted", LawId = id });
                council = TownGovernanceRules.PostNotice(council, "law", id,
                    $"Law {Number(id).ToString(CultureInfo.InvariantCulture)} adopted: {Text(draft.Subject, draft.Rule)} It applies to " +
                    $"{ScopeLabel(draft.Scope, townName, draft.SiteTiles.Count)} from tick {at.ToString(CultureInfo.InvariantCulture)}, not to earlier conduct. " +
                    "It records a social rule: it does not block actions or change ownership.", tick);
                continue;
            }
            var law = government.Laws.Single(l => l.Id == draft.LawId);
            var current = Current(law);
            if (current.EndedTick is not null || current.Version != draft.BaseVersion)
            {
                government = Replace(government, draft with { Status = "stale" });
                council = TownGovernanceRules.PostNotice(council, "law", law.Id,
                    $"A passed proposal could not change law {Number(law.Id).ToString(CultureInfo.InvariantCulture)} because the law changed first. " +
                    "A fresh proposal is needed.", tick);
                continue;
            }
            var ended = current with { EndedTick = at, EndedByProposalId = draft.ProposalId };
            var versions = law.Versions.Take(law.Versions.Count - 1).Append(ended);
            if (draft.Action == "amend")
                versions = versions.Append(new TownLawVersion(current.Version + 1, draft.Subject, draft.Rule, current.Scope,
                    current.SiteTiles, draft.ProposalId, at));
            government = Replace(government with
            {
                Laws = government.Laws.Select(l => l.Id == law.Id ? law with { Versions = versions.ToArray() } : l).ToArray(),
            }, draft with { Status = "enacted" });
            council = TownGovernanceRules.PostNotice(council, "law", law.Id, draft.Action == "amend"
                ? $"Law {Number(law.Id).ToString(CultureInfo.InvariantCulture)} amended: {Text(draft.Subject, draft.Rule)} The new wording applies from tick {at.ToString(CultureInfo.InvariantCulture)}; earlier conduct keeps the earlier wording."
                : $"Law {Number(law.Id).ToString(CultureInfo.InvariantCulture)} repealed: {draft.Subject}. It still applies to conduct before tick {at.ToString(CultureInfo.InvariantCulture)}.", tick);
        }
        return (council, government);
    }

    private static TownGovernmentState Replace(TownGovernmentState government, TownLawDraft draft) =>
        government with { LawDrafts = government.LawDrafts.Select(d => d.ProposalId == draft.ProposalId ? draft : d).ToArray() };

    /// <summary>The full content voters must see, including scope and the exact site; never truncate the voted rule.</summary>
    public static string VoteText(TownLawDraft draft) =>
        (draft.Action == "adopt" ? "Adopt law: " : draft.Action == "amend" ? $"Amend law {Number(draft.LawId!)} (version {draft.BaseVersion}) to: "
            : $"Repeal law {Number(draft.LawId!)} (version {draft.BaseVersion}): ") + Text(draft.Subject, draft.Rule) +
        ". Scope: " + ScopeLabel(draft.Scope, "this Town", draft.SiteTiles.Count) +
        (draft.SiteTiles.Count > 0 ? ". Site tiles: " + SiteKey(draft.SiteTiles.ToArray()) : "") + ".";

    internal static string ProposalText(TownLawDraft draft) => draft.Action switch
    {
        "adopt" => Text(draft.Subject, draft.Rule),
        "amend" => Bounded($"Amend law {Number(draft.LawId!).ToString(CultureInfo.InvariantCulture)} to read: {Text(draft.Subject, draft.Rule)}"),
        _ => Bounded($"Repeal law {Number(draft.LawId!).ToString(CultureInfo.InvariantCulture)}: {Text(draft.Subject, draft.Rule)}"),
    };
    internal static string RequestKey(TownLawDraft draft) => draft.Action switch
    {
        "adopt" => $"law:{draft.Scope}:{SiteKey(draft.SiteTiles.ToArray())}:{Normalize(Text(draft.Subject, draft.Rule))}",
        "amend" => $"law_amend:{draft.LawId}:{draft.BaseVersion?.ToString(CultureInfo.InvariantCulture)}:{Normalize(Text(draft.Subject, draft.Rule))}",
        _ => $"law_repeal:{draft.LawId}:{draft.BaseVersion?.ToString(CultureInfo.InvariantCulture)}",
    };
    internal static bool IsStructuredRequest(string key) =>
        key.StartsWith("law_amend:", StringComparison.Ordinal) || key.StartsWith("law_repeal:", StringComparison.Ordinal) ||
        new[] { Jurisdiction, Site, ResidentDuty }.Any(scope => key.StartsWith("law:" + scope + ":", StringComparison.Ordinal));

    private static string Normalize(string text) =>
        string.Join(' ', text.Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();

    private static string SiteKey(GridPoint[] tiles) => tiles.Length == 0 ? "-" :
        string.Join(';', tiles.Select(t => t.X.ToString(CultureInfo.InvariantCulture) + "," + t.Y.ToString(CultureInfo.InvariantCulture)));

    private static string Bounded(string text) => text.Length <= MaximumTextLength ? text : text[..(MaximumTextLength - 1)] + "…";
}

using System.Globalization;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>Checks a Town's saved laws, government changes and mayor's office against its council and land records.</summary>
public static class TownGovernmentValidation
{
    public static void Validate(TownRuntimeState town, SocietyCheckpoint society,
        IReadOnlyList<TownLandTitleRecord> titles, int day)
    {
        if (town.Government is not { } state)
        {
            if (town.FoundingState == "founded")
                throw new InvalidDataException("A founded Town is missing its saved laws and government record.");
            return;
        }
        if (town.Governance is not { } council)
            throw new InvalidDataException("A Town's government record needs its saved council.");
        if (state.Laws is null || state.LawDrafts is null || state.Changes is null || state.OfficeHistory is null ||
            state.Consents is null || state.ContestHistory is null || state.Offices is null || state.MayoralRetryCircumstances is null ||
            state.Laws.Any(l => l is null) || state.Changes.Any(c => c is null) || state.ContestHistory.Any(c => c is null) ||
            !TownArrangementRules.IsSupported(state.Arrangement) || state.Sequence < 0 || state.MayoralRetryTick < 0 ||
            state.MayoralRetryCircumstances.Length > 64)
            throw new InvalidDataException("A Town's saved government record is incomplete or declares an unsupported arrangement.");
        var tick = society.WorldTick;
        var ids = state.Laws.Select(l => l.Id).Concat(state.Changes.Select(c => c.Id))
            .Concat(state.ContestHistory.Select(c => c.Id)).Concat(state.Contest is { } live ? [live.Id] : []).ToArray();
        if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Length)
            throw new InvalidDataException("A Town's saved law, government-change and election identities must be unique.");
        ValidateLaws(town, state, council, titles, tick);
        TownGovernmentStateValidation.Validate(town, state, council, society, day);
    }

    private static void ValidateLaws(TownRuntimeState town, TownGovernmentState state, TownGovernanceState council,
        IReadOnlyList<TownLandTitleRecord> titles, long tick)
    {
        var proposals = council.Proposals.ToDictionary(p => p.Id, StringComparer.Ordinal);
        var recorded = new HashSet<string>(StringComparer.Ordinal);
        foreach (var law in state.Laws)
        {
            if (law is null || !ValidId(law.Id, town.Id + ":law:", state.Sequence) || law.Versions is not { Count: > 0 })
                throw new InvalidDataException("A Town's saved law is invalid.");
            for (var index = 0; index < law.Versions.Count; index++)
            {
                var version = law.Versions[index];
                var last = index == law.Versions.Count - 1;
                if (version is null || version.Version != index + 1 || version.SiteTiles is null ||
                    !TownLawRules.TryParse(TownLawRules.Text(version.Subject ?? "", version.Rule ?? ""), out var subject, out var rule) ||
                    subject != version.Subject || rule != version.Rule || !TownLawRules.IsScope(version.Scope) ||
                    version.Scope == TownLawRules.Site != version.SiteTiles.Count > 0 ||
                    !version.SiteTiles.SequenceEqual(TownLandRightsRules.OrderTiles(version.SiteTiles.Distinct())) ||
                    version.SiteTiles.Any(tile => !TownLandRightsRules.IsCoveredByTownTitle(tile, town.Id, titles)) ||
                    index > 0 && (version.Scope != law.Versions[0].Scope || !version.SiteTiles.SequenceEqual(law.Versions[0].SiteTiles)) ||
                    !proposals.TryGetValue(version.ProposalId ?? "", out var adopted) || adopted.Kind != "law" ||
                    adopted.Status != "passed" || adopted.SettledTick != version.AdoptedTick || version.AdoptedTick > tick ||
                    index > 0 && law.Versions[index - 1].EndedByProposalId != version.ProposalId ||
                    !last && version.EndedTick != law.Versions[index + 1].AdoptedTick ||
                    (version.EndedTick is null) != (version.EndedByProposalId is null) ||
                    !last && version.EndedTick is null ||
                    version.EndedTick is { } ended && (ended < version.AdoptedTick || ended > tick ||
                        !proposals.TryGetValue(version.EndedByProposalId!, out var ending) || ending.Kind != "law" ||
                        ending.Status != "passed" || ending.SettledTick != ended))
                    throw new InvalidDataException("A Town law's saved version history is invalid.");
                recorded.Add(version.ProposalId!);
                if (version.EndedByProposalId is { } endedBy) recorded.Add(endedBy);
            }
        }
        if (state.LawDrafts.Any(d => d is null) ||
            state.LawDrafts.Select(d => d.ProposalId).Distinct(StringComparer.Ordinal).Count() != state.LawDrafts.Count)
            throw new InvalidDataException("A Town's saved law proposals are invalid.");
        foreach (var draft in state.LawDrafts)
        {
            var law = draft.LawId is null ? null : state.Laws.SingleOrDefault(l => l.Id == draft.LawId);
            if (!proposals.TryGetValue(draft.ProposalId, out var proposal) || proposal.Kind != "law" ||
                draft.Action is not ("adopt" or "amend" or "repeal") || draft.SiteTiles is null ||
                !TownLawRules.TryParse(TownLawRules.Text(draft.Subject ?? "", draft.Rule ?? ""), out var subject, out var rule) ||
                subject != draft.Subject || rule != draft.Rule || !TownLawRules.IsScope(draft.Scope) ||
                draft.Scope == TownLawRules.Site != draft.SiteTiles.Count > 0 ||
                !draft.SiteTiles.SequenceEqual(TownLandRightsRules.OrderTiles(draft.SiteTiles.Distinct())) ||
                draft.SiteTiles.Any(tile => !TownLandRightsRules.IsCoveredByTownTitle(tile, town.Id, titles)) ||
                draft.Action == "adopt" && (draft.BaseVersion is not null || (draft.LawId is null) != (draft.Status != "enacted")) ||
                draft.Action != "adopt" && (law is null || draft.BaseVersion is not { } baseVersion ||
                    baseVersion < 1 || baseVersion > law.Versions.Count || draft.Scope != law.Versions[0].Scope ||
                    !draft.SiteTiles.SequenceEqual(law.Versions[0].SiteTiles)) ||
                proposal.Text != TownLawRules.ProposalText(draft) || proposal.RequestKey != TownLawRules.RequestKey(draft) ||
                draft.Status switch
                {
                    "pending" => proposal.Status != "pending",
                    "not_passed" => proposal.Status is "pending" or "passed",
                    "enacted" => proposal.Status != "passed" || !recorded.Contains(draft.ProposalId) ||
                        draft.LawId is null || state.Laws.SingleOrDefault(l => l.Id == draft.LawId) is not { } target ||
                        !target.Versions.Any(v => v.ProposalId == draft.ProposalId || v.EndedByProposalId == draft.ProposalId),
                    "stale" => proposal.Status != "passed" || recorded.Contains(draft.ProposalId) || draft.Action == "adopt" ||
                        law!.Versions[draft.BaseVersion!.Value - 1].EndedTick is not { } ended || ended > proposal.SettledTick,
                    _ => true,
                })
                throw new InvalidDataException("A Town's saved law proposal does not match its council vote.");
        }
        if (recorded.Any(id => !state.LawDrafts.Any(d => d.ProposalId == id && d.Status == "enacted")))
            throw new InvalidDataException("A Town law changed without a recorded passed law proposal.");
        var drafts = state.LawDrafts.ToDictionary(d => d.ProposalId, StringComparer.Ordinal);
        if (council.Proposals.Any(p => TownLawRules.IsStructuredRequest(p.RequestKey) && !drafts.ContainsKey(p.Id)))
            throw new InvalidDataException("Every structured law proposal needs its full saved wording and scope.");
        foreach (var law in state.Laws)
            foreach (var version in law.Versions)
            {
                var source = drafts[version.ProposalId];
                if (source.LawId != law.Id || source.Subject != version.Subject || source.Rule != version.Rule ||
                    source.Scope != version.Scope || !source.SiteTiles.SequenceEqual(version.SiteTiles) ||
                    (version.Version == 1 ? source.Action != "adopt" : source.Action != "amend" || source.BaseVersion != version.Version - 1))
                    throw new InvalidDataException("A law version must match the exact wording and scope approved by its Council.");
                if (version.EndedByProposalId is not { } endId) continue;
                var ending = drafts[endId];
                if (ending.LawId != law.Id || ending.BaseVersion != version.Version || ending.Action is not ("amend" or "repeal") ||
                    ending.Action == "amend" && (version.Version >= law.Versions.Count || law.Versions[version.Version].ProposalId != endId) ||
                    ending.Action == "repeal" && (version.Version != law.Versions.Count || ending.Subject != version.Subject || ending.Rule != version.Rule))
                    throw new InvalidDataException("An ended law version needs the exact amendment or repeal of that version.");
            }
    }

    internal static bool ValidId(string? id, string prefix, long sequence) =>
        !string.IsNullOrWhiteSpace(id) && id.StartsWith(prefix, StringComparison.Ordinal) &&
        long.TryParse(id.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var number) &&
        number > 0 && number <= sequence && id == prefix + number.ToString(CultureInfo.InvariantCulture);
}

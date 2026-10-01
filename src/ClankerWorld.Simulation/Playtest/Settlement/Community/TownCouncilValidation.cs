using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private static void ValidateTownCouncils(PrivateWorldRuntimeState state)
    {
        if (state.TownCouncils is not { } councils)
        {
            if (state.FounderSetup?.Started == true && (state.Towns ?? []).Count > 0)
                throw new InvalidDataException("A started Town must retain its saved council.");
            return;
        }
        if (state.SchemaVersion < 36 && councils.Count > 0)
            throw new InvalidDataException("Physical Town councils require private-world schema 36.");
        var tick = state.Society.Society.WorldTick;
        var towns = (state.Towns ?? []).ToDictionary(town => town.Id, StringComparer.Ordinal);
        var people = state.Society.Society.Inhabitants.ToDictionary(person => person.Id, StringComparer.Ordinal);
        var day = state.WorldSystems?.Config.TicksPerDay ?? 1440;
        var term = checked(10L * day);
        if (councils.Any(council => council is null) || councils.Count > towns.Count ||
            state.FounderSetup?.Started == true && councils.Count != towns.Count ||
            councils.Select(council => council.TownId).Distinct(StringComparer.Ordinal).Count() != councils.Count)
            throw new InvalidDataException("A Town has duplicate or unassigned councils.");
        foreach (var council in councils)
        {
            if (string.IsNullOrWhiteSpace(council.TownId) || !towns.TryGetValue(council.TownId, out var town) ||
                council.FoodPolicy is not ("open" or "essential_first") || council.GoverningForm is not ("collective" or "representative") ||
                council.FallbackReason is not (null or "population" or "candidate") ||
                council.LastResolutionTick < 0 || council.LastResolutionTick > tick || !UniqueIds(council.MemberIds) ||
                council.Laws is null || council.Laws.Any(law => law is null) || council.Laws.Count > 65 ||
                council.Laws.Select(law => law.Key).Distinct(StringComparer.Ordinal).Count() != council.Laws.Count ||
                council.CandidateRegister is null || council.CandidateRegister.Any(item => item is null) ||
                council.CandidateRegister.Select(item => item.CandidateId).Distinct(StringComparer.Ordinal).Count() != council.CandidateRegister.Count ||
                council.ProposalOutcomes is null || council.ProposalOutcomes.Any(item => item is null) || council.ProposalOutcomes.Count > 256 ||
                council.ProposalOutcomes.Select(item => item.RequestKey).Distinct(StringComparer.Ordinal).Count() != council.ProposalOutcomes.Count)
                throw new InvalidDataException("The saved Town council is invalid.");
            bool Adult(string id) => id is not null && town.ResidentIds.Contains(id, StringComparer.Ordinal) && people.TryGetValue(id, out var person) &&
                person.Status == SocietyInhabitantStatus.Active && person.AgeBand is SocietyAgeBand.Adult or SocietyAgeBand.Elder;
            var adults = town.ResidentIds.Where(Adult).ToHashSet(StringComparer.Ordinal);
            if (council.LastElectionOutcome is { } savedOutcome)
                ValidateTownElectionOutcome(state.WorldSeed, council.TownId, savedOutcome, people.Keys.ToHashSet(StringComparer.Ordinal), tick, day);
            if (council.MemberIds.Any(id => !Adult(id)) || council.TermStartedTick.HasValue != council.TermExpiryTick.HasValue ||
                council.TermStartedTick is { } start && (start < 0 || start > tick || council.TermExpiryTick - start != term) ||
                council.GoverningForm == "collective" && (!adults.SetEquals(council.MemberIds) || council.FallbackReason is null) ||
                council.GoverningForm == "representative" && (council.TermStartedTick is null || council.MemberIds.Count > 3 || adults.Count <= 3 || council.FallbackReason is not null) ||
                council.FallbackReason == "population" && council.TermStartedTick is not null ||
                council.CandidateRegister.Any(item => !Adult(item.CandidateId) || item.DeclaredTick < 0 || item.DeclaredTick > tick ||
                    !item.FullTermWilling && !item.ReplacementWilling || !ValidReplacementConsent(item, tick, term) ||
                    item.ReplacementWilling && (item.ReplacementTermStartedTick != council.TermStartedTick ||
                        item.ReplacementTermExpiryTick != council.TermExpiryTick || council.TermExpiryTick <= tick)) ||
                council.ElectionRetryAfterTick is { } retry && (retry < 0 || retry > tick + day || council.LastElectionOutcome is null ||
                    retry != council.LastElectionOutcome.ResolvedTick + day || council.Election is not null || council.FallbackReason != "candidate" ||
                    council.LastElectionOutcome.SelectedMemberIds.Count + council.LastElectionOutcome.RetainedMemberIds.Count >= 3))
                throw new InvalidDataException("Town councillors, willingness or retry state do not match their saved terms and adult residents.");
            var halls = (state.WorldSimulation?.Buildings ?? []).Where(building => building.TownId == town.Id &&
                (state.WorldContent?.Buildings ?? []).Any(definition => definition.CanonicalId == building.DefinitionId &&
                    definition.Tags.Contains("town_hall", StringComparer.Ordinal))).ToArray();
            if (halls.Length > 1 || halls.Any(hall => hall.HouseholdId is not null) || halls.Length == 0 &&
                (council.Ballot is not null || council.Election is not null || council.TermStartedTick is not null || council.CandidateRegister.Count > 0))
                throw new InvalidDataException("Civic ballots, candidacies and terms require their actual shared Town Hall.");
            foreach (var law in council.Laws)
                if (!ValidTownLawKey(law.Key) || !ValidTownRuleText(law.Text) || !people.ContainsKey(law.ProposerId ?? "") ||
                    law.AdoptedTick < 0 || law.AdoptedTick > tick)
                    throw new InvalidDataException("A saved Town law is invalid.");
            foreach (var closed in council.ProposalOutcomes)
                if (!ValidProposalOutcome(closed, people.Keys.ToHashSet(StringComparer.Ordinal), tick))
                    throw new InvalidDataException("A saved council request retry is invalid.");
            if (council.Election is { } election) ValidateTownElection(council, election, adults, Adult, people.Keys.ToHashSet(StringComparer.Ordinal), tick, day, term);
            if (council.Ballot is { } ballot && (!ValidTownLawKey(ballot.Key) || !ValidTownRuleText(ballot.Text) ||
                !people.ContainsKey(ballot.ProposerId ?? "") || ballot.ProposedTick < 0 || ballot.ProposedTick > tick || ballot.ExpiryTick <= tick ||
                ballot.ExpiryTick - ballot.ProposedTick != day || ballot.GoverningForm != council.GoverningForm ||
                ballot.FoodPolicy is not (null or "open" or "essential_first") ||
                ballot.Key == "shared_food" && (ballot.FoodPolicy is null || ballot.Repeal) || ballot.Key != "shared_food" && ballot.FoodPolicy is not null ||
                !UniqueIds(ballot.Electorate) || !UniqueIds(ballot.Approvals) || !UniqueIds(ballot.Rejections) || ballot.Electorate.Count == 0 ||
                !ballot.Electorate.ToHashSet(StringComparer.Ordinal).SetEquals(council.MemberIds) || ballot.Electorate.Any(id => !Adult(id)) ||
                ballot.Approvals.Concat(ballot.Rejections).Distinct(StringComparer.Ordinal).Count() != ballot.Approvals.Count + ballot.Rejections.Count ||
                ballot.Approvals.Concat(ballot.Rejections).Any(id => !ballot.Electorate.Contains(id, StringComparer.Ordinal))))
                throw new InvalidDataException("The saved Town rule ballot is invalid.");
        }
    }

    private static void ValidateTownElection(TownCouncilState council, TownCouncilElection election, HashSet<string> adults,
        Func<string, bool> adult, HashSet<string> people, long tick, long day, long term)
    {
        if (adults.Count <= 3 ||
            election.Kind is not ("initial" or "regular" or "replacement") || election.StartedTick < 0 || election.StartedTick > tick ||
            election.ExpiryTick <= tick || election.ExpiryTick - election.StartedTick != (election.IsRunoff ? 2L : 1L) * day ||
            election.IsRunoff && tick < election.StartedTick + day || election.TargetTermExpiryTick - election.TargetTermStartedTick != term ||
            election.Kind == "initial" && (election.TargetTermStartedTick != election.StartedTick + day || council.TermStartedTick is not null) ||
            election.Kind == "regular" && (election.TargetTermStartedTick != council.TermExpiryTick || election.StartedTick != election.TargetTermStartedTick - day) ||
            election.Kind == "replacement" && (election.TargetTermStartedTick != council.TermStartedTick || election.TargetTermExpiryTick != council.TermExpiryTick) ||
            !UniqueIds(election.Electorate) || !UniqueIds(election.Candidates) || !UniqueIds(election.SelectedMemberIds) || !UniqueIds(election.RetainedMemberIds) ||
            !UniqueIds(election.MainCandidateIds) || !UniqueIds(election.MainElectorate) || !UniqueIds(election.MainSupportedCandidateIds) ||
            !UniqueIds(election.OriginalMainSelectedMemberIds) || !UniqueIds(election.OriginalMainCutoffCandidateIds) ||
            !UniqueIds(election.OriginalMainRetainedMemberIds) ||
            election.Candidacies is null || election.Candidacies.Any(item => item is null) ||
            election.Candidacies.Select(item => item.CandidateId).Distinct(StringComparer.Ordinal).Count() != election.Candidacies.Count ||
            election.MainVotes is null || election.Votes is null ||
            election.AvailableSeats is < 1 or > 3 || election.AvailableSeats != 3 - election.SelectedMemberIds.Count - election.RetainedMemberIds.Count ||
            election.MainAvailableSeats is < 1 or > 3 || election.MainAvailableSeats > 3 - election.RetainedMemberIds.Count ||
            !election.IsRunoff && election.MainAvailableSeats != election.AvailableSeats ||
            election.RetainedMemberIds.Any(id => !council.MemberIds.Contains(id, StringComparer.Ordinal)) ||
            election.Kind == "replacement" && election.SelectedMemberIds.Any(id => !council.MemberIds.Contains(id, StringComparer.Ordinal)) ||
            election.Kind != "replacement" && election.RetainedMemberIds.Count != 0 ||
            !election.IsRunoff && (election.SelectedMemberIds.Count != 0 || election.MainVotes.Count != 0 ||
                election.MainSupportedCandidateIds.Count != 0 || election.MainCandidateIds.Count != 0 || election.MainElectorate.Count != 0) ||
            !election.IsRunoff && (election.OriginalMainSelectedMemberIds.Count != 0 || election.OriginalMainCutoffCandidateIds.Count != 0 ||
                election.OriginalMainRetainedMemberIds.Count != 0) ||
            election.Candidates.Concat(election.SelectedMemberIds).Concat(election.RetainedMemberIds).Distinct(StringComparer.Ordinal).Count() !=
                election.Candidates.Count + election.SelectedMemberIds.Count + election.RetainedMemberIds.Count ||
            !election.Candidates.Concat(election.SelectedMemberIds).ToHashSet(StringComparer.Ordinal)
                .SetEquals(election.Candidacies.Select(item => item.CandidateId)) ||
            election.Candidacies.Any(item => item.DeclaredTick < 0 || item.DeclaredTick > election.StartedTick ||
                !(election.Kind == "replacement" ? item.ReplacementWilling : item.FullTermWilling) ||
                !ValidReplacementConsent(item, tick, term) || election.Kind == "replacement" &&
                    (item.ReplacementTermStartedTick != election.TargetTermStartedTick || item.ReplacementTermExpiryTick != election.TargetTermExpiryTick ||
                        item.ReplacementDeclaredTick > election.StartedTick) ||
                !council.CandidateRegister.Any(registered => registered.CandidateId == item.CandidateId &&
                    (election.Kind == "replacement" ? registered.ReplacementWilling : registered.FullTermWilling))) ||
            election.Electorate.Concat(election.Candidates).Concat(election.SelectedMemberIds).Concat(election.RetainedMemberIds).Any(id => !adult(id)) ||
            !ValidTownVotes(election.Votes, election.Electorate, election.Candidates, election.AvailableSeats) ||
            !ValidTownVotes(election.MainVotes, election.MainElectorate, election.MainCandidateIds, election.MainAvailableSeats) ||
            election.MainElectorate.Concat(election.MainCandidateIds).Any(id => !people.Contains(id)) ||
            election.IsRunoff && (election.Candidates.Concat(election.SelectedMemberIds).Any(id => !election.MainSupportedCandidateIds.Contains(id, StringComparer.Ordinal)) ||
                election.MainSupportedCandidateIds.Any(id => !adult(id) || !election.MainVotes.Any(vote => vote.CandidateIds.Contains(id, StringComparer.Ordinal)))))
            throw new InvalidDataException("The saved Town contest, frozen candidacies, main support or runoff is invalid.");
        if (election.IsRunoff)
            ValidateOriginalTownRound(election.MainCandidateIds, election.MainVotes, election.MainAvailableSeats,
                election.OriginalMainSelectedMemberIds, election.OriginalMainCutoffCandidateIds, election.OriginalMainRetainedMemberIds,
                election.SelectedMemberIds, election.Candidates, election.RetainedMemberIds, people, true);
    }

    private static void ValidateTownElectionOutcome(string seed, string townId, TownElectionOutcome outcome, HashSet<string> people, long tick, long day)
    {
        if (outcome.Kind is not ("initial" or "regular" or "replacement") || outcome.StartedTick < 0 ||
            outcome.ResolvedTick < outcome.StartedTick + day || outcome.ResolvedTick > tick ||
            !UniqueIds(outcome.SupportedCandidateIds) || !UniqueIds(outcome.SelectedMemberIds) || !UniqueIds(outcome.DrawnMemberIds) ||
            !UniqueIds(outcome.DrawSlate) || !UniqueIds(outcome.RetainedMemberIds) || !UniqueIds(outcome.MainCandidateIds) || !UniqueIds(outcome.MainElectorate) ||
            !UniqueIds(outcome.RunoffCandidateIds) || !UniqueIds(outcome.RunoffElectorate) || !UniqueIds(outcome.BeforeRunoffSelectedIds) ||
            !UniqueIds(outcome.OriginalMainSelectedMemberIds) || !UniqueIds(outcome.OriginalMainCutoffCandidateIds) ||
            !UniqueIds(outcome.OriginalMainRetainedMemberIds) ||
            !UniqueIds(outcome.AdultIdsAtResolution) || outcome.AdultIdsAtResolution.Count <= 3 || outcome.AdultIdsAtResolution.Any(id => !people.Contains(id)) ||
            outcome.RegisterAtResolution is null || outcome.RegisterAtResolution.Any(item => item is null) ||
            outcome.RegisterAtResolution.Select(item => item.CandidateId).Distinct(StringComparer.Ordinal).Count() != outcome.RegisterAtResolution.Count ||
            outcome.RegisterAtResolution.Any(item => !outcome.AdultIdsAtResolution.Contains(item.CandidateId, StringComparer.Ordinal) ||
                item.DeclaredTick < 0 || item.DeclaredTick > outcome.ResolvedTick || !item.FullTermWilling && !item.ReplacementWilling ||
                !ValidReplacementConsent(item, outcome.ResolvedTick, checked(10L * day))) ||
            outcome.MainElectorate.Concat(outcome.MainCandidateIds).Concat(outcome.RetainedMemberIds).Any(id => !people.Contains(id)) ||
            outcome.MainAvailableSeats is < 1 or > 3 || outcome.MainAvailableSeats > 3 - outcome.RetainedMemberIds.Count ||
            !ValidTownVotes(outcome.MainVotes, outcome.MainElectorate, outcome.MainCandidateIds, outcome.MainAvailableSeats) ||
            !ValidTownVotes(outcome.RunoffVotes, outcome.RunoffElectorate, outcome.RunoffCandidateIds, 3 - outcome.RetainedMemberIds.Count - outcome.BeforeRunoffSelectedIds.Count) ||
            outcome.BeforeRunoffSelectedIds.Concat(outcome.RunoffCandidateIds).Any(id => !outcome.SupportedCandidateIds.Contains(id, StringComparer.Ordinal)) ||
            outcome.SupportedCandidateIds.Any(id => !outcome.MainVotes.Any(vote => vote.CandidateIds.Contains(id, StringComparer.Ordinal))) ||
            outcome.SelectedMemberIds.Concat(outcome.DrawSlate).Any(id => !outcome.SupportedCandidateIds.Contains(id, StringComparer.Ordinal)) ||
            outcome.SelectedMemberIds.Concat(outcome.RetainedMemberIds).Distinct(StringComparer.Ordinal).Count() != outcome.SelectedMemberIds.Count + outcome.RetainedMemberIds.Count ||
            outcome.SelectedMemberIds.Count + outcome.RetainedMemberIds.Count > 3 || outcome.Kind != "replacement" && outcome.RetainedMemberIds.Count > 0 ||
            outcome.DrawnMemberIds.Any(id => !outcome.SelectedMemberIds.Contains(id, StringComparer.Ordinal)) ||
            outcome.DrawSlate.Count > 0 && outcome.DrawSlate.Count <= outcome.DrawnMemberIds.Count ||
            !DrawTownCandidates(seed, townId, outcome.StartedTick, outcome.DrawSlate, outcome.DrawnMemberIds.Count)
                .SequenceEqual(outcome.DrawnMemberIds, StringComparer.Ordinal))
            throw new InvalidDataException("The recorded Town election outcome or fair draw is invalid.");
        var runoff = outcome.ResolvedTick >= outcome.StartedTick + 2 * day;
        ValidateOriginalTownRound(outcome.MainCandidateIds, outcome.MainVotes, outcome.MainAvailableSeats,
            outcome.OriginalMainSelectedMemberIds, outcome.OriginalMainCutoffCandidateIds, outcome.OriginalMainRetainedMemberIds,
            runoff ? outcome.BeforeRunoffSelectedIds : outcome.SelectedMemberIds,
            runoff ? outcome.RunoffCandidateIds : [], outcome.RetainedMemberIds, people, runoff);
        var ranked = RankTownCandidates(runoff ? outcome.RunoffCandidateIds : outcome.MainCandidateIds,
            runoff ? outcome.RunoffVotes : outcome.MainVotes, outcome.SupportedCandidateIds,
            3 - outcome.RetainedMemberIds.Count, outcome.BeforeRunoffSelectedIds);
        if ((!runoff && (outcome.BeforeRunoffSelectedIds.Count != 0 || outcome.RunoffVotes.Count != 0 || outcome.RunoffElectorate.Count != 0 || outcome.RunoffCandidateIds.Count != 0 || ranked.CutoffTie.Length > 0)) ||
            !ranked.CutoffTie.SequenceEqual(outcome.DrawSlate, StringComparer.Ordinal) ||
            !ranked.Selected.Concat(outcome.DrawnMemberIds).SequenceEqual(outcome.SelectedMemberIds, StringComparer.Ordinal) ||
            outcome.DrawSlate.Count > 0 && outcome.DrawnMemberIds.Count == 0)
            throw new InvalidDataException("The recorded draw does not resolve the supported runoff cutoff.");
    }

    private static void ValidateOriginalTownRound(IReadOnlyList<string> candidates, IReadOnlyList<TownElectionVote> votes,
        int seats, IReadOnlyList<string> originalSelected, IReadOnlyList<string> originalCutoff, IReadOnlyList<string> originalRetained,
        IReadOnlyList<string> selected, IReadOnlyList<string> cutoff, IReadOnlyList<string> retained, HashSet<string> people, bool runoff)
    {
        var supported = candidates.Where(id => votes.Any(vote => vote.CandidateIds.Contains(id, StringComparer.Ordinal))).ToArray();
        var ranked = RankTownCandidates(candidates, votes, supported, seats, []);
        if (originalRetained.Count != 3 - seats || originalRetained.Any(id => !people.Contains(id)) ||
            !ranked.Selected.SequenceEqual(originalSelected, StringComparer.Ordinal) ||
            !ranked.CutoffTie.SequenceEqual(originalCutoff, StringComparer.Ordinal) ||
            runoff != (originalCutoff.Count > 0) || selected.Any(id => !originalSelected.Contains(id, StringComparer.Ordinal)) ||
            cutoff.Any(id => !originalCutoff.Contains(id, StringComparer.Ordinal)) || retained.Any(id => !originalRetained.Contains(id, StringComparer.Ordinal)))
            throw new InvalidDataException("The saved runoff must preserve the main round's settled seats and actual cutoff tie.");
    }

    private static bool ValidReplacementConsent(TownCouncilCandidacy item, long tick, long term) =>
        item.ReplacementWilling
            ? item.ReplacementTermStartedTick is >= 0 && item.ReplacementTermExpiryTick - item.ReplacementTermStartedTick == term &&
                item.ReplacementDeclaredTick is >= 0 && item.ReplacementDeclaredTick <= tick &&
                item.ReplacementDeclaredTick >= item.ReplacementTermStartedTick && item.ReplacementDeclaredTick < item.ReplacementTermExpiryTick
            : item.ReplacementTermStartedTick is null && item.ReplacementTermExpiryTick is null && item.ReplacementDeclaredTick is null;

    private static bool ValidProposalOutcome(TownProposalOutcome closed, HashSet<string> people, long tick)
    {
        var key = closed.RequestKey?.Split('|');
        var context = closed.Circumstances;
        return key is { Length: 3 } && ValidTownLawKey(key[0]) && key[1] is "" or "open" or "essential_first" &&
            bool.TryParse(key[2], out var repeal) && (key[0] == "shared_food" ? key[1] != "" && !repeal : key[1] == "") &&
            closed.ClosedTick >= 0 && closed.ClosedTick <= tick && context is not null &&
            context.GoverningForm is "collective" or "representative" && UniqueIds(context.MemberIds) && context.MemberIds.Count > 0 &&
            context.MemberIds.All(people.Contains) && (context.GoverningForm != "representative" || context.MemberIds.Count <= 3) &&
            (context.SameLawAdoptedTick is null || context.SameLawAdoptedTick >= 0 && context.SameLawAdoptedTick <= closed.ClosedTick) &&
            (key[0] == "shared_food" ? context.AdultPopulation >= 0 && context.CommunalFoodQuantity >= 0 :
                context.AdultPopulation is null && context.CommunalFoodQuantity is null);
    }

    private static bool UniqueIds(IReadOnlyList<string>? ids) => ids is not null && ids.All(id => !string.IsNullOrWhiteSpace(id)) &&
        ids.Distinct(StringComparer.Ordinal).Count() == ids.Count;
    private static bool ValidTownRuleText(string? text) => !string.IsNullOrWhiteSpace(text) && text.Length <= 280 && !text.Any(char.IsControl);
    private static bool ValidTownVotes(IReadOnlyList<TownElectionVote>? votes, IReadOnlyList<string> voters, IReadOnlyList<string> candidates, int seats) =>
        votes is not null && votes.All(vote => vote is not null) && votes.Select(vote => vote.VoterId).Distinct(StringComparer.Ordinal).Count() == votes.Count &&
        votes.All(vote => voters.Contains(vote.VoterId, StringComparer.Ordinal) && UniqueIds(vote.CandidateIds) && vote.CandidateIds.Count <= seats &&
            vote.CandidateIds.All(id => candidates.Contains(id, StringComparer.Ordinal)));
}

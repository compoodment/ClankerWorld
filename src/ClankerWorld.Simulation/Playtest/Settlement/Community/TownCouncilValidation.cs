using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private static void ValidateTownCouncils(PrivateWorldRuntimeState state)
    {
        if (state.TownCouncils is not { } councils) return;
        if (state.SchemaVersion < 33 && councils.Count > 0)
            throw new InvalidDataException("Physical Town councils require private-world schema 33.");
        var tick = state.Society.Society.WorldTick;
        var towns = (state.Towns ?? []).ToDictionary(town => town.Id, StringComparer.Ordinal);
        var people = state.Society.Society.Inhabitants.ToDictionary(person => person.Id, StringComparer.Ordinal);
        var day = state.WorldSystems?.Config.TicksPerDay ?? 1440;
        var year = checked((long)day * (state.WorldSystems?.Config.DaysPerYear ?? 365));
        if (councils.Count > towns.Count || councils.Select(council => council.TownId).Distinct(StringComparer.Ordinal).Count() != councils.Count)
            throw new InvalidDataException("A Town has duplicate or unassigned councils.");
        foreach (var council in councils)
        {
            if (!towns.TryGetValue(council.TownId, out var town) || council.FoodPolicy is not ("open" or "essential_first") ||
                council.LastResolutionTick < 0 || council.LastResolutionTick > tick || council.MemberIds is null ||
                council.MemberIds.Distinct(StringComparer.Ordinal).Count() != council.MemberIds.Count || council.Laws is null ||
                council.Laws.Count > 65 || council.Laws.Select(law => law.Key).Distinct(StringComparer.Ordinal).Count() != council.Laws.Count)
                throw new InvalidDataException("The saved Town council is invalid.");
            bool Adult(string id) => town.ResidentIds.Contains(id, StringComparer.Ordinal) && people.TryGetValue(id, out var person) &&
                person.Status == SocietyInhabitantStatus.Active && person.AgeBand is SocietyAgeBand.Adult or SocietyAgeBand.Elder;
            var adults = town.ResidentIds.Where(Adult).ToHashSet(StringComparer.Ordinal);
            if (council.MemberIds.Any(id => !Adult(id)) ||
                council.TermStartedTick.HasValue != council.TermExpiryTick.HasValue ||
                council.TermStartedTick is null && !adults.SetEquals(council.MemberIds) ||
                council.TermStartedTick is { } start && (start < 0 || start > tick || council.TermExpiryTick - start != year || council.MemberIds.Count > 3) ||
                adults.Count < 8 && (council.TermStartedTick is not null || council.Election is not null || !adults.SetEquals(council.MemberIds)))
                throw new InvalidDataException("Town councillors must be current adult residents with valid saved terms.");
            var halls = (state.WorldSimulation?.Buildings ?? []).Where(building => building.TownId == town.Id &&
                (state.WorldContent?.Buildings ?? []).Any(definition => definition.CanonicalId == building.DefinitionId &&
                    definition.Tags.Contains("town_hall", StringComparer.Ordinal))).ToArray();
            if (halls.Length > 1 || halls.Any(hall => hall.HouseholdId is not null) ||
                halls.Length == 0 && (council.Ballot is not null || council.Election is not null || council.TermStartedTick is not null))
                throw new InvalidDataException("Civic ballots and representative terms require their actual shared Town Hall.");
            foreach (var law in council.Laws)
                if (!ValidTownLawKey(law.Key) || string.IsNullOrWhiteSpace(law.Text) || law.Text.Length > 280 || law.Text.Any(char.IsControl) ||
                    !people.ContainsKey(law.ProposerId) || law.AdoptedTick < 0 || law.AdoptedTick > tick)
                    throw new InvalidDataException("A saved Town law is invalid.");
            if (council.Election is { } election)
            {
                if (adults.Count < 8 || election.StartedTick < 0 || election.StartedTick > tick || election.ExpiryTick - election.StartedTick != day ||
                    election.Electorate is null || election.Candidates is null || election.Votes is null ||
                    election.Electorate.Distinct(StringComparer.Ordinal).Count() != election.Electorate.Count ||
                    election.Candidates.Distinct(StringComparer.Ordinal).Count() != election.Candidates.Count ||
                    election.Electorate.Concat(election.Candidates).Any(id => !Adult(id)) ||
                    election.Votes.Select(vote => vote.VoterId).Distinct(StringComparer.Ordinal).Count() != election.Votes.Count ||
                    election.Votes.Any(vote => !election.Electorate.Contains(vote.VoterId, StringComparer.Ordinal) || vote.CandidateIds is null ||
                        vote.CandidateIds.Count > 3 || vote.CandidateIds.Distinct(StringComparer.Ordinal).Count() != vote.CandidateIds.Count ||
                        vote.CandidateIds.Any(id => !election.Candidates.Contains(id, StringComparer.Ordinal))))
                    throw new InvalidDataException("The saved Town election is invalid.");
            }
            if (council.Ballot is { } ballot && (!ValidTownLawKey(ballot.Key) || string.IsNullOrWhiteSpace(ballot.Text) || ballot.Text.Length > 280 ||
                ballot.Text.Any(char.IsControl) || !people.ContainsKey(ballot.ProposerId) || ballot.ProposedTick < 0 || ballot.ProposedTick > tick ||
                ballot.ExpiryTick - ballot.ProposedTick != day || ballot.FoodPolicy is not (null or "open" or "essential_first") ||
                ballot.Key == "shared_food" && (ballot.FoodPolicy is null || ballot.Repeal) || ballot.Key != "shared_food" && ballot.FoodPolicy is not null ||
                ballot.Electorate is null || ballot.Approvals is null || ballot.Rejections is null || ballot.Electorate.Count == 0 ||
                ballot.Electorate.Distinct(StringComparer.Ordinal).Count() != ballot.Electorate.Count || ballot.Electorate.Any(id => !people.ContainsKey(id)) ||
                ballot.Approvals.Concat(ballot.Rejections).Distinct(StringComparer.Ordinal).Count() != ballot.Approvals.Count + ballot.Rejections.Count ||
                ballot.Approvals.Concat(ballot.Rejections).Any(id => !ballot.Electorate.Contains(id, StringComparer.Ordinal))))
                throw new InvalidDataException("The saved Town rule ballot is invalid.");
        }
    }
}

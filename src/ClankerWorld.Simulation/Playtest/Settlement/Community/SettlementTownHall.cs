using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private List<TownCouncilState> townCouncils = [];
    public IReadOnlyList<TownCouncilState> TownCouncils => townCouncils.OrderBy(item => item.TownId, StringComparer.Ordinal).ToArray();
    private long CivicVoteLifetime => worldSystems.Config.TicksPerDay;
    private long CouncilTermLifetime => checked((long)worldSystems.Config.TicksPerDay * worldSystems.Config.DaysPerYear);

    private string[] TownAdultResidents(string townId) => society.Checkpoint.Inhabitants.Where(person =>
            person.Status == SocietyInhabitantStatus.Active && TownForResident(person.Id) == townId &&
            person.AgeBand is SocietyAgeBand.Adult or SocietyAgeBand.Elder)
        .Select(person => person.Id).Order(StringComparer.Ordinal).ToArray();

    private PlacedBuilding? TownHallFor(string townId) => worldSimulation.Buildings.FirstOrDefault(building =>
        building.TownId == townId && worldContent.Buildings.Any(definition => definition.CanonicalId == building.DefinitionId &&
            definition.Tags.Contains("town_hall", StringComparer.Ordinal)));

    private void SetTownCouncil(TownCouncilState updated) => townCouncils = townCouncils
        .Where(item => item.TownId != updated.TownId).Append(updated).OrderBy(item => item.TownId, StringComparer.Ordinal).ToList();

    private bool AtTownHall(string actor, string townId) => inhabitants.TryGetValue(actor, out var physical) &&
        TownForResident(actor) == townId && TownAdultResidents(townId).Contains(actor, StringComparer.Ordinal) &&
        TownHallFor(townId) is { } hall && TownHallTiles(hall).Any(tile => IsWithinInteractionRange(physical.Position, tile, 1));

    private IEnumerable<ClankerWorld.Simulation.Harness.GridPoint> TownHallTiles(PlacedBuilding hall) =>
        WorldContentSimulationRules.Footprint(worldContent.Buildings.Single(definition => definition.CanonicalId == hall.DefinitionId), hall);

    private int TownSharedFoodQuantity(string townId)
    {
        var owners = society.Checkpoint.Inhabitants.Where(person => TownForResident(person.Id) == townId)
            .Select(person => person.HouseholdId).Where(id => id is not null).ToHashSet(StringComparer.Ordinal);
        return society.Checkpoint.Inventory.Lots.Where(lot => (owners.Contains(lot.OwnerId) || lot.OwnerId == townId) &&
            lot.GroundPosition is null && lot.StorageBuildingId is not null && lot.FreshnessBasisPoints > 0 &&
            worldSimulation.Buildings.Any(building => building.InstanceId == lot.StorageBuildingId && building.TownId == townId) &&
            lot.ConditionBasisPoints > 0 && IsEdibleFood(lot.ItemKind)).Sum(AvailableLotQuantity);
    }

    private string? ProposedTownFoodPolicy(string actor)
    {
        var townId = TownForResident(actor);
        var current = townCouncils.FirstOrDefault(item => item.TownId == townId);
        if (current is null || !current.MemberIds.Contains(actor, StringComparer.Ordinal) || current.Ballot is not null ||
            current.Election is not null || TownHallFor(current.TownId) is null || WorldTick - current.LastResolutionTick < 300)
            return null;
        var population = towns.Single(town => town.Id == current.TownId).ResidentIds.Count;
        var food = TownSharedFoodQuantity(current.TownId);
        return current.FoodPolicy == "open" && food < population * 2 ? "essential_first"
            : current.FoodPolicy == "essential_first" && food >= population * 4 ? "open" : null;
    }

    private void AdvanceTownCouncils()
    {
        townCouncils.RemoveAll(current => !towns.Any(town => town.Id == current.TownId));
        foreach (var town in towns.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            var adults = TownAdultResidents(town.Id);
            var current = townCouncils.FirstOrDefault(item => item.TownId == town.Id) ?? new(town.Id, "open", WorldTick, adults, Laws: []);
            if (current.Election is { } pendingElection)
            {
                var eligible = adults.ToHashSet(StringComparer.Ordinal);
                current = current with
                {
                    Election = pendingElection with
                    {
                        Electorate = pendingElection.Electorate.Where(eligible.Contains).ToArray(),
                        Candidates = pendingElection.Candidates.Where(eligible.Contains).ToArray(),
                        Votes = pendingElection.Votes.Where(vote => eligible.Contains(vote.VoterId))
                        .Select(vote => vote with { CandidateIds = vote.CandidateIds.Where(eligible.Contains).ToArray() }).ToArray(),
                    }
                };
            }
            if (adults.Length < 8)
            {
                current = current with { MemberIds = adults, TermStartedTick = null, TermExpiryTick = null, Election = null };
            }
            else
            {
                var eligibleMembers = current.MemberIds.Intersect(adults, StringComparer.Ordinal).ToArray();
                var needsElection = current.TermExpiryTick is null || WorldTick >= current.TermExpiryTick || eligibleMembers.Length < 3;
                current = current with { MemberIds = eligibleMembers };
                if (needsElection && current.Election is null && TownHallFor(town.Id) is not null)
                {
                    current = current with { Election = new(WorldTick, checked(WorldTick + CivicVoteLifetime), adults, adults, []), Ballot = null };
                    AppendEvent("town_election_started", town.Id);
                }
                if (current.Election is { } election && (WorldTick > election.ExpiryTick ||
                    election.Electorate.Intersect(adults, StringComparer.Ordinal).All(id => election.Votes.Any(vote => vote.VoterId == id))))
                {
                    var voters = election.Electorate.Intersect(adults, StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
                    var selected = election.Candidates.Intersect(adults, StringComparer.Ordinal)
                        .OrderByDescending(id => election.Votes.Count(vote => voters.Contains(vote.VoterId) && vote.CandidateIds.Contains(id, StringComparer.Ordinal)))
                        .ThenByDescending(id => current.MemberIds.Contains(id, StringComparer.Ordinal)).ThenBy(id => id, StringComparer.Ordinal).Take(3).ToArray();
                    // A departed candidate can leave fewer than three; restart with the current resident electorate.
                    if (selected.Length == 3)
                    {
                        current = current with
                        {
                            MemberIds = selected,
                            TermStartedTick = WorldTick,
                            TermExpiryTick = checked(WorldTick + CouncilTermLifetime),
                            Election = null
                        };
                        AppendEvent("town_council_elected", $"{town.Id}|{string.Join(',', selected)}");
                    }
                    else current = current with { Election = null };
                }
            }
            current = ResolveTownLawBallot(current);
            SetTownCouncil(current);
            foreach (var observer in adults.Where(actor => AtTownHall(actor, town.Id))) ObserveTownRules(observer, current);
        }
    }

    private void ObserveTownRules(string actor, TownCouncilState current)
    {
        foreach (var law in current.Laws ?? []) RememberTownRule(actor, current.TownId, law.Key, law.AdoptedTick, law.ProposerId,
            "Read this Town rule at its Hall: " + law.Text);
        if (current.Ballot is { } ballot) RememberTownRule(actor, current.TownId, "proposal_" + ballot.Key,
            ballot.ProposedTick, ballot.ProposerId, "Heard this proposed Town rule at its Hall: " + ballot.Text);
    }

    private void RememberTownRule(string actor, string townId, string key, long learnedVersion, string proposer, string summary)
    {
        var id = $"town-rule:{townId}:{key}:{learnedVersion}:{actor}";
        if (!society.Checkpoint.Memories.Any(memory => memory.Id == id))
            society.Apply(checkpoint => SocietyFixture.RecordSocialMemory(checkpoint, new(id, actor, proposer, summary, "public", WorldTick)));
    }

    private TownCouncilState ResolveTownLawBallot(TownCouncilState current)
    {
        if (current.Ballot is not { } ballot) return current;
        var electorate = ballot.Electorate.Intersect(current.MemberIds, StringComparer.Ordinal).ToArray();
        var required = current.MemberIds.Count / 2 + 1;
        var yes = ballot.Approvals.Count(id => electorate.Contains(id, StringComparer.Ordinal));
        var no = ballot.Rejections.Count(id => electorate.Contains(id, StringComparer.Ordinal));
        if (electorate.Length > 0 && yes >= required)
        {
            var laws = (current.Laws ?? []).Where(law => law.Key != ballot.Key).ToList();
            if (!ballot.Repeal) laws.Add(new(ballot.Key, ballot.Text, ballot.ProposerId, WorldTick));
            current = current with
            {
                FoodPolicy = ballot.FoodPolicy ?? current.FoodPolicy,
                Laws = laws.OrderBy(law => law.Key, StringComparer.Ordinal).ToArray(),
                Ballot = null,
                LastResolutionTick = WorldTick
            };
            AppendEvent("town_law_adopted", $"{current.TownId}|{ballot.Key}|{ballot.Repeal}");
        }
        else if (electorate.Length == 0 || no > current.MemberIds.Count - required || WorldTick > ballot.ExpiryTick)
        {
            current = current with { Ballot = null, LastResolutionTick = WorldTick };
            AppendEvent("town_law_rejected", $"{current.TownId}|{ballot.Key}");
        }
        return current;
    }

    private CivicActionResult CivicAction(Func<CivicActionResult> action)
    {
        gate.Wait();
        try { return action(); }
        finally { gate.Release(); }
    }

    public CivicActionResult ProposeTownLaw(string actor, string townId, string key, string text,
        string? foodPolicy = null, bool repeal = false) => CivicAction(() => ProposeTownLawCore(actor, townId, key, text, foodPolicy, repeal));

    private CivicActionResult ProposeTownLawCore(string actor, string townId, string key, string text, string? foodPolicy, bool repeal)
    {
        AdvanceTownCouncils();
        var current = townCouncils.FirstOrDefault(item => item.TownId == townId);
        if (current is null || !AtTownHall(actor, townId) || !current.MemberIds.Contains(actor, StringComparer.Ordinal) ||
            current.Election is not null || current.Ballot is not null || !ValidTownLawKey(key) ||
            string.IsNullOrWhiteSpace(text) || text.Length > 280 || text.Any(char.IsControl) ||
            foodPolicy is not (null or "open" or "essential_first") ||
            key == "shared_food" && (foodPolicy is null || repeal) || key != "shared_food" && foodPolicy is not null ||
            repeal && !(current.Laws ?? []).Any(law => law.Key == key) ||
            !repeal && key != "shared_food" && (current.Laws ?? []).Count >= 64 && !(current.Laws ?? []).Any(law => law.Key == key))
            return new(false, "Only a current council member at this Town's Hall may propose a valid rule while no vote is pending.");
        var ballot = new TownLawBallot(key, text.Trim(), actor, foodPolicy, repeal, WorldTick,
            checked(WorldTick + CivicVoteLifetime), current.MemberIds.ToArray(), [actor], []);
        SetTownCouncil(ResolveTownLawBallot(current with { Ballot = ballot }));
        ObserveTownRules(actor, townCouncils.Single(item => item.TownId == townId));
        AppendEvent("town_law_proposed", $"{townId}|{key}|{actor}");
        return new(true);
    }

    public CivicActionResult VoteTownLaw(string actor, string townId, bool approve) => CivicAction(() => VoteTownLawCore(actor, townId, approve));
    private CivicActionResult VoteTownLawCore(string actor, string townId, bool approve)
    {
        AdvanceTownCouncils();
        var current = townCouncils.FirstOrDefault(item => item.TownId == townId);
        if (current?.Ballot is not { } ballot || !AtTownHall(actor, townId) ||
            !current.MemberIds.Contains(actor, StringComparer.Ordinal) || !ballot.Electorate.Contains(actor, StringComparer.Ordinal) ||
            ballot.Approvals.Concat(ballot.Rejections).Contains(actor, StringComparer.Ordinal))
            return new(false, "Only an eligible council member at this Town's Hall may cast one vote on the pending rule.");
        SetTownCouncil(ResolveTownLawBallot(current with
        {
            Ballot = approve
            ? ballot with { Approvals = ballot.Approvals.Append(actor).Order(StringComparer.Ordinal).ToArray() }
            : ballot with { Rejections = ballot.Rejections.Append(actor).Order(StringComparer.Ordinal).ToArray() }
        }));
        AppendEvent("town_law_vote_recorded", $"{townId}|{actor}|{approve}");
        RememberTownRule(actor, townId, "proposal_" + ballot.Key, ballot.ProposedTick, ballot.ProposerId,
            "Heard this proposed Town rule at its Hall: " + ballot.Text);
        return new(true);
    }

    public CivicActionResult VoteTownElection(string actor, string townId, IReadOnlyList<string> candidates) =>
        CivicAction(() => VoteTownElectionCore(actor, townId, candidates));
    private CivicActionResult VoteTownElectionCore(string actor, string townId, IReadOnlyList<string> candidates)
    {
        AdvanceTownCouncils();
        var current = townCouncils.FirstOrDefault(item => item.TownId == townId);
        if (current?.Election is not { } election || !AtTownHall(actor, townId) || candidates is null ||
            candidates.Count is < 1 or > 3 || candidates.Distinct(StringComparer.Ordinal).Count() != candidates.Count ||
            !election.Electorate.Contains(actor, StringComparer.Ordinal) || election.Votes.Any(vote => vote.VoterId == actor) ||
            candidates.Any(id => !election.Candidates.Contains(id, StringComparer.Ordinal) || !TownAdultResidents(townId).Contains(id, StringComparer.Ordinal)))
            return new(false, "An adult voter at this Town's Hall may cast one ballot naming up to three current adult residents.");
        SetTownCouncil(current with { Election = election with { Votes = election.Votes.Append(new(actor, candidates.Order(StringComparer.Ordinal).ToArray())).ToArray() } });
        AppendEvent("town_election_vote_recorded", $"{townId}|{actor}");
        AdvanceTownCouncils();
        return new(true);
    }

    private static bool ValidTownLawKey(string key) => !string.IsNullOrWhiteSpace(key) && key.Length <= 64 &&
        key.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '_');

    private bool HasTownCouncilDecision(string actor)
    {
        var current = townCouncils.FirstOrDefault(item => item.TownId == TownForResident(actor));
        return current is not null && TownHallFor(current.TownId) is not null && (
            current.Election is { } election && election.Electorate.Contains(actor, StringComparer.Ordinal) && !election.Votes.Any(vote => vote.VoterId == actor) ||
            current.Ballot is { } ballot && ballot.Electorate.Contains(actor, StringComparer.Ordinal) && !ballot.Approvals.Concat(ballot.Rejections).Contains(actor, StringComparer.Ordinal) ||
            ProposedTownFoodPolicy(actor) is not null);
    }

    private void AddTownCouncilCandidates(List<CognitionCandidate> candidates, string actor)
    {
        if (!HasTownCouncilDecision(actor) || NeedsUrgentWarmth(inhabitants[actor])) return;
        var current = townCouncils.Single(item => item.TownId == TownForResident(actor));
        if (current.Election is { } election && election.Electorate.Contains(actor, StringComparer.Ordinal) && !election.Votes.Any(vote => vote.VoterId == actor))
            candidates.Add(new("council_town_elect", "Attend the Town Hall and vote for three adult representatives for one game year.", 13));
        if (ProposedTownFoodPolicy(actor) is { } policy)
            candidates.Add(new("council_town_propose:" + policy, policy == "essential_first"
                ? "Attend the Town Hall and propose saving scarce shared food for hungry residents."
                : "Attend the Town Hall and propose restoring open shared-food access.", 14));
        if (current.Ballot is { } ballot && ballot.Electorate.Contains(actor, StringComparer.Ordinal) &&
            !ballot.Approvals.Concat(ballot.Rejections).Contains(actor, StringComparer.Ordinal))
        {
            var population = towns.Single(town => town.Id == current.TownId).ResidentIds.Count;
            var helps = ballot.FoodPolicy == "essential_first" ? TownSharedFoodQuantity(current.TownId) < population * 2
                : ballot.FoodPolicy == "open" ? TownSharedFoodQuantity(current.TownId) >= population * 4 : true;
            candidates.Add(new("council_town_vote_yes", AtTownHall(actor, current.TownId)
                ? "Support the proposed Town rule: " + ballot.Text
                : "Attend the Town Hall to hear and consider its pending rule.", helps ? 13 : 60));
            candidates.Add(new("council_town_vote_no", "Attend the Town Hall and keep the current rule.", helps ? 60 : 13));
        }
    }

    private void ApplyTownCouncilCandidate(string actor, string candidate)
    {
        if (TownForResident(actor) is not { } townId || TownHallFor(townId) is not { } hall) return;
        if (!AtTownHall(actor, townId))
        {
            var destination = TownHallTiles(hall).Select(tile => (Tile: tile,
                    Route: FindUnoccupiedRoute(actor, inhabitants[actor].Position, tile, 1)))
                .Where(option => option.Route.Count > 0).OrderBy(option => option.Route.Count).ThenBy(option => option.Tile.Y)
                .ThenBy(option => option.Tile.X).FirstOrDefault();
            if (destination.Route is not null) MoveToward(actor, inhabitants[actor], destination.Tile, "town_hall", 1);
            return;
        }
        if (candidate == "council_town_elect" && townCouncils.FirstOrDefault(item => item.TownId == townId)?.Election is { } election)
            VoteTownElectionCore(actor, townId, election.Candidates.Where(id => TownAdultResidents(townId).Contains(id, StringComparer.Ordinal))
                .OrderByDescending(ContributionScore).ThenBy(id => id, StringComparer.Ordinal).Take(3).ToArray());
        else if (candidate.StartsWith("council_town_propose:", StringComparison.Ordinal))
        {
            var policy = candidate[21..];
            if (policy == ProposedTownFoodPolicy(actor)) ProposeTownLawCore(actor, townId, "shared_food", policy == "essential_first"
                ? "Reserve scarce shared food for hungry residents." : "Allow residents to collect plentiful shared food.", policy, false);
        }
        else if (candidate is "council_town_vote_yes" or "council_town_vote_no")
            VoteTownLawCore(actor, townId, candidate == "council_town_vote_yes");
    }

    private void AddTownHallBuildingPlans(List<CognitionCandidate> candidates, string actor)
    {
        if (TownForResident(actor) is not { } townId || !AdultResident(actor) || TownHallFor(townId) is not null ||
            inhabitants.Values.Any(person => person.Project is { Stage: not ("completed" or "cancelled") } project &&
                TownForResident(person.InhabitantId) == townId && TownConstructionCandidateIds.TryParse(project.CandidateId, out var selection) &&
                selection.DefinitionId == TownHallContent.TownHall().CanonicalId) ||
            !worldContent.Buildings.Any(definition => definition.CanonicalId == TownHallContent.TownHall().CanonicalId)) return;
        var definition = TownHallContent.TownHall();
        if (!CanAcquireProjectInputs(definition.BuildCosts, HouseholdFor(actor) ?? actor, actor)) return;
        foreach (var site in TownLayoutService.RankConstructionSites(CreateTownLayoutContext(actor), definition))
            candidates.Add(new(TownConstructionCandidateIds.Building(definition.CanonicalId, site.Position),
                "Build the shared Town Hall so residents can meet, propose rules and hold elections.", 41,
                $"build-site:{site.Position.X},{site.Position.Y}"));
    }
}

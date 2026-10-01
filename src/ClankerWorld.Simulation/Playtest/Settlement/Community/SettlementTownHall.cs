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
        PassengerBoat(actor) is null &&
        TownForResident(actor) == townId && TownAdultResidents(townId).Contains(actor, StringComparer.Ordinal) &&
        TownHallFor(townId) is { } hall && TownHallTiles(hall).Any(tile => IsWithinInteractionRange(physical.Position, tile, 1));

    private IEnumerable<ClankerWorld.Simulation.Harness.GridPoint> TownHallTiles(PlacedBuilding hall) =>
        WorldContentSimulationRules.Footprint(worldContent.Buildings.Single(definition => definition.CanonicalId == hall.DefinitionId), hall);

    private int TownSharedFoodQuantity(string townId)
    {
        return society.Checkpoint.Inventory.Lots.Where(lot => lot.OwnerId == townId &&
            lot.GroundPosition is null && lot.StorageBuildingId is not null && lot.FreshnessBasisPoints > 0 &&
            worldSimulation.Buildings.Any(building => building.InstanceId == lot.StorageBuildingId && building.TownId == townId) &&
            lot.ConditionBasisPoints > 0 && IsEdibleFood(lot.ItemKind)).Sum(AvailableLotQuantity);
    }

    private string? ProposedTownFoodPolicy(string actor)
    {
        var townId = TownForResident(actor);
        var current = townCouncils.FirstOrDefault(item => item.TownId == townId);
        if (current is null || !TownAdultResidents(current.TownId).Contains(actor, StringComparer.Ordinal) || current.Ballot is not null ||
            current.Election is not null || !AtTownHall(actor, current.TownId) || WorldTick - current.LastResolutionTick < 300)
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
            var eligible = adults.ToHashSet(StringComparer.Ordinal);
            var current = townCouncils.FirstOrDefault(item => item.TownId == town.Id) ?? new(town.Id, "open", WorldTick, adults, Laws: []);
            if (current.Election is { } pending)
            {
                var candidates = pending.Candidates.Where(eligible.Contains).ToArray();
                var selected = pending.SelectedMemberIds.Where(eligible.Contains).ToArray();
                current = current with
                {
                    Election = pending with
                    {
                        Electorate = pending.Electorate.Where(eligible.Contains).ToArray(),
                        Candidates = candidates,
                        SelectedMemberIds = selected,
                        AvailableSeats = 3 - selected.Length,
                        Candidacies = pending.Candidacies.Where(item => eligible.Contains(item.CandidateId)).ToArray(),
                        Votes = pending.Votes.Where(vote => eligible.Contains(vote.VoterId))
                            .Select(vote => vote with { CandidateIds = vote.CandidateIds.Where(id => candidates.Contains(id, StringComparer.Ordinal)).ToArray() }).ToArray()
                    }
                };
            }
            current = adults.Length < 8
                ? current with { MemberIds = adults, TermStartedTick = null, TermExpiryTick = null, Election = null }
                : current with { MemberIds = current.TermStartedTick is null ? adults : current.MemberIds.Where(eligible.Contains).ToArray() };
            current = CancelTownLawForChangedCouncil(current);
            if (adults.Length >= 8)
            {
                // Vacant seats remain vacant until the annual election; death does not appoint a successor.
                if ((current.TermExpiryTick is null || WorldTick >= current.TermExpiryTick) &&
                    current.Election is null && TownHallFor(town.Id) is not null)
                {
                    current = current with { Election = new(WorldTick, checked(WorldTick + CivicVoteLifetime), adults, [], []) };
                    AppendEvent("town_election_started", town.Id);
                }
                if (current.Election is { } election && WorldTick >= election.ExpiryTick)
                    current = FinishTownElection(current, election);
            }
            current = ResolveTownLawBallot(CancelTownLawForChangedCouncil(current));
            SetTownCouncil(current);
            foreach (var observer in adults.Where(actor => AtTownHall(actor, town.Id))) ObserveTownRules(observer, current);
        }
    }

    private TownCouncilState CancelTownLawForChangedCouncil(TownCouncilState current)
    {
        if (current.Ballot is not { } ballot || ballot.Electorate.ToHashSet(StringComparer.Ordinal).SetEquals(current.MemberIds)) return current;
        AppendEvent("town_law_cancelled", $"{current.TownId}|{ballot.Key}|council_changed");
        return current with { Ballot = null };
    }

    private TownCouncilState FinishTownElection(TownCouncilState current, TownCouncilElection election)
    {
        var selected = election.SelectedMemberIds.ToList();
        string[] cutoffTie = [];
        var ranked = election.Candidates.Select(id => (Id: id, Count: election.Votes.Count(vote => vote.CandidateIds.Contains(id, StringComparer.Ordinal))))
            .Where(item => item.Count > 0).GroupBy(item => item.Count).OrderByDescending(group => group.Key);
        foreach (var group in ranked)
        {
            var names = group.Select(item => item.Id).Order(StringComparer.Ordinal).ToArray();
            if (names.Length <= 3 - selected.Count) selected.AddRange(names);
            else { cutoffTie = names; break; }
            if (selected.Count == 3) break;
        }
        if (!election.IsRunoff && cutoffTie.Length > 0)
        {
            var remaining = cutoffTie.Concat(selected).ToHashSet(StringComparer.Ordinal);
            AppendEvent("town_election_runoff_started", current.TownId);
            return current with
            {
                Election = election with
                {
                    IsRunoff = true,
                    ExpiryTick = checked(election.ExpiryTick + CivicVoteLifetime),
                    AvailableSeats = 3 - selected.Count,
                    SelectedMemberIds = selected.ToArray(),
                    Candidates = cutoffTie,
                    Candidacies = election.Candidacies.Where(item => remaining.Contains(item.CandidateId)).ToArray(),
                    Votes = []
                }
            };
        }
        AppendEvent("town_council_elected", $"{current.TownId}|{string.Join(',', selected)}");
        return current with
        {
            MemberIds = selected.ToArray(),
            TermStartedTick = election.ExpiryTick,
            TermExpiryTick = checked(election.ExpiryTick + CouncilTermLifetime),
            Election = null
        };
    }

    private void ObserveTownRules(string actor, TownCouncilState current)
    {
        foreach (var candidacy in current.Election?.Candidacies ?? [])
            RememberTownRule(actor, current.TownId, "candidate_" + candidacy.CandidateId.Replace(':', '_'),
                candidacy.DeclaredTick, candidacy.CandidateId, "Read this willing council candidate at its Hall: " +
                society.Checkpoint.GetInhabitant(candidacy.CandidateId).Name);
        foreach (var law in current.Laws ?? []) RememberTownRule(actor, current.TownId, law.Key, law.AdoptedTick, law.ProposerId,
            "Read this Town rule at its Hall: " + law.Text);
        if (current.Ballot is { } ballot) RememberTownRule(actor, current.TownId, "proposal_" + ballot.Key,
            ballot.ProposedTick, ballot.ProposerId, "Heard this proposed Town rule at its Hall: " + ballot.Text);
    }

    private void RememberTownRule(string actor, string townId, string key, long learnedVersion, string proposer, string summary)
    {
        var id = $"town-rule:{townId}:{key}:{learnedVersion}:{actor}";
        id += ":" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(summary)))[..12];
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
        else if (electorate.Length == 0 || no > current.MemberIds.Count - required || WorldTick >= ballot.ExpiryTick)
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
        if (current is null || !AtTownHall(actor, townId) ||
            current.MemberIds.Count == 0 || current.Election is not null || current.Ballot is not null || !ValidTownLawKey(key) ||
            string.IsNullOrWhiteSpace(text) || text.Length > 280 || text.Any(char.IsControl) ||
            foodPolicy is not (null or "open" or "essential_first") ||
            key == "shared_food" && (foodPolicy is null || repeal) || key != "shared_food" && foodPolicy is not null ||
            repeal && !(current.Laws ?? []).Any(law => law.Key == key) ||
            !repeal && key != "shared_food" && (current.Laws ?? []).Count >= 64 && !(current.Laws ?? []).Any(law => law.Key == key))
            return new(false, "An adult resident at this Town's Hall may propose a valid rule while no vote is pending.");
        var ballot = new TownLawBallot(key, text.Trim(), actor, foodPolicy, repeal, WorldTick,
            checked(WorldTick + CivicVoteLifetime), current.MemberIds.ToArray(), [], []);
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

    public CivicActionResult VolunteerTownCouncil(string actor, string townId, bool willing = true) =>
        CivicAction(() => VolunteerTownCouncilCore(actor, townId, willing));
    private CivicActionResult VolunteerTownCouncilCore(string actor, string townId, bool willing)
    {
        AdvanceTownCouncils();
        var current = townCouncils.FirstOrDefault(item => item.TownId == townId);
        if (current?.Election is not { } election || !AtTownHall(actor, townId) ||
            willing && (election.IsRunoff || election.Candidacies.Any(item => item.CandidateId == actor)) ||
            !willing && !election.Candidacies.Any(item => item.CandidateId == actor))
            return new(false, "An adult resident at this Town's Hall may volunteer during the election or withdraw an existing candidacy.");
        var selected = election.SelectedMemberIds.Where(id => id != actor).ToArray();
        var candidates = willing ? election.Candidates.Append(actor).Order(StringComparer.Ordinal).ToArray()
            : election.Candidates.Where(id => id != actor).ToArray();
        SetTownCouncil(current with
        {
            Election = election with
            {
                Candidates = candidates,
                SelectedMemberIds = selected,
                AvailableSeats = 3 - selected.Length,
                Candidacies = willing ? election.Candidacies.Append(new(actor, WorldTick)).OrderBy(item => item.CandidateId, StringComparer.Ordinal).ToArray()
                    : election.Candidacies.Where(item => item.CandidateId != actor).ToArray(),
                Votes = election.Votes.Select(vote => vote with { CandidateIds = vote.CandidateIds.Where(id => candidates.Contains(id, StringComparer.Ordinal)).ToArray() }).ToArray()
            }
        });
        AppendEvent("town_candidacy_changed", $"{townId}|{actor}|{willing}");
        foreach (var observer in TownAdultResidents(townId).Where(id => AtTownHall(id, townId)))
            ObserveTownRules(observer, townCouncils.Single(item => item.TownId == townId));
        return new(true);
    }

    public CivicActionResult VoteTownElection(string actor, string townId, IReadOnlyList<string> candidates) =>
        CivicAction(() => VoteTownElectionCore(actor, townId, candidates));
    private CivicActionResult VoteTownElectionCore(string actor, string townId, IReadOnlyList<string> candidates)
    {
        AdvanceTownCouncils();
        var current = townCouncils.FirstOrDefault(item => item.TownId == townId);
        if (current?.Election is not { } election || !AtTownHall(actor, townId) || candidates is null ||
            candidates.Count > election.AvailableSeats || candidates.Distinct(StringComparer.Ordinal).Count() != candidates.Count ||
            !election.Electorate.Contains(actor, StringComparer.Ordinal) ||
            candidates.Any(id => !election.Candidates.Contains(id, StringComparer.Ordinal)))
            return new(false, "An eligible voter at this Town's Hall may revise support for distinct willing candidates or abstain before voting closes.");
        SetTownCouncil(current with
        {
            Election = election with
            {
                Votes = election.Votes.Where(vote => vote.VoterId != actor)
                .Append(new(actor, candidates.Order(StringComparer.Ordinal).ToArray())).OrderBy(vote => vote.VoterId, StringComparer.Ordinal).ToArray()
            }
        });
        AppendEvent("town_election_vote_recorded", $"{townId}|{actor}");
        return new(true);
    }

    private static bool ValidTownLawKey(string key) => !string.IsNullOrWhiteSpace(key) && key.Length <= 64 &&
        key.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '_');

    private bool HasTownCouncilDecision(string actor)
    {
        var current = townCouncils.FirstOrDefault(item => item.TownId == TownForResident(actor));
        if (current is null || !AtTownHall(actor, current.TownId)) return false;
        var electionWork = current.Election is { } election &&
            election.Electorate.Contains(actor, StringComparer.Ordinal) && election.Candidates.Count > 0 &&
            !election.Votes.Any(vote => vote.VoterId == actor);
        return electionWork || current.Ballot is { } ballot &&
                ballot.Electorate.Contains(actor, StringComparer.Ordinal) && !ballot.Approvals.Concat(ballot.Rejections).Contains(actor, StringComparer.Ordinal) ||
                ProposedTownFoodPolicy(actor) is not null;
    }

    private void AddTownCouncilCandidates(List<CognitionCandidate> candidates, string actor)
    {
        if (NeedsUrgentWarmth(inhabitants[actor])) return;
        var current = townCouncils.FirstOrDefault(item => item.TownId == TownForResident(actor));
        if (current is null || !TownAdultResidents(current.TownId).Contains(actor, StringComparer.Ordinal) || TownHallFor(current.TownId) is not { } hall) return;
        if (!AtTownHall(actor, current.TownId))
        {
            // Visiting a known civic building reveals no unseen ballot, nomination or stock information.
            if (TownHallTiles(hall).Any(tile => FindUnoccupiedRoute(actor, inhabitants[actor].Position, tile, 1).Count > 0))
                candidates.Add(new("council_town_visit", "Visit the Town Hall to read its public rules and notices.", 50));
            return;
        }
        if (current.Ballot is null && current.Election is null && current.MemberIds.Count > 0 && WorldTick - current.LastResolutionTick >= 300)
            candidates.Add(new("council_town_author", "Propose a useful named Town social rule at this Hall; the council must vote before it changes anything.", 110));
        if (current.Election is { } election)
        {
            var willing = election.Candidacies.Any(item => item.CandidateId == actor);
            if (!election.IsRunoff && !willing)
                candidates.Add(new("council_town_volunteer", "Volunteer to serve as a Town councillor for one game year.", 13));
            if (willing) candidates.Add(new("council_town_withdraw", "Withdraw your willing council candidacy.", 80));
            if (election.Electorate.Contains(actor, StringComparer.Ordinal))
            {
                var ballot = election.Votes.FirstOrDefault(vote => vote.VoterId == actor)?.CandidateIds ?? [];
                foreach (var id in election.Candidates)
                {
                    var name = society.Checkpoint.GetInhabitant(id).Name;
                    if (ballot.Contains(id, StringComparer.Ordinal))
                        candidates.Add(new("council_town_unsupport:" + id, "Withdraw your election support for willing candidate " + name + ".", 80));
                    else if (ballot.Count < election.AvailableSeats)
                        candidates.Add(new("council_town_support:" + id, "Support willing candidate " + name + " for the Town council.", 14 + ballot.Count));
                }
                if (ballot.Count > 0 || !election.Votes.Any(vote => vote.VoterId == actor))
                    candidates.Add(new("council_town_abstain", "Clear your council election ballot and abstain.", 85));
            }
        }
        if (ProposedTownFoodPolicy(actor) is { } policy)
            candidates.Add(new("council_town_propose:" + policy, policy == "essential_first"
                ? "Propose saving scarce communal food for hungry residents."
                : "Propose restoring open communal-food access.", 14));
        if (current.Ballot is { } law && law.Electorate.Contains(actor, StringComparer.Ordinal) &&
            !law.Approvals.Concat(law.Rejections).Contains(actor, StringComparer.Ordinal))
        {
            var population = towns.Single(town => town.Id == current.TownId).ResidentIds.Count;
            var helps = law.FoodPolicy == "essential_first" ? TownSharedFoodQuantity(current.TownId) < population * 2
                : law.FoodPolicy == "open" ? TownSharedFoodQuantity(current.TownId) >= population * 4 : true;
            candidates.Add(new("council_town_vote_yes", "Support the proposed Town rule: " + law.Text, helps ? 13 : 60));
            candidates.Add(new("council_town_vote_no", "Reject the proposed Town rule and keep the current rule: " + law.Text, helps ? 60 : 13));
        }
    }

    private void ApplyTownCouncilCandidate(string actor, string candidate)
    {
        if (TownForResident(actor) is not { } townId || TownHallFor(townId) is not { } hall) return;
        if (!AtTownHall(actor, townId))
        {
            if (candidate != "council_town_visit") return;
            var destination = TownHallTiles(hall).Select(tile => (Tile: tile,
                    Route: FindUnoccupiedRoute(actor, inhabitants[actor].Position, tile, 1)))
                .Where(option => option.Route.Count > 0).OrderBy(option => option.Route.Count).ThenBy(option => option.Tile.Y)
                .ThenBy(option => option.Tile.X).FirstOrDefault();
            if (destination.Route is not null) MoveToward(actor, inhabitants[actor], destination.Tile, "town_hall", 1);
            return;
        }
        if (candidate is "council_town_volunteer" or "council_town_withdraw")
            VolunteerTownCouncilCore(actor, townId, candidate == "council_town_volunteer");
        else if (candidate.StartsWith("council_town_support:", StringComparison.Ordinal) || candidate.StartsWith("council_town_unsupport:", StringComparison.Ordinal) ||
            candidate == "council_town_abstain")
        {
            if (townCouncils.FirstOrDefault(item => item.TownId == townId)?.Election is not { } election) return;
            var previous = election.Votes.FirstOrDefault(vote => vote.VoterId == actor)?.CandidateIds ?? [];
            var revised = candidate == "council_town_abstain" ? [] : candidate.StartsWith("council_town_support:", StringComparison.Ordinal)
                ? previous.Append(candidate["council_town_support:".Length..]).ToArray() : previous.Where(id => id != candidate["council_town_unsupport:".Length..]).ToArray();
            VoteTownElectionCore(actor, townId, revised);
        }
        else if (candidate.StartsWith("council_town_propose:", StringComparison.Ordinal))
        {
            var policy = candidate[21..];
            if (policy == ProposedTownFoodPolicy(actor)) ProposeTownLawCore(actor, townId, "shared_food", policy == "essential_first"
                ? "Reserve scarce communal food for hungry residents." : "Allow residents to collect plentiful communal food.", policy, false);
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

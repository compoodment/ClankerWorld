using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private List<TownCouncilState> townCouncils = [];
    public IReadOnlyList<TownCouncilState> TownCouncils => townCouncils.OrderBy(item => item.TownId, StringComparer.Ordinal).ToArray();
    private long CivicVoteLifetime => worldSystems.Config.TicksPerDay;
    private long CouncilTermLifetime => checked(10L * worldSystems.Config.TicksPerDay);

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
            current = ExpireReplacementConsent(current);
            current = current with
            {
                CandidateRegister = current.CandidateRegister.Where(item => eligible.Contains(item.CandidateId)).ToArray(),
                ProposalOutcomes = current.ProposalOutcomes.Where(item => WorldTick < item.ClosedTick + CivicVoteLifetime).ToArray()
            };
            if (current.Election is { } pending) current = current with { Election = PruneTownElection(pending, eligible, current.MemberIds) };
            current = current.GoverningForm == "collective"
                ? current with { MemberIds = adults }
                : current with { MemberIds = current.MemberIds.Where(eligible.Contains).ToArray() };
            if (adults.Length <= 3 && (current.GoverningForm == "representative" || current.FallbackReason == "candidate" || current.Election is not null))
                current = current with
                {
                    GoverningForm = "collective",
                    FallbackReason = "population",
                    MemberIds = adults,
                    TermStartedTick = null,
                    TermExpiryTick = null,
                    Election = null,
                    ElectionRetryAfterTick = null
                };
            current = CancelTownLawForChangedCouncil(current);
            if (TownHallFor(town.Id) is not null && adults.Length > 3)
            {
                // Scheduled full voting has priority over an unresolved replacement round.
                if (current.TermExpiryTick is { } expiry && WorldTick >= expiry - CivicVoteLifetime &&
                    current.Election?.Kind != "regular" && (current.LastElectionOutcome is not { Kind: "regular" } previous ||
                        previous.StartedTick < expiry - CivicVoteLifetime))
                {
                    current = StartTownElection(current, adults, "regular", current.TermExpiryTick!.Value,
                        checked(current.TermExpiryTick.Value + CouncilTermLifetime));
                }
                if (current.Election is { } election && WorldTick >= election.ExpiryTick)
                    current = FinishTownElection(current, election);
                if (current.Election is null)
                {
                    if (current.GoverningForm == "representative" && current.TermExpiryTick <= WorldTick)
                        current = CandidateFallback(current, adults);
                    var retryReady = current.ElectionRetryAfterTick is null || WorldTick >= current.ElectionRetryAfterTick ||
                        ImprovedElectionCircumstances(current, adults);
                    if (retryReady && (current.GoverningForm == "collective" && (adults.Length >= 8 || current.FallbackReason == "candidate") ||
                            current.GoverningForm == "representative" && current.MemberIds.Count < 3))
                    {
                        var replacement = current.TermExpiryTick > WorldTick && current.TermStartedTick is not null;
                        current = StartTownElection(current, adults, replacement ? "replacement" : "initial",
                            replacement ? current.TermStartedTick!.Value : checked(WorldTick + CivicVoteLifetime),
                            replacement ? current.TermExpiryTick!.Value : checked(WorldTick + CivicVoteLifetime + CouncilTermLifetime));
                    }
                }
            }
            current = ResolveTownLawBallot(CancelTownLawForChangedCouncil(current));
            current = ExpireReplacementConsent(current);
            SetTownCouncil(current);
            foreach (var observer in adults.Where(actor => AtTownHall(actor, town.Id))) ObserveTownRules(observer, current);
        }
    }

    private static TownCouncilElection PruneTownElection(TownCouncilElection pending, HashSet<string> eligible, IReadOnlyList<string> members)
    {
        var candidates = pending.Candidates.Where(eligible.Contains).ToArray();
        var selected = pending.SelectedMemberIds.Where(id => eligible.Contains(id) &&
            (pending.Kind != "replacement" || members.Contains(id, StringComparer.Ordinal))).ToArray();
        var retained = pending.RetainedMemberIds.Where(id => eligible.Contains(id) && members.Contains(id, StringComparer.Ordinal)).ToArray();
        return pending with
        {
            Electorate = pending.Electorate.Where(eligible.Contains).ToArray(),
            Candidates = candidates,
            SelectedMemberIds = selected,
            RetainedMemberIds = retained,
            AvailableSeats = 3 - selected.Length - retained.Length,
            MainAvailableSeats = pending.IsRunoff ? pending.MainAvailableSeats : 3 - retained.Length,
            Candidacies = pending.Candidacies.Where(item => candidates.Contains(item.CandidateId, StringComparer.Ordinal) ||
                selected.Contains(item.CandidateId, StringComparer.Ordinal)).ToArray(),
            MainSupportedCandidateIds = pending.MainSupportedCandidateIds.Where(eligible.Contains).ToArray(),
            Votes = pending.Votes.Where(vote => eligible.Contains(vote.VoterId)).Select(vote => vote with
            { CandidateIds = vote.CandidateIds.Where(id => candidates.Contains(id, StringComparer.Ordinal)).ToArray() }).ToArray()
        };
    }

    private TownCouncilState ExpireReplacementConsent(TownCouncilState current) => current with
    {
        CandidateRegister = current.CandidateRegister.Select(item => item.ReplacementWilling &&
                (current.TermExpiryTick <= WorldTick || item.ReplacementTermStartedTick != current.TermStartedTick ||
                    item.ReplacementTermExpiryTick != current.TermExpiryTick)
            ? item with
            {
                ReplacementWilling = false,
                ReplacementTermStartedTick = null,
                ReplacementTermExpiryTick = null,
                ReplacementDeclaredTick = null
            } : item)
            .Where(item => item.FullTermWilling || item.ReplacementWilling).ToArray()
    };

    private TownCouncilState StartTownElection(TownCouncilState current, string[] adults, string kind, long termStart, long termEnd)
    {
        var retained = kind == "replacement" && current.GoverningForm == "representative" ? current.MemberIds.ToArray() : [];
        var register = current.CandidateRegister.Where(item => (kind == "replacement" ? item.ReplacementWilling : item.FullTermWilling) &&
            !retained.Contains(item.CandidateId, StringComparer.Ordinal)).ToArray();
        var started = kind == "regular" ? checked(termStart - CivicVoteLifetime) : WorldTick;
        AppendEvent("town_election_started", $"{current.TownId}|{kind}");
        return current with
        {
            TermStartedTick = kind == "initial" ? null : current.TermStartedTick,
            TermExpiryTick = kind == "initial" ? null : current.TermExpiryTick,
            Election = new(started, checked(started + CivicVoteLifetime), adults, register.Select(item => item.CandidateId).ToArray(), [])
            {
                Kind = kind,
                Candidacies = register,
                RetainedMemberIds = retained,
                AvailableSeats = 3 - retained.Length,
                MainAvailableSeats = 3 - retained.Length,
                TargetTermStartedTick = termStart,
                TargetTermExpiryTick = termEnd
            },
            ElectionRetryAfterTick = null
        };
    }

    private static TownCouncilState CandidateFallback(TownCouncilState current, string[] adults) => current with
    { GoverningForm = "collective", FallbackReason = "candidate", MemberIds = adults };

    private static bool ImprovedElectionCircumstances(TownCouncilState current, string[] adults)
    {
        if (current.LastElectionOutcome is not { } failed) return false;
        var replacement = failed.Kind == "replacement";
        return adults.Except(failed.AdultIdsAtResolution, StringComparer.Ordinal).Any() ||
            current.CandidateRegister.Where(item => replacement ? item.ReplacementWilling : item.FullTermWilling).Select(item => item.CandidateId)
                .Except(failed.RegisterAtResolution.Where(item => replacement ? item.ReplacementWilling : item.FullTermWilling)
                    .Select(item => item.CandidateId), StringComparer.Ordinal).Any();
    }

    private static string[] DrawTownCandidates(string seed, string townId, long started, IReadOnlyList<string> slate, int seats)
    {
        var remaining = slate.Order(StringComparer.Ordinal).ToList();
        var random = Pcg32XshRrV1.Create(seed, $"town-election-draw:{townId}:{started}");
        var drawn = new List<string>();
        while (drawn.Count < seats && remaining.Count > 0)
        {
            var bound = (uint)remaining.Count;
            var threshold = unchecked(0u - bound) % bound;
            uint roll;
            do { roll = random.NextUInt(); } while (roll < threshold);
            var index = (int)(roll % bound);
            drawn.Add(remaining[index]); remaining.RemoveAt(index);
        }
        return drawn.ToArray();
    }

    private TownCouncilState CancelTownLawForChangedCouncil(TownCouncilState current)
    {
        if (current.Ballot is not { } ballot || ballot.GoverningForm == current.GoverningForm &&
            ballot.Electorate.ToHashSet(StringComparer.Ordinal).SetEquals(current.MemberIds)) return current;
        AppendEvent("town_law_cancelled", $"{current.TownId}|{ballot.Key}|council_changed");
        return current with { Ballot = null };
    }

    private TownCouncilState FinishTownElection(TownCouncilState current, TownCouncilElection election)
    {
        var supported = election.IsRunoff ? election.MainSupportedCandidateIds.ToArray() : election.Candidates
            .Where(id => election.Votes.Any(vote => vote.CandidateIds.Contains(id, StringComparer.Ordinal))).ToArray();
        var seatLimit = 3 - election.RetainedMemberIds.Count;
        var (selected, cutoffTie) = RankTownCandidates(election.Candidates, election.Votes, supported, seatLimit, election.SelectedMemberIds);
        if (!election.IsRunoff && cutoffTie.Length > 0)
        {
            var remaining = cutoffTie.Concat(selected).ToHashSet(StringComparer.Ordinal);
            AppendEvent("town_election_runoff_started", current.TownId);
            return current with
            {
                MemberIds = election.Kind == "replacement" && current.GoverningForm == "representative"
                    ? election.RetainedMemberIds.Concat(selected).ToArray() : current.MemberIds,
                Election = election with
                {
                    IsRunoff = true,
                    ExpiryTick = checked(election.ExpiryTick + CivicVoteLifetime),
                    Electorate = TownAdultResidents(current.TownId),
                    AvailableSeats = seatLimit - selected.Count,
                    SelectedMemberIds = selected.ToArray(),
                    Candidates = cutoffTie,
                    Candidacies = election.Candidacies.Where(item => remaining.Contains(item.CandidateId)).ToArray(),
                    Votes = [],
                    MainVotes = election.Votes,
                    MainCandidateIds = election.Candidates,
                    MainElectorate = election.Electorate,
                    MainSupportedCandidateIds = supported,
                    OriginalMainSelectedMemberIds = selected.ToArray(),
                    OriginalMainCutoffCandidateIds = cutoffTie,
                    OriginalMainRetainedMemberIds = election.RetainedMemberIds
                }
            };
        }
        var drawn = cutoffTie.Length == 0 ? [] : DrawTownCandidates(worldSeed, current.TownId, election.StartedTick, cutoffTie, seatLimit - selected.Count);
        selected.AddRange(drawn);
        var finalMembers = election.RetainedMemberIds.Concat(selected).ToArray();
        var result = new TownElectionOutcome(election.StartedTick, WorldTick, election.Kind, supported,
            selected.ToArray(), drawn)
        {
            MainVotes = election.IsRunoff ? election.MainVotes : election.Votes,
            MainCandidateIds = election.IsRunoff ? election.MainCandidateIds : election.Candidates,
            MainElectorate = election.IsRunoff ? election.MainElectorate : election.Electorate,
            DrawSlate = cutoffTie,
            RetainedMemberIds = election.RetainedMemberIds,
            RunoffVotes = election.IsRunoff ? election.Votes : [],
            RunoffCandidateIds = election.IsRunoff ? election.Candidates : [],
            RunoffElectorate = election.IsRunoff ? election.Electorate : [],
            BeforeRunoffSelectedIds = election.SelectedMemberIds,
            MainAvailableSeats = election.MainAvailableSeats,
            OriginalMainSelectedMemberIds = election.IsRunoff ? election.OriginalMainSelectedMemberIds : selected.Except(drawn, StringComparer.Ordinal).ToArray(),
            OriginalMainCutoffCandidateIds = election.IsRunoff ? election.OriginalMainCutoffCandidateIds : [],
            OriginalMainRetainedMemberIds = election.IsRunoff ? election.OriginalMainRetainedMemberIds : election.RetainedMemberIds,
            AdultIdsAtResolution = TownAdultResidents(current.TownId),
            RegisterAtResolution = current.CandidateRegister
        };
        if (drawn.Length > 0) AppendEvent("town_election_draw_recorded", $"{current.TownId}|{string.Join(',', drawn)}");
        if (finalMembers.Length != 3)
        {
            AppendEvent("town_election_failed", current.TownId);
            var failed = CandidateFallback(current with { Election = null, LastElectionOutcome = result }, TownAdultResidents(current.TownId));
            return failed with
            {
                ElectionRetryAfterTick = checked(WorldTick + CivicVoteLifetime)
            };
        }
        AppendEvent("town_council_elected", $"{current.TownId}|{string.Join(',', selected)}");
        return current with
        {
            GoverningForm = "representative",
            FallbackReason = null,
            MemberIds = finalMembers,
            TermStartedTick = election.Kind == "replacement" ? election.TargetTermStartedTick : WorldTick,
            TermExpiryTick = election.Kind == "replacement" ? election.TargetTermExpiryTick : checked(WorldTick + CouncilTermLifetime),
            Election = null,
            LastElectionOutcome = result,
            ElectionRetryAfterTick = null
        };
    }

    private static (List<string> Selected, string[] CutoffTie) RankTownCandidates(IReadOnlyList<string> candidates,
        IReadOnlyList<TownElectionVote> votes, IReadOnlyList<string> supported, int seats, IReadOnlyList<string> alreadySelected)
    {
        var selected = alreadySelected.ToList();
        foreach (var group in candidates.Select(id => (Id: id, Count: votes.Count(vote => vote.CandidateIds.Contains(id, StringComparer.Ordinal))))
                     .Where(item => supported.Contains(item.Id, StringComparer.Ordinal)).GroupBy(item => item.Count).OrderByDescending(group => group.Key))
        {
            var names = group.Select(item => item.Id).Order(StringComparer.Ordinal).ToArray();
            if (names.Length > seats - selected.Count) return (selected, names);
            selected.AddRange(names);
            if (selected.Count == seats) break;
        }
        return (selected, []);
    }

    private void ObserveTownRules(string actor, TownCouncilState current)
    {
        string Names(IEnumerable<string> ids) => string.Join(", ", ids.Select(id => society.Checkpoint.GetInhabitant(id).Name));
        var source = current.MemberIds.Count > 0 ? current.MemberIds[0] : actor;
        RememberTownRule(actor, current.TownId, "council", current.TermStartedTick ?? 0, source,
            "Read this council notice at its Hall: " + current.GoverningForm + "; members: " + Names(current.MemberIds) +
            (current.TermExpiryTick is { } end ? "; term ends at world tick " + end : "; all adult residents govern"));
        if (current.Election is { } election)
            RememberTownRule(actor, current.TownId, election.IsRunoff ? "runoff" : "election", election.StartedTick, source,
                "Read this voting notice at its Hall: " + election.Kind + (election.IsRunoff ? " runoff" : " election") +
                "; voting closes at world tick " + election.ExpiryTick + "; available seats: " + election.AvailableSeats +
                "; willing candidates: " + Names(election.Candidates) + "; eligible voters: " + Names(election.Electorate));
        if (current.LastElectionOutcome is { DrawnMemberIds.Count: > 0 } outcome)
            RememberTownRule(actor, current.TownId, "draw", outcome.ResolvedTick, source,
                "Read this recorded council draw at its Hall: " + Names(outcome.DrawnMemberIds));
        foreach (var candidacy in current.CandidateRegister)
            RememberTownRule(actor, current.TownId, "candidate_" + candidacy.CandidateId.Replace(':', '_'),
                candidacy.DeclaredTick, candidacy.CandidateId, "Read this willing council candidate at its Hall: " +
                society.Checkpoint.GetInhabitant(candidacy.CandidateId).Name +
                (candidacy.FullTermWilling ? " (full term)" : "") + (candidacy.ReplacementWilling ? " (remaining term)" : ""));
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
        var required = current.GoverningForm == "representative" ? 2 : current.MemberIds.Count / 2 + 1;
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
            current = RecordClosedTownProposal(current, ballot) with { Ballot = null, LastResolutionTick = WorldTick };
            AppendEvent("town_law_rejected", $"{current.TownId}|{ballot.Key}");
        }
        return current;
    }

    private static string TownProposalKey(string key, string? policy, bool repeal) => $"{key}|{policy ?? ""}|{repeal}";
    private TownProposalCircumstances ProposalCircumstances(TownCouncilState current, string key) => new(
        current.GoverningForm, current.MemberIds.Order(StringComparer.Ordinal).ToArray(),
        (current.Laws ?? []).FirstOrDefault(law => law.Key == key)?.AdoptedTick,
        key == "shared_food" ? TownAdultResidents(current.TownId).Length : null,
        key == "shared_food" ? TownSharedFoodQuantity(current.TownId) : null);

    private static bool SameProposalCircumstances(TownProposalCircumstances left, TownProposalCircumstances right) =>
        left.GoverningForm == right.GoverningForm && left.MemberIds.ToHashSet(StringComparer.Ordinal).SetEquals(right.MemberIds) &&
        left.SameLawAdoptedTick == right.SameLawAdoptedTick && left.AdultPopulation == right.AdultPopulation &&
        left.CommunalFoodQuantity == right.CommunalFoodQuantity;

    private TownCouncilState RecordClosedTownProposal(TownCouncilState current, TownLawBallot ballot) => current with
    {
        ProposalOutcomes = current.ProposalOutcomes.Where(item => item.RequestKey != TownProposalKey(ballot.Key, ballot.FoodPolicy, ballot.Repeal) &&
                WorldTick < item.ClosedTick + CivicVoteLifetime)
            .Append(new(TownProposalKey(ballot.Key, ballot.FoodPolicy, ballot.Repeal), WorldTick, ProposalCircumstances(current, ballot.Key))).ToArray()
    };

    public CivicActionResult WithdrawTownLaw(string actor, string townId) => CivicAction(() =>
    {
        AdvanceTownCouncils();
        var current = townCouncils.FirstOrDefault(item => item.TownId == townId);
        if (current?.Ballot is not { } ballot || ballot.ProposerId != actor || !AtTownHall(actor, townId))
            return new(false, "Only its proposing adult at the Hall may withdraw this unfinished request.");
        SetTownCouncil(RecordClosedTownProposal(current, ballot) with { Ballot = null, LastResolutionTick = WorldTick });
        AppendEvent("town_law_withdrawn", $"{townId}|{ballot.Key}");
        return new(true);
    });

    public CivicActionResult ResignTownCouncil(string actor, string townId) => CivicAction(() =>
    {
        AdvanceTownCouncils();
        var current = townCouncils.FirstOrDefault(item => item.TownId == townId);
        if (current is null || current.GoverningForm != "representative" || !current.MemberIds.Contains(actor, StringComparer.Ordinal) || !AtTownHall(actor, townId))
            return new(false, "Only a current representative at the Hall may resign their seat.");
        SetTownCouncil(RemoveTownRepresentative(current, actor));
        AppendEvent("town_councillor_resigned", $"{townId}|{actor}");
        AdvanceTownCouncils();
        return new(true);
    });

    private TownCouncilState RemoveTownRepresentative(TownCouncilState current, string actor)
    {
        var updated = current with { MemberIds = current.MemberIds.Where(id => id != actor).ToArray() };
        if (updated.Election is { Kind: "replacement" } election)
        {
            var selected = election.SelectedMemberIds.Where(id => id != actor).ToArray();
            var retained = election.RetainedMemberIds.Where(id => id != actor).ToArray();
            updated = updated with
            {
                Election = election with
                {
                    SelectedMemberIds = selected,
                    RetainedMemberIds = retained,
                    AvailableSeats = 3 - selected.Length - retained.Length,
                    Candidacies = election.Candidacies.Where(item => selected.Contains(item.CandidateId, StringComparer.Ordinal) ||
                        election.Candidates.Contains(item.CandidateId, StringComparer.Ordinal)).ToArray()
                }
            };
        }
        return CancelTownLawForChangedCouncil(updated);
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
            current.MemberIds.Count == 0 || !ValidTownLawKey(key) ||
            string.IsNullOrWhiteSpace(text) || text.Length > 280 || text.Any(char.IsControl) ||
            foodPolicy is not (null or "open" or "essential_first") ||
            key == "shared_food" && (foodPolicy is null || repeal) || key != "shared_food" && foodPolicy is not null ||
            repeal && !(current.Laws ?? []).Any(law => law.Key == key) ||
            !repeal && key != "shared_food" && (current.Laws ?? []).Count >= 64 && !(current.Laws ?? []).Any(law => law.Key == key))
            return new(false, "An adult resident at this Town's Hall may propose a valid rule while no vote is pending.");
        var requestKey = TownProposalKey(key, foodPolicy, repeal);
        if (current.Ballot is { } pending)
            return TownProposalKey(pending.Key, pending.FoodPolicy, pending.Repeal) == requestKey
                ? new(true) : new(false, "The council already has a different request awaiting its vote.");
        if (current.ProposalOutcomes.FirstOrDefault(item => item.RequestKey == requestKey) is { } previous &&
            WorldTick < previous.ClosedTick + CivicVoteLifetime && SameProposalCircumstances(previous.Circumstances, ProposalCircumstances(current, key)))
            return new(false, "The same request must wait one day unless material circumstances or the council change.");
        if (current.ProposalOutcomes.Count >= 256)
            return new(false, "This council must finish its one-day request retry window before accepting more distinct requests.");
        var ballot = new TownLawBallot(key, text.Trim(), actor, foodPolicy, repeal, WorldTick,
            checked(WorldTick + CivicVoteLifetime), current.MemberIds.ToArray(), [], [])
        { GoverningForm = current.GoverningForm };
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

    public CivicActionResult VolunteerTownCouncil(string actor, string townId, bool willing = true, bool replacement = false) =>
        CivicAction(() => VolunteerTownCouncilCore(actor, townId, willing, replacement));
    private CivicActionResult VolunteerTownCouncilCore(string actor, string townId, bool willing, bool replacement = false)
    {
        AdvanceTownCouncils();
        var current = townCouncils.FirstOrDefault(item => item.TownId == townId);
        var registered = current?.CandidateRegister.FirstOrDefault(item => item.CandidateId == actor);
        var alreadyWilling = replacement ? registered?.ReplacementWilling == true : registered?.FullTermWilling == true;
        if (current is null || !AtTownHall(actor, townId) || willing == alreadyWilling ||
            replacement && willing && (current.TermStartedTick is null || current.TermExpiryTick <= WorldTick))
            return new(false, "An adult resident at this Town's Hall may register or withdraw their own willingness for a full or remaining term.");
        var changed = (registered ?? new(actor, WorldTick) { FullTermWilling = false }) with
        {
            DeclaredTick = WorldTick,
            FullTermWilling = replacement ? registered?.FullTermWilling == true : willing,
            ReplacementWilling = replacement ? willing : registered?.ReplacementWilling == true,
            ReplacementTermStartedTick = replacement ? willing ? current.TermStartedTick : null : registered?.ReplacementTermStartedTick,
            ReplacementTermExpiryTick = replacement ? willing ? current.TermExpiryTick : null : registered?.ReplacementTermExpiryTick,
            ReplacementDeclaredTick = replacement ? willing ? WorldTick : null : registered?.ReplacementDeclaredTick
        };
        current = current with
        {
            CandidateRegister = current.CandidateRegister.Where(item => item.CandidateId != actor)
                .Concat(changed.FullTermWilling || changed.ReplacementWilling ? [changed] : []).OrderBy(item => item.CandidateId, StringComparer.Ordinal).ToArray()
        };
        if (!willing && current.Election is { } election && (election.Kind == "replacement") == replacement)
        {
            var selected = election.SelectedMemberIds.Where(id => id != actor).ToArray();
            var candidates = election.Candidates.Where(id => id != actor).ToArray();
            current = current with
            {
                MemberIds = election.Kind == "replacement" && election.SelectedMemberIds.Contains(actor, StringComparer.Ordinal)
                    ? current.MemberIds.Where(id => id != actor).ToArray() : current.MemberIds,
                Election = election with
                {
                    Candidates = candidates,
                    SelectedMemberIds = selected,
                    AvailableSeats = 3 - selected.Length - election.RetainedMemberIds.Count,
                    Candidacies = election.Candidacies.Where(item => item.CandidateId != actor).ToArray(),
                    MainSupportedCandidateIds = election.MainSupportedCandidateIds.Where(id => id != actor).ToArray(),
                    Votes = election.Votes.Select(vote => vote with { CandidateIds = vote.CandidateIds.Where(id => candidates.Contains(id, StringComparer.Ordinal)).ToArray() }).ToArray()
                }
            };
        }
        SetTownCouncil(CancelTownLawForChangedCouncil(current));
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
        if (current.Ballot is null && current.MemberIds.Count > 0 && WorldTick - current.LastResolutionTick >= 300)
            candidates.Add(new("council_town_author", "Propose a useful named Town social rule at this Hall; the council must vote before it changes anything.", 110));
        var registered = current.CandidateRegister.FirstOrDefault(item => item.CandidateId == actor);
        if (registered?.FullTermWilling != true)
            candidates.Add(new("council_town_volunteer", "Register your willingness to serve a ten-day council term; an already open election keeps its original candidate list.", 13));
        else candidates.Add(new("council_town_withdraw", "Withdraw your willingness for a full council term.", 80));
        if (registered?.ReplacementWilling != true && current.TermStartedTick is not null)
            candidates.Add(new("council_town_volunteer_replacement", "Register willingness to fill a vacant seat for the remainder of this council term only.", 45));
        else if (registered?.ReplacementWilling == true)
            candidates.Add(new("council_town_withdraw_replacement", "Withdraw your willingness to fill a seat for the remaining term.", 80));
        if (current.GoverningForm == "representative" && current.MemberIds.Contains(actor, StringComparer.Ordinal))
            candidates.Add(new("council_town_resign", "Resign your present council seat; its replacement requires an election.", 100));
        if (current.Election is { } election)
        {
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
        if (current.Ballot?.ProposerId == actor)
            candidates.Add(new("council_town_withdraw_law", "Withdraw your unfinished council request; an equivalent retry normally waits one day.", 100));
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
        else if (candidate is "council_town_volunteer_replacement" or "council_town_withdraw_replacement")
            VolunteerTownCouncilCore(actor, townId, candidate == "council_town_volunteer_replacement", replacement: true);
        else if (candidate == "council_town_resign")
        {
            var current = townCouncils.First(item => item.TownId == townId);
            if (current.GoverningForm == "representative" && current.MemberIds.Contains(actor, StringComparer.Ordinal))
            {
                SetTownCouncil(RemoveTownRepresentative(current, actor));
                AppendEvent("town_councillor_resigned", $"{townId}|{actor}");
                AdvanceTownCouncils();
            }
        }
        else if (candidate == "council_town_withdraw_law")
        {
            var current = townCouncils.First(item => item.TownId == townId);
            if (current.Ballot is { } ballot && ballot.ProposerId == actor)
            {
                SetTownCouncil(RecordClosedTownProposal(current, ballot) with { Ballot = null, LastResolutionTick = WorldTick });
                AppendEvent("town_law_withdrawn", $"{townId}|{ballot.Key}");
            }
        }
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

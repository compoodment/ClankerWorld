using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

public sealed record SettlementBallot(string Policy, long ProposedTick, long ExpiryTick,
    IReadOnlyList<string> Electorate, IReadOnlyList<string> Approvals, IReadOnlyList<string> Rejections);

public sealed record SettlementCouncil(string? StewardId, string FoodPolicy, long LastResolutionTick,
    SettlementBallot? Ballot = null);

public sealed partial class PrivateWorldRuntime
{
    private SettlementCouncil? council;

    private string[] CouncilMembers() => society.Checkpoint.Inhabitants.Where(person =>
            person.Status == SocietyInhabitantStatus.Active && person.HouseholdId is not null &&
            person.AgeBand is SocietyAgeBand.Adult or SocietyAgeBand.Elder)
        .Select(person => person.Id).Order(StringComparer.Ordinal).ToArray();

    private int ContributionScore(string actor) => society.Checkpoint.Memories.Count(memory => memory.SubjectId == actor &&
            (memory.Id.StartsWith("project-gratitude:", StringComparison.Ordinal) || memory.Id.StartsWith("settlement-trust:", StringComparison.Ordinal) ||
                memory.Id.StartsWith("lesson-gratitude:", StringComparison.Ordinal))) +
        (inhabitants.GetValueOrDefault(actor)?.Project?.Stage == "completed" ? 1 : 0);

    private int SharedFoodQuantity() => society.Checkpoint.Inventory.Lots.Where(lot =>
            society.Checkpoint.Households.Any(household => household.Id == lot.OwnerId) && IsEdibleFood(lot.ItemKind))
        .Sum(AvailableLotQuantity);

    private string? ProposedFoodPolicy(string actor)
    {
        if (council is null || council.StewardId != actor || !CouncilMembers().Contains(actor, StringComparer.Ordinal) ||
            council.Ballot is not null || WorldTick - council.LastResolutionTick < 300)
        {
            return null;
        }
        var quantity = SharedFoodQuantity();
        return council.FoodPolicy == "open" && quantity < inhabitants.Count * 2 ? "essential_first"
            : council.FoodPolicy == "essential_first" && quantity >= inhabitants.Count * 4 ? "open" : null;
    }

    private bool HasCouncilDecision(string actor) => !NeedsUrgentWarmth(inhabitants[actor]) && (
        council?.Ballot is { } ballot && ballot.Electorate.Contains(actor, StringComparer.Ordinal) &&
        !ballot.Approvals.Contains(actor, StringComparer.Ordinal) && !ballot.Rejections.Contains(actor, StringComparer.Ordinal) ||
        ProposedFoodPolicy(actor) is not null);

    private bool MayCollectSharedFood(string actor) => council?.FoodPolicy != "essential_first" ||
        inhabitants[actor].HungerBasisPoints < 4_500 || SharedFoodQuantity() > inhabitants.Count;

    private void AdvanceSettlementCouncil()
    {
        if (survivalState is null && council is null)
        {
            return;
        }
        if (council is null)
        {
            council = new(null, "open", WorldTick);
            checkpointSchemaVersion = StateSchemaVersion;
        }
        var members = CouncilMembers();
        if (council.StewardId is null || !members.Contains(council.StewardId, StringComparer.Ordinal))
        {
            var successor = members.Where(id => council.StewardId is not null || ContributionScore(id) > 0)
                .OrderByDescending(ContributionScore).ThenBy(id => id, StringComparer.Ordinal).FirstOrDefault();
            if (successor != council.StewardId)
            {
                council = council with { StewardId = successor };
                AppendEvent("council_steward_changed", successor ?? "none");
            }
        }
        ResolveCouncilBallot();
    }

    private void ResolveCouncilBallot()
    {
        if (council?.Ballot is not { } ballot)
        {
            return;
        }
        var living = CouncilMembers().Intersect(ballot.Electorate, StringComparer.Ordinal).ToArray();
        var required = living.Length / 2 + 1;
        var approvals = ballot.Approvals.Count(id => living.Contains(id, StringComparer.Ordinal));
        var rejections = ballot.Rejections.Count(id => living.Contains(id, StringComparer.Ordinal));
        if (living.Length > 0 && approvals >= required)
        {
            council = council with { FoodPolicy = ballot.Policy, LastResolutionTick = WorldTick, Ballot = null };
            AppendEvent("council_policy_adopted", ballot.Policy);
        }
        else if (living.Length == 0 || rejections > living.Length - required || WorldTick > ballot.ExpiryTick)
        {
            council = council with { LastResolutionTick = WorldTick, Ballot = null };
            AppendEvent("council_policy_rejected", ballot.Policy);
        }
    }

    private void AddCouncilCandidates(List<CognitionCandidate> candidates, string actor)
    {
        if (NeedsUrgentWarmth(inhabitants[actor]))
        {
            return;
        }
        if (ProposedFoodPolicy(actor) is { } policy)
        {
            candidates.Add(new("council_propose:" + policy, policy == "essential_first"
                ? "Ask the household to reserve scarce shared food for hungry members; a majority must agree."
                : "Ask the household to reopen plentiful shared food to everyone.", 14));
        }
        if (council?.Ballot is { } ballot && ballot.Electorate.Contains(actor, StringComparer.Ordinal) &&
            !ballot.Approvals.Contains(actor, StringComparer.Ordinal) && !ballot.Rejections.Contains(actor, StringComparer.Ordinal))
        {
            var helps = ballot.Policy == "essential_first" ? SharedFoodQuantity() < inhabitants.Count * 2 : SharedFoodQuantity() >= inhabitants.Count * 4;
            var description = ballot.Policy == "essential_first" ? "reserve the last shared servings for hungry members" : "restore open shared-food access";
            candidates.Add(new("council_vote_yes", "Vote to " + description + ".", helps ? 13 : 60));
            candidates.Add(new("council_vote_no", "Reject the proposed food policy; retain the existing access rule.", helps ? 60 : 13));
        }
    }

    private void ApplyCouncilCandidate(string actor, string candidate)
    {
        if (candidate.StartsWith("council_propose:", StringComparison.Ordinal))
        {
            var policy = candidate[16..];
            if (policy != ProposedFoodPolicy(actor) || council is null)
            {
                return;
            }
            council = council with { Ballot = new(policy, WorldTick, WorldTick + 120, CouncilMembers(), [actor], []) };
            AppendEvent("council_policy_proposed", policy);
        }
        else if (council?.Ballot is { } ballot && ballot.Electorate.Contains(actor, StringComparer.Ordinal) &&
                 !ballot.Approvals.Contains(actor, StringComparer.Ordinal) && !ballot.Rejections.Contains(actor, StringComparer.Ordinal))
        {
            council = council with
            {
                Ballot = candidate == "council_vote_yes"
                    ? ballot with { Approvals = ballot.Approvals.Append(actor).Order(StringComparer.Ordinal).ToArray() }
                    : ballot with { Rejections = ballot.Rejections.Append(actor).Order(StringComparer.Ordinal).ToArray() },
            };
            AppendEvent("council_vote_recorded", actor);
        }
        ResolveCouncilBallot();
    }

    private static void ValidateCouncil(PrivateWorldRuntimeState state)
    {
        if (state.Council is not { } saved)
        {
            return;
        }
        var tick = state.Society.Society.WorldTick;
        var known = state.Society.Society.Inhabitants.Select(person => person.Id).ToHashSet(StringComparer.Ordinal);
        if (state.SchemaVersion < 7 || saved.FoodPolicy is not ("open" or "essential_first") ||
            saved.LastResolutionTick < 0 || saved.LastResolutionTick > tick ||
            saved.StewardId is not null && !known.Contains(saved.StewardId))
        {
            throw new InvalidDataException("The saved household council is invalid.");
        }
        if (saved.Ballot is { } ballot && (ballot.Policy is not ("open" or "essential_first") || ballot.Policy == saved.FoodPolicy ||
            ballot.Electorate is null || ballot.Approvals is null || ballot.Rejections is null ||
            ballot.ProposedTick < 0 || ballot.ProposedTick > tick || ballot.ExpiryTick < ballot.ProposedTick || ballot.ExpiryTick - ballot.ProposedTick != 120 ||
            ballot.Electorate.Count == 0 || ballot.Electorate.Distinct(StringComparer.Ordinal).Count() != ballot.Electorate.Count ||
            ballot.Electorate.Any(id => !known.Contains(id)) ||
            ballot.Approvals.Concat(ballot.Rejections).Distinct(StringComparer.Ordinal).Count() != ballot.Approvals.Count + ballot.Rejections.Count ||
            ballot.Approvals.Concat(ballot.Rejections).Any(id => !ballot.Electorate.Contains(id, StringComparer.Ordinal))))
        {
            throw new InvalidDataException("The saved household ballot is invalid.");
        }
    }
}

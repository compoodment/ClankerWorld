using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private const int ChildSocialCooldownTicks = 120;

    private bool ChildResident(string actor) => inhabitants.ContainsKey(actor) &&
        society.Checkpoint.GetInhabitant(actor).AgeBand is SocietyAgeBand.Child or SocietyAgeBand.Adolescent;

    // This is the final world-side gate, in addition to the legal candidate list
    // used by cognition admission. It also protects continuing intentions after
    // an age transition and any future direct action caller.
    private bool AgePermitsCandidate(string actor, string candidate)
    {
        var age = society.Checkpoint.GetInhabitant(actor).AgeBand;
        if (age == SocietyAgeBand.Infant) return false;
        if (age is SocietyAgeBand.Adult or SocietyAgeBand.Elder)
            return !candidate.StartsWith("child_", StringComparison.Ordinal);
        return candidate is "safe_idle" or "consume_food" or "collect_shared_food" or
            "seek_food" or "harvest_food" or "wear_clothing" or "seek_warmth" ||
            candidate.StartsWith("guardian_accept:", StringComparison.Ordinal) ||
            candidate.StartsWith("guardian_refuse:", StringComparison.Ordinal) ||
            candidate.StartsWith("guardian_end:", StringComparison.Ordinal) ||
            candidate.StartsWith("child_converse:", StringComparison.Ordinal) ||
            candidate.StartsWith("child_play:", StringComparison.Ordinal) ||
            candidate.StartsWith("child_learn:", StringComparison.Ordinal) ||
            candidate == "child_help_food";
    }

    private bool ChildSocialAvailable(string actor, string target, string kind) =>
        ChildResident(actor) && actor != target && inhabitants.ContainsKey(target) &&
        society.Checkpoint.GetInhabitant(target).AgeBand != SocietyAgeBand.Infant &&
        !society.Checkpoint.Memories.Any(memory => memory.OwnerId == actor && memory.SubjectId == target &&
            memory.Id.StartsWith($"child-social:{kind}:", StringComparison.Ordinal) &&
            memory.SourceTick > WorldTick - ChildSocialCooldownTicks);

    private void AddChildCandidates(List<CognitionCandidate> candidates, string actor, PlaytestInhabitantState state)
    {
        var reachable = 0;
        foreach (var target in inhabitants.Keys.Where(id => id != actor &&
                     society.Checkpoint.GetInhabitant(id).AgeBand != SocietyAgeBand.Infant)
                 .OrderBy(id => map.FootDistance(state.Position, inhabitants[id].Position))
                 .ThenBy(id => id, StringComparer.Ordinal))
        {
            if (NeedsUrgentFood(state) && !IsWithinInteractionRange(state.Position, inhabitants[target].Position, ResourceInteractionRange))
                continue;
            if (FindUnoccupiedRoute(actor, state.Position, inhabitants[target].Position, ResourceInteractionRange).Count == 0)
                continue;
            if (ChildSocialAvailable(actor, target, "converse"))
                candidates.Add(new($"child_converse:{target}", "Talk with a nearby person and remember the encounter.", 38, target));
            if (ChildSocialAvailable(actor, target, "play"))
                candidates.Add(new($"child_play:{target}", "Play together and build a friendship.", 34, target));
            if (AdultResident(target) && ChildSocialAvailable(actor, target, "learn"))
                candidates.Add(new($"child_learn:{target}", "Learn a simple observation from an adult without taking an adult work role.", 42, target));
            if (++reachable == 3) break;
        }

        var householdId = society.Checkpoint.GetInhabitant(actor).HouseholdId;
        var house = householdId is null ? null : HouseForHousehold(householdId);
        if (state.HungerBasisPoints >= 6_000 && PreferredFood(actor, actor).Any(lot => AvailableLotQuantity(lot) > 1) &&
            FindUnoccupiedRoute(actor, state.Position,
                house?.Position ?? SettlementStoragePosition,
                house is null ? ResourceInteractionRange : 0).Count > 0)
            candidates.Add(new("child_help_food", "Carry one spare food serving back to the household store.", 26));
    }

    private void ApplyChildCandidate(string actor, PlaytestInhabitantState state, string candidate)
    {
        if (!ChildResident(actor) || NeedsUrgentWarmth(state)) return;
        if (candidate == "child_help_food")
        {
            if (state.HungerBasisPoints < 6_000 ||
                PreferredFood(actor, actor).FirstOrDefault(lot => AvailableLotQuantity(lot) > 1) is not { } lot)
                return;
            var householdId = society.Checkpoint.GetInhabitant(actor).HouseholdId;
            var house = householdId is null ? null : HouseForHousehold(householdId);
            var store = house?.Position ?? SettlementStoragePosition;
            var interactionRange = house is null ? ResourceInteractionRange : 0;
            if (!IsWithinInteractionRange(state.Position, store, interactionRange))
            {
                MoveToward(actor, state, store, "child_help", interactionRange);
                return;
            }
            ApplyInventoryTransition(inventory => InventoryFixture.Transfer(inventory,
                $"child-help:{WorldTick}:{actor}", actor, HouseholdFor(actor), lot.Id, 1,
                "child_household_help", house?.InstanceId));
            AppendEvent("child_helped_household", actor);
            return;
        }

        var separator = candidate.IndexOf(':', StringComparison.Ordinal);
        if (separator < 0) return;
        var kind = candidate[6..separator];
        var target = candidate[(separator + 1)..];
        if (kind is not ("converse" or "play" or "learn") || !ChildSocialAvailable(actor, target, kind) ||
            kind == "learn" && !AdultResident(target)) return;
        var destination = inhabitants[target].Position;
        if (!IsWithinInteractionRange(state.Position, destination, ResourceInteractionRange))
        {
            MoveToward(actor, state, destination, "child_social", ResourceInteractionRange);
            return;
        }

        society.Apply(checkpoint => SocietyFixture.RecordSocialMemory(checkpoint,
            new($"child-social:{kind}:{actor}:{target}:{WorldTick}", actor, target,
                kind switch
                {
                    "converse" => "Talked with another person.",
                    "play" => "Played with another person.",
                    _ => "Learned by observing an adult.",
                }, "public", WorldTick)));
        IncreaseTrust(actor, target, 1, "child_" + kind);
        AppendEvent("child_" + kind, $"{actor}:{target}");
    }
}

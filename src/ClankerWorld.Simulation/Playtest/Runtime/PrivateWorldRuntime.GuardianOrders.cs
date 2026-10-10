using System.Security.Cryptography;
using System.Text;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private OwnerInstructionOrder? ParseGuardianOrder(string text, string actor)
    {
        const string prefix = "become guardian for ";
        if (!AdultResident(actor) || !text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        var target = text[prefix.Length..].Trim();
        if (target.Length == 0) return null;
        var active = society.Checkpoint.Inhabitants.Where(person => person.Status == SocietyInhabitantStatus.Active);
        // Bind the complete, unique name (or exact ID) once. Renames cannot
        // redirect a queued order, and leftover words are never guessed away.
        var named = active.Where(person => person.Id == target).ToArray();
        if (named.Length == 0 && InhabitantNameRules.CanonicalKey(target) is { } targetName)
            named = active.Where(person => string.Equals(InhabitantNameRules.CanonicalKey(person.Name), targetName,
                StringComparison.OrdinalIgnoreCase)).ToArray();
        if (named.Length != 1 || !NeedsCaregiver(named[0].Id) || inhabitants[named[0].Id].GuardianSearch is null)
            return null;
        return new("accept_guardianship", "queued", 1, 0, "guardianships", false, TargetAgentId: named[0].Id);
    }

    private CognitionCandidate? GuardianOrderCandidate(string adult, string child) =>
        CanAcceptGuardian(adult, child)
            ? new("guardian_accept:" + child, "Accept primary care of the child named in this order.", 0)
            : null;

    private void ExecuteGuardianOrder(OwnerQueuedInstruction instruction, CognitionCandidate candidate)
    {
        var adult = instruction.TargetInhabitantId;
        var child = instruction.Order!.TargetAgentId!;
        if (candidate.Id != "guardian_accept:" + child || !CanAcceptGuardian(adult, child))
        {
            SetOrderStatus(instruction, "blocked", GuardianOrderBlockedReason(adult, child));
            return;
        }
        ApplyDependentCareCandidate(adult, candidate.Id);
        if (society.Checkpoint.GetInhabitant(child).PrimaryCaregiverId == adult && !NeedsCaregiver(child))
            CreditOrderEffect(instruction, GuardianOrderEffectId(adult, child), 1);
        else
            SetOrderStatus(instruction, "blocked", GuardianOrderBlockedReason(adult, child));
    }

    private string GuardianOrderBlockedReason(string adult, string child)
    {
        if (!AdultResident(adult)) return "Only an adult can become a guardian.";
        if (!NeedsCaregiver(child) || inhabitants[child].GuardianSearch is null)
            return "The named child is no longer looking for a guardian.";
        if (!ReadyForBriefInteraction(adult)) return "The adult needs to finish their current activity before accepting care.";
        return "Waiting for this adult to be asked by the child's guardian search.";
    }

    private static string GuardianOrderEffectId(string adult, string child) =>
        "guardianship:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{adult}|{child}")));
}

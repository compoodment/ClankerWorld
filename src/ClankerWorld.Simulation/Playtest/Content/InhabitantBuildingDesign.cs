using System.Security.Cryptography;
using System.Text;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>
/// Bounded resident authorship: experienced builders may suggest one safe
/// workbench design per purpose. A suggestion is only a durable proposal; the
/// owner still reviews, validates, approves and stages it.
/// </summary>
public sealed partial class PrivateWorldRuntime
{
    private const int BuildingDesignExperienceRequired = 3;
    private const long BuildingDesignProposalCooldownTicks = 300;
    private static readonly string[] BuildingDesignPurposes = ["shelter", "storage", "hearth"];

    private void AddInhabitantBuildingDesignCandidates(
        List<CognitionCandidate> candidates,
        SocietyInhabitant inhabitant,
        PlaytestInhabitantState state)
    {
        if (inhabitant.CurrentRole != SocietyWorkRole.Builder ||
            (state.Proficiency?.Building ?? 0) < BuildingDesignExperienceRequired)
        {
            return;
        }

        var registry = contentRegistry.ExportState();
        var authored = registry.Events
            .Where(item => item.Kind == "package_proposed_by_inhabitant" && item.Detail == inhabitant.Id)
            .OrderBy(item => item.WorldTick)
            .ToArray();
        if (authored.Length > 0 && WorldTick - authored[^1].WorldTick < BuildingDesignProposalCooldownTicks)
        {
            return;
        }

        foreach (var purpose in BuildingDesignPurposes)
        {
            if (authored.Any(item => ProposalHasPurpose(registry, item.PackageId, purpose)))
            {
                continue;
            }

            var design = CreateInhabitantBuildingDesign(inhabitant, state, registry, purpose);
            candidates.Add(new CognitionCandidate(
                "invent:building:" + purpose,
                $"Propose {design.Name} for owner review: a 1 x 1 {purpose} costing {design.WoodCost} wood. This does not approve or build it.",
                35));
        }
    }

    private void ApplyInhabitantBuildingDesignCandidate(string inhabitantId, string candidateId)
    {
        var available = new List<CognitionCandidate>();
        var inhabitant = society.Checkpoint.GetInhabitant(inhabitantId);
        var state = inhabitants[inhabitantId];
        AddInhabitantBuildingDesignCandidates(available, inhabitant, state);
        if (!available.Any(item => item.Id == candidateId))
        {
            AppendEvent("inhabitant_building_proposal_rejected", inhabitantId + ":stale");
            return;
        }

        var purpose = candidateId["invent:building:".Length..];
        var design = CreateInhabitantBuildingDesign(inhabitant, state, contentRegistry.ExportState(), purpose);
        contentRegistry.Propose(design.Manifest, WorldTick, inhabitantId);
        AppendEvent("inhabitant_building_proposed", inhabitantId + ":" + design.Manifest.PackageId);
    }

    private static bool ProposalHasPurpose(ContentRegistryState registry, string packageId, string purpose)
    {
        var package = registry.Packages.FirstOrDefault(item => item.Manifest.PackageId == packageId);
        if (package is null)
        {
            return false;
        }
        try
        {
            return BuildingDesign.Read(package.Manifest).Purpose == purpose;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidDataException or InvalidOperationException or FormatException)
        {
            return false;
        }
    }

    private static (ContentPackageManifest Manifest, string Name, string Purpose, int WoodCost) CreateInhabitantBuildingDesign(
        SocietyInhabitant inhabitant,
        PlaytestInhabitantState state,
        ContentRegistryState registry,
        string purpose)
    {
        var author = new string(inhabitant.Name.Where(character => !char.IsControl(character)).ToArray()).Trim();
        if (author.Length == 0)
        {
            author = "Resident";
        }
        var label = purpose switch
        {
            "shelter" => "shelter study",
            "storage" => "storehouse study",
            "hearth" => "hearth study",
            _ => throw new ArgumentException("Unknown building purpose.", nameof(purpose)),
        };
        var suffix = "'s " + label;
        author = author[..Math.Min(author.Length, 96 - suffix.Length)].TrimEnd();
        var name = author + suffix;
        var baseCost = purpose switch { "shelter" => 8, "storage" => 6, _ => 4 };
        var woodCost = baseCost - Math.Min(3, (state.Proficiency?.Building ?? 0) / 3);
        var manifest = BuildingDesign.Create(name, purpose, woodCost);
        if (registry.Packages.Any(item => item.Manifest.PackageId == manifest.PackageId) &&
            !registry.Events.Any(item => item.PackageId == manifest.PackageId &&
                item.Kind == "package_proposed_by_inhabitant" && item.Detail == inhabitant.Id))
        {
            var authorTag = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(inhabitant.Id)))[..6];
            var collisionSuffix = " [" + authorTag + "]";
            name = name[..Math.Min(name.Length, 96 - collisionSuffix.Length)].TrimEnd() + collisionSuffix;
            manifest = BuildingDesign.Create(name, purpose, woodCost);
        }
        return (manifest, name, purpose, woodCost);
    }
}

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>Immutable scope of a supported construction proposal; a name grants no authority.</summary>
public sealed record TownProjectPayload(string Name, string DefinitionId, GridPoint Site,
    GridPoint Entrance, IReadOnlyList<ContentQuantity> Budget);

/// <summary>An actual load and its retained input receipt, including released history.</summary>
public sealed record TownProjectDelivery(string Id, string ContributorId, string SourceLotId,
    string LotId, string ItemKind, int Quantity, long PickedUpTick, long? DeliveredTick = null,
    string? ReservationId = null, long? ReleasedTick = null, string? ReleaseReason = null);

/// <summary>One shared job per passed proposal; workers never own a separate copy of its budget.</summary>
public sealed record TownConstructionProject(string Id, string ProposalId, TownProjectPayload Plan,
    long ApprovedTick, string Stage, int WorkDone, long LastTransitionTick,
    IReadOnlyList<TownProjectDelivery> Deliveries, string? Blocker = null, string? CompletedBuildingId = null)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? RemovedTick { get; init; }
}

public static class TownProjectRules
{
    public const int WorkTicks = 10;
    public const int MaximumNameLength = 80;

    public static IReadOnlyList<BuildingDefinition> Definitions { get; } =
        Array.AsReadOnly(new[] { TownHallContent.Hall3x4(), MarketContent.Hall2x2(), MarketContent.Stall1x1() });

    public static BuildingDefinition? DefinitionFor(string definitionId) =>
        Definitions.SingleOrDefault(definition => definition.CanonicalId == definitionId);

    public static int RequiredWork(TownProjectPayload plan) =>
        plan.DefinitionId == MarketContent.Stall1x1().CanonicalId ? 3 : WorkTicks;

    public static IEnumerable<GridPoint> Footprint(TownProjectPayload plan) =>
        plan.DefinitionId == MarketContent.Hall2x2().CanonicalId
            ? MarketContent.SiteTiles(plan.Site)
            : WorldContentSimulationRules.Footprint(DefinitionFor(plan.DefinitionId) ??
                throw new InvalidDataException("Unsupported Town project definition."), plan.Site);

    public static void ValidatePayload(TownProjectPayload? plan)
    {
        var definition = plan is null ? null : DefinitionFor(plan.DefinitionId);
        if (plan is null || string.IsNullOrWhiteSpace(plan.Name) || plan.Name != plan.Name.Trim() ||
            plan.Name.Length > MaximumNameLength || plan.Name.Any(char.IsControl) ||
            definition is null || !EntranceMatches(plan) ||
            plan.Budget is null || !plan.Budget.SequenceEqual(definition.BuildCosts))
            throw new InvalidDataException("The Town project must bind a supported building, name, doorway and exact provisional budget.");
    }

    private static bool EntranceMatches(TownProjectPayload plan) => plan.DefinitionId == TownHallContent.Hall3x4().CanonicalId
        ? plan.Entrance == TownHallContent.Entrance(plan.Site)
        : plan.DefinitionId == MarketContent.Hall2x2().CanonicalId
            ? plan.Entrance == MarketContent.HallEntrance(plan.Site)
            : plan.Entrance.X == plan.Site.X && Math.Abs((long)plan.Entrance.Y - plan.Site.Y) == 1;

    public static string RequestKey(TownProjectPayload plan)
    {
        ValidatePayload(plan);
        var binding = string.Join('\n', plan.DefinitionId,
            plan.Site.X.ToString(CultureInfo.InvariantCulture), plan.Site.Y.ToString(CultureInfo.InvariantCulture),
            plan.Entrance.X.ToString(CultureInfo.InvariantCulture), plan.Entrance.Y.ToString(CultureInfo.InvariantCulture),
            string.Join('|', plan.Budget.Select(q => q.ResourceId + ":" + q.Amount.ToString(CultureInfo.InvariantCulture))));
        return "project:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(binding)));
    }

    public static string ProjectId(TownProposal proposal) => proposal.Id + ":construction";

    public static string BuildingId(string projectId) => "town-build-" +
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(projectId)));

    public static string ReservationPurpose(string projectId) => "town-project:" + projectId;

    public static string DeliveryId(string projectId, string actor, string sourceLotId, long tick, int ordinal) =>
        "town-project-load:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
            new[] { projectId, actor, sourceLotId, tick.ToString(CultureInfo.InvariantCulture), ordinal.ToString(CultureInfo.InvariantCulture) }))));

    public static string ProposalText(TownProjectPayload plan) =>
        FormattableString.Invariant($"Build {plan.Name}, a {DefinitionFor(plan.DefinitionId)?.DisplayName ?? "Town building"} at ({plan.Site.X},{plan.Site.Y}), with ") +
        string.Join(" and ", plan.Budget.Select(q => q.Amount.ToString(CultureInfo.InvariantCulture) + " " + q.ResourceId)) + " (provisional budget).";

    public static bool SameScope(TownProjectPayload first, TownProjectPayload second) =>
        first.Name == second.Name && first.DefinitionId == second.DefinitionId && first.Site == second.Site &&
        first.Entrance == second.Entrance && first.Budget.SequenceEqual(second.Budget);

    /// <summary>Usable delivered stock or already spent project stock, never a cumulative delivery counter.</summary>
    public static int DeliveredQuantity(TownConstructionProject project, string townId,
        InventoryCheckpoint inventory, string itemKind)
    {
        var site = new InventoryGroundPosition(project.Plan.Site.X, project.Plan.Site.Y);
        return project.Deliveries.Where(d => d.ItemKind == itemKind && d.ReleasedTick is null &&
                d.DeliveredTick is not null && d.ReservationId is not null)
            .Where(d => inventory.Reservations.Any(r => r.Id == d.ReservationId && r.OwnerId == townId &&
                r.LotId == d.LotId && r.Quantity == d.Quantity && r.Purpose == ReservationPurpose(project.Id) &&
                (project.Stage == "completed" && r.State == InventoryReservationState.Completed ||
                 r.State == InventoryReservationState.Reserved && r.ExpiryTick >= inventory.WorldTick &&
                 inventory.Lots.Any(l => l.Id == d.LotId && l.OwnerId == townId && l.ItemKind == itemKind &&
                     l.Quantity >= d.Quantity && l.GroundPosition == site && l.StorageBuildingId is null &&
                     l.CarrierId is null && l.ContainerLotId is null && l.FreshnessBasisPoints > 0 &&
                     l.ConditionBasisPoints > 0))))
            .Sum(d => d.Quantity);
    }
}

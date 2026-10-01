using ClankerWorld.Simulation.Harness;

namespace ClankerWorld.Simulation.World;

/// <summary>Why a tree could not be planted. <see cref="TreeGrowthRules.Reason"/> gives a short explanation.</summary>
public enum TreePlantingRefusal
{
    UnsupportedSpecies,
    NotATreeSeed,
    SeedNotOwned,
    NoSeedLeft,
    PlanterUnavailable,
    TooFar,
    OutsideMap,
    Water,
    Sand,
    UnsuitableGround,
    Building,
    Road,
    Occupied,
    AreaFull,
}

/// <summary>The outcome of one typed planting request. A refusal changes nothing, so the seed is kept.</summary>
public sealed record TreePlantingResult(bool Planted, TreePlantingRefusal? Refusal, string Message, string? TreeId = null)
{
    public static TreePlantingResult Success(string treeId, string species) =>
        new(true, null, $"A {species} sapling was planted.", treeId);

    public static TreePlantingResult Refused(TreePlantingRefusal refusal) =>
        new(false, refusal, TreeGrowthRules.Reason(refusal));
}

/// <summary>
/// Tree species, growth stages and planting ground shared by the world rules,
/// the host observation and save validation. Growth, harvest, inspection and
/// art all read the same saved <see cref="EcologyResource"/>.
/// </summary>
public static class TreeGrowthRules
{
    public const string Broadleaf = "broadleaf";
    public const string Conifer = "conifer";
    public const string Orchard = "orchard";

    /// <summary>One tree-seed item serves both wood species (content list 2.1 and 2.2).</summary>
    public const string TreeSeedItem = "tree_seed";
    public const string OrchardSeedItem = "orchard_seed";
    public const int OrchardSeedsPerPick = 1;

    /// <summary>Map resource IDs of trees planted on new tiles: <c>planted-tree-{x}-{y}</c>.</summary>
    public const string PlantedTreeIdPrefix = "planted-tree-";

    // Provisional values (#462). They are starting points to tune in
    // playtests, not agreed balance. Keep every tree number here.

    /// <summary>Days a planted sapling takes to become a mature tree. Provisional.</summary>
    public const int SaplingGrowthDays = 3;

    /// <summary>Days before a stump can regrow, during <see cref="StumpRegrowthSeason"/>. Provisional.</summary>
    public const int StumpRegrowthDays = 6;

    /// <summary>The season in which stumps regrow on their own. Provisional.</summary>
    public const SeasonKind StumpRegrowthSeason = SeasonKind.Spring;

    /// <summary>The only season in which orchard trees bear fruit (owner's leaning, 30 September).</summary>
    public const SeasonKind OrchardFruitSeason = SeasonKind.Autumn;

    /// <summary>Days a picked orchard tree takes to fruit again while its season lasts. Provisional.</summary>
    public const int OrchardRefruitDays = 3;

    /// <summary>Fruit gathered from one picking. Provisional.</summary>
    public const int OrchardFruitPerPick = 4;

    /// <summary>Tree seeds an agent collects when felling a mature wood tree. Provisional.</summary>
    public const int TreeSeedsPerFelledTree = 1;

    public static bool IsWoodTree(string? treeKind) => treeKind is Broadleaf or Conifer;

    public static bool IsPlantableSpecies(string? species) => IsWoodTree(species) || species == Orchard;
    public static string SeedItem(string species) => species == Orchard ? OrchardSeedItem : TreeSeedItem;
    public static string ResourceKind(string species) => species == Orchard ? "fruit" : "construction";

    public static string PlantedTreeId(GridPoint position) =>
        $"{PlantedTreeIdPrefix}{position.X}-{position.Y}";

    /// <summary>The growth state a generated tree starts a new world with.</summary>
    public static EcologyResource GeneratedTree(MapResource tree)
    {
        ArgumentNullException.ThrowIfNull(tree);
        if (tree.TreeKind == Orchard && tree.IsRenewable)
        {
            // New worlds start in spring: an orchard tree has leaves but no
            // fruit until its fruiting season arrives.
            return new EcologyResource(tree.Id, tree.Kind, tree.Position, true, 0, 1, 1, OrchardRefruitDays,
                OrchardFruitSeason, 0, EcologyResourceState.Regenerating);
        }

        var interval = tree.TreeKind == Orchard ? OrchardRefruitDays : StumpRegrowthDays;
        return new EcologyResource(tree.Id, tree.Kind, tree.Position, tree.IsRenewable, 1, 1,
            tree.IsRenewable ? 1 : 0, tree.IsRenewable ? interval : 0, StumpRegrowthSeason, interval,
            EcologyResourceState.Available);
    }

    /// <summary>A newly planted sapling. It becomes a mature tree after <see cref="SaplingGrowthDays"/>.</summary>
    public static EcologyResource PlantedSapling(MapResource tree, long plantedDay)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentOutOfRangeException.ThrowIfNegative(plantedDay);
        return new EcologyResource(tree.Id, tree.Kind, tree.Position, true, 0, 1, 1,
            tree.TreeKind == Orchard ? OrchardRefruitDays : StumpRegrowthDays,
            tree.TreeKind == Orchard ? OrchardFruitSeason : StumpRegrowthSeason,
            checked(plantedDay + SaplingGrowthDays), EcologyResourceState.Regenerating,
            IsPlanted: true);
    }

    /// <summary>
    /// The visible stage of a tree, read from its saved growth state. Wood
    /// trees are sapling, mature or stump; orchard trees are fruiting, picked
    /// (no fruit during the fruiting season) or growing (out of season).
    /// </summary>
    public static string? StageOf(string? treeKind, EcologyResource? tree, SeasonKind season) => treeKind switch
    {
        Broadleaf or Conifer when tree is null => null,
        Broadleaf or Conifer when tree.IsPlanted => "sapling",
        Broadleaf or Conifer when tree.Quantity > 0 && tree.State == EcologyResourceState.Available => "mature",
        Broadleaf or Conifer => "stump",
        Orchard when tree?.IsPlanted == true => "sapling",
        Orchard when tree?.Quantity > 0 => "fruiting",
        Orchard when tree is not null && season == tree.RegenerationSeason => "picked",
        Orchard => "growing",
        _ => null,
    };

    /// <summary>
    /// Whether the ground itself can take a tree. Trees grow on grass and
    /// forest ground, never in water, on sand, on rock, snow, dry scrub or
    /// mountains. Buildings, Roads and other objects are checked separately.
    /// </summary>
    public static TreePlantingRefusal? GroundRefusal(SeededMap map, GridPoint point)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (!map.Contains(point)) return TreePlantingRefusal.OutsideMap;
        var hydrology = map.HydrologyAt(point);
        var surface = map.SurfaceAt(point);
        var terrain = map.TerrainKindAt(point);
        var isWater = hydrology is { } water
            ? water != WaterKind.Land
            : surface is { } layer
                ? layer == SurfaceKind.Water
                : terrain is TerrainKind.Water or TerrainKind.River or TerrainKind.Lake or TerrainKind.Ocean;
        if (isWater) return TreePlantingRefusal.Water;
        if (!map.IsBuildable(point)) return TreePlantingRefusal.UnsuitableGround;
        if (surface is { } ground)
        {
            return ground switch
            {
                SurfaceKind.Sand => TreePlantingRefusal.Sand,
                SurfaceKind.Grass or SurfaceKind.ForestFloor or SurfaceKind.FertileSoil => null,
                _ => TreePlantingRefusal.UnsuitableGround,
            };
        }
        return terrain switch
        {
            TerrainKind.Sand => TreePlantingRefusal.Sand,
            TerrainKind.Meadow or TerrainKind.Forest => null,
            _ => TreePlantingRefusal.UnsuitableGround,
        };
    }

    /// <summary>Whether a saved planted tree is a legal result of planting on this map's ground.</summary>
    public static bool IsValidPlantedTree(SeededMap map, MapResource tree)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(tree);
        return tree.Id == PlantedTreeId(tree.Position) && tree.Kind == ResourceKind(tree.TreeKind ?? "") && tree.IsRenewable &&
            IsPlantableSpecies(tree.TreeKind) && tree.NaturalObjectKind is null &&
            GroundRefusal(map, tree.Position) is null;
    }

    public static string Reason(TreePlantingRefusal refusal) => refusal switch
    {
        TreePlantingRefusal.UnsupportedSpecies => "Choose a broadleaf, conifer or orchard tree.",
        TreePlantingRefusal.NotATreeSeed => "Wood trees need tree seeds; orchard trees need orchard seeds.",
        TreePlantingRefusal.SeedNotOwned => "The planter can only plant a seed they carry.",
        TreePlantingRefusal.NoSeedLeft => "There is no tree seed left to plant.",
        TreePlantingRefusal.PlanterUnavailable => "Only an adult in the world can plant a tree.",
        TreePlantingRefusal.TooFar => "The planter needs to stand on or next to the tile.",
        TreePlantingRefusal.OutsideMap => "That tile is outside the map.",
        TreePlantingRefusal.Water => "Trees cannot be planted in water.",
        TreePlantingRefusal.Sand => "Trees do not grow on sand.",
        TreePlantingRefusal.UnsuitableGround => "Trees cannot grow on this ground.",
        TreePlantingRefusal.Building => "A building stands on that tile.",
        TreePlantingRefusal.Road => "That tile must stay clear for a Road or bridge entrance.",
        TreePlantingRefusal.Occupied => "That tile already has a tree or another natural site.",
        TreePlantingRefusal.AreaFull => "This part of the map cannot hold another tree yet.",
        _ => throw new ArgumentOutOfRangeException(nameof(refusal)),
    };
}

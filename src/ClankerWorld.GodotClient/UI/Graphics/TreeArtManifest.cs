namespace ClankerWorld.GodotClient.UI;

/// <summary>
/// One tree species and growth stage, the art that shows it, where that art
/// comes from and under which licence. <paramref name="Code"/> is the value the
/// terrain layer stores per tile; 0 means the state has no map sprite.
/// </summary>
public sealed record TreeArtEntry(
    byte Code,
    string Species,
    string Stage,
    string AssetId,
    NatureSprite? Sprite,
    string Source,
    string Licence,
    string Review);

/// <summary>
/// The single reviewed list of tree art. The map reads tree sprites and stage
/// names from here, so a new stage or replacement art is added in one place.
/// Each entry records whether its art was approved in an owner review.
/// </summary>
public static class TreeArtManifest
{
    public const string CodeDrawn = "Drawn in code by UI/Graphics/NatureSprites.cs";
    public const string RepositoryLicence = "MIT, the repository licence (LICENSE)";
    public const string Provisional = "Provisional placeholder; not approved final art";
    public const string Approved = "Approved in the October 1 art review";
    public const string Missing = "Missing: no art yet; the game shows the item by name";

    public static IReadOnlyList<TreeArtEntry> Entries { get; } =
    [
        new(1, "broadleaf", "mature", "tree.broadleaf.mature", NatureSprite.Broadleaf, CodeDrawn, RepositoryLicence, Approved),
        new(2, "conifer", "mature", "tree.conifer.mature", NatureSprite.Conifer, CodeDrawn, RepositoryLicence, Approved),
        new(3, "broadleaf", "stump", "tree.broadleaf.stump", NatureSprite.BroadleafStump, CodeDrawn, RepositoryLicence, Approved),
        new(4, "conifer", "stump", "tree.conifer.stump", NatureSprite.ConiferStump, CodeDrawn, RepositoryLicence, Approved),
        new(5, "broadleaf", "sapling", "tree.broadleaf.sapling", NatureSprite.BroadleafSapling, CodeDrawn, RepositoryLicence, Approved),
        new(6, "conifer", "sapling", "tree.conifer.sapling", NatureSprite.ConiferSapling, CodeDrawn, RepositoryLicence, Approved),
        new(7, "orchard", "fruiting", "tree.orchard.fruiting", NatureSprite.OrchardFruiting, CodeDrawn, RepositoryLicence, Approved),
        new(8, "orchard", "picked", "tree.orchard.picked", NatureSprite.OrchardPicked, CodeDrawn, RepositoryLicence, Approved),
        new(9, "orchard", "growing", "tree.orchard.growing", NatureSprite.OrchardGrowing, CodeDrawn, RepositoryLicence, Approved),
        // One tree-seed item serves broadleaf and conifer. It lives in
        // inventories only, which list items by name.
        new(0, "broadleaf", "seed", "item.tree_seed", null, "None", "None", Missing),
        new(0, "conifer", "seed", "item.tree_seed", null, "None", "None", Missing),
    ];

    private static readonly Dictionary<byte, TreeArtEntry> ByCode = Entries
        .Where(entry => entry.Code != 0)
        .ToDictionary(entry => entry.Code);

    public static TreeArtEntry? ForCode(byte code) => ByCode.GetValueOrDefault(code);

    public static TreeArtEntry? For(string species, string stage) => Entries.FirstOrDefault(entry =>
        entry.Species == species && entry.Stage == stage);
}

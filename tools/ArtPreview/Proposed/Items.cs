using ClankerWorld.GodotClient.UI;
using Godot;

namespace ArtPreview.Proposed.Items;

/// <summary>
/// Proposed 16 × 16 item icons, round 1. Each icon is a text bitmap in the
/// same form as the game's <see cref="ItemIcons"/>: a palette of "kRRGGBB"
/// entries and sixteen rows of sixteen letters, '.' for empty, with nothing
/// on the outer one-pixel ring so the outline added by
/// <see cref="Bitmap.Outlined"/> always fits. Shades come from the STYLE.md
/// ramps, lit from the top-left and shaded to the bottom-right (I1). Tool
/// tiers share the handle and differ in the head (I3); cooked food sits in a
/// bowl or on a plate (I4).
/// </summary>
public sealed class ItemsProposal : IArtProposal, IArtSetProvider
{
    public string Family => "items";
    public string Name => "items";

    /// <summary>The icon grid, as in <see cref="ItemIcons.Grid"/>.</summary>
    private const int Grid = 16;

    /// <summary>Outline ink shared with the game's item icons (STYLE.md L3).</summary>
    private static readonly Color Ink = new("2A1A10");

    /// <summary>One hand-drawn icon: a note for the review sheet, its palette and its rows.</summary>
    private sealed record Drawing(string Note, string Palette, string[] Rows);

    // Palettes shared between icons, so tiers and families stay consistent.

    /// <summary>The handle every tool shares: the game's current tool-handle browns (light, shade).</summary>
    private const string Handle = "HC08448 D7A4E26";

    /// <summary>Worked iron, the cool steel of the game's iron bar icon: glint, light, base, shade, dark.</summary>
    private const string Steel = "wEEF2F4 lBFC8CF m939CA5 d6A727B k50575E";

    /// <summary>Fiber cord lashing a head to its handle (Thatch light and shade).</summary>
    private const string Cord = "cE6C77B CA98A45";

    /// <summary>
    /// The icons in sheet order: food and drink, then materials, tools and
    /// crafted goods. Kinds that replace a current icon keep its Id.
    /// </summary>
    private static readonly (string Kind, Drawing Art)[] Drawings =
    [
        ("water", new("Fresh water: a Lake-blue drop with a glint, its own silhouette beside the jug.",
            "wE8FFFF h8ABBD6 l6A9FC0 m598FB3 d4A7B9D",
            [
                "................",
                ".......hl.......",
                ".......hm.......",
                "......hllm......",
                "......hlmm......",
                ".....hllmmm.....",
                ".....hlmmmd.....",
                "....hllmmmmd....",
                "....hwlmmmmd....",
                "...hwllmmmmmd...",
                "...hwlmmmmmmd...",
                "...hwlmmmmmdd...",
                "...hlmmmmmmdd...",
                "....lmmmmmdd....",
                ".....dddddd.....",
                "................",
            ])),
        ("wild_greens", new("Wild greens: a loose bunch of three pointed leaves with pale midribs, so it reads as picked leaves rather than a lettuce head (broadleaf and orchard canopy greens).",
            "h8DB660 v9BC66F b6C9A4B s557D3E g8DB660 G476B36",
            [
                "................",
                ".......h........",
                "......hvb.......",
                ".h....hvbs....s.",
                ".hh..hhvbs...bs.",
                ".hvb.hvvbs..bvs.",
                "..hvbhbvbs.bvbs.",
                "..hbvbhvbsbvbs..",
                "...hbvbvbbvbs...",
                "....hbvvvvbs....",
                ".....hbvvbs.....",
                "......gGGG......",
                ".......gG.......",
                ".......gG.......",
                "................",
                "................",
            ])),
        ("potato", new("Potato: two lumpy tan potatoes with eyes, a big one behind and a small one in front (Timber light and highlight).",
            "wE3D6B5 hD2AC77 lA77C52 e8A6440",
            [
                "................",
                "................",
                "................",
                "...whhhh........",
                "..whhhhhhl......",
                ".whhehhhhhl.....",
                ".hhhhhhhehl.....",
                ".hhhhhhhhll.....",
                ".hheehhhhll.....",
                ".lhhhhhhll......",
                "..llhhll..hhh...",
                "...llll..whhhl..",
                ".........hhehll.",
                ".........lhhlll.",
                "..........llll..",
                "................",
            ])),
        ("grain", new("Grain: a fuller sheaf of three plump ears, tied with a twine band (Thatch ramp, STYLE N5).",
            "hF0DA9A lE6C77B bD2AE5E sA98A45 e6B5528 t8A6440 TA77C52",
            [
                "................",
                "......hll.......",
                "..hl..hlbb..hl..",
                ".hlbs.lhbs.hlbs.",
                ".lhbs.hlbs.lhbs.",
                ".hlbs.lhbs.hlbs.",
                ".lhbs.hlbs.lhbs.",
                "..lbs.lhbs.lbs..",
                "....sbsbsbss....",
                ".....bsbsbs.....",
                ".....tTTTTt.....",
                ".....sbsbse.....",
                "....sbsbsbse....",
                "...sbsbsbsbse...",
                "................",
                "................",
            ])),
        ("bread", new("Bread: a taller domed loaf with three diagonal scores showing pale crumb, in the current crust colours (STYLE I4).",
            "cC07A34 CDC9C52 hF2C27E uF0DA9A d8A5222 k6A3C18",
            [
                "................",
                "................",
                "................",
                "................",
                "......CCCC......",
                "....CChhCCcc....",
                "...ChhhuCCCuc...",
                "..ChhCuCCCuCcc..",
                ".ChhCuCCCuCCccd.",
                ".cCCuCCCuCCcccd.",
                ".cCCCCCCCCCcccd.",
                ".cccccccccccccd.",
                "..cccccccccccdd.",
                "...kdddddddddk..",
                "................",
                "................",
            ])),
        ("porridge", new("Porridge: a wooden bowl of creamy porridge with a spoon (Timber bowl, Cloth porridge, STYLE I4).",
            "RD2AC77 bA77C52 B8A6440 d6E4E31 cFFF5DF CE8DCC0 qCABC99 sD2AC77 SA77C52",
            [
                "................",
                "............sS..",
                "...........sS...",
                "..........sS....",
                ".....ccccsSc....",
                "...ccCCCsSCqq...",
                "..RcCCCCCCCqqR..",
                ".RcCCCqCCCCqqqR.",
                ".RCCCCCCCCqqqqR.",
                ".bRRRRRRRRRRRRB.",
                ".bbbbBBBBBBBBBd.",
                "..bbBBBBBBBBBd..",
                "...BBBBBBBBBd...",
                "....dBBBBBBd....",
                ".....dddddd.....",
                "................",
            ])),
        ("stew", new("Stew: a clay bowl with two lug handles, brown broth with potato and greens (Clay ramp, STYLE I4).",
            "REFA882 bD5825A BB8623C d8E4428 o8A6440 O6E4E31 yA77C52 pD2AC77 PE3D6B5 g8DB660 G557D3E",
            [
                "................",
                "................",
                "................",
                "................",
                "....RRRRRRRR....",
                "..RRyooPpooyRR..",
                ".RooPpoogGooOOR.",
                ".RyGgooooPpoOOR.",
                ".bRoooGgooOOORd.",
                "..bRRRRRRRRRRd..",
                ".bbBBBBBBBBBBdd.",
                ".dbbBBBBBBBBddd.",
                "..bbBBBBBBBBdd..",
                "...bBBBBBBBBd...",
                ".....dddddd.....",
                "................",
            ])),
        ("meal", new("Meal: two cooked potatoes and a heap of greens on a pale plate with a lit rim (STYLE I4).",
            "PFFF5DF pE8DCC0 qCABC99 QA09170 wE3D6B5 hD2AC77 lA77C52 e8A6440 g8DB660 G6C9A4B k476B36",
            [
                "................",
                "................",
                "................",
                "................",
                "...gg...........",
                "..gGGg.whh......",
                ".gGkGgwhhhl.....",
                ".kgGkGhhhhe.wh..",
                "..kGkkhhhlewhhl.",
                ".PPPPPPlllehhll.",
                ".Ppppppppppppll.",
                ".qpppppppppppQQ.",
                "..qqqqqqqqqqQQ..",
                "....QQQQQQQQ....",
                "................",
                "................",
            ])),
        ("wood", new("Wood: the same two logs, with rounder pale end grain showing a growth ring and pith, and bark grooves as short strokes instead of specks (Timber ramp, Thatch light end grain).",
            "fE6C77B rD2AC77 cA77C52 lA77C52 m8A6440 b6E4E31",
            [
                "................",
                "................",
                "................",
                "....fffllllllll.",
                "...frrrfmmlmmmm.",
                "...frcrfbbmmmbb.",
                "...frrrfmmmbmmm.",
                "....fffbbbbbbbb.",
                "................",
                "..ffflllllllll..",
                ".frrrfmmmlmmmm..",
                ".frcrfbbmmmbbm..",
                ".frrrfmmbmmmmm..",
                "..fffbbbbbbbbb..",
                "................",
                "................",
            ])),
        ("stone", new("Stone: a faceted chunk with a flat lit top, a crease and a loose pebble, in the cool Peak greys so it stays apart from the warmer iron ore.",
            "wEEF3F1 hC4C7C5 lAEB2B0 m8E8A85 d5F5955",
            [
                "................",
                "................",
                "......whhh......",
                "....whhhhhhm....",
                "...whhhhhhhhm...",
                "..whhhhhhhhmmm..",
                "..lllhhhhhdmmmd.",
                ".lllllllldmmmmd.",
                ".llllllldmmmmdd.",
                ".lllllldmmmmddd.",
                "..mllllmmmmdd...",
                "...mmmlmmddd.hl.",
                ".....mmmdd.hlmd.",
                "............lmd.",
                "................",
                "................",
            ])),
        ("diamond", new("Diamond: a cut gem with a flat table, crown and pavilion facets (Diamond ramp).",
            "wE8FFFF lB4EEF2 m7FD3DC s5FB4BE",
            [
                "................",
                "................",
                "................",
                "....wwlllllm....",
                "...wllwwllmms...",
                "..wlllwllllmss..",
                ".mmmmmmmmmmssss.",
                "..lllmlllmmsss..",
                "...llmlllmsss...",
                "....lmllmmss....",
                ".....mlmmss.....",
                "......lmss......",
                ".......ms.......",
                "................",
                "................",
                "................",
            ])),
        ("wooden_axe", new("Wooden axe: a carved wooden blade (Timber light) lashed to the shared handle with fiber cord (STYLE I3).",
            Handle + " " + Cord + " hD2AC77 lA77C52 m8A6440 d6E4E31",
            [
                "................",
                ".hh......HD.....",
                ".hlll....HD.....",
                ".hllllllcCcC....",
                ".hlllmmmCcCc....",
                ".hlmmmmmcCcC....",
                ".hmmdddmCcCc....",
                ".mmd.....HD.....",
                ".dd......HD.....",
                ".........HD.....",
                ".........HD.....",
                ".........HD.....",
                ".........HD.....",
                ".........HD.....",
                ".........DD.....",
                "................",
            ])),
        ("stone_axe", new("Stone axe: a knapped stone head (Rock light) with rounded, chipped corners, lashed to the shared handle (STYLE I3).",
            Handle + " " + Cord + " hA49C95 l8B837D m756D68 d625B56",
            [
                "................",
                "..hh.....HD.....",
                ".hhll....HD.....",
                ".hlhllmmcCcC....",
                ".hllmlmmCcCc....",
                ".lhlmmdmcCcC....",
                ".lmmdmddCcCc....",
                ".mmdd....HD.....",
                "..dd.....HD.....",
                ".........HD.....",
                ".........HD.....",
                ".........HD.....",
                ".........HD.....",
                ".........HD.....",
                ".........DD.....",
                "................",
            ])),
        ("iron_axe", new("Iron axe: a flared steel blade with a socket round the shared handle and a one-pixel glint (STYLE I3).",
            Handle + " " + Steel,
            [
                "................",
                "..ll.....HD.....",
                ".wllll..lmmd....",
                ".wllmmmmlwmdd...",
                ".wlmmmmmlmmdd...",
                ".wlmmdddlmmdd...",
                ".wmmdd..lmmd....",
                ".wmdd....HD.....",
                "..dd.....HD.....",
                ".........HD.....",
                ".........HD.....",
                ".........HD.....",
                ".........HD.....",
                ".........HD.....",
                ".........DD.....",
                "................",
            ])),
        ("iron_pickaxe", new("Iron pickaxe: the wooden pickaxe's layout with a steel head, socket and glint (STYLE I3).",
            Handle + " " + Steel,
            [
                "................",
                ".......HD.......",
                "...wlllwmmmmd...",
                "..wllmlwmmmmdd..",
                ".wlm..lmmd..mdd.",
                ".wl....HD....dd.",
                ".l.....HD.....d.",
                ".......HD.......",
                ".......HD.......",
                ".......HD.......",
                ".......HD.......",
                ".......HD.......",
                ".......HD.......",
                ".......HD.......",
                ".......DD.......",
                "................",
            ])),
        ("sickle", new("Sickle: a curved steel blade, bright on its inner cutting edge, on the shared handle with an iron ferrule.",
            Handle + " " + Steel,
            [
                "................",
                ".....mmmmm......",
                "...mmwlllmmd....",
                "..mlw.....lmd...",
                ".mlw.......lmd..",
                ".mw........lmd..",
                ".mw........lmd..",
                "..w........lmd..",
                "...........lmd..",
                "..........kmmk..",
                "...........HD...",
                "...........HD...",
                "...........HD...",
                "...........HD...",
                "...........DD...",
                "................",
            ])),
        ("rope", new("Rope: a coil of twisted rope seen from above, two turns with dark gaps between them and a frayed loose end (Timber browns).",
            "hD2AC77 mA77C52 d8A6440 fE6C77B",
            [
                "................",
                ".....mhhmhh.....",
                "...hmhhmhhmhh...",
                "..hmh......hdm..",
                "..mh..hhmh..mm..",
                ".mh..hhmhhd..dm.",
                ".hh.hhm..dmm.mm.",
                ".hm.hm....md.md.",
                ".mh.mhh..mdm.dm.",
                ".hh..hdmmdm..mm.",
                "..mh..mmdm..mm..",
                "..hhd......mmd..",
                "...dmmdmmdmmd...",
                ".....dmmdmmdmm..",
                "............mhf.",
                "................",
            ])),
        ("basket", new("Basket: a woven wicker basket with an arched handle (Thatch ramp).",
            "hF0DA9A lE6C77B bD2AE5E sA98A45 e6B5528",
            [
                "................",
                ".....hlllls.....",
                "....hl....bs....",
                "...hl......bs...",
                "...hl......bs...",
                "...hl......bs...",
                ".hhllllllllllls.",
                ".lsssssssssssse.",
                ".llbbllbbllbbse.",
                "..bbllbbllbbse..",
                "..llbbllbbllse..",
                "..bbllbbllbbse..",
                "...llbbllbbse...",
                "....eeeeeeee....",
                "................",
                "................",
            ])),
        ("water_jug", new("Water jug: a clay jug with a handle and a glimpse of water in the mouth (Clay ramp).",
            "hEFA882 lD5825A bB8623C s8E4428 w7FB4CF",
            [
                "................",
                "....hhhhhh......",
                "....hwwwwb......",
                ".....lbbs.......",
                ".....lbbsbllb...",
                "....hlbbbs..lb..",
                "...hlbbbbbs..lb.",
                "..hlbbbbbbbs.ls.",
                "..lbbbbbbbbbbs..",
                "..lbbbbbbbbbs...",
                "..lbbbbbbbbbs...",
                "..bbbbbbbbbbs...",
                "...bbbbbbbbs....",
                "....sssssss.....",
                "................",
                "................",
            ])),
        ("clothing", new("Clothing: the same blue tunic, now lit from the left and shaded on the right, with a V neck, dark cuffs, a belt with a buckle and a hem.",
            "b4A6E9E B6C92C4 h92B4DE d32507E k263E62 t7A4E2A TC09A5A",
            [
                "................",
                "................",
                "....bBhkkBbb....",
                "..bBhhBhkBBBbd..",
                ".bBhhBBBBBBBBbd.",
                ".bhBBBBBBBBBbbd.",
                ".BhBBBBBBBBBbdd.",
                ".ddbhBBBBBBbbdd.",
                "....hBBBBBbd....",
                "....tttTTttt....",
                "....hBBBBBbd....",
                "....hBBBBBbd....",
                "...hBBBBBBBbd...",
                "...dddddddddd...",
                "................",
                "................",
            ])),
        ("bandage", new("Bandage: a cream cloth roll with its spiral end showing and the loose strip unrolling to the right (Cloth ramp).",
            "hFFF5DF lE8DCC0 mCABC99 dA09170",
            [
                "................",
                "................",
                "................",
                ".....hhhh.......",
                "...hhllllhm.....",
                "..hlldddllmm....",
                "..hldlllldmm....",
                ".hldlhhdldlmm...",
                ".hldlhddldlmm...",
                ".hlldllldllmhhh.",
                "..hlldddllmmlll.",
                "..lllllllmmllmd.",
                "...mmmmmmmlllmd.",
                ".........mmmmd..",
                "................",
                "................",
            ])),
        ("medicine", new("Medicine: a corked glass bottle (Snow greys) of red tincture (Berry ramp) with a cream label and a green cross.",
            "cA77C52 C6E4E31 gDCE5E0 GB8C6C4 rC4474B RA33A3F pF08A8A LE8DCC0 x557D3E",
            [
                "................",
                "......cccc......",
                "......cCCC......",
                ".......gG.......",
                "......ggGG......",
                ".....gprrRG.....",
                "....gprrrrRG....",
                "...gprrrrrrRG...",
                "...grLLxLLrRG...",
                "...grLxxxLrRG...",
                "...grLLxLLrRG...",
                "...grrrrrrRRG...",
                "....GrrrrRRG....",
                ".....GGGGGG.....",
                "................",
                "................",
            ])),
        ("map", new("Map: a three-panel folded map on sand-coloured paper, with land, a river and a red route to an X.",
            "hE3D6B5 lC8B78C bBAA77B sA08F66 g6FA069 G527F4F w4786AB rC4474B",
            [
                "................",
                "................",
                ".hhhhh.....hhhh.",
                ".hhgghlllllhhhh.",
                ".hgGGhbbbbbhrhh.",
                ".hgggwwbbbblrhh.",
                ".hhhhlbwwbbhrhh.",
                ".hrhhlbbbwwhrhh.",
                ".hhrrlrrbbbwwwh.",
                ".hhhhlbbrrrhhrh.",
                ".hhhhlbbbbbhrhr.",
                ".hhhhlbbbbbhhhh.",
                ".sssssbbbbbssss.",
                "......sssss.....",
                "................",
                "................",
            ])),
        ("book", new("Book: a closed book with a red leather cover (Berry ramp), a small gold emblem and cream page edges.",
            "RF08A8A rC4474B sA33A3F e7A2A2E pFFF5DF PCABC99 GF2CC5E",
            [
                "................",
                "................",
                "...RRRRRRRRRR...",
                "..eRrrrrrrrrrs..",
                "..eRrrrrrrrrrsp.",
                "..eRrrrGGrrrrsp.",
                "..eRrrGrrGrrrsp.",
                "..eRrrrGGrrrrsp.",
                "..eRrrrrrrrrrsp.",
                "..eRrrrrrrrrrsp.",
                "..eRrrrrrrrrrsp.",
                "..essssssssssPp.",
                "...pppppppppppP.",
                "....PPPPPPPPPP..",
                "................",
                "................",
            ])),
        ("coin", new("Coin: a gold coin tilted to show its edge, with a raised rim and a small embossed star (Gold ramp).",
            "hFFE28A lF2CC5E bD9AE3C mB8902E d8A6A1E",
            [
                "................",
                "................",
                ".....hhhhhh.....",
                "...hhllllllhm...",
                "..hllbbbbbbllm..",
                "..hlbbbbhbbblm..",
                ".hlbbbbhlmbbblm.",
                ".hlbbbhlllmbblm.",
                ".hlbbbbmlmbbblm.",
                "..hlbbbbmbbblm..",
                "..mllbbbbbbllm..",
                "..dmmllllllmmd..",
                "...ddmmmmmmdd...",
                ".....dddddd.....",
                "................",
                "................",
            ])),
    ];

    private static readonly Dictionary<string, Drawing> ByKind =
        Drawings.ToDictionary(entry => entry.Kind, entry => entry.Art, StringComparer.Ordinal);

    /// <summary>The item kinds this proposal draws, in sheet order.</summary>
    public static IReadOnlyList<string> Kinds => Drawings.Select(entry => entry.Kind).ToArray();

    /// <summary>
    /// The outlined 16 × 16 icon for <paramref name="kind"/>. Kinds this
    /// proposal does not draw fall back to the game's current icon, and
    /// unknown kinds to its plain crate, as <see cref="ItemIcons"/> does.
    /// </summary>
    public static Image Icon(string kind)
    {
        if (!ByKind.TryGetValue(kind, out var art))
            return ItemIcons.OutlinedArt(ItemIcons.Has(kind) ? kind : "crate");
        CheckGrid(kind, art.Rows);
        try
        {
            return Bitmap.Outlined(Bitmap.Draw(art.Palette, art.Rows), Ink);
        }
        catch (ArgumentException problem)
        {
            throw new ArgumentException($"Icon '{kind}': {problem.Message}", problem);
        }
    }

    /// <summary>
    /// Throws unless the rows are 16 × 16 with an empty outer ring, so the
    /// outline always fits (the same rule as <see cref="ItemIcons.FitsGrid"/>).
    /// </summary>
    private static void CheckGrid(string kind, string[] rows)
    {
        if (rows.Length != Grid)
            throw new ArgumentException($"Icon '{kind}' has {rows.Length} rows, not {Grid}.");
        for (var y = 0; y < Grid; y++)
        {
            if (rows[y].Length != Grid)
                throw new ArgumentException($"Icon '{kind}' row {y} is {rows[y].Length} wide, not {Grid}.");
            var edgeRow = y == 0 || y == Grid - 1;
            if ((edgeRow && rows[y].Any(pixel => pixel != '.')) || rows[y][0] != '.' || rows[y][^1] != '.')
                throw new ArgumentException($"Icon '{kind}' row {y} touches the outer ring, which the outline needs.");
        }
    }

    /// <summary>One entry per icon, at its native 16 px; the review sheet scales it.</summary>
    public IEnumerable<Entry> Render()
    {
        foreach (var (kind, art) in Drawings)
            yield return new Entry(Family, kind, Icon(kind), art.Note);
    }

    /// <summary>Item icons do not appear in the reference scene, so nothing changes there.</summary>
    public void Apply(ArtSet set)
    {
    }
}

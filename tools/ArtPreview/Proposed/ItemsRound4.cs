using ClankerWorld.GodotClient.UI;
using Godot;

namespace ArtPreview.Proposed.Items;

/// <summary>
/// Round 4 (October 7): icons for items the game makes today that show a
/// plain crate or borrow another item's picture. All ten here were approved;
/// the crude wooden axe and pickaxe drawings were not, and keep the approved
/// wooden axe and pickaxe icons ("keep the og's"). Each is a 16 × 16
/// text bitmap in the same form as the game's <see cref="ItemIcons"/>, outlined
/// the same way, lit from the top-left, in STYLE.md ramp colours. Each sits
/// beside the icon it replaces or the family it joins on the review sheet.
/// </summary>
public sealed class ItemsRound4Proposal : IArtProposal
{
    public string Family => "items-round4";

    private const int Grid = 16;
    private static readonly Color Ink = new("2A1A10");

    private const string Steel = "wEEF2F4 lBFC8CF m939CA5 d6A727B k50575E";
    private const string GoldRamp = "hFFE28A lF2CC5E bD9AE3C mB8902E d8A6A1E wFFF5DF";
    /// <summary>The approved porridge bowl's palette, shared by every porridge so they read as one family.</summary>
    private const string Porridge = "RD2AC77 bA77C52 B8A6440 d6E4E31 cFFF5DF CE8DCC0 qCABC99 sD2AC77 SA77C52";

    private sealed record Drawing(string Kind, string Replaces, string Note, string Palette, string[] Rows);

    private static readonly Drawing[] Drawings =
    [
        new("iron_fittings", "iron",
            "Iron fittings: a corner bracket with nail holes and two nails, in the iron bar's steel.",
            Steel,
            [
                "................",
                ".......wlm.wlm..",
                "........m...m...",
                ".wll....m...m...",
                ".lmm....m...m...",
                ".lkm....d...d...",
                ".lmm............",
                ".lmm............",
                ".lkm............",
                ".lmm............",
                ".lmmllllllllll..",
                ".lmmmmmkmmmkmd..",
                ".lmmmmmmmmmmmd..",
                ".ddddddddddddd..",
                "................",
                "................",
            ]),
        new("gold", "gold_ore",
            "Refined gold: one bright cast ingot with a stamped mark, so it no longer shares the raw nuggets' picture.",
            GoldRamp,
            [
                "................",
                "................",
                "................",
                "................",
                "....hhhhhhhh....",
                "...hllllllllb...",
                "..hlwllllllllb..",
                ".hllllllllllllb.",
                ".bbbbbbbbbbbbbm.",
                ".bmmmmmmmmmmmmd.",
                ".mmmmmddmmmmmmd.",
                "..dddddddddddd..",
                "................",
                "................",
                "................",
                "................",
            ]),
        new("gold_ornament", "ornament",
            "Plain gold ornament: a thick polished band with no stone, beside the approved diamond-set ring.",
            GoldRamp,
            [
                "................",
                "................",
                "................",
                ".....hhllbm.....",
                "...hwlbbbbbmm...",
                "..hlbb....bbmm..",
                "..hlb......bmd..",
                ".hlb........bmd.",
                ".hlb........bmd.",
                ".llb........mmd.",
                "..lbb......bmd..",
                "..mmbb....bbmd..",
                "...mmmbbbbmmd...",
                ".....mmmmddd....",
                "................",
                "................",
            ]),
        new("saddle", "leather",
            "Saddle, seen from the side: a cantle and a tall horn either end of a dipped seat, a red blanket edge under it, and a stitched leather fender down to an iron stirrup.",
            "hE0A070 lC98A5A bB7774C mA9643C d7A4426 s939CA5 S6A727B rF08A8A RC4474B",
            [
                "................",
                "............hl..",
                "............lm..",
                ".hl.........lm..",
                ".hlb.......lbm..",
                "..lbbl...llbbm..",
                "..dmbbbbbbbbmd..",
                "..rRRRRRRRRRRr..",
                ".....lbbbbm.....",
                ".....lhbbbm.....",
                ".....lbbbhm.....",
                ".....lbbbbm.....",
                ".....dmmmmd.....",
                ".....S....S.....",
                ".....sSSSSs.....",
                "................",
            ]),
        new("cooked_eggs", "eggs",
            "Cooked eggs: two eggs fried in an iron pan with a wooden handle, instead of raw eggs in a nest.",
            "gA69E98 I8A827C i6C6560 k3E3A37 WFFF5DF yF2CC5E YD9AE3C H8A6440 D6E4E31",
            [
                "................",
                "................",
                "....gIIIIi......",
                "..gIiiiiiiik....",
                "..IiWWWiiiik....",
                ".gIWWyyWiWWik...",
                ".IiWYyWWWWWWk...",
                ".IiWWWWiWYyWk...",
                ".IiiWWiiWWWik...",
                "..kiiiiiiiik....",
                "...kkiiiiikkH...",
                ".......kkk.HHD..",
                "............HHD.",
                ".............DD.",
                "................",
                "................",
            ]),
        new("milk_porridge", "porridge",
            "Milk porridge: the approved porridge bowl and spoon, the oats sitting in a ring of milk.",
            Porridge + " MF4F8F6 NDCE5E0 OB8C6C4",
            [
                "................",
                "............sS..",
                "...........sS...",
                "..........sS....",
                ".....ccccsSc....",
                "...MNcCCsSCqN...",
                "..RMNcCCCCCqNR..",
                ".RMNNcCqCCqNNOR.",
                ".RNNNNNNNNNNOOR.",
                ".bRRRRRRRRRRRRB.",
                ".bbbbBBBBBBBBBd.",
                "..bbBBBBBBBBBd..",
                "...BBBBBBBBBd...",
                "....dBBBBBBd....",
                ".....dddddd.....",
                "................",
            ]),
        new("berry_porridge", "porridge",
            "Berry porridge: the same bowl and spoon with three purple berries on top (#794).",
            Porridge + " gC89AE6 eA060C0 E7E3A96 f4E2262",
            [
                "................",
                "............sS..",
                "...........sS...",
                "..........sS....",
                ".....ccccsSc....",
                "...ccgeCsSCqq...",
                "..RcCEfCCgeqqR..",
                ".RcgeCqCCEfqqqR.",
                ".RCEfCCCCCqqqqR.",
                ".bRRRRRRRRRRRRB.",
                ".bbbbBBBBBBBBBd.",
                "..bbBBBBBBBBBd..",
                "...BBBBBBBBBd...",
                "....dBBBBBBd....",
                ".....dddddd.....",
                "................",
            ]),
        new("fruit_porridge", "porridge",
            "Fruit porridge: the same bowl and spoon with slices of the approved pear on top (#794).",
            Porridge + " zF2EEB0 yDCD872 YC9C24E Z8E8428",
            [
                "................",
                "............sS..",
                "...........sS...",
                "..........sS....",
                ".....ccccsSc....",
                "...cczyCsSCqq...",
                "..RczyZCCzyqqR..",
                ".RczCCqCzyZqqqR.",
                ".RCyZCCCCCqqqqR.",
                ".bRRRRRRRRRRRRB.",
                ".bbbbBBBBBBBBBd.",
                "..bbBBBBBBBBBd..",
                "...BBBBBBBBBd...",
                "....dBBBBBBd....",
                ".....dddddd.....",
                "................",
            ]),
        new("rich_meal", "meal",
            "Rich meal: bread, a fried egg and greens together on a wooden trencher, fuller than the plain meal plate.",
            "PA77C52 p8A6440 q6E4E31 Q3F2A1A wF0DA9A hE6C77B lD2AE5E eA98A45 g8DB660 G6C9A4B k476B36 WFFF5DF yF2CC5E YD9AE3C",
            [
                "................",
                "................",
                "................",
                ".......whhl.....",
                "..gg..whhhhl....",
                ".gGGg.whhhhle...",
                ".gGkGgwhhhlle...",
                ".kgGkGhhWWWWe...",
                "..kGkWWWyyWWW...",
                ".PPPPWWWYyWWWWp.",
                ".PpppppWWWWWppp.",
                ".qpppppppppppQQ.",
                "..qqqqqqqqqqQQ..",
                "....QQQQQQQQ....",
                "................",
                "................",
            ]),
        new("leather_sack", "sack",
            "Leather sack: the approved sack's shape in tanned leather with a pale stitched seam and a dark tie.",
            "hE0A070 lC98A5A mA9643C d7A4426 tA77C52 T6E4E31 R5E3E26 sF0DA9A",
            [
                "................",
                ".....hll.lm.....",
                "......lmlm......",
                ".....tTTTTR.....",
                "......lllm.R....",
                ".....hllllm.R...",
                "....hlllsllm....",
                "...hllllllmmd...",
                "...hlmllslmmd...",
                "..hllllllmlmmd..",
                "..hllmllslmmmd..",
                "..lllllmllmmmd..",
                "..mlllllsmmmdd..",
                "...mmmmmmmmdd...",
                "................",
                "................",
            ]),
    ];

    public IEnumerable<Entry> Render()
    {
        foreach (var drawing in Drawings)
        {
            yield return new Entry(Family, drawing.Kind, Icon(drawing), drawing.Note);
            yield return new Entry(Family, drawing.Kind + ".was", ItemIcons.Render(drawing.Kind, Grid),
                ItemIcons.Has(drawing.Kind) ? $"Today {drawing.Kind} borrows another item's icon." : $"Today {drawing.Kind} shows the plain crate.");
            yield return new Entry(Family, drawing.Kind + ".beside", ItemIcons.Render(drawing.Replaces, Grid), $"The approved {drawing.Replaces} icon it sits beside.");
        }
    }

    private static Image Icon(Drawing drawing)
    {
        if (drawing.Rows.Length != Grid || drawing.Rows.Any(row => row.Length != Grid))
            throw new ArgumentException($"Icon '{drawing.Kind}' is not {Grid} × {Grid}: {string.Join(",", drawing.Rows.Select(r => r.Length))}.");
        if (drawing.Rows[0].Any(c => c != '.') || drawing.Rows[^1].Any(c => c != '.') || drawing.Rows.Any(r => r[0] != '.' || r[^1] != '.'))
            throw new ArgumentException($"Icon '{drawing.Kind}' touches the outer ring, which the outline needs.");
        return Bitmap.Outlined(Bitmap.Draw(drawing.Palette, drawing.Rows), Ink);
    }
}

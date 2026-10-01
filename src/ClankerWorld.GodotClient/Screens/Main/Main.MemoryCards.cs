using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

/// <summary>
/// The Memories reader as cards: tabs for memories, beliefs and maps, then
/// one card per entry with an icon for its kind, the words themselves first
/// and where they came from underneath. Beliefs show how sure the agent is;
/// map records list their places with a swatch of the ground.
/// </summary>
public partial class Main
{
    private enum MemoryKind { Memory, Belief, Map }

    private readonly record struct MemoryCard(long WorldTick, MemoryKind Kind, Func<Control> Build);

    private readonly SegmentedChoice memoryTabs = new();
    private readonly VBoxContainer memoryCards = new();
    private readonly ScrollContainer memoryScroll = new();
    private readonly List<MemoryCard> memoryCardEntries = [];

    private void BuildMemoryCards(VBoxContainer body)
    {
        body.AddThemeConstantOverride("separation", 8);
        foreach (var (text, id) in new[] { ("All", 9), ("Memories", 0), ("Beliefs", 1), ("Maps", 2) })
            memoryTabs.AddItem(text, id);
        memoryTabs.Select(0);
        memoryTabs.ItemSelected += _ => ShowMemoryCards();
        body.AddChild(memoryTabs);
        memoryScroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        memoryCards.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        memoryCards.AddThemeConstantOverride("separation", 6);
        memoryScroll.AddChild(memoryCards);
        body.AddChild(memoryScroll);
        memoryHistory.Hide();
    }

    private static Color MemoryAccent(MemoryKind kind) => kind switch
    {
        MemoryKind.Memory => UiTheme.Current.Partner,
        MemoryKind.Belief => UiTheme.Current.Name == "dark" ? new Color("E8B04A") : new Color("B77C10"),
        _ => UiTheme.Current.Name == "dark" ? new Color("8DBA6A") : new Color("4A7033"),
    };

    /// <summary>How sure, as five small segments.</summary>
    private static TextureRect Sureness(int basisPoints)
    {
        var lit = Math.Clamp((int)Math.Round(basisPoints / 2000f), 0, 5);
        var image = Image.CreateEmpty(5 * 5 + 1, 8, false, Image.Format.Rgba8);
        image.Fill(UiTheme.Current.ButtonEdge);
        for (var index = 0; index < 5; index++)
            image.FillRect(new Rect2I(1 + index * 5, 1, 4, 6), index < lit ? MemoryAccent(MemoryKind.Belief) : UiTheme.Current.Inset);
        return new TextureRect
        {
            Texture = ImageTexture.CreateFromImage(image),
            StretchMode = TextureRect.StretchModeEnum.KeepCentered,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            TooltipText = $"{basisPoints / 100}% sure",
        };
    }

    /// <summary>One entry: kind icon on the left, the words, then a dim line of where it came from.</summary>
    private static PanelContainer MemoryEntry(MemoryKind kind, PixelGlyph glyph, string words, Control meta, Control? extra = null, bool faded = false)
    {
        var card = new PanelContainer { ThemeTypeVariation = "InsetRow" };
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        row.AddChild(new TextureRect
        {
            Texture = PixelIcons.Texture(glyph, UiTheme.Current.Ink, MemoryAccent(kind), 1),
            StretchMode = TextureRect.StretchModeEnum.KeepCentered,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
        });
        var text = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        text.AddThemeConstantOverride("separation", 2);
        text.AddChild(new Label
        {
            Text = words,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(240, 0),
            ThemeTypeVariation = faded ? "DimLabel" : string.Empty,
        });
        text.AddChild(meta);
        if (extra is not null) text.AddChild(extra);
        row.AddChild(text);
        card.AddChild(row);
        return card;
    }

    private static HBoxContainer MetaLine(params Control[] parts)
    {
        var line = new HBoxContainer();
        line.AddThemeConstantOverride("separation", 6);
        foreach (var part in parts) line.AddChild(part);
        return line;
    }

    private static Label Dim(string text) => new() { Text = text, ThemeTypeVariation = "DimLabel", SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };

    private static Label Tag(string text, bool note = false) => new()
    {
        Text = text.ToUpperInvariant(),
        ThemeTypeVariation = note ? "TagNoteLabel" : "TagLabel",
        SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
    };

    /// <summary>A place on a map record: its ground, where it is, and what can be found there.</summary>
    private HBoxContainer MemorySite(string terrain, int x, int y, IReadOnlyList<string> resources)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        if (terrainMap is { } map && x >= 0 && y >= 0 && x < map.Width && y < map.Height)
            row.AddChild(new TextureRect { Texture = TileSwatch(map, x, y, 10), StretchMode = TextureRect.StretchModeEnum.KeepCentered, TextureFilter = CanvasItem.TextureFilterEnum.Nearest, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
        row.AddChild(new Label { Text = $"{Pretty(terrain)} at {x}, {y}", SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
        foreach (var kind in resources)
        {
            row.AddChild(new TextureRect { Texture = ItemIcons.Texture(kind, 16), StretchMode = TextureRect.StretchModeEnum.KeepCentered, TooltipText = Pretty(kind), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
        }
        return row;
    }

    private void RenderMemoryCards(OwnerWorldSnapshot snapshot, OwnerWorldInhabitant inhabitant)
    {
        memoryCardEntries.Clear();
        foreach (var memory in inhabitant.RecentMemories)
        {
            var item = memory;
            memoryCardEntries.Add(new(item.WorldTick, MemoryKind.Memory, () => MemoryEntry(MemoryKind.Memory, PixelGlyph.Thought, item.Summary,
                MetaLine(Dim($"{DisplayWorldClock(item.WorldTick)} · about {item.SubjectName}"), Tag(Pretty(item.Visibility))))));
        }
        foreach (var belief in inhabitant.RecentBeliefs)
        {
            var item = belief;
            var source = item.Provenance switch
            {
                "firsthand" => "Saw it",
                "hearsay" when item.SourceAgentName is { } name => $"Heard from {name}",
                "hearsay" => "Heard it",
                _ => "Worked it out",
            };
            var subject = item.AboutInhabitantId is { } id ? snapshot.Inhabitants.FirstOrDefault(person => person.Id == id)?.DisplayName : null;
            memoryCardEntries.Add(new(item.WorldTick, MemoryKind.Belief, () =>
            {
                var parts = new List<Control> { Dim($"{DisplayWorldClock(item.WorldTick)} · {source}"), Sureness(item.ConfidenceBasisPoints) };
                if (subject is not null) parts.Add(Dim($"about {subject}"));
                if (item.IsCorrected) parts.Add(Tag("Corrected", note: true));
                return MemoryEntry(MemoryKind.Belief, PixelGlyph.Bulb, item.Statement, MetaLine([.. parts]), faded: item.IsCorrected);
            }));
        }
        foreach (var fact in inhabitant.RecentKnowledgeFacts)
        {
            var item = fact;
            var how = item.Acquisition == "firsthand" ? "Found it" : $"{Pretty(item.Acquisition)} from {item.SourceAgentName ?? "someone"}";
            memoryCardEntries.Add(new(item.WorldTick, MemoryKind.Map, () => MemoryEntry(MemoryKind.Map, PixelGlyph.Map,
                item.ResourceKinds.Count == 0 ? $"{Pretty(item.Terrain)} with nothing to gather" : $"{string.Join(" and ", item.ResourceKinds.Select(kind => Pretty(kind).ToLowerInvariant()))} by the {Pretty(item.Terrain).ToLowerInvariant()}".Capitalized(),
                MetaLine(Dim($"{DisplayWorldClock(item.WorldTick)} · {how}")),
                MemorySite(item.Terrain, item.X, item.Y, item.ResourceKinds))));
        }
        foreach (var artifact in inhabitant.KnowledgeArtifacts)
        {
            var item = artifact;
            memoryCardEntries.Add(new(item.CreatedTick, MemoryKind.Map, () =>
            {
                var sites = new VBoxContainer();
                sites.AddThemeConstantOverride("separation", 2);
                foreach (var site in item.Sites) sites.AddChild(MemorySite(site.Terrain, site.X, site.Y, site.ResourceKinds));
                return MemoryEntry(MemoryKind.Map, PixelGlyph.Scroll, item.Title,
                    MetaLine(Dim($"{DisplayWorldClock(item.CreatedTick)} · {Pretty(item.Kind)} drawn by {item.CreatorName}")), sites);
            }));
        }
        var counts = new[] { memoryCardEntries.Count, memoryCardEntries.Count(entry => entry.Kind == MemoryKind.Memory),
            memoryCardEntries.Count(entry => entry.Kind == MemoryKind.Belief), memoryCardEntries.Count(entry => entry.Kind == MemoryKind.Map) };
        var names = new[] { "All", "Memories", "Beliefs", "Maps" };
        for (var index = 0; index < names.Length; index++)
            memoryTabs.SetItemText(index, counts[index] == 0 || index == 0 ? names[index] : $"{names[index]} {counts[index]}");
        ShowMemoryCards();
    }

    private void ShowMemoryCards()
    {
        foreach (var child in memoryCards.GetChildren())
        {
            memoryCards.RemoveChild(child);
            child.QueueFree();
        }
        var filter = memoryTabs.GetSelectedId();
        var shown = memoryCardEntries.Where(entry => filter is < 0 or 9 || (int)entry.Kind == filter)
            .OrderByDescending(entry => entry.WorldTick).ThenBy(entry => entry.Kind).ToArray();
        if (shown.Length == 0)
            memoryCards.AddChild(new Label
            {
                Text = memoryCardEntries.Count == 0 ? "Nothing remembered yet. Memories, beliefs and maps appear here as they learn." : "Nothing of this kind yet.",
                ThemeTypeVariation = "DimLabel",
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                CustomMinimumSize = new Vector2(300, 0),
            });
        foreach (var entry in shown) memoryCards.AddChild(entry.Build());
        memoryScroll.CustomMinimumSize = new Vector2(0, Math.Min(UiSize.Y - HudTop - 140, Math.Max(40, shown.Length * 64)));
        memoriesPanel.ResetSize();
    }
}

internal static class TextExtensions
{
    public static string Capitalized(this string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}

/// <summary>The Profile's People section as rows with an icon for each kind of tie.</summary>
public partial class Main
{
    private readonly HFlowContainer profilePeople = new();

    private void RenderProfilePeople(OwnerWorldSnapshot snapshot, OwnerWorldInhabitant inhabitant)
    {
        foreach (var child in profilePeople.GetChildren())
        {
            profilePeople.RemoveChild(child);
            child.QueueFree();
        }
        var ink = UiTheme.Current.Ink;
        void Row(Texture2D icon, string relation, string name, Control? extra = null)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 6);
            row.AddChild(new TextureRect { Texture = icon, StretchMode = TextureRect.StretchModeEnum.KeepCentered, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
            row.AddChild(new Label { Text = relation, ThemeTypeVariation = "DimLabel", SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
            row.AddChild(new Label { Text = name, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
            if (extra is not null) row.AddChild(extra);
            row.AddChild(new Control { CustomMinimumSize = new Vector2(6, 0) });
            profilePeople.AddChild(row);
        }
        foreach (var link in inhabitant.Relationships)
        {
            var name = GameUiText.PartyName(snapshot, link.OtherPartyId);
            var ended = link.State is "ended" or "ended_by_death";
            switch (link.Type)
            {
                case "partnership":
                    Row(PixelIcons.Texture(PixelGlyph.Heart, UiTheme.Current.Partner, UiTheme.Current.Partner, 1), ended ? "Former partner" : "Partner", name);
                    break;
                case "biological_parentage":
                    Row(PixelIcons.Texture(PixelGlyph.Person, ink, ink, 1), link.Direction == "parent" ? "Parent of" : "Child of", name);
                    break;
                case "household_membership":
                    Row(PixelIcons.Texture(PixelGlyph.House, ink, AccentWood, 1), "Lives with", name);
                    break;
                default:
                    Row(PixelIcons.Texture(PixelGlyph.Person, ink, ink, 1), Pretty(link.Type), name);
                    break;
            }
        }
        foreach (var standing in inhabitant.SocialStanding)
            Row(PixelIcons.Texture(PixelGlyph.Person, ink, AccentGreen, 1), "Trusts", standing.SubjectName, Sureness(standing.Trust * 1000));
        foreach (var note in inhabitant.SocialNotes)
            profilePeople.AddChild(new Label { Text = note, ThemeTypeVariation = "DimLabel" });
        if (profilePeople.GetChildCount() == 0)
            profilePeople.AddChild(new Label { Text = "No close relationships yet.", ThemeTypeVariation = "DimLabel" });
    }
}

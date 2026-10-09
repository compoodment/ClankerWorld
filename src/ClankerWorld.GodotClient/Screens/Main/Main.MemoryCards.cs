using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

/// <summary>
/// The Memories reader as cards: tabs for memories, beliefs and maps, then
/// one card per entry with an icon for its kind, the words themselves first
/// and when and how they learned it underneath. Beliefs show how sure the
/// agent is; map records list their places with a swatch of the ground.
/// </summary>
public partial class Main
{
    private enum MemoryKind { Memory, Belief, Map }

    /// <summary>One entry, built only when its tab is showing.</summary>
    private readonly record struct MemoryCard(long WorldTick, MemoryKind Kind, Func<Control> Build);

    private const int AllMemoriesTab = (int)MemoryKind.Map + 1;
    private static readonly (string Name, int Id)[] MemoryTabs =
        [("All", AllMemoriesTab), ("Memories", (int)MemoryKind.Memory), ("Beliefs", (int)MemoryKind.Belief), ("Maps", (int)MemoryKind.Map)];

    private readonly SegmentedChoice memoryTabs = new();
    private readonly VBoxContainer memoryCards = new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
    private readonly ScrollContainer memoryScroll = new() { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
    private readonly MarginContainer memoryGap = new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
    private readonly List<MemoryCard> memoryCardEntries = [];
    private string? renderedMemoryCards;

    private void BuildMemoryCards(VBoxContainer body)
    {
        body.AddThemeConstantOverride("separation", 8);
        foreach (var (name, id) in MemoryTabs) memoryTabs.AddItem(name, id);
        memoryTabs.Select(0);
        memoryTabs.ItemSelected += _ => ShowMemoryCards();
        body.AddChild(memoryTabs);
        memoryCards.AddThemeConstantOverride("separation", 6);
        memoryGap.AddChild(memoryCards);
        memoryScroll.AddChild(memoryGap);
        memoryScroll.TooltipText = "What this agent remembers and believes, plus the maps they know. This is their view, not the full world log.";
        body.AddChild(memoryScroll);
    }

    private static Color MemoryAccent(MemoryKind kind) => kind switch
    {
        MemoryKind.Memory => UiTheme.Current.Partner,
        MemoryKind.Belief => UiTheme.Current.Name == "dark" ? new Color("E8B04A") : new Color("B77C10"),
        _ => UiTheme.Current.Primary,
    };

    /// <summary>How sure, as five small segments; hovering gives the percentage.</summary>
    private static TextureRect Sureness(int basisPoints, string tooltip)
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
            TooltipText = tooltip,
        };
    }

    /// <summary>One entry: its kind's icon on the left, the words, then a dim line of when and how.</summary>
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
            Text = GameUiText.PlainEllipses(words),
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

    private static HFlowContainer MetaLine(params Control[] parts)
    {
        var line = new HFlowContainer();
        line.AddThemeConstantOverride("h_separation", 6);
        line.AddThemeConstantOverride("v_separation", 2);
        foreach (var part in parts) line.AddChild(part);
        return line;
    }

    private static Label MetaText(string text) => new() { Text = GameUiText.PlainEllipses(text), ThemeTypeVariation = "DimLabel", SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };

    private static Label Tag(string text, bool note = false) => new()
    {
        Text = text.ToUpperInvariant(),
        ThemeTypeVariation = note ? "TagNoteLabel" : "TagLabel",
        SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
    };

    /// <summary>A place on a map record: its ground, where it is, and what can be gathered there.</summary>
    private HBoxContainer MemorySite(string terrain, int x, int y, IReadOnlyList<string> resources)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        if (terrainMap is { } map && x >= 0 && y >= 0 && x < map.Width && y < map.Height)
            row.AddChild(new TextureRect { Texture = TileSwatch(map, x, y, 10), StretchMode = TextureRect.StretchModeEnum.KeepCentered, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
        row.AddChild(new Label { Text = $"{Pretty(terrain)} at {x}, {y}", SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
        foreach (var kind in resources)
            row.AddChild(new TextureRect { Texture = ItemIcons.Texture(kind, 16), StretchMode = TextureRect.StretchModeEnum.KeepCentered, TooltipText = Pretty(kind), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
        return row;
    }

    /// <summary>What this agent remembers, believes and has mapped, newest first, for the Memories panel.</summary>
    private void RenderMemoryCards(OwnerWorldSnapshot snapshot, OwnerWorldInhabitant inhabitant)
    {
        // A belief names its subject by looking them up, so a rename must redraw it too.
        // The names are copied out so the cards do not keep the whole snapshot alive.
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var person in snapshot.Inhabitants) names.TryAdd(person.Id, person.DisplayName);
        string? SubjectName(string? id) => id is not null && names.TryGetValue(id, out var name) ? name : null;
        var signature = string.Join("|", inhabitant.Id, UiTheme.Current.Name, displayPreferences.DateStyle, observedCalendarPace, displayPreferences.UseTwelveHourClock,
            string.Join(",", inhabitant.RecentMemories.Select(item => $"{item.WorldTick}:{item.Summary}:{item.SubjectName}:{item.Visibility}")),
            string.Join(",", inhabitant.RecentBeliefs.Select(item => $"{item.WorldTick}:{item.Statement}:{item.Provenance}:{item.ConfidenceBasisPoints}:{item.SourceAgentName}:{item.AboutInhabitantId}:{SubjectName(item.AboutInhabitantId)}:{item.IsCorrected}:{item.CorrectedTick}")),
            string.Join(",", inhabitant.RecentKnowledgeFacts.Select(item => $"{item.WorldTick}:{item.X}:{item.Y}:{item.Terrain}:{string.Join('+', item.ResourceKinds)}:{item.Acquisition}:{item.SourceAgentName}:{item.DiscovererName}")),
            string.Join(",", inhabitant.KnownRecipes.Select(item => $"{item.WorldTick}:{item.Name}:{item.Acquisition}:{item.SourceAgentName}")),
            string.Join(",", inhabitant.KnowledgeArtifacts.Select(item => $"{item.Id}:{item.Title}:{item.CreatorName}:{item.Sites.Count}:{string.Join('+', item.RecipeNames)}")));
        if (signature == renderedMemoryCards) return;
        renderedMemoryCards = signature;
        memoryCardEntries.Clear();
        foreach (var memory in inhabitant.RecentMemories)
        {
            var item = memory;
            memoryCardEntries.Add(new(item.WorldTick, MemoryKind.Memory, () =>
            {
                var meta = new List<Control> { MetaText($"{DisplayWorldClock(item.WorldTick)} · about {item.SubjectName}") };
                if (item.Visibility != "public") meta.Add(Tag(Pretty(item.Visibility)));
                return MemoryEntry(MemoryKind.Memory, PixelGlyph.Thought, item.Summary, MetaLine([.. meta]));
            }));
        }
        foreach (var belief in inhabitant.RecentBeliefs)
        {
            var item = belief;
            var source = item.Provenance switch
            {
                "firsthand" => "Saw it",
                "hearsay" when item.SourceAgentName is { } name => $"Heard from {name}",
                "hearsay" => "Heard it from someone",
                _ => "Worked it out",
            };
            var subject = SubjectName(item.AboutInhabitantId);
            memoryCardEntries.Add(new(item.WorldTick, MemoryKind.Belief, () =>
            {
                var meta = new List<Control>
                {
                    MetaText($"{DisplayWorldClock(item.WorldTick)} · {source}"),
                    Sureness(item.ConfidenceBasisPoints, $"{item.ConfidenceBasisPoints / 100}% sure"),
                };
                if (subject is not null) meta.Add(MetaText($"about {subject}"));
                if (item.IsCorrected)
                {
                    meta.Add(Tag("Corrected", note: true));
                    if (item.CorrectedTick is { } corrected) meta.Add(MetaText(DisplayWorldClock(corrected)));
                }
                return MemoryEntry(MemoryKind.Belief, PixelGlyph.Bulb, item.Statement, MetaLine([.. meta]), faded: item.IsCorrected);
            }));
        }
        foreach (var fact in inhabitant.RecentKnowledgeFacts)
        {
            var item = fact;
            var how = item.Acquisition == "firsthand"
                ? $"Found by {item.DiscovererName}"
                : $"{Pretty(item.Acquisition)} from {item.SourceAgentName ?? "another agent"} · found by {item.DiscovererName}";
            var gathered = string.Join(" and ", item.ResourceKinds.Select(kind => Pretty(kind).ToLowerInvariant()));
            var title = item.ResourceKinds.Count == 0
                ? $"{Pretty(item.Terrain)} with nothing to gather"
                : $"{char.ToUpperInvariant(gathered[0])}{gathered[1..]} near the {Pretty(item.Terrain).ToLowerInvariant()}";
            memoryCardEntries.Add(new(item.WorldTick, MemoryKind.Map, () => MemoryEntry(MemoryKind.Map, PixelGlyph.Map, title,
                MetaLine(MetaText($"{DisplayWorldClock(item.WorldTick)} · {how}")),
                MemorySite(item.Terrain, item.X, item.Y, item.ResourceKinds))));
        }
        foreach (var recipe in inhabitant.KnownRecipes)
        {
            var item = recipe;
            var how = item.Acquisition == "practice" ? "Learned through practice" :
                $"{Pretty(item.Acquisition)} from {item.SourceAgentName ?? "another agent"}";
            memoryCardEntries.Add(new(item.WorldTick, MemoryKind.Memory, () => MemoryEntry(MemoryKind.Memory, PixelGlyph.Scroll,
                $"Recipe: {item.Name}", MetaLine(MetaText($"{DisplayWorldClock(item.WorldTick)} · {how}")))));
        }
        foreach (var artifact in inhabitant.KnowledgeArtifacts)
        {
            var item = artifact;
            memoryCardEntries.Add(new(item.CreatedTick, MemoryKind.Map, () =>
            {
                var sites = new VBoxContainer();
                sites.AddThemeConstantOverride("separation", 2);
                foreach (var site in item.Sites) sites.AddChild(MemorySite(site.Terrain, site.X, site.Y, site.ResourceKinds));
                foreach (var recipe in item.RecipeNames) sites.AddChild(new Label { Text = "Recipe: " + recipe });
                var creation = item.Kind == "field_map" ? "drawn" : "written";
                return MemoryEntry(MemoryKind.Map, PixelGlyph.Scroll, item.Title,
                    MetaLine(MetaText($"{DisplayWorldClock(item.CreatedTick)} · {GameUiText.ItemName(item.Kind)} {creation} by {item.CreatorName}")), sites);
            }));
        }
        foreach (var (name, id) in MemoryTabs)
        {
            var count = memoryCardEntries.Count(entry => id == AllMemoriesTab || (int)entry.Kind == id);
            memoryTabs.SetItemText(memoryTabs.GetItemIndex(id), id == AllMemoriesTab || count == 0 ? name : $"{name} {count}");
        }
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
        var shown = memoryCardEntries.Where(entry => filter == AllMemoriesTab || (int)entry.Kind == filter)
            .OrderByDescending(entry => entry.WorldTick).ThenBy(entry => entry.Kind).ToArray();
        if (shown.Length == 0)
            memoryCards.AddChild(new Label
            {
                Text = memoryCardEntries.Count == 0
                    ? "No saved memories, beliefs, or map records for this agent yet."
                    : $"No {MemoryTabs.First(tab => tab.Id == filter).Name.ToLowerInvariant()} yet.",
                ThemeTypeVariation = "DimLabel",
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                CustomMinimumSize = new Vector2(300, 0),
            });
        foreach (var entry in shown) memoryCards.AddChild(entry.Build());
        FitMemoryCards();
        // Wrapped lines report their height only once they have a width, so fit again next frame.
        Callable.From(FitMemoryCards).CallDeferred();
    }

    /// <summary>The cards take the room they need, up to the bottom of the screen, and scroll beyond it.</summary>
    private void FitMemoryCards()
    {
        var content = memoryCards.GetCombinedMinimumSize().Y;
        var rest = memoriesPanel.GetCombinedMinimumSize().Y - memoryScroll.CustomMinimumSize.Y;
        var room = Math.Max(80, UiSize.Y - HudTop - 14 - rest);
        var scrolls = content > room;
        memoryGap.AddThemeConstantOverride("margin_right", scrolls ? SettingsScrollGap : 0);
        memoryScroll.CustomMinimumSize = new Vector2(0, scrolls ? room : content);
        memoriesPanel.ResetSize();
    }

    /// <summary>Every label and tooltip in the Memories cards, for checks and assistive reading.</summary>
    private string MemoryCardsText() => string.Join("\n",
        memoryCards.FindChildren("*", "", recursive: true, owned: false).OfType<Control>()
            .SelectMany(control => new[] { (control as Label)?.Text, control.TooltipText })
            .Where(text => !string.IsNullOrEmpty(text)));
}

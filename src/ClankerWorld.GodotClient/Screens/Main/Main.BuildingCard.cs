using ClankerWorld.GodotClient.UI;
using Godot;
using System.Globalization;

namespace ClankerWorld.GodotClient;

/// <summary>
/// The selected building in two steps, like an agent: a quick card beside it
/// on the map with who owns it, what is happening there and what it stores,
/// and a Details panel docked on the left with everything recorded about it.
/// Both show only facts the host reports; nothing is guessed.
/// </summary>
public partial class Main
{
    private const int BuildingCardWidth = 288;
    private const int BuildingDetailsWidth = 300;
    private const int SlotColumns = 5;

    private readonly PanelContainer buildingQuickCard = new();
    private readonly PanelContainer buildingDetailsPanel = new();
    private readonly BuildingHeader buildingQuickHeader = new() { RoofSize = 36 };
    private readonly BuildingHeader buildingDetailsHeader = new() { RoofSize = 48 };
    private readonly ItemStorage buildingQuickStorage = new() { Title = "STORED", Columns = SlotColumns };
    private readonly ItemStorage buildingDetailsStorage = new() { Title = "STORAGE", Columns = SlotColumns, Named = true };
    private readonly Button buildingDetailsButton = new();
    private readonly VBoxContainer buildingQuickStatus = new();
    private readonly GridContainer buildingFacts = new() { Columns = 2 };
    private readonly ScrollContainer buildingDetailsScroll = new();
    private readonly VBoxContainer buildingDetailsContent = new();
    private readonly VBoxContainer buildingWorkSection = new();
    private readonly VBoxContainer buildingWorkRows = new();
    private readonly VBoxContainer buildingPeopleSection = new();
    private readonly Label buildingPeopleSummary = new() { ThemeTypeVariation = "DimLabel" };
    private readonly Label buildingPeopleText = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private string? selectedBuildingId;
    private bool buildingDetailsRequested;
    private OwnerWorldSnapshot? buildingCardSnapshot;
    private string? renderedBuildingStatus;
    private string? renderedBuildingWork;

    private void BuildBuildingCards()
    {
        var quick = new VBoxContainer();
        quick.AddThemeConstantOverride("separation", 7);
        quick.AddChild(buildingQuickHeader);
        buildingQuickStatus.AddThemeConstantOverride("separation", 4);
        quick.AddChild(buildingQuickStatus);
        quick.AddChild(buildingQuickStorage);
        buildingDetailsButton.Text = "Details  ›";
        buildingDetailsButton.TooltipText = "Open everything recorded about this building.";
        StyleButton(buildingDetailsButton);
        buildingDetailsButton.Pressed += OpenBuildingDetails;
        quick.AddChild(buildingDetailsButton);
        AddPanelContents(buildingQuickCard, quick);
        buildingQuickCard.ThemeTypeVariation = "HudPanel";
        buildingQuickCard.CustomMinimumSize = new Vector2(BuildingCardWidth, 0);
        buildingQuickCard.ZIndex = 70;
        buildingQuickCard.Hide();
        // Wrapped text settles its height only after layout, so place the
        // card again once its size is known.
        buildingQuickCard.MinimumSizeChanged += () => Callable.From(() =>
        {
            if (buildingCardSnapshot is { } snapshot) PositionBuildingQuickCard(snapshot);
        }).CallDeferred();

        var details = new VBoxContainer();
        details.AddThemeConstantOverride("separation", 8);
        details.AddChild(buildingDetailsHeader);
        buildingDetailsContent.AddThemeConstantOverride("separation", 8);
        buildingDetailsContent.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        buildingFacts.AddThemeConstantOverride("h_separation", 12);
        buildingFacts.AddThemeConstantOverride("v_separation", 3);
        buildingDetailsContent.AddChild(buildingFacts);
        buildingWorkSection.AddThemeConstantOverride("separation", 4);
        buildingWorkSection.AddChild(new Label { Text = "WORKING", ThemeTypeVariation = "SectionLabel" });
        buildingWorkRows.AddThemeConstantOverride("separation", 6);
        buildingWorkSection.AddChild(buildingWorkRows);
        buildingDetailsContent.AddChild(buildingWorkSection);
        buildingDetailsContent.AddChild(buildingDetailsStorage);
        buildingPeopleSection.AddThemeConstantOverride("separation", 4);
        buildingPeopleSection.AddChild(SectionRow("PEOPLE", buildingPeopleSummary));
        buildingPeopleSection.AddChild(buildingPeopleText);
        buildingDetailsContent.AddChild(buildingPeopleSection);
        // On a short view the facts scroll under a header that stays put.
        buildingDetailsScroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        buildingDetailsScroll.AddChild(buildingDetailsContent);
        details.AddChild(buildingDetailsScroll);
        AddPanelContents(buildingDetailsPanel, details);
        buildingDetailsPanel.CustomMinimumSize = new Vector2(BuildingDetailsWidth, 0);
        buildingDetailsPanel.ZIndex = 75;
        buildingDetailsPanel.Hide();
        buildingDetailsContent.MinimumSizeChanged += () => Callable.From(PositionBuildingDetails).CallDeferred();

        foreach (var header in new[] { buildingQuickHeader, buildingDetailsHeader })
        {
            StyleIconButton(header.Find, PixelGlyph.Find);
            header.Find.TooltipText = "Center the map on this building";
            header.Find.Pressed += CenterOnSelectedBuilding;
        }
        StyleIconButton(buildingQuickHeader.Close, PixelGlyph.Close);
        buildingQuickHeader.Close.TooltipText = "Close (Esc)";
        buildingQuickHeader.Close.Pressed += ClearBuildingSelection;
        StyleIconButton(buildingDetailsHeader.Close, PixelGlyph.Back);
        buildingDetailsHeader.Close.TooltipText = "Back (Esc)";
        buildingDetailsHeader.Close.Pressed += BuildingDetailsBack;
        uiLayer.AddChild(buildingQuickCard);
        uiLayer.AddChild(buildingDetailsPanel);
    }

    /// <summary>A section heading with a short summary on the right, such as "STORAGE · 3 kinds · 12 items".</summary>
    private static HBoxContainer SectionRow(string title, Label summary)
    {
        var row = new HBoxContainer();
        row.AddChild(new Label { Text = title, ThemeTypeVariation = "SectionLabel", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        row.AddChild(summary);
        return row;
    }

    private static OwnerWorldPlacedBuilding? BuildingAt(OwnerWorldSnapshot snapshot, Vector2I tile) =>
        snapshot.PlacedBuildings.FirstOrDefault(building =>
            tile.X >= building.Position.X && tile.X < building.Position.X + Math.Max(1, building.Width) &&
            tile.Y >= building.Position.Y && tile.Y < building.Position.Y + Math.Max(1, building.Height));

    private OwnerWorldPlacedBuilding? SelectedBuilding() =>
        buildingCardSnapshot?.PlacedBuildings.FirstOrDefault(item =>
            string.Equals(item.InstanceId, selectedBuildingId, StringComparison.Ordinal));

    private static Rect2I Footprint(OwnerWorldPlacedBuilding building) =>
        new(building.Position.X, building.Position.Y, Math.Max(1, building.Width), Math.Max(1, building.Height));

    /// <summary>Selecting a building closes an agent's card and the tile card; selecting it again closes its own.</summary>
    private void SelectBuilding(string instanceId)
    {
        if (string.Equals(instanceId, selectedBuildingId, StringComparison.Ordinal))
        {
            ClearBuildingSelection();
            return;
        }
        if (selectedInhabitantId is not null) ClearInhabitantSelection();
        ClearTileSelection();
        selectedBuildingId = instanceId;
        buildingDetailsRequested = false;
        if (renderedMapSnapshot is { } snapshot) RenderBuildingCard(snapshot);
    }

    private void ClearBuildingSelection()
    {
        selectedBuildingId = null;
        buildingDetailsRequested = false;
        terrainLayer.SetSelectedBuilding(null);
        buildingQuickCard.Hide();
        buildingDetailsPanel.Hide();
    }

    private void OpenBuildingDetails()
    {
        buildingDetailsRequested = true;
        if (buildingCardSnapshot is { } snapshot) RenderBuildingCard(snapshot);
    }

    /// <summary>Details steps back to the quick card beside the building.</summary>
    private void BuildingDetailsBack()
    {
        buildingDetailsRequested = false;
        if (buildingCardSnapshot is { } snapshot) RenderBuildingCard(snapshot);
    }

    private void CenterOnSelectedBuilding()
    {
        if (SelectedBuilding() is not { } building) return;
        CenterCameraAt(new Vector2(building.Position.X + Math.Max(1, building.Width) / 2f,
            building.Position.Y + Math.Max(1, building.Height) / 2f));
    }

    private void RenderBuildingCard(OwnerWorldSnapshot snapshot)
    {
        buildingCardSnapshot = snapshot;
        if (SelectedBuilding() is not { } building)
        {
            if (selectedBuildingId is not null) ClearBuildingSelection();
            return;
        }

        var footprint = Footprint(building);
        terrainLayer.SetSelectedBuilding(footprint);
        var name = building.DisplayName ?? Pretty(building.DefinitionId[(building.DefinitionId.LastIndexOf('/') + 1)..]);
        var household = building.HouseholdId is { } householdId ? GameUiText.PartyName(snapshot, householdId) : null;
        var town = snapshot.Towns.FirstOrDefault(item => item.Id == building.TownId)?.Name;
        var owner = string.Join(" · ", new[] { household, town }.Where(part => part is not null));
        var inside = snapshot.Inhabitants
            .Where(person => !person.IsDraft && IsLiving(person) &&
                footprint.HasPoint(new Vector2I(person.Position.X, person.Position.Y)))
            .Select(person => person.DisplayName).Order(StringComparer.CurrentCulture).ToArray();
        var jobs = snapshot.ProductionJobs
            .Where(job => job.BuildingInstanceId == building.InstanceId &&
                string.Equals(job.State, "running", StringComparison.OrdinalIgnoreCase))
            .OrderBy(job => job.StartedTick).ThenBy(job => job.JobId, StringComparer.Ordinal).ToArray();
        var stored = building.StoredItems?
            .Where(item => item.Quantity > 0)
            .OrderByDescending(item => item.Quantity).ThenBy(item => item.Kind, StringComparer.Ordinal).ToArray();

        var kind = BuildingSprites.KindFor(building.Tags);
        var door = BuildingDoor.Facing(footprint, building.Entrance is { } entrance
            ? new Vector2I(entrance.X, entrance.Y) : null);
        foreach (var header in new[] { buildingQuickHeader, buildingDetailsHeader })
        {
            header.NameLabel.Text = name;
            header.NameLabel.TooltipText = name;
            header.OwnerLabel.Text = owner.Length == 0 ? "No owner recorded" : owner;
            header.SetRoof(kind, footprint.Size, door);
        }
        // A building that keeps no stores has no storage section, rather than an empty one.
        foreach (var storage in new[] { buildingQuickStorage, buildingDetailsStorage })
        {
            storage.Visible = stored is not null;
            if (stored is not null)
                storage.SetItems(stored.Select(item => (item.Kind, item.Quantity, Pretty(item.Kind))).ToArray());
        }
        RenderBuildingStatus(snapshot, jobs, inside);
        RenderBuildingDetails(snapshot, building, household, town, jobs, inside);

        buildingDetailsPanel.Visible = buildingDetailsRequested;
        buildingQuickCard.Visible = !buildingDetailsRequested;
        PositionBuildingQuickCard(snapshot);
        PositionBuildingDetails();
    }

    /// <summary>The quick card's one line on what is happening: work in progress, or who is inside.</summary>
    private void RenderBuildingStatus(OwnerWorldSnapshot snapshot, OwnerWorldProductionJob[] jobs, string[] inside)
    {
        var signature = jobs.Length > 0
            ? string.Join('|', JobSummary(snapshot, jobs[0])) + (jobs.Length > 1 ? "+" + (jobs.Length - 1) : "")
            : string.Join('|', inside);
        if (renderedBuildingStatus == signature) return;
        renderedBuildingStatus = signature;
        ClearChildren(buildingQuickStatus);
        if (jobs.Length > 0)
        {
            buildingQuickStatus.AddChild(JobRow(snapshot, jobs[0]));
            if (jobs.Length > 1)
                buildingQuickStatus.AddChild(new Label
                {
                    Text = $"and {Plural(jobs.Length - 1, "more job")} · see Details",
                    ThemeTypeVariation = "DimLabel",
                });
            return;
        }
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        row.AddChild(new TextureRect
        {
            Texture = PixelIcons.Themed(PixelGlyph.Person, UiTheme.Current.InkMuted, 1),
            StretchMode = TextureRect.StretchModeEnum.KeepCentered,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        });
        row.AddChild(new Label
        {
            Text = PeopleInside(inside),
            ThemeTypeVariation = inside.Length == 0 ? "DimLabel" : string.Empty,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        });
        buildingQuickStatus.AddChild(row);
    }

    private void RenderBuildingDetails(OwnerWorldSnapshot snapshot, OwnerWorldPlacedBuilding building,
        string? household, string? town, OwnerWorldProductionJob[] jobs, string[] inside)
    {
        var usedBy = household ?? (town is not null && building.Tags?.Contains("warehouse") == true
            ? $"{town} residents" : "Any agent");
        var facts = new List<(string Key, string Value)>
        {
            ("Owner", household ?? town ?? "Nobody"),
            ("Used by", usedBy),
            ("Built", SplitClock(DisplayWorldClock(building.PlacedTick)).Date),
        };
        // Only an entrance beside the footprint names a side; the fallback door is not a fact.
        if (building.Entrance is { } entrance &&
            BuildingDoor.Facing(Footprint(building), new Vector2I(entrance.X, entrance.Y)) is { Tile: not null } door)
            facts.Add(("Door", $"{door.Side} side"));
        RenderBuildingFacts(facts);

        buildingWorkSection.Visible = jobs.Length > 0;
        var work = string.Join('\n', jobs.Select(job => string.Join('|', JobSummary(snapshot, job))));
        if (renderedBuildingWork != work)
        {
            renderedBuildingWork = work;
            ClearChildren(buildingWorkRows);
            foreach (var job in jobs) buildingWorkRows.AddChild(JobRow(snapshot, job));
        }

        var residents = building.HouseholdId is { } householdId && building.Tags?.Contains("house") == true
            ? snapshot.Inhabitants
                .Where(person => !person.IsDraft && IsLiving(person) && person.Relationships.Any(link =>
                    link.Type == "household_membership" && link.OtherPartyId == householdId &&
                    string.Equals(link.State, "accepted", StringComparison.Ordinal)))
                .Select(person => person.DisplayName).Order(StringComparer.CurrentCulture).ToArray()
            : [];
        buildingPeopleSummary.Text = inside.Length == 0 ? "Nobody inside" : $"{inside.Length} inside";
        var people = new List<string>();
        if (inside.Length > 0) people.Add("Inside: " + string.Join(", ", inside));
        if (residents.Length > 0) people.Add($"Home of {household}: {string.Join(", ", residents)}");
        buildingPeopleText.Text = string.Join('\n', people);
        buildingPeopleText.Visible = people.Count > 0;
    }

    private void RenderBuildingFacts(List<(string Key, string Value)> facts)
    {
        var labels = buildingFacts.GetChildren().OfType<Label>().ToArray();
        if (labels.Length != facts.Count * 2)
        {
            ClearChildren(buildingFacts);
            labels = facts.SelectMany(_ => new[]
            {
                new Label { ThemeTypeVariation = "DimLabel" },
                new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill },
            }).ToArray();
            foreach (var label in labels) buildingFacts.AddChild(label);
        }
        for (var index = 0; index < facts.Count; index++)
        {
            labels[index * 2].Text = facts[index].Key;
            labels[index * 2 + 1].Text = facts[index].Value;
        }
    }

    /// <summary>One job: what is being made, how far along, who is working and how long is left.</summary>
    private HBoxContainer JobRow(OwnerWorldSnapshot snapshot, OwnerWorldProductionJob job)
    {
        var (what, percent, detail) = JobSummary(snapshot, job);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        row.AddChild(new TextureRect
        {
            Texture = ItemIcons.Texture(RecipeIcon(job.RecipeId), 32),
            StretchMode = TextureRect.StretchModeEnum.KeepCentered,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
        });
        var lines = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        lines.AddThemeConstantOverride("separation", 2);
        lines.AddChild(new Label { Text = what, AutowrapMode = TextServer.AutowrapMode.WordSmart });
        var progress = new HBoxContainer();
        progress.AddThemeConstantOverride("separation", 6);
        progress.AddChild(new PixelMeter { Kind = MeterKind.Progress, CaptionWidth = 0, Percent = percent, TooltipText = $"{percent}% done" });
        progress.AddChild(new Label { Text = $"{percent}%", ThemeTypeVariation = "DimLabel" });
        lines.AddChild(progress);
        lines.AddChild(new Label { Text = detail, ThemeTypeVariation = "DimLabel", AutowrapMode = TextServer.AutowrapMode.WordSmart });
        row.AddChild(lines);
        return row;
    }

    private (string What, int Percent, string Detail) JobSummary(OwnerWorldSnapshot snapshot, OwnerWorldProductionJob job)
    {
        var worker = snapshot.Inhabitants.FirstOrDefault(person => person.Id == job.WorkerId)?.DisplayName ?? "Someone";
        var length = Math.Max(1, job.CompletionTick - job.StartedTick);
        var percent = (int)Math.Clamp((snapshot.WorldTick - job.StartedTick) * 100 / length, 0, 100);
        return (RecipeName(job.RecipeId), percent, $"{worker} · {TimeLeft(job.CompletionTick - snapshot.WorldTick)}");
    }

    /// <summary>A recipe's own words, such as "Mill grain" for <c>…/mill-grain</c>.</summary>
    private static string RecipeName(string recipeId) =>
        Sentence(recipeId[(recipeId.LastIndexOf('/') + 1)..].Replace('-', ' ').Replace('_', ' '));

    /// <summary>The icon of the item a recipe is named after, such as grain for milling; otherwise a hammer.</summary>
    private static string RecipeIcon(string recipeId)
    {
        var local = recipeId[(recipeId.LastIndexOf('/') + 1)..].Replace('-', '_');
        return ItemIcons.Has(local) ? local : local.Split('_').FirstOrDefault(ItemIcons.Has) ?? "tool";
    }

    /// <summary>Time left in world hours or days, at the world's own calendar pace.</summary>
    private string TimeLeft(long ticks)
    {
        var ticksPerDay = Math.Max(1, observedCalendarPace?.TicksPerDay ?? 1440);
        var hours = Math.Max(0, ticks) * 24.0 / ticksPerDay;
        return hours < 1 ? "less than an hour left"
            : hours < 36 ? $"about {Plural((int)Math.Round(hours), "hour")} left"
            : $"about {Plural((int)Math.Round(hours / 24), "day")} left";
    }

    private static string PeopleInside(string[] names) => names.Length switch
    {
        0 => "Nobody inside",
        1 => $"{names[0]} is inside",
        2 => $"{names[0]} and {names[1]} are inside",
        _ => $"{names[0]}, {names[1]} and {Plural(names.Length - 2, "other")} are inside",
    };

    private static string Plural(int amount, string noun) =>
        $"{amount.ToString(CultureInfo.CurrentCulture)} {noun}{(amount == 1 ? "" : "s")}";

    /// <summary>Removes rows at once, so the card shrinks this frame rather than the next.</summary>
    private static void ClearChildren(Node parent)
    {
        foreach (var child in parent.GetChildren())
        {
            parent.RemoveChild(child);
            child.QueueFree();
        }
    }

    /// <summary>The quick card sits beside its building, like an agent's card.</summary>
    private void PositionBuildingQuickCard(OwnerWorldSnapshot snapshot)
    {
        if (!buildingQuickCard.Visible || UiSize.X <= 0 || UiSize.Y <= 0 || terrainMap is null ||
            snapshot.PlacedBuildings.FirstOrDefault(item => item.InstanceId == selectedBuildingId) is not { } building)
            return;
        var footprint = Footprint(building);
        var stride = currentTileSize + TileGap;
        var left = WrappedMarkerX(footprint.Position.X * stride, terrainMap.Width, stride, snapshot.WrapsEastWest);
        var target = new Rect2(
            (mapStage.Position + new Vector2(left, footprint.Position.Y * stride)) / uiLayer.Factor,
            new Vector2(footprint.Size.X * stride - TileGap, footprint.Size.Y * stride - TileGap) / uiLayer.Factor);
        buildingQuickCard.CustomMinimumSize = new Vector2(Math.Min(BuildingCardWidth, Math.Max(1, UiSize.X - 24)), 0);
        PlaceQuickCard(buildingQuickCard, target);
    }

    /// <summary>Details docks on the left like a Profile, and scrolls when the view is too short for it.</summary>
    private void PositionBuildingDetails()
    {
        if (!buildingDetailsPanel.Visible) return;
        buildingDetailsPanel.CustomMinimumSize = new Vector2(Math.Min(BuildingDetailsWidth, Math.Max(1, UiSize.X - 28)), 0);
        var header = buildingDetailsHeader.GetCombinedMinimumSize().Y;
        // Panel margins and the gap under the header.
        var room = UiSize.Y - HudTop - 14 - header - 28;
        buildingDetailsScroll.CustomMinimumSize = new Vector2(0,
            Math.Max(60, Math.Min(buildingDetailsContent.GetCombinedMinimumSize().Y, room)));
        buildingDetailsPanel.Size = buildingDetailsPanel.GetCombinedMinimumSize();
        buildingDetailsPanel.Position = new Vector2(14, HudTop);
    }
}

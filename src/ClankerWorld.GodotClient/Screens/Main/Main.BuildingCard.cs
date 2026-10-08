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
    private readonly VBoxContainer buildingRecipeSection = new();
    private readonly VBoxContainer buildingRecipeRows = new();
    private string? renderedBuildingRecipes;
    private readonly VBoxContainer buildingPeopleSection = new();
    private readonly Label buildingPeopleSummary = new() { ThemeTypeVariation = "DimLabel" };
    private readonly Label buildingPeopleText = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly VBoxContainer buildingManagementSection = new();
    private readonly Label buildingManagementNote = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly OptionButton buildingManagementChoice = new();
    private readonly Button buildingManagementApply = new() { Text = "Change owner" };
    private readonly Button buildingRemoveButton = new() { Text = "Remove building" };
    private readonly ConfirmationDialog buildingRemoveConfirmation = new();
    private OwnerBuildingRemovalAction? pendingBuildingRemoval;
    private string? pendingBuildingRemovalWorldId;
    private string? renderedBuildingManagementWorldId;
    private string? renderedBuildingManagementId;
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
        BuildBuildingStorageHistory();
        buildingRecipeSection.AddThemeConstantOverride("separation", 4);
        buildingRecipeSection.AddChild(new Label { Text = "CAN MAKE", ThemeTypeVariation = "SectionLabel" });
        buildingRecipeSection.AddChild(new Label
        {
            Text = "Each batch needs its materials and an adult allowed to work here.",
            ThemeTypeVariation = "DimLabel",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        buildingRecipeRows.AddThemeConstantOverride("separation", 8);
        buildingRecipeSection.AddChild(buildingRecipeRows);
        buildingDetailsContent.AddChild(buildingRecipeSection);
        buildingPeopleSection.AddThemeConstantOverride("separation", 4);
        buildingPeopleSection.AddChild(SectionRow("PEOPLE", buildingPeopleSummary));
        buildingPeopleSection.AddChild(buildingPeopleText);
        buildingDetailsContent.AddChild(buildingPeopleSection);
        buildingManagementSection.AddThemeConstantOverride("separation", 5);
        buildingManagementSection.AddChild(new Label { Text = "OWNER ACTIONS", ThemeTypeVariation = "SectionLabel" });
        buildingManagementNote.Text = "Empty stored items and wait for work and deliveries before changing ownership or removing this building. A Warehouse keeps its Town access record when moved.";
        buildingManagementSection.AddChild(buildingManagementNote);
        buildingManagementChoice.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        buildingManagementSection.AddChild(buildingManagementChoice);
        StyleButton(buildingManagementApply);
        buildingManagementApply.Pressed += ApplyBuildingReassignment;
        buildingManagementSection.AddChild(buildingManagementApply);
        StyleButton(buildingRemoveButton);
        buildingRemoveButton.Pressed += ConfirmBuildingRemoval;
        buildingManagementSection.AddChild(buildingRemoveButton);
        buildingDetailsContent.AddChild(buildingManagementSection);
        StyleConfirmation(buildingRemoveConfirmation, "Remove this building?", "Remove building");
        buildingRemoveConfirmation.GetOkButton().ThemeTypeVariation = "DangerButton";
        buildingRemoveConfirmation.Confirmed += RemoveSelectedBuilding;
        buildingRemoveConfirmation.Canceled += CancelBuildingRemoval;
        uiLayer.AddChild(buildingRemoveConfirmation);
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

    private OwnerWorldPlacedBuilding? BuildingAt(OwnerWorldSnapshot snapshot, Vector2I tile, Vector2? canvasPoint = null)
    {
        if (canvasPoint is { } point)
        {
            var (width, _) = MapDimensions(snapshot);
            var mapPoint = (point - mapStage.Position) / (currentTileSize + TileGap);
            foreach (var building in snapshot.PlacedBuildings.OrderByDescending(building => building.InstanceId, StringComparer.Ordinal))
            {
                if (StreetLanternLight.FromBuilding(building, width, snapshot.WrapsEastWest) is not { } lantern) continue;
                var relative = mapPoint - new Vector2(lantern.RoadTile.X, lantern.RoadTile.Y);
                if (snapshot.WrapsEastWest && width > 0) relative.X -= MathF.Round(relative.X / width) * width;
                if (relative.X is < -1 or > 2 || relative.Y is < -1 or > 2) continue;
                if (currentTileSize < WorldTerrainLayer.SpriteTileMinimum)
                {
                    if (lantern.OverviewFitting(currentTileSize + TileGap).HasPoint(relative * (currentTileSize + TileGap)))
                        return building;
                    continue;
                }
                var artPoint = relative * 32;
                var snap = BuildingSprites.AtlasTileSize(currentTileSize) == 16 ? 2 : 1;
                if (NightLightShapes.StreetLantern(lantern.Style, lantern.Post, lantern.Inward, 0, 0, 0, snap)
                    .Any(cell => cell.Kind == LightCellKind.Paint && cell.Area.HasPoint(artPoint)))
                    return building;
            }
        }
        return snapshot.PlacedBuildings.FirstOrDefault(building =>
            (canvasPoint is null || !StreetLanternLight.IsLantern(building.Tags)) &&
            tile.X >= building.Position.X && tile.X < building.Position.X + Math.Max(1, building.Width) &&
            tile.Y >= building.Position.Y && tile.Y < building.Position.Y + Math.Max(1, building.Height));
    }

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
        CancelBuildingRemoval();
        buildingManagementChoice.GetPopup().Hide();
        renderedBuildingManagementWorldId = null;
        renderedBuildingManagementId = null;
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
        if (pendingBuildingRemoval is { } pending &&
            (pendingBuildingRemovalWorldId != snapshot.WorldId || pending.InstanceId != selectedBuildingId))
            CancelBuildingRemoval();
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
        var inside = PeopleInside(snapshot, building.InstanceId).GetValueOrDefault(building.InstanceId, [])
            .Select(person => person.DisplayName).Order(StringComparer.CurrentCulture).ToArray();
        var jobs = snapshot.ProductionJobs
            .Where(job => job.BuildingInstanceId == building.InstanceId &&
                string.Equals(job.State, "running", StringComparison.OrdinalIgnoreCase))
            .OrderBy(job => job.StartedTick).ThenBy(job => job.JobId, StringComparer.Ordinal).ToArray();
        var stored = building.StoredItems?
            .Where(item => item.Quantity > 0)
            .OrderByDescending(item => item.Quantity).ThenBy(item => item.Kind, StringComparer.Ordinal).ToArray();

        var kind = BuildingSprites.KindFor(building.Tags);
        var (mapWidth, _) = MapDimensions(snapshot);
        var lantern = StreetLanternLight.FromBuilding(building, mapWidth, snapshot.WrapsEastWest);
        var door = BuildingDoor.Facing(footprint, building.Entrance is { } entrance
            ? new Vector2I(entrance.X, entrance.Y) : null);
        foreach (var header in new[] { buildingQuickHeader, buildingDetailsHeader })
        {
            header.NameLabel.Text = name;
            header.NameLabel.TooltipText = name;
            header.OwnerLabel.Text = owner.Length == 0 ? "No owner recorded" : owner;
            if (lantern is { } fitting) header.SetLantern(fitting);
            else header.SetRoof(kind, footprint.Size, door);
        }
        // Occupancy may be recorded even when the building has no owner-specific item list.
        foreach (var storage in new[] { buildingQuickStorage, buildingDetailsStorage })
        {
            storage.Visible = stored is not null || building.StorageCapacity is > 0 && building.StoredQuantity >= 0;
            storage.SetCapacity(building.StorageCapacity, building.StoredQuantity);
            storage.SetItems(stored?.Select(item => (item.Kind, item.Quantity, GameUiText.ItemName(item.Kind))).ToArray());
        }
        RenderBuildingStatus(snapshot, building, jobs, inside);
        RenderBuildingDetails(snapshot, building, household, town, jobs, inside);
        RenderBuildingStorageHistory(snapshot, building);

        buildingDetailsPanel.Visible = buildingDetailsRequested;
        buildingQuickCard.Visible = !buildingDetailsRequested;
        PositionBuildingQuickCard(snapshot);
        PositionBuildingDetails();
    }

    /// <summary>The quick card's one line on what is happening: work in progress, or who is inside.</summary>
    private void RenderBuildingStatus(OwnerWorldSnapshot snapshot, OwnerWorldPlacedBuilding building,
        OwnerWorldProductionJob[] jobs, string[] inside)
    {
        var signature = jobs.Length > 0
            ? string.Join('|', JobSummary(snapshot, jobs[0])) + (jobs.Length > 1 ? "+" + (jobs.Length - 1) : "")
            : string.Join('|', inside);
        signature += $"|{building.ExpansionState}|{building.ExpansionFailure}|" +
            string.Join('|', building.Trades.Select(trade => trade.OfferId + ":" + trade.Status)) + "|" +
            string.Join('|', building.ToolMakingRequests.Select(request => request.Id + ":" + request.Status + ":" + request.Blocker));
        var townHall = building.Tags?.Contains("town_hall", StringComparer.Ordinal) == true;
        var lantern = StreetLanternLight.IsLantern(building.Tags);
        var abandoned = building.TownId is { } townId && snapshot.Towns.Any(town => town.Id == townId && town.IsAbandoned);
        var lit = snapshot.DarknessBasisPoints > 500 && !abandoned;
        signature += $"|{townHall}|{lantern}|{(lantern && lit)}|{(lantern && abandoned)}";
        var market = MarketForBuilding(snapshot, building.InstanceId);
        signature += "|" + (market is null ? string.Empty : MarketBuildingText(market, building.InstanceId));
        var port = IsPortBuilding(building);
        signature += "|" + (port ? PortUsageText(snapshot, building) : string.Empty);
        if (renderedBuildingStatus == signature) return;
        renderedBuildingStatus = signature;
        ClearChildren(buildingQuickStatus);
        if (building.ExpansionState == "running")
            buildingQuickStatus.AddChild(new Label
            {
                Text = building.Tags?.Contains("house", StringComparer.Ordinal) == true
                    ? "House expansion underway · more storage and resident places when finished"
                    : "Expanding storage",
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            });
        else if (building.ExpansionFailure is { } failure)
            buildingQuickStatus.AddChild(new Label
            {
                Text = failure,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            });
        var activeRequests = building.ToolMakingRequests.Count(request => request.Status is "requested" or "accepted" or "ready" or "offered");
        if (activeRequests > 0)
            buildingQuickStatus.AddChild(new Label { Text = $"{Plural(activeRequests, "tool request")} · see Details" });
        var openTrades = building.Trades.Count(trade => trade.Status == "open");
        if (openTrades > 0)
            buildingQuickStatus.AddChild(new Label { Text = $"{Plural(openTrades, "customer exchange")} waiting" });
        if (townHall)
            buildingQuickStatus.AddChild(new Label { Text = "Town civic notice place · see Council decisions in World Info" });
        if (market is not null)
            buildingQuickStatus.AddChild(new Label
            {
                Text = MarketBuildingText(market, building.InstanceId),
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            });
        if (port)
            buildingQuickStatus.AddChild(new Label { Text = PortUsageText(snapshot, building), AutowrapMode = TextServer.AutowrapMode.WordSmart });
        if (lantern)
        {
            buildingQuickStatus.AddChild(new Label
            {
                Text = abandoned ? "Dark · its Town is abandoned · lights again when someone resettles it"
                    : lit ? "Lit · lights automatically at dusk · no fuel" : "Unlit · lights automatically at dusk · no fuel",
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            });
            return;
        }
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
        var townHall = building.Tags?.Contains("town_hall", StringComparer.Ordinal) == true;
        var market = MarketForBuilding(snapshot, building.InstanceId);
        var lantern = StreetLanternLight.IsLantern(building.Tags);
        var port = IsPortBuilding(building);
        var usedBy = port ? "Town residents and visitors with Council permission" : lantern ? "Road lighting" : townHall ? "Town civic notice place" : market is not null
            ? "Market sellers and customers · goods keep their recorded owners" : household ?? (town is not null && building.Tags?.Contains("warehouse") == true
            ? $"{town} residents" : "Any agent");
        var facts = new List<(string Key, string Value)>
        {
            ("Owner", household ?? town ?? "Nobody"),
            ("Used by", usedBy),
            ("Built", SplitClock(DisplayWorldClock(building.PlacedTick)).Date),
            ("Footprint", $"{building.Width} × {building.Height} tiles"),
        };
        var footprint = Footprint(building);
        var animals = snapshot.Animals.Where(animal => footprint.HasPoint(new Vector2I(animal.Position.X, animal.Position.Y)))
            .OrderBy(animal => animal.Name, StringComparer.CurrentCulture).ThenBy(animal => animal.Id, StringComparer.Ordinal).ToArray();
        if (animals.Length > 0)
            facts.Add(("Animals here", string.Join('\n', animals.Select(GameUiText.AnimalDescription))));
        if ((townHall || market is not null || lantern || port) && snapshot.Towns.SelectMany(item => item.Projects)
                .FirstOrDefault(project => project.CompletedBuildingId == building.InstanceId) is { } project)
        {
            facts.Add(("Town project", project.Name));
            facts.Add(("Proposed by", project.ProposerName));
            facts.Add(("Council approval", $"{project.Approval.Yes} yes / {project.Approval.No} no · {project.Approval.RequiredYes} yes needed"));
            facts.Add(("Materials spent", string.Join(" · ", project.Materials.Select(q =>
                $"{q.Supplied} {GameUiText.ItemName(q.Kind)}")) + " · provisional budget"));
            facts.Add(("Construction", $"{project.WorkDone} / {project.WorkRequired} units · provisional work"));
        }
        if (port)
        {
            facts.Add(("Docking spaces", PortUsageText(snapshot, building)));
            facts.Add(("Travel", "One passenger with carried goods · boats remain Town property"));
            facts.Add(("Night lantern", building.TownId is { } portTown && snapshot.Towns.Any(item => item.Id == portTown && item.IsAbandoned)
                ? "Dark · its Town is abandoned · lights again when someone resettles it"
                : "Lights automatically at dusk · no fuel"));
        }
        if (building.StorageCapacity is { } capacity)
            facts.Add(("Storage", $"{building.StoredQuantity} / {capacity} items"));
        if (market is not null)
        {
            facts.Add(("Market", $"Town-owned hall and stalls · {market.PlazaWidth} × {market.PlazaHeight} plaza"));
            facts.Add(("Stall use", market.RemovedTick is null
                ? "An adult seller may borrow a free stall until leaving; customers from any Town may trade"
                : "Market removed · stalls inactive; goods keep their recorded owners"));
            var stalls = building.InstanceId == market.HallBuildingId ? market.Stalls
                : market.Stalls.Where(stall => stall.BuildingInstanceId == building.InstanceId).ToArray();
            foreach (var stall in stalls)
            {
                facts.Add(($"Stall {stall.SlotIndex + 1}", MarketStallText(stall, market.RemovedTick is not null)));
                if (stall.Stock.Count == 0) facts.Add(("Goods at stall", "No goods deposited here"));
                foreach (var stock in stall.Stock)
                    facts.Add(("Goods at stall", MarketStockText(stock)));
                foreach (var trade in stall.Trades)
                {
                    facts.Add(("Exchange", MarketTradeText(trade)));
                    facts.Add(("Exchange ownership", $"Goods: {trade.GoodsOwnerName} · payment: {trade.PaymentOwnerName}"));
                }
            }
        }
        if (building.Tags?.Any(tag => tag is "farmhouse" or "blacksmith" or "tailor" or "store" or "restaurant" or "clinic") == true)
            facts.Add(("Customers", "May trade here; household stock and other uses remain private"));
        foreach (var hearing in LandHearingText.ForInspection(snapshot.Towns.SelectMany(item => item.LandHearings).Where(item =>
                     item.Tiles.Any(tile => tile.X >= building.Position.X && tile.X < building.Position.X + building.Width &&
                         tile.Y >= building.Position.Y && tile.Y < building.Position.Y + building.Height))))
        {
            facts.Add(("Land hearing", LandHearingText.Summary(hearing)));
            if (hearing.Rulings.Count > 0)
                facts.Add(("Use permission", LandHearingText.Outcome(hearing.Rulings[^1].Outcome, DisplayWorldClock)));
            facts.Add(("Private property", "This hearing does not change the building's owner or access"));
        }
        foreach (var transfer in LandTransferText.ForInspection(snapshot.Towns.SelectMany(item => item.LandTransfers).Where(item =>
                     item.Tiles.Any(tile => tile.X >= building.Position.X && tile.X < building.Position.X + building.Width &&
                         tile.Y >= building.Position.Y && tile.Y < building.Position.Y + building.Height))))
        {
            facts.Add(("Permission transfer", LandTransferText.Summary(transfer)));
            facts.Add(("Household acceptance", LandTransferText.Acceptance(transfer)));
            facts.Add(("Proposed", DisplayWorldClock(transfer.ProposedTick)));
            foreach (var terms in LandTransferText.Terms(transfer, DisplayWorldClock))
                facts.Add(("Exact permission terms", terms));
            facts.Add(("Private property", "This permission transfer leaves the building's owner and access unchanged"));
        }
        foreach (var request in building.ToolMakingRequests)
            facts.Add((request.RequesterName + " · " + request.RecipeName,
                GameUiText.ToolMakingRequestStatus(request.Status) +
                (string.IsNullOrWhiteSpace(request.Blocker) ? "" : " · " + request.Blocker)));
        foreach (var trade in building.Trades)
        {
            var terms = $"{trade.GoodsQuantity} {GameUiText.ItemName(trade.GoodsKind)} for {trade.PaymentQuantity} {GameUiText.ItemName(trade.PaymentKind)}";
            var status = trade.Status switch
            {
                "open" => "waiting for both traders at the shop",
                "settled" => "completed · purchase carried away, payment stored here",
                _ => "cancelled · " + trade.CancellationReason,
            };
            facts.Add((trade.BuyerName, $"{terms} · {status}"));
        }
        if (building.ResidentLimit is { } residentLimit)
        {
            facts.Add(("Permanent residents", $"{building.PermanentResidentCount} / {residentLimit} places"));
            if (building.HasDominantFamily)
                facts.Add(("Family limit", "One family is most of the household · 4 places per tile"));
            if (building.IsOvercrowded)
                facts.Add(("Crowding", "Over the limit · nobody new can move in until there is room"));
        }
        if (building.ExpansionState == "running")
            facts.Add(("Expansion", building.ResidentLimit is not null
                ? "Work in progress · current resident places remain until completion"
                : "Work in progress"));
        else if (building.ExpansionFailure is { } failure)
            facts.Add(("Expansion", failure));
        if (building.InvitedGuests is { Count: > 0 } guests)
            facts.Add(("Storm guests", string.Join(", ", guests) + " · shelter only"));
        // Only an entrance beside the footprint names a side; the fallback door is not a fact.
        var (mapWidth, _) = MapDimensions(snapshot);
        if (StreetLanternLight.FromBuilding(building, mapWidth, snapshot.WrapsEastWest) is { } fitting)
        {
            facts.Add(("Road edge", $"{fitting.Edge} edge of Road tile ({fitting.RoadTile.X}, {fitting.RoadTile.Y})"));
            facts.Add(("Lighting", "Lights automatically at dusk; off at dawn · no fuel"));
        }
        else if (!lantern && building.Entrance is { } entrance &&
            BuildingDoor.Facing(Footprint(building), new Vector2I(entrance.X, entrance.Y)) is { Tile: not null } door)
            facts.Add(("Door", $"{door.Side} side"));
        RenderBuildingFacts(facts);

        buildingWorkSection.Visible = jobs.Length > 0;
        var work = string.Join('\n', jobs.Select(job => string.Join('|', JobSummary(snapshot, job)) + "|" + JobMaterialsText(job)));
        if (renderedBuildingWork != work)
        {
            renderedBuildingWork = work;
            ClearChildren(buildingWorkRows);
            foreach (var job in jobs) buildingWorkRows.AddChild(JobRow(snapshot, job, includeMaterials: true));
        }

        RenderBuildingRecipes(building);

        var residents = building.HouseholdId is { } householdId && building.Tags?.Contains("house") == true
            ? snapshot.Inhabitants
                .Where(person => !person.IsDraft && IsLiving(person) && person.Relationships.Any(link =>
                    link.Type == "household_membership" && link.OtherPartyId == householdId &&
                    string.Equals(link.State, "accepted", StringComparison.Ordinal)))
                .Select(person => person.DisplayName).Order(StringComparer.CurrentCulture).ToArray()
            : [];
        buildingPeopleSection.Visible = !lantern;
        buildingPeopleSummary.Text = inside.Length == 0 ? "Nobody inside" : $"{inside.Length} inside";
        var people = new List<string>();
        if (inside.Length > 0) people.Add("Inside: " + string.Join(", ", inside));
        if (residents.Length > 0) people.Add($"Permanent residents (including travelers): {string.Join(", ", residents)}");
        buildingPeopleText.Text = string.Join('\n', people);
        buildingPeopleText.Visible = people.Count > 0;
        RenderBuildingManagement(snapshot, building);
    }

    private static OwnerWorldMarket? MarketForBuilding(OwnerWorldSnapshot snapshot, string buildingId) =>
        snapshot.Towns.SelectMany(town => town.Markets).FirstOrDefault(market =>
            market.HallBuildingId == buildingId || market.Stalls.Any(stall => stall.BuildingInstanceId == buildingId));

    private static string MarketBuildingText(OwnerWorldMarket market, string buildingId)
    {
        if (market.RemovedTick is not null) return "Market inactive · goods keep their recorded owners";
        if (buildingId == market.HallBuildingId)
            return $"{Plural(market.Stalls.Count, "stall")} built · {market.Stalls.Count(stall => stall.SellerId is not null)} borrowed";
        var stall = market.Stalls.First(item => item.BuildingInstanceId == buildingId);
        return MarketStallText(stall) + $" · {stall.Stock.Sum(stock => stock.Quantity)} items at stall · " +
            $"{Plural(stall.Trades.Count(trade => trade.Status == "open"), "exchange")} waiting";
    }

    private void RenderBuildingManagement(OwnerWorldSnapshot snapshot, OwnerWorldPlacedBuilding building)
    {
        if (!TryGetOwner(out _, out _, out _))
        {
            buildingManagementSection.Hide();
            buildingManagementChoice.GetPopup().Hide();
            renderedBuildingManagementWorldId = null;
            renderedBuildingManagementId = null;
            return;
        }

        var sameBuilding = renderedBuildingManagementWorldId == snapshot.WorldId &&
            renderedBuildingManagementId == building.InstanceId;
        var popup = buildingManagementChoice.GetPopup();
        var focusedIndex = popup.Visible ? popup.GetFocusedItem() : -1;
        var focusedTarget = sameBuilding && focusedIndex >= 0 && focusedIndex < buildingManagementChoice.ItemCount
            ? buildingManagementChoice.GetItemMetadata(focusedIndex).AsString()
            : null;
        if (!sameBuilding) popup.Hide();
        var previousTarget = sameBuilding && buildingManagementChoice.Selected >= 0
            ? buildingManagementChoice.GetItemMetadata(buildingManagementChoice.Selected).AsString()
            : null;
        buildingManagementSection.Show();
        buildingManagementChoice.Clear();
        var isWarehouse = building.Tags?.Contains("warehouse", StringComparer.Ordinal) == true;
        if (isWarehouse)
        {
            buildingManagementNote.Text = "Move a Warehouse to another recorded Town. Its Town border and land records stay where they are; stored stock and active work block the move.";
            foreach (var town in snapshot.Towns.Where(item => item.Id != building.TownId).OrderBy(item => item.Name, StringComparer.CurrentCulture))
            {
                buildingManagementChoice.AddItem(town.Name);
                buildingManagementChoice.SetItemMetadata(buildingManagementChoice.ItemCount - 1, town.Id);
            }
        }
        else if (building.AllowsHouseholdOwner || building.HouseholdId is not null)
        {
            var isHouse = building.Tags?.Contains("house", StringComparer.Ordinal) == true;
            buildingManagementNote.Text = "Changing a private building's household owner keeps its recorded Town assignment and title. Stock, deliveries and active work block the change.";
            foreach (var household in snapshot.Stockpiles.Where(item => item.OwnerId != building.HouseholdId)
                         .OrderBy(item => item.Name, StringComparer.CurrentCulture))
            {
                buildingManagementChoice.AddItem(household.Name);
                buildingManagementChoice.SetItemMetadata(buildingManagementChoice.ItemCount - 1, household.OwnerId);
            }
            if (!isHouse && building.HouseholdId is not null)
            {
                buildingManagementChoice.AddItem("No household owner");
                buildingManagementChoice.SetItemMetadata(buildingManagementChoice.ItemCount - 1, string.Empty);
            }
        }
        else
        {
            buildingManagementNote.Text = "This building has no household or Town reassignment rule.";
        }

        var selectedTarget = 0;
        for (var index = 0; index < buildingManagementChoice.ItemCount; index++)
            if (previousTarget is not null && buildingManagementChoice.GetItemMetadata(index).AsString() == previousTarget)
            {
                selectedTarget = index;
                break;
            }
        if (buildingManagementChoice.ItemCount > 0) buildingManagementChoice.Select(selectedTarget);
        if (popup.Visible)
        {
            var restoredFocus = -1;
            for (var index = 0; index < buildingManagementChoice.ItemCount; index++)
                if (focusedTarget is not null && buildingManagementChoice.GetItemMetadata(index).AsString() == focusedTarget)
                {
                    restoredFocus = index;
                    break;
                }
            if ((focusedTarget is not null && restoredFocus < 0) || buildingManagementChoice.ItemCount == 0)
                popup.Hide();
            else if (restoredFocus >= 0)
                popup.SetFocusedItem(restoredFocus);
        }
        renderedBuildingManagementWorldId = buildingManagementChoice.ItemCount > 0 ? snapshot.WorldId : null;
        renderedBuildingManagementId = buildingManagementChoice.ItemCount > 0 ? building.InstanceId : null;
        buildingManagementChoice.Visible = buildingManagementChoice.ItemCount > 0;
        buildingManagementApply.Visible = buildingManagementChoice.ItemCount > 0;
        buildingManagementApply.Disabled = buildingManagementChoice.ItemCount == 0;
        buildingRemoveButton.Visible = true;
    }

    private async void ApplyBuildingReassignment()
    {
        if (SelectedBuilding() is not { } building || buildingCardSnapshot is not { } snapshot ||
            buildingManagementChoice.Selected < 0 || !TryGetOwner(out var authority, out var deviceId, out var signer))
        {
            SetStatus("Select a building and a new owner first.", good: false);
            return;
        }

        var target = buildingManagementChoice.GetItemMetadata(buildingManagementChoice.Selected).AsString();
        var isWarehouse = building.Tags?.Contains("warehouse", StringComparer.Ordinal) == true;
        var action = new OwnerBuildingReassignmentAction(building.InstanceId, building.TownId, building.HouseholdId,
            isWarehouse ? target : null, isWarehouse || string.IsNullOrEmpty(target) ? null : target,
            WorldId: snapshot.WorldId);
        OwnerBuildingManagementResult? result = null;
        var generation = observationSession.RequestGeneration;
        await RunOwnerActionAsync(async () =>
        {
            result = await AwaitCurrentWorldResultAsync(ownerApi.ReassignBuildingAsync(ResolveWorldUri(), authority, deviceId, action,
                signer, CancellationToken.None));
            return result.Applied ? "Building owner changed" : result.Failure ?? "The building could not be reassigned.";
        });
        if (IsCurrentWorldRequest(generation) && result is { } changed)
            SetStatus(changed.Applied ? "Building owner changed" : changed.Failure ?? "The building could not be reassigned.",
                good: changed.Applied);
    }

    private void ConfirmBuildingRemoval()
    {
        if (SelectedBuilding() is not { } building || buildingCardSnapshot is not { } snapshot ||
            !TryGetOwner(out _, out _, out _)) return;
        pendingBuildingRemoval = new(building.InstanceId, building.TownId, building.HouseholdId, snapshot.WorldId);
        pendingBuildingRemovalWorldId = snapshot.WorldId;
        buildingRemoveConfirmation.DialogText = $"Remove {building.DisplayName ?? "this building"}? Its Town border and existing Roads will remain. The action is saved to the world.";
        PopupDialog(buildingRemoveConfirmation);
    }

    private void CancelBuildingRemoval()
    {
        pendingBuildingRemoval = null;
        pendingBuildingRemovalWorldId = null;
        buildingRemoveConfirmation.Hide();
    }

    private async void RemoveSelectedBuilding()
    {
        var action = pendingBuildingRemoval;
        var worldId = pendingBuildingRemovalWorldId;
        CancelBuildingRemoval();
        if (action is null || buildingCardSnapshot?.WorldId != worldId || selectedBuildingId != action.InstanceId ||
            !TryGetOwner(out var authority, out var deviceId, out var signer))
        {
            SetStatus("Select a building before removing it.", good: false);
            return;
        }

        OwnerBuildingManagementResult? result = null;
        var generation = observationSession.RequestGeneration;
        await RunOwnerActionAsync(async () =>
        {
            result = await AwaitCurrentWorldResultAsync(ownerApi.RemoveBuildingAsync(ResolveWorldUri(), authority, deviceId,
                action,
                signer, CancellationToken.None));
            return result.Applied ? "Building removed" : result.Failure ?? "The building could not be removed.";
        });
        if (!IsCurrentWorldRequest(generation) || result is not { } removed) return;
        SetStatus(removed.Applied ? "Building removed" : removed.Failure ?? "The building could not be removed.",
            good: removed.Applied);
        if (removed.Applied && buildingCardSnapshot?.WorldId == worldId && selectedBuildingId == action.InstanceId)
            ClearBuildingSelection();
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
    private HBoxContainer JobRow(OwnerWorldSnapshot snapshot, OwnerWorldProductionJob job, bool includeMaterials = false)
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
        if (includeMaterials)
            lines.AddChild(new Label
            {
                Text = JobMaterialsText(job),
                ThemeTypeVariation = "DimLabel",
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            });
        row.AddChild(lines);
        return row;
    }

    private static string MaterialQuantities(IReadOnlyList<OwnerWorldMaterialQuantity> materials) =>
        materials.Count == 0 ? "None" : string.Join(" · ", materials.Select(material =>
            $"{material.Quantity} {GameUiText.ItemName(material.Kind)}"));

    private static string RecipeMaterialsText(OwnerWorldProductionRecipe recipe) =>
        $"Uses: {MaterialQuantities(recipe.Inputs)}\nMakes: {MaterialQuantities(recipe.Outputs)}";

    private static string JobMaterialsText(OwnerWorldProductionJob job) =>
        (job.Recipe is { } recipe ? RecipeMaterialsText(recipe) : "Recipe details unavailable") +
        "\nMaterials held for this work: " + (job.HeldInputs is { } held ? MaterialQuantities(held) : "Not reported");

    private void RenderBuildingRecipes(OwnerWorldPlacedBuilding building)
    {
        buildingRecipeSection.Visible = building.AvailableRecipes is { Count: > 0 };
        var signature = building.AvailableRecipes is { } recipes
            ? string.Join('\n', recipes.Select(recipe => recipe.Id + "|" + recipe.Name + "|" + RecipeMaterialsText(recipe)))
            : null;
        if (renderedBuildingRecipes == signature) return;
        renderedBuildingRecipes = signature;
        ClearChildren(buildingRecipeRows);
        foreach (var recipe in building.AvailableRecipes ?? [])
        {
            var row = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            row.AddThemeConstantOverride("separation", 2);
            row.AddChild(new Label { Text = recipe.Name, AutowrapMode = TextServer.AutowrapMode.WordSmart });
            row.AddChild(new Label
            {
                Text = RecipeMaterialsText(recipe),
                ThemeTypeVariation = "DimLabel",
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            });
            buildingRecipeRows.AddChild(row);
        }
    }

    private (string What, int Percent, string Detail) JobSummary(OwnerWorldSnapshot snapshot, OwnerWorldProductionJob job)
    {
        var worker = snapshot.Inhabitants.FirstOrDefault(person => person.Id == job.WorkerId)?.DisplayName ?? "Someone";
        var length = Math.Max(1, job.CompletionTick - job.StartedTick);
        var percent = (int)Math.Clamp((snapshot.WorldTick - job.StartedTick) * 100 / length, 0, 100);
        return (job.Recipe?.Name ?? RecipeName(job.RecipeId), percent, $"{worker} · {TimeLeft(job.CompletionTick - snapshot.WorldTick)}");
    }

    /// <summary>A recipe's own words, such as "Mill grain" for <c>.../mill-grain</c>.</summary>
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
        // Measure the fixed header, themed frame, margins and gap together;
        // a guessed padding total can let a long shop history run off screen.
        var fixedHeight = buildingDetailsPanel.GetCombinedMinimumSize().Y -
            buildingDetailsScroll.GetCombinedMinimumSize().Y;
        var room = UiSize.Y - HudTop - 14 - fixedHeight;
        buildingDetailsScroll.CustomMinimumSize = new Vector2(0,
            Math.Max(60, Math.Min(buildingDetailsContent.GetCombinedMinimumSize().Y, room)));
        buildingDetailsPanel.Size = buildingDetailsPanel.GetCombinedMinimumSize();
        buildingDetailsPanel.Position = new Vector2(14, HudTop);
    }
}

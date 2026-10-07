using ClankerWorld.GodotClient.Pairing;
using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    /// <summary>
    /// Clicking a building opens its quick card and outlines it; Details docks
    /// on the left with its facts, work, storage and people; Escape steps back;
    /// an agent's card replaces it; world updates refresh it; and Details stays
    /// inside the view.
    /// </summary>
    private async Task VerifyBuildingCardsAsync(OwnerWorldSnapshot baseMap)
    {
        var smith = new OwnerWorldInhabitant("agent:smith-ui-test", "Oren", "active", new(2, 2), 8_000, [], [],
            new OwnerWorldRoute("idle", null, null, [], string.Empty),
            new OwnerWorldSpatialKnowledge(new(2, 2), [new(2, 2)], [new(2, 2)]), false)
        {
            Relationships = [new OwnerWorldInhabitantRelationship("home:oren", "household:one",
                "household_membership", "accepted", "household", 1)],
        };
        var house = new OwnerWorldPlacedBuilding("test-house", "sha256:test/house", new(2, 2), 0, "House", ["house"], 2, 1,
            "town:first", "household:one", [new("wood", 4), new("bread", 2), new("never_an_item", 1)], new(2, 3),
            ResidentLimit: 8, PermanentResidentCount: 2, HasDominantFamily: true, ExpansionState: "running",
            StorageCapacity: 128, StoredQuantity: 64);
        var buildingMap = baseMap with
        {
            WorldTick = 30,
            Inhabitants = [smith],
            PlacedBuildings = [.. baseMap.PlacedBuildings.Where(item => item.InstanceId != house.InstanceId), house],
            ProductionJobs = [new("job-ui-test", "sha256:test/wooden-axe", house.InstanceId, smith.Id, 10, 50, "running")],
        };
        PixelMeter SpaceMeter(ItemStorage storage) => storage.FindChildren("StorageSpaceMeter", "", owned: false)
            .OfType<PixelMeter>().Single();
        string SpaceText(ItemStorage storage) => storage.FindChildren("StorageSpaceText", nameof(Label), owned: false)
            .OfType<Label>().Single().Text;
        bool HasSpace(ItemStorage storage) => storage.FindChildren("StorageSpace", nameof(HBoxContainer), owned: false)
            .OfType<HBoxContainer>().Single().Visible;
        RenderMap(buildingMap);
        HandleMapInput(new InputEventMouseButton
        {
            Position = mapStage.Position + new Vector2(currentTileSize * 3.5f, currentTileSize * 2.5f),
            ButtonIndex = MouseButton.Left,
            Pressed = true,
        });
        for (var frame = 0; frame < 3; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var quickText = string.Join('\n', buildingQuickStatus.FindChildren("*", "Label", owned: false)
            .OfType<Label>().Select(label => label.Text));
        if (!buildingQuickCard.Visible || buildingDetailsPanel.Visible || selectedTilePanel.Visible ||
            terrainLayer.SelectedBuilding != new Rect2I(2, 2, 2, 1) ||
            buildingQuickHeader.NameLabel.Text != "House" ||
            buildingQuickHeader.OwnerLabel.Text != "Founder's household · First Town" ||
            !quickText.Contains("Wooden axe", StringComparison.Ordinal) ||
            !quickText.Contains("50%", StringComparison.Ordinal) ||
            !quickText.Contains("Oren · ", StringComparison.Ordinal) ||
            buildingQuickStorage.SlotCount != 3 || buildingQuickStorage.Summary != "7 items" ||
            !HasSpace(buildingQuickStorage) || SpaceMeter(buildingQuickStorage).Percent != 50 ||
            SpaceText(buildingQuickStorage) != "64 / 128 used" ||
            !mapCanvas.GetGlobalRect().Grow(1).Encloses(buildingQuickCard.GetGlobalRect()))
            throw new InvalidOperationException($"Clicking a building must outline it and open its quick card with its owner, work and stored items: {quickText} / {buildingQuickHeader.OwnerLabel.Text} / {buildingQuickStorage.Summary}.");

        buildingDetailsButton.EmitSignal(BaseButton.SignalName.Pressed);
        for (var frame = 0; frame < 3; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var facts = string.Join('\n', buildingFacts.GetChildren().OfType<Label>().Select(label => label.Text));
        if (!buildingDetailsPanel.Visible || buildingQuickCard.Visible ||
            !facts.Contains("Owner\nFounder's household", StringComparison.Ordinal) ||
            !facts.Contains("Used by\nFounder's household", StringComparison.Ordinal) ||
            !facts.Contains("Built\n", StringComparison.Ordinal) ||
            !facts.Contains("Permanent residents\n2 / 8 places", StringComparison.Ordinal) ||
            !facts.Contains("Family limit\nOne family is most of the household · 4 places per tile", StringComparison.Ordinal) ||
            !facts.Contains("Expansion\nWork in progress · current resident places remain until completion", StringComparison.Ordinal) ||
            !facts.Contains("Door\nSouth side", StringComparison.Ordinal) ||
            !buildingWorkSection.Visible || buildingWorkRows.GetChildCount() != 1 ||
            buildingDetailsStorage.Summary != "3 kinds · 7 items" || buildingDetailsStorage.SlotCount != 3 ||
            !buildingPeopleText.Text.Contains("Inside: Oren", StringComparison.Ordinal) ||
            !buildingPeopleText.Text.Contains("Permanent residents (including travelers): Oren", StringComparison.Ordinal) ||
            !mapCanvas.GetGlobalRect().Grow(1).Encloses(buildingDetailsPanel.GetGlobalRect()) ||
            buildingDetailsPanel.Position.X > 14.5f)
            throw new InvalidOperationException($"Details must dock on the left with the building's facts, work, storage and people: {facts} / {buildingPeopleText.Text} / {buildingDetailsPanel.GetGlobalRect()}.");

        var storageViews = new[] { buildingQuickStorage, buildingDetailsStorage };
        // Recorded occupancy can include other owners' goods absent from this household's item grid.
        foreach (var storage in storageViews)
            if (!HasSpace(storage) || SpaceMeter(storage).Percent != 50 || SpaceText(storage) != "64 / 128 used" ||
                SpaceMeter(storage).TooltipText != "Storage used: 64 of 128 units.")
                throw new InvalidOperationException("Building storage must use the host's occupied quantity and limit, rather than guessing from the visible household item grid.");
        foreach (var (quantity, percent) in new[] { (0, 0), (1, 1), (128, 100), (129, 100) })
        {
            RenderBuildingCard(buildingMap with { PlacedBuildings = [house with { StoredQuantity = quantity }] });
            foreach (var storage in storageViews)
                if (!HasSpace(storage) || SpaceMeter(storage).Percent != percent || SpaceText(storage) != $"{quantity} / 128 used")
                    throw new InvalidOperationException("Empty, lightly occupied, full and over-limit storage must retain exact recorded numbers while bounding the meter and keeping nonempty storage visible.");
        }
        RenderBuildingCard(buildingMap with
        {
            WorldId = "another-storage-world",
            PlacedBuildings = [house with { StorageCapacity = 256, StoredQuantity = 128 }],
        });
        foreach (var storage in storageViews)
            if (SpaceMeter(storage).Percent != 50 || SpaceText(storage) != "128 / 256 used")
                throw new InvalidOperationException("Changing worlds and capacities must refresh exact storage counts even when the percentage is unchanged.");
        foreach (int? limit in new int?[] { null, 0, -1 })
        {
            RenderBuildingCard(buildingMap with { PlacedBuildings = [house with { StorageCapacity = limit }] });
            if (storageViews.Any(HasSpace))
                throw new InvalidOperationException("Missing or unusable storage limits must hide the bar without inventing a capacity.");
        }
        RenderBuildingCard(buildingMap with { PlacedBuildings = [house with { StoredItems = null }] });
        foreach (var storage in storageViews)
            if (!storage.Visible || !HasSpace(storage) || SpaceText(storage) != "64 / 128 used" ||
                storage.SlotCount != 0 || storage.Summary.Length != 0 ||
                storage.FindChildren("*", nameof(Label), owned: false).OfType<Label>()
                    .Any(label => label.Visible && label.Text == "Nothing stored here yet."))
                throw new InvalidOperationException("Recorded occupancy must stay visible without inventing an empty inventory when the owner-specific item list is absent.");
        var unstored = house with { InstanceId = "no-storage-ui-test", StoredItems = null, StorageCapacity = null };
        selectedBuildingId = unstored.InstanceId;
        RenderBuildingCard(buildingMap with { PlacedBuildings = [unstored] });
        if (storageViews.Any(storage => storage.Visible || HasSpace(storage)))
            throw new InvalidOperationException("Selecting a building without recorded storage must clear the previous building's storage view.");
        selectedBuildingId = house.InstanceId;
        RenderBuildingCard(buildingMap);

        // World updates refresh the open panel, which scrolls rather than running off the view.
        RenderBuildingCard(buildingMap with
        {
            PlacedBuildings = [.. buildingMap.PlacedBuildings.Where(item => item.InstanceId != house.InstanceId),
                house with
                {
                    StoredItems = [new("wood", 5), new("bread", 2), new("never_an_item", 1), new("fruit", 3)],
                    Width = 2, Height = 2, StorageCapacity = 256, StoredQuantity = 11, FootprintRevision = 2,
                    InvitedGuests = ["Lina"], ExpansionState = "completed", ResidentLimit = 16,
                    PermanentResidentCount = 17, HasDominantFamily = true, IsOvercrowded = true,
                }],
            ProductionJobs = [],
        });
        if (buildingDetailsStorage.Summary != "4 kinds · 11 items" || buildingDetailsStorage.SlotCount != 4 ||
            buildingWorkSection.Visible || SpaceMeter(buildingDetailsStorage).Percent != 4 ||
            SpaceText(buildingDetailsStorage) != "11 / 256 used")
            throw new InvalidOperationException("Building Details must follow the building's latest storage and work.");
        facts = string.Join('\n', buildingFacts.GetChildren().OfType<Label>().Select(label => label.Text));
        if (!facts.Contains("Footprint\n2 × 2 tiles", StringComparison.Ordinal) ||
            !facts.Contains("Storage\n11 / 256 items", StringComparison.Ordinal) ||
            !facts.Contains("Permanent residents\n17 / 16 places", StringComparison.Ordinal) ||
            !facts.Contains("Crowding\nOver the limit · nobody new can move in until there is room", StringComparison.Ordinal) ||
            !facts.Contains("Storm guests\nLina · shelter only", StringComparison.Ordinal))
            throw new InvalidOperationException("Building Details must show current expansion geometry, capacity and limited guest access.");
        var store = house with
        {
            DefinitionId = "sha256:test/store",
            DisplayName = "Store",
            Tags = ["store", "storage"],
            Width = 1,
            Height = 2,
            Entrance = new(3, 2),
            ResidentLimit = null,
            PermanentResidentCount = 0,
            HasDominantFamily = false,
            IsOvercrowded = false,
            ExpansionState = null,
            ExpansionFailure = null,
            Trades = [new("trade-ui-test", "Lina", "wooden_axe", 1, "wood", 3, "open", null)],
        };
        var storeMap = buildingMap with { PlacedBuildings = [store], ProductionJobs = [] };
        RenderBuildingCard(storeMap);
        facts = string.Join('\n', buildingFacts.GetChildren().OfType<Label>().Select(label => label.Text));
        quickText = string.Join('\n', buildingQuickStatus.FindChildren("*", "Label", owned: false)
            .OfType<Label>().Select(label => label.Text));
        if (!facts.Contains("1 Wooden axe for 3 Wood", StringComparison.Ordinal) ||
            !facts.Contains("waiting for both traders at the shop", StringComparison.Ordinal) ||
            !facts.Contains("household stock and other uses remain private", StringComparison.Ordinal) ||
            facts.Contains("Permanent residents", StringComparison.Ordinal) ||
            facts.Contains("Family limit", StringComparison.Ordinal) ||
            facts.Contains("Expansion", StringComparison.Ordinal) ||
            !quickText.Contains("1 customer exchange waiting", StringComparison.Ordinal) ||
            BuildingSprites.KindFor(["store", "storage"]) != BuildingKind.Store)
            throw new InvalidOperationException("A shop card must show exact terms, transaction progress and limited customer access.");
        var busyStore = store with
        {
            Trades = Enumerable.Range(1, 8).Select(index => new OwnerWorldBusinessTrade(
                "trade-ui-" + index, "Customer " + index, "wooden_pickaxe", 1, "wood", 3, "open", null)).ToArray(),
        };
        RenderBuildingCard(storeMap with { PlacedBuildings = [busyStore] });
        ApplyResponsiveLayout();
        for (var frame = 0; frame < 3; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (!GetViewportRect().Grow(1).Encloses(buildingDetailsPanel.GetGlobalRect()) ||
            buildingDetailsContent.Size.Y <= buildingDetailsScroll.Size.Y ||
            buildingDetailsScroll.GetVScrollBar().MaxValue <= buildingDetailsScroll.GetVScrollBar().Page)
            throw new InvalidOperationException($"Eight shop exchanges must scroll inside Building Details: {buildingDetailsPanel.GetGlobalRect()}.");
        buildingDetailsScroll.ScrollVertical = int.MaxValue;
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (buildingDetailsScroll.ScrollVertical <= 0 ||
            !buildingDetailsPanel.GetGlobalRect().Encloses(buildingDetailsHeader.GetGlobalRect()))
            throw new InvalidOperationException("Shop history must remain reachable while the Building Details header stays visible.");
        RenderBuildingCard(storeMap with
        {
            PlacedBuildings = [store with { Trades = [store.Trades[0] with { Status = "settled" }] }],
        });
        for (var frame = 0; frame < 3; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        facts = string.Join('\n', buildingFacts.GetChildren().OfType<Label>().Select(label => label.Text));
        quickText = string.Join('\n', buildingQuickStatus.FindChildren("*", "Label", owned: false)
            .OfType<Label>().Select(label => label.Text));
        if (!facts.Contains("completed · purchase carried away, payment stored here", StringComparison.Ordinal) ||
            quickText.Contains("customer exchange waiting", StringComparison.Ordinal) ||
            !GetViewportRect().Grow(1).Encloses(buildingDetailsPanel.GetGlobalRect()))
            throw new InvalidOperationException("Settling a shop exchange must refresh its outcome and remove the waiting count without hiding Details.");

        // Escape closes open top-bar panels first, so keep Filters out of the way.
        var filtersWereOpen = filtersPanel.Visible;
        filtersPanel.Hide();
        if (!HandleEscape() || buildingDetailsPanel.Visible || !buildingQuickCard.Visible)
            throw new InvalidOperationException("Escape in building Details must go back to the quick card.");
        if (!HandleEscape() || buildingQuickCard.Visible || terrainLayer.SelectedBuilding is not null)
            throw new InvalidOperationException("Escape on a building's quick card must close it and clear the outline.");

        filtersPanel.Visible = filtersWereOpen;

        SelectBuilding(house.InstanceId);
        selectedInhabitantId = smith.Id;
        RenderSelectedInhabitantCard(buildingMap);
        if (buildingQuickCard.Visible || selectedBuildingId is not null || !selectedInhabitantCard.Visible)
            throw new InvalidOperationException("Choosing an agent must replace a building's quick card with theirs.");
        ClearInhabitantSelection();
        SelectBuilding(house.InstanceId);
        RenderBuildingCard(baseMap with { PlacedBuildings = baseMap.PlacedBuildings.Where(item => item.InstanceId != house.InstanceId).ToArray() });
        if (buildingQuickCard.Visible || selectedBuildingId is not null)
            throw new InvalidOperationException("A building that is gone must close its card.");
        var fruitSlot = new ItemSlot { IconSize = 32, Named = true };
        fruitSlot.SetItem("fruit", 3, "Fruit");
        if (fruitSlot.TooltipText != "Fruit × 3" || fruitSlot.CustomMinimumSize.Y <= fruitSlot.CustomMinimumSize.X - 8)
            throw new InvalidOperationException("A named item slot must leave room for its name and say what it holds.");
        fruitSlot.Free();
        VerifyBuildingManagementRefresh(baseMap);
        await VerifyMarketCardsAsync(baseMap);
    }

    private async Task VerifyMarketCardsAsync(OwnerWorldSnapshot baseMap)
    {
        var hall = new OwnerWorldPlacedBuilding("market-ui-hall", "test/market", new(4, 1), 10,
            "Market", ["market"], 2, 2, "town:first", Entrance: new(5, 3));
        var north = new OwnerWorldPlacedBuilding("market-ui-north", "test/stall", new(3, 4), 10,
            "Market stall", ["market_stall"], TownId: "town:first", Entrance: new(3, 3));
        var south = north with { InstanceId = "market-ui-south", Position = new(3, 5), Entrance = new(3, 6) };
        var trade = new OwnerWorldMarketTrade("market-ui-offer", "seller:one", "Sam", "household:sam",
            "Sam's household", "household:sam", "Sam's household", "buyer:two", "Lina",
            "berries", 2, "wood", 1, "open", null, BuyerAccepted: true);
        var first = new OwnerWorldMarketStall(north.InstanceId, 0, north.Position, "seller:one", "Sam", 11,
            [new("earlier-grain", null, "seller:earlier", "Sela", "grain", 3, 3),
                new("current-berries", null, "household:sam", "Sam's household", "berries", 2, 0)], [trade]);
        var second = new OwnerWorldMarketStall(south.InstanceId, 4, south.Position, null, null, null, [], []);
        var market = new OwnerWorldMarket("market-ui", "market-project-ui", hall.InstanceId, hall.Position,
            new(2, 3), 7, 4, [first, second]);
        var approval = new OwnerCivicProposal("market-approval-ui", "town_project", "Build our Market", "passed", 3, 0, 3, 20);
        var completed = new OwnerWorldTownProject(market.ProjectId, approval.Id, "First Market", "seller:one", "Sam",
            hall.DefinitionId, "Market", hall.Position, hall.Entrance!, 2, 2,
            [new("wood", 24, 24), new("stone", 8, 8), new("fiber", 4, 4)], 10, 10,
            "completed", null, hall.InstanceId, approval);
        var extension = completed with
        {
            Id = "extra-stall-ui",
            Name = "Another stall",
            DefinitionId = north.DefinitionId,
            DisplayName = "Market stall",
            Width = 1,
            Height = 1,
            Site = new(4, 4),
            Entrance = new(4, 3),
            Materials = [new("wood", 4, 0), new("fiber", 2, 0)],
            WorkDone = 0,
            WorkRequired = 3,
            Stage = "supplying",
            Blocker = "More Town materials are needed",
            CompletedBuildingId = null,
        };
        var town = baseMap.Towns[0] with { Markets = [market], Projects = [completed, extension] };
        var map = baseMap with
        {
            WorldId = "market-ui-world",
            WorldTick = 30,
            PackedTerrain = new(12, 10, "terrain-kind-v1", Convert.ToBase64String(new byte[120])),
            PackedMapLayers = null,
            MapLayersDigest = null,
            Tiles = Enumerable.Range(0, 120).Select(index => new OwnerWorldTile(index % 12, index / 12, "meadow")).ToArray(),
            PlacedBuildings = [hall, north, south],
            ProductionJobs = [],
            Towns = [town],
            Inhabitants = [MarketPerson("seller:one", "Sam", north.Entrance!),
                MarketPerson("buyer:two", "Lina", south.Entrance!)],
        };
        ClearBuildingSelection();
        RenderMap(map);
        RenderTownList(map);
        var labels = TownListText();
        if (!labels.Contains("2 stalls built · 1 borrowed", StringComparison.Ordinal) ||
            !labels.Contains("Stall 1 · borrowed by Sam", StringComparison.Ordinal) ||
            !labels.Contains("Stall 5 · free to borrow", StringComparison.Ordinal) ||
            !labels.Contains("3 Grain · owner: Sela", StringComparison.Ordinal) ||
            !labels.Contains("0 / 4 Wood", StringComparison.Ordinal) ||
            !labels.Contains("0 / 3 units", StringComparison.Ordinal) ||
            !labels.Contains("More Town materials are needed", StringComparison.Ordinal) ||
            labels.Contains("8 stalls built", StringComparison.Ordinal) ||
            BuildingSprites.KindFor(hall.Tags) != BuildingKind.Market ||
            BuildingSprites.KindFor(north.Tags) != BuildingKind.MarketStall)
            throw new InvalidOperationException("World Info must show actual paid stalls and preserve earlier stock owners separately from the borrower.");

        SelectBuilding(north.InstanceId);
        RenderBuildingCard(map);
        buildingDetailsButton.EmitSignal(BaseButton.SignalName.Pressed);
        RenderBuildingCard(map);
        for (var frame = 0; frame < 3; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var facts = Facts();
        if (!facts.Contains("3 Grain · owner: Sela", StringComparison.Ordinal) ||
            !facts.Contains("Sam → Lina: 2 Berries for 1 Wood", StringComparison.Ordinal) ||
            !facts.Contains("seller decision pending · buyer agreed", StringComparison.Ordinal) ||
            !facts.Contains("Goods: Sam's household · payment: Sam's household", StringComparison.Ordinal) ||
            buildingDetailsStorage.Visible ||
            !GetViewportRect().Grow(1).Encloses(buildingDetailsPanel.GetGlobalRect()))
            throw new InvalidOperationException("The stall card must show exact exchange terms and recorded owners without treating private ground goods as Town storage.");

        var refused = map with
        {
            Towns = [town with { Markets = [market with
            {
                Stalls = [first with { Trades = [trade with { Status = "cancelled", CancellationReason = "One of the traders cancelled the exchange." }] }, second],
            }] }],
        };
        RenderTownList(refused);
        RenderBuildingCard(refused);
        if (!Facts().Contains("cancelled · One of the traders cancelled the exchange.", StringComparison.Ordinal) ||
            Facts().Contains("seller decision pending", StringComparison.Ordinal))
            throw new InvalidOperationException("A cancelled exchange must refresh the stall from the pending answer to its recorded reason.");

        var settled = first with
        {
            SellerId = "seller:new",
            SellerName = "Mika",
            OccupiedTick = 30,
            Stock = [first.Stock[0], new("actual-payment", null, "household:sam", "Sam's household", "wood", 1, 1)],
            Trades = [trade with { Status = "settled", SellerAccepted = true }],
        };
        var changed = map with { Towns = [town with { Markets = [market with { Stalls = [settled, second] }] }] };
        // A changed observation at the same tick must refresh both caches.
        RenderTownList(changed);
        RenderBuildingCard(changed);
        facts = Facts();
        var quick = string.Join('\n', buildingQuickStatus.FindChildren("*", nameof(Label), owned: false)
            .OfType<Label>().Select(label => label.Text));
        if (!TownListText().Contains("borrowed by Mika", StringComparison.Ordinal) ||
            !facts.Contains("completed · buyer carries the purchase", StringComparison.Ordinal) ||
            !facts.Contains("1 Wood · owner: Sam's household", StringComparison.Ordinal) ||
            !facts.Contains("3 Grain · owner: Sela", StringComparison.Ordinal) ||
            facts.Contains("owner: Mika", StringComparison.Ordinal) ||
            quick.Contains("1 exchange waiting", StringComparison.Ordinal))
            throw new InvalidOperationException("A new borrower and settled exchange must refresh without reassigning earlier goods or the seller's payment.");

        var history = changed with
        {
            Towns = [town with { Markets = [market with { RemovedTick = 30, Stalls = [settled, second] }] }],
        };
        RenderTownList(history);
        RenderBuildingCard(history);
        if (!TownListText().Contains("Market removed", StringComparison.Ordinal) ||
            !Facts().Contains("stalls inactive", StringComparison.Ordinal) ||
            !Facts().Contains("3 Grain · owner: Sela", StringComparison.Ordinal) ||
            TownListText().Contains("free to borrow", StringComparison.Ordinal))
            throw new InvalidOperationException("Inactive Market history must retain goods ownership without advertising borrowing permission.");
        var description = WorldEventText.Describe(new(1, 30, "market_trade_offered",
            "town:first|market-ui|market-ui-north|seller:one|buyer:two|market-ui-offer", north.Position),
            map);
        if (!description.Contains("Sam", StringComparison.Ordinal) || !description.Contains("Lina", StringComparison.Ordinal) ||
            !description.Contains("exact terms", StringComparison.Ordinal))
            throw new InvalidOperationException("Market event descriptions must retain full pipe-delimited trader identities.");
        ClearBuildingSelection();
        RenderMap(baseMap);
        RenderTownList(baseMap);

        string Facts() => string.Join('\n', buildingFacts.GetChildren().OfType<Label>().Select(label => label.Text));

        static OwnerWorldInhabitant MarketPerson(string id, string name, OwnerWorldPosition position) =>
            new(id, name, "active", position, 8_000, [], [], new("idle", null, null, [], string.Empty),
                new(position, [position], [position]), false);
    }

    private void VerifyBuildingManagementRefresh(OwnerWorldSnapshot baseMap)
    {
        var previousRegistration = registration;
        var previousKey = deviceKey;
        var previousInvalid = registeredEndpointInvalid;
        var previousCi = System.Environment.GetEnvironmentVariable("CI");
        System.Environment.SetEnvironmentVariable("CI", "true");
        using var signer = OwnerDeviceKey.CreateEphemeralForContinuousIntegration();
        try
        {
            registration = new(new OwnerAuthorityIdentity("building-smoke", baseMap.WorldId),
                "building-smoke-device", signer.PublicKeyFingerprint, "http://127.0.0.1/");
            deviceKey = signer;
            registeredEndpointInvalid = false;
            var workshop = new OwnerWorldPlacedBuilding("choice-workshop", "test/workshop", new(1, 1), 0,
                "Workshop", ["workshop"], TownId: "town:first", HouseholdId: "household:one")
            { AllowsHouseholdOwner = true };
            var second = workshop with { InstanceId = "second-workshop", Position = new(2, 1) };
            var map = baseMap with
            {
                PlacedBuildings = [workshop, second],
                Stockpiles = [new("household:one", "Current", []), new("household:two", "Alpha", []),
                    new("household:three", "Beta", [])],
                ProductionJobs = [],
            };
            ClearBuildingSelection();
            RenderMap(map);
            SelectBuilding(workshop.InstanceId);
            OpenBuildingDetails();
            if (!buildingManagementSection.Visible || buildingManagementChoice.ItemCount != 3)
                throw new InvalidOperationException("A paired owner must receive the host's household choices and the unowned option.");
            buildingManagementChoice.Select(1);
            RenderBuildingCard(map with { WorldTick = map.WorldTick + 1 });
            if (ChosenOwner() != "household:three")
                throw new InvalidOperationException("An observation refresh must preserve the owner's chosen household.");
            var reordered = map with
            {
                Stockpiles = [new("household:one", "Current", []), new("household:two", "Zed", []),
                    new("household:three", "Aaron", [])],
            };
            RenderBuildingCard(reordered);
            if (ChosenOwner() != "household:three" || buildingManagementChoice.Selected != 0)
                throw new InvalidOperationException("Owner choices must survive reordered display names by household ID.");
            buildingManagementChoice.Select(2);
            RenderBuildingCard(map);
            if (ChosenOwner() != string.Empty)
                throw new InvalidOperationException("The explicit no-household choice must survive a refresh.");
            SelectBuilding(second.InstanceId);
            if (ChosenOwner() != "household:two")
                throw new InvalidOperationException("Choosing another building must reset its owner choice.");
            buildingManagementChoice.Select(1);
            RenderBuildingCard(map with { Stockpiles = map.Stockpiles.Where(item => item.OwnerId != "household:three").ToArray() });
            if (ChosenOwner() != "household:two")
                throw new InvalidOperationException("A removed owner choice must fall back to a current host-provided option.");
            RenderBuildingCard(map with
            {
                PlacedBuildings = [second with { Tags = ["house"], HouseholdId = null }],
                Stockpiles = [],
            });
            if (buildingManagementChoice.ItemCount != 0 || buildingManagementApply.Visible)
                throw new InvalidOperationException("A building without current owner options must not offer reassignment.");
            RenderBuildingCard(map);
            if (ChosenOwner() != "household:two")
                throw new InvalidOperationException("Returning owner options must start with a current choice.");

            buildingRemoveButton.EmitSignal(BaseButton.SignalName.Pressed);
            RenderBuildingCard(map with
            {
                PlacedBuildings = [workshop, second with { HouseholdId = "household:three" }],
            });
            if (!buildingRemoveConfirmation.Visible || pendingBuildingRemoval is not
                { InstanceId: "second-workshop", ExpectedTownId: "town:first", ExpectedHouseholdId: "household:one" } || pendingBuildingRemoval.WorldId != map.WorldId)
                throw new InvalidOperationException("Removal must retain the owner record originally confirmed so the host can reject a stale change.");
            buildingRemoveConfirmation.EmitSignal(ConfirmationDialog.SignalName.Canceled);
            if (pendingBuildingRemoval is not null || pendingBuildingRemovalWorldId is not null)
                throw new InvalidOperationException("Canceling removal must clear its retained action.");
            buildingRemoveButton.EmitSignal(BaseButton.SignalName.Pressed);
            RenderBuildingCard(map with { WorldId = "different-building-world" });
            if (pendingBuildingRemoval is not null || buildingRemoveConfirmation.Visible || ChosenOwner() != "household:two")
                throw new InvalidOperationException("A world change must cancel removal and reset its owner choice.");
            RenderBuildingCard(map);
            buildingRemoveButton.EmitSignal(BaseButton.SignalName.Pressed);
            RenderBuildingCard(map with { PlacedBuildings = [workshop] });
            if (pendingBuildingRemoval is not null || selectedBuildingId is not null || buildingRemoveConfirmation.Visible)
                throw new InvalidOperationException("A disappeared building must cancel its removal confirmation.");
        }
        finally
        {
            ClearBuildingSelection();
            registration = previousRegistration;
            deviceKey = previousKey;
            registeredEndpointInvalid = previousInvalid;
            System.Environment.SetEnvironmentVariable("CI", previousCi);
        }

        string? ChosenOwner() => buildingManagementChoice.Selected < 0 ? null :
            buildingManagementChoice.GetItemMetadata(buildingManagementChoice.Selected).AsString();
    }
}

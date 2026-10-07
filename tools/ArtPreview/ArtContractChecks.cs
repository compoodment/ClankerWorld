using ClankerWorld.GodotClient.UI;
using Godot;

namespace ArtPreview;

/// <summary>Checks approved art integration and the current scene without writing pictures.</summary>
internal static class ArtContractChecks
{
    public static void Run()
    {
        var current = new ArtSet();
        CheckApprovedBuildings(current);
        CheckApprovedNature(current);
        CheckApprovedItems();
        CheckApprovedHandcarts();
        CheckApprovedBoatsAndPorts();
        CheckApprovedAnimals();
        CheckApprovedYard();
        CheckApprovedNeglect();
        CheckApprovedConstruction();
        CheckApprovedNeglectedLanternsAndBridges();
        foreach (var size in new[] { 16, 32 })
        {
            foreach (var (facing, frame) in new[] { (6, AgentFrame.Walk2), (4, AgentFrame.Carry), (2, AgentFrame.Talk) })
                Equal(current.Agent(0, 2, facing, (int)frame, size), AgentSprites.Sprite(0, 2, facing, frame, size),
                    $"The current scene must show the observed facing and frame at {size} px.");
            foreach (var eastWest in new[] { false, true })
            {
                var deck = current.Bridge?.Invoke(eastWest, size)
                    ?? throw new InvalidOperationException("The current scene must use the approved bridge deck.");
                Equal(deck, RoadSprites.BridgeDeck(eastWest, size), "Current bridge art must match the client.");
            }
            var range = SceneSpec.MountainRange().Map();
            var relief = current.Relief?.Invoke(range, size)
                ?? throw new InvalidOperationException("The current scene must show the client's mountain relief.");
            Equal(relief, ReliefRenderer.Render(range, new Rect2I(0, 0, range.Width, range.Height), size)!,
                "Current relief must match the client.");
        }
        var crossing = new SceneSpec { Width = 5, Height = 5 };
        crossing.Roads.Add(new(1, 2));
        crossing.Bridges[new(2, 2)] = true;
        if (SceneComposer.RoadLinksAt(crossing, 1, 2, true) != (RoadLinks.Road | RoadLinks.East))
            throw new InvalidOperationException("A Road must join an aligned bridge deck.");
        crossing.Bridges[new(2, 2)] = false;
        if (SceneComposer.RoadLinksAt(crossing, 1, 2, true) != RoadLinks.Road)
            throw new InvalidOperationException("A Road must not join the side of a perpendicular deck.");
        crossing.Roads.Clear();
        if (SceneComposer.RoadLinksAt(crossing, 1, 2, true) != RoadLinks.None)
            throw new InvalidOperationException("A deck must not create a Road piece on an empty bank.");
        Console.WriteLine("Current-art contract checks passed.");
    }

    private static void CheckApprovedBuildings(ArtSet current)
    {
        var approved = new ArtSet();
        new Proposed.Buildings.BuildingsProposal().Apply(approved);
        var samples = new (BuildingKind Kind, int Width, int Height)[]
        {
            (BuildingKind.Clinic, 1, 1), (BuildingKind.Clinic, 1, 2), (BuildingKind.Clinic, 2, 1),
            (BuildingKind.Restaurant, 1, 2), (BuildingKind.Restaurant, 2, 1), (BuildingKind.Restaurant, 2, 2),
            // Existing families exercise the shared roof, yard and door rendering paths.
            (BuildingKind.House, 1, 1), (BuildingKind.House, 1, 2), (BuildingKind.House, 2, 2),
            (BuildingKind.Warehouse, 2, 2), (BuildingKind.Warehouse, 2, 3), (BuildingKind.Warehouse, 3, 2),
            (BuildingKind.Farmhouse, 1, 1), (BuildingKind.Farmhouse, 1, 2),
            (BuildingKind.Blacksmith, 1, 2), (BuildingKind.Blacksmith, 2, 2),
            (BuildingKind.Silo, 1, 1), (BuildingKind.TailorShop, 1, 1), (BuildingKind.TailorShop, 2, 2),
            (BuildingKind.Store, 1, 1), (BuildingKind.Store, 1, 2), (BuildingKind.Store, 2, 1),
            (BuildingKind.Workshop, 2, 2), (BuildingKind.Generic, 1, 1),
        };
        var comparisons = 0;
        foreach (var (kind, width, height) in samples)
        {
            // The proposal adapter otherwise falls back to the current client,
            // which would make a missing approved mapping compare to itself.
            if (!Proposed.Buildings.BuildingsProposal.HasApprovedDrawing(kind))
                throw new InvalidOperationException($"The approved building proposal has no drawing for {kind}.");
            foreach (var size in new[] { 16, 32 })
            {
                Equal(BuildingSprites.Render(kind, width, height, size),
                    approved.Building(kind, width, height, size, BuildingDoor.Default),
                    $"{kind} {width}x{height} with the default door must match its approved drawing at {size} px.");
                comparisons++;
                foreach (var side in Enum.GetValues<DoorSide>())
                {
                    var extent = side is DoorSide.North or DoorSide.South ? width : height;
                    // Null selects the middle of that side; explicit offsets also cover both ends.
                    for (var tile = -1; tile < extent; tile++)
                    {
                        var door = new BuildingDoor(side, tile < 0 ? null : tile);
                        Equal(current.Building(kind, width, height, size, door),
                            approved.Building(kind, width, height, size, door),
                            $"{kind} {width}x{height}, {side} door {door.Tile?.ToString() ?? "middle"}, must match its approved drawing at {size} px.");
                        comparisons++;
                    }
                }
            }
        }
        Console.WriteLine($"Approved building art: {comparisons} exact RGBA comparisons.");
    }

    private static void CheckApprovedNature(ArtSet current)
    {
        var approved = new ArtSet();
        new Proposed.Nature.NatureProposal().Apply(approved);
        var sprites = new[]
        {
            NatureSprite.HerbPatch, NatureSprite.HerbPatchPicked,
            NatureSprite.BerryBush, NatureSprite.BerryBushPicked,
            NatureSprite.WildGreens, NatureSprite.WildGreensPicked,
            NatureSprite.FiberPlant, NatureSprite.FiberPlantHarvested,
            NatureSprite.Reeds, NatureSprite.ReedsHarvested,
            NatureSprite.StoneOutcrop, NatureSprite.StoneOutcropDepleted,
            NatureSprite.IronOutcrop, NatureSprite.IronOutcropDepleted,
            NatureSprite.GoldOutcrop, NatureSprite.GoldOutcropDepleted,
            NatureSprite.DiamondOutcrop, NatureSprite.DiamondOutcropDepleted,
            NatureSprite.ClayBank, NatureSprite.ClayBankDepleted,
            NatureSprite.Broadleaf, NatureSprite.BroadleafStump, NatureSprite.BroadleafSapling,
            NatureSprite.Conifer, NatureSprite.ConiferStump, NatureSprite.ConiferSapling,
            NatureSprite.Regrowing, NatureSprite.Depleted,
        };
        foreach (var sprite in sprites)
        {
            if (!Proposed.Nature.NatureProposal.HasApprovedDrawing(sprite))
                throw new InvalidOperationException($"The approved nature proposal has no drawing for {sprite}.");
            foreach (var size in new[] { 16, 32 })
                Equal(current.Nature(sprite, size), approved.Nature(sprite, size),
                    $"{sprite} must match its approved drawing at {size} px.");
        }
        Console.WriteLine($"Approved nature art: {sprites.Length * 2} exact RGBA comparisons.");
    }

    private static void CheckApprovedItems()
    {
        var aliases = new (string Item, string Drawing)[]
        {
            ("potatoes", "potato"), ("cultivated_green_seed", "green_seed"),
            ("medicinal_herbs", "herbs"), ("diamond_ornament", "ornament"), ("simple_meal", "meal"),
            // The approved raw nuggets now show gold ore; the crude wooden tools keep the wooden tool icons (October 7).
            ("gold_ore", "gold"), ("crude_wooden_axe", "wooden_axe"), ("crude_wooden_pickaxe", "wooden_pickaxe"),
        };
        foreach (var (item, drawing) in aliases)
        {
            if (!Proposed.Items.ItemsProposal.HasApprovedDrawing(drawing))
                throw new InvalidOperationException($"The approved item proposal has no independent drawing for {drawing}.");
            foreach (var size in new[] { 16, 32, 48 })
            {
                var expected = Proposed.Items.ItemsProposal.Icon(drawing).Duplicate();
                expected.Resize(size, size, Image.Interpolation.Nearest);
                Equal(ItemIcons.Render(item, size), expected,
                    $"The actual item {item} must use the approved {drawing} drawing at {size} px.");
            }
        }
        Console.WriteLine($"Approved item aliases: {aliases.Length * 3} exact RGBA comparisons.");
        var drawn = 0;
        foreach (var kind in Proposed.Items.ItemsRound4Proposal.Kinds)
            foreach (var size in new[] { 16, 32, 48 })
            {
                var expected = Proposed.Items.ItemsRound4Proposal.IconFor(kind).Duplicate();
                expected.Resize(size, size, Image.Interpolation.Nearest);
                Equal(ItemIcons.Render(kind, size), expected, $"The item {kind} must use its approved round-four icon at {size} px.");
                drawn++;
            }
        Console.WriteLine($"Approved round-four item icons: {drawn} exact RGBA comparisons.");
    }

    private static void CheckApprovedHandcarts()
    {
        // Keep the proposal independent: comparing the client with another call
        // to its own drawing would not catch a changed palette, pose or pixel.
        var approved = new Proposed.Buildings.BuildingsProposal().Render()
            .Where(entry => entry.Id.StartsWith("handcart.", StringComparison.Ordinal) &&
                entry.Id.EndsWith(".sprite", StringComparison.Ordinal))
            .ToDictionary(entry => entry.Id, entry => entry.Image, StringComparer.Ordinal);
        if (approved.Count != 18)
            throw new InvalidOperationException("The approved handcart reference must contain all eighteen drawings.");
        string[] directions = ["S", "SW", "W", "NW", "N", "NE", "E", "SE"];
        for (var facing = 0; facing < directions.Length; facing++)
            foreach (var loaded in new[] { false, true })
            {
                var id = $"handcart.{(loaded ? "loaded" : "empty")}.{directions[facing]}.sprite";
                Equal(HandcartSprites.Sprite(facing, loaded, pulled: false), approved[id],
                    $"The playable cart must match the approved {id} drawing pixel for pixel.");
            }
        foreach (var facing in new[] { 6, 7 })
        {
            var id = $"handcart.pulled.{directions[facing]}.sprite";
            Equal(HandcartSprites.Sprite(facing, loaded: true, pulled: true), approved[id],
                $"The playable cart must match the approved {id} drawing pixel for pixel.");
        }
    }

    private static void CheckApprovedBoatsAndPorts()
    {
        var approved = new Proposed.Buildings.BuildingsProposal().Render().ToDictionary(entry => entry.Id, entry => entry.Image, StringComparer.Ordinal);
        string[] directions = ["S", "SW", "W", "NW", "N", "NE", "E", "SE"];
        for (var facing = 0; facing < directions.Length; facing++)
            foreach (var rowing in new[] { false, true })
            {
                var id = $"boat.{(rowing ? "rowing" : "moored")}.{directions[facing]}.sprite";
                var reference = Proposed.Buildings.BuildingsProposal.ApprovedBoat(facing, rowing);
                Equal(BoatSprites.Sprite(facing, rowing), reference, "The playable boat must match the independent approved " + id);
                Console.WriteLine($"Boat reference {facing} {rowing}: {Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(reference.GetData()))}");
            }
        foreach (var (w, h, side, letter) in new[] { (2, 4, DoorSide.North, "N"), (4, 2, DoorSide.East, "E"), (2, 4, DoorSide.South, "S"), (4, 2, DoorSide.West, "W") })
        {
            var reference = approved[$"Port.{w}x{h}.{letter}.sprite"];
            Equal(BuildingSprites.Render(BuildingKind.Port, w, h, 32, new(side, 1)), reference, "The playable Port must match the independent approved rotation.");
            Console.WriteLine($"Port reference {letter}: {Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(reference.GetData()))}");
        }
    }

    /// <summary>Every animal, young animal, horse state and facing in the client matches the approved drawing at 32 and 16 px.</summary>
    private static void CheckApprovedAnimals()
    {
        foreach (var size in new[] { 16, 32 })
            for (var facing = 0; facing < 8; facing++)
            {
                void Same(Image client, Image approved, string what) =>
                    Equal(client, approved, $"The client's {what} facing {facing} at {size} px must match the approved animal art.");
                Same(AnimalSprites.Sprite("chicken", facing, false, false, size: size), Proposed.Animals.AnimalsProposal.Chicken(facing, size), "hen");
                Same(AnimalSprites.Sprite("chicken", facing, true, false, size: size), Proposed.Animals.AnimalsProposal.Chick(facing, size), "chick");
                Same(AnimalSprites.Sprite("sheep", facing, false, false, size: size), Proposed.Animals.AnimalsProposal.Sheep(facing, false, size), "sheep");
                Same(AnimalSprites.Sprite("sheep", facing, false, false, shorn: true, size: size), Proposed.Animals.AnimalsProposal.Sheep(facing, true, size), "shorn sheep");
                Same(AnimalSprites.Sprite("sheep", facing, true, false, shorn: true, size: size), Proposed.Animals.AnimalsProposal.Lamb(facing, size), "lamb");
                Same(AnimalSprites.Sprite("cow", facing, false, false, size: size), Proposed.Animals.AnimalsProposal.Cow(facing, size), "cow");
                Same(AnimalSprites.Sprite("cow", facing, true, false, size: size), Proposed.Animals.AnimalsProposal.Calf(facing, size), "calf");
                Same(AnimalSprites.Sprite("horse", facing, false, false, size: size), Proposed.Animals.AnimalsProposal.Horse(facing, false, size, saddled: false), "bare horse");
                Same(AnimalSprites.Sprite("horse", facing, false, false, saddled: true, size: size), Proposed.Animals.AnimalsProposal.Horse(facing, false, size), "saddled horse");
                Same(AnimalSprites.Sprite("horse", facing, false, true, size: size), Proposed.Animals.AnimalsProposal.Horse(facing, true, size), "ridden horse");
                Same(AnimalSprites.Sprite("horse", facing, true, true, saddled: true, size: size), Proposed.Animals.AnimalsProposal.Foal(facing, size), "foal");
            }
    }

    /// <summary>The client's animal yard is the approved option A at every footprint and door side, halved at 16 px.</summary>
    private static void CheckApprovedYard()
    {
        foreach (var (width, height) in new[] { (2, 2), (2, 4), (4, 2), (4, 4) })
            foreach (var side in new[] { DoorSide.South, DoorSide.North, DoorSide.East, DoorSide.West })
                foreach (var tile in new int?[] { null, 0, 1 })
                {
                    var door = new BuildingDoor(side, tile);
                    var approved = Proposed.Yards.YardsProposal.Draw(Proposed.Yards.YardsProposal.Option.A, width, height, door);
                    Equal(BuildingSprites.Render(BuildingKind.AnimalYard, width, height, 32, door), approved,
                        $"The client's {width}×{height} animal yard with a {side} gate must match approved option A.");
                    approved.Resize(width * 16, height * 16, Image.Interpolation.Nearest);
                    Equal(BuildingSprites.Render(BuildingKind.AnimalYard, width, height, 16, door), approved,
                        $"The client's 16 px {width}×{height} animal yard must be the approved yard halved.");
                }
    }

    /// <summary>The client's neglected and falling-apart buildings match the approved abandoned looks pixel for pixel.</summary>
    private static void CheckApprovedNeglect()
    {
        var subjects = Proposed.BuildingStates.States.Subjects().ToDictionary(subject => subject.Id);
        foreach (var (id, kind, width, height, door) in StateCases())
            foreach (var ruin in new[] { false, true })
            {
                var neglect = ruin ? BuildingNeglect.FallingApart : BuildingNeglect.Neglected;
                var approved = Proposed.BuildingStates.States.AbandonedArt(subjects[id], ruin);
                Equal(BuildingSprites.Render(kind, width, height, 32, door, neglect), approved,
                    $"The client's {neglect} {id} must match the approved abandoned look.");
                approved.Resize(width * 16, height * 16, Image.Interpolation.Nearest);
                Equal(BuildingSprites.Render(kind, width, height, 16, door, neglect), approved,
                    $"The client's 16 px {neglect} {id} must be the approved look halved.");
            }
    }

    /// <summary>The client's construction stages match the approved stages pixel for pixel.</summary>
    private static void CheckApprovedConstruction()
    {
        var subjects = Proposed.BuildingStates.States.Subjects().ToDictionary(subject => subject.Id);
        foreach (var (id, kind, width, height, door) in StateCases())
            foreach (var stage in new[] { 1, 2, 3 })
            {
                var approved = Proposed.BuildingStates.States.StageArt(subjects[id], stage);
                Equal(BuildingSprites.RenderConstruction(kind, width, height, 32, door, stage), approved,
                    $"The client's {id} at construction stage {stage} must match the approved stage.");
                approved.Resize(width * 16, height * 16, Image.Interpolation.Nearest);
                Equal(BuildingSprites.RenderConstruction(kind, width, height, 16, door, stage), approved,
                    $"The client's 16 px {id} at construction stage {stage} must be the approved stage halved.");
            }
    }

    /// <summary>Every building the construction and abandoned looks were approved on, with the door it was drawn with.</summary>
    private static (string Id, BuildingKind Kind, int Width, int Height, BuildingDoor Door)[] StateCases()
    {
        var south = new BuildingDoor(DoorSide.South);
        return
        [
            ("house.1x1", BuildingKind.House, 1, 1, south), ("house.2x2", BuildingKind.House, 2, 2, south),
            ("farmhouse.1x1", BuildingKind.Farmhouse, 1, 1, south), ("farmhouse.2x2", BuildingKind.Farmhouse, 2, 2, south),
            ("warehouse.2x2", BuildingKind.Warehouse, 2, 2, south), ("blacksmith.1x2", BuildingKind.Blacksmith, 1, 2, south),
            ("tailor.1x1", BuildingKind.TailorShop, 1, 1, south), ("store.1x2", BuildingKind.Store, 1, 2, south),
            ("workshop.1x1", BuildingKind.Workshop, 1, 1, south), ("clinic.1x2", BuildingKind.Clinic, 1, 2, south),
            ("restaurant.2x2", BuildingKind.Restaurant, 2, 2, south), ("silo.1x1", BuildingKind.Silo, 1, 1, south),
            ("townhall.3x4", BuildingKind.TownHall, 3, 4, south), ("market.2x2", BuildingKind.Market, 2, 2, south),
            ("stall.1x1", BuildingKind.MarketStall, 1, 1, south), ("port.2x4", BuildingKind.Port, 2, 4, new BuildingDoor(DoorSide.South, 1)),
            ("yard.2x2", BuildingKind.AnimalYard, 2, 2, south),
        ];
    }

    /// <summary>The client's weathered street lanterns and bridges match the approved abandoned looks pixel for pixel.</summary>
    private static void CheckApprovedNeglectedLanternsAndBridges()
    {
        var subjects = Proposed.BuildingStates.States.Subjects().ToDictionary(subject => subject.Id);
        var east = new Vector2(1, 0);
        foreach (var ruin in new[] { false, true })
        {
            var neglect = ruin ? BuildingNeglect.FallingApart : BuildingNeglect.Neglected;
            foreach (var (id, style) in new[] { ("lantern.stone", LanternStyle.Stone), ("lantern.hanging", LanternStyle.Hanging) })
                Equal(BuildingSprites.NeglectedLantern(style, east, neglect), Proposed.BuildingStates.States.AbandonedArt(subjects[id], ruin),
                    $"The client's {neglect} {id} must match the approved abandoned look.");
            var bridge = Proposed.BuildingStates.States.AbandonedArt(subjects["bridge.3"], ruin);
            Equal(BuildingSprites.RenderNeglectedBridge(true, 3, 32, neglect), bridge,
                $"The client's {neglect} three-span bridge must match the approved abandoned look.");
            bridge.Resize(5 * 16, 16, Image.Interpolation.Nearest);
            Equal(BuildingSprites.RenderNeglectedBridge(true, 3, 16, neglect), bridge,
                $"The client's 16 px {neglect} bridge must be the approved look halved.");
        }

    }

    private static void Equal(Image actual, Image expected, string message)
    {
        if (actual.GetWidth() != expected.GetWidth() || actual.GetHeight() != expected.GetHeight())
            throw new InvalidOperationException($"{message} Actual dimensions {actual.GetWidth()}x{actual.GetHeight()}; expected {expected.GetWidth()}x{expected.GetHeight()}.");
        var actualBytes = actual.GetData();
        var expectedBytes = expected.GetData();
        if (actualBytes.AsSpan().SequenceEqual(expectedBytes)) return;
        var mismatch = Enumerable.Range(0, actualBytes.Length).First(index => actualBytes[index] != expectedBytes[index]);
        var pixel = mismatch / 4;
        throw new InvalidOperationException($"{message} First mismatch at ({pixel % actual.GetWidth()}, {pixel / actual.GetWidth()}), RGBA channel {mismatch % 4}: actual {actualBytes[mismatch]}, expected {expectedBytes[mismatch]}.");
    }
}

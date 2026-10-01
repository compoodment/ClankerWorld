using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

// LOCAL ONLY - never commit. Dumps generated art for visual review.
public partial class Main
{
    public override void _EnterTree()
    {
        if (OS.GetCmdlineUserArgs().Contains("--clean-hud", StringComparer.Ordinal))
            GetTree().ProcessFrame += () =>
            {
                if (!isInWorld) return;
                hudBar.Hide();
                hoverReadout.Hide();
                statusToast.Hide();
            };
        if (OS.GetCmdlineUserArgs().Any(arg => arg.StartsWith("--fs-out=", StringComparison.Ordinal)))
        {
            CallDeferred(nameof(RunFontSpecimen));
            return;
        }
        var logo = OS.GetCmdlineUserArgs().FirstOrDefault(arg => arg.StartsWith("--menu-logo=", StringComparison.Ordinal));
        if (logo is not null)
        {
            var logoShot = OS.GetCmdlineUserArgs().FirstOrDefault(arg => arg.StartsWith("--menu-shot=", StringComparison.Ordinal));
            CallDeferred(nameof(ApplyMenuLogo), logo["--menu-logo=".Length..], logoShot?["--menu-shot=".Length..] ?? "");
            return;
        }
        var clip = OS.GetCmdlineUserArgs().FirstOrDefault(arg => arg.StartsWith("--backdrop-clip=", StringComparison.Ordinal));
        if (clip is not null)
        {
            CallDeferred(nameof(RecordBackdropClip), clip["--backdrop-clip=".Length..]);
            return;
        }
        if (OS.GetCmdlineUserArgs().Contains("--menu-scene", StringComparer.Ordinal))
        {
            CallDeferred(nameof(StartMenuScene));
            return;
        }
        var menuBackground = OS.GetCmdlineUserArgs().FirstOrDefault(arg => arg.StartsWith("--menu-bg=", StringComparison.Ordinal));
        if (menuBackground is not null)
        {
            var shot = OS.GetCmdlineUserArgs().FirstOrDefault(arg => arg.StartsWith("--menu-shot=", StringComparison.Ordinal));
            CallDeferred(nameof(ApplyMenuBackground), menuBackground["--menu-bg=".Length..], shot?["--menu-shot=".Length..] ?? "");
            return;
        }
        if (OS.GetCmdlineUserArgs().Contains("--weather-preview", StringComparer.Ordinal))
        {
#if WEATHER_LAYER
            CallDeferred(nameof(StartWeatherPreview));
#endif
            return;
        }
        if (OS.GetCmdlineUserArgs().Contains("--icon-sheet", StringComparer.Ordinal))
        {
            var itemKinds = new[] { "wood", "stone", "clay", "iron_ore", "iron", "fiber", "grain", "flour", "seed", "food", "berries", "bread", "clothing", "wooden_axe", "wooden_pickaxe", "tool", "mystery" };
            var iconSheet = Image.CreateEmpty(itemKinds.Length * 56 + 8, 3 * 56 + 8, false, Image.Format.Rgba8);
            iconSheet.Fill(new Color("EFE4CC"));
            for (var i = 0; i < itemKinds.Length; i++)
            {
                foreach (var (size, row) in new[] { (48, 0), (32, 1), (16, 2) })
                {
                    var icon = ItemIcons.Render(itemKinds[i], size);
                    iconSheet.BlendRect(icon, new Rect2I(0, 0, size, size), new Vector2I(8 + i * 56 + (48 - size) / 2, 8 + row * 56 + (48 - size) / 2));
                }
            }
            var big = (Image)iconSheet.Duplicate();
            big.Resize(iconSheet.GetWidth() * 2, iconSheet.GetHeight() * 2, Image.Interpolation.Nearest);
            big.SavePng("/tmp/claude-0/-home-user-ClankerWorld/62ab178e-62a2-5836-a4b8-0ab0a61611f1/scratchpad/roads/border/icon-sheet.png");
            GetTree().Quit();
            return;
        }
        if (OS.GetCmdlineUserArgs().Contains("--door-sheet", StringComparer.Ordinal))
        {
            DumpDoorSheet();
            GetTree().Quit();
            return;
        }
        if (!OS.GetCmdlineUserArgs().Contains("--dump-art", StringComparer.Ordinal)) return;
        foreach (var size in new[] { 16, 32 })
        {
            NatureSprites.Atlas(size).GetImage().SavePng("/tmp/claude-0/-home-user-ClankerWorld/62ab178e-62a2-5836-a4b8-0ab0a61611f1/scratchpad/nature-" + size + ".png");
            TerrainTextures.Atlas(size).GetImage().SavePng("/tmp/claude-0/-home-user-ClankerWorld/62ab178e-62a2-5836-a4b8-0ab0a61611f1/scratchpad/atlas-" + size + ".png");
        }
        AgentSprites.Atlas(32).GetImage().SavePng("/tmp/claude-0/-home-user-ClankerWorld/62ab178e-62a2-5836-a4b8-0ab0a61611f1/scratchpad/agents-32.png");
        var kinds = Enum.GetValues<BuildingKind>();
        var sheet = Image.CreateEmpty(64 * 5 + 80, 32 * 2 * kinds.Length, false, Image.Format.Rgba8);
        sheet.Fill(new Color("5F8F5B"));
        for (var i = 0; i < kinds.Length; i++)
        {
            var a = BuildingSprites.Render(kinds[i], 2, 1, 32);
            sheet.BlendRect(a, new Rect2I(0, 0, 64, 32), new Vector2I(0, i * 64));
            var b = BuildingSprites.Render(kinds[i], 2, 2, 32);
            sheet.BlendRect(b, new Rect2I(0, 0, 64, 64), new Vector2I(80, i * 64));
            var c = BuildingSprites.Render(kinds[i], 1, 2, 32);
            sheet.BlendRect(c, new Rect2I(0, 0, 32, 64), new Vector2I(160, i * 64));
            var d = BuildingSprites.Render(kinds[i], 1, 1, 32);
            sheet.BlendRect(d, new Rect2I(0, 0, 32, 32), new Vector2I(208, i * 64));
            var e = BuildingSprites.Render(kinds[i], 2, 1, 16);
            sheet.BlendRect(e, new Rect2I(0, 0, 32, 16), new Vector2I(256, i * 64));
        }
        sheet.SavePng("/tmp/claude-0/-home-user-ClankerWorld/62ab178e-62a2-5836-a4b8-0ab0a61611f1/scratchpad/buildings.png");
        DumpTransitionPreview(32);
        DumpTransitionPreview(16);
        DumpWaterPreview(32, true);
        DumpWaterPreview(32, false);
        DumpWaterPreview(16, true);
#if WATER_BLOCKS
        WaterTextures.Atlas(32).GetImage().SavePng("/tmp/claude-0/-home-user-ClankerWorld/62ab178e-62a2-5836-a4b8-0ab0a61611f1/scratchpad/water-atlas-32.png");
#endif
        GetTree().Quit();
    
    }

    private static void DumpDoorSheet()
    {
        var kinds = new[] { BuildingKind.House, BuildingKind.Warehouse, BuildingKind.Farmhouse, BuildingKind.Blacksmith, BuildingKind.Workshop };
        var sizes = new (int W, int H)[] { (1, 1), (2, 2), (1, 1), (1, 2), (1, 1) };
        var doors = new[] { new BuildingDoor(DoorSide.South), new BuildingDoor(DoorSide.North, 0), new BuildingDoor(DoorSide.East, 0),
            new BuildingDoor(DoorSide.West, 0), new BuildingDoor(DoorSide.South, 1), new BuildingDoor(DoorSide.East, 1) };
        const int cell = 80;
        var sheet = Image.CreateEmpty(cell * doors.Length, cell * kinds.Length, false, Image.Format.Rgba8);
        sheet.Fill(new Color("5F8F5B"));
        for (var k = 0; k < kinds.Length; k++)
            for (var d = 0; d < doors.Length; d++)
            {
                var (w, h) = sizes[k];
                var door = doors[d];
                if (door.Tile > 0 && (door.Side is DoorSide.South or DoorSide.North ? w : h) <= door.Tile) continue;
                var image = BuildingSprites.Render(kinds[k], w, h, 32, door);
                sheet.BlendRect(image, new Rect2I(0, 0, w * 32, h * 32), new Vector2I(d * cell + 8, k * cell + 8));
            }
        var big = (Image)sheet.Duplicate();
        big.Resize(sheet.GetWidth() * 3, sheet.GetHeight() * 3, Image.Interpolation.Nearest);
        big.SavePng("/tmp/claude-0/-home-user-ClankerWorld/62ab178e-62a2-5836-a4b8-0ab0a61611f1/scratchpad/roads/door-sheet.png");
    }

    private static bool UseWaterBlocks = true;
    private static string[]? PreviewRows;
    private static string PreviewName = "transitions";

    private static void DumpWaterPreview(int size, bool blocks)
    {
        UseWaterBlocks = blocks;
        PreviewName = blocks ? "water-new" : "water-old";
        PreviewRows =
        [
            "CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC",
            "CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC",
            "CCCCCCCCCCCHHHHCCCCCCCCCCCCCCCCCCCCC",
            "CCCCCCCCCCHHSSHHCCCCCCCCCCCCCCCCCCCC",
            "CCCCCCCCCCHSSSSHHCCCCCCCCCCCCCCCCCCC",
            "CCCCCCCCCCCHSSSHCCCCCCCCCCCCCCCCCCCC",
            "CCCCCCCCCCCCHHCCCCCCCCCCCCCCCCCCCCCC",
            "CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC",
            "CCCSSSSSSSCCCCCCCCCCCCCCCCCCSSSSCCCC",
            "SSSSGGGGGSSSSSSSSSSSSSSSSSSSSGGSSSSS",
            "GGGGGGGGGGGGVVGGGGGGGGGGGGGGGGGGGGGG",
            "GGGGGGGGGGGGGVGGGGGGGGGGWWWWWGGGGGGG",
            "GGGGGGGGGGGGGVVVVGGGGGGWWWWWWWWGGGGG",
            "GGGGGGGGGGGGGGGGVGGGGGWWWWWWWWWWGGGG",
            "GGGGGGGGGGGGGGGGVVGGGGGWWWWWWWWWGGGG",
            "GGGGGGGGGGGGGGGGGVVVVVVWWWWWWWGGGGGG",
            "GGGGGGGGGGGGGGGGGGGGGGGGWWWWGGGGGGGG",
            "GGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGG",
        ];
        DumpTransitionPreview(size);
        PreviewRows = null;
        PreviewName = "transitions";
        UseWaterBlocks = true;
    }

    private static void DumpTransitionPreview(int size)
    {
        string[] rows = PreviewRows ??
        [
            "SSSSSSSSYYYYYYYGGGGG",
            "SSSSGGGGGYYYYGGGGGGG",
            "SSSGGGGGGGGGGGGFFFGG",
            "WWSGGOOGGGGGGGFFDDFG",
            "WWSGGOOGGGGVVVFFDDFG",
            "WWSSGGGGGVVVGGGFFFGG",
            "SSSSGGGGGVGGGGGGGRRR",
            "SSSSSGGGVVGGGTTTRRMM",
            "SSSSSSGGVGGGTTTNNRMM",
            "SSSSSSGVVGGTTNNNNRRM",
            "SSSSSSGVGGGTNNNNNNRR",
            "SSSSSSVVGGGGTNNNNNNR",
            "SSSSCCVCSSSGGTTNNNRR",
            "CCCCCCCCCCCSSSSSSSSS",
        ];
        var width = rows[0].Length;
        var height = rows.Length;
        var surface = new byte[width * height];
        var vegetation = new byte[width * height];
        var hydrology = new byte[width * height];
        var elevation = Enumerable.Repeat((byte)123, width * height).ToArray();
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var i = y * width + x;
                switch (rows[y][x])
                {
                    case 'S': surface[i] = 1; break;
                    case 'F': vegetation[i] = 2; break;
                    case 'D': vegetation[i] = 2; surface[i] = 5; break;
                    case 'R': surface[i] = 2; break;
                    case 'N': surface[i] = 3; break;
                    case 'T': vegetation[i] = 4; break;
                    case 'O': surface[i] = 7; break;
                    case 'Y': surface[i] = 6; break;
                    case 'M': surface[i] = 2; elevation[i] = 220; break;
                    case 'W': hydrology[i] = 2; break;
                    case 'V': hydrology[i] = 3; break;
                    case 'C': hydrology[i] = 1; break;
                    case 'H': surface[i] = 4; break;
                }
            }
        var layers = new OwnerWorldPackedMapLayers(width, height, "map-layers-v2",
            Convert.ToBase64String(Enumerable.Repeat((byte)1, width * height).ToArray()),
            Convert.ToBase64String(elevation), Convert.ToBase64String(hydrology),
            Convert.ToBase64String(surface), Convert.ToBase64String(vegetation));
        var map = WorldTerrainMap.FromTiles([], width, height, layers);
        var image = Image.CreateEmpty(width * size, height * size, false, Image.Format.Rgba8);
        var pieces = new List<(TerrainStyle Style, int Piece)>();
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var style = map.StyleAt(x, y);
#if WATER_BLOCKS
                if (TerrainTextures.IsWater(style) && UseWaterBlocks)
                {
                    var r = WaterTextures.Region(style, x, y, size);
                    image.BlendRect(WaterTextures.Atlas(size).GetImage(), new Rect2I((int)r.Position.X, (int)r.Position.Y, size, size), new Vector2I(x * size, y * size));
                }
                else
#endif
                image.BlendRect(TerrainTextures.Tile(style, TerrainTextures.VariantAt(x, y), size),
                    new Rect2I(0, 0, size, size), new Vector2I(x * size, y * size));
                var at = new Vector2I(x * size, y * size);
                var whole = new Rect2I(0, 0, size, size);
                if (TerrainTextures.IsWater(style))
                {
                    TerrainTransitions.CollectWater(map, x, y, false, pieces);
                    foreach (var (over, piece) in pieces)
                        image.BlendRect(TerrainTransitions.Piece(over, piece, size), whole, at);
                    TerrainTransitions.CollectCoast(map, x, y, false, pieces);
                    var river = style == TerrainStyle.River;
                    foreach (var (_, piece) in pieces)
                        image.BlendRect(Tint(CoastEdges.Piece(river ? CoastEdges.RiverShallowRow : CoastEdges.ShallowRow, piece, size), CoastEdges.ShallowColor(style)), whole, at);
                    if (!river)
                        foreach (var (_, piece) in pieces)
                            image.BlendRect(Tint(CoastEdges.Piece(CoastEdges.FoamRow, piece, size), CoastEdges.Foam), whole, at);
                    foreach (var (land, piece) in pieces)
                        image.BlendRect(Tint(CoastEdges.Piece(CoastEdges.LandRow, piece, size), TerrainTextures.BaseColor(land)), whole, at);
                    continue;
                }
                TerrainTransitions.Collect(map, x, y, false, pieces);
                foreach (var (over, piece) in pieces)
                    image.BlendRect(TerrainTransitions.Piece(over, piece, size), whole, at);
            }
        image.SavePng("/tmp/claude-0/-home-user-ClankerWorld/62ab178e-62a2-5836-a4b8-0ab0a61611f1/scratchpad/" + PreviewName + "-" + size + ".png");
    }

#if WEATHER_LAYER
    private async void StartWeatherPreview()
    {
        foreach (var child in GetChildren()) if (child is CanvasItem item) item.Hide();
        const int width = 96, height = 64;
        var surface = new byte[width * height];
        var vegetation = new byte[width * height];
        var hydrology = new byte[width * height];
        var elevation = Enumerable.Repeat((byte)123, width * height).ToArray();
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var i = y * width + x;
                var dx = x - 50; var dy = y - 30;
                if (dx * dx / 2 + dy * dy < 40) hydrology[i] = 2;
                else if (dx * dx / 2 + dy * dy < 60) surface[i] = 1;
                if (y > 50) hydrology[i] = 1;
                else if (y > 47) surface[i] = 1;
                if ((x * 7 + y * 13) % 11 == 0 && hydrology[i] == 0) vegetation[i] = 2;
            }
        var layers = new OwnerWorldPackedMapLayers(width, height, "map-layers-v2",
            Convert.ToBase64String(Enumerable.Repeat((byte)1, width * height).ToArray()),
            Convert.ToBase64String(elevation), Convert.ToBase64String(hydrology),
            Convert.ToBase64String(surface), Convert.ToBase64String(vegetation));
        var map = WorldTerrainMap.FromTiles([], width, height, layers);
        var holder = new Control();
        holder.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(holder);
        var stage = new Control();
        holder.AddChild(stage);
        var terrain = new WorldTerrainLayer();
        stage.AddChild(terrain);
        var weather = new WeatherLayer();
        weather.Follow(terrain);
        stage.AddChild(weather);
        terrain.SetWorld(map);
        terrain.SetWeatherRegions(32, [
            new OwnerWeatherRegion(0, 0, "storm"), new OwnerWeatherRegion(1, 0, "rain"), new OwnerWeatherRegion(2, 0, "clear"),
            new OwnerWeatherRegion(0, 1, "clear"), new OwnerWeatherRegion(1, 1, "snow"), new OwnerWeatherRegion(2, 1, "cloudy")]);
        var dir = "/tmp/claude-0/-home-user-ClankerWorld/62ab178e-62a2-5836-a4b8-0ab0a61611f1/scratchpad/";
        async Task Shot(Rect2 camera, int tile, string name, int frames)
        {
            stage.Size = new Vector2(width * tile, height * tile);
            terrain.Size = stage.Size;
            weather.Size = stage.Size;
            stage.Position = -camera.Position * tile;
            terrain.SetCamera(camera, tile, 0, false);
            for (var f = 0; f < frames; f++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GetViewport().GetTexture().GetImage().SavePng(dir + name + ".png");
        }
        var size = GetViewport().GetVisibleRect().Size;
        await Shot(new Rect2(8, 2, size.X / 16, size.Y / 16), 16, "wx-overview", 20);
        await Shot(new Rect2(16, 10, size.X / 32, size.Y / 32), 32, "wx-edge-1", 20);
        await Shot(new Rect2(16, 10, size.X / 32, size.Y / 32), 32, "wx-edge-2", 7);
        await Shot(new Rect2(40, 38, size.X / 32, size.Y / 32), 32, "wx-snow", 20);
        await Shot(new Rect2(0, 0, size.X / 8, size.Y / 8), 8, "wx-far", 20);
        await Shot(new Rect2(56, 20, size.X / 16, size.Y / 16), 16, "wx-cloudy", 90);
        GetTree().Quit();
    }
#endif

    private async void ApplyMenuLogo(string path, string shot)
    {
        foreach (var label in mainMenuCard.FindChildren("*", nameof(Label), true, false).OfType<Label>())
            if (label.Text == "CLANKERWORLD" || label.Text.StartsWith("A world shaped", StringComparison.Ordinal))
                label.Hide();
        mainMenuStatus.Hide();
        var stack = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        stack.AddThemeConstantOverride("separation", 18);
        mainMenuCenter.RemoveChild(mainMenuCard);
        mainMenuCenter.AddChild(stack);
        var picture = new TextureRect
        {
            Texture = ImageTexture.CreateFromImage(Image.LoadFromFile(path)),
            StretchMode = TextureRect.StretchModeEnum.KeepCentered,
            TextureFilter = TextureFilterEnum.Nearest,
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
        };
        stack.AddChild(picture);
        stack.AddChild(mainMenuCard);
        mainMenuCard.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        if (shot.Length == 0) return;
        for (var f = 0; f < 40; f++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GetViewport().GetTexture().GetImage().SavePng(shot);
        GetTree().Quit();
    }

    private async void RecordBackdropClip(string directory)
    {
        for (var f = 0; f < 20; f++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var count = OS.GetCmdlineUserArgs().FirstOrDefault(arg => arg.StartsWith("--clip-frames=", StringComparison.Ordinal));
        var frames = count is null ? 96 : int.Parse(count["--clip-frames=".Length..], System.Globalization.CultureInfo.InvariantCulture);
        for (var frame = 0; frame < frames; frame++)
        {
            await ToSignal(GetTree().CreateTimer(1.0 / 12), SceneTreeTimer.SignalName.Timeout);
            GetViewport().GetTexture().GetImage().SavePng($"{directory}/f{frame:000}.png");
        }
        GetTree().Quit();
    }

    private async void ApplyMenuBackground(string path, string shot)
    {
        var picture = new TextureRect
        {
            Texture = ImageTexture.CreateFromImage(Image.LoadFromFile(path)),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            TextureFilter = TextureFilterEnum.Nearest,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        picture.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        mainMenuOverlay.AddChild(picture);
        mainMenuOverlay.MoveChild(picture, mainMenuBackground.GetIndex() + 1);
        if (shot.Length == 0) return;
        for (var f = 0; f < 40; f++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GetViewport().GetTexture().GetImage().SavePng(shot);
        GetTree().Quit();
    }

    private async void StartMenuScene()
    {
        foreach (var child in GetChildren()) if (child is CanvasItem item) item.Hide();
        const int width = 64, height = 38;
        var surface = new byte[width * height];
        var vegetation = new byte[width * height];
        var hydrology = new byte[width * height];
        var elevation = Enumerable.Repeat((byte)123, width * height).ToArray();
        static float Noise(float x, float y) =>
            MathF.Sin(x * 0.31f + MathF.Sin(y * 0.17f) * 2f) * 0.5f + MathF.Sin(y * 0.23f + MathF.Cos(x * 0.13f) * 2f) * 0.5f;
        int I(int x, int y) => y * width + x;
        bool Inside(int x, int y) => x >= 0 && y >= 0 && x < width && y < height;
        var town = new Vector2(14, 18);
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var i = I(x, y);
                var coast = 31 + 2.2f * MathF.Sin(x * 0.19f + 0.4f) + 1.3f * MathF.Sin(x * 0.47f + 1.3f);
                if (y > coast) { hydrology[i] = 1; continue; }
                if (y > coast - 1.7f) surface[i] = 1;
                var dm = MathF.Sqrt(MathF.Pow(x - 61, 2) + MathF.Pow((y - 1) * 1.25f, 2)) + Noise(x, y) * 2.2f;
                if (dm < 7f) { elevation[i] = 250; surface[i] = 3; }
                else if (dm < 13) { elevation[i] = 222; surface[i] = 2; }
                else if (dm < 15.5f) surface[i] = 2;
                var dl = MathF.Pow((x - 9) / 5f, 2) + MathF.Pow((y - 7) / 2.8f, 2) + Noise(x * 2, y * 2) * 0.25f;
                if (dl < 1) { hydrology[i] = 2; continue; }
                var townDistance = (new Vector2(x, y) - town).Length();
                if (surface[i] == 0 && elevation[i] < 200 && townDistance > 7.5f)
                {
                    var forest = Noise(x * 1.3f + 3, y * 1.2f) + (x > 34 && y > 17 ? 0.45f : 0) + (y < 8 && x > 18 && x < 44 ? 0.3f : 0);
                    if (forest > 0.5f) { vegetation[i] = 2; if (forest > 0.95f) surface[i] = 5; }
                    else if (Noise(x * 0.9f + 11, y * 1.7f) > 0.72f) vegetation[i] = 3;
                }
            }
        (int X, int Y)[] riverPath = [(51, 11), (50, 13), (48, 15), (47, 17), (45, 19), (44, 21), (44, 23), (43, 25), (42, 27), (41, 29), (41, 34)];
        var riverTiles = new HashSet<Vector2I>();
        for (var p = 0; p + 1 < riverPath.Length; p++)
        {
            var (x0, y0) = riverPath[p];
            var (x1, y1) = riverPath[p + 1];
            var steps = Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0));
            for (var s = 0; s <= steps; s++)
            {
                var x = x0 + (x1 - x0) * s / steps;
                var y = y0 + (y1 - y0) * s / steps;
                riverTiles.Add(new Vector2I(x, y));
                if (s > 0) riverTiles.Add(new Vector2I(x0 + (x1 - x0) * (s - 1) / steps, y));
            }
        }
        foreach (var tile in riverTiles)
            if (Inside(tile.X, tile.Y) && hydrology[I(tile.X, tile.Y)] == 0)
            {
                hydrology[I(tile.X, tile.Y)] = 3;
                vegetation[I(tile.X, tile.Y)] = 0;
                surface[I(tile.X, tile.Y)] = 0;
            }
        var buildings = new List<OwnerWorldPlacedBuilding>();
        void Place(string id, string tag, int x, int y, int w = 1, int h = 1) =>
            buildings.Add(new OwnerWorldPlacedBuilding(id, id, new OwnerWorldPosition(x, y), 0, id, [tag], w, h));
        Place("warehouse", "warehouse", 12, 15, 2, 2);
        Place("house-a", "house", 10, 15);
        Place("house-b", "house", 16, 15);
        Place("house-c", "house", 10, 18);
        Place("house-d", "house", 16, 19);
        Place("house-e", "house", 12, 20);
        Place("house-f", "house", 18, 16);
        Place("house-g", "house", 11, 13);
        Place("farmhouse", "farmhouse", 8, 19);
        Place("smith", "blacksmith", 15, 20, 1, 2);
        var roads = new List<OwnerWorldPosition>();
        for (var x = 7; x <= 20; x++) roads.Add(new OwnerWorldPosition(x, 17));
        for (var y = 12; y <= 24; y++) if (y != 17) roads.Add(new OwnerWorldPosition(14, y));
        for (var x = 10; x <= 17; x++) if (x != 14) roads.Add(new OwnerWorldPosition(x, 23));
        roads.RemoveAll(road => buildings.Any(b => road.X >= b.Position.X && road.X < b.Position.X + b.Width &&
            road.Y >= b.Position.Y && road.Y < b.Position.Y + b.Height));
        var occupied = new HashSet<Vector2I>(roads.Select(r => new Vector2I(r.X, r.Y)));
        foreach (var b in buildings)
            for (var dy = 0; dy < b.Height; dy++)
                for (var dx = 0; dx < b.Width; dx++)
                    occupied.Add(new Vector2I(b.Position.X + dx, b.Position.Y + dy));
        for (var y = 19; y <= 23; y++)
            for (var x = 3; x <= 7; x++)
                if (Inside(x, y) && hydrology[I(x, y)] == 0 && !occupied.Contains(new Vector2I(x, y))) surface[I(x, y)] = 7;
        var resources = new List<OwnerWorldResource>();
        var natural = new List<OwnerWorldResource>();
        var serial = 0;
        void Tree(int x, int y, string kind, string stage = "available")
        {
            if (!Inside(x, y) || hydrology[I(x, y)] != 0 || occupied.Contains(new Vector2I(x, y))) return;
            occupied.Add(new Vector2I(x, y));
            resources.Add(new OwnerWorldResource("t" + serial++, "wood", new OwnerWorldPosition(x, y), true, "available", 10,
                TreeKind: kind, TreeStage: kind == "orchard" ? stage : null));
        }
        void Object(int x, int y, string kind)
        {
            if (!Inside(x, y) || hydrology[I(x, y)] != 0 || occupied.Contains(new Vector2I(x, y))) return;
            occupied.Add(new Vector2I(x, y));
            natural.Add(new OwnerWorldResource("n" + serial++, "food", new OwnerWorldPosition(x, y), true, "available", 4,
                NaturalObjectKind: kind));
        }
        foreach (var (x, y) in new[] { (18, 19), (19, 20), (18, 21), (19, 18), (20, 19) }) Tree(x, y, "orchard");
        Tree(17, 22, "orchard", "picked");
        Tree(20, 21, "orchard", "growing");
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var h = (int)(PixelArt.Hash(x, y, 91) % 100);
                var i = I(x, y);
                if (vegetation[i] == 2 && h < 66) Tree(x, y, elevation[i] > 200 || x > 46 || h % 3 == 0 ? "conifer" : "broadleaf");
                else if (vegetation[i] == 0 && surface[i] == 0 && elevation[i] < 200 && h < 5 &&
                    (new Vector2(x, y) - town).Length() > 6) Tree(x, y, "broadleaf");
            }
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var h = (int)(PixelArt.Hash(x, y, 57) % 100);
                var i = I(x, y);
                if (hydrology[i] != 0 || occupied.Contains(new Vector2I(x, y))) continue;
                var nearWater = hydrology[I(Math.Max(0, x - 1), y)] is 2 or 3 || hydrology[I(Math.Min(width - 1, x + 1), y)] is 2 or 3 ||
                    hydrology[I(x, Math.Max(0, y - 1))] is 2 or 3 || hydrology[I(x, Math.Min(height - 1, y + 1))] is 2 or 3;
                if (nearWater && h < 30) Object(x, y, h < 6 ? "clay_bank" : "reeds");
                else if (surface[i] == 2 && elevation[i] < 200 && h < 14) Object(x, y, h < 3 ? "iron_outcrop" : "stone_outcrop");
                else if (surface[i] == 0 && vegetation[i] == 0 && h < 4) Object(x, y, h < 2 ? "berry_bush" : "wild_greens");
                else if (vegetation[i] == 3 && h < 8) Object(x, y, "fiber_plant");
                else if (surface[i] == 7 && h < 45) Object(x, y, "wild_seed_patch");
            }
        var layers = new OwnerWorldPackedMapLayers(width, height, "map-layers-v2",
            Convert.ToBase64String(Enumerable.Repeat((byte)2, width * height).ToArray()),
            Convert.ToBase64String(elevation), Convert.ToBase64String(hydrology),
            Convert.ToBase64String(surface), Convert.ToBase64String(vegetation));
        var map = WorldTerrainMap.FromTiles([], width, height, layers);
        var holder = new Control();
        holder.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(holder);
        var stage = new Control();
        holder.AddChild(stage);
        var terrain = new WorldTerrainLayer();
        stage.AddChild(terrain);
        var people = new Control();
        stage.AddChild(people);
        var weather = new WeatherLayer();
        weather.Follow(terrain);
        stage.AddChild(weather);
        terrain.SetWorld(map);
        terrain.SetTrees(resources);
        terrain.SetNaturalObjects(natural);
        terrain.SetRoads(roads);
        terrain.SetBuildings(buildings, []);
        terrain.SetWeatherRegions(32, [
            new OwnerWeatherRegion(0, 0, "cloudy"), new OwnerWeatherRegion(1, 0, "cloudy"),
            new OwnerWeatherRegion(0, 1, "cloudy"), new OwnerWeatherRegion(1, 1, "cloudy")]);
        var walkers = new[] { (13, 17, 0, 2), (14, 14, 1, 2), (17, 17, 2, 2), (9, 17, 3, 1), (11, 23, 4, 2), (14, 22, 1, 3), (5, 21, 5, 2), (19, 17, 2, 1) };
        var agentAtlas = AgentSprites.Atlas(32);
        var agentRects = new List<(TextureRect Rect, int X, int Y)>();
        foreach (var (x, y, variant, ageStage) in walkers)
        {
            var sprite = new TextureRect
            {
                Texture = new AtlasTexture { Atlas = agentAtlas, Region = AgentSprites.Region(variant, ageStage, 32) },
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                TextureFilter = TextureFilterEnum.Nearest,
            };
            people.AddChild(sprite);
            agentRects.Add((sprite, x, y));
        }
        var dir = "/tmp/claude-0/-home-user-ClankerWorld/62ab178e-62a2-5836-a4b8-0ab0a61611f1/scratchpad/";
        async Task Shot(Rect2 camera, int tile, string name, int frames)
        {
            stage.Size = new Vector2(width * tile, height * tile);
            terrain.Size = stage.Size;
            weather.Size = stage.Size;
            people.Size = stage.Size;
            stage.Position = -camera.Position * tile;
            terrain.SetCamera(camera, tile, 0, false);
            foreach (var (rect, x, y) in agentRects)
            {
                rect.Position = new Vector2(x * tile, y * tile);
                rect.Size = new Vector2(tile, tile);
            }
            for (var f = 0; f < frames; f++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GetViewport().GetTexture().GetImage().SavePng(dir + name + ".png");
        }
        var size = GetViewport().GetVisibleRect().Size;
        await Shot(new Rect2(5, 3, size.X / 24, size.Y / 24), 24, "bgw-24", 150);
        await Shot(new Rect2(5, 3, size.X / 24, size.Y / 24), 24, "bgw-24b", 90);
        await Shot(new Rect2(0, 0, size.X / 20, size.Y / 20), 20, "bgw-20", 60);
        GetTree().Quit();
    }

    // ---- Font specimen -------------------------------------------------
    private const string SpecimenFonts = "/tmp/claude-0/-home-user-ClankerWorld/62ab178e-62a2-5836-a4b8-0ab0a61611f1/scratchpad/fonts/";
    private static FontFile? specimenHeading;
    private static int specimenSmall = 12;

    /// <summary>Event Log day headings go through here so the specimen can use its heading font.</summary>
    private static void SpecimenPushDayHeading(RichTextLabel label, int size)
    {
        if (specimenHeading is null) label.PushFontSize(size);
        else label.PushFont(specimenHeading, specimenSmall);
    }

    private static FontFile PixelFont(string path)
    {
        var font = new FontFile();
        var error = font.LoadDynamicFont(path);
        if (error != Error.Ok) throw new InvalidOperationException($"Could not load {path}: {error}");
        font.Antialiasing = TextServer.FontAntialiasing.None;
        font.Hinting = TextServer.Hinting.None;
        font.SubpixelPositioning = TextServer.SubpixelPositioning.Disabled;
        font.GenerateMipmaps = false;
        font.MultichannelSignedDistanceField = false;
        font.AllowSystemFallback = false;
        return font;
    }

    private async void RunFontSpecimen()
    {
        var args = OS.GetCmdlineUserArgs();
        string Arg(string key) => args.FirstOrDefault(a => a.StartsWith(key + "=", StringComparison.Ordinal))?[(key.Length + 1)..] ?? "";
        var native = Arg("--fs-heading").Length == 0;
        var body = native ? UiFonts.Text : PixelFont(SpecimenFonts + "fusion12.ttf");
        var heading = native ? UiFonts.Headings : PixelFont(Arg("--fs-heading"));
        if (!native) heading.Fallbacks = [body];
        var sizes = (native ? "36,24,12" : Arg("--fs-sizes")).Split(',').Select(s => int.Parse(s, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        var (large, medium, small) = (sizes[0], sizes[1], sizes[2]);
        var wood = args.Contains("--fs-wood");
        var dark = Arg("--fs-theme") == "dark";
        var output = Arg("--fs-out");
        if (!native)
        {
            specimenHeading = heading;
            specimenSmall = small;
            ThemeDB.FallbackFont = body;
            ThemeDB.FallbackFontSize = 12;
        }
        var glyphs = wood
            ? System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string[]>>(File.ReadAllText(SpecimenFonts + "timber-glyphs.json"))!
            : [];

        var sizeArg = Arg("--fs-size");
        if (sizeArg.Length > 0)
        {
            var parts = sizeArg.Split('x');
            var windowSize = new Vector2I(int.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture), int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture));
            GetWindow().Size = windowSize;
            GetWindow().ContentScaleSize = windowSize;
        }
        for (var f = 0; f < 10; f++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var scaleArg = Arg("--fs-scale");
        if (scaleArg.Length > 0)
        {
            var percent = int.Parse(scaleArg, System.Globalization.CultureInfo.InvariantCulture);
            if (percent == 0) ApplyUiScale();
            else SetUiFactor(Math.Max(1, percent / 100));
        }
        UiTheme.Apply(GetTree().Root, dark ? UiTheme.Dark : UiTheme.Light);
        for (var i = 0; i < themeChoice.ItemCount; i++)
            if (themeChoice.GetItemText(i) == (dark ? "Dark" : "Light")) themeChoice.Select(i);

        void ApplyFonts()
        {
            if (native) return;
            var theme = GetTree().Root.Theme;
            theme.DefaultFont = body;
            theme.DefaultFontSize = 12;
            theme.SetFont("font", "HeadingLabel", heading);
            theme.SetFont("font", "SectionLabel", heading);
            theme.SetFont("title_font", "Window", heading);
            theme.SetFontSize("title_font_size", "Window", medium);
            foreach (var item in new[] { "normal_font", "italics_font", "mono_font", "bold_font", "bold_italics_font" })
                theme.SetFont(item, "RichTextLabel", body);
            Remap(this);
        }

        void Remap(Node node)
        {
            if (node is Control control)
            {
                if (control is RichTextLabel)
                {
                    foreach (var item in new[] { "normal_font_size", "bold_font_size", "italics_font_size", "bold_italics_font_size", "mono_font_size" })
                        control.AddThemeFontSizeOverride(item, 12);
                }
                else
                {
                    var key = "fs_orig";
                    if (!control.HasMeta(key))
                        control.SetMeta(key, control.HasThemeFontSizeOverride("font_size") ? control.GetThemeFontSize("font_size") : 0);
                    var original = (int)control.GetMeta(key);
                    var variation = control.ThemeTypeVariation.ToString();
                    var size = variation switch
                    {
                        "HeadingLabel" => original >= 22 ? large : medium,
                        "SectionLabel" => small,
                        _ => original >= 20 ? 24 : 12,
                    };
                    if (control is Button && mainMenuCard.IsAncestorOf(control))
                    {
                        control.AddThemeFontOverride("font", heading);
                        size = medium;
                    }
                    control.AddThemeFontSizeOverride("font_size", size);
                    if (wood && variation == "HeadingLabel" && control is Label label)
                        WoodHeading(label, size / 12);
                }
            }
            foreach (var child in node.GetChildren())
                Remap(child);
        }

        void WoodHeading(Label label, int scale)
        {
            label.AddThemeFontOverride("font", woodFont ??= Wide());
            label.AddThemeColorOverride("font_color", Colors.Transparent);
            foreach (var old in label.GetChildren().Where(child => child.Name == "WoodText").ToArray())
            {
                label.RemoveChild(old);
                old.QueueFree();
            }
            if (string.IsNullOrEmpty(label.Text)) return;
            var image = WoodText(label.Text.ToUpperInvariant(), glyphs);
            var textWidth = woodFont.GetStringSize(label.Text, HorizontalAlignment.Left, -1, 12 * scale).X;
            var lineHeight = 14 * scale;
            var x = label.HorizontalAlignment switch
            {
                HorizontalAlignment.Center => Mathf.Floor((label.Size.X - textWidth) / 2),
                HorizontalAlignment.Right => label.Size.X - textWidth,
                _ => 0,
            };
            var y = label.VerticalAlignment switch
            {
                VerticalAlignment.Center => Mathf.Floor((label.Size.Y - lineHeight) / 2),
                VerticalAlignment.Bottom => label.Size.Y - lineHeight,
                _ => 0,
            };
            var picture = new TextureRect
            {
                Name = "WoodText",
                Texture = ImageTexture.CreateFromImage(image),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                TextureFilter = TextureFilterEnum.Nearest,
                MouseFilter = MouseFilterEnum.Ignore,
                Position = new Vector2(x - scale, y + scale),
                Size = new Vector2(image.GetWidth(), image.GetHeight()) * scale,
            };
            label.AddChild(picture);
        }

        FontFile Wide()
        {
            var font = PixelFont(SpecimenFonts + "timber-wide.ttf");
            font.Fallbacks = [body];
            return font;
        }

        // Main Menu. This container has no owner key store, so close the setup panel it opens.
        gameMenuPanel.Hide();
        menuShade.Hide();
        statusToast.Hide();
        pairingPanel.Hide();
        ShowMainMenu();
        mainMenuContinueButton.Disabled = false;
        mainMenuNewButton.Disabled = false;
        mainMenuLoadButton.Disabled = false;
        mainMenuConnectButton.Visible = false;
        mainMenuStatus.Hide();
        ApplyFonts();
        for (var f = 0; f < 12; f++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        ApplyFonts();
        for (var f = 0; f < 4; f++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GetViewport().GetTexture().GetImage().SavePng(output + "-menu.png");
        if (args.Contains("--fs-dialog"))
        {
            quitGameButton.EmitSignal(BaseButton.SignalName.Pressed);
            GD.Print($"DIALOGSIZE size={quitGameConfirmation.Size} min={quitGameConfirmation.MinSize} contents={quitGameConfirmation.GetContentsMinimumSize()} factor={quitGameConfirmation.ContentScaleFactor} mode={quitGameConfirmation.ContentScaleMode} wrap={quitGameConfirmation.WrapControls} csize={quitGameConfirmation.ContentScaleSize}");
            for (var f = 0; f < 3; f++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GD.Print($"DIALOGSIZE later size={quitGameConfirmation.Size}");
            GD.Print($"DIALOG title_font={quitGameConfirmation.GetThemeFont("title_font").GetFontName()} size={quitGameConfirmation.GetThemeFontSize("title_font_size")} rootHas={GetTree().Root.Theme.HasFont("title_font", "Window")} height={quitGameConfirmation.GetThemeConstant("title_height")} typed={quitGameConfirmation.GetThemeFont("title_font", "Window").GetFontName()} hasTyped={quitGameConfirmation.HasThemeFont("title_font", "Window")} has={quitGameConfirmation.HasThemeFont("title_font")} color={quitGameConfirmation.GetThemeColor("title_color")} rootSize={GetTree().Root.Theme.GetFontSize("title_font_size", "Window")} sizeHas={quitGameConfirmation.HasThemeFontSize("title_font_size")} same={ReferenceEquals(GetTree().Root.Theme, UiTheme.Theme)} types={string.Join(",", GetTree().Root.Theme.GetFontTypeList())}");
            for (var f = 0; f < 6; f++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GetViewport().GetTexture().GetImage().SavePng(output + "-dialog.png");
            quitGameConfirmation.Hide();
        }

        if (args.Contains("--fs-extra"))
        {
            worldMenuHeading.Text = "New World";
            worldMenuStatus.Text = "Choose a name, seed and size. You'll pick your first Town's site next.";
            worldMenuColumns.Visible = true;
            worldSelectionList.Visible = false;
            worldSelectButton.Visible = false;
            worldDeleteButton.Visible = false;
            worldCreateButton.Visible = true;
            worldPreviewButton.Visible = false;
            worldNameInput.Text = "Riverbend";
            worldSeedInput.Text = "riverbend";
            var previewJson = Arg("--fs-preview-json");
            if (previewJson.Length > 0)
            {
                using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(previewJson));
                var root = doc.RootElement;
                string S(string name) => root.GetProperty(name).GetString()!;
                var w = root.GetProperty("width").GetInt32();
                var h = root.GetProperty("height").GetInt32();
                worldPreview.SetWorld(WorldTerrainMap.FromPacked(new OwnerWorldPackedTerrain(w, h, "terrain-kind-v1", S("terrain")),
                    new OwnerWorldPackedMapLayers(w, h, "map-layers-v2", S("climate"), S("elevation"), S("hydrology"), S("surface"), S("vegetation")), true));
            }
            worldPreviewStatus.Text = "Map preview · 41 resource sites. You will choose where your first Town goes after creating the world.";
            worldPreview.Show();
            worldMenuOverlay.Show();
            worldCreateButton.Disabled = false;
            for (var f = 0; f < 10; f++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GetViewport().GetTexture().GetImage().SavePng(output + "-newworld.png");
            worldAdvancedToggle.ButtonPressed = true;
            for (var f = 0; f < 8; f++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GetViewport().GetTexture().GetImage().SavePng(output + "-newworld-more.png");
            worldAdvancedToggle.ButtonPressed = false;
            worldMenuOverlay.Hide();
            ShowMainMenu();
            for (var f = 0; f < 3; f++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        if (args.Contains("--fs-lists"))
        {
            worldMenuHeading.Text = "Load World";
            worldMenuStatus.Text = "Choose a world. The current world is saved before switching.";
            worldMenuColumns.Visible = false;
            worldPreviewButton.Visible = false;
            worldCreateButton.Visible = false;
            worldSelectionList.Visible = true;
            worldSelectButton.Visible = true;
            worldDeleteButton.Visible = true;
            worldMenuOverlay.Show();
            var now = DateTimeOffset.Now;
            var thumbs = new Dictionary<string, WorldThumbnail>();
            if (Arg("--fs-thumbs").Length > 0)
                thumbs = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, WorldThumbnail>>(File.ReadAllText(Arg("--fs-thumbs")))!;
            WorldThumbnail? T(string key) => thumbs.TryGetValue(key, out var thumb) ? thumb : null;
            await worldListRequest.RefreshAsync(_ => Task.FromResult(new WorldCatalogSnapshot("w1", [
                new CatalogWorld("w1", "Riverbend", "wid1", "3f9a1c27b8e2", now.AddMinutes(-12), [], null, "compatible", null, T("riverbend")),
                new CatalogWorld("w2", "Pinecrest", "wid2", "a71c0de4f9b3", now.AddDays(-1), [], null, "compatible", null, T("pinecrest")),
                new CatalogWorld("w3", "Saltmarsh test", "wid3", "00be7712aa10", now.AddDays(-6), [], null, "unknown", null, T("saltmarsh")),
                new CatalogWorld("w4", "Old prototype", "wid4", "9921cc0e3d4f", now.AddDays(-30), [], null, "incompatible", "It was made with an older version.", T("oldproto")),
            ])));
            for (var f = 0; f < 6; f++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            worldSelectionList.Select(1);
            for (var f = 0; f < 4; f++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GetViewport().GetTexture().GetImage().SavePng(output + "-load.png");
            worldMenuOverlay.Hide();
        }
        // Game Settings from the Main Menu.
        OpenMainMenuSettings();
        for (var f = 0; f < 6; f++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        ApplyFonts();
        for (var f = 0; f < 6; f++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        ApplyFonts();
        for (var f = 0; f < 3; f++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GetViewport().GetTexture().GetImage().SavePng(output + "-settings.png");
        if (args.Contains("--fs-popup"))
        {
            themeChoice.ShowPopup();
            for (var f = 0; f < 6; f++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GetViewport().GetTexture().GetImage().SavePng(output + "-popup.png");
            themeChoice.GetPopup().Hide();
            var point = themeChoice.GetGlobalRect().GetCenter();
            GetViewport().PushInput(new InputEventMouseMotion { Position = point, GlobalPosition = point }, true);
            Input.WarpMouse(point);
            for (var f = 0; f < 90; f++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GetViewport().GetTexture().GetImage().SavePng(output + "-tooltip.png");
        }

        // A world with an agent card and the Event Log.
        gameMenuPanel.Hide();
        menuShade.Hide();
        returnToMainMenu = false;
        mainMenuOverlay.Hide();
        isInWorld = true;
        var (snapshot, events) = SpecimenWorld();
        observedCalendarPace = snapshot.CalendarPace;
        Render(snapshot, events);
        cameraZoom = 1.9f;
        cameraCenterTiles = new Vector2(19f, 18.5f);
        selectedInhabitantId = "mira";
        Render(snapshot, []);
        eventsPanel.Show();
        RenderEventLog();
        for (var f = 0; f < 4; f++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        ApplyFonts();
        for (var f = 0; f < 8; f++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Render(snapshot, []);
        PositionSelectedInhabitantCard(snapshot);
        ApplyFonts();
        for (var f = 0; f < 30; f++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        ApplyFonts();
        for (var f = 0; f < 3; f++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        statusToast.Hide();
        if (args.Contains("--fs-extra"))
        {
            eventsPanel.Hide();
            selectedInhabitantId = null;
            Render(snapshot, []);
            ToggleWorldInfo();
            for (var f = 0; f < 6; f++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GetViewport().GetTexture().GetImage().SavePng(output + "-towns.png");
            worldInfoPanel.Hide();
            menuHeadingLabel.Text = "Paused";
            StyleIconButton(menuCloseButton, PixelGlyph.Close);
            SetWorldMenuActionsVisible(true);
            menuResumeButton.Show();
            settingsPanel.Hide();
            gameMenuPanel.Show();
            menuShade.Show();
            ApplyResponsiveLayout();
            for (var f = 0; f < 6; f++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GetViewport().GetTexture().GetImage().SavePng(output + "-pause.png");
            ShowSettingsSection(worldSpecific: false);
            for (var f = 0; f < 6; f++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            ApplyResponsiveLayout();
            for (var f = 0; f < 4; f++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GetViewport().GetTexture().GetImage().SavePng(output + "-pause-settings.png");
            settingsScroll.ScrollVertical = 100000;
            for (var f = 0; f < 4; f++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GetViewport().GetTexture().GetImage().SavePng(output + "-pause-settings-end.png");
            ShowSettingsSection(worldSpecific: true);
            for (var f = 0; f < 8; f++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            ApplyResponsiveLayout();
            for (var f = 0; f < 4; f++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GetViewport().GetTexture().GetImage().SavePng(output + "-pause-world.png");
            settingsScroll.ScrollVertical = 100000;
            for (var f = 0; f < 4; f++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GetViewport().GetTexture().GetImage().SavePng(output + "-pause-world-end.png");
            GetTree().Quit();
            return;
        }
        if (args.Contains("--fs-badge"))
        {
            addAgentButton.Show();
            eventsBadge.Text = "3";
            eventsBadge.Show();
        }
        for (var f = 0; f < 2; f++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GetViewport().GetTexture().GetImage().SavePng(output + "-world.png");
        if (args.Contains("--fs-lists"))
        {
            eventsPanel.Hide();
            selectedInhabitantId = null;
            Render(snapshot, []);
            var now = DateTimeOffset.Now;
            ShowManualSavePanel(false);
            manualSaveName.Text = DisplayWorldClock(snapshot.WorldTick);
            listedManualSaves = [
                new ManualWorldSave("s1", "Before the first winter", now.AddMinutes(-40), 6_120),
                new ManualWorldSave("s2", "Town founded", now.AddHours(-5), 1_440),
                new ManualWorldSave("s3", "Fresh start", now.AddDays(-2), 30),
            ];
            RenderManualSaveList();
            manualSaveList.Select(0);
            RefreshManualSaveAvailability();
            for (var f = 0; f < 6; f++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GetViewport().GetTexture().GetImage().SavePng(output + "-save.png");
            manualSaveOverlay.Hide();
        }
        var townJson = Arg("--town-json");
        if (townJson.Length > 0)
        {
            await RunTownJson(townJson.Split(','), output);
            GetTree().Quit();
            return;
        }
        if (args.Contains("--mock")) await RunMocks(snapshot, output);
        if (args.Contains("--audit")) await RunAudit(snapshot, output);
        if (args.Contains("--audit2")) await RunAudit2(snapshot, events, output);
        GetTree().Quit();
    }

    private static FontFile? woodFont;

    /// <summary>The logo's carved-wood letters at one pixel per font pixel: grain, a two-pixel drop and a dark outline.</summary>
    private static Image WoodText(string text, Dictionary<string, string[]> glyphs)
    {
        const int rows = 9;
        var width = 2;
        foreach (var c in text)
            width += glyphs.TryGetValue(c.ToString(), out var g) ? g[0].Length + 2 : 4;
        var height = rows + 4;
        var letters = new bool[height, width];
        var pen = 1;
        foreach (var c in text)
        {
            if (!glyphs.TryGetValue(c.ToString(), out var g)) { pen += 4; continue; }
            for (var y = 0; y < rows; y++)
                for (var x = 0; x < g[y].Length; x++)
                    if (g[y][x] == '#') letters[y + 1, pen + x] = true;
            pen += g[0].Length + 2;
        }
        bool At(int x, int y) => x >= 0 && y >= 0 && x < width && y < height && letters[y, x];
        var image = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
        var body = new bool[height, width];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                body[y, x] = At(x, y) || At(x, y - 1) || At(x, y - 2);
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                if (body[y, x]) continue;
                var touches = false;
                for (var dy = -1; dy <= 1; dy++)
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        var (nx, ny) = (x + dx, y + dy);
                        touches |= nx >= 0 && ny >= 0 && nx < width && ny < height && body[ny, nx];
                    }
                if (touches) image.SetPixel(x, y, new Color("28180E"));
            }
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                if (At(x, y)) continue;
                if (At(x, y - 1)) image.SetPixel(x, y, new Color("684226"));
                else if (At(x, y - 2)) image.SetPixel(x, y, new Color("563620"));
            }
        Color[] stops = [new("F0CC8C"), new("D29E60"), new("A66E3C")];
        const int top = 1;
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                if (!At(x, y)) continue;
                var row = Mathf.Clamp(y - top, 0, rows - 1) / (float)(rows - 1) * (stops.Length - 1);
                var index = Mathf.Min((int)row, stops.Length - 2);
                var color = stops[index].Lerp(stops[index + 1], row - index);
                if (!At(x, y + 1)) color = new Color("80522E");
                if (!At(x, y - 1)) color = new Color("FCE6B2");
                else if (y >= top + 2 && y <= top + rows - 2 && PixelArt.Hash(x / 4, y, 3) % 4 == 0 && PixelArt.Hash(x, y, 5) % 3 != 0)
                    color = new Color(color.R * 0.88f, color.G * 0.88f, color.B * 0.88f);
                image.SetPixel(x, y, color);
            }
        return image;
    }

    private static (OwnerWorldSnapshot Snapshot, OwnerWorldEvent[] Events) SpecimenWorld()
    {
        const int width = 64, height = 38;
        var surface = new byte[width * height];
        var vegetation = new byte[width * height];
        var hydrology = new byte[width * height];
        var elevation = Enumerable.Repeat((byte)123, width * height).ToArray();
        static float Noise(float x, float y) =>
            MathF.Sin(x * 0.31f + MathF.Sin(y * 0.17f) * 2f) * 0.5f + MathF.Sin(y * 0.23f + MathF.Cos(x * 0.13f) * 2f) * 0.5f;
        int I(int x, int y) => y * width + x;
        bool Inside(int x, int y) => x >= 0 && y >= 0 && x < width && y < height;
        var town = new Vector2(14, 18);
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var i = I(x, y);
                var coast = 31 + 2.2f * MathF.Sin(x * 0.19f + 0.4f) + 1.3f * MathF.Sin(x * 0.47f + 1.3f);
                if (y > coast) { hydrology[i] = 1; continue; }
                if (y > coast - 1.7f) surface[i] = 1;
                var dl = MathF.Pow((x - 9) / 5f, 2) + MathF.Pow((y - 7) / 2.8f, 2) + Noise(x * 2, y * 2) * 0.25f;
                if (dl < 1) { hydrology[i] = 2; continue; }
                var townDistance = (new Vector2(x, y) - town).Length();
                if (surface[i] == 0 && townDistance > 7.5f)
                {
                    var forest = Noise(x * 1.3f + 3, y * 1.2f) + (x > 34 && y > 17 ? 0.45f : 0);
                    if (forest > 0.5f) { vegetation[i] = 2; if (forest > 0.95f) surface[i] = 5; }
                    else if (Noise(x * 0.9f + 11, y * 1.7f) > 0.72f) vegetation[i] = 3;
                }
            }
        var buildings = new List<OwnerWorldPlacedBuilding>();
        void Place(string id, string name, string tag, int x, int y, int w = 1, int h = 1) =>
            buildings.Add(new OwnerWorldPlacedBuilding(id, id, new OwnerWorldPosition(x, y), 0, name, [tag], w, h));
        Place("warehouse", "Warehouse", "warehouse", 12, 15, 2, 2);
        Place("house-a", "House", "house", 10, 15);
        Place("house-b", "House", "house", 16, 15);
        Place("house-c", "House", "house", 10, 18);
        Place("house-d", "House", "house", 16, 19);
        Place("farmhouse", "Farmhouse", "farmhouse", 8, 19);
        Place("smith", "Blacksmith", "blacksmith", 15, 20, 1, 2);
        var roads = new List<OwnerWorldPosition>();
        for (var x = 7; x <= 20; x++) roads.Add(new OwnerWorldPosition(x, 17));
        for (var y = 12; y <= 24; y++) if (y != 17) roads.Add(new OwnerWorldPosition(14, y));
        roads.RemoveAll(road => buildings.Any(b => road.X >= b.Position.X && road.X < b.Position.X + b.Width &&
            road.Y >= b.Position.Y && road.Y < b.Position.Y + b.Height));
        var occupied = new HashSet<Vector2I>(roads.Select(r => new Vector2I(r.X, r.Y)));
        foreach (var b in buildings)
            for (var dy = 0; dy < b.Height; dy++)
                for (var dx = 0; dx < b.Width; dx++)
                    occupied.Add(new Vector2I(b.Position.X + dx, b.Position.Y + dy));
        for (var y = 19; y <= 23; y++)
            for (var x = 3; x <= 7; x++)
                if (Inside(x, y) && hydrology[I(x, y)] == 0 && !occupied.Contains(new Vector2I(x, y))) surface[I(x, y)] = 7;
        var resources = new List<OwnerWorldResource>();
        var serial = 0;
        void Tree(int x, int y, string kind)
        {
            if (!Inside(x, y) || hydrology[I(x, y)] != 0 || occupied.Contains(new Vector2I(x, y))) return;
            occupied.Add(new Vector2I(x, y));
            resources.Add(new OwnerWorldResource("t" + serial++, "wood", new OwnerWorldPosition(x, y), true, "available", 10,
                TreeKind: kind));
        }
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var h = (int)(PixelArt.Hash(x, y, 91) % 100);
                if (vegetation[I(x, y)] == 2 && h < 66) Tree(x, y, x > 46 || h % 3 == 0 ? "conifer" : "broadleaf");
            }
        var terrain = new byte[width * height];
        for (var i = 0; i < terrain.Length; i++)
            terrain[i] = hydrology[i] switch { 1 => 5, 2 => 4, 3 => 3, _ => vegetation[i] == 2 ? (byte)8 : surface[i] == 1 ? (byte)7 : (byte)0 };
        var layers = new OwnerWorldPackedMapLayers(width, height, "map-layers-v2",
            Convert.ToBase64String(Enumerable.Repeat((byte)2, width * height).ToArray()),
            Convert.ToBase64String(elevation), Convert.ToBase64String(hydrology),
            Convert.ToBase64String(surface), Convert.ToBase64String(vegetation));
        const long tick = 360 * 2 + 214;
        OwnerWorldInhabitant Person(string id, string name, int x, int y, string age) =>
            new(id, name, "active", new OwnerWorldPosition(x, y), 7_200, [], [new("age-band", age), new("age-years", age == "child" ? "6" : "24")],
                new OwnerWorldRoute("idle", null, null, [], string.Empty),
                new OwnerWorldSpatialKnowledge(new OwnerWorldPosition(x, y), [], []), false);
        var mira = Person("mira", "Mira", 13, 18, "adult") with
        {
            Survival = new OwnerWorldSurvival(8_200, 300, true, true, 7_400, null),
            PublicIntention = new OwnerWorldPublicIntention("gather_clay_by_the_river", "gathering clay by the river", "openai", tick),
            Relationships = [new OwnerWorldInhabitantRelationship("r1", "rowan", "partner", "active", "public", 100)],
            SocialStanding = [new OwnerWorldSocialStanding("ash", "Ash", 7)],
            RecentPrivateThoughts =
            [
                new OwnerWorldPrivateThought(360 + 300, "Berries are thinning near camp. Tomorrow I'll try the hills to the east."),
                new OwnerWorldPrivateThought(360 * 2 + 40, "Rowan looked tired at the fire. I should leave the warm spot for them tonight."),
                new OwnerWorldPrivateThought(tick - 10, "The river bank has good clay. If I carry some back before dusk, Ash can start on the kiln tomorrow."),
            ],
        };
        OwnerWorldInhabitant[] people =
        [
            mira,
            Person("rowan", "Rowan", 17, 17, "adult"),
            Person("ash", "Ash", 9, 17, "adult"),
            Person("wren", "Wren", 11, 23, "adult"),
            Person("pip", "Pip", 14, 22, "child"),
            Person("tamsin", "Tamsin", 5, 21, "adult"),
        ];
        var snapshot = new OwnerWorldSnapshot("font-specimen", tick, "specimen-map", [], [], resources, null, 12)
        {
            PackedTerrain = new OwnerWorldPackedTerrain(width, height, "terrain-kind-v1", Convert.ToBase64String(terrain)),
            PackedMapLayers = layers,
            RoadTiles = roads,
            PlacedBuildings = buildings,
            Inhabitants = people,
            CalendarPace = new OwnerWorldCalendarPace(360, 40),
            Towns = [new OwnerWorldTown("town:first", "Riverbend", "founded", 300, ["mira", "rowan", "ash", "wren", "pip", "tamsin"], [], [])],
            Cognition = new OwnerWorldCognition("openai", false, null, null, null, [],
                [new OwnerWorldInhabitantDecision("mira", "openai", "gather_clay_by_the_river", tick - 12, 0.8, "model", null, null)]),
        };
        OwnerWorldEvent[] events =
        [
            new(1, 20, "world_started", ""),
            new(2, 140, "weather_changed", "region:rain"),
            new(3, 230, "food_harvested", "wren"),
            new(4, 300, "town_founded", ""),
            new(5, 380, "build_started", "mira:family_house"),
            new(6, 455, "child_born", "pip"),
            new(7, 520, "food_consumed", "rowan"),
            new(8, 600, "partnership_accepted", ""),
            new(9, 730, "build_completed", "mira:family_house"),
            new(10, 790, "recipe_completed", "ash:stone_axe"),
            new(11, 860, "weather_changed", "region:clear"),
            new(12, 910, "food_harvested", "tamsin"),
        ];
        return (snapshot, events);
    }

    private static Image Tint(Image mask, Color color)
    {
        var tinted = (Image)mask.Duplicate();
        for (var y = 0; y < tinted.GetHeight(); y++)
            for (var x = 0; x < tinted.GetWidth(); x++)
            {
                var pixel = tinted.GetPixel(x, y);
                if (pixel.A > 0) tinted.SetPixel(x, y, new Color(color.R, color.G, color.B, pixel.A * color.A));
            }
        return tinted;
    }
}

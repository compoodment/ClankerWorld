using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private static readonly HashSet<Color> SeasonalOrchardCanopyColours =
    [new("3C5F2E"), new("4C7A3A"), new("5E8C45"), new("79A657"), new("9BC66F")];

    private void VerifySeasonalLandscape(OwnerWorldSnapshot original)
    {
        var center = cameraCenterTiles;
        var zoom = cameraZoom;
        var tree = new OwnerWorldResource("season-tree", "wood", new(1, 1), true,
            "available", 3, 3, TreeKind: "broadleaf", TreeStage: "mature");
        var authoring = original.Authoring ?? new OwnerWorldAuthoringState(false, 0, 0, 0,
            "season-initial", "season-current", "clear", "summer", []);
        var sample = original with { Resources = [tree], Authoring = authoring };
        RenderMap(sample);
        var map = terrainLayer.World;
        var summerGround = new Dictionary<int, byte[]>();
        var summerTree = new Dictionary<int, byte[]>();
        var renderedGround = new Dictionary<int, List<byte[]>>();
        var renderedCanopy = new Dictionary<int, List<byte[]>>();
        foreach (var size in new[] { 16, 32 })
        {
            summerGround[size] = TerrainTextures.Tile(TerrainStyle.Grass, 0, size).GetData();
            summerTree[size] = NatureSprites.Sprite(NatureSprite.Broadleaf, size).GetData();
            renderedGround[size] = [];
            renderedCanopy[size] = [];
        }
        try
        {
            foreach (var season in Enum.GetValues<LandscapeSeason>())
            {
                RenderMap(sample with { Authoring = authoring with { Season = season.ToString().ToLowerInvariant() } });
                if (terrainLayer.Season != season || !ReferenceEquals(map, terrainLayer.World) ||
                    terrainLayer.TreeStageAt(1, 1) != "mature")
                    throw new InvalidOperationException("The observed season must update the existing map without changing its terrain or tree stage.");
                foreach (var size in new[] { 16, 32 })
                {
                    var ground = TerrainTextures.Tile(TerrainStyle.Grass, 0, size, season).GetData();
                    var canopy = NatureSprites.Sprite(NatureSprite.Broadleaf, size, season).GetData();
                    if (ground.SequenceEqual(summerGround[size]) != (season == LandscapeSeason.Summer) ||
                        canopy.SequenceEqual(summerTree[size]) != (season == LandscapeSeason.Summer))
                        throw new InvalidOperationException("Every season must change grass and canopy pixels; summer must retain the accepted base art.");
                    if (renderedGround[size].Any(previous => previous.SequenceEqual(ground)) ||
                        renderedCanopy[size].Any(previous => previous.SequenceEqual(canopy)))
                        throw new InvalidOperationException("Each season must have distinct grass and canopy colours.");
                    renderedGround[size].Add(ground);
                    renderedCanopy[size].Add(canopy);
                    using var baseOrchard = NatureSprites.Sprite(NatureSprite.OrchardFruiting, size);
                    using var orchard = NatureSprites.Sprite(NatureSprite.OrchardFruiting, size, season);
                    var changedCanopyPixels = 0;
                    for (var y = 0; y < size; y++)
                        for (var x = 0; x < size; x++)
                        {
                            var before = baseOrchard.GetPixel(x, y);
                            var after = orchard.GetPixel(x, y);
                            if (before.A != after.A || (before != after && !SeasonalOrchardCanopyColours.Contains(new Color(before, 1))))
                                throw new InvalidOperationException("Seasonal orchard colours must preserve fruit, trunk, shadow and transparency pixels.");
                            if (before != after) changedCanopyPixels++;
                        }
                    if ((changedCanopyPixels == 0) != (season == LandscapeSeason.Summer))
                        throw new InvalidOperationException("Seasonal orchard art must change its canopy while summer retains its base pixels.");
                    foreach (var style in new[] { TerrainStyle.Sand, TerrainStyle.Rock, TerrainStyle.Snow, TerrainStyle.TundraSnow })
                        if (!TerrainTextures.Tile(style, 0, size, season).GetData().SequenceEqual(TerrainTextures.Tile(style, 0, size).GetData()))
                            throw new InvalidOperationException("Seasons must preserve permanent rock, desert and snow art.");
                    foreach (var sprite in new[] { NatureSprite.BroadleafStump, NatureSprite.WoodPile, NatureSprite.StoneOutcrop })
                        if (!NatureSprites.Sprite(sprite, size, season).GetData().SequenceEqual(NatureSprites.Sprite(sprite, size).GetData()))
                            throw new InvalidOperationException("Seasons must preserve non-canopy art.");
                    var atlas = TerrainTextures.Atlas(size, season);
                    var trees = NatureSprites.Atlas(size, season);
                    PanCamera(new Vector2(0.25f, 0));
                    ZoomAt(mapCanvas.Size / 2, zoomIn: cameraZoom <= minimumCameraZoom);
                    AdvanceCameraMotion(CameraEasing.ZoomSeconds);
                    RenderMap(renderedMapSnapshot!);
                    if (TerrainTextures.Atlas(size, season).GetInstanceId() != atlas.GetInstanceId() ||
                        NatureSprites.Atlas(size, season).GetInstanceId() != trees.GetInstanceId())
                        throw new InvalidOperationException("Panning and zooming must reuse the warmed seasonal art.");
                }
            }
            RenderMap(sample with { Authoring = null, WorldSystems = null });
            if (terrainLayer.Season != LandscapeSeason.Summer)
                throw new InvalidOperationException("A world without a reported season must use the base art, without retaining the previous world's winter.");
        }
        finally
        {
            RenderMap(original);
            cameraZoom = zoom;
            UpdateMapGeometry(original);
            SetCameraAtImmediately(center);
        }
    }
}

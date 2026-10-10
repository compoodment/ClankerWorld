using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    // RGBA8 digests from the independent approved #629 proposal renderer.
    private static readonly string[] ApprovedBoatPixels =
    [
        "F7E65A00DFEFC5C4E21AF306FB73A2FD6467D4FBC45EFB602CA2D0BD598A9C3A",
        "3EEFDBCB1B3AF3C361F871FA9D1AA26480A883CEB5F172C00EE0EAC84ADC7555",
        "4C8296FCE66BAF2085419C58AC54CC4C1CDE543ADF8E9A2D0C3DBB64CEE4BF7B",
        "9BAA075F858E085A3C5209D05F4434154E63CCAA55C5E4B42B66A213E9E24A28",
        "B20AEA8DBA5268472C0B015EEC6D663E90D3D70CB891E7C61C1D16DA1303466C",
        "250D4FD4B69D4DDED02C4872EBE7DE4EB98E3D151174D9C3E250660028F23609",
        "75857FA19DE51E56116778959F43F2EA3D7C6E4AB246BAE9D5AEC35540CECE6C",
        "3973B709CCB65AC3644B77415D2A138B63BB7C5594A86B72BB99525B49402778",
        "3BE0B046E64048F096CD34483B1A7DE44FF9FE640590C3013EA320FDA55F3822",
        "A9C83B63F1B2961D55E170DF197C5415C4B53BB173EF03869397E6915EA9C75B",
        "C6848A5247D3F32AAA22F18890CBDEC3C7AFB3BFBB77189FA94DB75CF4A4398C",
        "31B5CFF5106013A36C2D6FBCB3A1B78984632ACED4365E4601096B7270699CCF",
        "C1A060D0F5BFCD0B68E4FD2CC39AE82F9C319880918C65EDAF7DCB47BE349A96",
        "3B78FA4134FC008212E07AC4662E58D411529C10D5228B8C05C6D0E952B04443",
        "7D44F1C4A7F1F00DC113BE869F18C5C62A9A1B1AE2A9BA2CC64716E665624E26",
        "3C2D371907B2C605F252F923C55A704DBF8DFF164507392104D74B25A97878B7",
    ];

    private static readonly (int Width, int Height, DoorSide Side, string Digest)[] ApprovedPortPixels =
    [
        (2, 4, DoorSide.North, "A069C7842FF290A855CEE9586F0DFBB7C519CA7FE2898D3299D63D4868FBA70F"),
        (4, 2, DoorSide.East, "B0181B78139767F83C5CB44ABFC2641BC378935B6FBDF2312C301BB9FE87DBE5"),
        (2, 4, DoorSide.South, "48AEC9B742ACB064062AF085BB71A0D1B765D642EA7BE2B5D0E0120DE2DEB598"),
        (4, 2, DoorSide.West, "66543D8D9C047A9B23716217F45659FB942A74F49240B9331E90BCE914454F9E"),
    ];

    private void VerifyBoatPresentation()
    {
        for (var facing = 0; facing < 8; facing++)
            foreach (var rowing in new[] { false, true })
            {
                using var image = BoatSprites.Sprite(facing, rowing);
                using var texture = BoatSprites.Texture(facing, rowing).GetImage();
                var expected = ApprovedBoatPixels[facing * 2 + (rowing ? 1 : 0)];
                if (HandcartPixelDigest(image) != expected || HandcartPixelDigest(texture) != expected)
                    throw new InvalidOperationException("Native boat sprites and textures must retain all sixteen approved poses.");
            }
        foreach (var (width, height, side, digest) in ApprovedPortPixels)
        {
            using var image = BuildingSprites.Render(BuildingKind.Port, width, height, 32, new(side, 1));
            if (HandcartPixelDigest(image) != digest)
                throw new InvalidOperationException("Native Ports must retain all four approved rotations.");
        }

        var at = new OwnerWorldPosition(120, 60);
        var boat = new OwnerWorldBoat("boat", "town", "Cedar", at, "port", null, null, null, "moored", null, []);
        var port = new OwnerWorldPlacedBuilding("port", "port-south", new(118, 58), 0, "Port", ["port", "port-facing-south"],
            2, 4, "town", Entrance: new(118, 57));
        var town = new OwnerWorldTown("town", "Cedar", "founded", 0, [], [port.InstanceId], []);
        var map = new OwnerWorldSnapshot("boat-smoke", 1, "boat-map", [], [], [], null, 0)
        {
            PackedTerrain = new(256, 128, "terrain-kind-v1", Convert.ToBase64String(new byte[256 * 128])),
            Boats = [boat],
            PlacedBuildings = [port],
            Towns = [town],
        };
        RenderMap(map);
        cameraZoom = maximumCameraZoom;
        RenderMap(map);
        var marker = mapObjectVisuals["boat:boat"];

        void Expect(int facing, bool rowing)
        {
            var current = mapObjectVisuals["boat:boat"];
            var sprite = current.GetNode<TextureRect>("BoatSprite");
            using var image = sprite.Texture.GetImage();
            if (!ReferenceEquals(marker, current) || HandcartPixelDigest(image) != ApprovedBoatPixels[facing * 2 + (rowing ? 1 : 0)] ||
                sprite.TextureFilter != CanvasItem.TextureFilterEnum.Nearest || sprite.Size != new Vector2(32, 32) ||
                !new Rect2(Vector2.Zero, current.Size).Encloses(new Rect2(sprite.Position, sprite.Size)))
                throw new InvalidOperationException("One boat marker must show its observed heading and oars, unscaled and unclipped at close zoom.");
        }
        void Move(int x, int y, string status)
        {
            boat = boat with
            {
                Position = new(x, y),
                Status = status,
                DockedPortId = null,
                PassengerId = "traveler",
                PassengerName = "Rowan",
                DestinationPortId = "port",
                ReservedDock = new(120, 59),
                Cargo = [new("rope", 2)]
            };
            map = map with { WorldTick = map.WorldTick + 1, Boats = [boat] };
            RenderMap(map);
        }
        Expect(0, false);
        Move(121, 60, "underway"); Expect(6, true);
        Move(121, 59, "underway"); Expect(4, true);
        Move(122, 60, "underway"); Expect(7, true);
        Move(122, 60, "waiting"); Expect(7, false);
        Move(122, 59, "returning"); Expect(4, true);
        if (!marker.TooltipText.Contains("Returning to the departure Port", StringComparison.Ordinal) ||
            !marker.TooltipText.Contains("Town: Cedar", StringComparison.Ordinal) ||
            !marker.TooltipText.Contains("Passenger: Rowan", StringComparison.Ordinal) ||
            !marker.TooltipText.Contains("Rope 2", StringComparison.Ordinal))
            throw new InvalidOperationException("Boat hover help must disclose the actual Town, passenger, cargo and recovery state.");
        selectedTile = new(122, 59);
        RenderTileInspection(map);
        if (!TileCardText().Contains("Boat", StringComparison.Ordinal) || !TileCardText().Contains("Passenger: Rowan", StringComparison.Ordinal))
            throw new InvalidOperationException("The boat's actual tile must offer inspection.");
        if (!PortUsageText(map, port).Contains("1 / 6 spaces used", StringComparison.Ordinal) ||
            !TownBoatText(map, town).Contains("Passenger: Rowan", StringComparison.Ordinal))
            throw new InvalidOperationException("Port and Town inspection must count incoming space and disclose boat travel.");

        var originalDestination = port with { InstanceId = "original-destination", Position = new(196, 13) };
        var request = new OwnerWorldBoatTripRequest("boat-request:1", 1, "traveler", "Rowan", town.Id,
            port.InstanceId, originalDestination.InstanceId, "underway", boat.Id);
        var cases = new[]
        {
            (Status: "underway", Destination: originalDestination.InstanceId, Request: request, Expected: "196, 13 · aboard"),
            (Status: "returning", Destination: port.InstanceId, Request: request with { Status = "waiting", BoatId = null }, Expected: "196, 13 · waiting to depart"),
            (Status: "underway", Destination: port.InstanceId, Request: request with { BoatId = "missing-boat" }, Expected: "196, 13 · aboard"),
            (Status: "returning", Destination: port.InstanceId, Request: request, Expected: "118, 58 · returning"),
            (Status: "waiting", Destination: port.InstanceId, Request: request, Expected: "118, 58 · waiting for a safe arrival"),
        };
        foreach (var sample in cases)
        {
            var trip = map with
            {
                PlacedBuildings = [port, originalDestination],
                Boats = [boat with { Status = sample.Status, DestinationPortId = sample.Destination }],
                BoatRequests = [sample.Request],
            };
            RenderWorldInfo(trip);
            var row = PageText(townList).Split('\n').Single(line => line.StartsWith("Rowan →", StringComparison.Ordinal));
            if (row != "Rowan → Port at " + sample.Expected)
                throw new InvalidOperationException($"A Town trip row must show its current arrival and travel state: expected={sample.Expected} actual={row}");
        }
        RenderWorldInfo(map);

        cameraZoom = 1; RenderMap(map);
        var baseTile = currentTileSize;
        cameraZoom = 39f / baseTile; RenderMap(map);
        var smaller = marker.GetNode<TextureRect>("BoatSprite");
        using (var image = smaller.Texture.GetImage())
        using (var reference = BoatSprites.Sprite(4, true, 16))
            if (currentTileSize != 39 || image.GetWidth() != 16 || smaller.Size != new Vector2(16, 16) ||
                HandcartPixelDigest(image) != HandcartPixelDigest(reference))
                throw new InvalidOperationException("Medium zoom must use the approved 16px boat drawing (#914).");
        cameraZoom = 40f / baseTile; RenderMap(map); Expect(4, true);
        map = map with { WrapsEastWest = true, WorldTick = map.WorldTick + 1 }; RenderMap(map); Expect(0, true);
        Move(255, 60, "underway"); Expect(0, true);
        Move(0, 60, "underway"); Expect(6, true);
        map = map with { WorldTick = map.WorldTick + 1, Boats = [] }; RenderMap(map);
        if (mapObjectVisuals.ContainsKey("boat:boat") || boatFacings.ContainsKey("boat"))
            throw new InvalidOperationException("A removed boat must leave no marker or heading history.");
    }
}

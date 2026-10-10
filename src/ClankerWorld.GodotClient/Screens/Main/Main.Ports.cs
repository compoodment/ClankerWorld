using ClankerWorld.GodotClient.UI;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private static bool IsPortBuilding(OwnerWorldPlacedBuilding building) =>
        building.Tags?.Contains("port", StringComparer.Ordinal) == true;

    private static string PortUsageText(OwnerWorldSnapshot snapshot, OwnerWorldPlacedBuilding port)
    {
        var moored = snapshot.Boats.Count(boat => boat.DockedPortId == port.InstanceId);
        var incoming = snapshot.Boats.Count(boat => boat.DestinationPortId == port.InstanceId && boat.ReservedDock is not null);
        var waiting = snapshot.BoatRequests.Count(request => request.OriginPortId == port.InstanceId && request.Status == "waiting");
        return $"{moored + incoming} / 6 spaces used · {moored} moored · {incoming} incoming\n{waiting} waiting to depart";
    }

    private static string TownBoatText(OwnerWorldSnapshot snapshot, OwnerWorldTown town)
    {
        var boats = snapshot.Boats.Where(boat => boat.TownId == town.Id).ToArray();
        var lines = new List<string> { $"{boats.Length} communal {(boats.Length == 1 ? "boat" : "boats")}" };
        lines.AddRange(boats.Select(GameUiText.BoatDescription));
        foreach (var request in snapshot.BoatRequests.Where(request => request.BoatTownId == town.Id &&
                     request.Status is "waiting" or "underway").OrderBy(request => request.Sequence))
        {
            var boat = request.Status == "underway" ? boats.FirstOrDefault(item => item.Id == request.BoatId) : null;
            var destinationId = boat?.DestinationPortId ?? request.DestinationPortId;
            var destination = snapshot.PlacedBuildings.FirstOrDefault(port => port.InstanceId == destinationId);
            var place = destination is null ? "destination Port" : $"Port at {destination.Position.X}, {destination.Position.Y}";
            var status = request.Status == "waiting" ? "waiting to depart" : boat?.Status switch
            {
                "returning" => "returning",
                "waiting" => "waiting for a safe arrival",
                _ => "aboard",
            };
            lines.Add($"{request.PassengerName} → {place} · {status}");
        }
        return string.Join('\n', lines);
    }
}

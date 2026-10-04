using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    /// <summary>
    /// Which buildings may be lit tonight, from what the world already
    /// reports: someone is inside (a living agent stands within its
    /// footprint) or a job is running there. <see cref="NightLightShapes"/>
    /// decides what each design shows for that.
    /// </summary>
    private static List<BuildingLight> BuildingLights(OwnerWorldSnapshot snapshot)
    {
        var people = snapshot.Inhabitants
            .Where(person => !person.IsDraft && IsLiving(person))
            .Select(person => new Vector2I(person.Position.X, person.Position.Y)).ToArray();
        var working = snapshot.ProductionJobs
            .Where(job => string.Equals(job.State, "running", StringComparison.OrdinalIgnoreCase))
            .Select(job => job.BuildingInstanceId).ToHashSet(StringComparer.Ordinal);
        var lights = new List<BuildingLight>();
        foreach (var building in snapshot.PlacedBuildings)
        {
            var footprint = new Rect2I(building.Position.X, building.Position.Y,
                Math.Max(1, building.Width), Math.Max(1, building.Height));
            var kind = BuildingSprites.KindFor(building.Tags);
            if (LitDesignFor(kind) is not { } design) continue;
            var door = BuildingDoor.Facing(footprint, building.Entrance is { } entrance
                ? new Vector2I(entrance.X, entrance.Y) : null);
            var (roof, yard, middle, wing) = BuildingSprites.Plan(kind, footprint.Size.X, footprint.Size.Y, door);
            lights.Add(new BuildingLight(footprint, new LightPlan(design, roof, yard, door.Side, middle, Wing: wing),
                people.Any(footprint.HasPoint), working.Contains(building.InstanceId)) { Kind = kind, Door = door });
        }
        return lights;
    }

    /// <summary>The night-light design for a building family, or null for those with no lights.</summary>
    private static LitDesign? LitDesignFor(BuildingKind kind) => kind switch
    {
        BuildingKind.House or BuildingKind.Shelter => LitDesign.House,
        BuildingKind.Farmhouse => LitDesign.Farmhouse,
        BuildingKind.Store => LitDesign.Store,
        BuildingKind.TailorShop => LitDesign.TailorShop,
        BuildingKind.Workshop => LitDesign.Workshop,
        BuildingKind.Clinic => LitDesign.Clinic,
        BuildingKind.Restaurant => LitDesign.Restaurant,
        BuildingKind.TownHall => LitDesign.TownHall,
        BuildingKind.Blacksmith => LitDesign.Blacksmith,
        BuildingKind.Warehouse or BuildingKind.Storehouse => LitDesign.Warehouse,
        BuildingKind.Silo => LitDesign.Silo,
        BuildingKind.Generic => LitDesign.Generic,
        _ => null,
    };
}

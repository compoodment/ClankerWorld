using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

internal static class TailorTestWorld
{
    public static (PrivateWorldRuntimeState State, string ShopId) Create(string seed, int fiberInHouse)
    {
        const string household = "household:camp-alpha";
        using var generated = NormalPathWorld.CreateGenerated(seed, _ => new ActionCoverageRecorder(chooseIdle: true));
        Assert.True(generated.AdvanceOneTickAsync().AsTask().GetAwaiter().GetResult().Advanced);
        var initial = generated.ExportState();
        var inventory = InventoryFixture.AddLot(initial.Society.Society.Inventory, "shop-cost-fiber", "fiber", household, 2,
            storageBuildingId: "first-town-house-a");
        if (fiberInHouse > 0)
            inventory = InventoryFixture.AddLot(inventory, "house-fiber", "fiber", household, fiberInHouse,
                storageBuildingId: "first-town-house-a");
        using var setup = PrivateWorldRuntime.Restore(initial with
        {
            Society = initial.Society with { Society = initial.Society.Society with { Inventory = inventory } },
        }, _ => new ActionCoverageRecorder(chooseIdle: true));
        var shop = setup.WorldContent.Buildings.Single(item => item.LocalId == "tailor-shop-1x1");
        var house = setup.WorldSimulation.Buildings.Single(item => item.InstanceId == "first-town-house-a");
        var placed = Enumerable.Range(-4, 9).SelectMany(dy => Enumerable.Range(-4, 9)
                .Select(dx => new GridPoint(house.Position.X + dx, house.Position.Y + dy)))
            .OrderBy(point => Math.Abs(point.X - house.Position.X) + Math.Abs(point.Y - house.Position.Y))
            .Any(point => setup.PlaceBuilding("alpha-tailor", shop.CanonicalId, point, household).Applied);
        Assert.True(placed);
        return (PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(setup.ExportState())), "alpha-tailor");
    }
}

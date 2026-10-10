using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class GoodsCarriedReachabilityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReachableHoldingsKeepsCarriedGoodsAwayFromCamp(bool explicitCarrier)
    {
        using var generated = NormalPathWorld.CreateGenerated("cart-set-unfinished", _ => new DeterministicDecisionProvider());
        Assert.True((await generated.AdvanceOneTickAsync()).Advanced);
        var state = generated.ExportState();
        var actor = state.Inhabitants[0].InhabitantId;
        var camp = state.Map.CampObjects.FirstOrDefault(item => item.Id == "storage")?.Position ??
            state.WorldSimulation!.Buildings.First(item => item.InstanceId == "first-town-warehouse").Position;
        var remote = new GridPoint(238, 85);
        Assert.True(state.Map.IsBuildable(remote));
        Assert.False(state.Map.IsReachableOnFoot(remote, camp));
        Assert.All(state.Map.FootNeighbors(camp), point => Assert.False(state.Map.IsReachableOnFoot(remote, point)));
        var inventory = state.Society.Society.Inventory with
        {
            Lots = state.Society.Society.Inventory.Lots.Where(lot => lot.OwnerId != actor).ToArray()
        };
        inventory = InventoryFixture.AddLot(inventory, "repro-carried-wood", "wood", actor, 2);
        inventory = inventory with
        {
            Lots = inventory.Lots.Select(lot => lot.Id == "repro-carried-wood"
                ? lot with { CarrierId = explicitCarrier ? actor : null } : lot).ToArray()
        };
        state = state with
        {
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor ? person with
            { Position = remote, Equipment = null, Project = null, LastDecisionContext = null } : person).ToArray()
        };
        using var world = PrivateWorldRuntime.Restore(state, _ => new DeterministicDecisionProvider());
        var request = new GoodsRequest(GoodsUse.Holdings, actor, GoodsOwners.One(actor), GoodsKinds.One("wood"), Explain: true);
        var holding = Assert.Single(world.FindGoods(request).Matches);
        Assert.Equal(remote, holding.Place.Position);
        Assert.Equal(holding, Assert.Single(world.FindGoods(request with { Use = GoodsUse.Collect }).Matches));
        var preparation = world.FindGoods(request with { Use = GoodsUse.ReachableHoldings });
        Assert.DoesNotContain(("repro-carried-wood", GoodsReason.Route), preparation.Excluded);
        Assert.Equal(holding, Assert.Single(preparation.Matches));
    }
}

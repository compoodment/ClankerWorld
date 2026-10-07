using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed class InsufficientStormCoverTests
{
    private const string Actor = "founder:00000000000000000000000000000001";
    private static readonly GridPoint Cover = new(125, 61);
    private static readonly Lazy<Task<byte[]>> LitHouse = new(LightHouse);

    // Cover-route tests reach a forest but do not check cooling after arrival.
    // A real fuelled House and selected native warmth action must improve warmth
    // when natural storm protection is insufficient; sufficient cover stays useful.
    [Theory]
    [InlineData(WeatherKind.Storm, false)]
    [InlineData(WeatherKind.Snow, false)]
    [InlineData(WeatherKind.Storm, true)]
    public async Task SeekingWarmthCanLeaveCoolingCoverForALitHouse(WeatherKind weather, bool protectedByCloak)
    {
        var state = PrivateWorldRuntimeCodec.Decode(await LitHouse.Value);
        var house = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == "first-town-house-a");
        Assert.Contains(state.Map.Resources, resource => resource.Position == Cover &&
            resource.TreeKind is "broadleaf" or "conifer" or "orchard" &&
            state.WorldSystems!.Ecology.GetResource(resource.Id).Quantity > 0);
        state = SettlementWeatherTestFixture.WithWeather(state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == Actor ? person with
            {
                Position = Cover,
                Survival = new(3_400),
                LastDecisionContext = null,
                TravelCooldownTicks = 0,
            } : person).ToArray(),
            Society = state.Society with
            {
                Cognition = state.Society.Cognition with
                {
                    Runtimes = state.Society.Cognition.Runtimes.Select(runtime => runtime.InhabitantId == Actor
                        ? runtime with { CurrentIntention = null } : runtime).ToArray(),
                }
            },
        }, weather);
        var wanted = protectedByCloak ? "wear_clothing" : "seek_warmth";
        var policy = new MarketRulesPolicy
        {
            Choose = (actor, candidates) => candidates.FirstOrDefault(candidate => actor == Actor && candidate.Id == wanted) ??
                candidates.Single(candidate => candidate.Id == "safe_idle"),
        };
        if (protectedByCloak)
        {
            state = PaidMarketWorld.WithInventory(state, InventoryFixture.AddLot(state.Society.Society.Inventory,
                "storm-cover-cloak", "rain_cloak", Actor, 1));
            using var equipping = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)), policy.CreateProvider);
            for (var tick = 0; tick < 4 && equipping.Inhabitants.Single(person => person.InhabitantId == Actor).Equipment?.ClothingLotId != "storm-cover-cloak"; tick++)
                await equipping.AdvanceOneTickAsync();
            Assert.Equal("storm-cover-cloak", equipping.Inhabitants.Single(person => person.InhabitantId == Actor).Equipment?.ClothingLotId);
            state = equipping.ExportState() with
            {
                Inhabitants = equipping.Inhabitants.Select(person => person.InhabitantId == Actor
                ? person with { LastDecisionContext = null } : person).ToArray()
            };
            wanted = "seek_warmth";
        }
        var warmthBefore = state.Inhabitants.Single(person => person.InhabitantId == Actor).Survival!.WarmthBasisPoints;
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)), policy.CreateProvider);
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False((await world.AdvanceOneTickAsync(commitPermitted: () => false)).Advanced);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        for (var tick = 0; tick < 2; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var moving = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var continuation = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(moving), policy.CreateProvider);
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(moving), policy.CreateProvider);
        for (var tick = 2; tick < 24; tick++)
        {
            Assert.True((await continuation.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(continuation.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        }
        Assert.Contains(policy.Chosen, item => item.Actor == Actor && item.Id == "seek_warmth");
        var person = continuation.Inhabitants.Single(person => person.InhabitantId == Actor);
        var destination = protectedByCloak ? Cover : house.Position;
        Assert.True(person.Position == destination,
            $"{weather}: expected {destination}, actual {person.Position}, warmth {person.Survival!.WarmthBasisPoints}; " +
            string.Join("; ", continuation.ExportState().Events.Where(item => item.Kind == "inhabitant_moved" &&
                item.Detail.StartsWith(Actor + ":", StringComparison.Ordinal)).TakeLast(8).Select(item => item.Detail)));
        Assert.True(person.Survival!.WarmthBasisPoints > warmthBefore);
        Assert.Contains(continuation.ExportState().Survival!.Fires, fire => fire.BuildingId == house.InstanceId && fire.FuelUntilTick > continuation.WorldTick);
        Assert.Single(continuation.ExportState().Events, item => item.Kind == "fire_fuelled" && item.Detail == house.InstanceId);
        if (!protectedByCloak)
            Assert.Contains(continuation.ExportState().Events, item => item.Kind == "inhabitant_moved" && item.Detail.StartsWith(Actor + ":", StringComparison.Ordinal));
        var saved = PrivateWorldRuntimeCodec.Encode(continuation.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved));
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }

    private static async Task<byte[]> LightHouse()
    {
        var policy = new MarketRulesPolicy { Choose = (_, candidates) => candidates.Single(candidate => candidate.Id == "safe_idle") };
        using var generated = NormalPathWorld.CreateGenerated("storm-cover-warmth-audit", policy.CreateProvider);
        await generated.AdvanceOneTickAsync();
        var state = generated.ExportState();
        var house = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == "first-town-house-a");
        state = SettlementWeatherTestFixture.WithWeather(PaidMarketWorld.WithInventory(state,
            InventoryFixture.AddLot(state.Society.Society.Inventory, "storm-cover-fuel", "wood", Actor, 1)) with
        {
            Inhabitants = state.Inhabitants.Select(person => person with
            {
                Position = person.InhabitantId == Actor ? house.Position : person.Position,
                HungerBasisPoints = 10_000,
                Survival = new(person.InhabitantId == Actor ? 3_400 : 10_000),
                Equipment = null,
                LastDecisionContext = null,
            }).ToArray(),
        }, WeatherKind.Snow);
        policy.Choose = (actor, candidates) => candidates.FirstOrDefault(candidate => actor == Actor && candidate.Id == "tend_fire") ??
            candidates.Single(candidate => candidate.Id == "safe_idle");
        using var lighting = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)), policy.CreateProvider);
        for (var tick = 0; tick < 4 && !lighting.ExportState().Survival!.Fires.Any(fire => fire.BuildingId == house.InstanceId); tick++)
            await lighting.AdvanceOneTickAsync();
        Assert.DoesNotContain(lighting.Society.Inventory.Lots, lot => lot.Id == "storm-cover-fuel");
        Assert.Contains(lighting.ExportState().Events, item => item.Kind == "fire_fuelled" && item.Detail == house.InstanceId);
        return PrivateWorldRuntimeCodec.Encode(lighting.ExportState());
    }
}

using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;
using static ClankerWorld.Simulation.Tests.ShelterOrderTestFixture;

namespace ClankerWorld.Simulation.Tests;

public sealed class NaturalStormProtectionTests
{
    [Fact]
    public async Task NaturalCoverReducesStormCoolingLessThanAHouse()
    {
        var state = WithStorm(Prepared());
        var actor = Actor(state);
        var home = House(state);
        var forest = state.Map.Tiles.First(tile => state.Map.VegetationAt(tile.Position) == VegetationCover.Forest &&
            state.Map.IsPassable(tile.Position) && state.WorldSimulation!.Buildings.All(building =>
                state.Map.FootDistance(building.Position, tile.Position) > 4)).Position;
        var tree = state.Map.Resources.First(site => site.TreeKind is "broadleaf" or "conifer" &&
            state.WorldSystems!.Ecology.GetResource(site.Id).Quantity > 0 && state.Map.IsPassable(site.Position) &&
            state.Map.VegetationAt(site.Position) != VegetationCover.Forest &&
            state.WorldSimulation!.Buildings.All(building => state.Map.FootDistance(building.Position, site.Position) > 4)).Position;
        var bare = state.Map.Tiles.First(tile => state.Map.IsPassable(tile.Position) &&
            state.Map.VegetationAt(tile.Position) != VegetationCover.Forest &&
            !state.Map.Resources.Any(site => site.Position == tile.Position) &&
            state.WorldSimulation!.Buildings.All(building => state.Map.FootDistance(building.Position, tile.Position) > 4)).Position;
        var bareWarmth = await WarmthAfterTick(state, actor, bare);
        Assert.Equal(22, await WarmthAfterTick(state, actor, forest) - bareWarmth);
        Assert.Equal(22, await WarmthAfterTick(state, actor, tree) - bareWarmth);
        Assert.Equal(45, await WarmthAfterTick(state, actor, home.Position) - bareWarmth);
    }

    private static async Task<int> WarmthAfterTick(PrivateWorldRuntimeState state, string actor, GridPoint position)
    {
        state = At(state, actor, position);
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor ? person with { Survival = new(5_000) } : person).ToArray(),
        };
        using var world = Restore(state);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        return world.Inhabitants.Single(person => person.InhabitantId == actor).Survival!.WarmthBasisPoints;
    }

    private static PrivateWorldRuntime Restore(PrivateWorldRuntimeState state, string? seeker = null) =>
        PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)), id => new Choices(id == seeker));

    private sealed class Choices(bool seekWarmth) : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default) =>
            new DeterministicDecisionProvider().DecideAsync(request with
            {
                Observation = request.Observation with
                {
                    Candidates = [request.Observation.Candidates.FirstOrDefault(candidate =>
                        request.Observation.OperativeOrderInstructionId is not null && candidate.Id is "seek_shelter" or "inspect_shelter_site" ||
                        seekWarmth && candidate.Id == "seek_warmth") ??
                        request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle")],
                },
            }, cancellationToken);
    }
}

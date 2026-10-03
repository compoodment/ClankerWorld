using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class SettlementParenthoodTests
{
    [Theory]
    [InlineData("unknown_stage")]
    [InlineData("unknown_adult")]
    [InlineData("duplicate_adult")]
    [InlineData("future_stage")]
    public async Task SavedGuardianSearchRejectsInvalidStageTimingAndOfferedAdults(string damage)
    {
        using var world = PrivateWorldRuntime.Restore(await OrphanState(olderChild: true),
            _ => new ParentProvider("safe_idle"));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var state = world.ExportState();
        var childId = Assert.Single(state.Society.Society.Births).ChildId;
        var search = Assert.IsType<SettlementGuardianSearch>(
            Assert.Single(state.Inhabitants, person => person.InhabitantId == childId).GuardianSearch);
        Assert.Equal("household", search.Stage);
        Assert.NotEmpty(search.OfferedAdultIds);
        var canonical = PrivateWorldRuntimeCodec.Encode(state);
        using var validReload = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(canonical),
            _ => new ParentProvider("safe_idle"));
        Assert.Equal(canonical, PrivateWorldRuntimeCodec.Encode(validReload.ExportState()));

        var invalidSearch = damage switch
        {
            "unknown_stage" => search with { Stage = "mayor" },
            "unknown_adult" => search with { OfferedAdultIds = ["missing-adult"] },
            "duplicate_adult" => search with { OfferedAdultIds = [search.OfferedAdultIds[0], search.OfferedAdultIds[0]] },
            "future_stage" => search with { StageStartedTick = state.Society.Society.WorldTick + 1 },
            _ => throw new ArgumentOutOfRangeException(nameof(damage)),
        };
        var damaged = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == childId
                ? person with { GuardianSearch = invalidSearch }
                : person).ToArray(),
        };
        var error = Assert.Throws<InvalidDataException>(() =>
            PrivateWorldRuntime.Restore(damaged, _ => new ParentProvider("safe_idle")));
        Assert.Contains("guardian search", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(canonical, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
    }
}

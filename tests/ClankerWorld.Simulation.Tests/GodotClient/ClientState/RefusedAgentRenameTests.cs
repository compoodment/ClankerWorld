using ClankerWorld.GodotClient.ClientState;

namespace ClankerWorld.Simulation.Tests;

public sealed class RefusedAgentRenameTests
{
    [Theory]
    [InlineData("world:b", "agent:rowan", "Aster Vale")]
    [InlineData("world:a", "agent:aster", "Aster Vale")]
    [InlineData("world:a", "agent:rowan", "Aster Vale Jr")]
    [InlineData(null, "agent:rowan", "Aster Vale")]
    public void RefusedNameSurvivesRefreshesOnlyForTheSameFieldAgentAndWorld(string? worldId, string agentId, string fieldText)
    {
        var refused = new RefusedAgentRename();
        refused.Remember("world:a", "agent:rowan", "Aster Vale");
        Assert.True(refused.Keeps("world:a", "agent:rowan", "Aster Vale"));
        Assert.True(refused.Keeps("world:a", "agent:rowan", "Aster Vale"));

        Assert.False(refused.Keeps(worldId, agentId, fieldText));
        Assert.False(refused.Keeps("world:a", "agent:rowan", "Aster Vale"));
    }

    [Fact]
    public void ForgottenOrUnrecordedNameIsNeverKept()
    {
        var refused = new RefusedAgentRename();
        Assert.False(refused.Keeps("world:a", "agent:rowan", "Aster Vale"));
        refused.Remember("world:a", "agent:rowan", "Aster Vale");
        refused.Forget();
        Assert.False(refused.Keeps("world:a", "agent:rowan", "Aster Vale"));
    }
}

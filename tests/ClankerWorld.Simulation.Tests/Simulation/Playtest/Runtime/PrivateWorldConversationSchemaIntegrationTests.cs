using System.Text;
using System.Text.Json.Nodes;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class PrivateWorldConversationTests
{
    [Theory]
    [InlineData("conversations", false)]
    [InlineData("conversations", true)]
    [InlineData("conversationBudgets", false)]
    [InlineData("conversationBudgets", true)]
    public void CurrentSchemaRequiresExplicitConversationHistoryAndDailyBudgets(string member, bool explicitNull)
    {
        using var world = NewWorld("required-current-conversation-state", ConsentReviewProvider());
        var current = JsonNode.Parse(PrivateWorldRuntimeCodec.Encode(world.ExportState()))!;
        if (explicitNull) current["state"]![member] = null;
        else Assert.True(current["state"]!.AsObject().Remove(member));
        var invalid = Encoding.UTF8.GetBytes(current.ToJsonString());
        var error = Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Decode(invalid));
        Assert.Contains($"schema {PrivateWorldRuntime.ConversationSchemaVersion}", error.Message, StringComparison.Ordinal);
        Assert.Contains("conversation state and daily budgets", error.Message, StringComparison.Ordinal);
    }
}

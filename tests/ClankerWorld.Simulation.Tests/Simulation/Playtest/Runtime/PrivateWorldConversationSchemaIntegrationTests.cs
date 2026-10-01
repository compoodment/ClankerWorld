using System.Text;
using System.Text.Json.Nodes;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Observation;

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

    [Fact]
    public void ConversationSchemaCutoffKeepsRejectedFieldSaveBytesAndLoadsTheRepairedCurrentCombination()
    {
        var directory = Directory.CreateTempSubdirectory("conversation-field-schema-cutoff-");
        try
        {
            var (state, farmer, household, point) = FarmFieldTests.PreparedFarmer("conversation-field-cutoff");
            var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory,
                "conversation-ground-harvest", FarmFieldRules.Grain, household, 1,
                groundPosition: new(point.X, point.Y));
            state = FarmFieldTests.WithInventory(state, inventory) with
            {
                Fields = [new(point, household, FarmFieldStage.Prepared)],
            };
            var other = state.Inhabitants.First(person => person.InhabitantId != farmer).InhabitantId;
            state = WithAcceptedFarmConversation(state, farmer, other, "conversation:combined-cutoff");
            var currentBytes = PrivateWorldRuntimeCodec.Encode(state);
            var previous = JsonNode.Parse(currentBytes)!;
            previous["state"]!["schemaVersion"] = PrivateWorldRuntime.StateSchemaVersion - 1;
            Assert.True(previous["state"]!.AsObject().Remove("conversations"));
            Assert.True(previous["state"]!.AsObject().Remove("conversationBudgets"));
            var olderBytes = Encoding.UTF8.GetBytes(previous.ToJsonString());
            var path = Path.Combine(directory.FullName, "world.json");
            File.WriteAllBytes(path, olderBytes);
            var file = new PrivateWorldStateFile(path);

            var rejected = Assert.Throws<InvalidDataException>(() => file.LoadOrCreate(state.WorldSeed));
            Assert.Contains($"minimum supported schema {PrivateWorldRuntime.StateSchemaVersion}", rejected.Message,
                StringComparison.Ordinal);
            Assert.Equal(olderBytes, File.ReadAllBytes(path));

            File.WriteAllBytes(path, currentBytes);
            using var restored = file.LoadOrCreate(state.WorldSeed);
            Assert.Equal(PrivateWorldRuntime.ConversationSchemaVersion, restored.ExportState().SchemaVersion);
            Assert.Equal(FarmFieldStage.Prepared, Assert.Single(restored.Fields).Stage);
            Assert.Equal(new InventoryGroundPosition(point.X, point.Y),
                restored.Society.Inventory.GetLot("conversation-ground-harvest").GroundPosition);
            var conversation = Assert.Single(restored.Conversations);
            Assert.Equal("conversation:combined-cutoff", conversation.Id);
            Assert.Equal(AgentConversationStatus.Suspended, conversation.Status);
            Assert.Empty(conversation.ResumeAcceptedBy);
            Assert.Equal(2, restored.ConversationBudgets.Count);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(restored.ExportState()), File.ReadAllBytes(path));
        }
        finally { directory.Delete(recursive: true); }
    }
}

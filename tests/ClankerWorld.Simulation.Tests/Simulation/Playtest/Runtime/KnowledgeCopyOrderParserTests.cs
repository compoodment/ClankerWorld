using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class KnowledgeCopyOrderParserTests
{
    [Theory]
    [InlineData("copy a map", "field_map", 1, false)]
    [InlineData("copy a field record", "field_record", 1, false)]
    [InlineData("copy a book", "book", 1, false)]
    [InlineData("please copy two records!", "field_record", 2, false)]
    [InlineData("copy 3 field maps", "field_map", 3, false)]
    [InlineData("keep copying books", "book", 1, true)]
    [InlineData("repeat copy 2 maps", "field_map", 2, true)]
    [InlineData("copy a book until cancelled", "book", 1, true)]
    public void RecognizesOnlyCompleteCopyTasks(string text, string kind, int quantity, bool repeat)
    {
        var order = PrivateWorldInstructionOrderParser.Parse(text, [], resource => resource.Kind);
        Assert.NotNull(order);
        Assert.Equal(("copy_knowledge", kind, quantity, "copies", repeat),
            (order.Action, order.TargetKnowledgeKind, order.RequestedUnits, order.ProgressUnit, order.RepeatUntilCancelled));
        Assert.Null(order.KnowledgeCopySourceArtifactId);
    }

    [Theory]
    [InlineData("do not copy a map")]
    [InlineData("copy a field book")]
    [InlineData("copy a map and a book")]
    [InlineData("copy zero maps")]
    [InlineData("copy 1001 maps")]
    [InlineData("copy a map at 1,2")]
    [InlineData("copy a map of an unknown Town")]
    [InlineData("copy someone else's book")]
    [InlineData("keep copy a map")]
    public void UnsupportedCopyTextIsNotGuessed(string text) =>
        Assert.Null(PrivateWorldInstructionOrderParser.Parse(text, [], resource => resource.Kind));
}

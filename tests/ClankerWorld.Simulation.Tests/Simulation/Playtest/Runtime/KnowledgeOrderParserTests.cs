using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class KnowledgeOrderParserTests
{
    [Theory]
    [InlineData("write a field record", "field_record", 1, false)]
    [InlineData("draw a map", "field_map", 1, false)]
    [InlineData("bind a book", "book", 1, false)]
    [InlineData("please write two records!", "field_record", 2, false)]
    [InlineData("write 3 field maps", "field_map", 3, false)]
    [InlineData("keep writing records", "field_record", 1, true)]
    [InlineData("keep drawing maps", "field_map", 1, true)]
    [InlineData("repeat bind 2 books", "book", 2, true)]
    [InlineData("write a book until cancelled", "book", 1, true)]
    public void RecognizesOnlyCompleteWritingTasks(string text, string kind, int quantity, bool repeat)
    {
        var order = PrivateWorldInstructionOrderParser.Parse(text, [], resource => resource.Kind);
        Assert.NotNull(order);
        Assert.Equal(("write_knowledge", kind, quantity, "artifacts", repeat),
            (order.Action, order.TargetKnowledgeKind, order.RequestedUnits, order.ProgressUnit, order.RepeatUntilCancelled));
    }

    [Theory]
    [InlineData("do not write a map")]
    [InlineData("draw a book")]
    [InlineData("bind a record")]
    [InlineData("write a field book")]
    [InlineData("write a map and a book")]
    [InlineData("write zero maps")]
    [InlineData("write 0 maps")]
    [InlineData("write 1001 maps")]
    [InlineData("write a map at 1,2")]
    [InlineData("write a map of an unknown Town")]
    [InlineData("copy a map")]
    [InlineData("keep draw a map")]
    public void UnsupportedWritingTextIsNotGuessed(string text) =>
        Assert.Null(PrivateWorldInstructionOrderParser.Parse(text, [], resource => resource.Kind));
}

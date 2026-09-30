using ClankerWorld.GodotClient.UI;

namespace ClankerWorld.Simulation.Tests;

public sealed class WorldEventTextTests
{
    private const string FounderId = "founder:00000000000000000000000000000001";
    private const string AgentId = "agent:00000000000000000000000000000099";
    private const string ChildId = "world:inhabitant:birth:" + FounderId + ":" + AgentId + ":1";

    [Theory]
    [InlineData(FounderId)]
    [InlineData(AgentId)]
    [InlineData(ChildId)]
    [InlineData("founder-scout")]
    public void ActorEventsKeepCompleteIdsAndUseCurrentLivingOrDeceasedNames(string id)
    {
        var snapshot = Snapshot(Person(id, "Aster"));
        var cases = new[]
        {
            ("food_harvested", id + ":4", "gathered food"),
            ("food_consumed", id, "ate"),
            ("inhabitant_slept", id, "slept"),
            ("child_born", id, "was born"),
            ("inhabitant_removed", id, "died"),
            ("inhabitant_building_proposed", id + ":house", "suggested a new building design"),
            ("instruction_not_understood", id + ":private-instruction-0000000001",
                "didn't understand your order. For now, orders can only ask them to gather food, eat or find food"),
        };
        foreach (var (kind, detail, action) in cases)
        {
            var worldEvent = new OwnerWorldEvent(1, 1, kind, detail);
            Assert.Equal($"Aster {action}.", WorldEventText.Describe(worldEvent, snapshot));
            var renamed = snapshot with { Inhabitants = [Person(id, "Rowan", "dead")] };
            Assert.Equal($"Rowan {action}.", WorldEventText.Describe(worldEvent, renamed));
            Assert.Equal(detail, worldEvent.Detail);
        }
    }

    [Theory]
    [InlineData("town:first", FounderId)]
    [InlineData("town:first", AgentId)]
    [InlineData("town:first", ChildId)]
    [InlineData("legacy-town", "founder-scout")]
    public void TownResidentEventsReadTownAndResidentAsCompleteIdentities(string townId, string actorId)
    {
        var snapshot = Snapshot(Person(actorId, "Aster", "dead")) with
        {
            Towns = [new(townId, "First Town", "founded", 0, [], [], [])],
        };
        Assert.Equal("Aster joined the first Town.", WorldEventText.Describe(
            new(1, 0, "town_resident_joined", $"{townId}:{actorId}:child_joined:residents:4"), snapshot));
        Assert.Equal("Aster left the first Town.", WorldEventText.Describe(
            new(2, 1, "town_resident_left", $"{townId}:{actorId}:residents:3"), snapshot));
    }

    [Fact]
    public void KnownPrefixesNeverReplaceAnExactDifferentActorIdentity()
    {
        var snapshot = Snapshot(Person("agent", "Wrong prefix"), Person(AgentId, "Aster"),
            Person("legacy-parent", "Parent"), Person("legacy-parent:child", "Child")) with
        {
            Towns = [new("town", "Wrong Town", "founded", 0, [], [], []),
                new("town:first", "First Town", "founded", 0, [], [], [])],
        };
        Assert.Equal("Aster gathered food.", WorldEventText.Describe(new(1, 0, "food_harvested", AgentId + ":4"), snapshot));
        Assert.Equal("Someone died.", WorldEventText.Describe(new(2, 0, "inhabitant_removed", AgentId + ":unknown-child"), snapshot));
        Assert.Equal("Child suggested a new building design.", WorldEventText.Describe(
            new(3, 0, "inhabitant_building_proposed", "legacy-parent:child:house"), snapshot));
        Assert.Equal("Child joined the first Town.", WorldEventText.Describe(
            new(4, 0, "town_resident_joined", "town:first:legacy-parent:child:child_joined:residents:1"), snapshot));
    }

    [Fact]
    public void MissingSnapshotStillSupportsDelimiterFreeLegacyNamesAndSafeUnknownActors()
    {
        Assert.Equal("Scout ate.", WorldEventText.Describe(new(1, 0, "food_consumed", "scout"), null));
        Assert.Equal("Someone died.", WorldEventText.Describe(new(2, 0, "inhabitant_removed", AgentId), null));
        Assert.Equal("Someone ate.", WorldEventText.Describe(new(3, 0, "food_consumed", ""), null));
    }

    private static OwnerWorldSnapshot Snapshot(params OwnerWorldInhabitant[] people) =>
        new("event-names", 1, "map", [], [], [], null, 1) { Inhabitants = people };

    private static OwnerWorldInhabitant Person(string id, string name, string lifecycle = "active") =>
        new(id, name, lifecycle, new(0, 0), 8_000, [], [],
            new("idle", null, null, [], ""), new(new(0, 0), [], []), false);
}

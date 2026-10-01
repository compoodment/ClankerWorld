using System.Text;
using System.Text.Json.Nodes;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class TownHallTests
{
    [Theory]
    [InlineData("null")]
    [InlineData("empty")]
    public async Task AStartedNativeTownCannotLoseItsPendingCouncilThroughAnyRestoreBoundary(string omission)
    {
        var state = CivicCalendar(AtHall(WithHall(Initial())));
        using var world = PrivateWorldRuntime.Restore(state, _ => new CivicChooser(false, false));
        var council = Assert.Single(world.TownCouncils);
        Assert.Equal(4, council.MemberIds.Count);
        Assert.Null(council.Election);
        Assert.Null(council.TermStartedTick);
        var hall = Assert.Single(world.WorldSimulation.Buildings,
            item => item.DefinitionId == TownHallContent.TownHall().CanonicalId);
        Assert.Equal(Town, hall.TownId);
        Assert.Null(hall.HouseholdId);
        var cost = world.Society.Inventory.Reservations.Where(item => item.Purpose == "building:test-town-hall").ToArray();
        Assert.Equal(36, cost.Sum(item => item.Quantity));
        Assert.All(cost, item =>
        {
            Assert.Equal(Alpha, item.OwnerId);
            Assert.Equal(InventoryReservationState.Completed, item.State);
        });
        var footprint = WorldContentSimulationRules.Footprint(TownHallContent.TownHall(), hall).ToArray();
        Assert.All(council.MemberIds, actor => Assert.Contains(footprint, point =>
            state.Map.FootDistance(world.Inhabitants.Single(person => person.InhabitantId == actor).Position, point) <= 1));

        var adults = council.MemberIds.ToArray();
        const string text = "Let each speaker finish before replying.";
        Assert.True(world.ProposeTownLaw(adults[0], Town, "quiet_meetings", text).Applied);
        Assert.True(world.VoteTownLaw(adults[1], Town, true).Applied);
        var original = world.TownCouncils.Single(item => item.TownId == Town).Ballot!;
        var beforeTicks = world.WorldTick;
        await AdvanceTo(world, beforeTicks + 2);
        Assert.Equal(beforeTicks + 2, world.WorldTick);
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), _ => new CivicChooser(false, false));
        restored.Validate();
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        var positive = restored.ExportState();
        Assert.Equal(PrivateWorldRuntime.StateSchemaVersion, positive.SchemaVersion);
        Assert.True(positive.FounderSetup!.Started);
        var pending = Assert.Single(positive.TownCouncils!).Ballot!;
        Assert.Equal(original.ProposedTick, pending.ProposedTick);
        Assert.Equal(original.ExpiryTick, pending.ExpiryTick);
        Assert.Equal(adults[0], pending.ProposerId);
        Assert.Equal(text, pending.Text);
        Assert.Equal([adults[1]], pending.Approvals);
        Assert.Empty(pending.Rejections);
        Assert.True(restored.WorldTick < pending.ExpiryTick);

        // Mutate only the already-proven council collection, preserving its actual Town and stock.
        var invalid = positive with { TownCouncils = omission == "null" ? null : [] };
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(invalid));
        Assert.Throws<InvalidDataException>(() =>
        {
            using var rejected = PrivateWorldRuntime.Restore(invalid, _ => new CivicChooser(false, false));
        });

        var document = Assert.IsType<JsonObject>(JsonNode.Parse(bytes));
        var encodedState = Assert.IsType<JsonObject>(document["state"]);
        Assert.NotEmpty(Assert.IsType<JsonArray>(encodedState["townCouncils"]));
        encodedState["townCouncils"] = omission == "null" ? null : new JsonArray();
        var damaged = Encoding.UTF8.GetBytes(document.ToJsonString());
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Decode(damaged));
        if (omission == "null")
        {
            Assert.True(encodedState.Remove("townCouncils"));
            var missing = Encoding.UTF8.GetBytes(document.ToJsonString());
            Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Decode(missing));
        }

        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }
}

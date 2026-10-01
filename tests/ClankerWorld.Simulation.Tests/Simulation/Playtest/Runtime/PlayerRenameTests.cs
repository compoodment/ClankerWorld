using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Tests;

public sealed class PlayerRenameTests
{
    private const string First = "founder:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Second = "founder:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    [Theory]
    [InlineData("Élodie Vale", false)]
    [InlineData("  e\u0301LODIE\u00a0  Vale  ", false)]
    [InlineData("Élodie Vale", true)]
    public void PlayerCannotTakeAnotherPersonsFullName(string proposed, bool deceased)
    {
        using var initial = NewWorld();
        initial.RenameAgent(First, "Élodie Vale");
        var saved = initial.ExportState();
        if (deceased)
        {
            var physical = saved.Inhabitants.Single(person => person.InhabitantId == First);
            var society = SocietyFixture.Kill(saved.Society.Society, First, SocietyDeathCause.Accident).Checkpoint;
            saved = saved with
            {
                Society = saved.Society with { Society = society },
                Inhabitants = saved.Inhabitants.Where(person => person.InhabitantId != First).ToArray(),
                DeceasedInhabitants = [new(First, 0, society.AgeAt(society.GetInhabitant(First), 0), physical)],
                Towns = saved.Towns!.Select(town => town with
                { ResidentIds = town.ResidentIds.Where(id => id != First).ToArray() }).ToArray(),
            };
        }
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(saved)));
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.ThrowsAny<InvalidOperationException>(() => world.RenameAgent(Second, proposed));
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        Assert.True(world.RenameAgent(Second, "Élodie Lake"));
    }

    [Fact]
    public void ConfirmingOwnUnchosenPlaceholderRetainsPlayerPrecedenceWithoutClaimingAnotherName()
    {
        using var initial = NewWorld();
        var saved = initial.ExportState();
        saved = saved with
        {
            Society = saved.Society with
            {
                Society = saved.Society.Society with
                {
                    Inhabitants = saved.Society.Society.Inhabitants.Select(person =>
                        person.Id is First or Second ? person with { Name = "New agent" } : person).ToArray(),
                },
            },
        };
        using var world = PrivateWorldRuntime.Restore(saved);
        Assert.True(world.Society.GetInhabitant(First).NeedsName);
        Assert.True(world.RenameAgent(First, "New agent"));
        Assert.False(world.Society.GetInhabitant(First).NeedsName);
        Assert.Equal("New agent", world.Society.GetInhabitant(First).Name);
        Assert.True(world.Society.GetInhabitant(Second).NeedsName);
        var beforeRetry = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False(world.RenameAgent(First, "New agent"));
        Assert.Equal(beforeRetry, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        Assert.Throws<InhabitantNameTakenException>(() => world.RenameAgent(First, "NEW AGENT"));
    }

    [Fact]
    public void RenamePreservesIdentityAndHistoricalSpeechAcrossReloadAndRetry()
    {
        using var initial = NewWorld();
        initial.RenameAgent(First, "Rowan Vale");
        var saved = initial.ExportState();
        var conversation = AgentConversationRules.Propose("conversation:rename-history", First, Second, 0, 0) with
        {
            Status = AgentConversationStatus.Closed,
            AcceptedParticipantIds = [First, Second],
            Turns = [new("conversation:rename-history:turn:1", First, "I am Rowan Vale.", 0,
                [Second], AgentConversationDisposition.Withdraw)],
            Outcome = "withdrawn",
        };
        saved = saved with { Conversations = [conversation] };
        using var world = PrivateWorldRuntime.Restore(saved);
        var person = world.Society.GetInhabitant(First);
        Assert.True(world.RenameAgent(First, "Rowan Lake"));
        Assert.Equal(person with { Name = "Rowan Lake" }, world.Society.GetInhabitant(First));
        Assert.Equal(saved.Society.Society.Households, world.Society.Households);
        Assert.Equal(saved.Society.Society.Relationships, world.Society.Relationships);
        Assert.Equal(saved.Society.Society.Inventory, world.Society.Inventory);
        Assert.Equal("I am Rowan Vale.", Assert.Single(Assert.Single(world.Conversations).Turns).Text);
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(
            PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        var beforeRetry = PrivateWorldRuntimeCodec.Encode(restored.ExportState());
        Assert.False(restored.RenameAgent(First, "Rowan Lake"));
        Assert.Equal(beforeRetry, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        Assert.Equal(First, restored.Society.GetInhabitant(First).Id);
        Assert.Equal("I am Rowan Vale.", Assert.Single(Assert.Single(restored.Conversations).Turns).Text);
    }

    private static PrivateWorldRuntime NewWorld()
    {
        var world = new PrivateWorldRuntime("player-rename", startPace: WorldStartPace.FounderSetup);
        world.PlaceFounder(First, new GridPoint(0, 0));
        world.PlaceFounder(Second, new GridPoint(1, 2));
        world.PlaceFounder("founder:cccccccccccccccccccccccccccccccc", new GridPoint(2, 2));
        world.PlaceFounder("founder:dddddddddddddddddddddddddddddddd", new GridPoint(3, 2));
        world.StartWorld(resume: false);
        return world;
    }
}

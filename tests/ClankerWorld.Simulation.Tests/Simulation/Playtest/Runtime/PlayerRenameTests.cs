using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class PlayerRenameTests
{
    private const string First = "founder:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Second = "founder:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    [Theory]
    [InlineData("Élodie Lake", false)]
    [InlineData("  e\u0301LODIE\u00a0  Lake  ", false)]
    [InlineData("Élodie Lake", true)]
    public void PlayerCannotTakeAnotherPersonsChosenFirstName(string proposed, bool deceased)
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
                Towns = saved.Towns!.Select(town =>
                {
                    var residents = town.ResidentIds.Where(id => id != First).ToArray();
                    var adults = residents.Where(id => society.Inhabitants.Any(person => person.Id == id &&
                        person.Status == SocietyInhabitantStatus.Active &&
                        person.AgeBand is SocietyAgeBand.Adult or SocietyAgeBand.Elder));
                    // Complete the council transition that follows death in the normal runtime.
                    return town with
                    {
                        ResidentIds = residents,
                        Governance = TownGovernanceRules.Advance(town.Governance!, town.Id, saved.WorldSeed,
                            adults, society.WorldTick, saved.WorldSystems!.Config.TicksPerDay),
                    };
                }).ToArray(),
            };
        }
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(saved)));
        if (deceased)
        {
            Assert.True(world.RenameAgent(First, "Élodie Shore"));
            Assert.Contains(world.Society.Memories, item => item.OwnerId == First && item.Permanent &&
                item.Summary == "I was renamed on day 1 to Élodie Shore.");
        }
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.Throws<InhabitantNameTakenException>(() => world.RenameAgent(Second, proposed));
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        Assert.True(world.RenameAgent(Second, "Marin Lake"));
        Assert.True(world.Society.GetInhabitant(Second).HasChosenName);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChoosingPlaceholderTextClaimsItsFirstNameEvenAfterNamingCloses(bool namingClosed)
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
        if (namingClosed)
            saved = saved with
            {
                Society = saved.Society with
                {
                    Society = SocietyFixture.CloseNaming(SocietyFixture.CloseNaming(
                        saved.Society.Society, First).Checkpoint, Second).Checkpoint,
                },
            };
        using var world = PrivateWorldRuntime.Restore(saved);
        Assert.Equal(!namingClosed, world.Society.GetInhabitant(First).NeedsName);
        Assert.False(world.Society.GetInhabitant(First).HasChosenName);
        Assert.False(world.Society.GetInhabitant(Second).HasChosenName);
        Assert.True(world.RenameAgent(First, "New agent"));
        Assert.False(world.Society.GetInhabitant(First).NeedsName);
        Assert.True(world.Society.GetInhabitant(First).HasChosenName);
        Assert.Equal("New agent", world.Society.GetInhabitant(First).Name);
        Assert.Equal(!namingClosed, world.Society.GetInhabitant(Second).NeedsName);
        var beforeRetry = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False(world.RenameAgent(First, "New agent"));
        Assert.Equal(beforeRetry, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        Assert.True(world.RenameAgent(First, "NEW AGENT"));
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(
            PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        Assert.True(restored.Society.GetInhabitant(First).HasChosenName);
        Assert.False(restored.Society.GetInhabitant(Second).HasChosenName);
        var beforeConflict = PrivateWorldRuntimeCodec.Encode(restored.ExportState());
        Assert.Throws<InhabitantNameTakenException>(() => restored.RenameAgent(Second, "New Lake"));
        Assert.Equal(beforeConflict, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
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
        Assert.Contains(world.Society.Memories, memory => memory.OwnerId == First &&
            memory.Summary == "I was renamed on day 1 to Rowan Lake.");
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
        var history = restored.Society.Memories.Where(item => item.OwnerId == First).ToArray();
        Assert.Equal(2, history.Length);
        Assert.All(history, memory =>
        {
            Assert.True(memory.Permanent);
            Assert.Equal(First, memory.SubjectId);
            Assert.Equal("private", memory.Visibility);
            Assert.Null(memory.TombstonedTick);
        });
        Assert.Contains(history, item => item.Summary == "I was renamed on day 1 to Rowan Vale.");
        Assert.Contains(history, item => item.Summary == "I was renamed on day 1 to Rowan Lake.");
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

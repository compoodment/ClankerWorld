using System.Collections;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class PrivateWorldConversationTests
{
    [Fact]
    public async Task OrdinaryConversationBeliefHistoryValidatesWithLinearListVisits()
    {
        var state = await ConversationBeliefStateAsync();
        var checkpoint = state.Society.Society;
        var beliefs = checkpoint.Beliefs!;
        var bytes = SocietyCheckpointCodec.Encode(checkpoint);
        Assert.Equal(bytes, SocietyCheckpointCodec.Encode(SocietyCheckpointCodec.Decode(bytes)));
        var counted = new CountedBeliefs(beliefs);
        SocietyFixture.Validate(checkpoint with { Beliefs = counted });
        Assert.True(counted.Visits <= 12L * beliefs.Count,
            $"Validation visited {counted.Visits} belief records for {beliefs.Count} ordinary conversation beliefs.");
        Assert.Equal(bytes, SocietyCheckpointCodec.Encode(checkpoint));
    }

    [Fact]
    public async Task IndexedValidationKeepsCorrectionHistoryCompactionReferencesAndRejectsCorruptSources()
    {
        var state = await ConversationBeliefStateAsync();
        var checkpoint = state.Society.Society;
        var original = checkpoint.Beliefs![0];
        var previous = original;
        var originalEvents = checkpoint.Events;
        for (var correction = 0; correction < 32; correction++)
        {
            var replacement = previous with
            {
                Id = $"belief:validation-correction:{correction:D3}",
                Statement = $"A private clarification of the heard claim, version {correction}.",
                SourceTurnId = correction % 2 == 0 ? original.SourceTurnId : null,
            };
            checkpoint = SocietyFixture.CorrectAgentBelief(checkpoint, original.OwnerId, previous.Id, replacement);
            previous = checkpoint.Beliefs!.Single(item => item.Id == replacement.Id);
        }
        checkpoint = SocietyFixture.RecordAgentMemoryCompaction(checkpoint, original.OwnerId,
        [new SocietyAgentMemoryImportance(original.Id, SocietyMemorySourceKind.Belief,
            original.FormedTick, 8_000, 8_500, checkpoint.WorldTick)]);
        Assert.Equal(state.Society.Society.Beliefs!.Count + 32, checkpoint.Beliefs!.Count);
        Assert.Equal(originalEvents, checkpoint.Events);
        Assert.Contains(checkpoint.MemoryCompactions!.Single().Sources, item => item.SourceId == original.Id);
        var counted = new CountedBeliefs(checkpoint.Beliefs);
        SocietyFixture.Validate(checkpoint with { Beliefs = counted });
        Assert.True(counted.Visits <= 12L * counted.Count, $"Correction history required {counted.Visits} list visits for {counted.Count} beliefs.");
        var bytes = SocietyCheckpointCodec.Encode(checkpoint);
        Assert.Equal(bytes, SocietyCheckpointCodec.Encode(SocietyCheckpointCodec.Decode(bytes)));
        var complete = state with { Society = state.Society with { Society = checkpoint } };
        var fullBytes = PrivateWorldRuntimeCodec.Encode(complete);
        Assert.Equal(fullBytes, PrivateWorldRuntimeCodec.Encode(PrivateWorldRuntimeCodec.Decode(fullBytes)));

        Assert.Throws<InvalidDataException>(() => SocietyFixture.RecordAgentBelief(checkpoint,
            original with { Id = "belief:unrelated-duplicate", SupersededByBeliefId = null, SupersededTick = null }));
        var duplicate = original with { Id = "belief:unrelated-duplicate", SupersededByBeliefId = null, SupersededTick = null };
        Assert.Throws<InvalidDataException>(() => SocietyFixture.Validate(checkpoint with
        {
            Beliefs = checkpoint.Beliefs.Append(duplicate).OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(),
        }));
        void Reject(SocietyAgentBelief changed) => Assert.Throws<InvalidDataException>(() => SocietyFixture.Validate(checkpoint with
        {
            Beliefs = checkpoint.Beliefs.Select(item => item.Id == original.Id ? changed : item).ToArray(),
        }));
        Reject(original with { OwnerId = "unknown-owner" });
        Reject(original with { SourceAgentId = "unknown-reporter" });
        Reject(original with { AboutInhabitantId = "unknown-subject" });
        Reject(original with { SupersedesBeliefId = "belief:missing" });
        Reject(original with { SupersededByBeliefId = "belief:missing", SupersededTick = checkpoint.WorldTick });

        // Even equal-tick reciprocal links must not admit a correction cycle.
        var pair = state.Society.Society;
        pair = SocietyFixture.CorrectAgentBelief(pair, original.OwnerId, original.Id,
            original with { Id = "belief:cyclic-correction" });
        var ids = new[] { original.Id, "belief:cyclic-correction" };
        var cyclic = pair with
        {
            Beliefs = pair.Beliefs!.Select(item => ids.Contains(item.Id) ? item with
            {
                SourceTurnId = null,
                FormedTick = pair.WorldTick,
                SupersedesBeliefId = item.Id == ids[0] ? ids[1] : ids[0],
                SupersededByBeliefId = item.Id == ids[0] ? ids[1] : ids[0],
                SupersededTick = pair.WorldTick,
            } : item).ToArray(),
        };
        Assert.Throws<InvalidDataException>(() => SocietyFixture.Validate(cyclic));
        Assert.Throws<InvalidDataException>(() => SocietyCheckpointCodec.Encode(cyclic));
    }

    private static async Task<PrivateWorldRuntimeState> ConversationBeliefStateAsync()
    {
        var provider = new ConversationProvider(new HashSet<string>(StringComparer.Ordinal)
        {
            InitiatorId, InviteeId,
        });
        using var world = NewWorld("conversation-belief-validation", _ => provider);
        world.StartWorld();
        for (var attempt = 0; attempt < 80 && (world.Society.Beliefs ?? []).Count(item => item.SourceTurnId is not null) < 12; attempt++)
        {
            _ = await world.AdvanceOneTickNonBlockingAsync();
            await Task.Delay(5);
        }
        var beliefs = world.Society.Beliefs!;
        Assert.True(beliefs.Count(item => item.SourceTurnId is not null) >= 12,
            $"Expected ordinary conversation beliefs; got {beliefs.Count} beliefs from {provider.TurnRequests.Count} requested turns.");
        Assert.All(beliefs, belief => Assert.Contains(world.Conversations.SelectMany(item => item.Turns),
            turn => turn.Id == belief.SourceTurnId && turn.ListenerIds.Contains(belief.OwnerId)));
        return world.ExportState();
    }

    private sealed class CountedBeliefs(IReadOnlyList<SocietyAgentBelief> source) : IReadOnlyList<SocietyAgentBelief>
    {
        public long Visits { get; private set; }
        public int Count => source.Count;
        public SocietyAgentBelief this[int index]
        {
            get { Visits++; return source[index]; }
        }
        public IEnumerator<SocietyAgentBelief> GetEnumerator()
        {
            foreach (var item in source) { Visits++; yield return item; }
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class PrivateWorldConversationTests
{
    private static readonly string[] MarriageSurnameOptions = ["Ash", "Reed"];

    [Fact]
    public async Task UnicodeExpansionCannotAcceptOrRestoreAMarriageWithoutAnyFittingSurname()
    {
        var first = new MarriagePersonalProvider(InitiatorId);
        var second = new MarriagePersonalProvider(InviteeId);
        IDecisionProvider Route(string id) => id == InitiatorId ? first : id == InviteeId ? second : new DeterministicDecisionProvider();
        using var world = MarriedWorldSetup("unicode-marriage-eligibility", Route);
        var expandingName = new string('\u0344', 24) + " Q";
        Assert.True(world.RenameAgent(InitiatorId, expandingName));
        Assert.Equal(expandingName, world.Society.GetInhabitant(InitiatorId).Name);
        world.Validate(); // Existing naming accepts this raw name, whose canonical form expands.
        Assert.False(AgentMarriageRules.CanPropose(world.Society, world.Marriages, InitiatorId, InviteeId));
        Assert.Empty(world.Marriages);

        using var accepted = MarriedWorldSetup("unicode-marriage-receipt", Route);
        await AdvanceMarriageUntil(accepted, () => accepted.Marriages.Count == 1);
        accepted.Pause();
        var saved = accepted.ExportState();
        var marriage = Assert.Single(saved.Marriages) with { InitiatorNameAtAcceptance = expandingName };
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(saved with { Marriages = [marriage] }));
    }

    [Fact]
    public async Task LongOriginalNamesOnlyOfferSurnamesThatFitBothAndFinishWithoutRepeatedCalls()
    {
        var first = new MarriagePersonalProvider(InitiatorId) { UseAllowedSurnameOptions = true, DisagreeOnSurname = true };
        var second = new MarriagePersonalProvider(InviteeId) { UseAllowedSurnameOptions = true, DisagreeOnSurname = true };
        IDecisionProvider Route(string id) => id == InitiatorId ? first : id == InviteeId ? second : new DeterministicDecisionProvider();
        using var world = MarriedWorldSetup("length-safe-marriage", Route);
        var firstName = new string('A', 40);
        var longSurname = new string('S', 30);
        Assert.True(world.RenameAgent(InitiatorId, firstName + " Ash"));
        Assert.True(world.RenameAgent(InviteeId, "Rowan " + longSurname));
        await AdvanceMarriageUntil(world, () => world.Marriages.Any(item => item.CompletedTick is not null));

        var marriage = Assert.Single(world.Marriages);
        Assert.Equal("Ash", marriage.ChosenSurname);
        Assert.False(marriage.UsedTieBreak);
        Assert.Equal(2, marriage.SurnameReceipt!.Turns.Count);
        var surnameRequests = first.TurnRequests.Concat(second.TurnRequests)
            .Where(request => request.Purpose == AgentConversationPurpose.SurnameChoice).ToArray();
        Assert.Equal(2, surnameRequests.Length);
        Assert.All(surnameRequests, request => Assert.Equal("Ash", Assert.Single(request.AllowedSurnames)));
        Assert.Equal(firstName + " Ash", world.Society.GetInhabitant(InitiatorId).Name);
        Assert.Equal("Rowan Ash", world.Society.GetInhabitant(InviteeId).Name);
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var reloaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), Route);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(reloaded.ExportState()));
    }

    [Fact]
    public async Task PendingSurnameChoiceRefusesARenameThatWouldMakeItsChoicesImpossible()
    {
        var first = new MarriagePersonalProvider(InitiatorId);
        var second = new MarriagePersonalProvider(InviteeId);
        IDecisionProvider Route(string id) => id == InitiatorId ? first : id == InviteeId ? second : new DeterministicDecisionProvider();
        using var world = MarriedWorldSetup("pending-marriage-name-limit", Route);
        await AdvanceMarriageUntil(world, () => world.Marriages.Count == 1);
        world.Pause();
        Assert.Null(Assert.Single(world.Marriages).CompletedTick);
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.Throws<ArgumentException>(() => world.RenameAgent(InitiatorId, new string('A', 45) + " Q"));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        var saved = world.ExportState();
        var impossibleName = SocietyFixture.RenameInhabitant(saved.Society.Society, InitiatorId, new string('A', 45) + " Q");
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(saved with
        {
            Society = saved.Society with { Society = impossibleName.Checkpoint },
        }));
        Assert.True(world.RenameAgent(InitiatorId, "Aster Middle Vale"));
        Assert.Equal("Rowan Reed", world.Society.GetInhabitant(InviteeId).Name);
        var originalSurnameChoices = Assert.Single(world.Marriages);
        world.Resume();
        await AdvanceMarriageUntil(world, () => world.Marriages.Any(item => item.CompletedTick is not null));
        Assert.Equal("Aster Middle Ash", world.Society.GetInhabitant(InitiatorId).Name);
        Assert.Equal("Rowan Ash", world.Society.GetInhabitant(InviteeId).Name);
        Assert.Equal(originalSurnameChoices.InitiatorNameAtAcceptance, Assert.Single(world.Marriages).InitiatorNameAtAcceptance);
        Assert.Equal(originalSurnameChoices.InviteeNameAtAcceptance, Assert.Single(world.Marriages).InviteeNameAtAcceptance);
        world.Validate();
    }
    [Fact]
    public async Task NormalMarriageChoosesSurnameAndPlayerRenameUpdatesBothOrNeither()
    {
        var first = new MarriagePersonalProvider(InitiatorId);
        var second = new MarriagePersonalProvider(InviteeId);
        IDecisionProvider Route(string id) => id == InitiatorId ? first : id == InviteeId ? second : new DeterministicDecisionProvider();
        using var world = MarriedWorldSetup("normal-marriage", Route);
        await AdvanceMarriageUntil(world, () => world.Marriages.Any(item => item.CompletedTick is not null));

        var marriage = Assert.Single(world.Marriages);
        Assert.Equal("agreed", marriage.Consent.Outcome);
        Assert.Equal(AgentConversationEffect.Marriage, marriage.Consent.WrapUpEffect);
        Assert.Equal(2, marriage.Consent.WrapUpAcceptedBy.Count);
        Assert.Equal("Ash", marriage.ChosenSurname);
        Assert.False(marriage.UsedTieBreak);
        Assert.Equal("Aster Ash", world.Society.GetInhabitant(InitiatorId).Name);
        Assert.Equal("Rowan Ash", world.Society.GetInhabitant(InviteeId).Name);
        var receipt = Assert.IsType<AgentConversation>(marriage.SurnameReceipt);
        Assert.Equal(2, receipt.Turns.Count);
        Assert.Equal(new[] { InitiatorId, InviteeId }, receipt.Turns.Select(turn => turn.SpeakerId));
        Assert.Contains(first.TurnRequests, request => request.Purpose == AgentConversationPurpose.SurnameChoice);
        Assert.Contains(second.TurnRequests, request => request.Purpose == AgentConversationPurpose.SurnameChoice);
        Assert.All(first.TurnRequests, request => Assert.Equal(InitiatorId, request.SpeakerId));
        Assert.All(second.TurnRequests, request => Assert.Equal(InviteeId, request.SpeakerId));
        Assert.All(receipt.Turns, turn => Assert.Single(turn.ListenerIds));

        var snapshot = new OwnerWorldObservationStore(world).GetSnapshot();
        Assert.Contains(snapshot.Inhabitants.Single(item => item.Id == InitiatorId).SocialNotes,
            note => note.Contains("Married to Rowan Ash", StringComparison.Ordinal));
        Assert.Contains(snapshot.Conversations, item => item.Kind == "marriage_surname" && item.ChosenSurname == "Ash");
        Assert.True(world.RenameAgent(InitiatorId, "Aster Vale"));
        Assert.Equal("Rowan Vale", world.Society.GetInhabitant(InviteeId).Name);
        Assert.Equal("Vale", Assert.Single(world.Marriages).CurrentSurname);
        Assert.Equal("Ash", Assert.Single(world.Marriages).ChosenSurname);
        Assert.Equal(receipt, Assert.Single(world.Marriages).SurnameReceipt);
        var unchanged = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.Throws<InhabitantNameTakenException>(() => world.RenameAgent(InitiatorId, "Willow Pine"));
        Assert.Equal(unchanged, PrivateWorldRuntimeCodec.Encode(world.ExportState()));

        using var reloaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(unchanged), Route);
        Assert.Equal("Aster Vale", reloaded.Society.GetInhabitant(InitiatorId).Name);
        Assert.Equal("Rowan Vale", reloaded.Society.GetInhabitant(InviteeId).Name);
        Assert.Equal("Ash", Assert.Single(reloaded.Marriages).ChosenSurname);
        Assert.Equal("Vale", Assert.Single(reloaded.Marriages).CurrentSurname);
    }

    [Fact]
    public async Task InterruptedSurnameChoiceRetainsTurnsAndOnlyItsAcceptedSessionResumesRemotely()
    {
        var first = new MarriagePersonalProvider(InitiatorId) { DisagreeOnSurname = true };
        var second = new MarriagePersonalProvider(InviteeId) { DisagreeOnSurname = true, SurnameFailuresRemaining = 1 };
        IDecisionProvider Route(string id) => id == InitiatorId ? first : id == InviteeId ? second : new DeterministicDecisionProvider();
        using var world = MarriedWorldSetup("interrupted-marriage", Route);
        await AdvanceMarriageUntil(world, () => world.Conversations.Any(item => item.Kind == AgentConversationKind.MarriageSurname &&
            item.Status == AgentConversationStatus.Suspended));
        var interrupted = Assert.Single(world.Conversations, item => item.Kind == AgentConversationKind.MarriageSurname);
        var originalTurn = Assert.Single(interrupted.Turns);
        Assert.Null(Assert.Single(world.Marriages).CompletedTick);
        Assert.Equal(AgentConversationInterruption.ProviderTimedOut, interrupted.Interruption);
        world.Pause();
        var saved = world.ExportState();
        var distantPosition = saved.Inhabitants.Single(item => item.InhabitantId == DistantId).Position;
        saved = saved with
        {
            Inhabitants = saved.Inhabitants.Select(item => item.InhabitantId == InviteeId
                ? item with { Position = distantPosition } : item).ToArray(),
        };
        var callsBeforeLoad = first.TurnRequests.Count + second.TurnRequests.Count;
        using var reloaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(saved)), Route);
        Assert.Equal(callsBeforeLoad, first.TurnRequests.Count + second.TurnRequests.Count);
        Assert.Equal(originalTurn.Id, Assert.Single(reloaded.Conversations, item => item.Id == interrupted.Id).Turns.Single().Id);
        reloaded.Resume();
        await AdvanceMarriageUntil(reloaded, () => reloaded.Marriages.Any(item => item.CompletedTick is not null));
        var completed = Assert.Single(reloaded.Marriages);
        Assert.True(completed.UsedTieBreak);
        Assert.Equal(4, completed.SurnameReceipt!.Turns.Count);
        Assert.Equal(originalTurn.Id, completed.SurnameReceipt.Turns[0].Id);
        Assert.Contains(completed.ChosenSurname, MarriageSurnameOptions);
        Assert.Equal(2, completed.SurnameReceipt.Turns.Count(item => item.SpeakerId == InitiatorId));
        Assert.Equal(2, completed.SurnameReceipt.Turns.Count(item => item.SpeakerId == InviteeId));
        Assert.Contains(reloaded.ExportState().Events, item => item.Kind == "marriage_surname_draw" && item.Detail.Contains("four turns without agreement", StringComparison.Ordinal));
        var completedSave = PrivateWorldRuntimeCodec.Encode(reloaded.ExportState());
        using var replayed = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(completedSave), Route);
        Assert.Equal(completed.ChosenSurname, Assert.Single(replayed.Marriages).ChosenSurname);
        Assert.True(Assert.Single(replayed.Marriages).UsedTieBreak);

        var forged = saved with
        {
            Marriages = [],
        };
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(forged));
    }

    [Fact]
    public async Task MarriageSaveRefusesForgedConsentSurnamesAndMissingReceipts()
    {
        var first = new MarriagePersonalProvider(InitiatorId);
        var second = new MarriagePersonalProvider(InviteeId);
        IDecisionProvider Route(string id) => id == InitiatorId ? first : id == InviteeId ? second : new DeterministicDecisionProvider();
        using var world = MarriedWorldSetup("marriage-save-integrity", Route);
        await AdvanceMarriageUntil(world, () => world.Marriages.Any(item => item.CompletedTick is not null));
        var saved = world.ExportState();
        var marriage = Assert.Single(saved.Marriages);
        var badRecords = new[]
        {
            marriage with { Consent = marriage.Consent with { WrapUpAcceptedBy = [InitiatorId] } },
            marriage with { ChosenSurname = "Invented" },
            marriage with { UsedTieBreak = true },
            marriage with { SurnameReceipt = null },
            marriage with { PartnershipReceipt = marriage.PartnershipReceipt with { Consent = SocietyConsentState.Pending } },
            marriage with { LatestPlayerRename = new AgentMarriageRename(ListenerId, "Ash", world.WorldTick) },
        };
        foreach (var bad in badRecords)
            Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(saved with { Marriages = [bad] }));
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(saved with { Marriages = null! }));
        var wrongNames = saved with
        {
            Society = saved.Society with
            {
                Society = saved.Society.Society with
                {
                    Inhabitants = saved.Society.Society.Inhabitants.Select(item => item.Id == InviteeId
                        ? item with { Name = "Rowan Reed" } : item).ToArray(),
                },
            },
        };
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(wrongNames));
        // Completed consent and surname receipts outlive the ordinary 64-conversation window.
        var compacted = saved with
        {
            Conversations = [],
            Society = saved.Society with
            {
                Society = saved.Society.Society with
                {
                    Beliefs = (saved.Society.Society.Beliefs ?? []).Select(item => item with { SourceTurnId = null }).ToArray(),
                }
            },
        };
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(compacted)), Route);
        Assert.Equal("Ash", Assert.Single(restored.Marriages).ChosenSurname);
        Assert.Equal(2, Assert.Single(restored.Marriages).SurnameReceipt!.Turns.Count);
    }

    private static PrivateWorldRuntime MarriedWorldSetup(string seed, Func<string, IDecisionProvider> route)
    {
        using var setup = NewWorld(seed, route);
        Assert.True(setup.RenameAgent(InitiatorId, "Aster Ash"));
        Assert.True(setup.RenameAgent(InviteeId, "Rowan Reed"));
        Assert.True(setup.RenameAgent(ListenerId, "Willow Stone"));
        Assert.True(setup.RenameAgent(DistantId, "Mira Pine"));
        setup.StartWorld();
        var state = setup.ExportState();
        var proposal = SocietyFixture.ProposeRelationship(state.Society.Society, new SocietyRelationshipProposal(
            "partnership:marriage-test", 1, SocietyRelationshipType.Partnership, InitiatorId, InviteeId,
            state.Society.Society.WorldTick));
        var accepted = SocietyFixture.AcceptRelationship(proposal.Checkpoint, "partnership:marriage-test", 1, InviteeId);
        Assert.Equal(SocietyRelationshipState.Accepted, accepted.Checkpoint.GetRelationship("partnership:marriage-test").State);
        return PrivateWorldRuntime.Restore(state with { Society = state.Society with { Society = accepted.Checkpoint } }, route);
    }

    [Fact]
    public async Task RejectedMarriageTickKeepsBothNamesAndReusesTheUnadmittedReply()
    {
        var first = new MarriagePersonalProvider(InitiatorId);
        var second = new MarriagePersonalProvider(InviteeId);
        IDecisionProvider Route(string id) => id == InitiatorId ? first : id == InviteeId ? second : new DeterministicDecisionProvider();
        using var world = MarriedWorldSetup("marriage-rollback", Route);
        await AdvanceMarriageUntil(world, () => world.Conversations.Any(item => item.Kind == AgentConversationKind.MarriageSurname &&
            item.Turns.Count == 1 && item.Status == AgentConversationStatus.AwaitingSpeaker));
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        var calls = first.TurnRequests.Count + second.TurnRequests.Count;
        Assert.False((await world.AdvanceOneTickNonBlockingAsync(() => false)).Advanced);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        Assert.Equal("Aster Ash", world.Society.GetInhabitant(InitiatorId).Name);
        Assert.Equal("Rowan Reed", world.Society.GetInhabitant(InviteeId).Name);
        Assert.Null(Assert.Single(world.Marriages).CompletedTick);
        Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
        Assert.Equal(calls, first.TurnRequests.Count + second.TurnRequests.Count);
        Assert.NotNull(Assert.Single(world.Marriages).CompletedTick);
        Assert.Equal("Rowan Ash", world.Society.GetInhabitant(InviteeId).Name);
    }

    private static async Task AdvanceMarriageUntil(PrivateWorldRuntime world, Func<bool> finished)
    {
        for (var attempt = 0; attempt < 160 && !finished(); attempt++)
        {
            Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
            await Task.Delay(3);
        }
        Assert.True(finished(), "The normal personal-model marriage path did not reach its expected transition.");
    }

    private sealed class MarriagePersonalProvider(string ownerId) : IDecisionProvider, IAgentConversationProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
        public long ProviderEpoch => 1;
        public List<AgentConversationTurnRequest> TurnRequests { get; } = [];
        public bool DisagreeOnSurname { get; init; }
        public bool UseAllowedSurnameOptions { get; init; }
        public int SurnameFailuresRemaining { get; set; }
        public bool CanSpeakAs(string agentId) => agentId == ownerId;

        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            request.Validate();
            Assert.Equal(ownerId, request.Observation.InhabitantId);
            var observation = request.Observation;
            var selected = observation.Candidates.FirstOrDefault(candidate =>
                candidate.Id.StartsWith("conversation_wrapup_accept:", StringComparison.Ordinal) ||
                candidate.Id.StartsWith("conversation_resume:", StringComparison.Ordinal) ||
                candidate.Id.StartsWith("conversation_accept:", StringComparison.Ordinal)) ??
                observation.Candidates.FirstOrDefault(candidate => ownerId == InitiatorId && candidate.Id == $"talk:{InviteeId}") ??
                observation.Candidates.First(candidate => candidate.Id == "safe_idle");
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, ownerId, Kind, ProviderEpoch,
                observation.RunEpoch, observation.DecisionGeneration, observation.ObservationDigest, selected.Id, 1d,
                observation.Candidates.ToDictionary(candidate => candidate.Id, candidate => candidate.Id == selected.Id ? 1d : 0d),
                ChosenPersonality: observation.NeedsPersonality ? "patient" : null,
                ChosenAspiration: observation.NeedsAspiration ? "live well together" : null));
        }

        public ValueTask<AgentConversationTurnResponse> SpeakAsync(AgentConversationTurnRequest request, CancellationToken cancellationToken = default)
        {
            request.Validate();
            Assert.Equal(ownerId, request.SpeakerId);
            TurnRequests.Add(request);
            if (request.Purpose == AgentConversationPurpose.SurnameChoice && SurnameFailuresRemaining > 0)
            {
                SurnameFailuresRemaining--;
                throw new TimeoutException("The surname model timed out.");
            }
            return ValueTask.FromResult(new AgentConversationTurnResponse(request.RequestId, request.ConversationId,
                request.Revision, request.RunEpoch, ownerId, "I would like us to share a life together.", AgentConversationDisposition.Continue,
                request.Purpose == AgentConversationPurpose.WrapUp && request.AllowedEffects.Contains(AgentConversationEffect.Marriage)
                    ? AgentConversationEffect.Marriage : AgentConversationEffect.None,
                SurnameChoice: request.Purpose == AgentConversationPurpose.SurnameChoice
                    ? UseAllowedSurnameOptions
                        ? DisagreeOnSurname && ownerId == InviteeId ? request.AllowedSurnames[^1] : request.AllowedSurnames[0]
                        : DisagreeOnSurname && ownerId == InviteeId ? "Reed" : "Ash"
                    : null));
        }
    }
}

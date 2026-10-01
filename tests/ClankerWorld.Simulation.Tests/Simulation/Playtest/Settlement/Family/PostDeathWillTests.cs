using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.GodotClient.UI;
using ClankerWorld.Viewer.Control;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class PostDeathWillTests
{
    private const string Words = "Keep the orchard going.";

    [Fact]
    public async Task TwoHeirsShareEquallyAndHearFinalWordsThatSurviveSaveAndReload()
    {
        var provider = new WillProvider(CognitionWillContext.HeirsCandidateId, observation =>
            new CognitionWillChoice([HeirKey(observation, "Mira"), HeirKey(observation, "Rowan")],
                CognitionWillContext.EqualSplit, FinalWords: Words));
        using var world = NewWorld(provider);
        Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
        await provider.Called.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await AdvanceUntilResolved(world);

        var estate = Assert.Single(world.Society.Estates, item => item.DeceasedId == "founder-scout");
        Assert.Equal("accepted", estate.WillStatus);
        Assert.Equal(["founder-mira", "founder-rowan"], estate.WillHeirIds);
        Assert.Equal(Words, estate.FinalWords);
        // Lot-ID order: rope 1, seed 3, stone 5. Leftover units go one at a time
        // in will order, continuing from where the previous lot stopped.
        Assert.Equal(
        [
            new SocietyWillBequest("rope-lot", "founder-mira", 1),
            new SocietyWillBequest("seed-lot", "founder-mira", 1),
            new SocietyWillBequest("seed-lot", "founder-rowan", 2),
            new SocietyWillBequest("stone-lot", "founder-mira", 3),
            new SocietyWillBequest("stone-lot", "founder-rowan", 2),
        ], estate.WillBequests);
        var will = Assert.Single(provider.Observations).Will!;
        Assert.Equal(["rope", "seed", "stone"], will.Items.Select(item => item.Kind));
        Assert.DoesNotContain(will.Heirs, heir => heir.Key.EndsWith("founder-scout", StringComparison.Ordinal));
        Assert.Contains(world.ExportState().Events, item => item.Kind == "estate_will_accepted");
        Assert.True(GameUiText.IsPlayerFacingEvent("estate_will_accepted"));
        Assert.False(GameUiText.IsPlayerFacingEvent("estate_will_started"));

        var profile = new OwnerWorldObservationStore(world).GetSnapshot().Inhabitants
            .Single(item => item.Id == "founder-scout").FinalWill!;
        Assert.Equal(("accepted", "equal", Words), (profile.Status, profile.Split, profile.FinalWords));
        Assert.Equal(["Mira", "Rowan"], profile.Heirs.Select(heir => heir.Name));
        Assert.Equal([new ViewerInventoryEntry("rope", 1), new ViewerInventoryEntry("seed", 1), new ViewerInventoryEntry("stone", 3)],
            profile.Heirs[0].Items);

        var saved = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved),
            id => id == "founder-scout" ? provider : new DeterministicDecisionProvider());
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        var original = await AdvanceUntilSettled(world, estate.Id);
        var reloaded = await AdvanceUntilSettled(restored, estate.Id);
        Assert.Equal(Inherited(original), Inherited(reloaded));
        Assert.Equal(FinalWordMemories(original), FinalWordMemories(reloaded));

        Assert.Equal(
        [
            ("founder-mira", "rope-lot", 1), ("founder-mira", "seed-lot", 1), ("founder-mira", "stone-lot", 3),
            ("founder-rowan", "seed-lot", 2), ("founder-rowan", "stone-lot", 2),
        ], Inherited(reloaded));
        Assert.Equal(
        [
            ("founder-mira", "Scout's final words were: 'Keep the orchard going.'"),
            ("founder-rowan", "Scout's final words were: 'Keep the orchard going.'"),
        ], FinalWordMemories(reloaded));
        Assert.DoesNotContain(reloaded.Memories, memory => memory.OwnerId == "founder-ilya" &&
            memory.Summary.Contains("final words", StringComparison.Ordinal));
        Assert.Single(reloaded.Events, item => item.Kind == "estate_settled" && item.Detail == estate.Id);
        Assert.Equal(1, provider.CallCount);
    }

    [Fact]
    public async Task ItemByItemWillGivesListedItemsWholeSharesTheRestAndAChildOwnsTheirPart()
    {
        var provider = new WillProvider(CognitionWillContext.HeirsCandidateId, observation =>
        {
            var mira = HeirKey(observation, "Mira");
            var ilya = HeirKey(observation, "Ilya");
            return new CognitionWillChoice([mira, ilya, HeirKey(observation, "Rowan")], CognitionWillContext.ItemSplit,
                new Dictionary<string, string>
                {
                    [ItemKey(observation, "stone")] = ilya,
                    [ItemKey(observation, "rope")] = mira,
                });
        });
        using var world = NewWorld(provider, childHeir: true);
        Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
        await provider.Called.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await AdvanceUntilResolved(world);

        var estate = world.Society.Estates.Single(item => item.DeceasedId == "founder-scout");
        Assert.Equal("accepted", estate.WillStatus);
        Assert.Null(estate.FinalWords);
        Assert.Equal(
        [
            new SocietyWillBequest("rope-lot", "founder-mira", 1),
            new SocietyWillBequest("seed-lot", "founder-mira", 1),
            new SocietyWillBequest("seed-lot", "founder-ilya", 1),
            new SocietyWillBequest("seed-lot", "founder-rowan", 1),
            new SocietyWillBequest("stone-lot", "founder-ilya", 5),
        ], estate.WillBequests);
        Assert.Equal("your household", Assert.Single(provider.Observations).Will!.Heirs
            .Single(heir => heir.Name == "Mira").Relation);

        var settled = await AdvanceUntilSettled(world, estate.Id);
        Assert.Equal(SocietyAgeBand.Child, settled.GetInhabitant("founder-ilya").AgeBand);
        var childStone = Assert.Single(settled.Inventory.Lots, lot => lot.ProvenanceLotId == "stone-lot");
        Assert.Equal(("founder-ilya", 5, null, null), (childStone.OwnerId, childStone.Quantity,
            childStone.StorageBuildingId, childStone.GroundPosition));
        Assert.Equal(5 + 3 + 1, settled.Inventory.Lots.Where(lot => lot.ProvenanceLotId is "rope-lot" or "seed-lot" or "stone-lot")
            .Sum(lot => lot.Quantity));
        Assert.DoesNotContain(settled.Memories, memory => memory.Id.StartsWith("final-words:", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("unknown_heir")]
    [InlineData("unoffered_item")]
    [InlineData("four_heirs")]
    [InlineData("item_to_unnamed_heir")]
    [InlineData("markup_words")]
    public async Task InvalidWillChoicesKeepTheHouseholdDefault(string fault)
    {
        var provider = new WillProvider(CognitionWillContext.HeirsCandidateId, observation =>
        {
            var mira = HeirKey(observation, "Mira");
            var rowan = HeirKey(observation, "Rowan");
            return fault switch
            {
                "unknown_heir" => new CognitionWillChoice(["will:heir:ghost"], CognitionWillContext.EqualSplit, FinalWords: Words),
                "unoffered_item" => new CognitionWillChoice([mira], CognitionWillContext.ItemSplit,
                    new Dictionary<string, string> { ["item:99"] = mira }, Words),
                "four_heirs" => new CognitionWillChoice([mira, rowan, HeirKey(observation, "Ilya"), "will:heir:extra"],
                    CognitionWillContext.EqualSplit, FinalWords: Words),
                "item_to_unnamed_heir" => new CognitionWillChoice([mira], CognitionWillContext.ItemSplit,
                    new Dictionary<string, string> { [ItemKey(observation, "seed")] = rowan }, Words),
                _ => new CognitionWillChoice([mira], CognitionWillContext.EqualSplit, FinalWords: "[b]Keep it[/b]"),
            };
        });
        using var world = NewWorld(provider);
        var householdBeneficiaries = world.Society.Estates.Single(item => item.DeceasedId == "founder-scout")
            .BeneficiaryIds.ToArray();
        Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
        await AdvanceUntilResolved(world);
        var estate = world.Society.Estates.Single(item => item.DeceasedId == "founder-scout");
        Assert.Equal("default", estate.WillStatus);
        Assert.Null(estate.WillHeirIds);
        Assert.Null(estate.WillBequests);
        Assert.Equal(householdBeneficiaries, estate.BeneficiaryIds);
        // A reply the host admits keeps its words even when its division is unusable;
        // a malformed reply is discarded whole.
        Assert.Equal(fault is "unknown_heir" or "unoffered_item" ? Words : null, estate.FinalWords);
        Assert.Equal(1, provider.CallCount);
    }

    [Fact]
    public void DeadUnknownOrSelfHeirsAndItemsTheAgentDidNotOwnCannotBeCommitted()
    {
        using var world = NewWorld(new WillProvider(CognitionWillContext.HouseholdCandidateId));
        var started = SocietyFixture.MarkWillStarted(world.Society,
            world.Society.Estates.Single(item => item.DeceasedId == "founder-scout").Id).Checkpoint;
        var estateId = started.Estates.Single(item => item.DeceasedId == "founder-scout").Id;
        var deadRowan = SocietyFixture.Kill(started, "founder-rowan", SocietyDeathCause.Accident).Checkpoint;
        var forged = started with
        {
            Inventory = started.Inventory with
            {
                Lots = started.Inventory.Lots.Select(lot => lot.Id == "seed-lot"
                    ? lot with { OwnerId = "founder-rowan" } : lot).ToArray(),
            },
        };
        var cases = new (SocietyCheckpoint Checkpoint, SocietyWillDirective Directive)[]
        {
            (deadRowan, new(["founder-rowan"], "equal")),
            (started, new(["founder-ghost"], "equal")),
            (started, new(["founder-scout"], "equal")),
            (started, new(["town:first"], "equal")),
            (started, new(["founder-mira"], "items", new Dictionary<string, string> { ["not-owned"] = "founder-mira" })),
            (forged, new(["founder-mira"], "equal")),
            (started, new(["founder-mira", "founder-mira"], "equal")),
        };
        foreach (var (checkpoint, directive) in cases)
        {
            var resolved = SocietyFixture.ResolveWill(checkpoint, estateId, directive, "accepted", Words).Checkpoint;
            var estate = resolved.GetEstate(estateId);
            Assert.Equal("default", estate.WillStatus);
            Assert.Null(estate.WillBequests);
            Assert.Equal(Words, estate.FinalWords);
        }
    }

    [Fact]
    public void FinalWordsArePlainShortSingleLineText()
    {
        Assert.Equal("Keep the orchard going.", CognitionWillChoice.NormalizeFinalWords("  Keep the\norchard\t going.  "));
        Assert.Equal("Look after them", CognitionWillChoice.NormalizeFinalWords("Look‮ after​ them"));
        Assert.Null(CognitionWillChoice.NormalizeFinalWords("<b>Goodbye</b>"));
        Assert.Null(CognitionWillChoice.NormalizeFinalWords("{\"say\":\"hi\"}"));
        Assert.Null(CognitionWillChoice.NormalizeFinalWords(" \n "));
        Assert.Null(CognitionWillChoice.NormalizeFinalWords(new string('a', CognitionWillChoice.MaximumFinalWordsLength + 1)));
        Assert.NotNull(CognitionWillChoice.NormalizeFinalWords(new string('a', CognitionWillChoice.MaximumFinalWordsLength)));
        var lines = GameUiText.FinalWillLines("accepted", new OwnerWorldFinalWill("accepted", "equal",
            [new("founder-mira", "Mira", false, [new("seed", 2)]), new("town:first", "First Town", true, [])], Words));
        Assert.Equal(
        [
            "Final will: belongings shared equally between Mira and First Town.",
            "To Mira: 2 seed",
            "To First Town: nothing listed",
            "Final words: “Keep the orchard going.”",
        ], lines);
        Assert.Equal(["Personal estate follows household inheritance.", "Final words: “Be kind.”"],
            GameUiText.FinalWillLines("default", new OwnerWorldFinalWill("default", null, [], "Be kind.")));
    }

    [Fact]
    public async Task HouseholdChoiceKeepsDefaultAndItsHeirsHearTheFinalWords()
    {
        var provider = new WillProvider(CognitionWillContext.HouseholdCandidateId, _ =>
            new CognitionWillChoice([], FinalWords: Words));
        using var world = NewWorld(provider);
        Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
        await AdvanceUntilResolved(world);
        var estate = world.Society.Estates.Single(item => item.DeceasedId == "founder-scout");
        Assert.Equal(("default", Words), (estate.WillStatus, estate.FinalWords));
        Assert.Contains(world.ExportState().Events,
            item => item.Kind == "estate_will_default" && item.Detail.EndsWith(":household_selected", StringComparison.Ordinal));
        var settled = await AdvanceUntilSettled(world, estate.Id);
        var heirs = estate.BeneficiaryIds.Where(id => settled.GetInhabitant(id).Status == SocietyInhabitantStatus.Active)
            .Order(StringComparer.Ordinal).ToArray();
        Assert.NotEmpty(heirs);
        Assert.Equal(heirs, FinalWordMemories(settled).Select(item => item.OwnerId));
        Assert.DoesNotContain(settled.Inventory.Lots, lot => lot.OwnerId == estate.Id);
    }

    [Fact]
    public async Task MissingPersonalModelKeepsHouseholdInheritanceDefault()
    {
        using var world = NewWorld(new DeterministicDecisionProvider());

        Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);

        var estate = world.Society.Estates.Single(item => item.DeceasedId == "founder-scout");
        Assert.Equal("default", estate.WillStatus);
        Assert.Null(estate.WillHeirIds);
        Assert.Contains(world.ExportState().Events,
            item => item.Kind == "estate_will_default" && item.Detail.EndsWith(":no_personal_model", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FailureAndInterruptedPendingCallUseDefaultWithoutRetryAfterReload()
    {
        var failing = new WillProvider(CognitionWillContext.HouseholdCandidateId, fail: true);
        var directory = Directory.CreateTempSubdirectory("postdeath-will-logs-");
        try
        {
            var presence = new OwnerClientPresenceLease(TimeSpan.FromMinutes(1));
            presence.RecordAuthenticatedReconnect("owner");
            var logger = new RecordingLogger<PrivateWorldRuntimeService>();
            using var world = NewWorld(failing);
            using var service = new PrivateWorldRuntimeService(world,
                new PrivateWorldStateFile(Path.Combine(directory.FullName, "world.json")), presence, logger);
            Assert.True(await service.TryAdvanceOnceAsync());
            await failing.Called.Task.WaitAsync(TimeSpan.FromSeconds(5));
            for (var attempt = 0; attempt < 8 &&
                 world.Society.Estates.Single(item => item.DeceasedId == "founder-scout").WillStatus == "pending"; attempt++)
            {
                Assert.True(await service.TryAdvanceOnceAsync());
                await Task.Delay(10);
            }
            Assert.Equal("default", world.Society.Estates.Single(item => item.DeceasedId == "founder-scout").WillStatus);
            Assert.Equal(1, failing.CallCount);
            Assert.DoesNotContain(world.ExportState().Events,
                item => item.Detail.Contains("provider secret", StringComparison.Ordinal));
            Assert.Contains(logger.Messages, message =>
                message.Contains("estate_will", StringComparison.Ordinal) &&
                message.Contains("outcome=default", StringComparison.Ordinal) &&
                message.Contains("reason=provider_InvalidOperationException", StringComparison.Ordinal));
            Assert.DoesNotContain(logger.Messages, message => message.Contains("provider secret", StringComparison.Ordinal));
        }
        finally
        {
            directory.Delete(recursive: true);
        }

        var held = new WillProvider(CognitionWillContext.HeirsCandidateId, observation =>
            new CognitionWillChoice([HeirKey(observation, "Mira")], CognitionWillContext.EqualSplit), hold: true);
        using var pending = NewWorld(held);
        Assert.True((await pending.AdvanceOneTickNonBlockingAsync()).Advanced);
        await held.Called.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var bytes = PrivateWorldRuntimeCodec.Encode(pending.ExportState());
        pending.CancelPendingHostedDecisions();
        await held.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes),
            id => id == "founder-scout" ? held : new DeterministicDecisionProvider());
        Assert.True((await restored.AdvanceOneTickNonBlockingAsync()).Advanced);
        Assert.Equal("default", restored.Society.Estates.Single(item => item.DeceasedId == "founder-scout").WillStatus);
        Assert.Equal(1, held.CallCount);
    }

    [Fact]
    public async Task LateProviderResponseCannotOverrideWorldDeadline()
    {
        var late = new WillProvider(CognitionWillContext.HeirsCandidateId, observation =>
            new CognitionWillChoice([HeirKey(observation, "Mira")], CognitionWillContext.EqualSplit),
            hold: true, ignoreCancellation: true);
        using var world = NewWorld(late);
        Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
        await late.Called.Task.WaitAsync(TimeSpan.FromSeconds(5));
        for (var tick = 0; tick < 28; tick++)
            Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
        Assert.Equal("pending", world.Society.Estates.Single(item => item.DeceasedId == "founder-scout").WillStatus);

        late.ReleaseHeldResponse();
        await late.Completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(50);
        Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);

        Assert.Equal("default", world.Society.Estates.Single(item => item.DeceasedId == "founder-scout").WillStatus);
        Assert.Equal(1, late.CallCount);
        Assert.Contains(world.ExportState().Events,
            item => item.Kind == "estate_will_default" && item.Detail.EndsWith(":deadline", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ProviderEpochChangeCancelsPendingWillAndDefaultsWithoutWaitingForDeadline()
    {
        var held = new WillProvider(CognitionWillContext.HeirsCandidateId, observation =>
            new CognitionWillChoice([HeirKey(observation, "Mira")], CognitionWillContext.EqualSplit), hold: true);
        using var world = NewWorld(held);
        Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
        await held.Called.Task.WaitAsync(TimeSpan.FromSeconds(5));

        held.ProviderEpoch++;
        Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
        await held.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("default", world.Society.Estates.Single(item => item.DeceasedId == "founder-scout").WillStatus);
        Assert.Contains(world.ExportState().Events,
            item => item.Kind == "estate_will_default" && item.Detail.EndsWith(":provider_changed", StringComparison.Ordinal));
        Assert.Equal(1, held.CallCount);
    }

    internal static string HeirKey(InhabitantObservation observation, string name) =>
        observation.Will!.Heirs.Single(heir => heir.Name == name).Key;

    internal static string ItemKey(InhabitantObservation observation, string kind) =>
        observation.Will!.Items.Single(item => item.Kind == kind).Key;

    private static (string OwnerId, string LotId, int Quantity)[] Inherited(SocietyCheckpoint checkpoint) =>
        checkpoint.Inventory.Lots.Where(lot => lot.ProvenanceLotId is "rope-lot" or "seed-lot" or "stone-lot")
            .Select(lot => (lot.OwnerId, lot.ProvenanceLotId!, lot.Quantity))
            .OrderBy(item => item.OwnerId, StringComparer.Ordinal).ThenBy(item => item.Item2, StringComparer.Ordinal)
            .ToArray();

    private static (string OwnerId, string Summary)[] FinalWordMemories(SocietyCheckpoint checkpoint) =>
        checkpoint.Memories.Where(memory => memory.Id.StartsWith("final-words:", StringComparison.Ordinal))
            .Select(memory => (memory.OwnerId, memory.Summary))
            .OrderBy(item => item.OwnerId, StringComparer.Ordinal).ToArray();

    private static PrivateWorldRuntime NewWorld(IDecisionProvider provider, bool childHeir = false)
    {
        using var seed = new PrivateWorldRuntime("postdeath-will-seed");
        var state = seed.ExportState();
        var checkpoint = state.Society.Society;
        var inventory = checkpoint.Inventory;
        inventory = InventoryFixture.AddLot(inventory, "seed-lot", "seed", "founder-scout", 3);
        inventory = InventoryFixture.AddLot(inventory, "stone-lot", "stone", "founder-scout", 5);
        inventory = InventoryFixture.AddLot(inventory, "rope-lot", "rope", "founder-scout", 1);
        checkpoint = checkpoint with { Inventory = inventory };
        if (childHeir)
        {
            var birth = checkpoint.LifeTickAt(checkpoint.WorldTick) - 4 * checkpoint.Config.TicksPerLifecycleAge;
            checkpoint = checkpoint with
            {
                Inhabitants = checkpoint.Inhabitants.Select(person => person.Id == "founder-ilya" ? person with
                {
                    BirthTick = birth,
                    BirthLifeTick = checkpoint.LifeClock is null ? null : birth,
                    AgeBand = SocietyAgeBand.Child,
                    LastLifecycleYearChecked = 4,
                    CurrentRole = SocietyWorkRole.Unassigned,
                } : person).ToArray(),
            };
        }
        checkpoint = SocietyFixture.Kill(checkpoint, "founder-scout", SocietyDeathCause.Accident).Checkpoint;
        // Bounded test clock: settle a few ticks after the will instead of after a world day.
        checkpoint = checkpoint with
        {
            Estates = checkpoint.Estates.Select(estate => estate with { ExpiryTick = estate.CreatedTick + 4 }).ToArray(),
        };
        var physical = state.Inhabitants.Single(item => item.InhabitantId == "founder-scout");
        var deceased = checkpoint.GetInhabitant("founder-scout");
        state = state with
        {
            Society = state.Society with { Society = checkpoint },
            Inhabitants = state.Inhabitants.Where(item => item.InhabitantId != "founder-scout")
                .Select(item => childHeir && item.InhabitantId == "founder-ilya"
                    ? item with { LastDecisionContext = null } : item).ToArray(),
            DeceasedInhabitants = [new PlaytestDeceasedInhabitantState("founder-scout", 0,
                checkpoint.AgeAt(deceased, 0), physical)],
        };
        return PrivateWorldRuntime.Restore(state, id => id == "founder-scout" ? provider : new DeterministicDecisionProvider());
    }

    private static async Task AdvanceUntilResolved(PrivateWorldRuntime world)
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            if (world.Society.Estates.Single(item => item.DeceasedId == "founder-scout").WillStatus != "pending") return;
            Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
            await Task.Delay(10);
        }
        Assert.Fail("The final will did not resolve within the bounded test window.");
    }

    private static async Task<SocietyCheckpoint> AdvanceUntilSettled(PrivateWorldRuntime world, string estateId)
    {
        for (var attempt = 0; attempt < 12 && !world.Society.GetEstate(estateId).Settled; attempt++)
            Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
        Assert.True(world.Society.GetEstate(estateId).Settled);
        return world.Society;
    }

    internal sealed class WillProvider(
        string choice,
        Func<InhabitantObservation, CognitionWillChoice?>? will = null,
        bool fail = false,
        bool hold = false,
        bool ignoreCancellation = false) : IDecisionProvider
    {
        private readonly TaskCompletionSource<bool> release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
        public long ProviderEpoch { get; set; } = 42;
        public int CallCount { get; private set; }
        public List<InhabitantObservation> Observations { get; } = [];
        public TaskCompletionSource<bool> Called { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void ReleaseHeldResponse() => release.TrySetResult(true);

        public async ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            Observations.Add(request.Observation);
            Called.TrySetResult(true);
            if (hold)
            {
                if (ignoreCancellation)
                {
                    await release.Task.ConfigureAwait(false);
                }
                else
                {
                    try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        Cancelled.TrySetResult(true);
                        throw;
                    }
                }
            }
            if (fail) throw new InvalidOperationException("provider secret must not enter world events");
            var response = new CognitionDecisionResponse(request.RequestId, request.Observation.InhabitantId,
                Kind, request.ProviderEpoch, request.Observation.RunEpoch,
                request.Observation.DecisionGeneration, request.Observation.ObservationDigest,
                choice, 1, new Dictionary<string, double> { [choice] = 1 },
                Will: will?.Invoke(request.Observation));
            Completed.TrySetResult(true);
            return response;
        }
    }
}

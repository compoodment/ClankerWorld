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
    [Fact]
    public async Task PersonalModelMayDirectFrozenEstateToLivingHeirAndSaveReloadSettlesOnce()
    {
        var provider = new WillProvider("will:heir:founder-mira");
        using var world = NewWorld(provider);
        Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
        await provider.Called.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await AdvanceUntilResolved(world);

        var estate = Assert.Single(world.Society.Estates, item => item.DeceasedId == "founder-scout");
        Assert.Equal("accepted", estate.WillStatus);
        Assert.Equal("founder-mira", estate.WillBeneficiaryId);
        Assert.Contains(estate.FrozenLots!, lot => lot.LotId == "seed-lot" && lot.Quantity == 3);
        Assert.Equal(1, provider.CallCount);
        Assert.Contains(new OwnerWorldObservationStore(world).GetSnapshot().Inhabitants
            .Single(item => item.Id == "founder-scout").DecisionFactors,
            item => item.Key == "will-heir" && item.Detail == "Mira");
        Assert.Contains(new OwnerWorldObservationStore(world).GetEventsAfter(0).Events,
            item => item.Kind == "estate_will_accepted");
        Assert.True(GameUiText.IsPlayerFacingEvent("estate_will_accepted"));
        Assert.False(GameUiText.IsPlayerFacingEvent("estate_will_started"));

        var saved = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        using var restored = PrivateWorldRuntime.Restore(saved,
            id => id == "founder-scout" ? provider : new DeterministicDecisionProvider());
        while (!restored.Society.GetEstate(estate.Id).Settled && restored.WorldTick < estate.ExpiryTick + 4)
            Assert.True((await restored.AdvanceOneTickNonBlockingAsync()).Advanced);
        Assert.True(restored.Society.GetEstate(estate.Id).Settled);
        Assert.Equal("founder-mira", Assert.Single(restored.Society.Inventory.Lots,
            lot => lot.ProvenanceLotId == "seed-lot").OwnerId);
        Assert.True((await restored.AdvanceOneTickNonBlockingAsync()).Advanced);
        Assert.Single(restored.Society.Events,
            item => item.Kind == "estate_settled" && item.Detail == estate.Id);
        Assert.Equal(1, provider.CallCount);
    }

    [Theory]
    [InlineData("will:heir:ghost")]
    [InlineData("will:heir:founder-mira:forged-lot")]
    public async Task ForgedOrUnknownChoiceFallsBackToHousehold(string choice)
    {
        using var world = NewWorld(new WillProvider(choice));
        var householdBeneficiaries = world.Society.Estates.Single(item => item.DeceasedId == "founder-scout")
            .BeneficiaryIds.OrderBy(item => item, StringComparer.Ordinal).ToArray();
        Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
        await AdvanceUntilResolved(world);
        var estate = Assert.Single(world.Society.Estates, item => item.DeceasedId == "founder-scout");
        Assert.Equal("default", estate.WillStatus);
        Assert.Null(estate.WillBeneficiaryId);
        Assert.Equal(householdBeneficiaries, estate.BeneficiaryIds.OrderBy(item => item, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void ForgedEstateOwnershipCannotBeCommittedAsAWill()
    {
        using var world = NewWorld(new WillProvider("will:household"));
        var checkpoint = SocietyFixture.MarkWillStarted(world.Society,
            world.Society.Estates.Single(item => item.DeceasedId == "founder-scout").Id).Checkpoint;
        var estate = checkpoint.Estates.Single(item => item.DeceasedId == "founder-scout");
        var forged = checkpoint with
        {
            Inventory = checkpoint.Inventory with
            {
                Lots = checkpoint.Inventory.Lots.Select(lot => lot.Id == "seed-lot"
                    ? lot with { OwnerId = "founder-rowan" } : lot).ToArray(),
            }
        };
        var resolved = SocietyFixture.ResolveWill(forged, estate.Id, "founder-mira", "accepted").Checkpoint;
        Assert.Equal("default", resolved.GetEstate(estate.Id).WillStatus);
        Assert.Null(resolved.GetEstate(estate.Id).WillBeneficiaryId);
    }

    [Fact]
    public async Task MissingPersonalModelKeepsHouseholdInheritanceDefault()
    {
        using var world = NewWorld(new DeterministicDecisionProvider());

        Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);

        var estate = world.Society.Estates.Single(item => item.DeceasedId == "founder-scout");
        Assert.Equal("default", estate.WillStatus);
        Assert.Null(estate.WillBeneficiaryId);
        Assert.Contains(world.ExportState().Events,
            item => item.Kind == "estate_will_default" && item.Detail.EndsWith(":no_personal_model", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FailureAndInterruptedPendingCallUseDefaultWithoutRetryAfterReload()
    {
        var failing = new WillProvider("will:household", fail: true);
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

        var held = new WillProvider("will:heir:founder-mira", hold: true);
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
        var late = new WillProvider("will:heir:founder-mira", hold: true, ignoreCancellation: true);
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
        var held = new WillProvider("will:heir:founder-mira", hold: true);
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

    private static PrivateWorldRuntime NewWorld(IDecisionProvider provider)
    {
        using var seed = new PrivateWorldRuntime("postdeath-will-seed");
        var state = seed.ExportState();
        var checkpoint = state.Society.Society with
        {
            Config = state.Society.Society.Config with { EstateEscrowDays = 1 },
        };
        checkpoint = checkpoint with
        {
            Inventory = InventoryFixture.AddLot(checkpoint.Inventory, "seed-lot", "seed", "founder-scout", 3),
        };
        checkpoint = SocietyFixture.Kill(checkpoint, "founder-scout", SocietyDeathCause.Accident).Checkpoint;
        var physical = state.Inhabitants.Single(item => item.InhabitantId == "founder-scout");
        var deceased = checkpoint.GetInhabitant("founder-scout");
        state = state with
        {
            Society = state.Society with { Society = checkpoint },
            Inhabitants = state.Inhabitants.Where(item => item.InhabitantId != "founder-scout").ToArray(),
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

    private sealed class WillProvider(string choice, bool fail = false, bool hold = false, bool ignoreCancellation = false) : IDecisionProvider
    {
        private readonly TaskCompletionSource<bool> release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
        public long ProviderEpoch { get; set; } = 42;
        public int CallCount { get; private set; }
        public TaskCompletionSource<bool> Called { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void ReleaseHeldResponse() => release.TrySetResult(true);

        public async ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
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
                choice, 1, new Dictionary<string, double> { [choice] = 1 });
            Completed.TrySetResult(true);
            return response;
        }
    }
}

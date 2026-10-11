using System.Collections.Concurrent;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class LongInventoryActionCheckpointTests
{
    private const string Actor = "founder:00000000000000000000000000000001";
    private const string House = "first-town-house-a";
    private const string Cart = "lineage-cart";
    private static readonly Lazy<Task<byte[]>> StartingCheckpoint = new(async () =>
    {
        using var initial = NormalPathWorld.CreateGenerated("personal-storage-orders", _ => new ActionCoverageRecorder(chooseIdle: true));
        Assert.True((await initial.AdvanceOneTickAsync()).Advanced);
        return PrivateWorldRuntimeCodec.Encode(initial.ExportState());
    });

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task FoodOrderConsumesOnceAndHostSavesAcrossInventoryLineageLengths(int splits)
    {
        var (state, lotId) = await SplitInventoryState("food", splits);
        using var world = PrivateWorldRuntime.Restore(state, FoodProviders);
        using var host = new CheckpointHost(world);
        var receipt = world.SubmitInstruction(new OwnerInstructionRequest("lineage-eat", "owner:test", Actor,
            OwnerInstructionKind.MustDo, "eat one food"));
        for (var step = 0; step < 4 && Order(world, receipt.InstructionId).Status != "finished"; step++)
            await host.AdvanceAndCheckSaved();

        var finished = Order(world, receipt.InstructionId);
        Assert.Equal(("finished", 1), (finished.Status, finished.CompletedUnits));
        Assert.InRange(finished.LastEffectId!.Length, 1, 512);
        Assert.Equal(7 - splits, world.Society.Inventory.GetLot(lotId).Quantity);
        Assert.Equal(Actor, world.Society.Inventory.GetLot(lotId).OwnerId);
        Assert.Single(world.ExportState().Events, item => item.Kind == "food_consumed" && item.Detail == Actor);

        var finishedState = world.ExportState();
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(finishedState with
        {
            Instructions = finishedState.Instructions!.Select(item => item.InstructionId == receipt.InstructionId
                ? item with { Order = item.Order! with { LastEffectId = new string('x', 513) } } : item).ToArray(),
        }));

        using var replay = host.Reload(FoodProviders);
        using var replayHost = new CheckpointHost(replay);
        world.Pause();
        replay.Pause();
        world.Resume();
        replay.Resume();
        for (var step = 0; step < 3; step++)
        {
            await host.AdvanceAndCheckSaved();
            await replayHost.AdvanceAndCheckSaved();
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        }
        Assert.Equal(finished.LastEffectId, Order(replay, receipt.InstructionId).LastEffectId);
        Assert.Equal(1, Order(replay, receipt.InstructionId).CompletedUnits);
        Assert.Equal(7 - splits, replay.Society.Inventory.GetLot(lotId).Quantity);
        Assert.Single(replay.ExportState().Events, item => item.Kind == "food_consumed" && item.Detail == Actor);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task OfferedCartActionKeepsExactAcceptedIdentityAndHostSavesAfterLaterModelFailure(int splits)
    {
        var (state, lotId) = await SplitInventoryState("wood", splits);
        var model = new CartChoice(lotId);
        IDecisionProvider Providers(string id) => id == Actor ? model : new ActionCoverageRecorder(chooseIdle: true);
        using var world = PrivateWorldRuntime.Restore(state, Providers);
        using var host = new CheckpointHost(world);
        for (var step = 0; step < 8 && world.Society.Inventory.GetLot(lotId).ContainerLotId != Cart; step++)
            await host.AdvanceAndCheckSaved();

        var cargo = world.Society.Inventory.GetLot(lotId);
        Assert.Equal((Actor, 8 - splits, Cart), (cargo.OwnerId, cargo.Quantity, cargo.ContainerLotId));
        var selected = Assert.Single(model.Selected);
        Assert.Equal("load_handcart:" + lotId, selected);
        var accepted = world.Inhabitants.Single(person => person.InhabitantId == Actor).LastModelAttempt!;
        Assert.Equal(("ready", selected), (accepted.Status, accepted.LastAcceptedCandidateId));
        Assert.Equal(selected, world.ExportState().Society.Cognition.Runtimes.Single(item => item.InhabitantId == Actor)
            .CurrentIntention!.CandidateId);
        Assert.Equal(DecisionProviderKind.LargeLanguageModel, world.ExportState().Society.Cognition.Runtimes
            .Single(item => item.InhabitantId == Actor).CurrentIntention!.Provider);

        var acceptedState = world.ExportState();
        foreach (var damaged in new[]
                 {
                     accepted with { LastAcceptedCandidateId = " " },
                     accepted with { LastAcceptedCandidateId = "load_handcart:bad\nidentity" },
                     accepted with { LastAcceptedTick = null },
                     accepted with { LastAcceptedTick = world.WorldTick + 1 },
                     accepted with { Status = "invented" },
                 })
            Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(acceptedState with
            {
                Inhabitants = acceptedState.Inhabitants.Select(person => person.InhabitantId == Actor
                    ? person with { LastModelAttempt = damaged } : person).ToArray(),
            }));

        using var replay = host.Reload(Providers);
        using var replayHost = new CheckpointHost(replay);
        // The next call fails, so the historical accepted choice must survive
        // independently of the current safe fallback intention.
        await CompleteLaterFailure(world, host);
        await CompleteLaterFailure(replay, replayHost);
        var failed = world.Inhabitants.Single(person => person.InhabitantId == Actor).LastModelAttempt!;
        Assert.Equal((selected, accepted.LastAcceptedTick), (failed.LastAcceptedCandidateId, failed.LastAcceptedTick));
        Assert.Equal((selected, accepted.LastAcceptedTick),
            (replay.Inhabitants.Single(person => person.InhabitantId == Actor).LastModelAttempt!.LastAcceptedCandidateId,
                replay.Inhabitants.Single(person => person.InhabitantId == Actor).LastModelAttempt!.LastAcceptedTick));
        Assert.Equal("safe_idle", world.ExportState().Society.Cognition.Runtimes.Single(item => item.InhabitantId == Actor)
            .CurrentIntention!.CandidateId);
        using var reloaded = host.Reload(Providers);
        Assert.Equal(selected, reloaded.Inhabitants.Single(person => person.InhabitantId == Actor).LastModelAttempt!.LastAcceptedCandidateId);
        Assert.Contains(new OwnerWorldObservationStore(reloaded).GetSnapshot().Inhabitants.Single(person => person.Id == Actor)
            .DecisionFactors, factor => factor.Key == "last-model-choice" && factor.Detail == selected);
        Assert.Equal((Actor, 8 - splits, Cart), (reloaded.Society.Inventory.GetLot(lotId).OwnerId,
            reloaded.Society.Inventory.GetLot(lotId).Quantity, reloaded.Society.Inventory.GetLot(lotId).ContainerLotId));
    }

    private static async Task<(PrivateWorldRuntimeState State, string LotId)> SplitInventoryState(string kind, int splits)
    {
        var state = PrivateWorldRuntimeCodec.Decode(await StartingCheckpoint.Value);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, kind + "-lineage", kind, Actor, 8,
            storageBuildingId: House);
        var currentId = kind + "-lineage";
        // Controlled starting inventory, using the same split/move API and
        // operation identity as personal collection. No preceding trips are claimed.
        for (var sequence = 1; sequence <= splits; sequence++)
        {
            var previousIds = inventory.Lots.Select(lot => lot.Id).ToHashSet(StringComparer.Ordinal);
            var quantity = inventory.GetLot(currentId).Quantity - 1;
            inventory = InventoryFixture.Relocate(inventory, $"personal:{Actor}:{sequence}:{currentId}", currentId, Actor, quantity);
            currentId = Assert.Single(inventory.Lots, lot => !previousIds.Contains(lot.Id)).Id;
            if (sequence < splits)
                inventory = InventoryFixture.Relocate(inventory, "return:" + sequence, currentId, Actor, quantity, storageBuildingId: House);
        }
        if (kind == "wood")
        {
            var position = state.Inhabitants.Single(person => person.InhabitantId == Actor).Position;
            var ground = new InventoryGroundPosition(position.X, position.Y);
            inventory = InventoryFixture.Relocate(inventory, "place-cargo", currentId, Actor,
                inventory.GetLot(currentId).Quantity, groundPosition: ground);
            inventory = InventoryFixture.AddLot(inventory, Cart, "handcart", Actor, 1, groundPosition: ground);
        }
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == Actor
                ? person with { HungerBasisPoints = kind == "food" ? 3_500 : 9_500 } : person).ToArray(),
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
        };
        // Each input is already saveable and restoreable before the action.
        var bytes = PrivateWorldRuntimeCodec.Encode(state);
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), FoodProviders);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        return (restored.ExportState(), currentId);
    }

    private static async Task CompleteLaterFailure(PrivateWorldRuntime world, CheckpointHost host)
    {
        bool Failed() => world.ExportState().Events.Any(item => item.Kind == "model_attempt_status" &&
            item.Detail == Actor + ":model_unavailable");
        for (var step = 0; step < 32 && !Failed(); step++)
        {
            // Hosted replies run on the worker pool. Let the completed failure
            // reach the next host tick without assuming identical reply timing.
            await Task.Delay(5);
            await host.AdvanceAndCheckSaved();
        }
        Assert.True(Failed(), "The host must apply the controlled later model failure.");
        Assert.Equal("safe_idle", world.ExportState().Society.Cognition.Runtimes.Single(item => item.InhabitantId == Actor)
            .CurrentIntention!.CandidateId);
    }

    private static IDecisionProvider FoodProviders(string id) => id == Actor
        ? new DeterministicDecisionProvider() : new ActionCoverageRecorder(chooseIdle: true);

    private static OwnerInstructionOrder Order(PrivateWorldRuntime world, string id) =>
        Assert.Single(world.ExportState().Instructions!, item => item.InstructionId == id).Order!;

    private sealed class CheckpointHost : IDisposable
    {
        private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("clanker-lineage-checkpoint-");
        private readonly PrivateWorldRuntime world;
        private readonly PrivateWorldRuntimeService service;
        private readonly RecordingLogger<PrivateWorldRuntimeService> logger = new();
        private readonly PrivateWorldStateFile file;

        public CheckpointHost(PrivateWorldRuntime world)
        {
            this.world = world;
            file = new PrivateWorldStateFile(Path.Combine(directory.FullName, "world.json"));
            file.Save(world);
            var presence = new OwnerClientPresenceLease(TimeSpan.FromMinutes(5));
            presence.RecordAuthenticatedReconnect("test-owner");
            service = new PrivateWorldRuntimeService(world, file, presence, logger);
        }

        public async Task AdvanceAndCheckSaved()
        {
            Assert.True(await service.TryAdvanceOnceAsync(), string.Join("\n", logger.Messages));
            Assert.False(world.Society.IsPaused);
            var saved = File.ReadAllBytes(file.Path);
            Assert.Equal(world.WorldTick, PrivateWorldRuntimeCodec.Decode(saved).Society.Society.WorldTick);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), saved);
        }

        public PrivateWorldRuntime Reload(Func<string, IDecisionProvider> providers)
        {
            var saved = File.ReadAllBytes(file.Path);
            var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved), providers);
            Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
            return restored;
        }

        public void Dispose()
        {
            service.Dispose();
            directory.Delete(recursive: true);
        }
    }

    private sealed class CartChoice(string lotId) : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
        public long ProviderEpoch => 1;
        public ConcurrentQueue<string> Selected { get; } = new();

        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            request.Validate();
            var candidate = request.Observation.Candidates.SingleOrDefault(item => item.Id == "load_handcart:" + lotId);
            if (candidate is null) throw new HttpRequestException("Controlled later model failure.");
            Selected.Enqueue(candidate.Id);
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, request.Observation.InhabitantId,
                Kind, ProviderEpoch, request.Observation.RunEpoch, request.Observation.DecisionGeneration,
                request.Observation.ObservationDigest, candidate.Id, 1d,
                request.Observation.Candidates.ToDictionary(item => item.Id, item => item.Id == candidate.Id ? 1d : 0d,
                    StringComparer.Ordinal)));
        }
    }
}

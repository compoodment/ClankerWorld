using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Viewer.Control;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class MultiHeirWillAdmissionTests
{
    private const string Actor = "founder-scout";
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ALongDescendantIdentityUsesABoundedPromptKeyAndKeepsItsExactBequestAfterReload(bool rawKeyCollision)
    {
        using var handler = new LongHeirHandler();
        using var client = new HttpClient(handler);
        var provider = new OpenAiCompatibleDecisionProvider(client, () => "synthetic-key",
            new Uri("https://model.invalid/v1/chat/completions"), "synthetic-model", providerEpoch: 42);
        using var world = NewWorld(provider, longDescendant: true, rawKeyCollision: rawKeyCollision);
        var heirId = world.Society.Inhabitants.Single(person => person.Name == "Long Heir").Id;
        Assert.True(heirId.Length > CognitionWillContext.MaximumHeirKeyLength);

        Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
        await handler.Started.Task.WaitAsync(Deadline);
        for (var attempt = 0; attempt < 20 && Assert.Single(world.Society.Estates).WillStatus == "pending"; attempt++)
        {
            await Task.Delay(25);
            Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
        }

        var estate = Assert.Single(world.Society.Estates);
        Assert.Equal("accepted", estate.WillStatus);
        Assert.Equal([heirId], estate.WillHeirIds);
        Assert.Equal([new SocietyWillBequest("will-seed", heirId, 3)], estate.WillBequests);
        Assert.Equal(1, handler.RequestCount);
        Assert.StartsWith("will:heir:sha256:", handler.HeirKey);
        Assert.InRange(handler.HeirKey!.Length, 1, CognitionWillContext.MaximumHeirKeyLength);
        Assert.Equal(rawKeyCollision, handler.HeirKey!.EndsWith(":1", StringComparison.Ordinal));
        if (rawKeyCollision)
            Assert.Equal("will:heir:" + world.Society.Inhabitants.Single(person => person.Name == "Hash Twin").Id,
                handler.CollisionKey);

        var saved = world.ExportState();
        Assert.Equal(53, saved.SchemaVersion);
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(
            PrivateWorldRuntimeCodec.Encode(saved)));
        var restoredEstate = Assert.Single(restored.Society.Estates);
        Assert.Equal([heirId], restoredEstate.WillHeirIds);
        Assert.Equal(estate.WillBequests, restoredEstate.WillBequests);
        Assert.Contains(restored.Society.Inhabitants, person => person.Id == heirId);
        restored.Validate();
    }

    [Fact]
    public async Task TheWillPromptKeepsTheActorsBeliefProvenanceAndCorrectionLabels()
    {
        using var handler = new WillMemoryHandler();
        using var client = new HttpClient(handler);
        var provider = new OpenAiCompatibleDecisionProvider(client, () => "synthetic-key",
            new Uri("https://model.invalid/v1/chat/completions"), "synthetic-model", providerEpoch: 42);
        var observation = new InhabitantObservation(Actor, 10, 2, 0, "will-memory-digest", 0,
            [new(CognitionWillContext.HouseholdCandidateId, "Leave your belongings to your household.")],
            RequiresPersonalProvider: true,
            RetrievedMemories: [new CognitionMemoryExcerpt("old-belief", Actor, "family",
                "A relative said the orchard needed care.", 4, "belief", "private", "hearsay", 3500,
                "relative", 3, IsCorrected: true)],
            Will: new CognitionWillContext([new("item:1", "seed", 3)], []));

        var response = await provider.DecideAsync(new("will:memory-test", 42, observation));

        Assert.True(handler.Checked);
        Assert.Equal(CognitionWillContext.HouseholdCandidateId, response.SelectedCandidateId);
    }

    [Theory]
    [InlineData("epoch")]
    [InlineData("kind")]
    public async Task AProviderChangedDuringFinalPreparationCannotAdmitTheCompletedWill(string change)
    {
        var provider = new HeldWillProvider();
        using var world = NewWorld(provider);
        Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
        await provider.Started.Task.WaitAsync(Deadline);

        // This legal provider completion permits its awaiting continuation to
        // run inline on the completion thread, as configured HTTP adapters do.
        await Task.Run(provider.Complete).WaitAsync(Deadline);
        var changed = false;
        var result = await world.AdvanceOneTickNonBlockingAsync(null, (_, _) =>
        {
            // The host prepares child-provider assignments outside the runtime
            // gate, after preparing the tick and before admitting its changes.
            // A provider setting can change here without a world event or tick.
            if (change == "epoch") provider.ProviderEpoch++;
            else provider.Kind = DecisionProviderKind.Deterministic;
            changed = true;
            return [];
        });

        Assert.True(changed);
        Assert.True(result.Advanced);
        var estate = Assert.Single(world.Society.Estates);
        Assert.Equal("default", estate.WillStatus);
        Assert.Null(estate.WillHeirIds);
        Assert.Null(estate.WillBequests);
        Assert.Null(estate.FinalWords);
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "estate_will_accepted");
        Assert.Equal(1, provider.CallCount);
    }

    [Fact]
    public async Task LostPresenceCancelsOneWillAndReconnectUsesTheDefaultWithoutAnotherCall()
    {
        var directory = Directory.CreateTempSubdirectory("will-presence-admission-");
        try
        {
            var provider = new HeldWillProvider();
            using var world = NewWorld(provider);
            var clock = new ManualClock();
            var presence = new OwnerClientPresenceLease(TimeSpan.FromSeconds(5), clock);
            using var service = new PrivateWorldRuntimeService(world,
                new PrivateWorldStateFile(Path.Combine(directory.FullName, "world.json")), presence);

            Assert.False(await service.TryAdvanceOnceAsync());
            Assert.Equal(0, world.WorldTick);
            Assert.Equal(0, provider.CallCount);
            Assert.Null(Assert.Single(world.Society.Estates).WillStatus);

            presence.RecordAuthenticatedReconnect("owner");
            Assert.True(await service.TryAdvanceOnceAsync());
            await provider.Started.Task.WaitAsync(Deadline);
            var tick = world.WorldTick;
            clock.Advance(TimeSpan.FromSeconds(5));

            Assert.False(await service.TryAdvanceOnceAsync());
            await provider.Cancelled.Task.WaitAsync(Deadline);
            Assert.False(await service.TryAdvanceOnceAsync());
            Assert.Equal(tick, world.WorldTick);
            Assert.Equal(1, provider.CallCount);

            presence.RecordAuthenticatedReconnect("owner");
            Assert.True(await service.TryAdvanceOnceAsync());
            Assert.Equal("default", Assert.Single(world.Society.Estates).WillStatus);
            Assert.Equal(1, provider.CallCount);
            Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "estate_will_accepted");
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public async Task CancellingTheLastConfiguredWillCanFinishAccountingAndSaveThePause()
    {
        var directory = Directory.CreateTempSubdirectory("will-usage-cancellation-");
        PrivateWorldRuntime? world = null;
        InlineCancellationHandler? handler = null;
        Task? pause = null;
        try
        {
            var configuration = new ProviderConfigurationStore(Path.Combine(directory.FullName, "providers.json"),
                new("deterministic", null, null, null, null, null, null));
            _ = configuration.Configure(new("personal", "openai", "will-test-model", "will-test-key", false,
                Actor, Guid.NewGuid().ToString("N"), "Will test model"));
            var usage = new ProviderUsageStore(Path.Combine(directory.FullName, "usage.json"));
            _ = usage.Configure(new ProviderUsageLimitAction(1));
            handler = new InlineCancellationHandler();
            var provider = new ConfigurableDecisionProvider(configuration, new HeldHttpClientFactory(handler),
                usageStore: usage);
            world = NewWorld(provider);
            var stateFile = new PrivateWorldStateFile(Path.Combine(directory.FullName, "world.json"));
            var logger = new RecordingLogger<MultiHeirWillAdmissionTests>();
            var effects = new ProviderUsageWorldEffects(world, stateFile, configuration.WorldMutationGate, logger);
            usage.LimitReached += effects.PauseAtLimit;
            usage.WarningReached += effects.RecordWarning;

            Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
            await handler.Started.Task.WaitAsync(Deadline);
            Assert.Equal(1, usage.Capture().Attempts);

            // Cancellation finishes the real configured usage reservation on
            // the same thread that owns the runtime gate. Its limit callback
            // must defer gate acquisition, then save the paused world.
            pause = Task.Run(world.Pause);
            await pause.WaitAsync(Deadline);
            using var timeout = new CancellationTokenSource(Deadline);
            while (!logger.Messages.Any(message => message.Contains(
                       "provider_usage_limit_reached outcome=paused scope=installation", StringComparison.Ordinal)))
                await Task.Delay(10, timeout.Token);

            var captured = usage.Capture();
            Assert.Equal(1, captured.Attempts);
            Assert.Equal(1, captured.Abandoned);
            Assert.Equal(0, captured.Completed);
            Assert.Equal(0, captured.Failed);
            Assert.Equal(1, handler.RequestCount);
            Assert.True(world.Society.IsPaused);
            Assert.True(PrivateWorldRuntimeCodec.Decode(File.ReadAllBytes(stateFile.Path)).Society.Society.IsPaused);
            Assert.False((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
            Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "estate_will_accepted");
        }
        finally
        {
            // A failing deadlock baseline must not block again in Dispose.
            if (pause is not { IsCompleted: false })
            {
                world?.Dispose();
                handler?.Dispose();
                directory.Delete(recursive: true);
            }
        }
    }

    private static PrivateWorldRuntime NewWorld(IDecisionProvider provider, bool longDescendant = false,
        bool rawKeyCollision = false)
    {
        using var seed = new PrivateWorldRuntime(longDescendant ? new string('s', 100) : "multiheir-will-admission");
        var state = seed.ExportState();
        var checkpoint = state.Society.Society;
        if (longDescendant)
        {
            var parentId = Actor;
            for (var generation = 0; generation < 2; generation++)
            {
                var partnerId = generation == 0 ? "founder-mira" : "founder-rowan";
                var partnershipId = $"will-descendant-partnership-{generation}";
                checkpoint = SocietyFixture.ProposeRelationship(checkpoint, new(partnershipId, 1,
                    SocietyRelationshipType.Partnership, parentId, partnerId, checkpoint.WorldTick)).Checkpoint;
                checkpoint = SocietyFixture.AcceptRelationship(checkpoint, partnershipId, 1, partnerId).Checkpoint;
                var householdId = checkpoint.GetInhabitant(parentId).HouseholdId!;
                var caregivers = new[] { parentId, partnerId }
                    .Where(id => checkpoint.GetInhabitant(id).HouseholdId == householdId)
                    .Order(StringComparer.Ordinal).ToArray();
                var birth = SocietyFixture.CommitBirth(checkpoint, new($"family:{parentId}:{checkpoint.WorldTick}", 1,
                    parentId, partnerId, householdId, caregivers, [parentId, partnerId],
                    "food:camp-alpha", 2, checkpoint.WorldTick,
                    ChildName: generation == 0 ? "Intermediate Parent" : "Long Heir", PrimaryCaregiverId: parentId));
                var childId = Assert.IsType<string>(birth.CreatedId);
                checkpoint = birth.Checkpoint;
                // Keep the actual inherited identities and birth records, with
                // adult ages so this test does not start dependent-care searches.
                var adultBirth = checkpoint.LifeTickAt(checkpoint.WorldTick) - 20 * checkpoint.Config.TicksPerLifecycleAge;
                checkpoint = checkpoint with
                {
                    Inhabitants = checkpoint.Inhabitants.Select(person => person.Id == childId ? person with
                    {
                        BirthTick = adultBirth,
                        BirthLifeTick = checkpoint.LifeClock is null ? null : adultBirth,
                        AgeBand = SocietyAgeBand.Adult,
                        LastLifecycleYearChecked = 20,
                    } : person).ToArray(),
                };
                var position = state.Map.Tiles.Select(tile => tile.Position).First(point =>
                    state.Map.IsPassable(point) && state.Inhabitants.All(person => person.Position != point) &&
                    state.Map.FootNeighbors(point).Any(state.Map.IsPassable));
                state = state with
                {
                    Inhabitants = state.Inhabitants.Append(new PlaytestInhabitantState(childId, position,
                        9_500, 0, "careful", "keep the orchard going")).ToArray(),
                };
                parentId = childId;
            }
            if (rawKeyCollision)
            {
                var rawIdentity = "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(parentId))).ToLowerInvariant();
                checkpoint = SocietyFixture.AddAdult(checkpoint, rawIdentity, checkpoint.GetInhabitant(Actor).HouseholdId).Checkpoint;
                checkpoint = SocietyFixture.RenameInhabitant(checkpoint, rawIdentity, "Hash Twin").Checkpoint;
                var position = state.Map.Tiles.Select(tile => tile.Position).First(point =>
                    state.Map.IsPassable(point) && state.Inhabitants.All(person => person.Position != point) &&
                    state.Map.FootNeighbors(point).Any(state.Map.IsPassable));
                state = state with
                {
                    Inhabitants = state.Inhabitants.Append(new PlaytestInhabitantState(rawIdentity, position,
                        9_500, 0, "careful", "keep the orchard going")).ToArray(),
                };
            }
        }
        checkpoint = checkpoint with
        {
            Inventory = InventoryFixture.AddLot(checkpoint.Inventory, "will-seed", "seed", Actor, 3),
        };
        checkpoint = SocietyFixture.Kill(checkpoint, Actor, SocietyDeathCause.Accident).Checkpoint;
        var physical = state.Inhabitants.Single(item => item.InhabitantId == Actor);
        var deceased = checkpoint.GetInhabitant(Actor);
        var estate = Assert.Single(checkpoint.Estates);
        checkpoint = checkpoint with
        {
            Inventory = InventoryFixture.Relocate(checkpoint.Inventory, "fixture-death", "will-seed",
                estate.Id, 3, groundPosition: new InventoryGroundPosition(physical.Position.X, physical.Position.Y)),
        };
        state = state with
        {
            Society = state.Society with { Society = checkpoint },
            Inhabitants = state.Inhabitants.Where(item => item.InhabitantId != Actor).ToArray(),
            DeceasedInhabitants = [new PlaytestDeceasedInhabitantState(Actor, 0,
                checkpoint.AgeAt(deceased, 0), physical)],
        };
        return PrivateWorldRuntime.Restore(state,
            id => id == Actor ? provider : new DeterministicDecisionProvider());
    }

    private sealed class HeldWillProvider : IDecisionProvider
    {
        // Inline continuations let completion return only after the awaiting
        // runtime task has observed the reply; no polling of private tasks.
        private readonly TaskCompletionSource<CognitionDecisionResponse> reply = new();
        private CognitionDecisionRequest? request;
        private CancellationTokenRegistration registration;
        private int callCount;

        public DecisionProviderKind Kind { get; set; } = DecisionProviderKind.LargeLanguageModel;
        public long ProviderEpoch { get; set; } = 42;
        public int CallCount => Volatile.Read(ref callCount);
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest value,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref callCount);
            request = value;
            registration = cancellationToken.Register(() =>
            {
                reply.TrySetCanceled(cancellationToken);
                Cancelled.TrySetResult(true);
            });
            Started.TrySetResult(true);
            return new(reply.Task);
        }

        public void Complete()
        {
            var current = request ?? throw new InvalidOperationException("No will was started.");
            var heirs = current.Observation.Will!.Heirs.Take(2).Select(item => item.Key).ToArray();
            reply.TrySetResult(new CognitionDecisionResponse(current.RequestId, Actor,
                DecisionProviderKind.LargeLanguageModel, current.ProviderEpoch,
                current.Observation.RunEpoch, current.Observation.DecisionGeneration,
                current.Observation.ObservationDigest, CognitionWillContext.HeirsCandidateId, 1,
                new Dictionary<string, double> { [CognitionWillContext.HeirsCandidateId] = 1 },
                Will: new CognitionWillChoice(heirs, CognitionWillContext.EqualSplit,
                    FinalWords: "Keep the orchard going.")));
            registration.Dispose();
        }
    }

    private sealed class ManualClock : TimeProvider
    {
        private long timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => timestamp;
        public void Advance(TimeSpan amount) => timestamp += amount.Ticks;
    }

    private sealed class HeldHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class WillMemoryHandler : HttpMessageHandler
    {
        public bool Checked { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var messages = payload.RootElement.GetProperty("messages");
            Assert.Contains("a corrected belief is superseded history", messages[0].GetProperty("content").GetString());
            using var input = JsonDocument.Parse(messages[1].GetProperty("content").GetString()!);
            var memory = Assert.Single(input.RootElement.GetProperty("retrieved_memories").EnumerateArray());
            Assert.Equal("belief", memory.GetProperty("kind").GetString());
            Assert.Equal("private", memory.GetProperty("visibility").GetString());
            Assert.Equal("hearsay", memory.GetProperty("provenance").GetString());
            Assert.Equal(3500, memory.GetProperty("confidence_basis_points").GetInt32());
            Assert.Equal("relative", memory.GetProperty("source_agent_id").GetString());
            Assert.Equal(3, memory.GetProperty("source_event_id").GetInt64());
            Assert.True(memory.GetProperty("is_corrected").GetBoolean());
            Checked = true;
            return new(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {"choices":[{"message":{"content":"{\"selected_candidate_id\":\"will:household\",\"confidence\":1}"}}]}
                    """),
            };
        }
    }

    private sealed class LongHeirHandler : HttpMessageHandler
    {
        private int requestCount;
        public int RequestCount => Volatile.Read(ref requestCount);
        public string? HeirKey { get; private set; }
        public string? CollisionKey { get; private set; }
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref requestCount);
            using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            using var input = JsonDocument.Parse(payload.RootElement.GetProperty("messages")[1]
                .GetProperty("content").GetString()!);
            var heirs = input.RootElement.GetProperty("possible_heirs").EnumerateArray().ToArray();
            Assert.All(heirs, heir => Assert.InRange(heir.GetProperty("id").GetString()!.Length,
                1, CognitionWillContext.MaximumHeirKeyLength));
            Assert.Equal(heirs.Length, heirs.Select(heir => heir.GetProperty("id").GetString()).Distinct().Count());
            HeirKey = heirs.Single(heir => heir.GetProperty("name").GetString() == "Long Heir")
                .GetProperty("id").GetString()!;
            CollisionKey = heirs.SingleOrDefault(heir => heir.GetProperty("name").GetString() == "Hash Twin") is { ValueKind: JsonValueKind.Object } collision
                ? collision.GetProperty("id").GetString() : null;
            var answer = JsonSerializer.Serialize(new
            {
                selected_candidate_id = CognitionWillContext.HeirsCandidateId,
                confidence = 1,
                heirs = new[] { HeirKey },
                split = CognitionWillContext.EqualSplit,
            });
            var reply = JsonSerializer.Serialize(new { choices = new[] { new { message = new { content = answer } } } });
            Started.TrySetResult(true);
            return new(HttpStatusCode.OK) { Content = new StringContent(reply) };
        }
    }

    private sealed class InlineCancellationHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource<HttpResponseMessage> reply = new();
        private CancellationTokenRegistration registration;
        private int requestCount;
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int RequestCount => Volatile.Read(ref requestCount);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref requestCount);
            registration = cancellationToken.Register(() => reply.TrySetCanceled(cancellationToken));
            Started.TrySetResult(true);
            return reply.Task;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) registration.Dispose();
            base.Dispose(disposing);
        }
    }
}

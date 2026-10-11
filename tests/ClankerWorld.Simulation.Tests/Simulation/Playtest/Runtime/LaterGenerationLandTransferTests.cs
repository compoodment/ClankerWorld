using System.Collections.Concurrent;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class GrownAgentHelperMemoryTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NativeTransferPartiesSurviveHostedConsentAndCheckpointReload(bool laterGeneration)
    {
        var state = PrivateWorldRuntimeCodec.Decode(await (laterGeneration ? LaterAdult : ConversationAdult).Value);
        var actor = state.Society.Society.Births.OrderBy(birth => birth.CommittedTick).Last().ChildId;
        Assert.True(laterGeneration ? actor.Length > 256 : actor.Length <= 256);
        var town = state.Towns![0];
        // As in the civic fixtures, arrange board proximity and comfort only.
        // Birth identities, ages, households, titles and permissions stay native.
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person with
            {
                Position = town.OriginSite!.Value,
                HungerBasisPoints = 10_000,
                LastDecisionContext = null,
                Survival = (person.Survival ?? new SurvivalCondition()) with { WarmthBasisPoints = 10_000, IllnessBasisPoints = 0 },
            }).ToArray(),
        };
        var household = state.Society.Society.GetInhabitant(actor).HouseholdId!;
        var source = state.HouseholdLandUseRights!.First(right => right.TownId == town.Id && right.HouseholdId == household);
        var target = state.Society.Society.Households.Single(item => item.Id == "household:camp-beta").Id;
        var policy = new NativeTransferChoices(actor, target, source.Tiles.ToArray());
        using var world = PrivateWorldRuntime.Restore(state, _ => policy);
        world.Resume();
        world.SubmitInstruction(new("native-transfer-guidance", "owner:test", actor, OwnerInstructionKind.Suggestive,
            "Consider offering this existing household permission to the specified household at the public notice place."));
        var directory = Directory.CreateTempSubdirectory("native-transfer-checkpoint-");
        try
        {
            var file = new PrivateWorldStateFile(Path.Combine(directory.FullName, "world.json"), _ => policy);
            file.Save(world);
            var presence = new OwnerClientPresenceLease(TimeSpan.FromMinutes(5));
            presence.RecordAuthenticatedReconnect("test-owner");
            using var service = new PrivateWorldRuntimeService(world, file, presence);
            async Task AdvanceUntil(Func<bool> finished)
            {
                for (var tick = 0; tick < 40 && !finished(); tick++)
                {
                    Assert.True(await service.TryAdvanceOnceAsync());
                    await WaitForRequests(world);
                    Assert.False(world.Society.IsPaused);
                    Assert.Equal(world.WorldTick, PrivateWorldRuntimeCodec.Decode(File.ReadAllBytes(file.Path)).Society.Society.WorldTick);
                }
                Assert.True(finished());
            }
            await AdvanceUntil(() => world.Towns[0].LandHearings.Transfers.Count == 1);
            policy.Propose = false;
            var pending = Assert.Single(world.Towns[0].LandHearings.Transfers);
            Assert.Equal(actor, pending.FilerId);
            Assert.Equal("pending", pending.Status);
            Assert.Empty(pending.Responses);
            Assert.Null(pending.Receipt);
            Assert.Contains(pending.Parties, party => party.AdultIds.Contains(actor));
            Assert.Equal(source.Tiles, pending.Tiles);
            Assert.Equal(state.HouseholdLandUseRights, world.ExportState().HouseholdLandUseRights);
            Assert.Contains(policy.Selected, choice => choice.Actor == actor && choice.Id.Contains("|land_transfer_propose|", StringComparison.Ordinal));
            AssertMalformedTransferPeople(world.ExportState(), pending);
            using (var pendingReload = file.LoadOrCreate(state.WorldSeed))
                Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(pendingReload.ExportState()));

            // Reading and silence supply no acceptance, even for the filer.
            policy.Read = true;
            await AdvanceUntil(() => pending.Parties.SelectMany(party => party.AdultIds).All(id =>
                world.Towns[0].Governance!.Knowledge.Any(receipt => receipt.AgentId == id && receipt.NoticeId == pending.NoticeId)));
            Assert.Empty(world.Towns[0].LandHearings.Transfers[0].Responses);
            Assert.Equal(state.HouseholdLandUseRights, world.ExportState().HouseholdLandUseRights);
            policy.Accept = true;
            policy.Hold = actor;
            await AdvanceUntil(() => world.Towns[0].LandHearings.Transfers[0].Responses.Count == pending.Parties.Sum(party => party.AdultIds.Count) - 1);
            var waiting = world.Towns[0].LandHearings.Transfers[0];
            Assert.Equal("pending", waiting.Status);
            Assert.DoesNotContain(waiting.Responses, response => response.AgentId == actor);
            Assert.Null(waiting.Receipt);
            Assert.Equal(state.HouseholdLandUseRights, world.ExportState().HouseholdLandUseRights);
            AssertMalformedTransferPeople(world.ExportState(), waiting);
            var beforeRollback = PrivateWorldRuntimeCodec.Encode(world.ExportState());
            Assert.False((await world.AdvanceOneTickAsync(() => false)).Advanced);
            Assert.Equal(beforeRollback, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
            policy.Hold = null;
            await AdvanceUntil(() => world.Towns[0].LandHearings.Transfers[0].Status == "transferred");
            var completed = world.Towns[0].LandHearings.Transfers[0];
            Assert.Equal(actor, completed.FilerId);
            Assert.Equal(pending.Parties.Sum(party => party.AdultIds.Count), completed.Responses.Count);
            Assert.Contains(completed.Responses, response => response.AgentId == actor && response.PartyAdults.Contains(actor));
            var receipt = Assert.IsType<TownLandTransferReceipt>(completed.Receipt);
            Assert.Contains(receipt.Parties, party => party.AdultIds.Contains(actor));
            Assert.All(completed.Responses, response => Assert.Contains(world.Towns[0].Governance!.Knowledge,
                learned => learned.AgentId == response.AgentId && learned.NoticeId == completed.NoticeId && learned.LearnedTick <= response.Tick));
            Assert.Single(world.Towns[0].LandHearings.Adjustments, adjustment => adjustment.Id == receipt.AdjustmentId && adjustment.TransferId == completed.Id);
            Assert.All(source.Tiles, tile =>
            {
                var right = Assert.Single(world.HouseholdLandUseRights, item => item.Tiles.Contains(tile));
                Assert.Equal((target, source.GrantedTick, source.AgreedEndTick, source.GrantSource),
                    (right.HouseholdId, right.GrantedTick, right.AgreedEndTick, right.GrantSource));
            });
            AssertMalformedTransferPeople(world.ExportState(), completed);
            policy.Read = policy.Accept = false;
            world.Pause();
            world.Resume();
            Assert.True(await service.TryAdvanceOnceAsync());
            await WaitForRequests(world);
            using var reload = file.LoadOrCreate(state.WorldSeed);
            var saved = PrivateWorldRuntimeCodec.Encode(world.ExportState());
            Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(reload.ExportState()));
            // Both paired worlds start from disk with the same blocking admission mode.
            using var original = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved), _ => new QuietProvider());
            using var replay = PrivateWorldRuntime.Restore(reload.ExportState(), _ => new QuietProvider());
            for (var tick = 0; tick < 3; tick++)
            {
                Assert.True((await original.AdvanceOneTickAsync()).Advanced);
                Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
                Assert.Equal(PrivateWorldRuntimeCodec.Encode(original.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
            }
        }
        finally { directory.Delete(recursive: true); }
    }

    private static void AssertMalformedTransferPeople(PrivateWorldRuntimeState state, TownLandTransferRequest request)
    {
        var party = request.Parties.Single(item => item.AdultIds.Contains(request.FilerId));
        var people = party.AdultIds;
        foreach (var invalid in new[] { "", " " + request.FilerId, request.FilerId + "\0", request.FilerId + ":unknown" })
        {
            AssertInvalid(request with { FilerId = invalid });
            AssertInvalid(request with
            {
                Parties = request.Parties.Select(item => item == party
                ? item with { AdultIds = people.Select(id => id == request.FilerId ? invalid : id).Order(StringComparer.Ordinal).ToArray() } : item).ToArray()
            });
            foreach (var response in request.Responses)
                AssertInvalid(request with
                {
                    Responses = request.Responses.Select(item => item == response
                    ? item with { PartyAdults = item.PartyAdults.Append(invalid).Order(StringComparer.Ordinal).ToArray() } : item).ToArray()
                });
            if (request.Receipt is { } receipt)
                AssertInvalid(request with
                {
                    Receipt = receipt with
                    {
                        Parties = receipt.Parties.Select(item => item.HouseholdId == party.HouseholdId
                    ? item with { AdultIds = item.AdultIds.Append(invalid).Order(StringComparer.Ordinal).ToArray() } : item).ToArray()
                    }
                });
        }
        foreach (var invalid in new[] { people.Append(people[0]).Order(StringComparer.Ordinal).ToArray(), people.Reverse().ToArray() })
            AssertInvalid(request with { Parties = request.Parties.Select(item => item == party ? item with { AdultIds = invalid } : item).ToArray() });
        AssertInvalid(request with { Id = new string('k', 257) });
        AssertInvalid(request with { RightVersions = request.RightVersions.Select(version => version with { Version = "stale" }).ToArray() });
        if (request.Receipt is not null)
            AssertInvalid(request with { Responses = request.Responses.Where(response => response.AgentId != request.FilerId).ToArray() });

        void AssertInvalid(TownLandTransferRequest damaged)
        {
            var corrupted = state with
            {
                Towns = state.Towns!.Select(town => town.Id == request.TownId
                ? town with { LandHearings = town.LandHearings with { Transfers = town.LandHearings.Transfers.Select(item => item.Id == request.Id ? damaged : item).ToArray() } } : town).ToArray()
            };
            Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(corrupted));
        }
    }

    private sealed class NativeTransferChoices(string actor, string target, GridPoint[] tiles) : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
        public long ProviderEpoch => 0;
        public bool Propose { get; set; } = true;
        public bool Read { get; set; }
        public bool Accept { get; set; }
        public string? Hold { get; set; }
        public ConcurrentQueue<(string Actor, string Id)> Selected { get; } = new();
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var observation = request.Observation;
            CognitionCandidate? Pick(string action) => observation.Candidates.FirstOrDefault(item => item.Id.Contains("|" + action + "|", StringComparison.Ordinal));
            var proposal = Propose && observation.InhabitantId == actor ? Pick("land_transfer_propose") : null;
            var choice = proposal ?? (Read ? Pick("land_transfer_read") ?? Pick("read") : null) ??
                (Accept && observation.InhabitantId != Hold ? Pick("land_transfer_accept") : null) ??
                observation.Candidates.Single(item => item.Id == "safe_idle");
            var proposing = choice == proposal;
            if (proposing)
            {
                Assert.Contains(target, choice.Description, StringComparison.Ordinal);
                Propose = false;
            }
            Selected.Enqueue((observation.InhabitantId, choice.Id));
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, observation.InhabitantId, Kind, ProviderEpoch,
                observation.RunEpoch, observation.DecisionGeneration, observation.ObservationDigest, choice.Id, 1,
                observation.Candidates.ToDictionary(item => item.Id, item => item.Id == choice.Id ? 1d : 0d, StringComparer.Ordinal),
                CivicLandTiles: proposing ? tiles.Select(tile => new CognitionLandTile(tile.X, tile.Y)).ToArray() : null,
                CivicLandHearing: proposing ? new(HouseholdId: target) : null));
        }
    }
}

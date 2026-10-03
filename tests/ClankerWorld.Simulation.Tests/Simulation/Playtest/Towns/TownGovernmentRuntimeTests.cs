using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;
using ClankerWorld.Viewer.Observation;
using GodotSnapshot = ClankerWorld.GodotClient.UI.OwnerWorldSnapshot;

namespace ClankerWorld.Simulation.Tests;

public sealed class TownGovernmentRuntimeTests
{
    private const string Author = "founder:00000000000000000000000000000001";
    private const int Day = 40;

    private static PrivateWorldRuntime NewWorld(Func<string, IDecisionProvider> providers)
    {
        using var generated = NormalPathWorld.CreateGenerated("government-personal-path", providers);
        var state = generated.ExportState();
        var society = state.Society.Society;
        var oldDay = society.Config.TicksPerWorldDay;
        return PrivateWorldRuntime.Restore(state with
        {
            Inhabitants = state.Inhabitants.Select(p => p with { Position = state.Towns![0].OriginSite!.Value, HungerBasisPoints = 8_000 }).ToArray(),
            WorldSystems = RegionalWeatherRules.Initialize(state.WorldSystems! with
            { Config = state.WorldSystems.Config with { TicksPerDay = Day }, RegionalWeather = null }, state.Map),
            Society = state.Society with
            {
                Society = society with
                {
                    Config = society.Config with { TicksPerWorldDay = Day },
                    Inhabitants = society.Inhabitants.Select(p => p with
                    { BirthTick = p.BirthTick / oldDay * Day, BirthLifeTick = p.BirthLifeTick is { } birth ? birth / oldDay * Day : null }).ToArray()
                }
            }
        }, providers);
    }

    [Fact]
    public async Task PersonalModelTurnsElectMayorAndPausedVotesSurviveRollbackReloadAndReplay()
    {
        var provider = new MayorProvider();
        using var world = NewWorld(_ => provider);
        var original = world.ExportState();
        for (var step = 0; step < 20 && world.Towns[0].Government!.Changes.Count == 0; step++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Single(world.Towns[0].Government!.Changes);
        world.Pause();
        var paused = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(paused, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(paused), _ => new MayorProvider());
        Assert.Equal(paused, PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        world.Resume(); replay.Resume();
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False((await world.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        for (var step = 0; step < Day * 3 && world.Towns[0].Government!.Offices.Count == 0; step++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
            world.Validate(); replay.Validate();
        }
        var government = world.Towns[0].Government!;
        Assert.Equal(Author, Assert.Single(government.Offices).HolderId);
        Assert.Equal("completed", Assert.Single(government.Changes).Status);
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        Assert.Contains(provider.Seen, id => id.Contains("|government_yes|", StringComparison.Ordinal));
        Assert.Contains(provider.Seen, id => id.Contains("|mayor_vote|", StringComparison.Ordinal));
        Assert.Contains(provider.Notes, note => note.Contains("Resident government", StringComparison.Ordinal));
        Assert.Equal(original.Towns![0].ResidentIds, world.Towns[0].ResidentIds);
        Assert.Equal(original.TownLandTitles!.Select(t => t.Id), world.ExportState().TownLandTitles!.Select(t => t.Id));
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var snapshot = new OwnerWorldObservationStore(world).GetSnapshot();
        var client = JsonSerializer.Deserialize<GodotSnapshot>(JsonSerializer.Serialize(snapshot, json), json)!;
        var visible = Assert.Single(client.Towns).Government!;
        Assert.Equal("land disputes and permission expiries", Assert.Single(visible.Offices).Mandate);
        Assert.NotNull(visible.Offices[0].HolderName);
        Assert.Equal("completed", visible.LatestElection!.Stage);
        Assert.Contains(world.ExportState().Events, e => e.Kind == "town_civic_mayor");
    }

    [Theory]
    [InlineData("majority")]
    [InlineData("openingRoster")]
    [InlineData("term")]
    [InlineData("officeSource")]
    [InlineData("winnerVotes")]
    [InlineData("mandate")]
    [InlineData("sequence")]
    [InlineData("duplicateMandate")]
    [InlineData("nullChanges")]
    public async Task DamagedGovernmentAuthorityIsRefused(string damage)
    {
        using var world = NewWorld(_ => new ActionCoverageRecorder(chooseIdle: true));
        for (var tick = 0; tick < Day; tick++) await world.AdvanceOneTickAsync();
        var state = world.ExportState();
        var town = state.Towns![0];
        var adults = town.ResidentIds;
        var (council, government) = TownGovernmentRules.RegisterMayor(town.Governance!, town.Government!, adults[0], "land", adults, 0);
        (council, government) = TownGovernmentRules.Propose(council, government, town.Id, adults[0],
            new(TownArrangementRules.Council, TownArrangementRules.Mayor), false, adults, 0, Day);
        foreach (var voter in adults.Take(3)) government = TownGovernmentRules.Vote(government, government.Changes[0].Id, voter, true, 0);
        (council, government) = TownGovernmentRules.Advance(council, government, town.Id, town.Name, state.WorldSeed, adults, 0, Day);
        government = TownGovernmentRules.VoteMayor(government, TownGovernmentRules.RoundToken(government.Contest!), adults[0], adults[0], 0);
        (council, government) = TownGovernmentRules.Advance(council, government, town.Id, town.Name, state.WorldSeed, adults, Day, Day);
        var healthy = state with { Towns = [town with { Governance = council, Government = government }] };
        var encoded = PrivateWorldRuntimeCodec.Encode(healthy);
        using (var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(encoded)))
        {
            restored.Validate();
            Assert.Equal(encoded, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        }
        var document = JsonNode.Parse(encoded)!;
        var saved = document["state"]!["towns"]![0]!["government"]!;
        switch (damage)
        {
            case "majority": saved["changes"]![0]!["votes"] = new JsonArray(); break;
            case "openingRoster": saved["changes"]![0]!["openingVoters"] = new JsonArray(); break;
            case "term": saved["offices"]![0]!["termEndTick"] = Day * 30; break;
            case "officeSource": saved["offices"]![0]!["electionId"] = "invented"; break;
            case "winnerVotes": saved["contestHistory"]![0]!["rounds"]![0]!["ballots"] = new JsonArray(); break;
            case "mandate": saved["offices"]![0]!["mandates"] = "ordinary"; break;
            case "sequence": saved["sequence"] = 0; break;
            case "duplicateMandate": saved["offices"]!.AsArray().Add(saved["offices"]![0]!.DeepClone()); break;
            case "nullChanges": saved["changes"]!.AsArray().Add(null); break;
        }
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Decode(Encoding.UTF8.GetBytes(document.ToJsonString())));
    }

    [Fact]
    public async Task GovernmentProposalsAreOfferedAtTheNoticePlaceAndMayoralConsentOnlyWhileAnOfficeIsInPlay()
    {
        var provider = new RecordingProvider();
        using var initial = NewWorld(_ => provider);
        var state = initial.ExportState();
        var town = state.Towns![0];
        var board = town.OriginSite!.Value;
        var away = town.BorderTiles.Where(state.Map.IsPassable)
            .OrderByDescending(point => state.Map.FootDistance(point, board)).ThenBy(point => point.Y).ThenBy(point => point.X).First();
        const string traveler = "founder:00000000000000000000000000000002";
        using var world = PrivateWorldRuntime.Restore(state with
        {
            Inhabitants = state.Inhabitants.Select(p => p.InhabitantId == traveler ? p with { Position = away } : p).ToArray(),
        }, _ => provider);

        Assert.True((await world.AdvanceOneTickAsync()).Advanced);

        // Each choice costs prompt space on every model call; residents propose at the notice place.
        Assert.Contains(provider.Offered(Author), id => id.Contains("|government_propose|", StringComparison.Ordinal));
        Assert.DoesNotContain(provider.Offered(traveler), id => id.Contains("|government_propose|", StringComparison.Ordinal));
        // No office exists or is being created, so nobody is asked to seek one yet.
        Assert.DoesNotContain(provider.Offered(Author), id => id.Contains("|mayor_register|", StringComparison.Ordinal));
        Assert.DoesNotContain(provider.Offered(traveler), id => id.Contains("|mayor_register|", StringComparison.Ordinal));
    }

    private sealed class RecordingProvider : IDecisionProvider
    {
        private readonly ConcurrentDictionary<string, ConcurrentQueue<string>> offered = new(StringComparer.Ordinal);
        public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
        public long ProviderEpoch => 1;
        public string[] Offered(string id) => offered.TryGetValue(id, out var queue) ? queue.ToArray() : [];
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var o = request.Observation;
            foreach (var item in o.Candidates) offered.GetOrAdd(o.InhabitantId, _ => new()).Enqueue(item.Id);
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, o.InhabitantId, Kind, ProviderEpoch,
                o.RunEpoch, o.DecisionGeneration, o.ObservationDigest, "safe_idle", 1,
                o.Candidates.ToDictionary(c => c.Id, c => c.Id == "safe_idle" ? 1d : 0d, StringComparer.Ordinal)));
        }
    }

    private sealed class MayorProvider : IDecisionProvider
    {
        public ConcurrentQueue<string> Seen { get; } = new();
        public ConcurrentQueue<string> Notes { get; } = new();
        public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
        public long ProviderEpoch => 1;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var o = request.Observation;
            foreach (var item in o.Candidates) Seen.Enqueue(item.Id);
            if (o.Self?.CivicNote is { } note) Notes.Enqueue(note);
            var choice = o.Candidates.FirstOrDefault(c => c.Id.Contains("|read|", StringComparison.Ordinal)) ??
                o.Candidates.FirstOrDefault(c => c.Id.Contains("|government_yes|", StringComparison.Ordinal)) ??
                o.Candidates.FirstOrDefault(c => c.Id.Contains("|mayor_vote|", StringComparison.Ordinal) && c.Id.EndsWith("|" + Author, StringComparison.Ordinal));
            if (choice is null && o.InhabitantId == Author)
                choice = o.Candidates.FirstOrDefault(c => c.Id.Contains("|mayor_register|land|", StringComparison.Ordinal)) ??
                    o.Candidates.FirstOrDefault(c => c.Id.Contains("|government_propose|council+mayor|", StringComparison.Ordinal));
            choice ??= o.Candidates.Single(c => c.Id == "safe_idle");
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, o.InhabitantId, Kind, ProviderEpoch,
                o.RunEpoch, o.DecisionGeneration, o.ObservationDigest, choice.Id, 1,
                o.Candidates.ToDictionary(c => c.Id, c => c.Id == choice.Id ? 1d : 0d, StringComparer.Ordinal)));
        }
    }
}

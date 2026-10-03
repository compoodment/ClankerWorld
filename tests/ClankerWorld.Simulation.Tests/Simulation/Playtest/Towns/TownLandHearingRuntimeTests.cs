using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed class TownLandHearingRuntimeTests
{
    private const string Judge = "founder:00000000000000000000000000000001";
    private const string Filer = "founder:00000000000000000000000000000003";
    private const string Waiver = "founder:00000000000000000000000000000004";
    private const int Day = 40;

    [Fact]
    public async Task FreshPersonalTurnsPublishHearAndConfirmHouseholdCaseWithoutChangingPrivateProperty()
    {
        var provider = new HearingProvider();
        using var world = NewWorld(provider);
        var generated = world.ExportState();
        var household = generated.Society.Society.GetInhabitant(Filer).HouseholdId!;
        var right = generated.HouseholdLandUseRights!.Where(item => item.HouseholdId == household)
            .OrderBy(item => item.Id, StringComparer.Ordinal).First();
        provider.Plot = right.Tiles.ToArray();

        // Government authority is created by the same admitted personal turns as a normal mayor election.
        await UntilAsync(world, () => world.Towns[0].Government!.Offices.Any(), Day * 3, provider);
        var office = Assert.Single(world.Towns[0].Government!.Offices);
        Assert.Equal(Judge, office.HolderId);
        Assert.Equal("land", office.Mandates);
        var property = PrivateProperty(world.ExportState());
        var permissions = Permissions(world.ExportState());
        provider.HearingsEnabled = true;
        await UntilAsync(world, () => world.Towns[0].LandHearings.Cases.Count > 0, 20, provider);

        var opened = Assert.Single(world.Towns[0].LandHearings.Cases);
        var revision = Assert.Single(opened.Revisions);
        Assert.Equal("pending", opened.Status);
        Assert.Equal(right.Tiles, revision.Tiles);
        Assert.Equal(revision.PublishedTick + Day, revision.DeadlineTick);
        Assert.Equal(Filer, Assert.Single(opened.Filings).AgentId);
        Assert.Equal("confirm", revision.RequestedOutcome.Kind);
        var governance = world.Towns[0].Governance!;
        var notice = Assert.Single(governance.Notices, item => item.Id == revision.NoticeId);
        Assert.Equal("land_hearing", notice.Kind);
        Assert.Equal(opened.Id + ":1", notice.SubjectId);
        Assert.Contains(governance.Knowledge, receipt => receipt.NoticeId == notice.Id && receipt.AgentId == Filer);
        Assert.DoesNotContain(governance.Knowledge, receipt => receipt.NoticeId == notice.Id && receipt.AgentId == Judge);
        Assert.Empty(opened.Responses);

        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False((await world.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        var replayProvider = new HearingProvider { HearingsEnabled = true, Plot = provider.Plot };
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(before), _ => replayProvider);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        for (var step = 0; step < Day * 2 && world.Towns[0].LandHearings.Cases[0].Status != "settled"; step++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
        }

        var settled = Assert.Single(world.Towns[0].LandHearings.Cases);
        Assert.Equal("settled", settled.Status);
        var ruling = Assert.Single(settled.Rulings);
        Assert.Equal(1, ruling.Revision);
        Assert.Equal("confirm", ruling.Outcome.Kind);
        Assert.Equal(Judge, ruling.Judge.AgentId);
        Assert.Equal("land_mayor", ruling.Judge.Kind);
        Assert.Equal(office.ElectionId, ruling.Judge.AuthorityId);
        Assert.True(ruling.Tick < revision.DeadlineTick);
        Assert.Empty(ruling.AdjustmentIds);
        Assert.Empty(world.Towns[0].LandHearings.Adjustments);
        Assert.Contains(settled.Responses, response => response.Revision == 1 && response.AgentId == Filer && response.Kind == "answer");
        Assert.Contains(settled.Responses, response => response.Revision == 1 && response.AgentId == Waiver && response.Kind == "waive");
        Assert.Equal(new[] { Filer, Waiver }, settled.Responses.Select(response => response.AgentId).Distinct().Order(StringComparer.Ordinal));
        Assert.Contains(settled.Reads, read => read.AgentId == Judge && read.Revision == 1 && ruling.EvidenceIds.All(read.EvidenceIds.Contains));
        Assert.NotEmpty(ruling.EvidenceIds);
        Assert.All(ruling.EvidenceIds, id => Assert.Contains(settled.Evidence, evidence => evidence.Id == id && evidence.Kind == "record"));
        Assert.Contains(world.ExportState().Events, item => item.Kind == "land_case_ruling");
        foreach (var action in new[] { "hearing_file", "read", "hearing_inspect", "hearing_answer", "hearing_waive", "hearing_rule" })
            Assert.Contains(provider.Selected, item => item.Contains("|" + action + "|", StringComparison.Ordinal));
        Assert.Equal(permissions, Permissions(world.ExportState()));
        Assert.Equal(property, PrivateProperty(world.ExportState()));
        Assert.Equal(generated.Towns![0].ResidentIds, world.Towns[0].ResidentIds);
        world.Validate();
        replay.Validate();
        var final = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.Equal(final, PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        using (var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(final), _ => new HearingProvider { HearingsEnabled = true, Plot = provider.Plot }))
        {
            restored.Validate();
            Assert.Equal(final, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
            Assert.Equal(property, PrivateProperty(restored.ExportState()));
        }
        AssertMalformedHearingSavesRefused(final);
        world.Pause();
        var paused = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(paused, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
    }

    [Fact]
    public async Task ActualPermissionExpiryPublishesUnlearnedCaseAndRetainsProvisionalPermissionAcrossPauseAndReload()
    {
        using var generated = NormalPathWorld.CreateGenerated("government-personal-path", _ => new ActionCoverageRecorder(chooseIdle: true));
        var state = generated.ExportState();
        var original = state.HouseholdLandUseRights![0];
        var ending = original with { AgreedEndTick = 1 };
        // A near-event term fixture changes only an existing permission's agreed expiry.
        // No Council decision, title, physical property, founder placement or hearing is fabricated.
        using var world = PrivateWorldRuntime.Restore(state with
        {
            HouseholdLandUseRights = state.HouseholdLandUseRights.Select(right => right.Id == original.Id ? ending : right).ToArray()
        }, _ => new ActionCoverageRecorder(chooseIdle: true));
        var permissions = Permissions(world.ExportState());
        var property = PrivateProperty(world.ExportState());

        Assert.True((await world.AdvanceOneTickAsync()).Advanced);

        var item = Assert.Single(world.Towns[0].LandHearings.Cases);
        var revision = Assert.Single(item.Revisions);
        Assert.Equal("pending", item.Status);
        Assert.Equal("expiry", item.Kind);
        Assert.Equal(original.Id, Assert.Single(item.Filings).AuthorityId);
        Assert.Null(item.Filings[0].AgentId);
        Assert.Equal(1, revision.PublishedTick);
        Assert.Equal(1 + state.Society.Society.Config.TicksPerWorldDay, revision.DeadlineTick);
        Assert.Equal(original.Tiles, revision.Tiles);
        Assert.Contains(world.Towns[0].Governance!.Notices, notice => notice.Id == revision.NoticeId && notice.Kind == "land_hearing");
        Assert.DoesNotContain(world.Towns[0].Governance!.Knowledge, receipt => receipt.NoticeId == revision.NoticeId);
        Assert.Empty(item.Responses);
        Assert.Empty(item.Rulings);
        Assert.Equal(permissions, Permissions(world.ExportState()));
        Assert.Equal(property, PrivateProperty(world.ExportState()));
        world.Validate();
        world.Pause();
        var paused = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(paused, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(paused), _ => new ActionCoverageRecorder(chooseIdle: true));
        restored.Validate();
        Assert.Equal(paused, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        Assert.Equal(permissions, Permissions(restored.ExportState()));
        Assert.Equal(property, PrivateProperty(restored.ExportState()));
    }

    [Fact]
    public async Task OtherPersonalProviderCannotFileTheOfferedHouseholdCase()
    {
        var provider = new HearingProvider(DecisionProviderKind.Jev) { HearingsEnabled = true };
        using var world = NewWorld(provider);
        var original = world.ExportState();
        var household = original.Society.Society.GetInhabitant(Filer).HouseholdId;
        provider.Plot = original.HouseholdLandUseRights!.Where(item => item.HouseholdId == household)
            .OrderBy(item => item.Id, StringComparer.Ordinal).First().Tiles.ToArray();
        var acceptedFileChoice = false;
        for (var step = 0; step < 4; step++)
        {
            var result = await world.AdvanceOneTickAsync();
            Assert.True(result.Advanced);
            acceptedFileChoice |= result.Decisions.Any(decision => decision.InhabitantId == Filer &&
                decision.Admission.Accepted && !decision.Admission.FellBack &&
                decision.Admission.Intention is { Provider: DecisionProviderKind.Jev } intention &&
                intention.CandidateId.Contains("|hearing_file|", StringComparison.Ordinal));
        }
        Assert.Contains(provider.Selected, item => item.Contains("|hearing_file|", StringComparison.Ordinal));
        Assert.True(acceptedFileChoice);
        Assert.Empty(world.Towns[0].LandHearings.Cases);
        Assert.Equal(Permissions(original), Permissions(world.ExportState()));
        Assert.Equal(PrivateProperty(original), PrivateProperty(world.ExportState()));
        world.Validate();
    }

    private static PrivateWorldRuntime NewWorld(HearingProvider provider)
    {
        using var generated = NormalPathWorld.CreateGenerated("government-personal-path", _ => provider);
        var state = generated.ExportState();
        var society = state.Society.Society;
        var oldDay = society.Config.TicksPerWorldDay;
        // Only time scale, comfort and proximity to the actual public board are arranged;
        // generated Town title, household allocations, buildings, goods and founder identities remain actual.
        return PrivateWorldRuntime.Restore(state with
        {
            Inhabitants = state.Inhabitants.Select(person => person with
            { Position = state.Towns![0].OriginSite!.Value, HungerBasisPoints = 8_000 }).ToArray(),
            WorldSystems = RegionalWeatherRules.Initialize(state.WorldSystems! with
            { Config = state.WorldSystems.Config with { TicksPerDay = Day }, RegionalWeather = null }, state.Map),
            Society = state.Society with
            {
                Society = society with
                {
                    Config = society.Config with { TicksPerWorldDay = Day },
                    Inhabitants = society.Inhabitants.Select(person => person with
                    { BirthTick = person.BirthTick / oldDay * Day, BirthLifeTick = person.BirthLifeTick is { } birth ? birth / oldDay * Day : null }).ToArray()
                }
            }
        }, _ => provider);
    }

    private static async Task UntilAsync(PrivateWorldRuntime world, Func<bool> complete, int limit, HearingProvider provider)
    {
        for (var step = 0; step < limit && !complete(); step++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.True(complete(), "Missing personal-path boundary; recent selected actions: " + string.Join(", ", provider.Selected.TakeLast(16)));
    }

    private static string Permissions(PrivateWorldRuntimeState state) => JsonSerializer.Serialize(
        state.HouseholdLandUseRights!.OrderBy(right => right.Id, StringComparer.Ordinal));

    private static string PrivateProperty(PrivateWorldRuntimeState state) => JsonSerializer.Serialize(new
    {
        Titles = state.TownLandTitles!.OrderBy(title => title.Id, StringComparer.Ordinal),
        Buildings = state.WorldSimulation!.Buildings.OrderBy(building => building.InstanceId, StringComparer.Ordinal),
        state.Fields,
        Membership = state.Society.Society.Inhabitants.OrderBy(person => person.Id, StringComparer.Ordinal)
            .Select(person => new { person.Id, person.HouseholdId }),
        Lots = state.Society.Society.Inventory.Lots.OrderBy(lot => lot.Id, StringComparer.Ordinal)
            .Select(lot => new { lot.Id, lot.ItemKind, lot.OwnerId, lot.Quantity, lot.ProvenanceLotId,
                lot.StorageBuildingId, lot.DeliveryBuildingId, lot.ContainerLotId, lot.GroundPosition, lot.CarrierId })
    });

    private static void AssertMalformedHearingSavesRefused(byte[] encoded)
    {
        foreach (var damage in new[] { "missing", "null", "notice", "judge", "source", "read" })
        {
            var document = JsonNode.Parse(encoded)!;
            var town = document["state"]!["towns"]![0]!;
            var hearing = town["landHearings"]!;
            var item = hearing["cases"]![0]!;
            switch (damage)
            {
                case "missing": town.AsObject().Remove("landHearings"); break;
                case "null": town["landHearings"] = null; break;
                case "notice": item["revisions"]![0]!["noticeId"] = "notice:invented"; break;
                case "judge": item["rulings"]![0]!["judge"]!["kind"] = "ordinary_mayor"; break;
                case "source": item["evidence"]![0]!["sourceVersion"] = "invented"; break;
                case "read":
                    foreach (var read in item["reads"]!.AsArray().Where(read => read!["agentId"]!.GetValue<string>() == Judge))
                        read!["evidenceIds"] = new JsonArray();
                    break;
            }
            Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Decode(Encoding.UTF8.GetBytes(document.ToJsonString())));
        }
    }

    private sealed class HearingProvider(DecisionProviderKind kind = DecisionProviderKind.LargeLanguageModel) : IDecisionProvider
    {
        public ConcurrentQueue<string> Selected { get; } = new();
        public DecisionProviderKind Kind => kind;
        public long ProviderEpoch => 1;
        public bool HearingsEnabled { get; set; }
        public GridPoint[] Plot { get; set; } = [];

        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var observation = request.Observation;
            var choices = observation.Candidates;
            var choice = choices.FirstOrDefault(candidate => candidate.Id.Contains("|read|", StringComparison.Ordinal));
            CognitionLandHearingChoice? hearing = null;
            IReadOnlyList<CognitionLandTile>? tiles = null;
            if (HearingsEnabled)
            {
                if (observation.InhabitantId == Judge)
                    choice ??= choices.FirstOrDefault(candidate => candidate.Id.Contains("|hearing_rule|", StringComparison.Ordinal) && candidate.Id.EndsWith("|confirm", StringComparison.Ordinal)) ??
                        choices.FirstOrDefault(candidate => candidate.Id.Contains("|hearing_inspect|", StringComparison.Ordinal));
                if (observation.InhabitantId == Filer)
                {
                    choice ??= choices.FirstOrDefault(candidate => candidate.Id.Contains("|hearing_answer|", StringComparison.Ordinal));
                    if (choice is null && !choices.Any(candidate => candidate.Id.Contains("|hearing_statement|", StringComparison.Ordinal) ||
                            candidate.Id.Contains("|hearing_inspect|", StringComparison.Ordinal) || candidate.Id.Contains("|hearing_reopen|", StringComparison.Ordinal)))
                        choice = choices.FirstOrDefault(candidate => candidate.Id.Contains("|hearing_file|", StringComparison.Ordinal));
                }
                if (observation.InhabitantId == Waiver)
                    choice ??= choices.FirstOrDefault(candidate => candidate.Id.Contains("|hearing_waive|", StringComparison.Ordinal));
            }
            if (Kind == DecisionProviderKind.LargeLanguageModel)
            {
                choice ??= choices.FirstOrDefault(candidate => candidate.Id.Contains("|government_yes|", StringComparison.Ordinal)) ??
                    choices.FirstOrDefault(candidate => candidate.Id.Contains("|mayor_vote|", StringComparison.Ordinal) && candidate.Id.EndsWith("|" + Judge, StringComparison.Ordinal));
                if (choice is null && observation.InhabitantId == Judge)
                    choice = choices.FirstOrDefault(candidate => candidate.Id.Contains("|mayor_register|land|", StringComparison.Ordinal)) ??
                        choices.FirstOrDefault(candidate => candidate.Id.Contains("|government_propose|council+mayor|", StringComparison.Ordinal));
            }
            choice ??= choices.Single(candidate => candidate.Id == "safe_idle");
            if (choice.Id.Contains("|hearing_file|", StringComparison.Ordinal))
            {
                tiles = Plot.Select(point => new CognitionLandTile(point.X, point.Y)).ToArray();
                hearing = new(Statement: "Please confirm our recorded household permission on this exact starter plot.", RequestedOutcome: "confirm");
            }
            else if (choice.Id.Contains("|hearing_answer|", StringComparison.Ordinal))
                hearing = new(Statement: "I answer for myself: our household asks to retain its recorded permission.");
            else if (choice.Id.Contains("|hearing_rule|", StringComparison.Ordinal))
            {
                var evidence = choice.Description.Split([' ', ';', ','], StringSplitOptions.RemoveEmptyEntries)
                    .Where(token => token.StartsWith("land-evidence:", StringComparison.Ordinal)).Select(token => token.Split('=')[0])
                    .Distinct(StringComparer.Ordinal).ToArray();
                hearing = new(Statement: "The inspected public record and both personal responses support confirming this permission.", EvidenceIds: evidence);
            }
            Selected.Enqueue(observation.InhabitantId + ":" + choice.Id);
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, observation.InhabitantId, Kind, ProviderEpoch,
                observation.RunEpoch, observation.DecisionGeneration, observation.ObservationDigest, choice.Id, 1,
                choices.ToDictionary(candidate => candidate.Id, candidate => candidate.Id == choice.Id ? 1d : 0d, StringComparer.Ordinal),
                CivicLandTiles: tiles, CivicLandHearing: hearing));
        }
    }
}

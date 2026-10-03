using System.Collections.Concurrent;
using System.Text.Json;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed class TownLandTransferRuntimeTests
{
    private const string Filer = "founder:00000000000000000000000000000003";
    private const string SourcePartner = "founder:00000000000000000000000000000004";
    private const string Beneficiary = "founder:00000000000000000000000000000001";
    private const string Newcomer = "agent:00000000000000000000000000000093";
    private const int Day = 40;
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task PersonalTransferPartitionsARealCouncilGrantAfterEveryAdultAgreesAndPreservesItsReceipt()
    {
        var provider = new TransferProvider();
        using var world = NewWorld(provider);
        Configure(provider, world);
        var property = PrivateProperty(world.ExportState());
        var grantPlot = FreePair(world);
        var request = world.RequestHouseholdLandUse("transfer-source-grant", Filer, world.Towns[0].Id, grantPlot, Day * 10);
        Assert.True(request.Applied, request.Failure);
        Assert.False(request.IsDisputed);
        provider.AcceptGrants = true;
        await UntilAsync(world, () => world.HouseholdLandUseRequests.Single(item => item.Id == "transfer-source-grant").Status == "granted", 80, provider);
        var grant = world.HouseholdLandUseRequests.Single(item => item.Id == "transfer-source-grant");
        var grantReceipt = JsonSerializer.Serialize(grant);
        var sourceRight = Assert.Single(world.HouseholdLandUseRights, right => right.GrantSource == HouseholdLandGrantRules.GrantSource(grant.Id));
        Assert.Equal(grantPlot, sourceRight.Tiles);
        Assert.Equal(Day * 10, sourceRight.AgreedEndTick);
        Assert.Equal(new[] { Filer, SourcePartner }, grant.GrantAdults);
        provider.Plot = [grantPlot[0]];
        provider.AllowPropose = true;
        world.SubmitInstruction(new("consider-transfer", "owner:test", Filer, OwnerInstructionKind.Suggestive,
            "Consider proposing a voluntary transfer of the specified part of your household's recorded permission."));
        await UntilAsync(world, () => world.Towns[0].LandHearings.Transfers.Count == 1, 20, provider);
        var proposed = Assert.Single(world.Towns[0].LandHearings.Transfers);
        Assert.Equal("pending", proposed.Status);
        Assert.Empty(proposed.Responses);
        Assert.Null(proposed.Receipt);
        Assert.Equal(provider.Plot, proposed.Tiles);
        Assert.Equal(sourceRight.Id, Assert.Single(proposed.RightVersions).Id);
        Assert.Contains(world.Towns[0].Governance!.Knowledge, receipt => receipt.AgentId == Filer && receipt.NoticeId == proposed.NoticeId);
        Assert.DoesNotContain(world.Towns[0].Governance!.Knowledge, receipt => receipt.AgentId == Beneficiary && receipt.NoticeId == proposed.NoticeId);
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False((await world.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        var replayProvider = provider.ReplayPolicy();
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(before), _ => replayProvider);
        for (var step = 0; step < 40 && world.Towns[0].LandHearings.Transfers[0].Status == "pending"; step++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
        }

        var completed = Assert.Single(world.Towns[0].LandHearings.Transfers);
        Assert.Equal("transferred", completed.Status);
        Assert.Equal(4, completed.Responses.Count);
        Assert.All(completed.Responses, response =>
        {
            Assert.Equal("accept", response.Kind);
            Assert.Contains(world.Towns[0].Governance!.Knowledge, receipt => receipt.NoticeId == completed.NoticeId &&
                receipt.AgentId == response.AgentId && receipt.LearnedTick <= response.Tick);
        });
        var receipt = Assert.IsType<TownLandTransferReceipt>(completed.Receipt);
        Assert.Equal(4, receipt.Parties.Sum(party => party.AdultIds.Count));
        var adjustment = Assert.Single(world.Towns[0].LandHearings.Adjustments);
        Assert.Equal("voluntary_transfer", adjustment.Kind);
        Assert.Equal(completed.Id, adjustment.TransferId);
        Assert.Equal(receipt.AdjustmentId, adjustment.Id);
        Assert.All(grantPlot, tile =>
        {
            var right = Assert.Single(world.HouseholdLandUseRights, right => right.Tiles.Contains(tile));
            Assert.Equal(tile == grantPlot[0] ? provider.TargetHouseholdId : provider.SourceHouseholdId, right.HouseholdId);
            Assert.Equal(sourceRight.GrantedTick, right.GrantedTick);
            Assert.Equal(sourceRight.AgreedEndTick, right.AgreedEndTick);
            Assert.Equal(sourceRight.GrantSource, right.GrantSource);
        });
        Assert.Equal(grantReceipt, JsonSerializer.Serialize(world.HouseholdLandUseRequests.Single(item => item.Id == grant.Id)));
        Assert.Empty(world.Towns[0].LandHearings.Cases);
        Assert.Empty(world.Towns[0].Government!.Offices);
        Assert.Equal(property, PrivateProperty(world.ExportState()));
        Assert.Contains(provider.Selected, item => item.Contains("|land_transfer_propose|", StringComparison.Ordinal));
        Assert.Contains(provider.Selected, item => item.Contains("|land_transfer_accept|", StringComparison.Ordinal));
        world.Validate(); replay.Validate();
        var final = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.Equal(final, PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(final), _ => new ActionCoverageRecorder(chooseIdle: true));
        restored.Validate();
        Assert.Equal(final, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        Assert.Equal(property, PrivateProperty(restored.ExportState()));
    }

    [Fact]
    public async Task ActualGrantOffersAnUndisputedSubsetWhileEveryCompleteSourcePlotContainsACompetingClaim()
    {
        var provider = new TransferProvider { AcceptGrants = true };
        using var world = NewWorld(provider);
        Configure(provider, world);
        var property = PrivateProperty(world.ExportState());
        var plot = FreePair(world);
        var application = world.RequestHouseholdLandUse("partial-transfer-source", Filer, world.Towns[0].Id, plot, Day * 10);
        Assert.True(application.Applied, application.Failure);
        await UntilAsync(world, () => world.HouseholdLandUseRequests.Single(request => request.Id == application.Request!.Id).Status == "granted", 80, provider);
        var grant = world.HouseholdLandUseRequests.Single(request => request.Id == application.Request!.Id);
        var grantReceipt = JsonSerializer.Serialize(grant);
        var source = Assert.Single(world.HouseholdLandUseRights, right => right.GrantSource == HouseholdLandGrantRules.GrantSource(grant.Id));
        var otherPermissions = JsonSerializer.Serialize(world.HouseholdLandUseRights.Where(right => right.GrantSource != source.GrantSource)
            .OrderBy(right => right.Id, StringComparer.Ordinal));

        var rival = world.RequestHouseholdLandUse("partial-transfer-rival", Beneficiary, world.Towns[0].Id, [plot[1]]);
        Assert.True(rival.Applied, rival.Failure);
        Assert.True(rival.IsDisputed);
        // Actual competing applications also cover the unrelated source starter plots. Without this
        // arrangement, the old full-plot gate could offer a generic proposal through another legal plot.
        var unrelated = world.HouseholdLandUseRights.Where(right => right.HouseholdId == provider.SourceHouseholdId && right.Id != source.Id).ToArray();
        for (var index = 0; index < unrelated.Length; index++)
        {
            var claim = world.RequestHouseholdLandUse("partial-transfer-other-" + index, Beneficiary, world.Towns[0].Id, unrelated[index].Tiles);
            Assert.True(claim.Applied, claim.Failure);
            Assert.True(claim.IsDisputed);
        }
        Assert.All(world.HouseholdLandUseRights.Where(right => right.HouseholdId == provider.SourceHouseholdId), right =>
            Assert.Contains(world.HouseholdLandUseRequests, request => request.Status == "pending" && request.HouseholdId == provider.TargetHouseholdId &&
                request.Tiles.Any(right.Tiles.Contains)));
        provider.Plot = [plot[0]];
        provider.AllowPropose = true;
        world.SubmitInstruction(new("consider-undisputed-subset", "owner:test", Filer, OwnerInstructionKind.Suggestive,
            "Consider a voluntary transfer of the undisputed part of your recorded permission."));
        await UntilAsync(world, () => world.Towns[0].LandHearings.Transfers.Count == 1, 20, provider);
        var offered = Assert.IsType<string>(provider.ProposalDescription);
        Assert.Contains("Eligible source coordinates by beneficiary", offered, StringComparison.Ordinal);
        Assert.Contains(FormattableString.Invariant($"({plot[0].X}, {plot[0].Y})"), offered, StringComparison.Ordinal);
        Assert.DoesNotContain(FormattableString.Invariant($"({plot[1].X}, {plot[1].Y})"), offered, StringComparison.Ordinal);
        await UntilAsync(world, () => world.Towns[0].LandHearings.Transfers[0].Status == "transferred", 40, provider);

        var transfer = Assert.Single(world.Towns[0].LandHearings.Transfers);
        Assert.Equal(new[] { plot[0] }, transfer.Tiles);
        Assert.Equal(4, transfer.Responses.Count);
        Assert.All(transfer.Responses, response => Assert.Equal("accept", response.Kind));
        var moved = Assert.Single(world.HouseholdLandUseRights, right => right.Tiles.Contains(plot[0]));
        var retained = Assert.Single(world.HouseholdLandUseRights, right => right.Tiles.Contains(plot[1]));
        Assert.Equal(provider.TargetHouseholdId, moved.HouseholdId);
        Assert.Equal(provider.SourceHouseholdId, retained.HouseholdId);
        Assert.Equal(source.AgreedEndTick, moved.AgreedEndTick);
        Assert.Equal(source.AgreedEndTick, retained.AgreedEndTick);
        Assert.Equal(source.GrantSource, moved.GrantSource);
        Assert.Equal(source.GrantSource, retained.GrantSource);
        Assert.Equal("pending", world.HouseholdLandUseRequests.Single(request => request.Id == "partial-transfer-rival").Status);
        Assert.DoesNotContain(world.HouseholdLandUseRequests.Single(request => request.Id == "partial-transfer-rival").HearingResolutions,
            resolution => resolution.Tiles.Contains(plot[1]));
        Assert.Equal(otherPermissions, JsonSerializer.Serialize(world.HouseholdLandUseRights.Where(right => right.GrantSource != source.GrantSource)
            .OrderBy(right => right.Id, StringComparer.Ordinal)));
        Assert.Equal(grantReceipt, JsonSerializer.Serialize(world.HouseholdLandUseRequests.Single(request => request.Id == grant.Id)));
        Assert.Equal(property, PrivateProperty(world.ExportState()));
        world.Validate();
        var saved = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved), _ => new ActionCoverageRecorder(chooseIdle: true));
        restored.Validate();
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }

    [Fact]
    public async Task HostedOldAdultConsentCannotSupplyTheConsentOfANewAdultWhoMustActuallyVisitReadAndAccept()
    {
        var provider = new TransferProvider { AllowPropose = true, HoldPartner = true, AllowNewcomer = false };
        using var world = NewWorld(provider);
        Configure(provider, world);
        try
        {
            for (var attempt = 0; attempt < 40 && (!provider.Started.Task.IsCompleted ||
                     world.Towns[0].LandHearings.Transfers.Count == 0 || world.Towns[0].LandHearings.Transfers[0].Responses.Count < 3); attempt++)
            {
                Assert.True((await world.AdvanceOneTickNonBlockingAsync().AsTask().WaitAsync(Deadline)).Advanced);
                await Task.Delay(10);
            }
            await provider.Started.Task.WaitAsync(Deadline);
            var pending = Assert.Single(world.Towns[0].LandHearings.Transfers);
            Assert.Equal("pending", pending.Status);
            Assert.Equal(3, pending.Responses.Count);
            Assert.DoesNotContain(pending.Responses, response => response.AgentId == SourcePartner);
            var permissions = Permissions(world.ExportState());
            Assert.Contains("|land_transfer_accept|", provider.HeldCandidateId!, StringComparison.Ordinal);
            var house = world.WorldSimulation.Buildings.First(building => building.HouseholdId == provider.TargetHouseholdId &&
                building.InstanceId.Contains("house-", StringComparison.Ordinal));
            Assert.Equal(provider.TargetHouseholdId, world.AddAgent(Newcomer, house.Position));
            var propertyAfterJoin = PrivateProperty(world.ExportState());
            provider.Release();
            for (var attempt = 0; attempt < 20 && world.Towns[0].LandHearings.Transfers[0].Responses.Count < 4; attempt++)
            {
                Assert.True((await world.AdvanceOneTickNonBlockingAsync().AsTask().WaitAsync(Deadline)).Advanced);
                await Task.Delay(10);
            }
            pending = Assert.Single(world.Towns[0].LandHearings.Transfers);
            Assert.Equal("pending", pending.Status);
            Assert.Equal(4, pending.Responses.Count);
            Assert.DoesNotContain(pending.Responses, response => response.AgentId == Newcomer);
            Assert.Null(pending.Receipt);
            Assert.Equal(permissions, Permissions(world.ExportState()));
            world.Validate();
            provider.AllowNewcomer = true;
            // This prompts a fresh real personal provider turn; it supplies no acceptance or permission.
            world.SubmitInstruction(new("consider-current-transfer", "owner:test", Newcomer, OwnerInstructionKind.Suggestive,
                "Visit the notice place, read the proposed permission transfer and consider your own response."));
            await UntilAsync(world, () => world.Towns[0].LandHearings.Transfers[0].Status == "transferred", 80, provider);
            var completed = Assert.Single(world.Towns[0].LandHearings.Transfers);
            var newConsent = Assert.Single(completed.Responses, response => response.AgentId == Newcomer);
            Assert.Equal(provider.TargetHouseholdId, newConsent.HouseholdId);
            Assert.Equal("accept", newConsent.Kind);
            Assert.Contains(world.Towns[0].Governance!.Knowledge, learned => learned.AgentId == Newcomer &&
                learned.NoticeId == completed.NoticeId && learned.LearnedTick <= newConsent.Tick);
            Assert.Contains(Newcomer, Assert.Single(completed.Receipt!.Parties, party => party.Kind == "beneficiary").AdultIds);
            Assert.Equal(propertyAfterJoin, PrivateProperty(world.ExportState()));
            world.Validate();
            world.Pause();
            var saved = PrivateWorldRuntimeCodec.Encode(world.ExportState());
            using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved), _ => new ActionCoverageRecorder(chooseIdle: true));
            restored.Validate();
            Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        }
        finally { provider.Release(); }
    }

    [Theory]
    [InlineData("decline", "rejected", "adult_declined")]
    [InlineData("withdraw", "withdrawn", "filer_withdrew")]
    [InlineData("dispute", "invalidated", "plot_disputed")]
    [InlineData("expiry", "invalidated", "permission_expired")]
    public async Task RefusedOrExpiredTransferRetainsCurrentPermissionAndPrivateProperty(string mode, string status, string reason)
    {
        var provider = new TransferProvider { AllowPropose = true, Mode = mode };
        using var source = NewWorld(provider);
        Configure(provider, source);
        var state = source.ExportState();
        if (mode == "expiry")
            state = state with
            {
                HouseholdLandUseRights = state.HouseholdLandUseRights!.Select(right =>
                right.Tiles.Any(provider.Plot.Contains) ? right with { AgreedEndTick = 4 } : right).ToArray()
            };
        using var world = PrivateWorldRuntime.Restore(state, _ => provider);
        var permissions = Permissions(world.ExportState());
        var property = PrivateProperty(world.ExportState());
        await UntilAsync(world, () => world.Towns[0].LandHearings.Transfers.Count == 1, 20, provider);
        Assert.Empty(world.Towns[0].LandHearings.Transfers[0].Responses);
        if (mode == "dispute")
        {
            var competing = world.RequestHouseholdLandUse("transfer-rival", Beneficiary, world.Towns[0].Id, provider.Plot);
            Assert.True(competing.Applied, competing.Failure);
            Assert.True(competing.IsDisputed);
        }
        await UntilAsync(world, () => world.Towns[0].LandHearings.Transfers[0].Status != "pending", mode == "withdraw" ? Day * 2 : 20, provider);
        var stopped = Assert.Single(world.Towns[0].LandHearings.Transfers);
        Assert.Equal(status, stopped.Status);
        Assert.Equal(reason, stopped.Reason);
        Assert.Null(stopped.Receipt);
        Assert.DoesNotContain(world.Towns[0].LandHearings.Adjustments, adjustment => adjustment.TransferId == stopped.Id);
        Assert.Equal(permissions, Permissions(world.ExportState()));
        Assert.Equal(property, PrivateProperty(world.ExportState()));
        world.Validate();
        var saved = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved), _ => new ActionCoverageRecorder(chooseIdle: true));
        restored.Validate();
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }

    [Fact]
    public async Task AdmittedJevProposalCannotCreatePermissionTransferAuthority()
    {
        var provider = new TransferProvider(DecisionProviderKind.Jev) { AllowPropose = true };
        using var world = NewWorld(provider);
        Configure(provider, world);
        var before = world.ExportState();
        var admitted = false;
        for (var tick = 0; tick < 4; tick++)
        {
            var step = await world.AdvanceOneTickAsync();
            admitted |= step.Decisions.Any(decision => decision.InhabitantId == Filer && decision.Admission.Accepted &&
                !decision.Admission.FellBack && decision.Admission.Intention is { Provider: DecisionProviderKind.Jev } intention &&
                intention.CandidateId.Contains("|land_transfer_propose|", StringComparison.Ordinal));
        }
        Assert.True(admitted);
        Assert.Empty(world.Towns[0].LandHearings.Transfers);
        Assert.Equal(Permissions(before), Permissions(world.ExportState()));
        Assert.Equal(PrivateProperty(before), PrivateProperty(world.ExportState()));
        world.Validate();
    }

    private static void Configure(TransferProvider provider, PrivateWorldRuntime world)
    {
        provider.SourceHouseholdId = world.Society.GetInhabitant(Filer).HouseholdId!;
        provider.TargetHouseholdId = world.Society.GetInhabitant(Beneficiary).HouseholdId!;
        provider.Plot = world.HouseholdLandUseRights.Where(right => right.HouseholdId == provider.SourceHouseholdId)
            .OrderBy(right => right.Id, StringComparer.Ordinal).First().Tiles.ToArray();
    }

    private static PrivateWorldRuntime NewWorld(TransferProvider provider)
    {
        using var generated = NormalPathWorld.CreateGenerated("government-personal-path", _ => provider);
        var state = generated.ExportState();
        var society = state.Society.Society;
        var oldDay = society.Config.TicksPerWorldDay;
        // Existing generated title, plots, founders, buildings and stock remain actual.
        // The same scoped public-board proximity/comfort and shorter day fixture as the civic runtime tests is used.
        return PrivateWorldRuntime.Restore(state with
        {
            Inhabitants = state.Inhabitants.Select(person => person with { Position = state.Towns![0].OriginSite!.Value, HungerBasisPoints = 8_000 }).ToArray(),
            WorldSystems = RegionalWeatherRules.Initialize(state.WorldSystems! with
            { Config = state.WorldSystems.Config with { TicksPerDay = Day, CalendarOffsetTicks = 0 }, RegionalWeather = null }, state.Map),
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

    private static GridPoint[] FreePair(PrivateWorldRuntime world)
    {
        var state = world.ExportState();
        var definitions = world.WorldContent.Buildings.ToDictionary(definition => definition.CanonicalId, StringComparer.Ordinal);
        var occupied = world.WorldSimulation.Buildings.SelectMany(building => WorldContentSimulationRules.Footprint(definitions[building.DefinitionId], building))
            .Concat(world.RoadTiles).Concat(state.Map.Resources.Select(resource => resource.Position)).Concat(state.Map.CampObjects.Select(item => item.Position)).ToHashSet();
        var free = world.TownLandTitles.SelectMany(title => title.Tiles).Where(tile => state.Map.IsBuildable(tile) && !occupied.Contains(tile) &&
            !world.HouseholdLandUseRights.Any(right => right.Tiles.Contains(tile))).ToHashSet();
        foreach (var tile in free.OrderBy(tile => tile.Y).ThenBy(tile => tile.X))
            foreach (var neighbour in new[] { new GridPoint(tile.X + 1, tile.Y), new GridPoint(tile.X, tile.Y + 1) }.Select(state.Map.WrapColumn))
                if (free.Contains(neighbour)) return TownLandRightsRules.OrderTiles([tile, neighbour]);
        throw new InvalidOperationException("The generated Town needs two actual adjacent free titled tiles for the grant fixture.");
    }

    private static async Task UntilAsync(PrivateWorldRuntime world, Func<bool> complete, int limit, TransferProvider provider)
    {
        for (var tick = 0; tick < limit && !complete(); tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.True(complete(), "Missing transfer boundary; recent choices: " + string.Join(", ", provider.Selected.TakeLast(16)));
    }

    private static string Permissions(PrivateWorldRuntimeState state) => JsonSerializer.Serialize(state.HouseholdLandUseRights!.OrderBy(right => right.Id, StringComparer.Ordinal));
    private static string PrivateProperty(PrivateWorldRuntimeState state) => JsonSerializer.Serialize(new
    {
        Titles = state.TownLandTitles!.OrderBy(title => title.Id, StringComparer.Ordinal),
        Buildings = state.WorldSimulation!.Buildings.OrderBy(building => building.InstanceId, StringComparer.Ordinal),
        state.Fields,
        Membership = state.Society.Society.Inhabitants.OrderBy(person => person.Id, StringComparer.Ordinal).Select(person => new { person.Id, person.HouseholdId }),
        TownMembers = state.Towns!.OrderBy(town => town.Id, StringComparer.Ordinal).Select(town => new { town.Id, town.ResidentIds }),
        Lots = state.Society.Society.Inventory.Lots.OrderBy(lot => lot.Id, StringComparer.Ordinal).Select(lot => new
        { lot.Id, lot.ItemKind, lot.OwnerId, lot.Quantity, lot.ProvenanceLotId, lot.StorageBuildingId, lot.DeliveryBuildingId, lot.ContainerLotId, lot.GroundPosition, lot.CarrierId })
    });

    private sealed class TransferProvider(DecisionProviderKind kind = DecisionProviderKind.LargeLanguageModel) : IDecisionProvider
    {
        private readonly TaskCompletionSource<CognitionDecisionResponse> release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private CognitionDecisionResponse? held;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ConcurrentQueue<string> Selected { get; } = new();
        public DecisionProviderKind Kind => kind;
        public long ProviderEpoch => 1;
        public bool AllowPropose { get; set; }
        public bool Proposed { get; set; }
        public bool AcceptGrants { get; set; }
        public bool HoldPartner { get; set; }
        public bool AllowNewcomer { get; set; } = true;
        public string? HeldCandidateId { get; private set; }
        public string? ProposalDescription { get; private set; }
        public string? Mode { get; set; }
        public string SourceHouseholdId { get; set; } = "";
        public string TargetHouseholdId { get; set; } = "";
        public GridPoint[] Plot { get; set; } = [];

        public TransferProvider ReplayPolicy() => new(kind)
        {
            AllowPropose = AllowPropose,
            Proposed = Proposed,
            AcceptGrants = AcceptGrants,
            Mode = Mode,
            SourceHouseholdId = SourceHouseholdId,
            TargetHouseholdId = TargetHouseholdId,
            Plot = Plot.ToArray()
        };

        public void Release() { if (held is not null) release.TrySetResult(held); }

        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var observation = request.Observation;
            var candidates = observation.Candidates;
            CognitionCandidate? Pick(string action) => candidates.FirstOrDefault(candidate => candidate.Id.Contains("|" + action + "|", StringComparison.Ordinal));
            var proposal = AllowPropose && !Proposed && observation.InhabitantId == Filer ? Pick("land_transfer_propose") : null;
            var choice = Kind == DecisionProviderKind.Jev ? proposal : null;
            choice ??= Pick("read") ?? Pick("land_transfer_read") ?? Pick("visit");
            if (choice is null && AcceptGrants)
                choice = Pick("yes") ?? (observation.InhabitantId is Filer or SourcePartner ? Pick("accept_land_use") : null);
            if (choice is null && Mode == "withdraw" && observation.InhabitantId == Filer) choice = Pick("land_transfer_withdraw");
            if (choice is null && Mode == "decline" && observation.InhabitantId == SourcePartner) choice = Pick("land_transfer_decline");
            if (choice is null && Mode is null && (observation.InhabitantId != Newcomer || AllowNewcomer)) choice = Pick("land_transfer_accept");
            choice ??= proposal ?? candidates.Single(candidate => candidate.Id == "safe_idle");
            var proposing = choice.Id.Contains("|land_transfer_propose|", StringComparison.Ordinal);
            if (proposing)
            {
                Proposed = true;
                ProposalDescription = choice.Description;
            }
            Selected.Enqueue(observation.InhabitantId + ":" + choice.Id);
            var response = new CognitionDecisionResponse(request.RequestId, observation.InhabitantId, Kind, ProviderEpoch,
                observation.RunEpoch, observation.DecisionGeneration, observation.ObservationDigest, choice.Id, 1,
                candidates.ToDictionary(candidate => candidate.Id, candidate => candidate.Id == choice.Id ? 1d : 0d, StringComparer.Ordinal),
                CivicLandTiles: proposing ? Plot.Select(tile => new CognitionLandTile(tile.X, tile.Y)).ToArray() : null,
                CivicLandHearing: proposing ? new(HouseholdId: TargetHouseholdId) : null);
            if (HoldPartner && observation.InhabitantId == SourcePartner && held is null && choice.Id.Contains("|land_transfer_accept|", StringComparison.Ordinal))
            {
                held = response;
                HeldCandidateId = choice.Id;
                Started.TrySetResult();
                return new(release.Task);
            }
            return ValueTask.FromResult(response);
        }
    }
}

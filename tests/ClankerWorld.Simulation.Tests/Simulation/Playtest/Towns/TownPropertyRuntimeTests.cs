using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Society;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class TownLandHearingRuntimeTests
{
    private const string PropertyFiler = "founder:00000000000000000000000000000002";
    private const string PropertyHouse = "first-town-house-b";
    private static readonly JsonSerializerOptions PropertyJsonOptions = new(JsonSerializerDefaults.Web);

    // Only the already tested election and opening are shared: every test runs its own
    // actual personal choices, inventory changes and serialized world boundary.
    private static readonly Lazy<Task<byte[]>> PendingPropertyCase = new(async () =>
    {
        var election = new HearingProvider();
        using var elected = NewWorld(election);
        await UntilAsync(elected, () => elected.Towns[0].Government!.Offices.Any(), Day * 3, election);
        var state = elected.ExportState();
        var household = state.Society.Society.GetInhabitant(Filer).HouseholdId!;
        var stock = state.Society.Society.Inventory.Lots.First(lot => lot.OwnerId == household &&
            lot.StorageBuildingId == PropertyHouse && lot.ContainerLotId is null && lot.Quantity > 1);
        var inventory = InventoryFixture.Transfer(state.Society.Society.Inventory, "personal-held", household, Filer,
            stock.Id, 1, "personal gift retained at the House", destinationStorageBuildingId: PropertyHouse);
        using var stocked = PrivateWorldRuntime.Restore(state with
        { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } }, _ => election);
        Assert.True(stocked.DisplaceAdult(Filer));
        Assert.True(stocked.DisplaceAdult(Waiver));
        var provider = new PropertyProvider { Request = true };
        using var world = PrivateWorldRuntime.Restore(stocked.ExportState(), _ => provider);
        await PropertyUntil(world, () => world.Towns[0].LandHearings.Cases.Count > 0, 20);
        var item = Assert.Single(world.Towns[0].LandHearings.Cases);
        Assert.Equal("property", item.Kind);
        Assert.Equal(new[] { Filer, Waiver }, item.Property!.Snapshots[0].LivingFormerMemberIds);
        Assert.Equal(item.Revisions[0].PublishedTick + Day, item.Revisions[0].DeadlineTick);
        Assert.Empty(item.Property.Consents);
        Assert.Null(item.Property.Transfer);
        world.Validate();
        return PrivateWorldRuntimeCodec.Encode(world.ExportState());
    });

    [Fact]
    public async Task AnEmptyHouseholdsPaidHouseIsOfferedForATownPropertyCaseAndDepartureDoesNotTransferIt()
    {
        var provider = new HearingProvider { SeekMayor = false };
        using var world = NewWorld(provider);
        var source = world.ExportState().Society.Society.GetInhabitant(Filer).HouseholdId!;
        var house = Assert.Single(world.ExportState().WorldSimulation!.Buildings,
            building => building.HouseholdId == source && world.ExportState().WorldContent!.Buildings.Any(definition =>
                definition.CanonicalId == building.DefinitionId && definition.Tags.Contains("house", StringComparer.Ordinal)));
        Assert.True(world.DisplaceAdult(Filer));
        Assert.True(world.DisplaceAdult(Waiver));
        Assert.Empty(world.ExportState().Society.Society.Households.Single(household => household.Id == source).MemberIds);
        Assert.Equal(source, world.ExportState().WorldSimulation!.Buildings.Single(building => building.InstanceId == house.InstanceId).HouseholdId);
        for (var step = 0; step < 5; step++)
        {
            Prompt(world, Judge, "Consider a Town property hearing for the empty household's House.");
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        }
        Assert.Contains(provider.Offered[Judge], choice => choice.Contains("|hearing_property_request|", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PersonalOwnerAgreementsAndAnIndependentRulingTransferActualStockAndReplayExactly()
    {
        var save = await PendingPropertyCase.Value;
        var provider = new PropertyProvider { Agree = true, Rule = true };
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(save), _ => provider);
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(save), _ => new PropertyProvider { Agree = true, Rule = true });
        var before = world.ExportState();
        Assert.False((await world.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(save, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        for (var step = 0; step < 20 && world.Towns[0].LandHearings.Cases[0].Status == "pending"; step++)
        {
            PropertyPromptAll(world);
            PropertyPromptAll(replay);
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
        }
        var item = Assert.Single(world.Towns[0].LandHearings.Cases);
        Assert.Equal("settled", item.Status);
        var transfer = Assert.IsType<TownPropertyTransfer>(item.Property!.Transfer);
        var ruling = Assert.Single(item.Rulings);
        Assert.Equal(("reclaim", Judge), (ruling.Outcome.Kind, ruling.Judge.AgentId));
        Assert.All(new[] { Filer, Waiver }, actor => Assert.Contains(item.Property.Consents, consent => consent.AgentId == actor && consent.Agreed));
        Assert.Equal(PropertyHouse, transfer.ResultBuilding.InstanceId);
        Assert.Null(transfer.ResultBuilding.HouseholdId);
        Assert.NotEmpty(transfer.PriorLots);
        Assert.Equal(transfer.PriorLots.Sum(lot => lot.Quantity), transfer.ResultLots.Sum(lot => lot.Quantity));
        Assert.All(transfer.ResultLots, lot => Assert.Equal(world.Towns[0].Id, lot.OwnerId));
        Assert.Equal(before.Towns![0].ResidentIds, world.Towns[0].ResidentIds);
        Assert.Equal(before.Fields, world.ExportState().Fields);
        Assert.All(before.Society.Society.Inventory.Lots.Where(lot => lot.OwnerId == Filer || lot.OwnerId == Waiver), personal =>
            Assert.Equal(personal.OwnerId, world.ExportState().Society.Society.Inventory.GetLot(personal.Id).OwnerId));
        Assert.DoesNotContain(world.HouseholdLandUseRights, right => right.HouseholdId == transfer.PriorBuilding.HouseholdId &&
            right.Tiles.Any(item.Revisions[0].Tiles.Contains));
        world.Validate();
        replay.Validate();
        var final = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.Equal(final, PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        using var reloaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(final), _ => new PropertyProvider());
        Assert.Equal(final, PrivateWorldRuntimeCodec.Encode(reloaded.ExportState()));
        AssertDamagedPropertyFilesRefused(world.ExportState());
        var personal = Assert.Single(before.Society.Society.Inventory.Lots, lot => lot.OwnerId == Filer &&
            lot.StorageBuildingId == PropertyHouse && lot.Id.EndsWith("#transfer:personal-held", StringComparison.Ordinal));
        Assert.Equal(Filer, world.ExportState().Society.Society.Inventory.GetLot(personal.Id).OwnerId);
        using var collector = PrivateWorldRuntime.Restore(world.ExportState(), _ => new PropertyProvider { Collect = personal.Id });
        await PropertyUntil(collector, () => PersonalEquipmentRules.IsPhysicallyCarried(collector.ExportState().Society.Society.Inventory,
            collector.ExportState().Society.Society.Inventory.GetLot(personal.Id), Filer), 16);
        Assert.Equal(Filer, collector.ExportState().Society.Society.Inventory.GetLot(personal.Id).OwnerId);
        collector.Validate();
    }

    [Fact]
    public async Task AResponseWaiverAndTheFullNoticeDeadlineNeverSupplyPropertyConsent()
    {
        var provider = new PropertyProvider { Waive = true };
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(await PendingPropertyCase.Value), _ => provider);
        await PropertyUntil(world, () => world.Towns[0].LandHearings.Cases[0].Reads.Any(read => read.AgentId == Judge) &&
            world.Towns[0].LandHearings.Cases[0].Responses.Any(response => response.AgentId == Waiver && response.Kind == "waive"), 12);
        var item = world.Towns[0].LandHearings.Cases[0];
        Assert.Empty(item.Property!.Consents);
        Assert.Null(item.Property.Transfer);
        // Invoke the adjudication boundary at the actual notice's deadline: the real case,
        // actual elected mandate and read evidence are retained; elapsed time supplies no agreement.
        Assert.Throws<InvalidOperationException>(() => TownLandHearingRules.Rule(world.Towns[0].LandHearings, world.ExportState().Map,
            item.Id, item.Revisions[0].Number, item.Judge!, item.Revisions[0].DeadlineTick,
            TownPropertyRules.Outcome(item.Property.Request), item.Evidence.Select(evidence => evidence.Id).ToArray(), [],
            "The notice period ended; inspect the property consent requirement.", world.HouseholdLandUseRights,
            item.Revisions[0].Parties, true));
        Assert.Equal(item.Property.Request.SourceHouseholdId, world.ExportState().WorldSimulation!.Buildings.Single(building => building.InstanceId == PropertyHouse).HouseholdId);
        world.Validate();
    }

    [Fact]
    public async Task ALivingFormerMemberCanRefuseAndTheJudgeRejectsWithoutTakingProperty()
    {
        var provider = new PropertyProvider { Agree = true, Refuser = Waiver, Reject = true };
        var state = PrivateWorldRuntimeCodec.Decode(await PendingPropertyCase.Value);
        var moved = SocietyFixture.CreateHousehold(state.Society.Society, "household:former-member", "Another household", [Waiver]);
        using var world = PrivateWorldRuntime.Restore(state with { Society = state.Society with { Society = moved.Checkpoint } }, _ => provider);
        await PropertyUntil(world, () => world.Towns[0].LandHearings.Cases[0].Status == "settled", 20);
        var item = Assert.Single(world.Towns[0].LandHearings.Cases);
        Assert.Contains(item.Property!.Consents, consent => consent.AgentId == Waiver && !consent.Agreed);
        Assert.Equal("reject", Assert.Single(item.Rulings).Outcome.Kind);
        Assert.Null(item.Property.Transfer);
        Assert.Empty(world.Towns[0].LandHearings.Adjustments);
        Assert.Equal(item.Property.Request.SourceHouseholdId, world.ExportState().WorldSimulation!.Buildings.Single(building => building.InstanceId == PropertyHouse).HouseholdId);
        var saved = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var reloaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved), _ => new PropertyProvider());
        reloaded.Validate();
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(reloaded.ExportState()));
    }

    [Fact]
    public async Task ChangedSharedStockPublishesAFreshNoticeAndRequiresFreshPropertyConsent()
    {
        using var agreed = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(await PendingPropertyCase.Value),
            _ => new PropertyProvider { Agree = true });
        await PropertyUntil(agreed, () => agreed.Towns[0].LandHearings.Cases[0].Property!.Consents.Count == 2, 15);
        var state = agreed.ExportState();
        var item = state.Towns![0].LandHearings.Cases[0];
        var lot = item.Property!.Snapshots[0].SharedLots.First(stock => stock.ContainerLotId is null && stock.Quantity > 1);
        var inventory = InventoryFixture.Transfer(state.Society.Society.Inventory, "changed-stock", lot.OwnerId, Filer,
            lot.Id, 1, "personal transfer before adjudication", destinationStorageBuildingId: lot.StorageBuildingId,
            destinationGroundPosition: lot.GroundPosition);
        using var world = PrivateWorldRuntime.Restore(state with
        { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } }, _ => new PropertyProvider());
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var current = world.Towns[0].LandHearings.Cases[0];
        Assert.Equal(2, current.Revisions.Count);
        Assert.Equal(current.Revisions[1].PublishedTick + Day, current.Revisions[1].DeadlineTick);
        Assert.Equal(2, current.Property!.Snapshots.Count);
        Assert.All(current.Property.Consents, consent => Assert.Equal(1, consent.Revision));
        Assert.Null(current.Property.Transfer);
        world.Validate();
        var saved = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved), _ => new PropertyProvider());
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }

    [Fact]
    public async Task TheTownCanGrantItsRecoveredHouseAndGoodsToAnAcceptingHousehold()
    {
        var provider = new PropertyProvider { Agree = true, Rule = true };
        using var reclaimed = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(await PendingPropertyCase.Value), _ => provider);
        await PropertyUntil(reclaimed, () => reclaimed.Towns[0].LandHearings.Cases[0].Status == "settled", 20);
        var state = reclaimed.ExportState();
        // A legitimate receiving household is formed with the same Society operation used by
        // household_found. No House, stock, hearing, vote, permission or Town membership is added.
        const string recipient = "household:property-recipient";
        var society = SocietyFixture.CreateHousehold(state.Society.Society, recipient, "Receiving household", [Filer]);
        var onward = new PropertyProvider { Request = true, Target = recipient, Agree = true, Rule = true };
        using var world = PrivateWorldRuntime.Restore(state with { Society = state.Society with { Society = society.Checkpoint } }, _ => onward);
        await PropertyUntil(world, () => world.Towns[0].LandHearings.Cases.Count == 2 && world.Towns[0].LandHearings.Cases[1].Status == "settled", 25);
        var item = world.Towns[0].LandHearings.Cases[1];
        Assert.Equal("grant", Assert.Single(item.Rulings).Outcome.Kind);
        var transfer = Assert.IsType<TownPropertyTransfer>(item.Property!.Transfer);
        Assert.Equal(recipient, transfer.ResultBuilding.HouseholdId);
        Assert.All(transfer.ResultLots, lot => Assert.Equal(recipient, lot.OwnerId));
        Assert.Contains(item.Property.Consents, consent => consent.AgentId == Filer && consent.Agreed);
        Assert.Contains(world.HouseholdLandUseRights, right => right.HouseholdId == recipient && right.Tiles.SequenceEqual(item.Revisions[0].Tiles));
        Assert.Equal(state.Towns![0].ResidentIds, world.Towns[0].ResidentIds);
        Assert.Equal(state.TownLandTitles, world.ExportState().TownLandTitles);
        world.Validate();
        var saved = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved), _ => new PropertyProvider());
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }

    private static void AssertDamagedPropertyFilesRefused(PrivateWorldRuntimeState state)
    {
        var town = state.Towns![0];
        var item = town.LandHearings.Cases[0];
        var property = item.Property!;
        TownPropertyCase?[] damages = [null, property with { Consents = [] }, property with { Transfer = null },
            property with { Snapshots = [property.Snapshots[0] with { LivingFormerMemberIds = [Filer] }] },
            property with { Transfer = property.Transfer! with { ResultBuilding = property.Transfer.ResultBuilding with { HouseholdId = property.Request.SourceHouseholdId } } },
            property with { Transfer = property.Transfer! with { ResultLots = property.Transfer.ResultLots.Select(lot => lot with { Quantity = lot.Quantity + 1 }).ToArray() } }];
        foreach (var damage in damages)
        {
            var damaged = state with { Towns = [town with { LandHearings = town.LandHearings with { Cases = [item with { Property = damage }] } }] };
            Assert.Throws<InvalidDataException>(() =>
            {
                using var invalid = PrivateWorldRuntime.Restore(damaged, _ => new PropertyProvider());
            });
            var document = JsonNode.Parse(PrivateWorldRuntimeCodec.Encode(state))!;
            document["state"]!["towns"]![0]!["landHearings"]!["cases"]![0]!["property"] =
                damage is null ? null : JsonSerializer.SerializeToNode(damage, PropertyJsonOptions);
            Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Decode(System.Text.Encoding.UTF8.GetBytes(document.ToJsonString())));
        }
    }

    private static void PropertyPromptAll(PrivateWorldRuntime world)
    {
        foreach (var agent in world.Towns[0].ResidentIds) Prompt(world, agent, "Consider the public property case and make your own choice.");
    }

    private static async Task PropertyUntil(PrivateWorldRuntime world, Func<bool> done, int limit)
    {
        for (var step = 0; step < limit && !done(); step++)
        {
            PropertyPromptAll(world);
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        }
        Assert.True(done(), "The native property-case boundary was not reached: " + JsonSerializer.Serialize(world.ExportState().Events.TakeLast(8)));
    }

    private sealed class PropertyProvider : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
        public long ProviderEpoch => 1;
        public bool Request { get; init; }
        public string? Target { get; init; }
        public bool? Agree { get; init; }
        public string? Refuser { get; init; }
        public bool Rule { get; init; }
        public bool Reject { get; init; }
        public bool Waive { get; init; }
        public string? Collect { get; init; }

        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var observation = request.Observation;
            var choices = observation.Candidates;
            CognitionCandidate? Find(string action) => choices.FirstOrDefault(candidate => candidate.Id.Contains("|" + action + "|", StringComparison.Ordinal));
            var selected = Find("read") ?? Find("hearing_inspect") ?? Find("yes");
            if (Collect is { } lot && observation.InhabitantId == Filer)
                selected ??= choices.FirstOrDefault(candidate => candidate.Id == "household_collect:" + lot);
            if (Request && observation.InhabitantId == PropertyFiler)
                selected ??= choices.FirstOrDefault(candidate => candidate.Id.Contains("|hearing_property_request|" + PropertyHouse + "|", StringComparison.Ordinal) &&
                    candidate.Id.EndsWith("|" + (Target ?? "town"), StringComparison.Ordinal));
            if (Agree is { } agree)
                selected ??= Find("hearing_property_" + (observation.InhabitantId == Refuser || !agree ? "refuse" : "accept"));
            if (Waive) selected ??= Find("hearing_waive");
            selected ??= Find("hearing_answer");
            if (observation.InhabitantId == Judge && (Rule || Reject))
                selected ??= choices.FirstOrDefault(candidate => candidate.Id.Contains("|hearing_rule|", StringComparison.Ordinal) &&
                    (candidate.Id.EndsWith("|reclaim", StringComparison.Ordinal) || candidate.Id.EndsWith("|grant", StringComparison.Ordinal) || Reject &&
                        candidate.Id.EndsWith("|reject", StringComparison.Ordinal)));
            selected ??= choices.Single(candidate => candidate.Id == "safe_idle");
            var evidence = selected.Description.Split([' ', ';', ','], StringSplitOptions.RemoveEmptyEntries)
                .Where(token => token.StartsWith("land-evidence:", StringComparison.Ordinal)).Select(token => token.Split('=')[0]).Distinct(StringComparer.Ordinal).ToArray();
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, observation.InhabitantId, Kind, ProviderEpoch,
                observation.RunEpoch, observation.DecisionGeneration, observation.ObservationDigest, selected.Id, 1,
                choices.ToDictionary(candidate => candidate.Id, candidate => candidate.Id == selected.Id ? 1d : 0d, StringComparer.Ordinal),
                CivicLandHearing: new(Statement: "Please decide this exact noticed property request using its ownership records and personal agreements.", EvidenceIds: evidence)));
        }
    }
}

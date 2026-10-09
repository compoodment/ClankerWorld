using System.Collections.Concurrent;
using System.Text;
using System.Text.Json.Nodes;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Tests;

public sealed class TownDefaultEstateLawTests
{
    private const string Town = "town:first";
    private const string Author = "founder:00000000000000000000000000000001";
    private static readonly string[] Adults = ["alice", "bob", "cara"];

    [Fact]
    public void MajorityAdoptionAmendmentAndRepealKeepTheRuleAtTheDeathTick()
    {
        var civic = TownLawRules.ProposeEstateDefault(TownGovernanceState.Create(Adults), TownGovernmentState.Create(),
            Town, "alice", 25, Adults, 2, 10);
        Assert.Null(TownEstateDefaultRules.AtDeath(civic.Government, 3));
        Assert.Throws<InvalidOperationException>(() => TownLawRules.ProposeEstateDefault(civic.Council, civic.Government,
            Town, "bob", 50, Adults, 3, 10));
        civic = Pass(civic, 4);
        Assert.Null(TownEstateDefaultRules.AtDeath(civic.Government, 3));
        Assert.Null(TownEstateDefaultRules.AtDeath(civic.Government, 4));
        Assert.Equal(25, TownEstateDefaultRules.AtDeath(civic.Government, 5)!.Value.Version.EstateDefault!.TownSharePercent);
        var law = Assert.Single(civic.Government.Laws);
        Assert.Throws<InvalidOperationException>(() => TownLawRules.ProposeAmendment(civic.Council, civic.Government,
            Town, "alice", law.Id, "Default estate: Take everything.", Adults, 6, 10));
        civic = Pass(TownLawRules.ProposeEstateDefault(civic.Council, civic.Government, Town, "alice", 75, Adults, 6, 10), 7);
        civic = Pass(TownLawRules.ProposeRepeal(civic.Council, civic.Government, Town, "alice", law.Id, Adults, 9, 10), 10);
        Assert.Equal(25, TownEstateDefaultRules.AtDeath(civic.Government, 5)!.Value.Version.EstateDefault!.TownSharePercent);
        Assert.Equal(25, TownEstateDefaultRules.AtDeath(civic.Government, 7)!.Value.Version.EstateDefault!.TownSharePercent);
        Assert.Equal(75, TownEstateDefaultRules.AtDeath(civic.Government, 8)!.Value.Version.EstateDefault!.TownSharePercent);
        Assert.Equal(75, TownEstateDefaultRules.AtDeath(civic.Government, 10)!.Value.Version.EstateDefault!.TownSharePercent);
        Assert.Null(TownEstateDefaultRules.AtDeath(civic.Government, 11));
    }

    [Theory]
    [InlineData(false, 50, 2, 3)]
    [InlineData(false, 100, 5, 0)]
    [InlineData(true, 100, 0, 5)]
    public void FullEscrowThenDefaultShareOrValidWillPreservesGoods(bool acceptedWill, int share, int townWood, int heirWood)
    {
        var checkpoint = EstateFixture(household: true);
        var estate = Assert.Single(checkpoint.Estates);
        if (acceptedWill)
            checkpoint = SocietyFixture.ResolveWill(SocietyFixture.MarkWillStarted(checkpoint, estate.Id).Checkpoint, estate.Id,
                new SocietyWillDirective(["bob"], "equal"), "accepted", null).Checkpoint;
        var stores = new[] { new SocietyTownStore(Town, "warehouse", 20, new HashSet<string>(["food"])) };
        var divisions = new[] { new SocietyDefaultEstateDivision(estate.Id, Town, share) };
        var before = SocietyFixture.AdvanceTo(checkpoint, estate.ExpiryTick - 1, stores, divisions).Checkpoint;
        Assert.False(before.GetEstate(estate.Id).Settled);
        Assert.Equal(5, before.Inventory.Lots.Where(lot => lot.OwnerId == estate.Id && lot.ItemKind == "wood").Sum(lot => lot.Quantity));
        var settled = SocietyFixture.AdvanceTo(before, estate.ExpiryTick, stores, divisions).Checkpoint;
        Assert.True(settled.GetEstate(estate.Id).Settled);
        Assert.Equal(townWood, settled.Inventory.Lots.Where(lot => lot.OwnerId == Town && lot.ItemKind == "wood").Sum(lot => lot.Quantity));
        Assert.Equal(heirWood, settled.Inventory.Lots.Where(lot => lot.OwnerId == "bob" && lot.ItemKind == "wood").Sum(lot => lot.Quantity));
        Assert.Equal(2, settled.Inventory.Lots.Where(lot => lot.OwnerId == "bob" && lot.ItemKind == "food").Sum(lot => lot.Quantity));
        Assert.Equal(7, settled.Inventory.Lots.Sum(lot => lot.Quantity));
        var repeated = SocietyFixture.AdvanceTo(settled, estate.ExpiryTick + 1, stores, divisions).Checkpoint;
        Assert.Equal(settled.Inventory.Lots, repeated.Inventory.Lots);
        Assert.Single(repeated.Inventory.Events, entry => entry.Kind == "estate_settled");
    }

    [Fact]
    public void NoLivingHouseholdUsesActualTownAndKeepsOverflowSeparateFromWarehouseAndBookIdentity()
    {
        var checkpoint = EstateFixture(household: false, book: true);
        var estate = Assert.Single(checkpoint.Estates);
        var settled = SocietyFixture.AdvanceTo(checkpoint, estate.ExpiryTick,
            [new SocietyTownStore(Town, "warehouse", 2, new HashSet<string>(["food"]))],
            [new SocietyDefaultEstateDivision(estate.Id, Town, 0)]).Checkpoint;
        Assert.All(settled.Inventory.Lots, lot => Assert.Equal(Town, lot.OwnerId));
        Assert.Equal(2, settled.Inventory.Lots.Where(lot => lot.StorageBuildingId == "warehouse").Sum(lot => lot.Quantity));
        Assert.Equal(6, settled.Inventory.Lots.Where(lot => lot.StorageBuildingId is null).Sum(lot => lot.Quantity));
        Assert.Equal(settled.Inventory.Lots.Count, settled.Inventory.Lots.Select(lot => lot.Id).Distinct().Count());
        var book = Assert.Single(settled.Inventory.Lots, lot => lot.ItemKind == "book");
        Assert.Equal("book-lot", book.Id);
        Assert.Equal(1, book.Quantity);
        Assert.Equal(8, settled.Inventory.Lots.Sum(lot => lot.Quantity));
    }

    [Theory]
    [InlineData(50, 20, "bob")]
    [InlineData(100, 4, "bob")]
    [InlineData(100, 5, Town)]
    public void DefaultLawNeverSplitsAVesselOrExceedsFamilyCapacity(int share, int room, string owner)
    {
        var checkpoint = EstateFixture(household: true);
        // Replace the frozen loose stock with a complete physical jug family
        // before a fresh death, keeping the native container admission rules.
        var config = checkpoint.Config;
        checkpoint = SocietyFixture.CreateGenesis("default-vessel", Adults.Select(id =>
            SocietyFixture.CreateFounder(id, id, "model:" + id, config: config)).ToArray(), config: config);
        checkpoint = SocietyFixture.CreateHousehold(checkpoint, "home", "The Home", ["alice", "bob"]).Checkpoint;
        var inventory = InventoryFixture.AddLot(checkpoint.Inventory, "jug", InventoryContainerRules.WaterJug, "alice", 1);
        inventory = InventoryFixture.AddLot(inventory, "water", InventoryContainerRules.FreshWater, "alice", 4, containerLotId: "jug");
        checkpoint = SocietyFixture.Kill(checkpoint with { Inventory = inventory }, "alice", SocietyDeathCause.Hazard).Checkpoint;
        var estate = Assert.Single(checkpoint.Estates);
        var settled = SocietyFixture.AdvanceTo(checkpoint, estate.ExpiryTick,
            [new SocietyTownStore(Town, "warehouse", room, new HashSet<string>())],
            [new SocietyDefaultEstateDivision(estate.Id, Town, share)]).Checkpoint;
        Assert.Equal(["jug", "water"], settled.Inventory.Lots.Select(lot => lot.Id));
        Assert.All(settled.Inventory.Lots, lot => Assert.Equal(owner, lot.OwnerId));
        Assert.Equal("jug", settled.Inventory.GetLot("water").ContainerLotId);
        Assert.Equal(5, settled.Inventory.Lots.Sum(lot => lot.Quantity));
        Assert.All(settled.Inventory.Lots, lot => Assert.Equal(owner == Town ? "warehouse" : null, lot.StorageBuildingId));
    }

    [Fact]
    public async Task PersonalCouncilTurnsAdoptTypedRuleAndReloadReplayOrRefusedTickKeepIt()
    {
        var providers = new ConcurrentDictionary<string, EstateLawProvider>(StringComparer.Ordinal);
        IDecisionProvider Provider(string id) => providers.GetOrAdd(id, key => new EstateLawProvider(key));
        using var generated = NormalPathWorld.CreateGenerated("town-law-site", Provider);
        var initial = generated.ExportState();
        var origin = initial.Towns![0].OriginSite!.Value;
        var noticeTile = initial.Map.Tiles.Select(tile => tile.Position).Where(point => initial.Map.IsBuildable(point) &&
                !initial.Map.CampObjects.Any(item => item.Position == point))
            .OrderBy(point => initial.Map.FootDistance(point, origin)).First();
        using var world = PrivateWorldRuntime.Restore(initial with
        {
            Inhabitants = initial.Inhabitants.Select(person => person with { Position = noticeTile, HungerBasisPoints = 8_000 }).ToArray(),
        }, Provider);
        for (var tick = 0; tick < 100 && world.Towns[0].Government!.Laws.Count == 0; tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var law = Assert.Single(world.Towns[0].Government!.Laws);
        Assert.Equal(50, Assert.Single(law.Versions).EstateDefault!.TownSharePercent);
        Assert.Contains(providers[Author].Seen, id => id == "civic|town:first|estate_default|adopt|50");
        var validation = MapAcceptance.Validate(world.ExportState().Map, allowEmptyCamp: world.ExportState().FounderSetup is not null);
        Assert.True(validation.IsValid, validation.Failure);
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False((await world.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        world.Pause();
        var encoded = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(encoded), Provider);
        Assert.Equal(encoded, PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        world.Resume();
        replay.Resume();
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        replay.Validate();
    }

    [Theory]
    [InlineData(false, 50, 2, 3)]
    [InlineData(true, 0, 5, 0)]
    public async Task NaturalDeathUsesRecordedTownAndHistoricalDefaultLawAcrossSettlementReload(bool allDie, int share, int townWood, int heirWood)
    {
        var willProvider = new DefaultWillProvider();
        IDecisionProvider Provider(string id) => !allDie && id == Author ? willProvider : new ActionCoverageRecorder(chooseIdle: true);
        using var generated = NormalPathWorld.CreateGenerated("default-estate-natural", Provider);
        var state = generated.ExportState();
        var society = state.Society.Society;
        var lastDay = Assert.IsType<SocietyDayLifecycle>(society.Config.DayLifecycle).MaximumDay;
        var birth = society.LifeTickAt(society.WorldTick + 1) - lastDay * society.Config.TicksPerLifecycleAge;
        var inventory = society.Inventory with { Lots = [] };
        inventory = InventoryFixture.AddLot(inventory, "personal-wood", "wood", Author, 5);
        society = society with
        {
            Inventory = inventory,
            Inhabitants = society.Inhabitants.Select(person => allDie || person.Id == Author ? person with
            {
                BirthTick = society.LifeClock is null ? birth : person.BirthTick,
                BirthLifeTick = society.LifeClock is null ? null : birth,
                AgeBand = SocietyAgeBand.Elder,
                LastLifecycleYearChecked = lastDay - 1,
            } : person).ToArray(),
        };
        var town = state.Towns![0];
        if (!allDie)
        {
            var civic = TownLawRules.ProposeEstateDefault(town.Governance!, town.Government!, town.Id, town.ResidentIds[0],
                share, town.ResidentIds, 0, state.WorldSystems!.Config.TicksPerDay);
            var council = civic.Council;
            foreach (var voter in town.ResidentIds.Take(3)) council = TownGovernanceRules.VoteProposal(council, council.Proposals[^1].Id, voter, true, 0);
            civic = TownLawRules.Enact(council, civic.Government, town.Id, town.Name, 0);
            town = town with { Governance = civic.Council, Government = civic.Government };
        }
        using var world = PrivateWorldRuntime.Restore(state with
        {
            Society = state.Society with { Society = society },
            Towns = [town],
            Inhabitants = state.Inhabitants.Select(person => person with { Equipment = null }).ToArray(),
        }, Provider);
        Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
        var estate = world.Society.Estates.Single(item => item.DeceasedId == Author);
        for (var attempt = 0; attempt < 40 && estate.WillStatus == "pending"; attempt++)
        {
            await Task.Delay(10);
            Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
            estate = world.Society.GetEstate(estate.Id);
        }
        Assert.Equal("default", estate.WillStatus);
        if (!allDie)
        {
            var request = Assert.Single(willProvider.Requests);
            var choice = request.Observation.Candidates.Single(candidate => candidate.Id == CognitionWillContext.HouseholdCandidateId);
            Assert.Contains("50%", choice.Description, StringComparison.Ordinal);
            Assert.Contains(town.Name, choice.Description, StringComparison.Ordinal);
        }
        Assert.Equal(Town, world.ExportState().DeceasedInhabitants!.Single(person => person.InhabitantId == Author).TownId);
        Assert.False(estate.Settled);
        // The native society test above covers the full unshortened hold. Here the
        // restored deadline is one tick away to exercise the world commit boundary.
        var saved = world.ExportState();
        var due = saved.Society.Society with
        {
            Estates = saved.Society.Society.Estates.Select(item => item with { ExpiryTick = saved.Society.Society.WorldTick + 1 }).ToArray(),
        };
        var encoded = PrivateWorldRuntimeCodec.Encode(saved with { Society = saved.Society with { Society = due } });
        using var settling = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(encoded), _ => new ActionCoverageRecorder(chooseIdle: true));
        Assert.False((await settling.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(encoded, PrivateWorldRuntimeCodec.Encode(settling.ExportState()));
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(encoded), _ => new ActionCoverageRecorder(chooseIdle: true));
        Assert.True((await settling.AdvanceOneTickAsync()).Advanced);
        Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(settling.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        Assert.True(settling.Society.GetEstate(estate.Id).Settled);
        var wood = settling.Society.Inventory.Lots.Where(lot => lot.ItemKind == "wood").ToArray();
        Assert.Equal(townWood, wood.Where(lot => lot.OwnerId == Town && lot.StorageBuildingId == "first-town-warehouse").Sum(lot => lot.Quantity));
        Assert.Equal(heirWood, wood.Where(lot => estate.BeneficiaryIds.Contains(lot.OwnerId)).Sum(lot => lot.Quantity));
        Assert.DoesNotContain(wood, lot => lot.OwnerId == "settlement:communal");
        Assert.Equal(5, wood.Sum(lot => lot.Quantity));
        Assert.True((await settling.AdvanceOneTickAsync()).Advanced);
        Assert.Single(settling.Society.Inventory.Events, item => item.Kind == "estate_settled" && item.Detail == estate.Id);
        settling.Validate();
        if (!allDie) return;

        const string visitor = "agent:00000000000000000000000000000140";
        var abandoned = settling.ExportState();
        var outside = abandoned.Map.Tiles.Select(tile => tile.Position).First(point => abandoned.Map.IsBuildable(point) &&
            !abandoned.Towns!.Any(item => item.BorderTiles.Contains(point)) &&
            !abandoned.Map.CampObjects.Any(item => item.Position == point) &&
            !abandoned.Map.Resources.Any(item => item.Position == point));
        settling.AddAgent(visitor, outside);
        Assert.True(settling.RenameAgent(visitor, "Moss Reed"));
        var warehouse = settling.WorldSimulation.Buildings.Single(building => building.InstanceId == "first-town-warehouse");
        var arrival = settling.ExportState();
        using var pickup = PrivateWorldRuntime.Restore(arrival with
        {
            Inhabitants = arrival.Inhabitants.Select(person => person.InhabitantId == visitor ? person with
            {
                Position = warehouse.Position,
                TravelCooldownTicks = 0,
                LastDecisionContext = null,
            } : person).ToArray(),
        }, _ => new SalvageProvider());
        for (var tick = 0; tick < 12 && !pickup.ExportState().Events.Any(item => item.Kind == "town_stock_salvaged"); tick++)
            Assert.True((await pickup.AdvanceOneTickAsync()).Advanced);
        Assert.Single(pickup.ExportState().Events, item => item.Kind == "town_stock_salvaged");
        Assert.Equal(5, pickup.Society.Inventory.Lots.Where(lot => lot.OwnerId == visitor && lot.ItemKind == "wood").Sum(lot => lot.Quantity));
        Assert.True(Assert.Single(pickup.Towns).IsAbandoned);
        Assert.DoesNotContain(visitor, pickup.Towns[0].ResidentIds);
        var collected = PrivateWorldRuntimeCodec.Encode(pickup.ExportState());
        using var collectedReload = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(collected));
        Assert.Equal(collected, PrivateWorldRuntimeCodec.Encode(collectedReload.ExportState()));
    }

    [Theory]
    [InlineData("share")]
    [InlineData("missingShare")]
    [InlineData("missingVersionEffect")]
    [InlineData("missingDraftEffect")]
    [InlineData("wording")]
    [InlineData("scope")]
    public void DamagedTypedEstateLawsAreRefused(string damage)
    {
        using var generated = NormalPathWorld.CreateGenerated("default-estate-damage", _ => new ActionCoverageRecorder(chooseIdle: true));
        var state = generated.ExportState();
        var town = state.Towns![0];
        var ids = town.ResidentIds;
        var civic = TownLawRules.ProposeEstateDefault(town.Governance!, town.Government!, town.Id, ids[0], 50,
            ids, 0, state.WorldSystems!.Config.TicksPerDay);
        var council = civic.Council;
        foreach (var voter in ids.Take(3)) council = TownGovernanceRules.VoteProposal(council, council.Proposals[^1].Id, voter, true, 0);
        civic = TownLawRules.Enact(council, civic.Government, town.Id, town.Name, 0);
        var encoded = PrivateWorldRuntimeCodec.Encode(state with { Towns = [town with { Governance = civic.Council, Government = civic.Government }] });
        using (var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(encoded)))
            Assert.Equal(encoded, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        var document = JsonNode.Parse(encoded)!;
        var government = document["state"]!["towns"]![0]!["government"]!;
        var version = government["laws"]![0]!["versions"]![0]!;
        var draft = government["lawDrafts"]![0]!;
        switch (damage)
        {
            case "share": version["estateDefault"]!["townSharePercent"] = 101; break;
            case "missingShare": version["estateDefault"]!.AsObject().Remove("townSharePercent"); break;
            case "missingVersionEffect": version.AsObject().Remove("estateDefault"); break;
            case "missingDraftEffect": draft.AsObject().Remove("estateDefault"); break;
            case "wording": version["rule"] = "Take all goods."; break;
            case "scope": version["scope"] = TownLawRules.Jurisdiction; break;
        }
        Assert.ThrowsAny<Exception>(() =>
        {
            using var refused = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(Encoding.UTF8.GetBytes(document.ToJsonString())));
        });
    }

    private static (TownGovernanceState Council, TownGovernmentState Government) Pass(
        (TownGovernanceState Council, TownGovernmentState Government) civic, long tick)
    {
        var council = civic.Council;
        foreach (var voter in Adults.Take(2)) council = TownGovernanceRules.VoteProposal(council, council.Proposals[^1].Id, voter, true, tick);
        return TownLawRules.Enact(council, civic.Government, Town, "First Town", tick);
    }

    private static SocietyCheckpoint EstateFixture(bool household, bool book = false)
    {
        var config = new SocietyConfig(TicksPerWorldDay: 2, DaysPerWorldYear: 2, BaseNaturalMortalityBasisPoints: 0);
        var checkpoint = SocietyFixture.CreateGenesis("default-estate", Adults.Select(id =>
            SocietyFixture.CreateFounder(id, id, "model:" + id, config: config)).ToArray(), config: config);
        if (household) checkpoint = SocietyFixture.CreateHousehold(checkpoint, "home", "The Home", ["alice", "bob"]).Checkpoint;
        var inventory = InventoryFixture.AddLot(checkpoint.Inventory, "wood-lot", "wood", "alice", 5);
        inventory = InventoryFixture.AddLot(inventory, "food-lot", "food", "alice", 2);
        if (book) inventory = InventoryFixture.AddLot(inventory, "book-lot", "book", "alice", 1);
        return SocietyFixture.Kill(checkpoint with { Inventory = inventory }, "alice", SocietyDeathCause.Hazard).Checkpoint;
    }

    private sealed class SalvageProvider : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
        public long ProviderEpoch => 1;

        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var observation = request.Observation;
            var selected = observation.Candidates.FirstOrDefault(candidate => candidate.Id.StartsWith("town_salvage:", StringComparison.Ordinal)) ??
                observation.Candidates.Single(candidate => candidate.Id == "safe_idle");
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, observation.InhabitantId, Kind, ProviderEpoch,
                observation.RunEpoch, observation.DecisionGeneration, observation.ObservationDigest, selected.Id, 1,
                observation.Candidates.ToDictionary(candidate => candidate.Id, candidate => candidate.Id == selected.Id ? 1d : 0d, StringComparer.Ordinal)));
        }
    }

    private sealed class DefaultWillProvider : IDecisionProvider
    {
        public ConcurrentQueue<CognitionDecisionRequest> Requests { get; } = new();
        public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
        public long ProviderEpoch => 1;

        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var observation = request.Observation;
            var isWill = observation.Will is not null;
            if (isWill) Requests.Enqueue(request);
            var selected = observation.Candidates.Single(candidate => candidate.Id ==
                (isWill ? CognitionWillContext.HouseholdCandidateId : "safe_idle"));
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, observation.InhabitantId, Kind, ProviderEpoch,
                observation.RunEpoch, observation.DecisionGeneration, observation.ObservationDigest, selected.Id, 1,
                observation.Candidates.ToDictionary(candidate => candidate.Id, candidate => candidate.Id == selected.Id ? 1d : 0d, StringComparer.Ordinal),
                Will: isWill ? new CognitionWillChoice([]) : null));
        }
    }

    private sealed class EstateLawProvider(string actor) : IDecisionProvider
    {
        public ConcurrentQueue<string> Seen { get; } = new();
        public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
        public long ProviderEpoch => 1;

        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var observation = request.Observation;
            foreach (var candidate in observation.Candidates) Seen.Enqueue(candidate.Id);
            var civic = observation.Candidates.Where(candidate => candidate.Id.StartsWith("civic|town:first|", StringComparison.Ordinal)).ToArray();
            var selected = civic.FirstOrDefault(candidate => candidate.Id.Contains("|yes|", StringComparison.Ordinal)) ??
                civic.FirstOrDefault(candidate => candidate.Id.Contains("|read|", StringComparison.Ordinal)) ??
                (actor == Author ? civic.FirstOrDefault(candidate => candidate.Id == "civic|town:first|estate_default|adopt|50") : null) ??
                observation.Candidates.Single(candidate => candidate.Id == "safe_idle");
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, observation.InhabitantId, Kind, ProviderEpoch,
                observation.RunEpoch, observation.DecisionGeneration, observation.ObservationDigest, selected.Id, 1,
                observation.Candidates.ToDictionary(candidate => candidate.Id, candidate => candidate.Id == selected.Id ? 1d : 0d, StringComparer.Ordinal)));
        }
    }
}

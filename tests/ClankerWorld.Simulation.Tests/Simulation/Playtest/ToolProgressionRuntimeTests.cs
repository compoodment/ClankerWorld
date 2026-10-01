using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed class ToolProgressionRuntimeTests
{
    [Theory]
    [InlineData("gold_ore", "iron")]
    [InlineData("diamond", "iron")]
    [InlineData("gold_ore", "stone")]
    [InlineData("diamond", "stone")]
    [InlineData("gold_ore", "reserved")]
    [InlineData("diamond", "reserved")]
    [InlineData("gold_ore", "full")]
    [InlineData("diamond", "full")]
    public async Task NormalRareMiningRequiresAUsableIronPickAndRoomAndDepletesRealStockAcrossReload(
        string itemKind, string scenario)
    {
        using var setup = NormalPathWorld.CreateGenerated("tool-rare-mining", _ => new CandidateProvider("safe_idle"));
        var state = setup.ExportState();
        var actor = state.Society.Society.Inhabitants[0].Id;
        var source = state.Map.Resources.Where(resource => resource.Kind == itemKind && !resource.IsRenewable &&
                state.Map.IsPassable(resource.Position) &&
                !state.Inhabitants.Any(person => person.InhabitantId != actor && person.Position == resource.Position))
            .OrderBy(resource => resource.Id, StringComparer.Ordinal).First();
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory,
            "mining-pick", scenario == "stone" ? "stone_pickaxe" : "iron_pickaxe", actor, 1);
        inventory = InventoryFixture.AddLot(inventory, "mining-basket", "basket", actor, 1);
        if (scenario == "full")
            inventory = InventoryFixture.AddLot(inventory, "mining-ballast", "fiber", actor, 8);
        if (scenario == "reserved")
            inventory = InventoryFixture.Reserve(inventory, "held-mining-pick", actor, "mining-pick", 1,
                "pending_trade", state.Society.Society.WorldTick + 120);
        var systems = state.WorldSystems!;
        state = state with
        {
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
            WorldSystems = systems with
            {
                Ecology = systems.Ecology with
                {
                    Resources = systems.Ecology.Resources.Select(resource => resource.Id == source.Id
                        ? resource with { Quantity = 1 } : resource).ToArray(),
                },
            },
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with
                {
                    Position = source.Position,
                    HungerBasisPoints = 10_000,
                    LastDecisionContext = null,
                    Project = null,
                    Equipment = new(CarryAidLotId: "mining-basket"),
                } : person).ToArray(),
        };
        var candidate = "gather_rare_material:" + itemKind;
        var chooser = new CandidateProvider(candidate);
        using var mining = PrivateWorldRuntime.Restore(
            PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)),
            id => id == actor ? chooser : new CandidateProvider("safe_idle"));
        for (var tick = 0; tick < 20 && (scenario == "iron"
                 ? !mining.Society.Inventory.Lots.Any(lot => lot.OwnerId == actor && lot.ItemKind == itemKind)
                 : chooser.ObservedCandidateSets.Count == 0); tick++)
        {
            Assert.True((await mining.AdvanceOneTickAsync()).Advanced);
            mining.Validate();
        }
        Assert.NotEmpty(chooser.ObservedCandidateSets);
        var extracted = mining.Society.Inventory.Lots.Where(lot => lot.OwnerId == actor && lot.ItemKind == itemKind).ToArray();
        if (scenario == "iron")
        {
            Assert.Contains(chooser.ObservedCandidateSets, candidates => candidates.Contains(candidate));
            var goods = Assert.Single(extracted);
            Assert.Equal(8, goods.Quantity);
            Assert.Null(goods.StorageBuildingId);
            Assert.Null(goods.DeliveryBuildingId);
            Assert.Null(goods.GroundPosition);
            var depleted = mining.WorldSystems.Ecology.GetResource(source.Id);
            Assert.Equal((0, EcologyResourceState.Depleted), (depleted.Quantity, depleted.State));
            Assert.Equal(9_000, mining.Society.Inventory.GetLot("mining-pick").ConditionBasisPoints);
        }
        else
        {
            Assert.DoesNotContain(chooser.ObservedCandidateSets, candidates => candidates.Contains(candidate));
            Assert.Empty(extracted);
            Assert.Equal(1, mining.WorldSystems.Ecology.GetResource(source.Id).Quantity);
            Assert.Equal(10_000, mining.Society.Inventory.GetLot("mining-pick").ConditionBasisPoints);
            if (scenario == "full")
                Assert.Equal(8, mining.Society.Inventory.GetLot("mining-ballast").Quantity);
            if (scenario == "reserved")
                Assert.Equal(InventoryReservationState.Reserved,
                    mining.Society.Inventory.GetReservation("held-mining-pick").State);
        }
        var bytes = PrivateWorldRuntimeCodec.Encode(mining.ExportState());
        using var reloaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes),
            _ => new CandidateProvider("safe_idle"));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(reloaded.ExportState()));
        Assert.True((await reloaded.AdvanceOneTickAsync()).Advanced);
        reloaded.Validate();
        Assert.Equal(extracted.Select(lot => lot with { LastProcessedTick = reloaded.Society.WorldTick }),
            reloaded.Society.Inventory.Lots.Where(lot => lot.OwnerId == actor && lot.ItemKind == itemKind));
    }

    [Theory]
    [InlineData("wooden_axe", "collect_wooden_axe")]
    [InlineData("wooden_pickaxe", "collect_wooden_pickaxe")]
    public async Task LegacyHouseholdStarterToolsArePhysicalStockAndCanBeCollected(
        string itemKind, string candidateId)
    {
        const string actor = "founder-scout";
        var chooser = new CandidateProvider(candidateId);
        using var world = new PrivateWorldRuntime("legacy-tool-stock-" + itemKind,
            id => id == actor ? chooser : new CandidateProvider("safe_idle"));

        var stock = Assert.Single(world.Society.Inventory.Lots, lot =>
            lot.OwnerId == "household:camp-alpha" && lot.ItemKind == itemKind);
        Assert.Equal(1, stock.Quantity);
        Assert.Equal(10_000, stock.ConditionBasisPoints);
        Assert.Null(stock.StorageBuildingId);
        Assert.Null(stock.GroundPosition);

        for (var tick = 0; tick < 120 && !world.ExportState().Events.Any(item =>
                 item.Kind == "equipment_collected" && item.Detail == $"{actor}:{itemKind}"); tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);

        Assert.Contains(chooser.ObservedCandidateSets, candidates => candidates.Contains(candidateId));
        Assert.Contains(world.ExportState().Events, item =>
            item.Kind == "equipment_collected" && item.Detail == $"{actor}:{itemKind}");
        var collected = Assert.Single(world.Society.Inventory.Lots, lot =>
            lot.OwnerId == actor && lot.ItemKind == itemKind);
        Assert.Equal(1, collected.Quantity);
        Assert.Null(collected.StorageBuildingId);
        Assert.Null(collected.GroundPosition);
    }

    private sealed class CandidateProvider(string candidateId) : IDecisionProvider
    {
        public List<string[]> ObservedCandidateSets { get; } = [];
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;

        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            var candidates = request.Observation.Candidates;
            ObservedCandidateSets.Add(candidates.Select(item => item.Id).ToArray());
            var selected = candidates.FirstOrDefault(item => item.Id == candidateId) ??
                candidates.Single(item => item.Id == "safe_idle");
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId,
                request.Observation.InhabitantId, Kind, ProviderEpoch, request.Observation.RunEpoch,
                request.Observation.DecisionGeneration, request.Observation.ObservationDigest, selected.Id, 1,
                candidates.ToDictionary(item => item.Id, item => item.Id == selected.Id ? 1d : 0d)));
        }
    }
}

using System.Text.Json.Nodes;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class BusinessTradeCheckpointTests
{
    [Theory]
    [InlineData("none")]
    [InlineData("seller-only")]
    [InlineData("both")]
    [InlineData("unrelated")]
    [InlineData("duplicate-buyer")]
    public async Task DecodingAnOpenShopExchangeRequiresTheCustomersActualAcceptance(string damage)
    {
        var (state, _) = await PendingExchangeAsync();
        var healthyBytes = PrivateWorldRuntimeCodec.Encode(state);
        var trade = Assert.Single(state.BusinessTrades!);
        var offer = state.Society.Society.Inventory.GetOffer(trade.OfferId);
        var document = JsonNode.Parse(healthyBytes)!;
        var savedOffer = document["state"]!["society"]!["society"]!["inventory"]!["offers"]!.AsArray()
            .Single(item => item!["id"]!.GetValue<string>() == offer.Id)!;
        savedOffer["acceptedBy"] = new JsonArray(DamagedAcceptance(damage, offer)
            .Select(party => (JsonNode?)JsonValue.Create(party)).ToArray());
        var damagedBytes = System.Text.Encoding.UTF8.GetBytes(document.ToJsonString());

        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Decode(damagedBytes));

        AssertHealthyRoundtrip(state, healthyBytes);
    }

    private static string[] DamagedAcceptance(string damage, DirectBarterOffer offer) => damage switch
    {
        "none" => [],
        "seller-only" => [offer.FirstPartyId],
        "both" => new[] { offer.FirstPartyId, offer.SecondPartyId }.Order(StringComparer.Ordinal).ToArray(),
        "unrelated" => ["unrelated-shop-customer"],
        "duplicate-buyer" => [offer.SecondPartyId, offer.SecondPartyId],
        _ => throw new ArgumentException("Unknown checkpoint damage.", nameof(damage)),
    };

    private static void AssertHealthyRoundtrip(PrivateWorldRuntimeState state, byte[] healthyBytes)
    {
        Assert.Equal(healthyBytes, PrivateWorldRuntimeCodec.Encode(state));
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(healthyBytes),
            _ => new ShopChoice("safe_idle"));
        Assert.Equal(state.BusinessTrades, restored.BusinessTrades);
        Assert.Equal(healthyBytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }

    private static async Task<(PrivateWorldRuntimeState State, string Seller)> PendingExchangeAsync()
    {
        using var setup = NormalPathWorld.CreateGenerated("probe-a", _ => new ShopChoice("safe_idle"));
        var state = setup.ExportState();
        if (!state.WorldSimulation!.Buildings.Any(building => state.WorldContent!.Buildings
                .Single(definition => definition.CanonicalId == building.DefinitionId).Tags.Contains("blacksmith")))
        {
            var definition = state.WorldContent!.Buildings.Single(item => item.LocalId == "blacksmith-1x2");
            var house = state.WorldSimulation.Buildings.First(building => building.HouseholdId is not null &&
                state.WorldContent.Buildings.Single(item => item.CanonicalId == building.DefinitionId).Tags.Contains("house"));
            var inventory = state.Society.Society.Inventory;
            foreach (var cost in definition.BuildCosts)
                inventory = InventoryFixture.AddLot(inventory, "checkpoint-shop-building-" + cost.ResourceId,
                    cost.ResourceId, house.HouseholdId!, cost.Amount, storageBuildingId: house.InstanceId);
            using var placing = PrivateWorldRuntime.Restore(WithInventory(state, inventory), _ => new ShopChoice("safe_idle"));
            _ = state.Map.Tiles.OrderBy(tile => state.Map.FootDistance(tile.Position, house.Position))
                .Select(tile => tile.Position).First(point =>
                    placing.PlaceBuilding("checkpoint-business-blacksmith", definition.CanonicalId, point, house.HouseholdId).Applied);
            state = placing.ExportState();
        }
        var shop = state.WorldSimulation!.Buildings.Single(building => state.WorldContent!.Buildings
            .Single(definition => definition.CanonicalId == building.DefinitionId).Tags.Contains("blacksmith"));
        var seller = state.Society.Society.GetHousehold(shop.HouseholdId!).MemberIds[0];
        var buyer = state.Society.Society.Inhabitants.First(person => person.HouseholdId != shop.HouseholdId).Id;
        var taken = state.Inhabitants.Where(person => person.InhabitantId != buyer && person.InhabitantId != seller)
            .Select(person => person.Position).ToHashSet();
        var buyerPosition = state.Map.Tiles.Select(tile => tile.Position).First(position =>
            Math.Abs(position.X - shop.Position.X) + Math.Abs(position.Y - shop.Position.Y) == 1 &&
            state.Map.IsPassable(position) && !taken.Contains(position));
        var stock = state.Society.Society.Inventory with
        {
            Lots = state.Society.Society.Inventory.Lots.Where(lot => lot.OwnerId != buyer).ToArray(),
        };
        stock = InventoryFixture.AddLot(stock, "checkpoint-shop-axe", "wooden_axe", shop.HouseholdId!, 2,
            conditionBasisPoints: 7_600, storageBuildingId: shop.InstanceId);
        stock = InventoryFixture.AddLot(stock, "checkpoint-buyer-payment", "wood", buyer, 6);
        state = WithInventory(state, stock) with
        {
            Inhabitants = state.Inhabitants.Select(person => person with
            {
                Position = person.InhabitantId == buyer ? buyerPosition : person.InhabitantId == seller ? shop.Position : person.Position,
                HungerBasisPoints = 7_500,
                LastDecisionContext = null,
                Project = null,
                Equipment = person.InhabitantId == buyer ? null : person.Equipment,
            }).ToArray(),
        };
        using var offering = PrivateWorldRuntime.Restore(state, actor => actor == buyer
            ? new ShopChoice("business_shop:") : new ShopChoice("safe_idle"));
        for (var tick = 0; tick < 40 && offering.BusinessTrades.Count == 0; tick++)
            Assert.True((await offering.AdvanceOneTickAsync()).Advanced);
        var trade = Assert.Single(offering.BusinessTrades);
        Assert.Equal(DirectBarterState.Open, offering.Society.Inventory.GetOffer(trade.OfferId).State);
        return (offering.ExportState(), seller);
    }

    private static PrivateWorldRuntimeState WithInventory(PrivateWorldRuntimeState state, InventoryCheckpoint inventory) => state with
    {
        Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
    };

    private sealed class ShopChoice(string prefix) : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;

        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            var choice = request.Observation.Candidates.FirstOrDefault(candidate => candidate.Id.StartsWith(prefix, StringComparison.Ordinal))
                ?? request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle");
            return new DeterministicDecisionProvider().DecideAsync(request with
            {
                Observation = request.Observation with { Candidates = [choice] },
            }, cancellationToken);
        }
    }
}

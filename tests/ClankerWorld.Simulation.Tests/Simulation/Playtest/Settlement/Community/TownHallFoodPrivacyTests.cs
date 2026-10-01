using System.Collections.Concurrent;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class TownHallTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ModelFoodChoiceAndActualHousePickupIgnoreForeignPrivateStockAcrossReload(bool reload)
    {
        var state = AtHall(WithHall(Initial()));
        using (var adopting = PrivateWorldRuntime.Restore(state, _ => new CivicChooser(false, false)))
        {
            var voters = adopting.Towns.Single(town => town.Id == Town).ResidentIds.ToArray();
            Assert.True(adopting.ProposeTownLaw(voters[0], Town, "shared_food",
                "Keep scarce communal food for hungry residents.", "essential_first").Applied);
            foreach (var voter in voters.Take(3)) Assert.True(adopting.VoteTownLaw(voter, Town, true).Applied);
            Assert.Equal("essential_first", adopting.TownCouncils.Single().FoodPolicy);
            state = adopting.ExportState();
        }

        var actor = state.Society.Society.Inhabitants.First(person => person.HouseholdId == Alpha).Id;
        var inventory = state.Society.Society.Inventory;
        var starterLots = inventory.Lots.Where(lot => FoodItems.IsEdible(lot.ItemKind) && lot.Quantity > 0 &&
            lot.OwnerId is Alpha or "household:camp-beta").Select(lot => lot.Id).ToArray();
        foreach (var id in starterLots)
        {
            var ration = inventory.GetLot(id);
            inventory = InventoryFixture.Reserve(inventory, "privacy-consumed:" + id, ration.OwnerId,
                id, ration.Quantity, "fixture_rations_consumed", inventory.WorldTick);
            inventory = InventoryFixture.ConsumeReservation(inventory, "privacy-consumed:" + id);
        }
        inventory = InventoryFixture.AddLot(inventory, "own-private-serving", "berries", Alpha, 1,
            storageBuildingId: "first-town-house-a");
        var house = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == "first-town-house-a");
        state = state with
        {
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { Position = house.Position, HungerBasisPoints = 6_000, Survival = new() } : person).ToArray()
        };
        var foreignInventory = InventoryFixture.AddLot(inventory, "foreign-private-serving", "berries",
            "household:camp-beta", 4, storageBuildingId: "first-town-house-b");
        var foreignState = state with
        {
            Society = state.Society with { Society = state.Society.Society with { Inventory = foreignInventory } }
        };
        if (reload)
        {
            state = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state));
            foreignState = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(foreignState));
        }

        var withoutForeign = new FoodPrivacyModelRecorder(actor);
        var withForeign = new FoodPrivacyModelRecorder(actor);
        using var first = PrivateWorldRuntime.Restore(state, _ => withoutForeign);
        using var second = PrivateWorldRuntime.Restore(foreignState, _ => withForeign);
        var firstStep = await first.AdvanceOneTickAsync();
        var secondStep = await second.AdvanceOneTickAsync();
        Assert.True(firstStep.Advanced);
        Assert.True(secondStep.Advanced);
        Assert.Equal("collect_shared_food", firstStep.Decisions.Single(item => item.InhabitantId == actor).Admission.Intention!.CandidateId);
        Assert.Equal("collect_shared_food", secondStep.Decisions.Single(item => item.InhabitantId == actor).Admission.Intention!.CandidateId);

        var firstRequest = Assert.Single(withoutForeign.Requests, request => request.Observation.InhabitantId == actor);
        var secondRequest = Assert.Single(withForeign.Requests, request => request.Observation.InhabitantId == actor);
        var firstChoice = Assert.Single(firstRequest.Observation.Candidates, candidate => candidate.Id == "collect_shared_food");
        var secondChoice = Assert.Single(secondRequest.Observation.Candidates, candidate => candidate.Id == "collect_shared_food");
        Assert.Equal(firstChoice.Description, secondChoice.Description);
        Assert.Equal(firstChoice.DestinationName, secondChoice.DestinationName);
        foreach (var world in new[] { first, second })
        {
            var collected = world.Society.Inventory.GetLot("own-private-serving");
            Assert.Equal((actor, "berries", 1, (string?)null),
                (collected.OwnerId, collected.ItemKind, collected.Quantity, collected.ProvenanceLotId));
            Assert.Null(collected.StorageBuildingId);
            Assert.Null(collected.DeliveryBuildingId);
            Assert.Null(collected.ContainerLotId);
            Assert.Null(collected.GroundPosition);
            Assert.Contains(world.ExportState().Events, item => item.Kind == "household_food_collected" &&
                item.Detail == actor + ":own-private-serving:1" && item.Position == house.Position);
            Assert.Contains(world.Society.Inventory.Events, item => item.Kind == "inventory_transferred" &&
                item.Detail == $"household-food:1:{actor}:{Alpha}:{actor}:own-private-serving:1:household_food_share");
            foreach (var id in starterLots)
            {
                Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.Id == id);
                Assert.Equal(id, world.Society.Inventory.GetReservation("privacy-consumed:" + id).LotId);
                Assert.Equal(InventoryReservationState.Completed,
                    world.Society.Inventory.GetReservation("privacy-consumed:" + id).State);
            }
            world.Validate();
        }
        Assert.Equal(4, second.Society.Inventory.GetLot("foreign-private-serving").Quantity);
        Assert.Equal("household:camp-beta", second.Society.Inventory.GetLot("foreign-private-serving").OwnerId);
        Assert.Equal("first-town-house-b", second.Society.Inventory.GetLot("foreign-private-serving").StorageBuildingId);
    }

    private sealed class FoodPrivacyModelRecorder(string actor) : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
        public long ProviderEpoch => 0;
        public ConcurrentQueue<CognitionDecisionRequest> Requests { get; } = new();

        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            request.Validate();
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Enqueue(request);
            var selected = request.Observation.InhabitantId == actor
                ? request.Observation.Candidates.FirstOrDefault(candidate => candidate.Id == "collect_shared_food") : null;
            selected ??= request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle");
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, request.Observation.InhabitantId,
                Kind, ProviderEpoch, request.Observation.RunEpoch, request.Observation.DecisionGeneration,
                request.Observation.ObservationDigest, selected.Id, 1,
                new Dictionary<string, double> { [selected.Id] = 1 }));
        }
    }
}

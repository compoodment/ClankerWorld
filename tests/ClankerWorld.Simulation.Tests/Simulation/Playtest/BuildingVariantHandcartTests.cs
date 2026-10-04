using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class BuildingVariantHandcartTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EachSmithMakesFittingsThenCollectsPersonalCartInputsAndReplaysItsActualJob(bool largerSmith)
    {
        var fixture = largerSmith
            ? await BuildingVariantTestWorld.BuildAsync("blacksmith")
            : await BuildingVariantTestWorld.CreatePreparedAsync("blacksmith");
        if (!largerSmith)
        {
            var smith = fixture.State.WorldSimulation!.Buildings.Single(item => item.InstanceId == "first-town-blacksmith");
            fixture = fixture with
            {
                Actor = fixture.State.Society.Society.Inhabitants.First(item => item.HouseholdId == smith.HouseholdId).Id,
                Definition = fixture.State.WorldContent!.Buildings.Single(item => item.CanonicalId == smith.DefinitionId),
            };
        }
        var building = fixture.Building;
        var actor = fixture.Actor;
        var household = fixture.Household;
        var recipes = fixture.State.WorldContent!.Recipes.Where(item => item.WorkstationBuildingId == building.DefinitionId).ToArray();
        var fittingsRecipe = Assert.Single(recipes, item => item.Outputs.Any(output => output.ResourceId == "iron_fittings"));
        var cartRecipe = Assert.Single(recipes, item => item.Outputs.Any(output => output.ResourceId == "handcart"));
        var cartCandidate = "build:recipe:" + cartRecipe.CanonicalId;
        var fittingsCandidate = "build:recipe:" + fittingsRecipe.CanonicalId;
        var inventory = fixture.State.Society.Society.Inventory;
        var replaced = inventory.Lots.Where(lot => (lot.OwnerId == household || lot.OwnerId == actor) &&
            lot.ItemKind is "iron" or "wood" or "iron_fittings" or "rope").Select(lot => lot.Id).ToHashSet(StringComparer.Ordinal);
        Assert.DoesNotContain(inventory.Reservations, reservation => replaced.Contains(reservation.LotId) &&
            reservation.State is InventoryReservationState.Reserved or InventoryReservationState.PartiallyConsumed or InventoryReservationState.Committed);
        inventory = inventory with { Lots = inventory.Lots.Where(lot => !replaced.Contains(lot.Id)).ToArray() };
        inventory = InventoryFixture.AddLot(inventory, "cart-native-iron", "iron", household, 1,
            storageBuildingId: building.InstanceId);
        inventory = InventoryFixture.AddLot(inventory, "cart-native-wood", "wood", household, 4,
            storageBuildingId: building.InstanceId);
        inventory = InventoryFixture.AddLot(inventory, "cart-native-rope", "rope", household, 1,
            storageBuildingId: building.InstanceId);
        inventory = InventoryFixture.AddLot(inventory, "cart-carry-basket", "basket", actor, 1);
        fixture = fixture with
        {
            State = BuildingVariantTestWorld.WithInventory(fixture.State, inventory) with
            {
                Inhabitants = fixture.State.Inhabitants.Select(person => person.InhabitantId == actor
                    ? person with
                    {
                        Position = building.Position,
                        Project = null,
                        LastDecisionContext = null,
                        HungerBasisPoints = 10_000,
                        TravelCooldownTicks = 0,
                    } : person).ToArray(),
            },
        };
        var fittingsProvider = new VariantActionProvider(id => id == "equip_carry_aid");
        using var makingFittings = BuildingVariantTestWorld.Restore(fixture, fittingsProvider);
        await BuildingVariantTestWorld.UntilAsync(makingFittings, () => makingFittings.Inhabitants
            .Single(person => person.InhabitantId == actor).Equipment?.CarryAidLotId == "cart-carry-basket", 8);
        Assert.Contains("equip_carry_aid", fittingsProvider.Chosen);
        fittingsProvider.Choose = id => id == fittingsCandidate;
        await BuildingVariantTestWorld.UntilAsync(makingFittings, () => makingFittings.WorldSimulation.ProductionJobs
            .Any(job => job.RecipeId == fittingsRecipe.CanonicalId && job.State == WorldProductionJobState.Completed), 100);
        var fittingsJob = Assert.Single(makingFittings.WorldSimulation.ProductionJobs,
            job => job.RecipeId == fittingsRecipe.CanonicalId);
        Assert.Equal(building.InstanceId, fittingsJob.BuildingInstanceId);
        Assert.Equal(household, fittingsJob.OwnerId);
        Assert.Contains(fittingsCandidate, fittingsProvider.Chosen);
        Assert.DoesNotContain(makingFittings.Society.Inventory.Lots, lot => lot.Id == "cart-native-iron");
        var actualFittings = Assert.Single(makingFittings.Society.Inventory.Lots,
            lot => lot.ItemKind == "iron_fittings" && lot.OwnerId == household);
        Assert.Equal(2, actualFittings.Quantity);
        Assert.DoesNotContain(makingFittings.Society.Inventory.Lots, lot => lot.ItemKind == "handcart");

        var ready = makingFittings.ExportState();
        fixture = fixture with
        {
            State = ready with
            {
                Inhabitants = ready.Inhabitants.Select(person => person.InhabitantId == actor
                    ? person with { LastDecisionContext = null } : person).ToArray(),
            },
        };
        static bool CartCollection(string id) => id.StartsWith("collect_handcart_material:", StringComparison.Ordinal);
        bool ChooseCart(string id) => CartCollection(id) || id == cartCandidate;
        var cartProvider = new VariantActionProvider(ChooseCart);
        using var world = BuildingVariantTestWorld.Restore(fixture, cartProvider);
        await BuildingVariantTestWorld.UntilAsync(world, () => world.WorldSimulation.ProductionJobs
            .Any(job => job.RecipeId == cartRecipe.CanonicalId && job.State == WorldProductionJobState.Running), 100);
        foreach (var kind in new[] { "wood", "iron_fittings", "rope" })
            Assert.Contains("collect_handcart_material:" + kind, cartProvider.Chosen);
        Assert.Contains(cartCandidate, cartProvider.Chosen);
        var cartJob = Assert.Single(world.WorldSimulation.ProductionJobs, job => job.RecipeId == cartRecipe.CanonicalId);
        Assert.Equal((actor, actor, building.InstanceId), (cartJob.OwnerId, cartJob.WorkerId, cartJob.BuildingInstanceId));
        Assert.Equal(7, cartJob.InputReservationIds.Sum(id => world.Society.Inventory.GetReservation(id).Quantity));
        Assert.All(cartJob.InputReservationIds, id =>
        {
            var reservation = world.Society.Inventory.GetReservation(id);
            var lot = world.Society.Inventory.GetLot(reservation.LotId);
            Assert.Equal(actor, reservation.OwnerId);
            Assert.Equal(actor, lot.OwnerId);
            Assert.True(ToolProgressionRules.IsTopLevelCarriedLot(lot, actor));
        });
        var saved = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        // Both sizes share the same active content catalogue. A global first
        // recipe can match only one of these two actual running jobs.
        await AssertRunningCartDoesNotCollectAnotherSet(fixture with
        { State = PrivateWorldRuntimeCodec.Decode(saved) }, cartJob.JobId);
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved),
            id => id == actor ? new VariantActionProvider(ChooseCart) : new VariantActionProvider());
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        for (var tick = 0; tick < 100 && !world.Society.Inventory.Lots.Any(lot => lot.ItemKind == "handcart"); tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
        }
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        var cart = Assert.Single(world.Society.Inventory.Lots, lot => lot.ItemKind == "handcart");
        Assert.Equal(actor, cart.OwnerId);
        Assert.Equal(1, cart.Quantity);
        Assert.Equal(new InventoryGroundPosition(building.Position.X, building.Position.Y), cart.GroundPosition);
        Assert.Null(cart.StorageBuildingId);
        Assert.Null(cart.CarrierId);
        Assert.Null(cart.ContainerLotId);
        Assert.Empty(world.ExportState().HandcartHitches!);
        Assert.Equal(WorldProductionJobState.Completed,
            world.WorldSimulation.ProductionJobs.Single(job => job.JobId == cartJob.JobId).State);
        Assert.All(cartJob.InputReservationIds, id =>
            Assert.Equal(InventoryReservationState.Completed, world.Society.Inventory.GetReservation(id).State));
        Assert.DoesNotContain(world.Society.Inventory.Lots,
            lot => (lot.OwnerId == actor || lot.OwnerId == household) &&
                lot.ItemKind is "iron" or "wood" or "iron_fittings" or "rope");
    }

    private static async Task AssertRunningCartDoesNotCollectAnotherSet(BuildingVariantFixture fixture, string jobId)
    {
        var inventory = fixture.State.Society.Society.Inventory;
        foreach (var (kind, quantity) in new[] { ("wood", 4), ("iron_fittings", 2), ("rope", 1) })
            inventory = InventoryFixture.AddLot(inventory, "cart-spare-" + kind, kind, fixture.Household,
                quantity, storageBuildingId: fixture.Building.InstanceId);
        fixture = fixture with
        {
            State = BuildingVariantTestWorld.Strict(BuildingVariantTestWorld.WithInventory(fixture.State, inventory)),
        };
        var recorder = new RunningCartObservationProvider();
        using var world = PrivateWorldRuntime.Restore(fixture.State,
            id => id == fixture.Actor ? recorder : new VariantActionProvider());
        var worker = world.Inhabitants.Single(person => person.InhabitantId == fixture.Actor);
        Assert.True(PersonalEquipmentRules.FreeCapacity(world.Society.Inventory, fixture.Actor, worker.Equipment) > 0);
        Assert.Equal(WorldProductionJobState.Running,
            world.WorldSimulation.ProductionJobs.Single(job => job.JobId == jobId).State);
        // A real observer message requests a fresh model observation even while
        // ordinary project continuation would otherwise avoid another call.
        const string message = "Keep working on your current handcart.";
        var receipt = world.SubmitInstruction(new OwnerInstructionRequest("cart-running-observation", "owner:test", fixture.Actor,
            OwnerInstructionKind.Suggestive, message));
        await BuildingVariantTestWorld.UntilAsync(world, () => recorder.Requests.Count > 0, 3);
        var observation = Assert.Single(recorder.Requests).Observation;
        Assert.Equal(fixture.Actor, observation.InhabitantId);
        Assert.Contains(observation.ObserverGuidance ?? [], guidance =>
            guidance.InstructionId == receipt.InstructionId && guidance.TargetInhabitantId == fixture.Actor &&
                guidance.Text == message);
        Assert.Contains(observation.Candidates, candidate => candidate.Id == "safe_idle");
        Assert.Contains(world.ExportState().Instructions!, instruction =>
            instruction.InstructionId == receipt.InstructionId && instruction.Text == message);
        Assert.DoesNotContain(observation.Candidates,
            candidate => candidate.Id.StartsWith("collect_handcart_material:", StringComparison.Ordinal));
        Assert.Equal(WorldProductionJobState.Running,
            world.WorldSimulation.ProductionJobs.Single(job => job.JobId == jobId).State);
        foreach (var (kind, quantity) in new[] { ("wood", 4), ("iron_fittings", 2), ("rope", 1) })
        {
            var spare = world.Society.Inventory.GetLot("cart-spare-" + kind);
            Assert.Equal((fixture.Household, quantity, fixture.Building.InstanceId),
                (spare.OwnerId, spare.Quantity, spare.StorageBuildingId));
        }
    }

    private sealed class RunningCartObservationProvider : IDecisionProvider
    {
        private readonly VariantActionProvider idle = new();
        public List<CognitionDecisionRequest> Requests { get; } = [];
        public DecisionProviderKind Kind => idle.Kind;
        public long ProviderEpoch => idle.ProviderEpoch;

        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return idle.DecideAsync(request, cancellationToken);
        }
    }
}

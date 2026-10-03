using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Tests;

public sealed class ProjectHouseholdDeliveryTests
{
    private const string Household = "household:camp-beta";
    private const string SourceWood = "project-delivery-source-wood";
    private const string Ballast = "project-delivery-protected-stone";
    private const string Workshop = "project-delivery-paid-workshop";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnActiveProjectFinishesItsActualHouseholdDeliveryThenPaysForToolsAcrossReload(bool toWorkstation)
    {
        var fixture = await Prepared(toWorkstation);
        using var world = Restore(fixture.State);
        var initialBytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var replay = Restore(PrivateWorldRuntimeCodec.Decode(initialBytes));
        Assert.Equal(initialBytes, PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        Assert.Equal(initialBytes, PrivateWorldRuntimeCodec.Encode(world.ExportState()));

        var originalWood = world.Society.Inventory.GetLot(fixture.CargoId);
        Assert.Equal((fixture.Actor, 2, fixture.Destination.InstanceId, SourceWood),
            (originalWood.OwnerId, originalWood.Quantity, originalWood.DeliveryBuildingId, originalWood.ProvenanceLotId));
        Assert.Equal(8, Load(world, fixture.Actor));
        Assert.NotEqual(fixture.Destination.Position, Position(world, fixture.Actor));
        Assert.True(fixture.State.Map.IsReachableOnFoot(Position(world, fixture.Actor), fixture.Destination.Position));
        Assert.Equal(1, world.Society.Inventory.GetLot(SourceWood).Quantity);

        var sawDelivery = false;
        var moved = false;
        for (var tick = 0; tick < 96 && !CompletedTools(world, fixture); tick++)
        {
            var previous = Position(world, fixture.Actor);
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
            var current = Position(world, fixture.Actor);
            if (current != previous)
            {
                Assert.True(fixture.State.Map.CanFootStep(previous, current), $"The worker jumped from {previous} to {current}.");
                moved = true;
            }
            Assert.InRange(Load(world, fixture.Actor), 0, 8);
            if (!sawDelivery && world.ExportState().Events.Any(item => item.Kind == "household_stock_delivered" &&
                    item.Detail.StartsWith(fixture.Actor + ":" + fixture.CargoId + ":", StringComparison.Ordinal)))
            {
                sawDelivery = true;
                var delivered = world.Society.Inventory.GetLot(fixture.CargoId);
                Assert.Equal((Household, 2, fixture.Destination.InstanceId, (string?)null, SourceWood),
                    (delivered.OwnerId, delivered.Quantity, delivered.StorageBuildingId,
                        delivered.DeliveryBuildingId, delivered.ProvenanceLotId));
                Assert.Equal(fixture.Destination.Position, current);
                var project = world.Inhabitants.Single(person => person.InhabitantId == fixture.Actor).Project;
                Assert.NotNull(project);
                Assert.Equal(fixture.ProjectCandidate, project.CandidateId);
                Assert.Null(project.JobId);
            }
        }

        Assert.True(moved);
        Assert.True(sawDelivery, Failure(world, fixture));
        Assert.True(CompletedTools(world, fixture), Failure(world, fixture));
        var job = Assert.Single(world.WorldSimulation.ProductionJobs, item => item.WorkerId == fixture.Actor &&
            item.RecipeId == fixture.Recipe.CanonicalId);
        Assert.Equal((Workshop, Household, WorldProductionJobState.Completed),
            (job.BuildingInstanceId, job.OwnerId, job.State));
        var inputs = job.InputReservationIds.Select(world.Society.Inventory.GetReservation).ToArray();
        Assert.NotEmpty(inputs);
        Assert.Equal(3, inputs.Sum(receipt => receipt.Quantity));
        Assert.All(inputs, receipt =>
        {
            Assert.Equal(Household, receipt.OwnerId);
            Assert.Equal(InventoryReservationState.Completed, receipt.State);
        });
        Assert.Contains(inputs, receipt => receipt.LotId == fixture.CargoId && receipt.Quantity == 2);
        Assert.Contains(inputs, receipt => receipt.LotId == SourceWood && receipt.Quantity == 1);
        Assert.Equal((Household, "tool", 1),
            (world.Society.Inventory.GetLot(job.JobId + ":output:00").OwnerId,
                world.Society.Inventory.GetLot(job.JobId + ":output:00").ItemKind,
                world.Society.Inventory.GetLot(job.JobId + ":output:00").Quantity));
        Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.Id == fixture.CargoId || lot.Id == SourceWood);
        Assert.DoesNotContain(world.ExportState().Events, item => item.WorldTick > fixture.StartTick &&
            item.Kind == "carrying_full" && item.Detail.StartsWith(fixture.Actor + ":", StringComparison.Ordinal));
        AssertProtectedStock(world, fixture);
        world.Validate();
    }

    [Fact]
    public async Task AnActiveClaimOnTheActualDeliveryIsNotReleasedOrConsumedForAProject()
    {
        var fixture = await Prepared(toWorkstation: true);
        var inventory = InventoryFixture.Reserve(fixture.State.Society.Society.Inventory,
            "project-delivery-cargo-claim", fixture.Actor, fixture.CargoId, 2, "fixture-protected-delivery", long.MaxValue);
        using var world = Restore(FoodCapacityTestFixture.WithInventory(fixture.State, inventory));
        var claim = world.Society.Inventory.GetReservation("project-delivery-cargo-claim");
        var cargo = world.Society.Inventory.GetLot(fixture.CargoId);
        var position = Position(world, fixture.Actor);
        for (var tick = 0; tick < 12; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(claim, world.Society.Inventory.GetReservation(claim.Id));
        Assert.Equal(cargo with { LastProcessedTick = world.WorldTick }, world.Society.Inventory.GetLot(cargo.Id));
        Assert.Equal(position, Position(world, fixture.Actor));
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "household_stock_delivered" &&
            item.Detail.Contains(fixture.CargoId, StringComparison.Ordinal));
        Assert.DoesNotContain(world.WorldSimulation.ProductionJobs, item => item.WorkerId == fixture.Actor);
        AssertProtectedStock(world, fixture);
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var replay = Restore(PrivateWorldRuntimeCodec.Decode(bytes));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
    }

    [Fact]
    public async Task AProjectCannotRestoreARealDeliveryRetargetedToAnotherHousehold()
    {
        var fixture = await Prepared(toWorkstation: false);
        using var world = Restore(fixture.State);
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        var foreignHouse = fixture.State.WorldSimulation!.Buildings.Single(building => building.InstanceId == "first-town-house-a");
        Assert.NotEqual(Household, foreignHouse.HouseholdId);
        var inventory = fixture.State.Society.Society.Inventory with
        {
            Lots = fixture.State.Society.Society.Inventory.Lots.Select(lot => lot.Id == fixture.CargoId
                ? lot with { DeliveryBuildingId = foreignHouse.InstanceId } : lot).ToArray(),
        };
        var invalid = FoodCapacityTestFixture.WithInventory(fixture.State, inventory);
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(invalid));
        Assert.Throws<InvalidDataException>(() => Restore(invalid));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
    }

    private static async Task<Fixture> Prepared(bool toWorkstation)
    {
        var (initial, _, _) = await FoodCapacityTestFixture.Generated("probe-a");
        var actor = initial.Society.Society.Inhabitants.First(person => person.HouseholdId == Household &&
            person.AgeBand is SocietyAgeBand.Adult or SocietyAgeBand.Elder).Id;
        var house = initial.WorldSimulation!.Buildings.Single(building => building.InstanceId == "first-town-house-b");
        var smith = initial.WorldSimulation.Buildings.Single(building => building.InstanceId == "first-town-blacksmith");
        Assert.Equal(Household, smith.HouseholdId);
        var workshop = initial.WorldContent!.Buildings.Single(definition => definition.LocalId == "workshop");
        var recipe = initial.WorldContent.Recipes.Single(definition => definition.LocalId == "tools");
        Assert.Equal(3, Assert.Single(recipe.Inputs).Amount);
        Assert.Equal("wood", Assert.Single(recipe.Inputs).ResourceId);
        var inventory = InventoryFixture.AddLot(initial.Society.Society.Inventory, "00-project-workshop-payment",
            "wood", "household:camp-alpha", 10, storageBuildingId: "first-town-house-a");
        using var placing = Restore(FoodCapacityTestFixture.WithInventory(initial, inventory));
        var town = initial.Towns!.Single(item => item.ResidentIds.Contains(actor, StringComparer.Ordinal));
        BuildingPlacementResult? placed = null;
        foreach (var site in initial.Map.Tiles.Select(tile => tile.Position)
                     .Where(point => initial.Map.IsBuildable(point) &&
                         TownBorderRules.IsWithinOrAdjacent(town, point, workshop.Width, workshop.Height) &&
                         initial.Map.IsReachableOnFoot(house.Position, point))
                     .OrderBy(point => initial.Map.FootDistance(house.Position, point))
                     .ThenBy(point => point.Y).ThenBy(point => point.X))
        {
            var result = placing.PlaceBuilding(Workshop, workshop.CanonicalId, site);
            if (!result.Applied) continue;
            placed = result;
            break;
        }
        Assert.NotNull(placed);
        var state = placing.ExportState();
        var paid = state.Society.Society.Inventory.Reservations.Where(receipt =>
            receipt.LotId == "00-project-workshop-payment").ToArray();
        Assert.NotEmpty(paid);
        Assert.Equal(10, paid.Sum(receipt => receipt.Quantity));
        Assert.All(paid, receipt => Assert.Equal(InventoryReservationState.Completed, receipt.State));
        inventory = state.Society.Society.Inventory;
        var protectedClaims = new List<InventoryReservation>();
        foreach (var lot in inventory.Lots.Where(lot => lot.OwnerId == Household && lot.ItemKind == "wood").ToArray())
        {
            var available = PersonalEquipmentRules.AvailableQuantity(inventory, lot);
            if (available == 0) continue;
            var id = "project-fixture-existing-wood:" + lot.Id;
            inventory = InventoryFixture.Reserve(inventory, id, Household, lot.Id, available,
                "fixture-other-household-work", long.MaxValue);
            protectedClaims.Add(inventory.GetReservation(id));
        }
        Assert.DoesNotContain(inventory.Lots, lot => lot.OwnerId == actor);
        var destination = toWorkstation ? smith : house;
        var source = toWorkstation ? house : smith;
        inventory = InventoryFixture.AddLot(inventory, SourceWood, "wood", Household, 3, storageBuildingId: source.InstanceId);
        inventory = InventoryFixture.Transfer(inventory, "project-fixture-real-pickup", Household, actor, SourceWood, 2,
            toWorkstation ? "smith_input_picked_up" : "household_stock_picked_up",
            destinationDeliveryBuildingId: destination.InstanceId);
        var cargo = Assert.Single(inventory.Lots, lot => lot.OwnerId == actor && lot.ProvenanceLotId == SourceWood);
        inventory = InventoryFixture.AddLot(inventory, Ballast, "stone", actor, 6);
        inventory = InventoryFixture.Reserve(inventory, "project-delivery-ballast-claim", actor, Ballast, 6,
            "fixture-protected-personal-material", long.MaxValue);
        protectedClaims.Add(inventory.GetReservation("project-delivery-ballast-claim"));
        var candidate = TownConstructionCandidateIds.Recipe(recipe.CanonicalId);
        state = FoodCapacityTestFixture.WithInventory(state, inventory) with
        {
            Inhabitants = state.Inhabitants.Select(person => person with
            {
                HungerBasisPoints = 9_500,
                Position = person.InhabitantId == actor ? source.Position : person.Position,
                Equipment = person.InhabitantId == actor ? null : person.Equipment,
                Project = person.InhabitantId == actor
                    ? new SettlementProject(candidate, recipe.DisplayName, state.Society.Society.WorldTick,
                        "acquiring", LastTransitionTick: state.Society.Society.WorldTick)
                    : person.Project,
                LastDecisionContext = person.InhabitantId == actor ? null : person.LastDecisionContext,
                TravelCooldownTicks = person.InhabitantId == actor ? 0 : person.TravelCooldownTicks,
            }).ToArray(),
        };
        return new(state, actor, destination, cargo.Id, recipe, candidate, state.Society.Society.WorldTick,
            protectedClaims.ToArray(), inventory.Lots.Where(lot => protectedClaims.Any(claim => claim.LotId == lot.Id)).ToArray());
    }

    private static PrivateWorldRuntime Restore(PrivateWorldRuntimeState state) => PrivateWorldRuntime.Restore(state, _ => new Idle());
    private static GridPoint Position(PrivateWorldRuntime world, string actor) => world.Inhabitants.Single(person => person.InhabitantId == actor).Position;
    private static int Load(PrivateWorldRuntime world, string actor) => PersonalEquipmentRules.CarriedQuantity(world.Society.Inventory,
        actor, world.Inhabitants.Single(person => person.InhabitantId == actor).Equipment);
    private static bool CompletedTools(PrivateWorldRuntime world, Fixture fixture) => world.WorldSimulation.ProductionJobs.Any(job =>
        job.WorkerId == fixture.Actor && job.RecipeId == fixture.Recipe.CanonicalId && job.State == WorldProductionJobState.Completed);
    private static string Failure(PrivateWorldRuntime world, Fixture fixture) =>
        $"tick={world.WorldTick}; position={Position(world, fixture.Actor)}; project=" +
        world.Inhabitants.Single(person => person.InhabitantId == fixture.Actor).Project;

    private static void AssertProtectedStock(PrivateWorldRuntime world, Fixture fixture)
    {
        foreach (var claim in fixture.ProtectedClaims) Assert.Equal(claim, world.Society.Inventory.GetReservation(claim.Id));
        foreach (var lot in fixture.ProtectedLots)
            Assert.Equal(lot with { LastProcessedTick = world.WorldTick }, world.Society.Inventory.GetLot(lot.Id));
    }

    private sealed record Fixture(PrivateWorldRuntimeState State, string Actor, PlacedBuilding Destination, string CargoId,
        RecipeDefinition Recipe, string ProjectCandidate, long StartTick, InventoryReservation[] ProtectedClaims, InventoryLot[] ProtectedLots);

    private sealed class Idle : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default) =>
            new DeterministicDecisionProvider().DecideAsync(request with
            {
                Observation = request.Observation with
                {
                    Candidates = [request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle")],
                },
            }, cancellationToken);
    }
}

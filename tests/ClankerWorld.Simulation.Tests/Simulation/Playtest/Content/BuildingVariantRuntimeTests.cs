using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class BuildingVariantRuntimeTests
{
    [Theory]
    [InlineData("farmhouse", "flour", 1)]
    [InlineData("blacksmith", "wooden_hammer", 1)]
    [InlineData("tailor", "cloth", 1)]
    [InlineData("clinic", "medicine", 2)]
    [InlineData("restaurant", "porridge", 2)]
    public async Task OfferedVariantIsPaidForAndMakesRealGoodsAtItsExactWorksite(
        string family, string outputKind, int outputQuantity)
    {
        var fixture = await BuildingVariantTestWorld.BuildAsync(family);
        var building = fixture.Building;
        var recipe = fixture.State.WorldContent!.Recipes.Single(item =>
            item.WorkstationBuildingId == fixture.Definition.CanonicalId &&
            item.Outputs.Any(output => output.ResourceId == outputKind));
        fixture = WithRecipeInputs(fixture, recipe);
        var provider = new VariantActionProvider(id => id == "build:recipe:" + recipe.CanonicalId);
        using var world = BuildingVariantTestWorld.Restore(fixture, provider);
        await BuildingVariantTestWorld.UntilAsync(world, () => world.WorldSimulation.ProductionJobs.Any(job =>
            job.RecipeId == recipe.CanonicalId));
        var job = Assert.Single(world.WorldSimulation.ProductionJobs, item => item.RecipeId == recipe.CanonicalId);
        Assert.Equal((fixture.Actor, fixture.Household, building.InstanceId, WorldProductionJobState.Running),
            (job.WorkerId, job.OwnerId, job.BuildingInstanceId, job.State));
        Assert.Contains("build:recipe:" + recipe.CanonicalId, provider.Offered);
        Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.Id == job.JobId + ":output:00");
        foreach (var input in recipe.Inputs)
        {
            var reservation = Assert.Single(job.InputReservationIds.Select(world.Society.Inventory.GetReservation),
                item => item.LotId == "variant-input-" + input.ResourceId);
            Assert.Equal((fixture.Household, input.Amount, InventoryReservationState.Reserved),
                (reservation.OwnerId, reservation.Quantity, reservation.State));
        }

        // The medicine row covers a live job with contained water through strict replay.
        var saved = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var replay = family == "clinic" ? BuildingVariantTestWorld.Restore(fixture with
        { State = PrivateWorldRuntimeCodec.Decode(saved) }, new VariantActionProvider(provider.Choose)) : null;
        if (replay is not null) Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        for (var tick = 0; tick < 60 && world.WorldSimulation.ProductionJobs.Single(item => item.JobId == job.JobId)
                 .State == WorldProductionJobState.Running; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            if (replay is not null) Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
        }
        Assert.Equal(WorldProductionJobState.Completed,
            world.WorldSimulation.ProductionJobs.Single(item => item.JobId == job.JobId).State);
        var output = world.Society.Inventory.GetLot(job.JobId + ":output:00");
        Assert.Equal((outputKind, outputQuantity, fixture.Household, building.InstanceId),
            (output.ItemKind, output.Quantity, output.OwnerId, output.StorageBuildingId));
        Assert.Null(output.CarrierId);
        Assert.Null(output.GroundPosition);
        Assert.All(job.InputReservationIds, id =>
            Assert.Equal(InventoryReservationState.Completed, world.Society.Inventory.GetReservation(id).State));
        foreach (var input in recipe.Inputs.Where(input => input.ResourceId != "fresh_water"))
            Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.Id == "variant-input-" + input.ResourceId);
        if (recipe.Inputs.Any(input => input.ResourceId == "fresh_water"))
        {
            var water = world.Society.Inventory.GetLot("variant-input-fresh_water");
            Assert.Equal((3, "variant-input-jug", building.InstanceId),
                (water.Quantity, water.ContainerLotId, water.StorageBuildingId));
            Assert.Equal((1, fixture.Household, building.InstanceId),
                (world.Society.Inventory.GetLot("variant-input-jug").Quantity,
                    world.Society.Inventory.GetLot("variant-input-jug").OwnerId,
                    world.Society.Inventory.GetLot("variant-input-jug").StorageBuildingId));
        }
        if (replay is not null)
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        using var loaded = BuildingVariantTestWorld.Restore(fixture with
        { State = BuildingVariantTestWorld.Strict(world.ExportState()) }, new VariantActionProvider());
        Assert.Equal(output, loaded.Society.Inventory.GetLot(output.Id));
    }

    [Fact]
    public async Task PaidConstructionKeepsItsChosenSiteAndPaymentAcrossMidWorkReplay()
    {
        var fixture = await BuildingVariantTestWorld.CreatePreparedAsync("blacksmith");
        var provider = new VariantActionProvider(id => BuildingVariantTestWorld.IsBuildingCandidate(id, fixture.Definition.CanonicalId));
        using var world = BuildingVariantTestWorld.Restore(fixture, provider);
        await BuildingVariantTestWorld.UntilAsync(world, () => world.Inhabitants.Single(person => person.InhabitantId == fixture.Actor)
            .Project is { Stage: "working", WorkDone: > 0 and < 10 });
        var project = world.Inhabitants.Single(person => person.InhabitantId == fixture.Actor).Project!;
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var replay = BuildingVariantTestWorld.Restore(fixture with { State = PrivateWorldRuntimeCodec.Decode(before) },
            new VariantActionProvider(provider.Choose));
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        for (var tick = 0; tick < 40 && !world.WorldSimulation.Buildings.Any(item => item.DefinitionId == fixture.Definition.CanonicalId); tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
        }
        var built = Assert.Single(world.WorldSimulation.Buildings, item => item.DefinitionId == fixture.Definition.CanonicalId);
        Assert.True(TownConstructionCandidateIds.TryParse(project.CandidateId, out var selected));
        Assert.Equal(selected.SitePosition, built.Position);
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        Assert.Equal(fixture.Definition.BuildCosts.Sum(cost => cost.Amount), world.Society.Inventory.Reservations
            .Where(item => item.Purpose == "building:" + built.InstanceId && item.State == InventoryReservationState.Completed)
            .Sum(item => item.Quantity));
    }

    [Fact]
    public async Task MissingVariantMaterialsStillAllowAffordableOriginalButActiveFamilyWorkBlocksBothSizes()
    {
        var fixture = await BuildingVariantTestWorld.CreatePreparedAsync("tailor");
        var original = fixture.State.WorldContent!.Buildings.Single(item => item.Tags.Contains("tailor", StringComparer.Ordinal) &&
            item.Width == 1 && item.Height == 1);
        var inventory = fixture.State.Society.Society.Inventory;
        inventory = inventory with
        {
            Lots = inventory.Lots.Select(lot => lot.Id == "variant-build-wood" ? lot with { Quantity = lot.Quantity - 1 } : lot).ToArray(),
        };
        var recorder = new VariantActionProvider();
        using (var poor = BuildingVariantTestWorld.Restore(fixture with
        { State = BuildingVariantTestWorld.WithInventory(fixture.State, inventory) }, recorder))
        {
            await BuildingVariantTestWorld.UntilAsync(poor, () => recorder.Offered.Count > 0, 5);
            Assert.Contains(recorder.Offered, id => BuildingVariantTestWorld.IsBuildingCandidate(id, original.CanonicalId));
            Assert.DoesNotContain(recorder.Offered, id => BuildingVariantTestWorld.IsBuildingCandidate(id, fixture.Definition.CanonicalId));
        }

        var chooser = new VariantActionProvider(id => BuildingVariantTestWorld.IsBuildingCandidate(id, fixture.Definition.CanonicalId));
        using var planning = BuildingVariantTestWorld.Restore(fixture, chooser);
        await BuildingVariantTestWorld.UntilAsync(planning, () => planning.Inhabitants.Single(person => person.InhabitantId == fixture.Actor)
            .Project is { Stage: "working", WorkDone: > 0 });
        var peer = fixture.State.Society.Society.Inhabitants.First(person => person.HouseholdId == fixture.Household &&
            person.Id != fixture.Actor).Id;
        var state = planning.ExportState() with
        {
            Inhabitants = planning.Inhabitants.Select(person => person.InhabitantId == peer
                ? person with { LastDecisionContext = null } : person).ToArray(),
        };
        var peerChoices = new VariantActionProvider();
        using var competing = PrivateWorldRuntime.Restore(BuildingVariantTestWorld.Strict(state),
            id => id == peer ? peerChoices : new VariantActionProvider());
        await BuildingVariantTestWorld.UntilAsync(competing, () => peerChoices.Offered.Count > 0, 5);
        Assert.DoesNotContain(peerChoices.Offered, id => BuildingVariantTestWorld.IsBuildingCandidate(id, original.CanonicalId) ||
            BuildingVariantTestWorld.IsBuildingCandidate(id, fixture.Definition.CanonicalId));
        Assert.DoesNotContain(competing.WorldSimulation.Buildings, item => item.DefinitionId == fixture.Definition.CanonicalId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ForeignClaimOnNonAnchorTileBlocksAChosenSquarePlanWithoutSpending(bool pending)
    {
        var fixture = await BuildingVariantTestWorld.CreatePreparedAsync("blacksmith");
        var chooser = new VariantActionProvider(id => BuildingVariantTestWorld.IsBuildingCandidate(id, fixture.Definition.CanonicalId));
        using var planning = BuildingVariantTestWorld.Restore(fixture, chooser);
        await BuildingVariantTestWorld.UntilAsync(planning, () => planning.Inhabitants.Single(person => person.InhabitantId == fixture.Actor)
            .Project is { Stage: "working", WorkDone: > 0 });
        var state = planning.ExportState();
        var project = state.Inhabitants.Single(person => person.InhabitantId == fixture.Actor).Project!;
        Assert.True(TownConstructionCandidateIds.TryParse(project.CandidateId, out var selected));
        var anchor = selected.SitePosition!.Value;
        var tile = WorldContentSimulationRules.Footprint(fixture.Definition, anchor).Last();
        Assert.NotEqual(anchor, tile);
        var town = state.Towns!.Single();
        var titles = state.TownLandTitles!.ToList();
        if (!titles.Any(title => title.Tiles.Contains(tile)))
            titles.Add(new("variant-site-title", town.Id, [tile], state.Society.Society.WorldTick));
        state = state with
        {
            Towns = [town with { BorderTiles = TownLandRightsRules.OrderTiles(town.BorderTiles.Concat(
                WorldContentSimulationRules.Footprint(fixture.Definition, anchor)).Distinct()) }],
            TownLandTitles = titles.OrderBy(title => title.Id, StringComparer.Ordinal).ToArray(),
        };
        // Permission is a fixture precondition; grant/vote/consent history has its own tests.
        // A pending request (not a grant) must exclude the other household just as a right does.
        if (pending)
        {
            var requester = state.Society.Society.Inhabitants.First(person => person.HouseholdId == "household:camp-beta").Id;
            state = state with
            {
                HouseholdLandUseRequests = state.HouseholdLandUseRequests!.Append(new HouseholdLandUseRequest(
                "variant-rival-request", town.Id, "household:camp-beta", requester, [tile], state.Society.Society.WorldTick)).ToArray()
            };
        }
        else
            state = state with
            {
                HouseholdLandUseRights = state.HouseholdLandUseRights!.Append(new HouseholdLandUseRight(
                "variant-rival-right", town.Id, "household:camp-beta", [tile], state.Society.Society.WorldTick,
                "variant_test_fixture")).ToArray()
            };
        using var blocked = BuildingVariantTestWorld.Restore(fixture with { State = BuildingVariantTestWorld.Strict(state) },
            new VariantActionProvider());
        Assert.True((await blocked.AdvanceOneTickAsync()).Advanced);
        Assert.DoesNotContain(blocked.WorldSimulation.Buildings, item => item.DefinitionId == fixture.Definition.CanonicalId);
        var after = blocked.Inhabitants.Single(person => person.InhabitantId == fixture.Actor).Project!;
        Assert.Equal("blocked", after.Stage);
        Assert.Contains("selected site is no longer legal", after.Blocker, StringComparison.Ordinal);
        Assert.Equal(project.WorkDone, after.WorkDone);
        // Inventory maintenance advances this timestamp even when construction spends nothing.
        Assert.Equal(state.Society.Society.Inventory.Lots.Where(lot => lot.Id.StartsWith("variant-build-", StringComparison.Ordinal))
                .Select(lot => lot with { LastProcessedTick = blocked.WorldTick }),
            blocked.Society.Inventory.Lots.Where(lot => lot.Id.StartsWith("variant-build-", StringComparison.Ordinal)));
        Assert.Equal(state.Society.Society.Inventory.Reservations, blocked.Society.Inventory.Reservations);
        BuildingVariantTestWorld.Strict(blocked.ExportState());
    }

    [Fact]
    public async Task VariantWorkKeepsExactRecipeHouseholdOnsiteInputsAndOneWorkerBoundaries()
    {
        var fixture = await BuildingVariantTestWorld.BuildAsync("clinic");
        var recipe = fixture.State.WorldContent!.Recipes.Single(item => item.WorkstationBuildingId == fixture.Definition.CanonicalId);
        fixture = WithRecipeInputs(fixture, recipe);
        var foreign = fixture.State.Society.Society.Inhabitants.First(person => person.HouseholdId != fixture.Household).Id;
        var originalRecipe = fixture.State.WorldContent!.Recipes.Single(item => item.LocalId == "clinic-medicine");
        using var world = BuildingVariantTestWorld.Restore(fixture, new VariantActionProvider());
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        var outsider = world.StartProduction(recipe.CanonicalId, fixture.Building.InstanceId, foreign);
        Assert.False(outsider.Applied);
        Assert.Equal("Only a member of the building's household can work there.", outsider.Failure);
        var wrongSize = world.StartProduction(originalRecipe.CanonicalId, fixture.Building.InstanceId, fixture.Actor);
        Assert.False(wrongSize.Applied);
        Assert.Equal("The placed building is not a valid workstation for this recipe.", wrongSize.Failure);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));

        var house = fixture.State.WorldSimulation!.Buildings.Single(item => item.HouseholdId == fixture.Household &&
            fixture.State.WorldContent!.Buildings.Single(definition => definition.CanonicalId == item.DefinitionId)
                .Tags.Contains("house", StringComparer.Ordinal));
        var remote = fixture.State.Society.Society.Inventory with
        {
            Lots = fixture.State.Society.Society.Inventory.Lots.Select(lot => lot.ItemKind == "fresh_water" ||
                lot.Id == "variant-input-jug" ? lot with { StorageBuildingId = house.InstanceId } : lot).ToArray(),
        };
        using (var unstocked = BuildingVariantTestWorld.Restore(fixture with
        { State = BuildingVariantTestWorld.Strict(BuildingVariantTestWorld.WithInventory(fixture.State, remote)) },
               new VariantActionProvider()))
        {
            var untouched = PrivateWorldRuntimeCodec.Encode(unstocked.ExportState());
            var refused = unstocked.StartProduction(recipe.CanonicalId, fixture.Building.InstanceId, fixture.Actor);
            Assert.False(refused.Applied);
            Assert.Contains("on-site", refused.Failure, StringComparison.Ordinal);
            Assert.Equal(untouched, PrivateWorldRuntimeCodec.Encode(unstocked.ExportState()));
        }
        var started = world.StartProduction(recipe.CanonicalId, fixture.Building.InstanceId, fixture.Actor);
        Assert.True(started.Applied, started.Failure);
        var occupied = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        var second = world.StartProduction(recipe.CanonicalId, fixture.Building.InstanceId, fixture.Actor);
        Assert.False(second.Applied);
        Assert.Contains("capacity", second.Failure, StringComparison.Ordinal);
        Assert.Equal(occupied, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
    }

    internal static BuildingVariantFixture WithRecipeInputs(BuildingVariantFixture fixture, RecipeDefinition recipe)
    {
        var state = fixture.State;
        var inventory = state.Society.Society.Inventory;
        foreach (var input in recipe.Inputs)
        {
            if (input.ResourceId == "fresh_water")
                inventory = InventoryFixture.AddLot(inventory, "variant-input-jug", InventoryContainerRules.WaterJug, fixture.Household, 1,
                    storageBuildingId: fixture.Building.InstanceId);
            inventory = InventoryFixture.AddLot(inventory, "variant-input-" + input.ResourceId, input.ResourceId,
                fixture.Household, input.ResourceId == "fresh_water" ? 4 : input.Amount,
                storageBuildingId: fixture.Building.InstanceId,
                containerLotId: input.ResourceId == "fresh_water" ? "variant-input-jug" : null);
        }
        return fixture with
        {
            State = BuildingVariantTestWorld.Strict(BuildingVariantTestWorld.WithInventory(state, inventory) with
            {
                Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == fixture.Actor
                    ? person with { LastDecisionContext = null, Project = null, HungerBasisPoints = 10_000 }
                    : person).ToArray(),
            }),
        };
    }
}

using System.Text.Json;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class HouseholdLandGrantTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NewRoadsAndStreetRunOnAvoidHeldAndRequestedLand(bool runOn)
    {
        using var generated = await TownBridgeRuntimeTests.GrowthWorldAsync();
        var baseline = generated.ExportState();
        var choices = new Choices();
        using var source = PrivateWorldRuntime.Restore(baseline with
        {
            Inhabitants = baseline.Inhabitants.Select(person => person with { Position = baseline.Towns![0].OriginSite!.Value }).ToArray(),
        }, _ => new Provider(choices));
        var before = source.ExportState();
        var buildingId = runOn ? "bridge-run-on" : "bridge-growth";
        var buildingSite = runOn ? new GridPoint(50, 41) : new GridPoint(53, 41);
        var workshop = source.WorldContent.Buildings.Single(definition => definition.LocalId == "workshop");
        using var control = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(before)));
        var controlPlacement = control.PlaceBuilding(buildingId, workshop.CanonicalId, buildingSite);
        Assert.True(controlPlacement.Applied, controlPlacement.Failure);
        var newRoads = control.RoadTiles.Except(source.RoadTiles).ToArray();
        Assert.NotEmpty(newRoads);
        var newKinds = control.ExportState().Events.Skip(before.Events.Count).Select(item => item.Kind).ToArray();
        Assert.Contains("town_road_extended", newKinds);
        if (runOn) Assert.DoesNotContain("town_road_generated", newKinds);
        else Assert.Contains("town_road_generated", newKinds);
        var ownFootprint = WorldContentSimulationRules.Footprint(workshop, buildingSite).ToHashSet();
        var protectedCandidates = newRoads.Where(tile => !ownFootprint.Contains(tile) &&
            before.TownLandTitles!.Any(title => title.TownId == source.Towns[0].Id && title.Tiles.Contains(tile)) &&
            !before.HouseholdLandUseRights!.Any(right => right.Tiles.Contains(tile))).ToArray();
        Assert.True(protectedCandidates.Length > 0,
            "The unprotected control must lay at least one previously free, already titled Road tile: " + string.Join(",", newRoads));
        var point = protectedCandidates.OrderBy(tile => tile.Y).ThenBy(tile => tile.X).First();
        var applicant = source.Towns[0].ResidentIds[0];
        var requested = source.RequestHouseholdLandUse("land-boundary-road", applicant, source.Towns[0].Id, [point]);
        Assert.True(requested.Applied, requested.Failure);
        if (!runOn) await GrantLandForBoundaryTest(source, choices, applicant, "land-boundary-road");
        else Assert.Equal("pending", Assert.Single(source.HouseholdLandUseRequests).Status);
        using var protectedWorld = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(
            PrivateWorldRuntimeCodec.Encode(source.ExportState())));
        var rightsBefore = protectedWorld.HouseholdLandUseRights;
        var requestsBefore = protectedWorld.HouseholdLandUseRequests;
        var roadsBefore = protectedWorld.RoadTiles.ToHashSet();
        Assert.DoesNotContain(point, roadsBefore);
        var result = protectedWorld.PlaceBuilding(buildingId, workshop.CanonicalId, buildingSite);
        Assert.True(result.Applied, result.Failure);
        Assert.Equal(rightsBefore, protectedWorld.HouseholdLandUseRights);
        Assert.Equal(requestsBefore, protectedWorld.HouseholdLandUseRequests);
        Assert.True(roadsBefore.IsSubsetOf(protectedWorld.RoadTiles));
        var after = PrivateWorldRuntimeCodec.Encode(protectedWorld.ExportState());
        using var reloaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(after));
        Assert.Equal(after, PrivateWorldRuntimeCodec.Encode(reloaded.ExportState()));
        Assert.True(!protectedWorld.RoadTiles.Contains(point),
            $"{(runOn ? "street run-on over pending claim" : "new route over granted land")} laid a new Road at {point}.");
    }

    [Theory]
    [InlineData("unclaimed", true)]
    [InlineData("own-pending", true)]
    [InlineData("own-granted", true)]
    [InlineData("foreign-pending", false)]
    [InlineData("foreign-granted", false)]
    public async Task TillingRespectsPendingAndGrantedHouseholdLand(string protection, bool permitted)
    {
        var choices = new Choices();
        using var source = Create("household-grant", choices);
        var (farmer, household, point) = FarmerAndUnclaimedTitledPlot(source.ExportState());
        if (protection != "unclaimed")
        {
            var applicant = protection.StartsWith("own-", StringComparison.Ordinal) ? farmer :
                source.Society.Inhabitants.First(person => person.HouseholdId != household).Id;
            var requested = source.RequestHouseholdLandUse("land-boundary-land", applicant, source.Towns[0].Id, [point]);
            Assert.True(requested.Applied, requested.Failure);
            if (protection.EndsWith("granted", StringComparison.Ordinal))
                await GrantLandForBoundaryTest(source, choices, applicant, "land-boundary-land");
            else
                Assert.Equal("pending", Assert.Single(source.HouseholdLandUseRequests).Status);
            Assert.Equal(permitted, source.Society.GetInhabitant(applicant).HouseholdId == household);
        }
        using var world = FarmFieldTests.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(
            EquipFarmerForLandTest(source.ExportState(), farmer, point))));
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        var result = world.StartFieldWork(farmer, point, FarmWorkKind.Till);
        Assert.True(result.Accepted == permitted,
            $"{protection}: expected till accepted={permitted}, actual={result.Accepted}; {result.Message}");
        if (!permitted)
        {
            Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
            return;
        }
        await FinishTillingForLandTest(world, household, point);
        Assert.True(world.Society.Inventory.GetLot("land-boundary-carried-hoe").ConditionBasisPoints < 10_000);
        var completed = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var reloaded = FarmFieldTests.Restore(PrivateWorldRuntimeCodec.Decode(completed));
        Assert.Equal(completed, PrivateWorldRuntimeCodec.Encode(reloaded.ExportState()));
        if (protection == "own-granted")
        {
            // A recorded-right dispute stays a dispute even when its holder has farmed the plot.
            var challenger = reloaded.Society.Inhabitants.First(person => person.HouseholdId != household).Id;
            var disputed = reloaded.RequestHouseholdLandUse("worked-right-dispute", challenger, reloaded.Towns[0].Id, [point]);
            Assert.True(disputed.Applied, disputed.Failure);
            Assert.True(disputed.IsDisputed);
            Assert.Null(disputed.Request!.CouncilProposalId);
            Assert.Equal("pending", disputed.Request.Status);
            Assert.Equal(world.Fields, reloaded.Fields);
            Assert.Equal(JsonSerializer.Serialize(world.HouseholdLandUseRights), JsonSerializer.Serialize(reloaded.HouseholdLandUseRights));
            var disputeSave = PrivateWorldRuntimeCodec.Encode(reloaded.ExportState());
            using var disputedReload = FarmFieldTests.Restore(PrivateWorldRuntimeCodec.Decode(disputeSave));
            Assert.Equal(disputeSave, PrivateWorldRuntimeCodec.Encode(disputedReload.ExportState()));
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LandRequestsCannotClaimAnotherHouseholdsWorkedField(bool sameHousehold)
    {
        var choices = new Choices();
        using var initial = Create("household-grant", choices);
        var (farmer, household, point) = FarmerAndUnclaimedTitledPlot(initial.ExportState());
        using var tilled = FarmFieldTests.Restore(EquipFarmerForLandTest(initial.ExportState(), farmer, point));
        var started = tilled.StartFieldWork(farmer, point, FarmWorkKind.Till);
        Assert.True(started.Accepted, started.Message);
        await FinishTillingForLandTest(tilled, household, point);
        Assert.True(tilled.Society.Inventory.GetLot("land-boundary-carried-hoe").ConditionBasisPoints < 10_000);
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(
            PrivateWorldRuntimeCodec.Encode(tilled.ExportState())), _ => new Provider(choices));
        var applicant = sameHousehold ? farmer : world.Society.Inhabitants.First(person => person.HouseholdId != household).Id;
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        var request = world.RequestHouseholdLandUse("land-boundary-field", applicant, world.Towns[0].Id, [point]);
        Assert.Equal(sameHousehold, request.Applied);
        if (sameHousehold)
            Assert.Equal("pending", Assert.Single(world.HouseholdLandUseRequests).Status);
        else
            Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
    }

    [Fact]
    public async Task HistoricalForeignFieldBlocksAnOtherwiseApprovedLandGrant()
    {
        var choices = new Choices();
        using var initial = Create("household-grant", choices);
        var (farmer, household, point) = FarmerAndUnclaimedTitledPlot(initial.ExportState());
        using var tilled = FarmFieldTests.Restore(EquipFarmerForLandTest(initial.ExportState(), farmer, point));
        Assert.True(tilled.StartFieldWork(farmer, point, FarmWorkKind.Till).Accepted);
        await FinishTillingForLandTest(tilled, household, point);
        var worked = tilled.ExportState();
        Assert.True(tilled.Society.Inventory.GetLot("land-boundary-carried-hoe").ConditionBasisPoints < 10_000);

        // Before #885, filing ignored fields and could create this saved combination.
        // Replay that admission in a fieldless clone, then retain the real worked field.
        // The request, proposal and notices are native; votes and consents are still absent.
        using var admission = PrivateWorldRuntime.Restore(worked with { Fields = [] }, _ => new Provider(choices));
        var applicant = admission.Society.Inhabitants.First(person => person.HouseholdId != household).Id;
        const string requestId = "historical-foreign-field";
        var filing = admission.RequestHouseholdLandUse(requestId, applicant, admission.Towns[0].Id, [point]);
        Assert.True(filing.Applied, filing.Failure);
        var historical = admission.ExportState() with { Fields = worked.Fields };
        Assert.Equal("pending", Assert.Single(historical.HouseholdLandUseRequests!).Status);
        Assert.Empty(historical.HouseholdLandUseRequests![0].Consents);
        Assert.Empty(historical.Towns![0].Governance!.Proposals.Single().Votes);
        var saved = PrivateWorldRuntimeCodec.Encode(historical);
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved), _ => new Provider(choices));
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(world.ExportState()));

        var applicantsHousehold = world.Society.GetInhabitant(applicant).HouseholdId;
        choices.Voters.UnionWith(world.Towns[0].ResidentIds);
        choices.Acceptors.UnionWith(world.Society.Inhabitants.Where(person => person.HouseholdId == applicantsHousehold).Select(person => person.Id));
        Refresh(world, world.Towns[0].ResidentIds);
        await Until(world, () => world.Towns[0].Governance!.Proposals.Single().Status == "passed" &&
            choices.Acceptors.All(actor => world.HouseholdLandUseRequests.Single().Consents.Any(consent => consent.AgentId == actor && consent.Accepted)));
        Assert.Equal("pending", Assert.Single(world.HouseholdLandUseRequests).Status);
        Assert.Equal(worked.Fields, world.Fields);
        Assert.Equal(JsonSerializer.Serialize(worked.HouseholdLandUseRights), JsonSerializer.Serialize(world.HouseholdLandUseRights));
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "land_use_granted");

        using var reloaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(
            PrivateWorldRuntimeCodec.Encode(world.ExportState())), _ => new Provider(choices));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.True((await reloaded.AdvanceOneTickAsync()).Advanced);
        Assert.Equal("pending", Assert.Single(world.HouseholdLandUseRequests).Status);
        Assert.Equal(worked.Fields, world.Fields);
        Assert.Equal(JsonSerializer.Serialize(worked.HouseholdLandUseRights), JsonSerializer.Serialize(world.HouseholdLandUseRights));
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(reloaded.ExportState()));
    }

    private static (string Farmer, string Household, GridPoint Point) FarmerAndUnclaimedTitledPlot(PrivateWorldRuntimeState state)
    {
        var farmhouse = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == "first-town-farmhouse");
        var household = farmhouse.HouseholdId!;
        var farmer = state.Society.Society.Inhabitants.First(person => person.HouseholdId == household).Id;
        var definitions = state.WorldContent!.Buildings.ToDictionary(definition => definition.CanonicalId, StringComparer.Ordinal);
        var occupied = state.WorldSimulation.Buildings.SelectMany(building => WorldContentSimulationRules.Footprint(definitions[building.DefinitionId], building))
            .Concat(state.Map.Resources.Select(resource => resource.Position)).Concat(state.RoadTiles!)
            .Concat(state.Bridges!.SelectMany(bridge => bridge.Entrances.Concat(bridge.Span)))
            .Concat(state.Map.CampObjects.Select(item => item.Position)).Concat(state.Fields!.Select(field => field.Position)).ToHashSet();
        Assert.Empty(state.WorldSimulation.BuildingExpansions ?? []);
        var fertility = new LandFertility(state.Map, state.WorldSeed);
        var point = state.TownLandTitles!.Where(title => title.TownId == state.Towns![0].Id).SelectMany(title => title.Tiles)
            .Where(tile => fertility.CanFarm(tile) && !occupied.Contains(tile) &&
                !state.HouseholdLandUseRights!.Any(right => right.Tiles.Contains(tile)))
            .OrderBy(tile => state.Map.FootDistance(farmhouse.Position, tile)).ThenBy(tile => tile.Y).ThenBy(tile => tile.X).First();
        return (farmer, household, point);
    }

    private static PrivateWorldRuntimeState EquipFarmerForLandTest(PrivateWorldRuntimeState state, string farmer, GridPoint point) =>
        FarmFieldTests.WithInventory(state, InventoryFixture.AddLot(state.Society.Society.Inventory,
            "land-boundary-carried-hoe", "wooden_hoe", farmer, 1, state.Society.Society.WorldTick)) with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == farmer ? person with
            {
                Position = point,
                HungerBasisPoints = 10_000,
                Survival = person.Survival is { } survival ? survival with { WarmthBasisPoints = 10_000 } : null,
            } : person).ToArray(),
        };

    private static async Task FinishTillingForLandTest(PrivateWorldRuntime world, string household, GridPoint point)
    {
        for (var tick = 0; tick < 8 && world.Fields.Single(field => field.Position == point).Stage != FarmFieldStage.Prepared; tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var field = Assert.Single(world.Fields, field => field.Position == point);
        Assert.Equal(FarmFieldStage.Prepared, field.Stage);
        Assert.Equal(household, field.HouseholdId);
        Assert.Null(field.Work);
        Assert.Contains(world.ExportState().Events, item => item.Kind == "field_prepared");
    }

    private static async Task GrantLandForBoundaryTest(PrivateWorldRuntime world, Choices choices, string applicant, string requestId)
    {
        var household = world.Society.GetInhabitant(applicant).HouseholdId!;
        choices.Voters.UnionWith(world.Towns[0].ResidentIds);
        choices.Acceptors.UnionWith(world.Society.Inhabitants.Where(person => person.HouseholdId == household).Select(person => person.Id));
        Refresh(world, world.Towns[0].ResidentIds);
        await Until(world, () => world.HouseholdLandUseRequests.Single(request => request.Id == requestId).Status == "granted");
        var request = world.HouseholdLandUseRequests.Single(request => request.Id == requestId);
        Assert.Equal("passed", world.Towns[0].Governance!.Proposals.Single(proposal => proposal.Id == request.CouncilProposalId).Status);
        Assert.Equal(choices.Acceptors.Order(), request.GrantAdults);
        var right = Assert.Single(world.HouseholdLandUseRights, item => item.Id == HouseholdLandGrantRules.RightId(requestId));
        Assert.Equal(household, right.HouseholdId);
        Assert.Equal(request.Tiles, right.Tiles);
    }
}

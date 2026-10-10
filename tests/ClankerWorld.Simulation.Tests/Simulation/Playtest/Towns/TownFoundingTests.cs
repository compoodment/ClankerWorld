using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;
using System.Text.Json;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class TownMembershipTests
{
    private const string FoundTownChoice = "civic|town:site:";

    [Fact]
    public async Task AnAdultFoundsATownWithoutFreePropertyAndMovesItsCareGroupAcrossReplayAndReload()
    {
        var founder = Founders[0];
        var (state, children) = WithChildren(Generated("town-found-care"), founder, Founders[1], 2);
        state = WithTowns(state, Founders.Concat(children), null);
        var site = NewTownSite(state);
        state = Calm(At(state, site, founder));
        var bytes = PrivateWorldRuntimeCodec.Encode(state);
        var model = FoundingModel(founder);
        using var world = Reopen(state, model);
        using var replay = Reopen(PrivateWorldRuntimeCodec.Decode(bytes), FoundingModel(founder));
        bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False((await world.AdvanceOneTickAsync(commitPermitted: () => false)).Advanced);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
        var founded = Assert.Single(world.Towns, town => town.Id != First);
        Assert.Equal(site, founded.OriginSite);
        Assert.Equal("founded", founded.FoundingState);
        Assert.Equal(new[] { founder }.Concat(children).Order(StringComparer.Ordinal), founded.ResidentIds);
        Assert.Equal([founder], founded.Governance!.Members);
        Assert.NotNull(founded.Government);
        Assert.DoesNotContain(world.Towns.Single(town => town.Id == First).ResidentIds,
            id => id == founder || children.Contains(id));
        Assert.Equal(JsonSerializer.Serialize(state.WorldSimulation!.Buildings), JsonSerializer.Serialize(world.WorldSimulation.Buildings));
        Assert.Equal(state.Society.Society.Inventory.Lots.Select(lot => (lot.Id, lot.OwnerId, lot.Quantity)),
            world.Society.Inventory.Lots.Select(lot => (lot.Id, lot.OwnerId, lot.Quantity)));
        Assert.Equal(JsonSerializer.Serialize(state.HouseholdLandUseRights),
            JsonSerializer.Serialize(world.ExportState().HouseholdLandUseRights));
        Assert.Equal(founded.BorderTiles, TownLandRightsRules.OrderTiles(world.ExportState().TownLandTitles!
            .Where(title => title.TownId == founded.Id).SelectMany(title => title.Tiles)));
        foreach (var child in children)
        {
            Assert.Equal(founder, world.Society.GetInhabitant(child).PrimaryCaregiverId);
            Assert.Equal(state.Inhabitants.Single(person => person.InhabitantId == child).Position,
                world.Inhabitants.Single(person => person.InhabitantId == child).Position);
        }
        Assert.Single(world.ExportState().Events, item => item.Kind == "town_founded" && item.Detail.StartsWith(founded.Id, StringComparison.Ordinal));
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        AssertRoundTrip(world);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClaimedLandAndSitesWhoseInitialLayoutTouchesAnotherTownRejectInventedFounding(bool besideTown)
    {
        var state = Generated("town-found-refused");
        var first = Town(state, First);
        var site = besideTown ? state.Map.Tiles.Select(tile => tile.Position).First(point =>
            state.Map.IsBuildable(point) && !first.BorderTiles.Contains(point) && !Taken(state).Contains(point) &&
            TownBorderRules.Around(state.Map, [point]).Any(first.BorderTiles.Contains)) : first.OriginSite!.Value;
        var model = FoundingModel(Founders[0]);
        model.Scripts[Founders[0]] = [FoundTownChoice, "!civic|town:site:invented|found|0,0|"];
        using var world = Reopen(Calm(At(state, site, Founders[0])), model);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Single(world.Towns);
        Assert.DoesNotContain(model.ObservationsOf(Founders[0]).SelectMany(observation => observation.Candidates),
            candidate => candidate.Id.StartsWith(FoundTownChoice, StringComparison.Ordinal));
        Assert.DoesNotContain(world.ExportState().Events,
            item => item.Kind == "town_founded" && item.Detail.StartsWith("town:site:", StringComparison.Ordinal));
        AssertRoundTrip(world);
    }

    [Fact]
    public async Task CompetingFoundersCannotClaimOverlappingLandAndLaterVisitorsNeedAdmission()
    {
        var state = Generated("town-found-competing");
        var site = NewTownSite(state);
        var occupied = Taken(state).Concat(state.Towns!.SelectMany(town => town.BorderTiles)).ToHashSet();
        var neighbor = state.Map.Tiles.Select(tile => tile.Position).First(point => point != site &&
            state.Map.IsBuildable(point) && state.Map.FootDistance(site, point) == 1 &&
            TownBorderRules.Around(state.Map, [point]).All(tile => !occupied.Contains(tile)));
        state = Calm(At(At(state, site, Founders[0]), neighbor, Founders[1]));
        var model = FoundingModel(Founders[0], Founders[1]);
        using var world = Reopen(state, model);
        for (var tick = 0; tick < 3; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var town = Assert.Single(world.Towns, item => item.Id != First);
        var founder = Assert.Single(town.ResidentIds);
        Assert.Contains(founder, Founders[..2]);
        Assert.Single(world.ExportState().Events, item => item.Kind == "town_founded" && item.Detail.StartsWith(town.Id, StringComparison.Ordinal));
        var titles = world.ExportState().TownLandTitles!;
        Assert.Equal(titles.Sum(title => title.Tiles.Count), titles.SelectMany(title => title.Tiles).Distinct().Count());
        var visitor = Founders[2];
        var visitModel = new ScriptedModel();
        using var visiting = Reopen(Calm(At(world.ExportState(), town.OriginSite!.Value, visitor)), visitModel);
        Assert.True((await visiting.AdvanceOneTickAsync()).Advanced);
        Assert.DoesNotContain(visitor, visiting.Towns.Single(item => item.Id == town.Id).ResidentIds);
        Assert.Contains(visitModel.ObservationsOf(visitor).SelectMany(observation => observation.Candidates),
            candidate => candidate.Id == Civic(town.Id, "admission") + "|");
        AssertRoundTrip(visiting);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FoundingPreservesAnExistingOwnedHouseAndRejectsAnotherHouseholdsHouse(bool ownsHouse)
    {
        var state = Generated("town-found-house");
        var site = NewTownSite(state);
        var definition = state.WorldContent!.Buildings.Single(item => item.LocalId == "house-1x1");
        var owner = ownsHouse ? Alpha : state.Society.Society.GetInhabitant(Founders[2]).HouseholdId!;
        var inventory = state.Society.Society.Inventory;
        foreach (var cost in definition.BuildCosts)
            inventory = ClankerWorld.Simulation.Kernel.InventoryFixture.AddLot(inventory,
                "found-house-cost:" + cost.ResourceId, cost.ResourceId, owner, cost.Amount);
        using (var placing = Reopen(WithInventory(state, inventory), new ScriptedModel()))
        {
            Assert.True(placing.PlaceBuilding("founding-existing-house", definition.CanonicalId, site, owner).Applied);
            state = placing.ExportState();
        }
        var house = Assert.Single(state.WorldSimulation!.Buildings, building => building.InstanceId == "founding-existing-house");
        Assert.Null(house.TownId);
        var model = FoundingModel(Founders[0]);
        model.Scripts[Founders[0]] = [FoundTownChoice, "!civic|town:site:invented|found||"];
        using var world = Reopen(Calm(At(state, site, Founders[0])), model);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        if (!ownsHouse)
        {
            Assert.Single(world.Towns);
            Assert.Equal(house, world.WorldSimulation.Buildings.Single(building => building.InstanceId == house.InstanceId));
            Assert.DoesNotContain(model.ObservationsOf(Founders[0]).SelectMany(observation => observation.Candidates),
                candidate => candidate.Id.StartsWith(FoundTownChoice, StringComparison.Ordinal));
            AssertRoundTrip(world);
            return;
        }
        var town = Assert.Single(world.Towns, item => item.Id != First);
        var joined = Assert.Single(world.WorldSimulation.Buildings, building => building.InstanceId == house.InstanceId);
        Assert.Equal(town.FoundedTick, town.FirstBuildingCompletedTick);
        var link = Assert.Single(world.ExportState().Events, item =>
            item.Kind is "town_road_linked" or "town_road_link_unconnected" &&
            item.Detail.StartsWith(town.Id + "|" + First + "|", StringComparison.Ordinal));
        Assert.True(link.WorldTick >= town.FoundedTick);
        // The completed adopted House is the new Town's first building;
        // its Road may choose a door, without changing the building or owner.
        Assert.Equal(house with { TownId = town.Id, Entrance = joined.Entrance }, joined);
        Assert.Equal(state.WorldSimulation.Buildings.Count, world.WorldSimulation.Buildings.Count);
        Assert.Equal(state.Society.Society.Inventory.Lots.Select(lot => (lot.Id, lot.OwnerId, lot.Quantity)),
            world.Society.Inventory.Lots.Select(lot => (lot.Id, lot.OwnerId, lot.Quantity)));
        Assert.Contains(world.ExportState().HouseholdLandUseRights!, right =>
            right.TownId == town.Id && right.HouseholdId == Alpha && right.Tiles.Contains(site));
        AssertRoundTrip(world);
    }

    [Fact]
    public async Task ADelayedFoundingReplyCannotClaimLandAfterAnotherAdultFoundsThere()
    {
        var actor = Founders[0];
        var settler = Founders[1];
        var state = Generated("town-delayed-founding");
        var site = NewTownSite(state);
        var model = new DeferredTownFoundingModel(actor);
        model.OtherChoices.Scripts[settler] = [FoundTownChoice];
        using var world = Reopen(Calm(At(state, site, actor, settler)), model);
        var deadline = TimeSpan.FromSeconds(10);
        for (var tick = 0; tick < 12 && (model.Pending is null || world.Towns.Count == 1); tick++)
        {
            Assert.True((await world.AdvanceOneTickNonBlockingAsync().AsTask().WaitAsync(deadline)).Advanced);
            await Task.Delay(10);
        }
        Assert.NotNull(model.Pending);
        var town = Assert.Single(world.Towns, item => item.Id != First);
        Assert.Equal([settler], town.ResidentIds);
        model.Resolve();
        var completed = false;
        for (var tick = 0; tick < 10 && !completed; tick++)
        {
            await Task.Delay(10);
            var step = await world.AdvanceOneTickNonBlockingAsync().AsTask().WaitAsync(deadline);
            Assert.True(step.Advanced);
            completed = step.Decisions.Any(decision => decision.InhabitantId == actor && decision.Admission.FellBack) ||
                step.Events.Any(item => item.Kind == "hosted_decision_discarded" && item.Detail == actor);
        }
        Assert.True(completed, "The host must process and refuse the previously valid founding reply after title is claimed.");
        Assert.Equal(2, world.Towns.Count);
        Assert.Equal([settler], world.Towns.Single(item => item.Id == town.Id).ResidentIds);
        Assert.Contains(actor, world.Towns.Single(item => item.Id == First).ResidentIds);
        Assert.Single(world.ExportState().Events, item => item.Kind == "town_founded" && item.Detail.StartsWith(town.Id, StringComparison.Ordinal));
        AssertRoundTrip(world);
    }

    private sealed class DeferredTownFoundingModel(string actor) : IDecisionProvider
    {
        private readonly TaskCompletionSource<CognitionDecisionResponse> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ScriptedModel OtherChoices { get; } = new();
        public CognitionDecisionRequest? Pending { get; private set; }
        public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
        public long ProviderEpoch => 1;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            if (request.Observation.InhabitantId != actor || Pending is not null ||
                !request.Observation.Candidates.Any(candidate => candidate.Id.StartsWith(FoundTownChoice, StringComparison.Ordinal)))
                return OtherChoices.DecideAsync(request, cancellationToken);
            Pending = request;
            return new(completion.Task.WaitAsync(cancellationToken));
        }
        public void Resolve()
        {
            var request = Pending!;
            var observation = request.Observation;
            var selected = observation.Candidates.Single(candidate => candidate.Id.StartsWith(FoundTownChoice, StringComparison.Ordinal)).Id;
            var scores = observation.Candidates.ToDictionary(candidate => candidate.Id, _ => 0d, StringComparer.Ordinal);
            scores[selected] = 1;
            completion.TrySetResult(new(request.RequestId, actor, Kind, ProviderEpoch, observation.RunEpoch,
                observation.DecisionGeneration, observation.ObservationDigest, selected, 1, scores));
        }
    }

    private static ScriptedModel FoundingModel(params string[] founders)
    {
        var model = new ScriptedModel();
        foreach (var founder in founders) model.Scripts[founder] = [FoundTownChoice];
        return model;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FoundingRetainsOwnedFieldUseAndRefusesForeignFields(bool ownsField)
    {
        var state = Generated("town-found-field");
        var occupied = Taken(state).Concat(state.Towns!.SelectMany(town => town.BorderTiles)).ToHashSet();
        var fertility = new LandFertility(state.Map, state.WorldSeed);
        var site = state.Map.Tiles.Select(tile => tile.Position).First(point => fertility.CanFarm(point) &&
            TownBorderRules.Around(state.Map, [point]).All(tile => !occupied.Contains(tile)));
        var owner = ownsField ? Alpha : state.Society.Society.GetInhabitant(Founders[2]).HouseholdId!;
        var field = new FarmFieldState(site, owner, FarmFieldStage.Prepared);
        state = Calm(At(state with { Fields = [field] }, site, Founders[0]));
        var model = FoundingModel(Founders[0]);
        model.Scripts[Founders[0]] = [FoundTownChoice, "!civic|town:site:invented|found||"];
        using var world = Reopen(state, model);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal([field], world.Fields);
        if (ownsField)
        {
            var town = Assert.Single(world.Towns, item => item.Id != First);
            Assert.Contains(world.HouseholdLandUseRights, right => right.TownId == town.Id &&
                right.HouseholdId == owner && right.Tiles.Contains(site));
        }
        else
        {
            Assert.Single(world.Towns);
            Assert.DoesNotContain(model.ObservationsOf(Founders[0]).SelectMany(observation => observation.Candidates),
                candidate => candidate.Id.StartsWith(FoundTownChoice, StringComparison.Ordinal));
        }
        AssertRoundTrip(world);
    }

    private static GridPoint NewTownSite(PrivateWorldRuntimeState state)
    {
        var occupied = Taken(state).Concat(state.Towns!.SelectMany(town => town.BorderTiles))
            .Concat(state.TownLandTitles!.SelectMany(title => title.Tiles)).ToHashSet();
        return state.Map.Tiles.Select(tile => tile.Position).First(site => state.Map.IsBuildable(site) &&
            TownBorderRules.Around(state.Map, [site]).All(tile => !occupied.Contains(tile)));
    }
}

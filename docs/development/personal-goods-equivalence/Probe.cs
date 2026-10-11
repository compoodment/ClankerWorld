// Portable native feature probe: compile this unchanged against both checkouts.
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text.Json;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.World;

var output = args[0];
Directory.CreateDirectory(output);
using var generated = CreateGenerated("personal-goods-query-equivalence", new Choices());
if (!(await generated.AdvanceOneTickAsync()).Advanced) throw new InvalidOperationException("Initial settlement activation failed.");
var initial = generated.ExportState();
var actor = initial.Society.Society.Inhabitants.First(person => person.HouseholdId == "household:camp-alpha").Id;
var household = initial.Society.Society.GetInhabitant(actor).HouseholdId!;
var house = initial.WorldSimulation!.Buildings.Single(building => building.InstanceId == "first-town-house-a");
foreach (var order in new[] { false, true })
foreach (var kind in new[] { "wood", "fruit", "storage_pot" })
{
    var inventory = initial.Society.Society.Inventory with
    { Lots = initial.Society.Society.Inventory.Lots.Where(lot => lot.OwnerId != actor).ToArray() };
    inventory = InventoryFixture.AddLot(inventory, "proof-property", kind, actor, kind == "storage_pot" ? 1 : 2,
        storageBuildingId: house.InstanceId);
    if (kind == "storage_pot") inventory = InventoryFixture.AddLot(inventory, "proof-contents", "fruit", actor, 1,
        storageBuildingId: house.InstanceId, containerLotId: "proof-property");
    inventory = inventory with { Lots = inventory.Lots.Select(lot => lot.Id == "proof-property" ? lot with
    { ConditionBasisPoints = kind == "fruit" ? 10_000 : 0, FreshnessBasisPoints = kind == "fruit" ? 0 : 10_000 } : lot).ToArray() };
    var state = WithInventory(initial, inventory) with
    {
        JevEnabled = false, RoutineHelper = RoutineHelperSettings.Off,
        Inhabitants = initial.Inhabitants.Select(person => person with
        { Position = person.InhabitantId == actor ? house.Position : person.Position, Equipment = person.InhabitantId == actor ? null : person.Equipment,
            HungerBasisPoints = 10_000, LastDecisionContext = null, Survival = new SurvivalCondition { WarmthBasisPoints = 10_000, NutritionBasisPoints = 10_000 } }).ToArray(),
    };
    var recorder = new ConcurrentQueue<object>();
    using var world = PrivateWorldRuntime.Restore(state, id => new Choices(id == actor && !order ? "household_collect:proof-property" : null, recorder));
    if (order) world.SubmitInstruction(new("proof-order", "owner:test", actor, OwnerInstructionKind.MustDo, "collect my " + kind.Replace('_', ' ')));
    await CaptureRun(world, recorder, output, $"recovery-{order}-{kind}", 2);
    if (!PersonalEquipmentRules.IsCarried(world.Society.Inventory.GetLot("proof-property"), actor)) throw new InvalidOperationException("Recovery did not execute.");
    if (world.ExportState().Events.Count(item => item.Kind == "personal_goods_collected") != 1) throw new InvalidOperationException("Missing recovery receipt.");
}

// Explicit relationship and initial-stock fixture; planning and birth run through native decisions.
var partner = initial.Society.Society.Inhabitants.First(person => person.Id != actor && person.HouseholdId == household).Id;
var stock = InventoryFixture.AddLot(initial.Society.Society.Inventory, "proof-birth-stock", "food", household, 20, storageBuildingId: house.InstanceId);
var partnered = SocietyFixture.ProposeRelationship(initial.Society.Society,
    new("proof-partnership", 1, SocietyRelationshipType.Partnership, actor, partner, initial.Society.Society.WorldTick)).Checkpoint;
partnered = SocietyFixture.AcceptRelationship(partnered, "proof-partnership", 1, partner).Checkpoint;
var near = initial.Map.FootNeighbors(house.Position).First(point => initial.Map.IsPassable(point) && initial.Inhabitants.All(person => person.Position != point));
var preparingState = WithInventory(initial with { Society = initial.Society with { Society = partnered } }, stock) with
{
    JevEnabled = false, RoutineHelper = RoutineHelperSettings.Off,
    Inhabitants = initial.Inhabitants.Select(person => person with
    { Position = person.InhabitantId == actor ? house.Position : person.InhabitantId == partner ? near : person.Position,
      HungerBasisPoints = 10_000, LastDecisionContext = null, Survival = new SurvivalCondition { WarmthBasisPoints = 10_000, NutritionBasisPoints = 10_000 } }).ToArray(),
};
using var preparing = PrivateWorldRuntime.Restore(preparingState, id => new Choices(id == actor ? "parent_propose:" : id == partner ? "parent_accept:" : null));
for (var tick = 0; tick < 8 && preparing.Inhabitants.Single(person => person.InhabitantId == actor).Parenthood?.Stage != "preparing"; tick++)
    if (!(await preparing.AdvanceOneTickAsync()).Advanced) throw new InvalidOperationException("Planning tick refused.");
var plan = preparing.Inhabitants.Single(person => person.InhabitantId == actor).Parenthood;
if (plan?.Stage != "preparing") throw new InvalidOperationException("Native planning did not complete.");
preparing.Pause();
var waiting = preparing.ExportState();
// Move only the fixture clock past idle waiting, preserving the actual native plan.
var due = plan.LastTransitionTick + 599;
var advanced = SocietyFixture.AdvanceTo(SocietyFixture.Resume(waiting.Society.Society).Checkpoint, due).Checkpoint;
var systems = waiting.WorldSystems!;
waiting = waiting with
{
    Society = waiting.Society with { Society = SocietyFixture.Pause(advanced).Checkpoint },
    WorldSystems = systems with { WorldTick = due, RegionalWeather = RegionalWeatherRules.Advance(systems, due),
        Climate = WeatherRules.Advance(systems.Climate, due, waiting.WorldSeed, systems.Config) },
    Inhabitants = waiting.Inhabitants.Select(person => person with { LastDecisionContext = null }).ToArray(),
};
foreach (var broken in new[] { false, true })
{
    var caregiver = plan.PrimaryCaregiverId!;
    var owner = waiting.Society.Society.GetInhabitant(caregiver).HouseholdId!;
    var inventory = waiting.Society.Society.Inventory with
    { Lots = waiting.Society.Society.Inventory.Lots.Where(lot => lot.OwnerId != caregiver &&
        (lot.OwnerId != owner || !InventoryContainerRules.IsFood(lot.ItemKind))).ToArray() };
    inventory = InventoryFixture.AddLot(inventory, "proof-birth-pot", "storage_pot", owner, 1, storageBuildingId: house.InstanceId);
    inventory = InventoryFixture.AddLot(inventory, "proof-birth-food", "food", owner, 6, storageBuildingId: house.InstanceId, containerLotId: "proof-birth-pot");
    inventory = InventoryFixture.AddLot(inventory, "proof-birth-second-pot", "storage_pot", owner, 1, storageBuildingId: house.InstanceId);
    inventory = InventoryFixture.AddLot(inventory, "proof-birth-second-food", "food", owner, 6, storageBuildingId: house.InstanceId, containerLotId: "proof-birth-second-pot");
    inventory = InventoryFixture.Reserve(inventory, "proof-other-meal", owner, "proof-birth-food", 2, "another_meal", long.MaxValue);
    inventory = InventoryFixture.AddLot(inventory, "proof-ballast", "stone", caregiver, PersonalEquipmentRules.Capacity(inventory, caregiver, null));
    if (broken) inventory = inventory with { Lots = inventory.Lots.Select(lot => lot.Id is "proof-birth-pot" or "proof-birth-second-pot" ? lot with { ConditionBasisPoints = 0 } : lot).ToArray() };
    var away = waiting.Map.Tiles.First(tile => waiting.Map.IsPassable(tile.Position) && waiting.Map.FootDistance(tile.Position, house.Position) > 3 &&
        waiting.Inhabitants.All(person => person.Position != tile.Position)).Position;
    var state = WithInventory(waiting, inventory) with
    { Inhabitants = waiting.Inhabitants.Select(person => person with { HungerBasisPoints = 10_000,
        Position = person.InhabitantId == caregiver ? away : person.Position, Equipment = person.InhabitantId == caregiver ? null : person.Equipment }).ToArray() };
    var recorder = new ConcurrentQueue<object>();
    using var world = PrivateWorldRuntime.Restore(state, _ => new Choices(null, recorder));
    if (PersonalEquipmentRules.FreeCapacity(world.Society.Inventory, caregiver, null) != 0) throw new InvalidOperationException("Cargo is not full.");
    world.Resume();
    await CaptureRun(world, recorder, output, $"birth-{broken}", 3);
    if (world.Society.Births.Count != (broken ? 0 : 1)) throw new InvalidOperationException("Birth gate differed.");
    var retained = world.Society.Inventory.GetLot("proof-birth-food");
    if (retained.Quantity != (broken ? 6 : 2) || retained.ContainerLotId != "proof-birth-pot" || retained.StorageBuildingId != house.InstanceId)
        throw new InvalidOperationException("Birth did not consume food in place.");
}
Console.WriteLine("Eight native scenarios: six physical recoveries, birth in a reserved family with full cargo, and broken-pot rejection.");

static PrivateWorldRuntimeState WithInventory(PrivateWorldRuntimeState state, InventoryCheckpoint inventory) =>
    state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } };

static async Task CaptureRun(PrivateWorldRuntime world, ConcurrentQueue<object> observations, string output, string name, int ticks)
{
    var directory = Path.Combine(output, name);
    Directory.CreateDirectory(directory);
    world.Validate();
    var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
    if ((await world.AdvanceOneTickAsync(() => false)).Advanced || !before.SequenceEqual(PrivateWorldRuntimeCodec.Encode(world.ExportState())))
        throw new InvalidOperationException("Refused tick changed the checkpoint.");
    for (var tick = 0; tick <= ticks; tick++)
    {
        var state = world.ExportState();
        var prefix = Path.Combine(directory, $"tick-{tick:D6}");
        var bytes = PrivateWorldRuntimeCodec.Encode(state);
        using (var file = File.Create(prefix + ".checkpoint.gz"))
        using (var compressed = new GZipStream(file, CompressionLevel.Fastest)) compressed.Write(bytes);
        File.WriteAllText(prefix + ".events.json", JsonSerializer.Serialize(state.Events));
        File.WriteAllText(prefix + ".digests.json", JsonSerializer.Serialize(observations.ToArray()));
        using var loaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes));
        if (!bytes.SequenceEqual(PrivateWorldRuntimeCodec.Encode(loaded.ExportState()))) throw new InvalidOperationException("Strict reload differed.");
        if (tick != ticks && !(await world.AdvanceOneTickAsync()).Advanced) throw new InvalidOperationException("Native tick refused.");
    }
    world.Validate();
    Console.WriteLine($"{name}: {ticks + 1} exact frames, {observations.Count} observations.");
}

static PrivateWorldRuntime CreateGenerated(string seed, IDecisionProvider provider)
{
    var options = new GeographyOptions(seed, WorldSizePreset.Small);
    var world = new PrivateWorldRuntime(seed, _ => provider, startPace: WorldStartPace.FounderSetup, geographyOptions: options);
    var map = world.ExportState().Map;
    var camp = map.Resources.Single(item => item.Id == "berry-patch").Position;
    var occupied = map.Resources.Select(item => item.Position).Concat(map.CampObjects.Select(item => item.Position)).ToHashSet();
    var anchor = map.Tiles.Select(tile => tile.Position)
        .Where(point => map.IsBuildable(point) && !occupied.Contains(point))
        .OrderBy(point => Math.Abs(point.X - camp.X) + Math.Abs(point.Y - camp.Y)).ThenBy(point => point.Y).ThenBy(point => point.X)
        .Prepend(camp).First(point =>
        {
            if (FirstTownLayoutPlanner.Plan(map, point) is not { } layout) return false;
            var taken = occupied.Concat(layout.RoadTiles).Concat(layout.Buildings.SelectMany(building =>
                Enumerable.Range(0, building.Height).SelectMany(y => Enumerable.Range(0, building.Width)
                    .Select(x => new GridPoint(building.Position.X + x, building.Position.Y + y))))).ToHashSet();
            return map.Tiles.Count(tile => Math.Abs(tile.Position.X - point.X) <= 5 && Math.Abs(tile.Position.Y - point.Y) <= 5 &&
                map.IsBuildable(tile.Position) && !taken.Contains(tile.Position)) >= PrivateWorldRuntime.RequiredFounders;
        });
    world.InitializeFirstTownContent();
    world.AcceptFirstTownLayout(anchor);
    var buildings = world.WorldSimulation.Buildings.SelectMany(building => WorldContentSimulationRules.Footprint(
        world.WorldContent.Buildings.Single(item => item.CanonicalId == building.DefinitionId), building.Position)).ToHashSet();
    var roads = world.RoadTiles.ToHashSet();
    var sites = map.Tiles.Where(tile => Math.Abs(tile.Position.X - anchor.X) <= 5 && Math.Abs(tile.Position.Y - anchor.Y) <= 5 &&
        map.IsBuildable(tile.Position) && !occupied.Contains(tile.Position) && !buildings.Contains(tile.Position) && !roads.Contains(tile.Position))
        .Take(PrivateWorldRuntime.RequiredFounders).Select(tile => tile.Position).ToArray();
    for (var index = 0; index < sites.Length; index++) world.PlaceFounder($"founder:{index + 1:D32}", sites[index]);
    world.StartWorld();
    return world;
}

sealed class Choices(string? wanted = null, ConcurrentQueue<object>? observations = null) : IDecisionProvider
{
    public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
    public long ProviderEpoch => 0;
    public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
    {
        observations?.Enqueue(new { request.Observation.InhabitantId, request.Observation.WorldTick, request.Observation.ObservationDigest,
            request.Observation.Candidates });
        var selected = wanted is null ? "safe_idle" : request.Observation.Candidates.FirstOrDefault(candidate => candidate.Id.StartsWith(wanted, StringComparison.Ordinal))?.Id ?? "safe_idle";
        return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, request.Observation.InhabitantId, Kind, request.ProviderEpoch,
            request.Observation.RunEpoch, request.Observation.DecisionGeneration, request.Observation.ObservationDigest, selected, 1, new Dictionary<string, double> { [selected] = 1 }));
    }
}

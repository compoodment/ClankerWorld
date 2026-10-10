using System.Text.Json;
using ClankerWorld.GodotClient.UI;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class HouseToolsBootstrapTests
{
    private const string Actor = "founder-scout";
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task OfferedPersonalChoicesPayForAnIndependentHouseAndBothToolsAcrossProjectAndJobReloads()
    {
        var chooser = new OfferedChoiceProvider();
        using var world = await BuildIndependentHouse(chooser);
        var household = world.Society.GetInhabitant(Actor).HouseholdId!;
        var house = Assert.Single(world.WorldSimulation.Buildings);
        var paidHouseTick = world.WorldTick;
        foreach (var kind in new[] { HouseToolsContent.CrudeWoodenAxe, HouseToolsContent.CrudeWoodenPickaxe })
        {
            Assert.Equal(kind == HouseToolsContent.CrudeWoodenPickaxe,
                (Physical(world).Skills ?? []).Any(skill => skill.Kind == SettlementSkillKind.Crafting));
            chooser.Target = kind;
            var recipe = world.WorldContent.Recipes.Single(item => item.Outputs.Any(output => output.ResourceId == kind));
            await Until(world, () => Physical(world).Project is { WorkDone: > 0, JobId: null } project &&
                project.CandidateId == "build:recipe:" + recipe.CanonicalId, chooser);
            Assert.DoesNotContain(world.WorldSimulation.ProductionJobs, job => job.RecipeId == recipe.CanonicalId);
            Assert.True(world.Society.Inventory.Lots.Where(lot => lot.OwnerId == household &&
                lot.StorageBuildingId == house.InstanceId && lot.ItemKind == "wood").Sum(lot => lot.Quantity) >= 3);
            // Replay actual preparation, before a job or output exists.
            using (var twin = Restore(world, new OfferedChoiceProvider { Target = kind }))
            {
                Assert.True((await world.AdvanceOneTickAsync()).Advanced);
                Assert.True((await twin.AdvanceOneTickAsync()).Advanced);
                Assert.Equal(Encode(world), Encode(twin));
            }
            await Until(world, () => world.WorldSimulation.ProductionJobs.Any(job =>
                job.RecipeId == recipe.CanonicalId && job.State == WorldProductionJobState.Running), chooser);
            var running = Assert.Single(world.WorldSimulation.ProductionJobs, job => job.RecipeId == recipe.CanonicalId);
            var workTicks = kind == HouseToolsContent.CrudeWoodenAxe ? 24L : 19L;
            Assert.Equal((Actor, household, house.InstanceId, workTicks),
                (running.WorkerId, running.OwnerId, running.BuildingInstanceId, running.CompletionTick - running.StartedTick));
            Assert.Equal(3, running.InputReservationIds.Sum(id => world.Society.Inventory.GetReservation(id).Quantity));
            Assert.All(running.InputReservationIds, id =>
            {
                var reservation = world.Society.Inventory.GetReservation(id);
                var lot = world.Society.Inventory.GetLot(reservation.LotId);
                Assert.Equal((household, "wood", house.InstanceId), (reservation.OwnerId, lot.ItemKind, lot.StorageBuildingId));
                Assert.Equal(InventoryReservationState.Reserved, reservation.State);
            });
            Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.ItemKind == kind);
            // Replay the running reservation through the one real output commit.
            using (var twin = Restore(world, new OfferedChoiceProvider { Target = kind }))
            {
                for (var count = 0; count < 40 && Job(world, running.JobId).State != WorldProductionJobState.Completed; count++)
                {
                    Assert.True((await world.AdvanceOneTickAsync()).Advanced);
                    Assert.True((await twin.AdvanceOneTickAsync()).Advanced);
                }
                Assert.Equal(WorldProductionJobState.Completed, Job(world, running.JobId).State);
                Assert.Equal(Encode(world), Encode(twin));
            }
            Assert.All(running.InputReservationIds, id => Assert.Equal(InventoryReservationState.Completed,
                world.Society.Inventory.GetReservation(id).State));
            var output = Assert.Single(world.Society.Inventory.Lots, lot => lot.ItemKind == kind);
            Assert.Equal((1, household, house.InstanceId, 10_000),
                (output.Quantity, output.OwnerId, output.StorageBuildingId, output.ConditionBasisPoints));
            AssertToolEventThroughClient(world, kind);
            await Until(world, () => Carried(world, kind), chooser);
            var collected = world.Society.Inventory.GetLot(output.Id);
            Assert.Equal(household, collected.OwnerId);
            Assert.True(PersonalEquipmentRules.IsCarried(collected, Actor));
        }
        Assert.True(world.WorldTick > paidHouseTick);
        Assert.Equal(2, world.WorldSimulation.ProductionJobs.Count);
        Assert.Equal(48, world.Society.Inventory.GetLot("wood:camp-alpha").Quantity);
        Assert.Equal(4, world.Society.Inventory.GetLot("tools:camp-alpha").Quantity);
        Assert.Empty(world.Towns);
        Assert.Single(world.WorldSimulation.Buildings);
        Assert.Contains(chooser.Choices, choice => choice == "supply_workstation:wood");
        Assert.Contains(chooser.Choices, choice => choice == "collect_tool:" + HouseToolsContent.CrudeWoodenAxe);
        Assert.Contains(chooser.Choices, choice => choice == "collect_tool:" + HouseToolsContent.CrudeWoodenPickaxe);
        Assert.DoesNotContain(chooser.Choices, choice => choice.StartsWith("trade", StringComparison.Ordinal));
        using var reloaded = Restore(world, new OfferedChoiceProvider());
        Assert.Equal(Encode(world), Encode(reloaded));
    }

    [Fact]
    public async Task ActualBuiltInChooserSuppliesMakesCollectsAndUsesItsHouseToolsWithoutATown()
    {
        using var prepared = await BuildIndependentHouse(new OfferedChoiceProvider());
        // The House was paid through offered choices. From this saved boundary
        // the real built-in provider receives the complete native candidate set.
        var chooser = new ActionCoverageRecorder();
        using var world = Restore(prepared, chooser);
        var treeBefore = world.WorldSystems.Ecology.GetResource("timber-tree").Quantity;
        var stoneBefore = world.WorldSystems.Ecology.GetResource("settlement-stone").Quantity;
        await Until(world, () => Carried(world, HouseToolsContent.CrudeWoodenAxe) &&
            Carried(world, HouseToolsContent.CrudeWoodenPickaxe), chooser);
        Assert.Contains(chooser.Chosen.Keys, choice => choice == "supply_workstation:wood");
        Assert.Contains(chooser.Chosen.Keys, choice => choice == "collect_tool:" + HouseToolsContent.CrudeWoodenAxe);
        Assert.Contains(chooser.Chosen.Keys, choice => choice == "collect_tool:" + HouseToolsContent.CrudeWoodenPickaxe);
        Assert.Equal(2, world.WorldSimulation.ProductionJobs.Count(job => job.State == WorldProductionJobState.Completed &&
            world.WorldContent.Recipes.Single(recipe => recipe.CanonicalId == job.RecipeId).Tags.Contains("house-tool")));
        var firstTools = world.Society.Inventory.Lots.Where(lot => lot.ItemKind is
            HouseToolsContent.CrudeWoodenAxe or HouseToolsContent.CrudeWoodenPickaxe).ToArray();
        Assert.Equal(2, firstTools.Length);
        foreach (var tool in firstTools) AssertToolEventThroughClient(world, tool.ItemKind);
        await Until(world, () => world.WorldSystems.Ecology.GetResource("timber-tree").Quantity < treeBefore &&
            world.WorldSystems.Ecology.GetResource("settlement-stone").Quantity < stoneBefore, chooser, 320);
        foreach (var firstTool in firstTools)
        {
            var tool = world.Society.Inventory.GetLot(firstTool.Id);
            Assert.InRange(tool.ConditionBasisPoints, 0, 6_000);
            Assert.Equal(world.Society.GetInhabitant(Actor).HouseholdId, tool.OwnerId);
        }
        Assert.Contains(world.ExportState().Events, item => item.Kind == "material_gathered" && item.Detail == Actor + ":wood:4");
        Assert.Contains(world.ExportState().Events, item => item.Kind == "material_gathered" && item.Detail == Actor + ":stone:4");
        Assert.Equal(48, world.Society.Inventory.GetLot("wood:camp-alpha").Quantity);
        Assert.Empty(world.Towns);
        Assert.DoesNotContain(world.WorldSimulation.Buildings, building =>
            world.WorldContent.Buildings.Single(item => item.CanonicalId == building.DefinitionId).Tags.Contains("blacksmith"));
        using var reloaded = Restore(world, new ActionCoverageRecorder());
        Assert.Equal(Encode(world), Encode(reloaded));
    }

    private static async Task<PrivateWorldRuntime> BuildIndependentHouse(OfferedChoiceProvider chooser)
    {
        using var setup = new PrivateWorldRuntime("house-tools-no-town", _ => new ActionCoverageRecorder(chooseIdle: true),
            startPace: WorldStartPace.DecidedPlaytest);
        setup.Resume();
        Assert.True(setup.StageStarterContent());
        for (var tick = 0; tick < 12 && !setup.Content.Packages.Any(package =>
            package.Manifest.PackageId == HouseToolsContent.PackageId && package.Lifecycle == ContentPackageLifecycle.Active); tick++)
            Assert.True((await setup.AdvanceOneTickAsync()).Advanced);
        Assert.Contains(setup.Content.Packages, package => package.Manifest.PackageId == HouseToolsContent.PackageId &&
            package.Lifecycle == ContentPackageLifecycle.Active);
        Assert.Empty(setup.Towns);
        Assert.Empty(setup.WorldSimulation.Buildings);
        Assert.Equal(16, setup.WorldSystems.Ecology.GetResource("settlement-wood").Quantity);
        Assert.DoesNotContain(setup.Society.Inventory.Lots, lot => ToolProgressionRules.Find(lot.ItemKind)?.Family is ToolFamily.Axe or ToolFamily.Pickaxe);
        Assert.True(setup.DisplaceAdult(Actor));
        Assert.Null(setup.Society.GetInhabitant(Actor).HouseholdId);
        Assert.DoesNotContain(setup.Society.Inventory.Lots, lot => lot.OwnerId == Actor && lot.ItemKind != "food");
        var world = Restore(setup, chooser);
        await Until(world, () => world.WorldSimulation.Buildings.Count == 1, chooser);
        var house = Assert.Single(world.WorldSimulation.Buildings);
        Assert.Equal(HouseContent.House1x1().CanonicalId, house.DefinitionId);
        Assert.Equal(world.Society.GetInhabitant(Actor).HouseholdId, house.HouseholdId);
        Assert.Null(house.TownId);
        var payments = world.Society.Inventory.Reservations.Where(reservation => reservation.Purpose == "building:" + house.InstanceId).ToArray();
        Assert.Equal(8, payments.Sum(item => item.Quantity));
        Assert.All(payments, payment =>
        {
            Assert.Equal(Actor, payment.OwnerId);
            Assert.Equal(InventoryReservationState.Completed, payment.State);
        });
        Assert.Equal(8, world.WorldSystems.Ecology.GetResource("settlement-wood").Quantity);
        Assert.Equal(48, world.Society.Inventory.GetLot("wood:camp-alpha").Quantity);
        Assert.Contains(chooser.Choices, choice => choice == "household_found");
        Assert.Contains(chooser.Choices, choice => choice == "gather_building_material:wood");
        return world;
    }

    private static async Task Until(PrivateWorldRuntime world, Func<bool> completed, object chooser, int maximumTicks = 180)
    {
        for (var tick = 0; tick < maximumTicks && !completed(); tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.True(completed(), "Native work did not reach its boundary. " + Describe(world, chooser));
    }

    private static string Describe(PrivateWorldRuntime world, object chooser) =>
        "Tick=" + world.WorldTick + "; project=" + Physical(world).Project + "; choices=" +
        (chooser is OfferedChoiceProvider offered ? string.Join(",", offered.Choices.TakeLast(12)) :
            chooser is ActionCoverageRecorder automatic ? string.Join(",", automatic.Chosen.Keys) : "") +
        "; events=" + string.Join(" | ", world.ExportState().Events.TakeLast(12).Select(item => item.Kind + ":" + item.Detail));

    private static PlaytestInhabitantState Physical(PrivateWorldRuntime world) => world.ExportState().Inhabitants.Single(item => item.InhabitantId == Actor);
    private static WorldProductionJob Job(PrivateWorldRuntime world, string id) => world.WorldSimulation.ProductionJobs.Single(item => item.JobId == id);
    private static byte[] Encode(PrivateWorldRuntime world) => PrivateWorldRuntimeCodec.Encode(world.ExportState());
    private static PrivateWorldRuntime Restore(PrivateWorldRuntime world, IDecisionProvider chooser) =>
        PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(Encode(world)),
            id => id == Actor ? chooser : new ActionCoverageRecorder(chooseIdle: true));
    private static bool Carried(PrivateWorldRuntime world, string kind) => world.Society.Inventory.Lots.Any(lot =>
        lot.ItemKind == kind && PersonalEquipmentRules.IsCarried(lot, Actor));

    private static void AssertToolEventThroughClient(PrivateWorldRuntime world, string kind)
    {
        var store = new OwnerWorldObservationStore(world);
        var snapshot = JsonSerializer.Deserialize<OwnerWorldSnapshot>(JsonSerializer.Serialize(store.GetSnapshot(), WebJson), WebJson)!;
        var events = JsonSerializer.Deserialize<OwnerWorldEventSlice>(JsonSerializer.Serialize(store.GetEventsAfter(0), WebJson), WebJson)!;
        var made = Assert.Single(events.Events, item => item.Kind == "house_tool_made" && item.Detail == Actor + "|" + kind);
        Assert.True(GameUiText.IsPlayerFacingEvent(made.Kind));
        Assert.Equal(world.Society.GetInhabitant(Actor).Name + " made a " + kind.Replace('_', ' ') + ".", WorldEventText.Describe(made, snapshot));
        Assert.Contains(snapshot.Inhabitants, person => person.Id == Actor);
        var house = world.WorldSimulation.Buildings.Single(building => building.HouseholdId == world.Society.GetInhabitant(Actor).HouseholdId &&
            building.DefinitionId == HouseContent.House1x1().CanonicalId);
        Assert.Equal(new OwnerWorldPosition(house.Position.X, house.Position.Y), made.Position);
    }

    private sealed class OfferedChoiceProvider : IDecisionProvider
    {
        public string? Target { get; set; }
        public List<string> Choices { get; } = [];
        public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
        public long ProviderEpoch => 0;

        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var candidates = request.Observation.Candidates;
            var selected = Target is null
                ? candidates.FirstOrDefault(item => item.Id == "household_found") ??
                    candidates.FirstOrDefault(item => TownConstructionCandidateIds.TryParse(item.Id, out var selection) &&
                        selection.IsBuilding && selection.DefinitionId == HouseContent.House1x1().CanonicalId) ??
                    candidates.FirstOrDefault(item => item.Id == "gather_building_material:wood")
                : candidates.FirstOrDefault(item => item.Id == "collect_tool:" + Target) ??
                    candidates.FirstOrDefault(item => TownConstructionCandidateIds.TryParse(item.Id, out var selection) &&
                        !selection.IsBuilding && selection.DefinitionId.EndsWith("/" + Target.Replace('_', '-') + "@1.0.0", StringComparison.Ordinal)) ??
                    candidates.FirstOrDefault(item => item.Id == "haul_household_stock") ??
                    candidates.FirstOrDefault(item => item.Id == "supply_workstation:wood");
            selected ??= candidates.Single(item => item.Id == "safe_idle");
            Choices.Add(selected.Id);
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, request.Observation.InhabitantId,
                Kind, ProviderEpoch, request.Observation.RunEpoch, request.Observation.DecisionGeneration,
                request.Observation.ObservationDigest, selected.Id, 1,
                candidates.ToDictionary(item => item.Id, item => item.Id == selected.Id ? 1d : 0d),
                ChosenName: request.Observation.NeedsName ? "Hazel Vale" : null));
        }
    }
}

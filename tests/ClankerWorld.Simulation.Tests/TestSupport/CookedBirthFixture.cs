using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

internal sealed record CookedBirthState(PrivateWorldRuntimeState State, string First, string Second,
    string Household, string House, IReadOnlyList<string> OutputIds)
{
    public SocietyBirthRequest Request(string id = "cooked-birth") => new(id, 1, First, Second, Household,
        [First, Second], [First, Second], OutputIds[0], 4, State.Society.Society.WorldTick,
        PrimaryCaregiverId: First,
        FoodContributions: [new(OutputIds[0], 2), new(OutputIds[1], 2)]);
}

internal static class CookedBirthFixture
{
    internal const string PotId = "birth-storage-pot";
    private static readonly Lazy<Task<byte[]>> Cooked = new(CreateCookedAsync);

    public static async Task<CookedBirthState> PrepareAsync(bool inPot = false)
    {
        var state = PrivateWorldRuntimeCodec.Decode(await Cooked.Value);
        const string household = "household:camp-alpha";
        var parents = state.Society.Society.Inhabitants.Where(person => person.HouseholdId == household)
            .OrderBy(person => person.Id, StringComparer.Ordinal).ToArray();
        var house = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == "first-town-house-a");
        var outputs = state.WorldSimulation.ProductionJobs.Where(job => job.RecipeId ==
                state.WorldContent!.Recipes.Single(recipe => recipe.LocalId == "house-meal").CanonicalId)
            .Select(job => job.JobId + ":output:00").Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(4, outputs.Length);
        if (inPot)
        {
            var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, PotId,
                InventoryContainerRules.StoragePot, household, 1, storageBuildingId: house.InstanceId);
            foreach (var id in outputs)
                inventory = InventoryFixture.PutIntoContainer(inventory, "birth-pot:" + id, household, PotId, id, 2);
            state = FarmFieldTests.WithInventory(state, inventory);
        }
        return new(state, parents[0].Id, parents[1].Id, household, house.InstanceId, outputs);
    }

    public static PrivateWorldRuntime Restore(CookedBirthState prepared) =>
        PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(prepared.State)),
            id => new BirthChooser(id, prepared.First, prepared.Second));

    private static async Task<byte[]> CreateCookedAsync()
    {
        using var initial = NormalPathWorld.CreateGenerated("cooked-child-birth", _ => new BirthChooser("", "", ""));
        var state = initial.ExportState();
        const string household = "household:camp-alpha";
        var parents = state.Society.Society.Inhabitants.Where(person => person.HouseholdId == household)
            .OrderBy(person => person.Id, StringComparer.Ordinal).ToArray();
        Assert.Equal(2, parents.Length);
        var house = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == "first-town-house-a");
        var inventory = state.Society.Society.Inventory;
        foreach (var lot in inventory.Lots.Where(lot => lot.OwnerId == household &&
                     InventoryContainerRules.IsFood(lot.ItemKind) && lot.FreshnessBasisPoints > 0 && lot.ConditionBasisPoints > 0).ToArray())
        {
            var available = PersonalEquipmentRules.AvailableQuantity(inventory, lot);
            if (available > 0)
                inventory = InventoryFixture.Reserve(inventory, "birth-stock-claim:" + lot.Id, household,
                    lot.Id, available, "unrelated_household_food", long.MaxValue);
        }
        inventory = InventoryFixture.AddLot(inventory, "birth-input-potatoes", "potatoes", household, 8,
            storageBuildingId: house.InstanceId);
        inventory = InventoryFixture.AddLot(inventory, "birth-input-wood", "wood", household, 4,
            storageBuildingId: house.InstanceId);
        var originalInputs = inventory.Lots.ToDictionary(lot => lot.Id, StringComparer.Ordinal);
        var partnerPoint = state.Map.Tiles.First(tile => state.Map.IsPassable(tile.Position) &&
            state.Map.FootDistance(house.Position, tile.Position) == 1 &&
            state.Inhabitants.All(person => person.InhabitantId == parents[0].Id ||
                person.InhabitantId == parents[1].Id || person.Position != tile.Position)).Position;
        using (var society = SocietyWorldRuntime.Restore(state.Society))
        {
            society.Apply(checkpoint => SocietyFixture.ProposeRelationship(checkpoint,
                new("cooked-birth-parents", 1, SocietyRelationshipType.Partnership,
                    parents[0].Id, parents[1].Id, checkpoint.WorldTick)));
            society.Apply(checkpoint => SocietyFixture.AcceptRelationship(checkpoint,
                "cooked-birth-parents", 1, parents[1].Id));
            state = state with { Society = society.ExportState() };
        }
        state = FarmFieldTests.WithInventory(state, inventory) with
        {
            Inhabitants = state.Inhabitants.Select(person => person with
            {
                Position = person.InhabitantId == parents[0].Id ? house.Position :
                    person.InhabitantId == parents[1].Id ? partnerPoint : person.Position,
                HungerBasisPoints = 10_000,
            }).ToArray(),
            WorldSystems = state.WorldSystems! with
            {
                RegionalWeather = null,
                Config = state.WorldSystems.Config with
                {
                    WeatherProfiles = Enum.GetValues<SeasonKind>().Select(season =>
                        new WeatherProfile(season, 1, 0, 0, 0, 0)).ToArray(),
                },
                Climate = state.WorldSystems.Climate with { Weather = WeatherKind.Clear },
            },
        };
        using var world = PrivateWorldRuntime.Restore(state,
            id => new BirthChooser(id, parents[0].Id, parents[1].Id));
        var recipe = world.WorldContent.Recipes.Single(item => item.LocalId == "house-meal");
        Assert.Equal(2, Assert.Single(recipe.Outputs).Amount);
        for (var batch = 0; batch < 4; batch++)
        {
            var started = world.StartProduction(recipe.CanonicalId, house.InstanceId, parents[0].Id);
            Assert.True(started.Applied, started.Failure);
            for (var tick = 0; tick < recipe.DurationTicks; tick++)
                Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            var job = world.WorldSimulation.ProductionJobs.Single(item => item.JobId == started.JobId);
            Assert.Equal(WorldProductionJobState.Completed, job.State);
            var receipts = job.InputReservationIds.Select(world.Society.Inventory.GetReservation).ToArray();
            Assert.Equal(2, receipts.Where(receipt => originalInputs[receipt.LotId].ItemKind == "potatoes")
                .Sum(receipt => receipt.Quantity));
            Assert.Equal(1, receipts.Where(receipt => originalInputs[receipt.LotId].ItemKind == "wood")
                .Sum(receipt => receipt.Quantity));
            Assert.All(receipts, receipt =>
            {
                Assert.Equal(household, receipt.OwnerId);
                Assert.Equal(InventoryReservationState.Completed, receipt.State);
            });
            var output = world.Society.Inventory.GetLot(started.JobId + ":output:00");
            Assert.Equal("simple_meal", output.ItemKind);
            Assert.Equal(2, output.Quantity);
            Assert.Equal(household, output.OwnerId);
            Assert.Equal(house.InstanceId, output.StorageBuildingId);
        }
        Assert.Empty(world.Society.Births);
        return PrivateWorldRuntimeCodec.Encode(world.ExportState());
    }

    internal sealed class BirthChooser(string actor, string first, string second) : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            var prefix = actor == first && first.Length > 0 ? "parent_propose:" :
                actor == second && second.Length > 0 ? "parent_accept:" : null;
            var selected = prefix is null ? null : request.Observation.Candidates.FirstOrDefault(item =>
                item.Id.StartsWith(prefix, StringComparison.Ordinal));
            selected ??= request.Observation.Candidates.Single(item => item.Id == "safe_idle");
            return new DeterministicDecisionProvider().DecideAsync(request with
            { Observation = request.Observation with { Candidates = [selected] } }, cancellationToken);
        }
    }
}

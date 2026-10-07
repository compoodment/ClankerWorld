using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class AnimalPipelineTests
{
    [Theory]
    [InlineData("sheep")]
    [InlineData("cow")]
    public async Task LeadingMovesTheExactNewlyReadyProductAndItsReservationAcrossReload(string species)
    {
        var (state, actor, home, yard) = CreateYard("animal-led-product-" + species);
        var day = state.WorldSystems!.Config.TicksPerDay;
        var position = state.Map.Tiles.Select(tile => tile.Position).First(point => state.Map.IsBuildable(point) &&
            state.Map.FootDistance(point, yard.Position) == 3 && !state.Inhabitants.Any(person => person.Position == point));
        var definition = AnimalRules.Definition(species);
        var animal = new AnimalState("led", "Moss", species, "female", -(long)definition.AdultDays * day,
            position, "household:" + home, home, yard.InstanceId, CareUntilTick: day,
            ProductProgressTicks: definition.ProductDays * day - 1, LeaderId: actor, LeadDestination: yard.Position);
        using var world = PrivateWorldRuntime.Restore(At(state, actor, position, state.Society.Society.Inventory, [animal]),
            id => id == actor ? new AnimalChooser("animal:lead_home:") : new AnimalChooser());
        await Until(world, () => world.Animals.Single().Position != position && world.Animals.Single().ReadyProductLotId is not null);
        var moved = world.Animals.Single();
        var lot = world.Society.Inventory.GetLot(moved.ReadyProductLotId!);
        Assert.Equal((definition.Product, home, definition.ProductQuantity), (lot.ItemKind, lot.OwnerId, lot.Quantity));
        Assert.Equal(new InventoryGroundPosition(moved.Position.X, moved.Position.Y), lot.GroundPosition);
        var reservation = world.Society.Inventory.GetReservation(moved.ReadyProductReservationId!);
        Assert.Equal((lot.Id, home, lot.Quantity, InventoryReservationState.Reserved),
            (reservation.LotId, reservation.OwnerId, reservation.Quantity, reservation.State));
        world.Validate();
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes),
            id => id == actor ? new AnimalChooser("animal:lead_home:") : new AnimalChooser());
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OutsiderCareProtectsTheAggregateReserveOfTheHouseholdSupplyingItsGreens(bool enough)
    {
        var (state, owner, home, yard) = CreateYard("animal-outsider-feed");
        var outsider = state.Society.Society.Inhabitants.First(person => person.Status == SocietyInhabitantStatus.Active &&
            person.AgeBand == SocietyAgeBand.Adult && person.HouseholdId != home).Id;
        var outsiderHome = state.Society.Society.GetInhabitant(outsider).HouseholdId!;
        var family = state.Society.Society.Inhabitants.Where(person => person.Status == SocietyInhabitantStatus.Active &&
            person.HouseholdId == outsiderHome).Select(person => person.Id).ToHashSet(StringComparer.Ordinal);
        var inventory = state.Society.Society.Inventory with
        { Lots = state.Society.Society.Inventory.Lots.Where(lot => lot.OwnerId != outsiderHome && !family.Contains(lot.OwnerId)).ToArray() };
        inventory = InventoryFixture.AddLot(inventory, "00-feed", "wild_greens", outsider, 1);
        inventory = InventoryFixture.AddLot(inventory, "01-feed", "wild_greens", outsider, 1);
        inventory = InventoryFixture.AddLot(inventory, "family-food", "berries", outsiderHome,
            family.Count * 2 - 1 + (enough ? 1 : 0), groundPosition: new(yard.Position.X, yard.Position.Y));
        inventory = InventoryFixture.AddLot(inventory, "animal-family-food", "berries", home, 20,
            groundPosition: new(yard.Position.X, yard.Position.Y));
        inventory = InventoryFixture.AddLot(inventory, "water-jug", "water_jug", outsider, 1);
        inventory = InventoryFixture.AddLot(inventory, "water", "fresh_water", outsider, 2, containerLotId: "water-jug");
        var day = state.WorldSystems!.Config.TicksPerDay;
        var animal = new AnimalState("cow", "Moss", "cow", "female", -7L * day, yard.Position,
            "household:" + home, home, yard.InstanceId)
        { CarePermissions = [outsider] };
        using var world = PrivateWorldRuntime.Restore(At(state, outsider, yard.Position, inventory, [animal]),
            id => id == outsider ? new AnimalChooser("animal:care:") : new AnimalChooser());
        if (enough) await Until(world, () => world.Animals.Single().CareUntilTick > world.WorldTick);
        else for (var tick = 0; tick < 4; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(enough ? 0 : 2, world.Society.Inventory.Lots.Where(lot => lot.Id is "00-feed" or "01-feed").Sum(lot => lot.Quantity));
        Assert.Equal(enough ? 0 : 2, world.Society.Inventory.Lots.Where(lot => lot.Id == "water").Sum(lot => lot.Quantity));
        Assert.Equal(enough, world.ExportState().Events.Any(item => item.Kind == "animal_cared"));
        world.Validate();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DrinkingMilkRequiresAUsableSourceJug(bool broken)
    {
        var (state, actor, _, yard) = CreateYard("animal-broken-milk");
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "milk-jug", "water_jug", actor, 1);
        inventory = InventoryFixture.AddLot(inventory, "milk", "milk", actor, 1, containerLotId: "milk-jug");
        if (broken) inventory = inventory with
        { Lots = inventory.Lots.Select(lot => lot.Id == "milk-jug" ? lot with { ConditionBasisPoints = 0 } : lot).ToArray() };
        state = At(state, actor, yard.Position, inventory, []) with
        { Inhabitants = At(state, actor, yard.Position, inventory, []).Inhabitants.Select(person => person.InhabitantId == actor ? person with { HungerBasisPoints = 3000 } : person).ToArray() };
        using var world = PrivateWorldRuntime.Restore(state, id => id == actor ? new AnimalChooser("drink_milk") : new AnimalChooser());
        if (!broken) await Until(world, () => world.ExportState().Events.Any(item => item.Kind == "milk_drunk"));
        else for (var tick = 0; tick < 4; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(broken ? 1 : 0, world.Society.Inventory.Lots.Where(lot => lot.Id == "milk").Sum(lot => lot.Quantity));
        Assert.Equal(actor, world.Society.Inventory.GetLot("milk-jug").OwnerId);
        Assert.Equal(broken ? 0 : 10_000, world.Society.Inventory.GetLot("milk-jug").ConditionBasisPoints);
        world.Validate();
    }
}

using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Tests;

public sealed class ArchivedContentSchemaTests
{
    [Theory]
    [InlineData(32)]
    [InlineData(33)]
    [InlineData(34)]
    public async Task ActualWornGearKeepsItsEstateAndHistoricalSlotButCannotUseOlderFormats(int olderSchema)
    {
        using var seed = NormalPathWorld.CreateGenerated("probe-a", _ => new ActionCoverageRecorder(true));
        var state = seed.ExportState();
        var actor = state.Inhabitants[0].InhabitantId;
        var household = state.Society.Society.GetInhabitant(actor).HouseholdId!;
        var stock = state.Society.Society.Inventory.Lots.First(lot => lot.OwnerId == household && lot.ItemKind == "clothing");
        var inventory = InventoryFixture.Transfer(state.Society.Society.Inventory, "collect-archive-garment",
            household, actor, stock.Id, 1, "equipment_collected");
        using var prepared = PrivateWorldRuntime.Restore(state with
        {
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } }
        }, _ => new ActionCoverageRecorder(true));
        var garment = prepared.Society.Inventory.Lots.Single(lot => lot.OwnerId == actor && lot.ItemKind == "clothing");
        Assert.True(prepared.EquipItem(actor, garment.Id).Applied);
        using var world = PrivateWorldRuntime.Restore(DiesNextTick(prepared.ExportState(), actor), _ => new ActionCoverageRecorder(true));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(SocietyInhabitantStatus.Dead, world.Society.GetInhabitant(actor).Status);
        var saved = world.ExportState();
        var deceased = Assert.Single(saved.DeceasedInhabitants!, person => person.InhabitantId == actor);
        Assert.Equal(garment.Id, deceased.LastPhysical.Equipment!.WornClothingLotId);
        Assert.DoesNotContain(saved.Inhabitants, person => person.InhabitantId == actor);
        Assert.All(saved.Inhabitants, person => Assert.Null(person.Equipment));
        var estate = Assert.Single(world.Society.Estates, item => item.DeceasedId == actor);
        var inherited = world.Society.Inventory.GetLot(garment.Id);
        Assert.Equal(estate.Id, inherited.OwnerId);
        Assert.Equal(1, inherited.Quantity);
        using var loaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(saved)));
        Assert.Equal(deceased, Assert.Single(loaded.ExportState().DeceasedInhabitants!, person => person.InhabitantId == actor));
        Assert.Equal(inherited, loaded.Society.Inventory.GetLot(garment.Id));
        loaded.Validate();
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(saved with { SchemaVersion = olderSchema, TownCouncils = [] }));
    }

    [Theory]
    [InlineData(32)]
    [InlineData(33)]
    [InlineData(34)]
    public async Task ActualArchivedCareConsentRoundTripsWhileOlderAlphaFormatsAreRefused(int olderSchema)
    {
        using var prepared = NormalPathWorld.CreateGenerated("probe-a", _ => new ActionCoverageRecorder(true));
        var actor = prepared.Inhabitants[0].InhabitantId;
        var caregiver = prepared.Inhabitants[1].InhabitantId;
        Assert.True(prepared.AllowMedicalCare(actor, caregiver, true).Applied);
        using var world = PrivateWorldRuntime.Restore(DiesNextTick(prepared.ExportState(), actor), _ => new ActionCoverageRecorder(true));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(SocietyInhabitantStatus.Dead, world.Society.GetInhabitant(actor).Status);
        var saved = world.ExportState();
        var deceased = Assert.Single(saved.DeceasedInhabitants!, person => person.InhabitantId == actor);
        Assert.Equal([caregiver], deceased.LastPhysical.MedicalCaregiverIds);
        Assert.All(saved.Inhabitants, person => Assert.Null(person.MedicalCaregiverIds));
        using var loaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(saved)));
        Assert.Equal([caregiver], Assert.Single(loaded.ExportState().DeceasedInhabitants!, person => person.InhabitantId == actor)
            .LastPhysical.MedicalCaregiverIds);
        loaded.Validate();
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(saved with { SchemaVersion = olderSchema, TownCouncils = [] }));
    }

    [Fact]
    public async Task CurrentAlphaKeepsActualArchivedHousingAndRefusesOlderHousingSaves()
    {
        using var prepared = NormalPathWorld.CreateGenerated("probe-a", _ => new ActionCoverageRecorder(true));
        var actor = "agent:" + 880.ToString("x32", System.Globalization.CultureInfo.InvariantCulture);
        var state = prepared.ExportState();
        var occupied = prepared.WorldSimulation.Buildings.SelectMany(building => WorldContentSimulationRules.Footprint(
            prepared.WorldContent.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId), building.Position)).ToHashSet();
        var point = prepared.Towns.Single().BorderTiles.First(tile => state.Map.IsPassable(tile) && !occupied.Contains(tile) &&
            !prepared.Inhabitants.Any(person => person.Position == tile));
        Assert.Null(prepared.AddAgent(actor, point));
        Assert.True((await prepared.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(HousingBlockers.NoHousehold, prepared.Inhabitants.Single(person => person.InhabitantId == actor).Housing!.Blocker);
        using var world = PrivateWorldRuntime.Restore(DiesNextTick(prepared.ExportState(), actor), _ => new ActionCoverageRecorder(true));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var saved = world.ExportState();
        var deceased = Assert.Single(saved.DeceasedInhabitants!, person => person.InhabitantId == actor);
        Assert.Equal(HousingBlockers.NoHousehold, deceased.LastPhysical.Housing!.Blocker);
        Assert.All(saved.Inhabitants, person => Assert.Null(person.Housing));
        using var loaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(saved)));
        Assert.Equal(deceased.LastPhysical.Housing, Assert.Single(loaded.ExportState().DeceasedInhabitants!, person => person.InhabitantId == actor)
            .LastPhysical.Housing);
        loaded.Validate();
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(saved with { SchemaVersion = 31 }));
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(saved with { SchemaVersion = 32 }));
    }

    private static PrivateWorldRuntimeState DiesNextTick(PrivateWorldRuntimeState state, string actor)
    {
        var society = state.Society.Society;
        var maximum = society.Config.DayLifecycle!.MaximumDay;
        var birth = society.LifeTickAt(society.WorldTick) - maximum * society.Config.TicksPerLifecycleAge + 1;
        return state with
        {
            Society = state.Society with
            {
                Society = society with
                {
                    Inhabitants = society.Inhabitants.Select(person => person.Id == actor ? person with
                    {
                        BirthTick = birth,
                        BirthLifeTick = society.LifeClock is null ? null : birth,
                        AgeBand = SocietyAgeBand.Elder,
                        LastLifecycleYearChecked = maximum - 1
                    } : person).ToArray()
                }
            }
        };
    }
}

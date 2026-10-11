using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class DepartureAllowanceStorageTests
{
    private const string Household = "household:camp-alpha";
    private const string Food = "000-departure-food";
    private const string Store = "departure-store";
    private static readonly Lazy<byte[]> Prepared = new(() =>
    {
        using var generated = NormalPathWorld.CreateGenerated("departure-store-repro", _ => new Choices());
        generated.Pause();
        var state = generated.ExportState();
        var definition = generated.WorldContent.Buildings.Single(item => item.LocalId == "store-1x1");
        var inventory = state.Society.Society.Inventory;
        foreach (var cost in definition.BuildCosts)
            inventory = InventoryFixture.AddLot(inventory, "store-cost-" + cost.ResourceId,
                cost.ResourceId, Household, cost.Amount, storageBuildingId: "first-town-house-a");
        using var placing = PrivateWorldRuntime.Restore(WithInventory(state, inventory), _ => new Choices());
        var house = placing.WorldSimulation.Buildings.Single(item => item.InstanceId == "first-town-house-a");
        Assert.Contains(state.Map.Tiles.OrderBy(tile => state.Map.FootDistance(tile.Position, house.Position)),
            tile => placing.PlaceBuilding(Store, definition.CanonicalId, tile.Position, Household).Applied);
        return PrivateWorldRuntimeCodec.Encode(placing.ExportState());
    });

    [Theory]
    [InlineData("store", false, 3)]
    [InlineData("store", true, 3)]
    [InlineData("store", false, 2)]
    [InlineData("store", true, 2)]
    [InlineData("house", false, 3)]
    [InlineData("house", true, 3)]
    [InlineData("ground", false, 3)]
    [InlineData("ground", true, 3)]
    public async Task DepartureAllowanceKeepsItsLocationAndCanBeSavedThenPhysicallyCollected(
        string location, bool displaced, int portions)
    {
        var state = PrivateWorldRuntimeCodec.Decode(Prepared.Value);
        var actor = state.Society.Society.GetHousehold(Household).MemberIds[0];
        var store = state.WorldSimulation!.Buildings.Single(item => item.InstanceId == Store);
        var storage = location == "store" ? Store : location == "house" ? "first-town-house-a" : null;
        var ground = storage is null ? new InventoryGroundPosition(store.Position.X, store.Position.Y) : (InventoryGroundPosition?)null;
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, Food, "food", Household,
            portions, storageBuildingId: storage, groundPosition: ground);
        state = WithInventory(state, inventory) with
        {
            // Place the actor near the physical stock, with no urgent food competing with collection.
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { Position = store.Position, HungerBasisPoints = 9_000, LastDecisionContext = null }
                : person).ToArray(),
        };
        var choices = new Choices { Actor = actor, Wanted = displaced ? "safe_idle" : "household_leave" };
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)), _ => choices);
        var directory = Directory.CreateTempSubdirectory("departure-allowance-");
        try
        {
            var file = new PrivateWorldStateFile(Path.Combine(directory.FullName, "world.bin"), _ => choices);
            file.Save(world);
            var presence = new OwnerClientPresenceLease(TimeSpan.FromSeconds(30));
            presence.RecordAuthenticatedReconnect("owner");
            using var host = new PrivateWorldRuntimeService(world, file, presence);
            world.Resume();
            if (displaced) Assert.True(world.DisplaceAdult(actor));
            for (var tick = 0; tick < 8 && world.Society.GetInhabitant(actor).HouseholdId is not null; tick++)
                Assert.True(await host.TryAdvanceOnceAsync(), "An offered departure must remain saveable.");
            Assert.Null(world.Society.GetInhabitant(actor).HouseholdId);
            if (!displaced) Assert.True(choices.SelectedLeave);
            var departure = Assert.Single(world.Inhabitants.Single(person => person.InhabitantId == actor).Departures!);
            Assert.Equal(2, departure.AllowancePortions);
            var allowance = world.Society.Inventory.GetLot(Assert.Single(departure.AllowanceLotIds));
            Assert.Equal((actor, 2, storage, ground, (string?)null),
                (allowance.OwnerId, allowance.Quantity, allowance.StorageBuildingId, allowance.GroundPosition, allowance.CarrierId));
            Assert.Equal(portions - 2, world.Society.Inventory.Lots.Where(lot => lot.OwnerId == Household && lot.Id == Food).Sum(lot => lot.Quantity));
            Assert.False(world.DisplaceAdult(actor));
            world.Validate();
            for (var tick = 0; tick < 2; tick++) Assert.True(await host.TryAdvanceOnceAsync());
            var bytes = File.ReadAllBytes(file.Path);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), bytes);
            using var reloaded = file.LoadOrCreate(state.WorldSeed);
            Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(reloaded.ExportState()));
            choices.Wanted = "household_collect:" + allowance.Id;
            var saved = reloaded.ExportState();
            saved = saved with
            {
                Inhabitants = saved.Inhabitants.Select(person => person.InhabitantId == actor
                    ? person with { LastDecisionContext = null } : person).ToArray(),
            };
            using var collecting = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(saved)), _ => choices);
            collecting.Resume();
            for (var tick = 0; tick < 80 && !PersonalEquipmentRules.IsCarried(collecting.Society.Inventory.GetLot(allowance.Id), actor); tick++)
                Assert.True((await collecting.AdvanceOneTickAsync()).Advanced);
            var carried = collecting.Society.Inventory.GetLot(allowance.Id);
            Assert.True(PersonalEquipmentRules.IsCarried(carried, actor));
            Assert.Null(carried.StorageBuildingId);
            Assert.Null(carried.GroundPosition);
            Assert.Equal((actor, 2), (carried.OwnerId, carried.Quantity));
            Assert.Single(collecting.Inhabitants.Single(person => person.InhabitantId == actor).Departures!);
            var collected = PrivateWorldRuntimeCodec.Encode(collecting.ExportState());
            using var final = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(collected), _ => choices);
            Assert.Equal(collected, PrivateWorldRuntimeCodec.Encode(final.ExportState()));
        }
        finally { directory.Delete(recursive: true); }
    }

    [Theory]
    [InlineData("unrecorded")]
    [InlineData("wrong-owner")]
    [InlineData("wrong-household")]
    [InlineData("wrong-kind")]
    [InlineData("too-many")]
    public void ADepartureDoesNotAuthorizeUnrelatedPersonalStoreStock(string corruption)
    {
        var state = PrivateWorldRuntimeCodec.Decode(Prepared.Value);
        var actor = state.Society.Society.GetHousehold(Household).MemberIds[0];
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, Food, "food", Household, 3,
            storageBuildingId: "first-town-house-a");
        using var departing = PrivateWorldRuntime.Restore(WithInventory(state, inventory), _ => new Choices());
        Assert.True(departing.DisplaceAdult(actor));
        state = departing.ExportState();
        var departure = Assert.Single(state.Inhabitants.Single(person => person.InhabitantId == actor).Departures!);
        var id = Assert.Single(departure.AllowanceLotIds);
        inventory = InventoryFixture.Relocate(state.Society.Society.Inventory, "invalid-store", id, actor, 2, storageBuildingId: Store);
        state = WithInventory(state, inventory);
        if (corruption == "unrecorded")
            state = state with { Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor ? person with { Departures = null } : person).ToArray() };
        else if (corruption == "wrong-household")
            state = state with { WorldSimulation = state.WorldSimulation! with { Buildings = state.WorldSimulation.Buildings.Select(building => building.InstanceId == Store ? building with { HouseholdId = "household:camp-beta" } : building).ToArray() } };
        else
            state = WithInventory(state, inventory with
            {
                Lots = inventory.Lots.Select(lot => lot.Id != id ? lot : corruption switch
                {
                    "wrong-owner" => lot with { OwnerId = state.Society.Society.GetHousehold(Household).MemberIds[0] },
                    "wrong-kind" => lot with { ItemKind = "wood" },
                    _ => lot with { Quantity = 3 },
                }).ToArray(),
            });
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(state));
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(state));
    }

    [Fact]
    public async Task AnUncollectedStoreAllowanceRemainsPhysicalThroughDeathAndEstateSettlement()
    {
        var state = PrivateWorldRuntimeCodec.Decode(Prepared.Value);
        var actor = state.Society.Society.GetHousehold(Household).MemberIds[0];
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, Food, "food", Household, 3,
            storageBuildingId: Store);
        using var leaving = PrivateWorldRuntime.Restore(WithInventory(state, inventory), _ => new Choices());
        Assert.True(leaving.DisplaceAdult(actor));
        var allowanceId = Assert.Single(Assert.Single(leaving.Inhabitants.Single(person => person.InhabitantId == actor).Departures!).AllowanceLotIds);
        state = leaving.ExportState();
        var checkpoint = state.Society.Society;
        // Explicit lifecycle fixture: the departed adult reaches the normal
        // maximum age on the next tick; death and escrow use production paths.
        var lastDay = Assert.IsType<SocietyDayLifecycle>(checkpoint.Config.DayLifecycle).MaximumDay;
        var birth = checkpoint.LifeTickAt(checkpoint.WorldTick + 1) - lastDay * checkpoint.Config.TicksPerLifecycleAge;
        checkpoint = checkpoint with
        {
            Inhabitants = checkpoint.Inhabitants.Select(person => person.Id == actor ? person with
            {
                BirthTick = checkpoint.LifeClock is null ? birth : person.BirthTick,
                BirthLifeTick = checkpoint.LifeClock is null ? null : birth,
                AgeBand = SocietyAgeBand.Elder,
                LastLifecycleYearChecked = lastDay - 1,
            } : person).ToArray(),
        };
        state = state with { Society = state.Society with { Society = checkpoint } };
        using var dying = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)), _ => new Choices());
        dying.Resume();
        Assert.True((await dying.AdvanceOneTickNonBlockingAsync()).Advanced);
        var estate = Assert.Single(dying.Society.Estates);
        var frozen = Assert.Single(estate.FrozenLots!, lot => lot.LotId == allowanceId);
        Assert.Equal((Store, 2), (frozen.StorageBuildingId, frozen.Quantity));
        Assert.Equal((estate.Id, Store, 2), (dying.Society.Inventory.GetLot(allowanceId).OwnerId,
            dying.Society.Inventory.GetLot(allowanceId).StorageBuildingId, dying.Society.Inventory.GetLot(allowanceId).Quantity));
        var held = PrivateWorldRuntimeCodec.Encode(dying.ExportState());
        using var heldReload = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(held), _ => new Choices());
        Assert.Equal(held, PrivateWorldRuntimeCodec.Encode(heldReload.ExportState()));
        var missingPortion = dying.ExportState();
        missingPortion = WithInventory(missingPortion, missingPortion.Society.Society.Inventory with
        {
            Lots = missingPortion.Society.Society.Inventory.Lots.Select(lot => lot.Id == allowanceId
                ? lot with { Quantity = 1 } : lot).ToArray(),
        });
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(missingPortion));
        var forged = dying.ExportState();
        forged = forged with
        {
            Society = forged.Society with
            {
                Society = forged.Society.Society with
                {
                    Estates = forged.Society.Society.Estates.Select(item => item with
                    {
                        FrozenLots = item.FrozenLots!.Select(lot => lot.LotId == allowanceId
                            ? lot with { StorageBuildingId = "first-town-house-a" } : lot).ToArray(),
                    }).ToArray(),
                }
            },
        };
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(forged));
        state = heldReload.ExportState();
        state = state with
        {
            Society = state.Society with
            {
                Society = state.Society.Society with
                {
                    Estates = state.Society.Society.Estates.Select(item => item with
                    { ExpiryTick = state.Society.Society.WorldTick + 1 }).ToArray(),
                }
            },
        };
        var ready = PrivateWorldRuntimeCodec.Encode(state);
        using var settling = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(ready), _ => new Choices());
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(ready), _ => new Choices());
        for (var tick = 0; tick < 3; tick++)
        {
            Assert.True((await settling.AdvanceOneTickNonBlockingAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickNonBlockingAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(settling.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        }
        Assert.True(Assert.Single(settling.Society.Estates).Settled);
        var inherited = Assert.Single(settling.Society.Inventory.Lots, lot => lot.Id == allowanceId || lot.ProvenanceLotId == allowanceId);
        var town = Assert.Single(settling.ExportState().DeceasedInhabitants!, person => person.InhabitantId == actor).TownId;
        Assert.Equal((town, Store, 2, (string?)null), (inherited.OwnerId, inherited.StorageBuildingId, inherited.Quantity, inherited.CarrierId));
        var saved = PrivateWorldRuntimeCodec.Encode(settling.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved), _ => new Choices());
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StoreAllowanceSurvivesAnUnavailableWillHeirAndAnotherInheritance(bool namedHeirDies)
    {
        var state = PrivateWorldRuntimeCodec.Decode(Prepared.Value);
        var members = state.Society.Society.GetHousehold(Household).MemberIds;
        var actor = members[0];
        var beneficiary = members[1];
        var otherHeir = state.Society.Society.GetHousehold("household:camp-beta").MemberIds[0];
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, Food, "food", Household, 2,
            storageBuildingId: Store);
        using var leaving = PrivateWorldRuntime.Restore(WithInventory(state, inventory), _ => new Choices());
        Assert.True(leaving.DisplaceAdult(actor));
        if (namedHeirDies) Assert.True(leaving.DisplaceAdult(beneficiary));
        state = leaving.ExportState();
        if (namedHeirDies)
            state = state with
            {
                Society = state.Society with
                {
                    Society = SocietyFixture.CreateHousehold(
                state.Society.Society, "household:allowance-heirs", "The allowance heirs", [actor, beneficiary]).Checkpoint
                }
            };
        var names = (namedHeirDies ? new[] { beneficiary, otherHeir } : [beneficiary])
            .Select(id => state.Society.Society.GetInhabitant(id).Name).ToArray();
        var will = new PostDeathWillTests.WillProvider(CognitionWillContext.HeirsCandidateId, observation =>
            new CognitionWillChoice(names.Select(name => PostDeathWillTests.HeirKey(observation, name)).ToArray(),
                CognitionWillContext.EqualSplit));
        using var dying = PrivateWorldRuntime.Restore(DieOnNextTick(state, actor),
            id => id == actor ? will : new Choices());
        dying.Resume();
        Assert.True((await dying.AdvanceOneTickNonBlockingAsync()).Advanced);
        await will.Called.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await will.Completed.Task.WaitAsync(TimeSpan.FromSeconds(30));
        for (var tick = 0; tick < 8 && dying.Society.Estates.Single(item => item.DeceasedId == actor).WillStatus == "pending"; tick++)
            Assert.True((await dying.AdvanceOneTickNonBlockingAsync()).Advanced);
        var first = Assert.Single(dying.Society.Estates, item => item.DeceasedId == actor);
        Assert.Equal("accepted", first.WillStatus);
        Assert.Contains(first.WillBequests!, item => item.LotId == Food && item.HeirId == beneficiary);
        state = dying.ExportState();
        if (namedHeirDies)
        {
            Assert.Contains(first.BeneficiaryIds, id => id == beneficiary);
            using var unavailable = PrivateWorldRuntime.Restore(DieOnNextTick(state, otherHeir), _ => new Choices());
            Assert.True((await unavailable.AdvanceOneTickNonBlockingAsync()).Advanced);
            Assert.Equal(SocietyInhabitantStatus.Dead, unavailable.Society.GetInhabitant(otherHeir).Status);
            state = unavailable.ExportState();
        }
        var ready = PrivateWorldRuntimeCodec.Encode(DueOnNextTick(state, first.Id));
        using var settling = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(ready), _ => new Choices());
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(ready), _ => new Choices());
        for (var tick = 0; tick < 3; tick++)
        {
            Assert.True((await settling.AdvanceOneTickNonBlockingAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickNonBlockingAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(settling.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        }
        Assert.True(settling.Society.GetEstate(first.Id).Settled);
        var inherited = Assert.Single(settling.Society.Inventory.Lots, lot => lot.ProvenanceLotId == Food);
        Assert.Equal((beneficiary, Store, 2, (string?)null),
            (inherited.OwnerId, inherited.StorageBuildingId, inherited.Quantity, inherited.CarrierId));
        if (namedHeirDies) return;

        // The heir has no departure record. Its next real death must preserve
        // the first estate's physical allowance rather than requiring a new one.
        Assert.Null(settling.Inhabitants.Single(person => person.InhabitantId == beneficiary).Departures);
        using var secondDeath = PrivateWorldRuntime.Restore(DieOnNextTick(settling.ExportState(), beneficiary), _ => new Choices());
        Assert.True((await secondDeath.AdvanceOneTickNonBlockingAsync()).Advanced);
        var second = Assert.Single(secondDeath.Society.Estates, item => item.DeceasedId == beneficiary);
        Assert.Equal((second.Id, Store, 2), (secondDeath.Society.Inventory.GetLot(inherited.Id).OwnerId,
            secondDeath.Society.Inventory.GetLot(inherited.Id).StorageBuildingId,
            secondDeath.Society.Inventory.GetLot(inherited.Id).Quantity));
        var held = PrivateWorldRuntimeCodec.Encode(secondDeath.ExportState());
        using var reloaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(held), _ => new Choices());
        Assert.Equal(held, PrivateWorldRuntimeCodec.Encode(reloaded.ExportState()));
        var unrecorded = reloaded.ExportState() with
        {
            DeceasedInhabitants = reloaded.ExportState().DeceasedInhabitants!.Select(person => person.InhabitantId == actor
                ? person with { LastPhysical = person.LastPhysical with { Departures = null } } : person).ToArray(),
        };
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(unrecorded));
        var secondReady = PrivateWorldRuntimeCodec.Encode(DueOnNextTick(reloaded.ExportState(), second.Id));
        using var secondSettlement = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(secondReady), _ => new Choices());
        using var secondReplay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(secondReady), _ => new Choices());
        for (var tick = 0; tick < 3; tick++)
        {
            Assert.True((await secondSettlement.AdvanceOneTickNonBlockingAsync()).Advanced);
            Assert.True((await secondReplay.AdvanceOneTickNonBlockingAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(secondSettlement.ExportState()), PrivateWorldRuntimeCodec.Encode(secondReplay.ExportState()));
        }
        Assert.True(secondSettlement.Society.GetEstate(second.Id).Settled);
        var final = secondSettlement.Society.Inventory.GetLot(inherited.Id);
        Assert.Equal((Store, 2, (string?)null), (final.StorageBuildingId, final.Quantity, final.CarrierId));
    }

    [Fact]
    public void ARecordedMultiLotStoreAllowanceCannotExceedItsTotalPortions()
    {
        var state = PrivateWorldRuntimeCodec.Decode(Prepared.Value);
        var actor = state.Society.Society.GetHousehold(Household).MemberIds[0];
        var inventory = state.Society.Society.Inventory;
        foreach (var suffix in new[] { "-a", "-b" })
            inventory = InventoryFixture.AddLot(inventory, Food + suffix, "food", Household, 1, storageBuildingId: Store);
        using var leaving = PrivateWorldRuntime.Restore(WithInventory(state, inventory), _ => new Choices());
        Assert.True(leaving.DisplaceAdult(actor));
        state = leaving.ExportState();
        var departure = Assert.Single(leaving.Inhabitants.Single(person => person.InhabitantId == actor).Departures!);
        Assert.Equal(2, departure.AllowanceLotIds.Count);
        Assert.Equal(2, departure.AllowancePortions);
        Assert.Equal(2, state.Society.Society.Inventory.Lots.Where(lot => departure.AllowanceLotIds.Contains(lot.Id)).Sum(lot => lot.Quantity));
        var bytes = PrivateWorldRuntimeCodec.Encode(state);
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), _ => new Choices());
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        var forged = WithInventory(state, state.Society.Society.Inventory with
        {
            Lots = state.Society.Society.Inventory.Lots.Select(lot => departure.AllowanceLotIds.Contains(lot.Id)
                ? lot with { Quantity = 2 } : lot).ToArray(),
        });
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(forged));
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(forged));
    }

    private static PrivateWorldRuntimeState DieOnNextTick(PrivateWorldRuntimeState state, string actor)
    {
        var checkpoint = state.Society.Society;
        var lastDay = Assert.IsType<SocietyDayLifecycle>(checkpoint.Config.DayLifecycle).MaximumDay;
        var birth = checkpoint.LifeTickAt(checkpoint.WorldTick + 1) - lastDay * checkpoint.Config.TicksPerLifecycleAge;
        return state with
        {
            Society = state.Society with
            {
                Society = checkpoint with
                {
                    Inhabitants = checkpoint.Inhabitants.Select(person => person.Id == actor ? person with
                    {
                        BirthTick = checkpoint.LifeClock is null ? birth : person.BirthTick,
                        BirthLifeTick = checkpoint.LifeClock is null ? null : birth,
                        AgeBand = SocietyAgeBand.Elder,
                        LastLifecycleYearChecked = lastDay - 1,
                    } : person).ToArray(),
                }
            }
        };
    }

    private static PrivateWorldRuntimeState DueOnNextTick(PrivateWorldRuntimeState state, string estateId) =>
        state with
        {
            Society = state.Society with
            {
                Society = state.Society.Society with
                {
                    Estates = state.Society.Society.Estates.Select(item => item.Id == estateId
                        ? item with { ExpiryTick = state.Society.Society.WorldTick + 1 } : item).ToArray(),
                }
            }
        };

    private static PrivateWorldRuntimeState WithInventory(PrivateWorldRuntimeState state, InventoryCheckpoint inventory) =>
        state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } };

    private sealed class Choices : IDecisionProvider
    {
        public string? Actor { get; init; }
        public string Wanted { get; set; } = "safe_idle";
        public bool SelectedLeave { get; private set; }
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var observation = request.Observation;
            var selected = observation.Candidates.FirstOrDefault(candidate => observation.InhabitantId == Actor && candidate.Id == Wanted)
                ?? observation.Candidates.Single(candidate => candidate.Id == "safe_idle");
            if (selected.Id == "household_leave") SelectedLeave = true;
            return new DeterministicDecisionProvider().DecideAsync(request with { Observation = observation with { Candidates = [selected] } }, cancellationToken);
        }
    }
}

using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.World;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class HouseholdDepartureTests
{
    private const string Alpha = "household:camp-alpha";
    private const string Beta = "household:camp-beta";

    [Theory]
    [InlineData("padded_coat", false)]
    [InlineData("sack", false)]
    [InlineData("padded_coat", true)]
    [InlineData("sack", true)]
    public async Task AHouseholdLeaverCollectsPersonalEquipmentBeforeEquippingIt(string kind, bool onGround)
    {
        var provider = new Choices();
        using var initial = NormalPathWorld.CreateGenerated("departure-stored-equipment", _ => provider);
        initial.Pause();
        var actor = initial.Society.GetHousehold(Alpha).MemberIds[0];
        var house = initial.WorldSimulation.Buildings.Single(building => building.InstanceId == "first-town-house-a");
        const string lotId = "departed-personal-equipment";
        var state = initial.ExportState();
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, lotId, kind, actor, 1,
            storageBuildingId: onGround ? null : house.InstanceId,
            groundPosition: onGround ? new(house.Position.X, house.Position.Y) : null);
        state = WithInventory(state, inventory) with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { Position = house.Position, HungerBasisPoints = 9_000, LastDecisionContext = null }
                : person).ToArray(),
        };
        using var departing = PrivateWorldRuntime.Restore(state, _ => provider);
        provider.Wanted[actor] = "household_leave";
        departing.Resume();
        await AdvanceUntil(departing, () => departing.Society.GetInhabitant(actor).HouseholdId is null);
        departing.Pause();
        state = departing.ExportState();
        var systems = state.WorldSystems!;
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { LastDecisionContext = null } : person).ToArray(),
            WorldSystems = systems with
            {
                RegionalWeather = null,
                Config = systems.Config with
                {
                    WeatherProfiles = Enum.GetValues<SeasonKind>().Select(season =>
                        new WeatherProfile(season, 0, 0, 0, 0, 1)).ToArray(),
                },
                Climate = systems.Climate with { Weather = WeatherKind.Snow },
            },
        };
        var saved = PrivateWorldRuntimeCodec.Encode(state);
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved), _ => provider);
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        var action = kind == "sack" ? "equip_carry_aid" : "wear_clothing";
        provider.Wanted[actor] = action;
        world.Resume();
        for (var tick = 0; tick < 3; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            world.Validate();
            Assert.DoesNotContain(action, provider.Offered[actor]);
            Assert.False(PersonalEquipmentRules.IsCarried(world.Society.Inventory.GetLot(lotId), actor));
        }
        await VerifyHostAndSaveAsync(world);
        provider.Wanted[actor] = "household_collect:" + lotId;
        // A deliberate change of choice needs a fresh decision, not altered stock.
        state = world.ExportState();
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { LastDecisionContext = null } : person).ToArray(),
        };
        using var collecting = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(
            PrivateWorldRuntimeCodec.Encode(state)), _ => provider);
        await AdvanceUntil(collecting, () => PersonalEquipmentRules.IsCarried(collecting.Society.Inventory.GetLot(lotId), actor));
        provider.Wanted[actor] = action;
        await AdvanceUntil(collecting, () => kind == "sack"
            ? collecting.Inhabitants.Single(person => person.InhabitantId == actor).Equipment?.CarryAidLotId == lotId
            : collecting.Inhabitants.Single(person => person.InhabitantId == actor).Equipment?.ClothingLotId == lotId);
        collecting.Validate();
        var unit = collecting.Society.Inventory.GetLot(lotId);
        Assert.Equal((actor, 1), (unit.OwnerId, unit.Quantity));
        Assert.Null(unit.StorageBuildingId);
        Assert.Null(unit.GroundPosition);
        var equipped = PrivateWorldRuntimeCodec.Encode(collecting.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(equipped), _ => provider);
        Assert.Equal(equipped, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        await VerifyHostAndSaveAsync(restored);

        async Task VerifyHostAndSaveAsync(PrivateWorldRuntime current)
        {
            var directory = Directory.CreateTempSubdirectory("departed-equipment-host-");
            try
            {
                var file = new PrivateWorldStateFile(Path.Combine(directory.FullName, "world.bin"), _ => provider);
                file.Save(current);
                var presence = new OwnerClientPresenceLease(TimeSpan.FromSeconds(30));
                presence.RecordAuthenticatedReconnect("owner");
                using var service = new PrivateWorldRuntimeService(current, file, presence);
                var before = current.WorldTick;
                for (var tick = 0; tick < 3; tick++)
                    Assert.True(await service.TryAdvanceOnceAsync(), "The native host must keep advancing and writing recovery checkpoints.");
                Assert.Equal(before + 3, current.WorldTick);
                Assert.False(current.Society.IsPaused);
                current.Validate();
                var bytes = File.ReadAllBytes(file.Path);
                Assert.Equal(PrivateWorldRuntimeCodec.Encode(current.ExportState()), bytes);
                using var fromDisk = file.LoadOrCreate(state.WorldSeed);
                Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(fromDisk.ExportState()));
            }
            finally { directory.Delete(recursive: true); }
        }
    }

    [Fact]
    public async Task CurrentResidentCanDeliberatelyCollectPersonalGoodsAfterReload()
    {
        var provider = new Choices();
        using var initial = NormalPathWorld.CreateGenerated("personal-home-collection", _ => provider);
        initial.Pause();
        var actor = initial.Society.GetHousehold(Alpha).MemberIds[0];
        var house = initial.WorldSimulation.Buildings.Single(building => building.InstanceId == "first-town-house-a");
        var state = initial.ExportState();
        var inventory = state.Society.Society.Inventory;
        var kinds = new[] { "field_map", "field_record", "food", "wooden_axe", "clothing" };
        var fact = new AgentKnowledgeFact("personal-collection-fact", actor, actor, house.Position,
            state.Map.TerrainKindAt(house.Position)!.Value.ToString(), [], initial.WorldTick, "firsthand");
        var artifacts = new List<AgentKnowledgeArtifact>();
        foreach (var kind in kinds.Take(2))
        {
            var projectId = "personal-writing-" + kind;
            var paperId = "personal-writing-paper-" + kind;
            var reservationId = projectId + ":paper";
            inventory = InventoryFixture.AddLot(inventory, paperId, "paper", actor, 1, initial.WorldTick);
            inventory = InventoryFixture.Reserve(inventory, reservationId, actor, paperId, 1,
                $"knowledge_writing:{projectId}:paper", long.MaxValue);
            inventory = InventoryFixture.ConsumeReservation(inventory, reservationId);
            artifacts.Add(new AgentKnowledgeArtifact(
                "personal-artifact-" + kind, actor, "personal-" + kind, kind, "Personal field notes",
                initial.WorldTick, [fact])
            {
                WritingProjectId = projectId,
                Materials = [new AgentKnowledgeMaterial(reservationId, paperId, "paper", 1)],
            });
        }
        foreach (var kind in kinds)
            inventory = InventoryFixture.AddLot(inventory, "personal-" + kind, kind, actor, 1,
                storageBuildingId: house.InstanceId);
        state = WithInventory(state, inventory) with
        {
            Knowledge = new PrivateWorldKnowledgeState([fact], artifacts),
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { Position = house.Position, HungerBasisPoints = 9_000, LastDecisionContext = null }
                : person).ToArray(),
        };
        var saved = PrivateWorldRuntimeCodec.Encode(state);
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved), _ => provider);
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        world.Resume();
        foreach (var kind in kinds)
        {
            var lotId = "personal-" + kind;
            provider.Wanted[actor] = "household_collect:" + lotId;
            await AdvanceUntil(world, () => PersonalEquipmentRules.IsCarried(world.Society.Inventory.GetLot(lotId), actor));
            Assert.Equal(actor, world.Society.Inventory.GetLot(lotId).OwnerId);
            Assert.Equal(Alpha, world.Society.GetInhabitant(actor).HouseholdId);
        }
        world.Validate();
        var collected = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(collected), _ => provider);
        Assert.Equal(collected, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }

    [Fact]
    public async Task VoluntaryDeparturePreservesPropertyTownAndOnceOnlyPhysicalAllowanceAcrossReload()
    {
        var provider = new Choices();
        using var initial = NormalPathWorld.CreateGenerated("departure-normal", _ => provider);
        initial.Pause();
        var actor = initial.Society.GetHousehold(Alpha).MemberIds[0];
        var house = initial.WorldSimulation.Buildings.Single(building => building.InstanceId == "first-town-house-a");
        var state = initial.ExportState();
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "personal-coat", "padded_coat", actor, 1,
            storageBuildingId: house.InstanceId);
        inventory = InventoryFixture.AddLot(inventory, "borrowed-axe", "wooden_axe", Alpha, 1);
        inventory = InventoryFixture.Relocate(inventory, "borrow", "borrowed-axe", Alpha, 1, actor);
        state = WithInventory(state, inventory);
        using var world = PrivateWorldRuntime.Restore(state, _ => provider);
        provider.Wanted[actor] = "household_leave";
        var town = world.Towns.Single().ResidentIds.ToArray();
        world.Resume();
        await AdvanceUntil(world, () => world.Society.GetInhabitant(actor).HouseholdId is null);
        provider.Wanted[actor] = "safe_idle";
        var departure = Assert.Single(world.Inhabitants.Single(person => person.InhabitantId == actor).Departures!);
        Assert.InRange(departure.AllowancePortions, 0, 2);
        Assert.Equal("voluntary", departure.Cause);
        Assert.Equal(actor, world.Society.Inventory.GetLot("personal-coat").OwnerId);
        Assert.Equal(house.InstanceId, world.Society.Inventory.GetLot("personal-coat").StorageBuildingId);
        Assert.Equal(Alpha, world.Society.Inventory.GetLot("borrowed-axe").OwnerId);
        Assert.Equal(actor, world.Society.Inventory.GetLot("borrowed-axe").CarrierId);
        Assert.Equal(town, world.Towns.Single().ResidentIds);
        Assert.Equal(Alpha, world.WorldSimulation.Buildings.Single(item => item.InstanceId == house.InstanceId).HouseholdId);
        Assert.All(departure.AllowanceLotIds, id =>
        {
            var lot = world.Society.Inventory.GetLot(id);
            Assert.Equal(actor, lot.OwnerId);
            Assert.False(PersonalEquipmentRules.IsCarried(lot, actor));
        });
        world.Pause();
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), _ => provider);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        Assert.False(restored.DisplaceAdult(actor));
        Assert.Single(restored.Inhabitants.Single(person => person.InhabitantId == actor).Departures!);
        provider.Wanted[actor] = "household_collect:personal-coat";
        restored.Resume();
        await AdvanceUntil(restored, () => PersonalEquipmentRules.IsCarried(restored.Society.Inventory.GetLot("personal-coat"), actor), 180);
        Assert.Null(restored.Society.GetInhabitant(actor).HouseholdId);
        Assert.Equal(actor, restored.Society.Inventory.GetLot("personal-coat").OwnerId);
        provider.Wanted[actor] = "household_return:borrowed-axe";
        await AdvanceUntil(restored, () => restored.Society.Inventory.GetLot("borrowed-axe").StorageBuildingId == house.InstanceId, 180);
        Assert.Equal(Alpha, restored.Society.Inventory.GetLot("borrowed-axe").OwnerId);
        Assert.Single(restored.Inhabitants.Single(person => person.InhabitantId == actor).Departures!);
    }

    [Fact]
    public async Task PublicWorkKeepsOriginalHouseholdOwnerAcrossLeavingFoundingAndReload()
    {
        var provider = new Choices();
        using var initial = NormalPathWorld.CreateGenerated("departure-public-work", _ => provider);
        initial.Pause();
        var actor = initial.Society.GetHousehold(Alpha).MemberIds[0];
        var workshop = initial.WorldContent.Buildings.Single(building => building.LocalId == "workshop");
        var recipe = initial.WorldContent.Recipes.Single(item => item.LocalId == "tools");
        BuildingPlacementResult? placed = null;
        foreach (var point in initial.ExportState().Map.Tiles.Select(tile => tile.Position))
        {
            var attempt = initial.PlaceBuilding("departure-public-workshop", workshop.CanonicalId, point);
            if (!attempt.Applied) continue;
            placed = attempt;
            break;
        }
        Assert.NotNull(placed);
        var state = initial.ExportState();
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { Position = placed.Position, LastDecisionContext = null } : person).ToArray(),
        };
        using var world = PrivateWorldRuntime.Restore(state, _ => provider);
        var started = world.StartProduction(recipe.CanonicalId, placed.InstanceId, actor);
        Assert.True(started.Applied, started.Failure);
        Assert.Equal(Alpha, world.WorldSimulation.ProductionJobs.Single(job => job.JobId == started.JobId).OwnerId);
        foreach (var owner in new string?[] { Beta, null })
        {
            var invalidOwner = world.ExportState();
            invalidOwner = invalidOwner with
            {
                WorldSimulation = invalidOwner.WorldSimulation! with
                {
                    ProductionJobs = invalidOwner.WorldSimulation!.ProductionJobs.Select(job => job.JobId == started.JobId
                        ? job with { OwnerId = owner } : job).ToArray(),
                },
            };
            Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(invalidOwner));
        }
        provider.Wanted[actor] = "household_leave";
        world.Resume();
        await AdvanceUntil(world, () => world.Society.GetInhabitant(actor).HouseholdId is null);
        world.Pause();
        var homeless = world.ExportState();
        homeless = homeless with
        {
            Inhabitants = homeless.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with
                {
                    LastDecisionContext = null,
                    Housing = new(Refusals:
                    [new(Alpha, world.WorldTick), new(Beta, world.WorldTick)])
                } : person).ToArray(),
        };
        using var forming = PrivateWorldRuntime.Restore(homeless, _ => provider);
        provider.Wanted[actor] = "household_found";
        forming.Resume();
        await AdvanceUntil(forming, () => forming.Society.GetInhabitant(actor).HouseholdId is not null);
        Assert.NotEqual(Alpha, forming.Society.GetInhabitant(actor).HouseholdId);
        Assert.Equal(WorldProductionJobState.Running, forming.WorldSimulation.ProductionJobs.Single(job => job.JobId == started.JobId).State);
        forming.Pause();
        var saved = PrivateWorldRuntimeCodec.Encode(forming.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved), _ => provider);
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        provider.Wanted[actor] = "safe_idle";
        restored.Resume();
        await AdvanceUntil(restored, () => restored.WorldSimulation.ProductionJobs.Single(job => job.JobId == started.JobId).State == WorldProductionJobState.Completed);
        Assert.Equal(Alpha, restored.Society.Inventory.GetLot(started.JobId + ":output:00").OwnerId);
    }

    [Fact]
    public void DisplacementDoesNotInventFoodOrTouchReservationsAndLastAdultPropertySurvives()
    {
        using var initial = NormalPathWorld.CreateGenerated("departure-reserved", _ => new Choices());
        initial.Pause();
        var state = initial.ExportState();
        var inventory = state.Society.Society.Inventory;
        inventory = inventory with { Lots = inventory.Lots.Where(lot => lot.OwnerId != Alpha || lot.ItemKind != "food").ToArray() };
        inventory = InventoryFixture.AddLot(inventory, "departure-food", "food", Alpha, 3);
        inventory = InventoryFixture.Reserve(inventory, "care-food", Alpha, "departure-food", 2, "care", long.MaxValue);
        state = WithInventory(state, inventory);
        using var world = PrivateWorldRuntime.Restore(state);
        var members = world.Society.GetHousehold(Alpha).MemberIds.ToArray();
        Assert.True(world.DisplaceAdult(members[0]));
        var first = Assert.Single(world.Inhabitants.Single(person => person.InhabitantId == members[0]).Departures!);
        Assert.Equal(1, first.AllowancePortions);
        Assert.Equal(2, world.Society.Inventory.GetLot("departure-food").Quantity);
        Assert.Equal(InventoryReservationState.Reserved, world.Society.Inventory.GetReservation("care-food").State);
        Assert.True(world.DisplaceAdult(members[1]));
        Assert.Empty(world.Society.GetHousehold(Alpha).MemberIds);
        Assert.Equal(0, Assert.Single(world.Inhabitants.Single(person => person.InhabitantId == members[1]).Departures!).AllowancePortions);
        Assert.Contains(world.WorldSimulation.Buildings, item => item.HouseholdId == Alpha);
        Assert.Equal(Alpha, world.Society.Inventory.GetLot("departure-food").OwnerId);
        Assert.False(world.DisplaceAdult(members[1]));
        Assert.Equal(2, world.Society.Inventory.GetLot("departure-food").Quantity);
    }

    [Fact]
    public async Task HomelessAdultChecksExistingHomesBeforeSoloFormationWithoutFreeResources()
    {
        var provider = new Choices();
        using var initial = NormalPathWorld.CreateGenerated("departure-solo", _ => provider);
        var actor = initial.Society.GetHousehold(Alpha).MemberIds[0];
        Assert.True(initial.DisplaceAdult(actor));
        provider.Wanted[actor] = "household_found";
        await AdvanceUntil(initial, () => provider.Offered.ContainsKey(actor));
        Assert.DoesNotContain("household_found", provider.Offered[actor]);
        Assert.Contains(provider.Offered[actor], item => item.StartsWith("household_ask:", StringComparison.Ordinal));
        initial.Pause();
        var state = initial.ExportState();
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { Housing = new(Refusals: [new(Alpha, state.Society.Society.WorldTick), new(Beta, state.Society.Society.WorldTick)]) }
                : person).ToArray(),
        };
        var beforeLots = state.Society.Society.Inventory.Lots.ToArray();
        var beforeBuildings = state.WorldSimulation!.Buildings.ToArray();
        using var world = PrivateWorldRuntime.Restore(state, _ => provider);
        world.Resume();
        await AdvanceUntil(world, () => world.Society.GetInhabitant(actor).HouseholdId is { } home && home != Alpha && home != Beta);
        var solo = world.Society.GetHousehold(world.Society.GetInhabitant(actor).HouseholdId!);
        Assert.Equal([actor], solo.MemberIds);
        Assert.DoesNotContain(world.WorldSimulation.Buildings, item => item.HouseholdId == solo.Id);
        Assert.Equal(beforeBuildings.Length, world.WorldSimulation.Buildings.Count);
        Assert.Equal(beforeLots.Sum(lot => lot.Quantity), world.Society.Inventory.Lots.Sum(lot => lot.Quantity));
        Assert.NotNull(world.Inhabitants.Single(person => person.InhabitantId == actor).Housing?.Blocker);
    }

    [Fact]
    public async Task CompleteCareGroupNeedsRoomAndUnanimousAdmissionAndCareCanBeExplicitlyReassigned()
    {
        var provider = new Choices();
        using var initial = NormalPathWorld.CreateGenerated("departure-care-normal", _ => provider);
        initial.Pause();
        var actor = initial.Society.GetHousehold(Alpha).MemberIds[0];
        var child = initial.Society.GetHousehold(Alpha).MemberIds[1];
        var state = WithDependent(initial.ExportState(), actor, child);
        using var world = PrivateWorldRuntime.Restore(state, _ => provider);
        Assert.False(world.DisplaceAdult(actor));
        Assert.Equal(Alpha, world.Society.GetInhabitant(actor).HouseholdId);
        provider.Wanted[actor] = "household_leave";
        world.Resume();
        await AdvanceUntil(world, () => world.Society.GetInhabitant(actor).HouseholdId is null);
        Assert.Null(world.Society.GetInhabitant(child).HouseholdId);
        Assert.True(SocietyFixture.HasActivePrimaryCaregiver(world.Society, child));
        provider.Wanted[actor] = "safe_idle";
        provider.Offered.TryRemove(actor, out _);
        await AdvanceUntil(world, () => provider.Offered.ContainsKey(actor));
        Assert.DoesNotContain("household_ask:" + Beta, provider.Offered[actor]);
        Assert.Contains("household_found", provider.Offered[actor]);
        var betaMembers = world.Society.GetHousehold(Beta).MemberIds.ToArray();
        Assert.True(world.DisplaceAdult(betaMembers[1]));
        provider.Wanted[actor] = "household_ask:" + Beta;
        provider.Wanted[betaMembers[0]] = "household_admit:" + actor;
        await AdvanceUntil(world, () => world.Society.GetInhabitant(actor).HouseholdId == Beta, 160);
        provider.Wanted[actor] = "safe_idle";
        Assert.Equal(Beta, world.Society.GetInhabitant(child).HouseholdId);
        Assert.Equal(actor, world.Society.GetInhabitant(child).PrimaryCaregiverId);
        Assert.Contains(child, world.Society.GetHousehold(Beta).MemberIds);
        Assert.Null(world.Inhabitants.Single(person => person.InhabitantId == child).Housing);
        world.Pause();
        var saved = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved), _ => provider);
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        provider.Wanted[betaMembers[0]] = "household_accept_care:" + child;
        restored.Resume();
        await AdvanceUntil(restored, () => restored.Society.GetInhabitant(child).PrimaryCaregiverId == betaMembers[0], 160);
        provider.Wanted[actor] = "household_leave";
        await AdvanceUntil(restored, () => restored.Society.GetInhabitant(actor).HouseholdId is null, 160);
        Assert.Equal(Beta, restored.Society.GetInhabitant(child).HouseholdId);
        Assert.True(SocietyFixture.HasActivePrimaryCaregiver(restored.Society, child));
        Assert.Equal(2, restored.Inhabitants.Single(person => person.InhabitantId == actor).Departures!.Count);
    }

    [Fact]
    public async Task PersonalCollectionUsesLimitedLoadsAndRepeatedTripsWithoutAnotherAllowance()
    {
        var provider = new Choices();
        using var initial = NormalPathWorld.CreateGenerated("departure-loads", _ => provider);
        initial.Pause();
        var actor = initial.Society.GetHousehold(Alpha).MemberIds[0];
        var house = initial.WorldSimulation.Buildings.Single(building => building.InstanceId == "first-town-house-a");
        var state = initial.ExportState();
        var inventory = state.Society.Society.Inventory;
        inventory = inventory with { Lots = inventory.Lots.Where(lot => lot.OwnerId != actor).ToArray() };
        inventory = InventoryFixture.AddLot(inventory, "own-coats", "clothing", actor, 3, storageBuildingId: house.InstanceId);
        inventory = InventoryFixture.AddLot(inventory, "carried-berries", "berries", actor, 7);
        state = WithInventory(state, inventory) with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { Equipment = null, HungerBasisPoints = 3_500, Position = house.Position, LastDecisionContext = null }
                : person).ToArray(),
        };
        using var world = PrivateWorldRuntime.Restore(state, _ => provider);
        Assert.True(world.DisplaceAdult(actor));
        provider.Wanted[actor] = "household_collect:own-coats";
        world.Resume();
        await AdvanceUntil(world, () => world.Society.Inventory.GetLot("own-coats").Quantity == 2);
        provider.Wanted[actor] = "safe_idle";
        Assert.Equal(8, PersonalEquipmentRules.CarriedQuantity(world.Society.Inventory, actor, null));
        Assert.Equal(3, world.Society.Inventory.Lots.Where(lot => lot.ItemKind == "clothing" && lot.OwnerId == actor).Sum(lot => lot.Quantity));
        var departure = Assert.Single(world.Inhabitants.Single(person => person.InhabitantId == actor).Departures!);
        world.Pause();
        var saved = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved), _ => provider);
        provider.Wanted[actor] = "consume_food";
        restored.Resume();
        await AdvanceUntil(restored, () => restored.Society.Inventory.GetLot("carried-berries").Quantity < 7, 160);
        provider.Wanted[actor] = "household_collect:own-coats";
        await AdvanceUntil(restored, () => restored.Society.Inventory.GetLot("own-coats").Quantity == 1 ||
            restored.Society.Inventory.GetLot("own-coats").StorageBuildingId is null, 160);
        Assert.Equal(departure.AllowancePortions, Assert.Single(restored.Inhabitants.Single(person => person.InhabitantId == actor).Departures!).AllowancePortions);
        Assert.Equal(3, restored.Society.Inventory.Lots.Where(lot => lot.ItemKind == "clothing" && lot.OwnerId == actor).Sum(lot => lot.Quantity));
    }

    [Fact]
    public async Task DepartureLeavesPotFoodSealedAndCollectsAnOwnPotWithItsContents()
    {
        var provider = new Choices();
        using var initial = NormalPathWorld.CreateGenerated("departure-pots", _ => provider);
        initial.Pause();
        var actor = initial.Society.GetHousehold(Alpha).MemberIds[0];
        var house = initial.WorldSimulation.Buildings.Single(building => building.InstanceId == "first-town-house-a");
        var state = initial.ExportState();
        var inventory = state.Society.Society.Inventory;
        // Only food sealed in a pot remains, so the allowance must leave it alone.
        inventory = inventory with
        {
            Lots = inventory.Lots.Where(lot => lot.OwnerId != actor && !(lot.OwnerId == Alpha &&
                lot.ItemKind is "food" or "fruit" or "berries" or "wild_greens" or "cultivated_greens")).ToArray(),
        };
        inventory = InventoryFixture.AddLot(inventory, "household-pot", InventoryContainerRules.StoragePot, Alpha, 1,
            storageBuildingId: house.InstanceId);
        inventory = InventoryFixture.AddLot(inventory, "household-pot-berries", "berries", Alpha, 3,
            storageBuildingId: house.InstanceId, containerLotId: "household-pot");
        inventory = InventoryFixture.AddLot(inventory, "own-pot", InventoryContainerRules.StoragePot, actor, 1,
            storageBuildingId: house.InstanceId);
        inventory = InventoryFixture.AddLot(inventory, "own-pot-berries", "berries", actor, 2,
            storageBuildingId: house.InstanceId, containerLotId: "own-pot");
        state = WithInventory(state, inventory) with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { Equipment = null, Position = house.Position, LastDecisionContext = null }
                : person).ToArray(),
        };
        using var world = PrivateWorldRuntime.Restore(state, _ => provider);

        Assert.True(world.DisplaceAdult(actor));
        Assert.Equal(0, Assert.Single(world.Inhabitants.Single(person => person.InhabitantId == actor).Departures!).AllowancePortions);
        var sealedFood = world.Society.Inventory.GetLot("household-pot-berries");
        Assert.Equal((Alpha, "household-pot", 3), (sealedFood.OwnerId, sealedFood.ContainerLotId, sealedFood.Quantity));

        provider.Wanted[actor] = "household_collect:own-pot";
        world.Resume();
        await AdvanceUntil(world, () => PersonalEquipmentRules.IsCarried(world.Society.Inventory.GetLot("own-pot"), actor));
        // The pot is offered whole; its contents are never collected on their own.
        Assert.DoesNotContain("household_collect:own-pot-berries", provider.Offered[actor]);
        var carriedBerries = world.Society.Inventory.GetLot("own-pot-berries");
        Assert.Equal(("own-pot", (string?)null), (carriedBerries.ContainerLotId, carriedBerries.StorageBuildingId));
        Assert.Equal(world.Society.Inventory.GetLot("own-pot").CarrierId, carriedBerries.CarrierId);
        Assert.Equal(3, PersonalEquipmentRules.CarriedQuantity(world.Society.Inventory, actor, null));
        world.Pause();
        var saved = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved), _ => provider);
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }

    [Fact]
    public async Task LeavingRevokesPrivateWorkAccessButPreservesOldHouseholdJobsAndReservations()
    {
        var provider = new Choices();
        using var initial = NormalPathWorld.CreateGenerated("departure-private-work", _ => provider);
        initial.Pause();
        var actor = initial.Society.GetHousehold(Alpha).MemberIds[0];
        var house = initial.WorldSimulation.Buildings.Single(building => building.InstanceId == "first-town-house-a");
        // Keep two private material reservations without depending on recipe catalogue order.
        var recipe = initial.WorldContent.Recipes.Single(item => item.LocalId == "weave-basket" &&
            item.WorkstationBuildingId == house.DefinitionId);
        var state = initial.ExportState();
        var inventory = state.Society.Society.Inventory;
        var reservationIds = new List<string>();
        for (var index = 0; index < recipe.Inputs.Count; index++)
        {
            var input = recipe.Inputs[index];
            var lotId = $"private-work-input-{index}";
            var reservationId = $"private-work-reservation-{index}";
            inventory = InventoryFixture.AddLot(inventory, lotId, input.ResourceId, Alpha, input.Amount,
                storageBuildingId: house.InstanceId);
            inventory = InventoryFixture.Reserve(inventory, reservationId, Alpha, lotId, input.Amount, "work", initial.WorldTick + 30);
            reservationIds.Add(reservationId);
        }
        state = WithInventory(state, inventory) with
        {
            WorldSimulation = state.WorldSimulation! with
            {
                ProductionJobs = [new WorldProductionJob("departure-private-job", recipe.CanonicalId, house.InstanceId,
                    actor, initial.WorldTick, initial.WorldTick + 30, WorldProductionJobState.Running, reservationIds) { OwnerId = Alpha }],
            },
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor ? person with
            {
                Project = new(TownConstructionCandidateIds.Recipe(recipe.CanonicalId), recipe.DisplayName,
                    initial.WorldTick, "waiting", JobId: "departure-private-job", LastTransitionTick: initial.WorldTick),
            } : person).ToArray(),
        };
        using var world = PrivateWorldRuntime.Restore(state, _ => provider);
        Assert.True(world.DisplaceAdult(actor));
        Assert.Null(world.Inhabitants.Single(person => person.InhabitantId == actor).Project);
        Assert.NotNull(Assert.Single(world.Inhabitants.Single(person => person.InhabitantId == actor).Departures!).SharedProject);
        provider.Wanted[actor] = TownConstructionCandidateIds.Recipe(recipe.CanonicalId);
        world.Resume();
        for (var tick = 0; tick < 8; tick++) await world.AdvanceOneTickAsync();
        Assert.Equal(WorldProductionJobState.Paused, Assert.Single(world.WorldSimulation.ProductionJobs).State);
        Assert.All(reservationIds, id => Assert.Equal(InventoryReservationState.Reserved, world.Society.Inventory.GetReservation(id).State));
        Assert.DoesNotContain(TownConstructionCandidateIds.Recipe(recipe.CanonicalId), provider.Offered[actor]);
        Assert.DoesNotContain("collect_shared_food", provider.Offered[actor]);
        world.Pause();
        var saved = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved), _ => provider);
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        Assert.Equal(WorldProductionJobState.Paused, Assert.Single(restored.WorldSimulation.ProductionJobs).State);
        Assert.All(reservationIds, id => Assert.Equal(Alpha, restored.Society.Inventory.GetReservation(id).OwnerId));
        var pausedAt = restored.WorldSimulation.ProductionJobs.Single().PausedAtTick;
        var again = restored.ExportState();
        again = again with
        {
            Society = again.Society with
            {
                Society =
            SocietyFixture.JoinHouseholdCareGroup(again.Society.Society, actor, Alpha).Checkpoint
            }
        };
        using var repeated = PrivateWorldRuntime.Restore(again, _ => provider);
        Assert.True(repeated.DisplaceAdult(actor));
        Assert.Equal(pausedAt, repeated.WorldSimulation.ProductionJobs.Single().PausedAtTick);
        var remaining = repeated.Society.GetHousehold(Alpha).MemberIds.Single();
        var atWorkSite = repeated.ExportState();
        atWorkSite = atWorkSite with
        {
            Inhabitants = atWorkSite.Inhabitants.Select(person => person.InhabitantId == remaining
                ? person with { Position = house.Position, LastDecisionContext = null } : person).ToArray(),
        };
        using var resuming = PrivateWorldRuntime.Restore(atWorkSite, _ => provider);
        provider.Wanted[remaining] = "household_resume_work:departure-private-job";
        resuming.Resume();
        await AdvanceUntil(resuming, () => resuming.WorldSimulation.ProductionJobs.Single().State == WorldProductionJobState.Completed);
        var resumed = resuming.ExportState().Events.Single(item => item.Kind == "household_work_resumed");
        Assert.Equal(30, resuming.WorldSimulation.ProductionJobs.Single().CompletionTick - resumed.WorldTick);
        Assert.All(reservationIds, id => Assert.Equal(InventoryReservationState.Completed, resuming.Society.Inventory.GetReservation(id).State));
        Assert.Contains(resuming.Society.Inventory.Lots, lot => lot.Id.StartsWith("departure-private-job:output:", StringComparison.Ordinal) && lot.OwnerId == Alpha);
    }

    [Fact]
    public void BorrowedReservedCustodyCanBeSetDownWithoutChangingPropertyOrCommitments()
    {
        var inventory = InventoryFixture.CreateGenesis([new InventoryLot("borrowed", "wooden_axe", Alpha, 1,
            10_000, 10_000, 0, CarrierId: "adult")]);
        inventory = InventoryFixture.Reserve(inventory, "borrowed-commitment", Alpha, "borrowed", 1, "work", long.MaxValue);
        var dropped = InventoryFixture.DropCarrierGoods(inventory, "adult", new(3, 4));
        var lot = dropped.GetLot("borrowed");
        Assert.Equal(Alpha, lot.OwnerId);
        Assert.Null(lot.CarrierId);
        Assert.Equal(new InventoryGroundPosition(3, 4), lot.GroundPosition);
        Assert.Equal(inventory.Reservations, dropped.Reservations);
        Assert.Equal(1, lot.Quantity);
    }

    [Theory]
    [InlineData("clothing", 0, 10_000)]
    [InlineData("food", 10_000, 0)]
    public async Task UnusablePersonalGoodsCanStillBeCollectedAndBrokenBorrowedGoodsReturned(string kind, int condition, int freshness)
    {
        var provider = new Choices();
        using var initial = NormalPathWorld.CreateGenerated("departure-unusable", _ => provider);
        initial.Pause();
        var actor = initial.Society.GetHousehold(Alpha).MemberIds[0];
        var house = initial.WorldSimulation.Buildings.Single(building => building.InstanceId == "first-town-house-a");
        var state = initial.ExportState();
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "unusable-personal", kind,
            actor, 1, conditionBasisPoints: condition, freshnessBasisPoints: freshness, storageBuildingId: house.InstanceId);
        inventory = InventoryFixture.AddLot(inventory, "broken-borrowed", "wooden_axe", Alpha, 1, conditionBasisPoints: 0);
        inventory = InventoryFixture.Relocate(inventory, "broken-borrow", "broken-borrowed", Alpha, 1, actor);
        state = WithInventory(state, inventory) with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { Position = house.Position, LastDecisionContext = null } : person).ToArray(),
        };
        using var world = PrivateWorldRuntime.Restore(state, _ => provider);
        Assert.True(world.DisplaceAdult(actor));
        provider.Wanted[actor] = "household_collect:unusable-personal";
        world.Resume();
        await AdvanceUntil(world, () => PersonalEquipmentRules.IsCarried(world.Society.Inventory.GetLot("unusable-personal"), actor));
        provider.Wanted[actor] = "household_return:broken-borrowed";
        await AdvanceUntil(world, () => world.Society.Inventory.GetLot("broken-borrowed").StorageBuildingId == house.InstanceId);
        Assert.Equal(Alpha, world.Society.Inventory.GetLot("broken-borrowed").OwnerId);
        Assert.Equal(0, world.Society.Inventory.GetLot("broken-borrowed").ConditionBasisPoints);
        Assert.Equal(actor, world.Society.Inventory.GetLot("unusable-personal").OwnerId);
    }

    [Fact]
    public async Task DepartureLogsHaveOutcomeAndTickWithoutPrivateModelOrPlayerText()
    {
        var directory = Directory.CreateTempSubdirectory("household-departure-logs-");
        try
        {
            const string secret = "private-personality-api-key-and-raw-model-text";
            var provider = new Choices();
            using var initial = NormalPathWorld.CreateGenerated("departure-logs", _ => provider);
            var actor = initial.Society.GetHousehold(Alpha).MemberIds[0];
            var state = initial.ExportState();
            state = state with
            {
                Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                    ? person with { Personality = secret } : person).ToArray(),
            };
            using var world = PrivateWorldRuntime.Restore(state, _ => provider);
            provider.Wanted[actor] = "household_leave";
            var presence = new OwnerClientPresenceLease(TimeSpan.FromSeconds(30));
            presence.RecordAuthenticatedReconnect("owner");
            var logger = new RecordingLogger<PrivateWorldRuntimeService>();
            using var service = new PrivateWorldRuntimeService(world,
                new PrivateWorldStateFile(Path.Combine(directory.FullName, "world.json")), presence, logger);
            for (var tick = 0; tick < 8 && world.Society.GetInhabitant(actor).HouseholdId is not null; tick++)
                Assert.True(await service.TryAdvanceOnceAsync());
            Assert.Contains(logger.Messages, message => message.Contains("settlement_housing tick=", StringComparison.Ordinal) &&
                message.Contains("event=household_left", StringComparison.Ordinal));
            Assert.DoesNotContain(logger.Messages, message => message.Contains(secret, StringComparison.Ordinal));
            Assert.DoesNotContain(logger.Messages, message => message.Contains("follow_caregiver", StringComparison.Ordinal));
        }
        finally { directory.Delete(recursive: true); }
    }

    [Theory]
    [InlineData(false, "available")]
    [InlineData(true, "available")]
    [InlineData(true, "caregiver-death")]
    [InlineData(true, "reserved")]
    [InlineData(true, "full-hands")]
    public async Task DependentKeepsDefaultInheritanceCollectionAfterMovingAndGrowingUp(bool moved, string boundary)
    {
        var provider = new Choices();
        using var generated = NormalPathWorld.CreateGenerated("dependent-inherited-collection", _ => provider);
        var state = generated.ExportState();
        var society = state.Society.Society;
        var parents = society.GetHousehold(Alpha).MemberIds.Order(StringComparer.Ordinal).ToArray();
        var caregiver = parents[0];
        var deceased = parents[1];
        var house = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == "first-town-house-a");
        society = ChosenBirthNameTestFixture.NameParent(society, caregiver);
        society = SocietyFixture.ProposeRelationship(society, new("dependent-parents", 1,
            SocietyRelationshipType.Partnership, caregiver, deceased, society.WorldTick)).Checkpoint;
        society = SocietyFixture.AcceptRelationship(society, "dependent-parents", 1, deceased).Checkpoint;
        var inventory = InventoryFixture.AddLot(society.Inventory, "dependent-birth-food", "food", Alpha, 4,
            storageBuildingId: house.InstanceId);
        inventory = InventoryFixture.AddLot(inventory, "dependent-inherited-axe", "wooden_axe", deceased, 2,
            storageBuildingId: house.InstanceId);
        society = society with { Inventory = inventory };
        var birth = SocietyFixture.CommitBirth(society, new SocietyBirthRequest("dependent-birth", 1, caregiver, deceased,
            Alpha, parents, parents, "dependent-birth-food", 4, society.WorldTick,
            ChildName: ChosenBirthNameTestFixture.ChildName(society, caregiver, "Pip"), PrimaryCaregiverId: caregiver));
        society = birth.Checkpoint;
        var child = Assert.IsType<string>(birth.CreatedId);
        var rate = society.LifeClock?.Rate ?? 1;
        var adultAge = society.Config.FounderStartingAge;
        var childBirth = society.GetInhabitant(child).BirthLifeTick ?? society.GetInhabitant(child).BirthTick;
        var beforeAdult = childBirth + adultAge * society.Config.TicksPerLifecycleAge - 16L * rate;
        // Skip only idle age waiting; death, default settlement, departure and maturation still run normally.
        if (society.LifeClock is not null)
            society = society with { LifeClock = new(rate, society.WorldTick, beforeAdult) };
        else
        {
            society = SocietyFixture.AdvanceTo(society, beforeAdult).Checkpoint;
            var systems = state.WorldSystems!;
            state = state with
            {
                WorldSystems = systems with
                {
                    WorldTick = beforeAdult,
                    RegionalWeather = RegionalWeatherRules.Advance(systems, beforeAdult),
                    Climate = WeatherRules.Advance(systems.Climate, beforeAdult, state.WorldSeed, systems.Config),
                }
            };
        }
        var lastDay = society.Config.DayLifecycle!.MaximumDay;
        society = society with
        {
            Inhabitants = society.Inhabitants.Select(person => person.Id == deceased ? person with
            {
                BirthTick = society.LifeClock is null
                    ? society.LifeTickAt(society.WorldTick + 1) - lastDay * society.Config.TicksPerLifecycleAge : person.BirthTick,
                BirthLifeTick = society.LifeClock is null ? null
                    : society.LifeTickAt(society.WorldTick + 1) - lastDay * society.Config.TicksPerLifecycleAge,
                AgeBand = SocietyAgeBand.Elder,
                LastLifecycleYearChecked = lastDay - 1,
            } : person.Id == child ? person with
            {
                AgeBand = society.Config.AgeBandAt(adultAge - 1),
                LastLifecycleYearChecked = adultAge - 1,
            } : person).ToArray(),
        };
        var occupied = state.Inhabitants.Where(person => person.InhabitantId != caregiver).Select(person => person.Position)
            .Append(house.Position).ToHashSet();
        var childPosition = state.Map.FootNeighbors(house.Position).First(point => state.Map.IsPassable(point) && !occupied.Contains(point));
        state = state with
        {
            Society = state.Society with { Society = society },
            Survival = new(society.WorldTick, []),
            Inhabitants = state.Inhabitants.Select(person => person with
            {
                Position = person.InhabitantId == caregiver ? house.Position : person.Position,
                HungerBasisPoints = 10_000,
                Survival = new(10_000),
                Equipment = null,
                Project = null,
                LastDecisionContext = null,
            }).Append(new(child, childPosition, 10_000, 0, "curious", "grow with the household", Survival: new(10_000))).ToArray(),
            Towns = state.Towns!.Select(town => town.ResidentIds.Contains(caregiver, StringComparer.Ordinal)
                ? town with { ResidentIds = town.ResidentIds.Append(child).Order(StringComparer.Ordinal).ToArray() } : town).ToArray(),
        };
        using var dying = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)), _ => provider);
        Assert.True((await dying.AdvanceOneTickAsync()).Advanced);
        var estate = Assert.Single(dying.Society.Estates, item => item.DeceasedId == deceased);
        var saved = dying.ExportState();
        saved = saved with
        {
            Society = saved.Society with
            {
                Society = saved.Society.Society with
                {
                    Estates = saved.Society.Society.Estates.Select(item => item.Id == estate.Id
                        ? item with { ExpiryTick = saved.Society.Society.WorldTick + 1 } : item).ToArray(),
                }
            },
        };
        using var moving = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(saved)), _ => provider);
        Assert.True((await moving.AdvanceOneTickAsync()).Advanced);
        Assert.True(moving.Society.GetEstate(estate.Id).Settled);
        Assert.Empty(moving.Society.GetEstate(estate.Id).WillBequests ?? []);
        var axe = Assert.Single(moving.Society.Inventory.Lots, lot => lot.OwnerId == child && lot.ProvenanceLotId == "dependent-inherited-axe");
        Assert.Equal(("wooden_axe", 1, house.InstanceId), (axe.ItemKind, axe.Quantity, axe.StorageBuildingId));
        var inheritedState = moving.ExportState();
        if (moved)
        {
            provider.Wanted[caregiver] = "household_leave";
            // Start the departure phase with a fresh ordinary choice rather
            // than waiting out the earlier idle intention past adulthood.
            inheritedState = inheritedState with
            {
                Inhabitants = inheritedState.Inhabitants.Select(person => person.InhabitantId == caregiver
                    ? person with { LastDecisionContext = null } : person).ToArray(),
            };
        }
        using var departed = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(inheritedState)), _ => provider);
        if (moved)
        {
            await AdvanceUntil(departed, () => departed.Society.GetInhabitant(caregiver).HouseholdId is null, 12);
            Assert.Null(departed.Society.GetInhabitant(child).HouseholdId);
            Assert.NotEqual(SocietyAgeBand.Adult, departed.Society.GetInhabitant(child).AgeBand);
            provider.Wanted[caregiver] = "safe_idle";
        }
        var bytes = PrivateWorldRuntimeCodec.Encode(departed.ExportState());
        using var growing = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), _ => provider);
        using var growingReplay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), _ => provider);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(growing.ExportState()));
        for (var tick = 0; tick < 24 && growing.Society.GetInhabitant(child).AgeBand != SocietyAgeBand.Adult; tick++)
            await AdvancePair(growing, growingReplay);
        Assert.Equal(SocietyAgeBand.Adult, growing.Society.GetInhabitant(child).AgeBand);
        var collectionState = growing.ExportState();
        if (boundary == "caregiver-death")
        {
            var checkpoint = collectionState.Society.Society;
            collectionState = collectionState with
            {
                Society = collectionState.Society with
                {
                    Society = checkpoint with
                    {
                        Inhabitants = checkpoint.Inhabitants.Select(person => person.Id == caregiver ? person with
                        {
                            BirthTick = checkpoint.LifeClock is null
                                ? checkpoint.LifeTickAt(checkpoint.WorldTick + 1) - lastDay * checkpoint.Config.TicksPerLifecycleAge : person.BirthTick,
                            BirthLifeTick = checkpoint.LifeClock is null ? null
                                : checkpoint.LifeTickAt(checkpoint.WorldTick + 1) - lastDay * checkpoint.Config.TicksPerLifecycleAge,
                            AgeBand = SocietyAgeBand.Elder,
                            LastLifecycleYearChecked = lastDay - 1,
                        } : person).ToArray(),
                    }
                },
            };
            using var orphaned = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(collectionState)), _ => provider);
            Assert.True((await orphaned.AdvanceOneTickAsync()).Advanced);
            collectionState = orphaned.ExportState();
            Assert.DoesNotContain(collectionState.Inhabitants, person => person.InhabitantId == caregiver);
            var archived = Assert.Single(collectionState.DeceasedInhabitants!, person => person.InhabitantId == caregiver);
            Assert.Contains(archived.LastPhysical.Departures!, departure => departure.HouseholdId == Alpha && departure.CareGroup.Contains(child));
            var departure = Assert.Single(archived.LastPhysical.Departures!);
            foreach (var invalid in new[]
                     {
                         departure with { CareGroup = [child] },
                         departure with { CareGroup = [caregiver, child, "unknown-child"] },
                         departure with { CareGroup = [caregiver, child, child] },
                         departure with { HouseholdId = "unknown-household" },
                         departure with { Tick = archived.DeathTick + 1 },
                     })
            {
                var corrupted = collectionState with
                {
                    DeceasedInhabitants = collectionState.DeceasedInhabitants!.Select(person => person.InhabitantId == caregiver
                        ? person with { LastPhysical = person.LastPhysical with { Departures = [invalid] } } : person).ToArray(),
                };
                Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(corrupted));
            }
            var document = JsonNode.Parse(PrivateWorldRuntimeCodec.Encode(collectionState))!;
            var savedCaregiver = document["state"]!["deceasedInhabitants"]!.AsArray()
                .Single(person => person!["inhabitantId"]!.GetValue<string>() == caregiver)!;
            savedCaregiver["lastPhysical"]!["departures"]![0]!["careGroup"] = new JsonArray(child);
            Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Decode(
                System.Text.Encoding.UTF8.GetBytes(document.ToJsonString())));
        }
        var collectionInventory = collectionState.Society.Society.Inventory;
        if (boundary == "reserved")
            collectionInventory = InventoryFixture.Reserve(collectionInventory, "dependent-collection-reserved", child,
                axe.Id, 1, "work", long.MaxValue);
        if (boundary == "full-hands")
        {
            var equipment = collectionState.Inhabitants.Single(person => person.InhabitantId == child).Equipment;
            var room = PersonalEquipmentRules.FreeCapacity(collectionInventory, child, equipment);
            collectionInventory = InventoryFixture.AddLot(collectionInventory, "dependent-full-hands", "wood", child, room);
            Assert.Equal(0, PersonalEquipmentRules.FreeCapacity(collectionInventory, child, equipment));
        }
        collectionState = collectionState with
        {
            Society = collectionState.Society with { Society = collectionState.Society.Society with { Inventory = collectionInventory } },
        };
        bytes = PrivateWorldRuntimeCodec.Encode(collectionState);
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), _ => provider);
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), _ => provider);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        provider.Wanted[child] = "collect_equipment";
        var instruction = new OwnerInstructionRequest("collect-inherited-axe", "owner:test", child, OwnerInstructionKind.MustDo, "collect one wooden axe");
        var receipt = world.SubmitInstruction(instruction);
        Assert.Equal(receipt, replay.SubmitInstruction(instruction));
        for (var tick = 0; tick < 12 && Order().Status != "finished"; tick++) await AdvancePair(world, replay);
        var available = boundary is "available" or "caregiver-death";
        Assert.Equal(available ? ("finished", 1) : ("blocked", 0), (Order().Status, Order().CompletedUnits));
        Assert.Equal(available, PersonalEquipmentRules.IsCarried(world.Society.Inventory.GetLot(axe.Id), child));
        if (!available) Assert.Equal(house.InstanceId, world.Society.Inventory.GetLot(axe.Id).StorageBuildingId);
        Assert.Equal(child, world.Society.Inventory.GetLot(axe.Id).OwnerId);
        Assert.Equal(moved ? null : Alpha, world.Society.GetInhabitant(child).HouseholdId);
        world.Validate();

        OwnerInstructionOrder Order() => world.ExportState().Instructions!.Single(item => item.InstructionId == receipt.InstructionId).Order!;
        static async Task AdvancePair(PrivateWorldRuntime first, PrivateWorldRuntime second)
        {
            Assert.True((await first.AdvanceOneTickAsync()).Advanced);
            Assert.True((await second.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(first.ExportState()), PrivateWorldRuntimeCodec.Encode(second.ExportState()));
        }
    }

    private static PrivateWorldRuntimeState WithDependent(PrivateWorldRuntimeState state, string adult, string child)
    {
        var society = state.Society.Society;
        society = society with
        {
            Inhabitants = society.Inhabitants.Select(person => person.Id == child ? person with
            {
                AgeBand = SocietyAgeBand.Infant,
                BirthTick = society.WorldTick,
                BirthLifeTick = null,
                LastLifecycleYearChecked = 0,
                PrimaryCaregiverId = adult,
                DomesticFamilyUnitId = society.GetInhabitant(adult).DomesticFamilyUnitId,
            } : person).ToArray(),
            Relationships = society.Relationships.Append(new SocietyRelationship("departure-test-care", 1,
                SocietyRelationshipType.Caregiver, adult, child, SocietyRelationshipState.Accepted,
                SocietyConsentState.Accepted, society.WorldTick, society.WorldTick, "public", Alpha, [adult]))
                .OrderBy(edge => edge.Id, StringComparer.Ordinal).ToArray(),
            Households = society.Households.Select(home => home.Id == Alpha
                ? home with { CaregiverIds = [adult] } : home).ToArray(),
        };
        return state with
        {
            Society = state.Society with { Society = society },
            Towns = state.Towns!.Select(town => town with
            {
                // The former adult is now a dependent and no longer sits on the council.
                Governance = TownGovernanceRules.Advance(town.Governance!, town.Id, state.WorldSeed,
                    town.ResidentIds.Where(id => society.Inhabitants.Any(person => person.Id == id &&
                        person.Status == SocietyInhabitantStatus.Active &&
                        person.AgeBand is SocietyAgeBand.Adult or SocietyAgeBand.Elder)),
                    society.WorldTick, state.WorldSystems!.Config.TicksPerDay),
            }).ToArray(),
        };
    }

    private static PrivateWorldRuntimeState WithInventory(PrivateWorldRuntimeState state, InventoryCheckpoint inventory) => state with
    {
        Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
    };

    private static async Task AdvanceUntil(PrivateWorldRuntime world, Func<bool> ready, int maxTicks = 80)
    {
        for (var tick = 0; tick < maxTicks && !ready(); tick++) await world.AdvanceOneTickAsync();
        Assert.True(ready(), $"Expected household state was not reached within {maxTicks} ticks.");
    }

    private sealed class Choices : IDecisionProvider
    {
        public ConcurrentDictionary<string, string> Wanted { get; } = new(StringComparer.Ordinal);
        public ConcurrentDictionary<string, string[]> Offered { get; } = new(StringComparer.Ordinal);
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var observation = request.Observation;
            Offered[observation.InhabitantId] = observation.Candidates.Select(item => item.Id).ToArray();
            var wanted = Wanted.GetValueOrDefault(observation.InhabitantId, "safe_idle");
            var selected = observation.Candidates.FirstOrDefault(item => item.Id.StartsWith(wanted, StringComparison.Ordinal))
                ?? observation.Candidates.Single(item => item.Id == "safe_idle");
            return new DeterministicDecisionProvider().DecideAsync(request with { Observation = observation with { Candidates = [selected] } }, cancellationToken);
        }
    }
}

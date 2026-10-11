using System.Reflection;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class GrownAgentHelperMemoryTests
{
    private static readonly Lazy<Task<byte[]>> DueHouseEstate = new(CreateDueHouseEstateAsync);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TownInheritsNativeStoredFoodWithoutHaltingItsHost(bool groundControl)
    {
        var state = PrivateWorldRuntimeCodec.Decode(await DueHouseEstate.Value);
        var society = state.Society.Society;
        var due = society.Estates.Where(estate => !estate.Settled && estate.ExpiryTick == society.WorldTick + 1).ToArray();
        var lots = society.Inventory.Lots.Where(lot => due.Any(estate => estate.Id == lot.OwnerId) &&
            lot.ItemKind == "bread" && lot.StorageBuildingId is not null).ToArray();
        Assert.NotEmpty(lots);
        Assert.Empty(state.Inhabitants);
        Assert.All(due, estate => Assert.DoesNotContain(estate.BeneficiaryIds,
            id => society.GetInhabitant(id).Status == ClankerWorld.Simulation.Society.SocietyInhabitantStatus.Active));
        Assert.Contains(due, estate => estate.WillStatus == "default");
        var house = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == lots[0].StorageBuildingId);
        Assert.NotNull(house.HouseholdId);
        var originalHousehold = house.HouseholdId;
        if (groundControl)
        {
            // Explicit location-only comparison, including the frozen receipt.
            // The House case above retains every naturally generated field.
            var moved = society.Inventory.Lots.Where(lot => lot.ItemKind == "bread" && lot.StorageBuildingId == house.InstanceId &&
                society.Estates.Any(estate => !estate.Settled && estate.Id == lot.OwnerId))
                .Select(lot => lot.Id).ToHashSet(StringComparer.Ordinal);
            society = society with
            {
                Inventory = society.Inventory with
                {
                    Lots = society.Inventory.Lots.Select(lot => moved.Contains(lot.Id) ? lot with
                    { StorageBuildingId = null, GroundPosition = new(house.Position.X, house.Position.Y) } : lot).ToArray()
                },
                Estates = society.Estates.Select(estate => estate with
                {
                    FrozenLots = estate.FrozenLots?.Select(frozen => moved.Contains(frozen.LotId)
                        ? frozen with { StorageBuildingId = null } : frozen).ToArray()
                }).ToArray()
            };
            state = state with { Society = state.Society with { Society = society } };
        }
        var before = PrivateWorldRuntimeCodec.Encode(state);
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(before), _ => new QuietProvider());
        var folder = Directory.CreateTempSubdirectory("town-estate-house-");
        try
        {
            var file = new PrivateWorldStateFile(Path.Combine(folder.FullName, "world.json"), _ => new QuietProvider());
            file.Save(world);
            Assert.False((await world.AdvanceOneTickAsync(() => false)).Advanced);
            Assert.Equal(before, File.ReadAllBytes(file.Path));
            Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
            var presence = new OwnerClientPresenceLease(TimeSpan.FromMinutes(10));
            presence.RecordAuthenticatedReconnect("estate-test-owner");
            using var service = new PrivateWorldRuntimeService(world, file, presence);
            Assert.True(await service.TryAdvanceOnceAsync());
            Assert.All(due, estate => Assert.True(world.Society.GetEstate(estate.Id).Settled));
            foreach (var lot in lots)
            {
                var inherited = world.Society.Inventory.GetLot(lot.Id);
                var estate = due.Single(item => item.Id == lot.OwnerId);
                var town = state.DeceasedInhabitants!.Single(person => person.InhabitantId == estate.DeceasedId).TownId;
                Assert.Equal(town, inherited.OwnerId);
                Assert.Equal(lot.Quantity, inherited.Quantity);
                Assert.Equal(lot.ItemKind, inherited.ItemKind);
                Assert.InRange(inherited.FreshnessBasisPoints, 1, lot.FreshnessBasisPoints);
                Assert.Equal(lot.ConditionBasisPoints, inherited.ConditionBasisPoints);
                Assert.Equal(lot.ProvenanceLotId, inherited.ProvenanceLotId);
                Assert.Equal(groundControl ? null : lot.StorageBuildingId, inherited.StorageBuildingId);
                Assert.Equal(groundControl ? new InventoryGroundPosition(house.Position.X, house.Position.Y) : lot.GroundPosition,
                    inherited.GroundPosition);
                Assert.Null(inherited.CarrierId);
            }
            Assert.Equal(originalHousehold, world.WorldSimulation.Buildings.Single(building => building.InstanceId == house.InstanceId).HouseholdId);
            world.Validate();
            var saved = File.ReadAllBytes(file.Path);
            using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved), _ => new QuietProvider());
            Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
            if (!groundControl) AssertInvalidTownEstateCustody(world.ExportState(), lots[0].Id);
            Assert.False((await world.AdvanceOneTickAsync(() => false)).Advanced);
            Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
            for (var step = 0; step < 3; step++)
            {
                Assert.True(await service.TryAdvanceOnceAsync());
                Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
                Assert.Equal(File.ReadAllBytes(file.Path), PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
            }
        }
        finally { folder.Delete(true); }
    }

    private static void AssertInvalidTownEstateCustody(PrivateWorldRuntimeState state, string lotId)
    {
        var society = state.Society.Society;
        var lot = society.Inventory.GetLot(lotId);
        var estate = Assert.Single(society.Estates, item => item.Settled && item.FrozenLots!.Any(frozen => frozen.LotId == lotId));
        var frozen = estate.FrozenLots!.Single(item => item.LotId == lotId);
        foreach (var invalid in new[]
        {
            estate with { FrozenLots = [] },
            estate with { Settled = false },
            estate with { FrozenLots = estate.FrozenLots!.Select(item => item == frozen ? item with { LotId = "unrelated-lot" } : item).ToArray() },
            estate with { FrozenLots = estate.FrozenLots!.Select(item => item == frozen ? item with { ItemKind = "wood" } : item).ToArray() },
            estate with { FrozenLots = estate.FrozenLots!.Select(item => item == frozen ? item with { Quantity = lot.Quantity - 1 } : item).ToArray() },
            estate with { FrozenLots = estate.FrozenLots!.Select(item => item == frozen ? item with { StorageBuildingId = null } : item).ToArray() },
        })
            Refuse(state with
            {
                Society = state.Society with
                {
                    Society = society with
                    { Estates = society.Estates.Select(item => item.Id == estate.Id ? invalid : item).ToArray() }
                }
            });
        Refuse(state with
        {
            Society = state.Society with
            {
                Society = society with
                { Estates = society.Estates.Where(item => item.Id != estate.Id).ToArray() }
            }
        });
        Refuse(state with
        {
            DeceasedInhabitants = state.DeceasedInhabitants!.Select(person => person.InhabitantId == estate.DeceasedId
            ? person with { TownId = null } : person).ToArray()
        });
        Refuse(state with
        {
            Society = state.Society with
            {
                Society = society with
                {
                    Inventory = society.Inventory with
                    { Lots = society.Inventory.Lots.Select(item => item.Id == lotId ? item with { OwnerId = "town:unrelated" } : item).ToArray() }
                }
            }
        });
        static void Refuse(PrivateWorldRuntimeState invalid) =>
            Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(invalid));
    }

    private static async Task<byte[]> CreateDueHouseEstateAsync()
    {
        var choices = new DefaultEstateChoices();
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(await LaterAdult.Value), _ => choices);
        world.Pause();
        world.SetLifePace(1460);
        world.Resume();
        for (var step = 0; step < 900; step++)
        {
            if (world.Society.Estates.Any(estate => !estate.Settled && estate.ExpiryTick == world.WorldTick + 1 &&
                world.Society.Inventory.Lots.Any(lot => lot.OwnerId == estate.Id && lot.ItemKind == "bread" && lot.StorageBuildingId is not null)))
            {
                Assert.True(choices.Wills > 0);
                return PrivateWorldRuntimeCodec.Encode(world.ExportState());
            }
            Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
            await WaitForRequests(world);
            var pending = Assert.IsAssignableFrom<System.Collections.IDictionary>(typeof(PrivateWorldRuntime)
                .GetField("pendingWills", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(world));
            await Task.WhenAll(pending.Values.Cast<object>().Select(item => (Task)item.GetType().GetProperty("Task")!.GetValue(item)!))
                .WaitAsync(TimeSpan.FromSeconds(30));
        }
        throw new Xunit.Sdk.XunitException("The natural family deaths did not reach a stored-food estate expiry.");
    }

    private sealed class DefaultEstateChoices : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
        public long ProviderEpoch => 0;
        public int Wills { get; private set; }
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var observation = request.Observation;
            var selected = observation.Candidates.FirstOrDefault(candidate => candidate.Id == CognitionWillContext.HouseholdCandidateId)
                ?? observation.Candidates.Single(candidate => candidate.Id == "safe_idle");
            if (selected.Id == CognitionWillContext.HouseholdCandidateId) Wills++;
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, observation.InhabitantId, Kind, ProviderEpoch,
                observation.RunEpoch, observation.DecisionGeneration, observation.ObservationDigest, selected.Id, 1,
                observation.Candidates.ToDictionary(candidate => candidate.Id, candidate => candidate == selected ? 1d : 0d, StringComparer.Ordinal)));
        }
    }
}

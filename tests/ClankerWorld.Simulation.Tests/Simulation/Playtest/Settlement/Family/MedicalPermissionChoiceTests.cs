using System.Globalization;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed class MedicalPermissionChoiceTests
{
    [Theory]
    [InlineData(14, true)]
    [InlineData(15, true)]
    [InlineData(16, false)]
    public async Task TheLastPermissionLetsThePatientChooseEitherNearbyAdultWhileTheLimitStillHolds(int existingCount, bool expected)
    {
        var (state, patient, first, chosen) = await PreparedState(existingCount);
        var choices = new PersonalChoices(patient, chosen);
        using var world = PrivateWorldRuntime.Restore(state, choices.CreateProvider);
        using var replay = PrivateWorldRuntime.Restore(state, new PersonalChoices(patient, chosen).CreateProvider);
        world.Validate();
        Assert.False(world.TreatPatient(chosen, patient, "medicine").Applied);
        Assert.False(replay.TreatPatient(chosen, patient, "medicine").Applied);
        for (var tick = 0; tick < 4; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        }
        var observed = choices.Observations.Where(observation => observation.InhabitantId == patient).ToArray();
        Assert.NotEmpty(observed);
        if (expected)
        {
            Assert.Contains(observed[0].Candidates, candidate => candidate.Id == "medical_allow:" + first);
            Assert.Contains(observed[0].Candidates, candidate => candidate.Id == "medical_allow:" + chosen);
        }
        else
            Assert.All(observed, observation => Assert.DoesNotContain(observation.Candidates,
                candidate => candidate.Id.StartsWith("medical_allow:", StringComparison.Ordinal)));
        var consent = world.Inhabitants.Single(person => person.InhabitantId == patient).MedicalConsent!;
        Assert.Equal(expected, consent.CaregiverIds.Contains(chosen));
        Assert.Equal(existingCount + (expected ? 1 : 0), consent.CaregiverIds.Count);
        Assert.InRange(consent.CaregiverIds.Count, 1, MedicalCareRules.MaximumNamedCaregivers);
        Assert.Equal(expected, world.TreatPatient(chosen, patient, "medicine").Applied);
        Assert.Equal(expected, replay.TreatPatient(chosen, patient, "medicine").Applied);
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        if (expected)
        {
            Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.Id == Dose);
            var treatment = Assert.IsType<MedicalTreatmentState>(world.Inhabitants.Single(person => person.InhabitantId == patient).MedicalTreatment);
            var receipt = world.Society.Inventory.GetReservation(treatment.DoseReservationId);
            Assert.Equal((chosen, Dose, 1, InventoryReservationState.Completed),
                (receipt.OwnerId, receipt.LotId, receipt.Quantity, receipt.State));
        }
        else Assert.Equal(1, world.Society.Inventory.GetLot(Dose).Quantity);
        world.Validate();
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        restored.Validate();
    }

    private const string Dose = "last-permission-medicine";

    private static async Task<(PrivateWorldRuntimeState State, string Patient, string First, string Chosen)> PreparedState(int existingCount)
    {
        using var generated = NormalPathWorld.CreateGenerated("medical-permission-alternatives",
            _ => new ActionCoverageRecorder(chooseIdle: true));
        generated.Pause();
        for (var index = 0; index < 14; index++)
        {
            var current = generated.ExportState();
            var place = current.Map.Tiles.Select(tile => tile.Position).First(point => current.Map.IsBuildable(point) &&
                !generated.Towns.Any(town => town.BorderTiles.Contains(point)) &&
                !current.Map.Resources.Any(resource => resource.Position == point) &&
                !current.Map.CampObjects.Any(camp => camp.Position == point) &&
                generated.Inhabitants.All(person => person.Position != point));
            generated.AddAgent("agent:" + (100 + index).ToString("x32", CultureInfo.InvariantCulture), place);
        }
        generated.Resume();
        Assert.True((await generated.AdvanceOneTickAsync()).Advanced);
        var state = generated.ExportState();
        Assert.Equal(18, state.Inhabitants.Count);
        var founders = state.Inhabitants.Where(person => person.InhabitantId.StartsWith("founder:", StringComparison.Ordinal))
            .Select(person => person.InhabitantId).Order(StringComparer.Ordinal).ToArray();
        var patient = founders[0];
        var first = founders[1];
        var chosen = founders[2];
        var away = state.Inhabitants.Select(person => person.InhabitantId)
            .Where(id => id != patient && id != first && id != chosen).Order(StringComparer.Ordinal).ToArray();
        var permissions = away.Take(Math.Min(15, existingCount)).Concat(existingCount == 16 ? [first] : [])
            .Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(existingCount, permissions.Length);
        var occupied = state.Map.Resources.Select(resource => resource.Position).Concat(state.Map.CampObjects.Select(camp => camp.Position))
            .Concat(state.WorldSimulation!.Buildings.SelectMany(building => WorldContentSimulationRules.Footprint(
                state.WorldContent!.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId), building.Position)))
            .ToHashSet();
        bool Free(GridPoint point) => state.Map.IsBuildable(point) && !occupied.Contains(point);
        var center = state.Map.Tiles.Select(tile => tile.Position).First(point => Free(point) &&
            state.Map.FootNeighbors(point).Count(neighbor => Free(neighbor) && !state.Map.IsDiagonalFootStep(point, neighbor)) >= 2);
        var adjacent = state.Map.FootNeighbors(center).Where(point => Free(point) && !state.Map.IsDiagonalFootStep(center, point))
            .Take(2).ToArray();
        var distant = state.Map.Tiles.Select(tile => tile.Position).Where(point => Free(point) &&
            state.Map.FootDistance(center, point) >= 5).Take(away.Length).ToArray();
        Assert.Equal(away.Length, distant.Length);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, Dose, "medicine", chosen, 1);
        state = state with
        {
            JevEnabled = false,
            RoutineHelper = RoutineHelperSettings.Off,
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
            Inhabitants = state.Inhabitants.Select(person => person with
            {
                Position = person.InhabitantId == patient ? center : person.InhabitantId == first ? adjacent[0] :
                    person.InhabitantId == chosen ? adjacent[1] : distant[Array.IndexOf(away, person.InhabitantId)],
                HungerBasisPoints = 10_000,
                Survival = new(IllnessBasisPoints: person.InhabitantId == patient ? 4_000 : 0),
                LastDecisionContext = null,
                MedicalConsent = person.InhabitantId == patient ? new(permissions) : null,
            }).ToArray(),
            WorldSystems = state.WorldSystems! with
            {
                RegionalWeather = null,
                Config = state.WorldSystems.Config with
                {
                    WeatherProfiles = Enum.GetValues<SeasonKind>().Select(season => new WeatherProfile(season, 1, 0, 0, 0, 0)).ToArray(),
                },
                Climate = state.WorldSystems.Climate with { Weather = WeatherKind.Clear },
            },
        };
        return (state, patient, first, chosen);
    }

    private sealed class PersonalChoices(string patient, string chosen)
    {
        public List<InhabitantObservation> Observations { get; } = [];
        public IDecisionProvider CreateProvider(string actor) => new Provider(this, actor, patient, chosen);
        private sealed class Provider(PersonalChoices owner, string actor, string patient, string chosen) : IDecisionProvider
        {
            public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
            public long ProviderEpoch => 1;
            public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
                CancellationToken cancellationToken = default)
            {
                owner.Observations.Add(request.Observation);
                var candidates = request.Observation.Candidates;
                var selected = candidates.FirstOrDefault(candidate => actor == patient && candidate.Id == "medical_allow:" + chosen)
                    ?? candidates.Single(candidate => candidate.Id == "safe_idle");
                return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, actor, Kind, ProviderEpoch,
                    request.Observation.RunEpoch, request.Observation.DecisionGeneration, request.Observation.ObservationDigest,
                    selected.Id, 1, candidates.ToDictionary(candidate => candidate.Id,
                        candidate => candidate.Id == selected.Id ? 1d : 0d, StringComparer.Ordinal)));
            }
        }
    }
}

using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Viewer.Control;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class InhabitantBuildingDesignTests
{
    [Fact]
    public async Task ExperiencedBuilderCreatesReviewOnlyProposalWithDurableAuthorship()
    {
        using var seed = await PreparedWorldAsync("inhabitant-design", SocietyWorkRole.Builder, 3, new DesignChoiceProvider("shelter"));
        var prepared = seed.ExportState();
        var actor = prepared.Society.Society.Inhabitants.Single(person => person.Name == "private-inventor");
        var originalContent = PrivateWorldRuntimeCodec.Encode(prepared);
        var directory = Directory.CreateTempSubdirectory("inhabitant-design-log-");
        try
        {
            using var world = PrivateWorldRuntime.Restore(prepared,
                id => id == actor.Id ? new DesignChoiceProvider("shelter") : new IdleProvider());
            var presence = new OwnerClientPresenceLease(TimeSpan.FromSeconds(30));
            presence.RecordAuthenticatedReconnect("owner");
            var logger = new RecordingLogger<PrivateWorldRuntimeService>();
            using var service = new PrivateWorldRuntimeService(world,
                new PrivateWorldStateFile(Path.Combine(directory.FullName, "world.json")), presence, logger);

            Assert.True(await service.TryAdvanceOnceAsync());

            var package = Assert.Single(world.Content.Packages, item =>
                item.Manifest.PackageId.StartsWith("owner-building-", StringComparison.Ordinal));
            Assert.Equal(ContentPackageLifecycle.Proposed, package.Lifecycle);
            Assert.Null(package.ValidationTick);
            Assert.Null(package.StagedTick);
            Assert.Null(package.ActivationTick);
            var design = BuildingDesign.Read(package.Manifest);
            Assert.Equal("shelter", design.Purpose);
            Assert.Contains("private-inventor", design.Name, StringComparison.Ordinal);
            Assert.Contains(world.Content.Events, item => item.PackageId == package.Manifest.PackageId &&
                item.Kind == "package_proposed_by_inhabitant" && item.Detail == actor.Id);
            Assert.Equal(7, design.WoodCost);
            var projected = new OwnerWorldObservationStore(world).GetSnapshot().ContentPackages.Single(item =>
                item.PackageId == package.Manifest.PackageId);
            Assert.Equal(actor.Id, projected.ProposedByInhabitantId);
            Assert.DoesNotContain(world.WorldContent.Buildings, building =>
                building.PackageDigest == package.Manifest.PackageDigest);
            Assert.Equal(prepared.WorldSimulation!.Buildings.Count, world.WorldSimulation.Buildings.Count);
            Assert.Contains(logger.Messages, message => message.Contains("inhabitant_content_proposal", StringComparison.Ordinal) &&
                message.Contains("lifecycle=proposed", StringComparison.Ordinal));
            Assert.DoesNotContain(logger.Messages, message => message.Contains("private-inventor", StringComparison.Ordinal));

            var preview = await OwnerBuildingDesign.PreviewAsync(new(design.Name, design.Purpose, design.WoodCost), CancellationToken.None);
            Assert.True(preview.ConstructionPassed);
            Assert.Equal(package.Manifest.PackageId, preview.Package.PackageId);
            using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())));
            Assert.Contains(restored.Content.Events, item => item.PackageId == package.Manifest.PackageId &&
                item.Kind == "package_proposed_by_inhabitant" && item.Detail == actor.Id);
            Assert.NotEqual(originalContent, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(SocietyWorkRole.Builder, 2)]
    [InlineData(SocietyWorkRole.Farmer, 3)]
    public async Task InventionRequiresBuilderPermissionAndCompletedExperience(SocietyWorkRole role, int experience)
    {
        var provider = new DesignChoiceProvider("shelter");
        using var world = await PreparedWorldAsync("inhabitant-design-gate", role, experience, provider);
        for (var tick = 0; tick < 10; tick++)
        {
            await world.AdvanceOneTickAsync();
        }
        Assert.False(provider.SawInventionCandidate);
        Assert.DoesNotContain(world.Content.Packages, item =>
            item.Manifest.PackageId.StartsWith("owner-building-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AuthoredPurposesDoNotRepeatAndDifferentIdeasAreRateLimited()
    {
        using var world = await PreparedWorldAsync("inhabitant-design-cooldown", SocietyWorkRole.Builder, 3,
            new DesignChoiceProvider("shelter", "storage"));
        await world.AdvanceOneTickAsync();
        var first = Assert.Single(world.Content.Events, item => item.Kind == "package_proposed_by_inhabitant");
        Assert.Equal("shelter", BuildingDesign.Read(world.Content.Packages.Single(item =>
            item.Manifest.PackageId == first.PackageId).Manifest).Purpose);

        for (var tick = 0; tick < 305 && world.Content.Events.Count(item =>
                 item.Kind == "package_proposed_by_inhabitant") == 1; tick++)
        {
            await world.AdvanceOneTickAsync();
        }
        var authored = world.Content.Events.Where(item => item.Kind == "package_proposed_by_inhabitant").ToArray();
        Assert.Equal(2, authored.Length);
        Assert.True(authored[1].WorldTick - authored[0].WorldTick >= 300);
        Assert.Equal(["shelter", "storage"], authored.Select(item => BuildingDesign.Read(world.Content.Packages.Single(package =>
            package.Manifest.PackageId == item.PackageId).Manifest).Purpose));
    }

    private static async Task<PrivateWorldRuntime> PreparedWorldAsync(
        string seed,
        SocietyWorkRole role,
        int experience,
        DesignChoiceProvider provider)
    {
        using var baseline = new PrivateWorldRuntime(seed, _ => new IdleProvider());
        baseline.StageStarterContent();
        for (var tick = 0; tick < 3; tick++) await baseline.AdvanceOneTickAsync();
        var state = baseline.ExportState();
        var actor = state.Society.Society.Inhabitants[0];
        state = state with
        {
            Society = state.Society with
            {
                Society = state.Society.Society with
                {
                    Inhabitants = state.Society.Society.Inhabitants.Select(person => person.Id == actor.Id
                        ? person with { CurrentRole = role, Name = "private-inventor" }
                        : person).ToArray(),
                },
            },
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor.Id
                ? person with
                {
                    HungerBasisPoints = 9_000,
                    Project = null,
                    Proficiency = new SettlementProficiency(Building: experience),
                }
                : person).ToArray(),
        };
        return PrivateWorldRuntime.Restore(state, id => id == actor.Id ? provider : new IdleProvider());
    }

    private sealed class DesignChoiceProvider(params string[] purposes) : IDecisionProvider
    {
        public bool SawInventionCandidate { get; private set; }
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;

        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SawInventionCandidate |= request.Observation.Candidates.Any(candidate =>
                candidate.Id.StartsWith("invent:building:", StringComparison.Ordinal));
            var selected = purposes.Select(purpose => "invent:building:" + purpose)
                .Select(target => request.Observation.Candidates.FirstOrDefault(candidate => candidate.Id == target))
                .FirstOrDefault(candidate => candidate is not null)
                ?? request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle");
            return new DeterministicDecisionProvider().DecideAsync(request with
            {
                Observation = request.Observation with { Candidates = [selected] },
            }, cancellationToken);
        }
    }

    private sealed class IdleProvider : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default) =>
            new DeterministicDecisionProvider().DecideAsync(request with
            {
                Observation = request.Observation with
                {
                    Candidates = request.Observation.Candidates.Where(candidate => candidate.Id == "safe_idle").ToArray(),
                },
            }, cancellationToken);
    }
}

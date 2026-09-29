using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class SettlementFamilyTests
{
    [Fact]
    public async Task LegacyRevokedParentageStillCountsAsFamilyHistory()
    {
        var state = await PreparedState();
        var first = state.Inhabitants[0].InhabitantId;
        var second = state.Inhabitants[1].InhabitantId;
        var parent = state.Inhabitants[2].InhabitantId;
        var tick = state.Society.Society.WorldTick;
        var edges = new[] { first, second }.Select(child => new SocietyRelationship("legacy-parent:" + child, 1,
            SocietyRelationshipType.BiologicalParentage, parent, child, SocietyRelationshipState.Revoked,
            SocietyConsentState.Revoked, tick, tick, "family"));
        state = state with
        {
            Society = state.Society with
            {
                Society = state.Society.Society with
                {
                    Relationships = state.Society.Society.Relationships.Concat(edges).OrderBy(edge => edge.Id, StringComparer.Ordinal).ToArray(),
                }
            }
        };
        using var world = PrivateWorldRuntime.Restore(state, actor => new FamilyProvider(actor == first ? "partner_propose:" : "safe_idle"));
        await world.AdvanceOneTickAsync();
        Assert.DoesNotContain(world.Society.Relationships, edge => edge.Type == SocietyRelationshipType.Partnership);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task AncestryAndSiblingExclusionsSurviveInterveningRelativeDeath(bool ancestor, bool relativeDies)
    {
        var state = await PreparedState();
        var first = state.Inhabitants[0].InhabitantId;
        var second = state.Inhabitants[1].InhabitantId;
        var middle = state.Inhabitants[2].InhabitantId;
        var tick = state.Society.Society.WorldTick;
        SocietyRelationship[] edges =
        [
            new("ancestry:1", 1, SocietyRelationshipType.BiologicalParentage, ancestor ? first : middle, ancestor ? middle : first,
                SocietyRelationshipState.Accepted, SocietyConsentState.ProtectedLifecycle, tick, tick, "household"),
            new("ancestry:2", 1, SocietyRelationshipType.BiologicalParentage, middle, second,
                SocietyRelationshipState.Accepted, SocietyConsentState.ProtectedLifecycle, tick, tick, "household"),
        ];
        state = state with
        {
            Society = state.Society with
            {
                Society = state.Society.Society with
                {
                    Relationships = state.Society.Society.Relationships.Concat(edges).OrderBy(edge => edge.Id, StringComparer.Ordinal).ToArray(),
                }
            }
        };
        if (relativeDies)
        {
            using var society = SocietyWorldRuntime.Restore(state.Society);
            society.Apply(checkpoint => SocietyFixture.Kill(checkpoint, middle, SocietyDeathCause.Accident, tick));
            state = state with { Society = society.ExportState(), Inhabitants = state.Inhabitants.Where(person => person.InhabitantId != middle).ToArray() };
        }
        using var world = PrivateWorldRuntime.Restore(state, actor => new FamilyProvider(actor == first ? "partner_propose:" : "safe_idle"));
        await world.AdvanceOneTickAsync();
        Assert.DoesNotContain(world.Society.Relationships, edge => edge.Type == SocietyRelationshipType.Partnership);
    }

    [Theory]
    [InlineData("partner_accept:", SocietyRelationshipState.Accepted)]
    [InlineData("partner_refuse:", SocietyRelationshipState.Rejected)]
    public async Task PartnershipRequiresAnIndependentResponseAcrossRestart(string response, SocietyRelationshipState expected)
    {
        var state = await PreparedState();
        var proposer = state.Inhabitants[0].InhabitantId;
        using var world = PrivateWorldRuntime.Restore(state, actor => new FamilyProvider(actor == proposer ? "partner_propose:" : "safe_idle"));
        await world.AdvanceOneTickAsync();
        var proposal = Assert.Single(world.Society.Relationships, item => item.Type == SocietyRelationshipType.Partnership);
        Assert.Equal(SocietyRelationshipState.Proposed, proposal.State);
        world.Pause();
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), _ => new FamilyProvider(response));
        Assert.False((await restored.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        restored.Resume();
        await restored.AdvanceOneTickAsync();
        Assert.Equal(expected, restored.Society.GetRelationship(proposal.Id).State);
        Assert.Empty(restored.Society.Births);
        Assert.Equal(4, restored.Inhabitants.Count);
        if (expected == SocietyRelationshipState.Accepted)
        {
            Assert.Contains(new OwnerWorldObservationStore(restored).GetSnapshot().Inhabitants.Single(item => item.Id == proposer).Relationships,
                item => item.Type == "partnership" && item.State == "accepted");
        }
        for (var tick = 0; tick < 5; tick++)
        {
            await restored.AdvanceOneTickAsync();
        }
        Assert.Single(restored.Society.Relationships, item => item.Type == SocietyRelationshipType.Partnership);
    }

    [Fact]
    public async Task EitherPartnerCanLeaveWithoutTheOthersPermission()
    {
        var state = await PreparedState();
        var proposer = state.Inhabitants[0].InhabitantId;
        using var world = PrivateWorldRuntime.Restore(state, actor => new FamilyProvider(actor == proposer ? "partner_propose:" : "partner_accept:"));
        await world.AdvanceOneTickAsync();
        await world.AdvanceOneTickAsync();
        var relationship = Assert.Single(world.Society.Relationships, item => item.Type == SocietyRelationshipType.Partnership);
        Assert.Equal(SocietyRelationshipState.Accepted, relationship.State);
        using var restored = PrivateWorldRuntime.Restore(world.ExportState(), _ => new FamilyProvider("partner_leave:"));
        for (var tick = 0; tick < 40 && restored.Society.GetRelationship(relationship.Id).State == SocietyRelationshipState.Accepted; tick++)
        {
            await restored.AdvanceOneTickAsync();
        }
        Assert.Equal(SocietyRelationshipState.Revoked, restored.Society.GetRelationship(relationship.Id).State);
    }

    [Fact]
    public async Task UnansweredProposalsExpireInsteadOfBecomingConsent()
    {
        var state = await PreparedState();
        var proposer = state.Inhabitants[0].InhabitantId;
        using var world = PrivateWorldRuntime.Restore(state, actor => new FamilyProvider(actor == proposer ? "partner_propose:" : "safe_idle"));
        for (var tick = 0; tick < 125; tick++)
        {
            await world.AdvanceOneTickAsync();
        }
        Assert.Equal(SocietyRelationshipState.Revoked,
            Assert.Single(world.Society.Relationships, item => item.Type == SocietyRelationshipType.Partnership).State);
        Assert.Contains(world.ExportState().Events, item => item.Kind == "partnership_expired");
    }

    [Fact]
    public async Task NoProposalWithoutPriorCooperation()
    {
        var state = await PreparedState();
        state = state with { Society = state.Society with { Society = state.Society.Society with { Memories = [] } } };
        using var world = PrivateWorldRuntime.Restore(state, _ => new FamilyProvider("partner_propose:"));
        for (var tick = 0; tick < 5; tick++)
        {
            await world.AdvanceOneTickAsync();
        }
        Assert.DoesNotContain(world.Society.Relationships, item => item.Type == SocietyRelationshipType.Partnership);
    }

    [Fact]
    public async Task MinorsCannotReceivePartnershipProposals()
    {
        var state = await PreparedState();
        var target = state.Inhabitants[1].InhabitantId;
        state = state with
        {
            Society = state.Society with
            {
                Society = state.Society.Society with
                {
                    Inhabitants = state.Society.Society.Inhabitants.Select(person => person.Id == target
                        ? person with { BirthTick = state.Society.Society.WorldTick, AgeBand = SocietyAgeBand.Infant, LastLifecycleYearChecked = 0 }
                        : person).ToArray(),
                },
            },
        };
        using var world = PrivateWorldRuntime.Restore(state, _ => new FamilyProvider("partner_propose:"));
        await world.AdvanceOneTickAsync();
        Assert.DoesNotContain(world.Society.Relationships, item => item.Type == SocietyRelationshipType.Partnership);
    }

    [Fact]
    public async Task PartnershipJournalDoesNotIncludeNamesOrModelText()
    {
        var directory = Directory.CreateTempSubdirectory("clankerworld-family-log-");
        try
        {
            var state = await PreparedState();
            var proposer = state.Inhabitants[0].InhabitantId;
            state = state with
            {
                Society = state.Society with
                {
                    Society = state.Society.Society with
                    {
                        Inhabitants = state.Society.Society.Inhabitants.Select(person => person with { Name = "family-private-text-secret" }).ToArray(),
                    },
                },
            };
            using var world = PrivateWorldRuntime.Restore(state, actor => new FamilyProvider(actor == proposer ? "partner_propose:" : "safe_idle"));
            var presence = new OwnerClientPresenceLease(TimeSpan.FromSeconds(30));
            presence.RecordAuthenticatedReconnect("owner");
            var logger = new RecordingLogger<PrivateWorldRuntimeService>();
            using var service = new PrivateWorldRuntimeService(world, new PrivateWorldStateFile(Path.Combine(directory.FullName, "world.json")), presence, logger);
            Assert.True(await service.TryAdvanceOnceAsync());
            Assert.Contains(logger.Messages, message => message.Contains("settlement_family", StringComparison.Ordinal) &&
                message.Contains("event=partnership_proposed", StringComparison.Ordinal));
            Assert.DoesNotContain(logger.Messages, message => message.Contains("family-private-text-secret", StringComparison.Ordinal));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static async Task<PrivateWorldRuntimeState> PreparedState()
    {
        using var world = new PrivateWorldRuntime("settlement-family", _ => new FamilyProvider("safe_idle"));
        world.StageStarterContent();
        for (var tick = 0; tick < 5; tick++)
        {
            await world.AdvanceOneTickAsync();
        }
        var state = world.ExportState();
        var proposer = state.Inhabitants[0].InhabitantId;
        var other = state.Inhabitants[1].InhabitantId;
        using var society = SocietyWorldRuntime.Restore(state.Society);
        society.Apply(checkpoint => SocietyFixture.RecordSocialMemory(checkpoint,
            new("settlement-trust:" + proposer + ":" + other, proposer, other, "Exchanged needed materials.", "public", checkpoint.WorldTick)));
        return state with { Society = society.ExportState(), Inhabitants = state.Inhabitants.Select(person => person with { LastDecisionContext = null }).ToArray() };
    }

    private sealed class FamilyProvider(string prefix) : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var candidate = request.Observation.Candidates.FirstOrDefault(item => item.Id.StartsWith(prefix, StringComparison.Ordinal))
                ?? request.Observation.Candidates.Single(item => item.Id == "safe_idle");
            return new DeterministicDecisionProvider().DecideAsync(request with
            {
                Observation = request.Observation with { Candidates = [candidate] },
            }, cancellationToken);
        }
    }
}

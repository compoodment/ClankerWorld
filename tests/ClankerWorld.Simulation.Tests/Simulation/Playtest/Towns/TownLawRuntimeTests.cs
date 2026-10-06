using System.Collections.Concurrent;
using System.Text;
using System.Text.Json.Nodes;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class TownLawRuntimeTests
{
    private const string Author = "founder:00000000000000000000000000000001";
    private const string Grove = "Grove: Do not cut trees near the notice place.";
    private const string Amended = "Grove: Cut only fallen wood near the notice place.";

    [Fact]
    public async Task PersonalTurnsAdoptAndAmendASiteLawThatSavesReplaysAndChangesNoRightsOrGoods()
    {
        var providers = new ConcurrentDictionary<string, LawProvider>(StringComparer.Ordinal);
        using var generated = NormalPathWorld.CreateGenerated("town-law-site", id => providers.GetOrAdd(id, key => new LawProvider(key)));
        var initial = generated.ExportState();
        var origin = initial.Towns![0].OriginSite!.Value;
        using var world = PrivateWorldRuntime.Restore(initial with
        {
            Inhabitants = initial.Inhabitants.Select(p => p with { Position = origin, HungerBasisPoints = 8_000 }).ToArray(),
        }, id => providers.GetOrAdd(id, key => new LawProvider(key)));
        var before = world.ExportState();
        for (var tick = 0; tick < 120 && !(world.Towns[0].Government is { Laws: [{ Versions.Count: > 1 }, ..] }); tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);

        var town = world.Towns[0];
        var law = Assert.Single(town.Government!.Laws);
        Assert.Equal(2, law.Versions.Count);
        Assert.Equal(TownLawRules.Site, law.Versions[0].Scope);
        Assert.NotEmpty(law.Versions[0].SiteTiles);
        Assert.All(law.Versions[0].SiteTiles, tile => Assert.True(TownLandRightsRules.IsCoveredByTownTitle(tile, town.Id, world.ExportState().TownLandTitles!)));
        Assert.Equal("Cut only fallen wood near the notice place.", law.Versions[1].Rule);
        Assert.Equal(law.Versions[0].EndedTick, law.Versions[1].AdoptedTick);
        var adoption = town.Governance!.Proposals.Single(p => p.Id == law.Versions[0].ProposalId);
        Assert.Equal("passed", adoption.Status);
        Assert.Equal(adoption.SettledTick, law.Versions[0].AdoptedTick);
        Assert.Contains(providers[Author].Seen, id => id.Contains("|propose|site:", StringComparison.Ordinal));
        Assert.Contains(providers.Values.SelectMany(p => p.Notes), note => note.Contains("Law 1 adopted", StringComparison.Ordinal));

        // A law records a social rule: titles, use rights and goods are untouched by its adoption.
        var after = world.ExportState();
        Assert.Equal(before.TownLandTitles!.Select(t => t.Id + ":" + string.Join(";", t.Tiles)),
            after.TownLandTitles!.Select(t => t.Id + ":" + string.Join(";", t.Tiles)));
        Assert.Equal(before.HouseholdLandUseRights!.Select(r => r.Id + ":" + r.HouseholdId + ":" + string.Join(";", r.Tiles)),
            after.HouseholdLandUseRights!.Select(r => r.Id + ":" + r.HouseholdId + ":" + string.Join(";", r.Tiles)));
        Assert.Equal(before.Society.Society.Inventory.Lots.Select(l => l.Id + ":" + l.OwnerId).Order(),
            after.Society.Society.Inventory.Lots.Select(l => l.Id + ":" + l.OwnerId).Order());
        Assert.Equal(before.Towns![0].ResidentIds, after.Towns![0].ResidentIds);

        world.Pause();
        var encoded = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(encoded),
            id => providers.GetOrAdd(id, key => new LawProvider(key)));
        Assert.Equal(encoded, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        world.Resume();
        restored.Resume();
        await world.AdvanceOneTickAsync();
        await restored.AdvanceOneTickAsync();
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        restored.Validate();
    }

    [Theory]
    [InlineData("adoptedTick")]
    [InlineData("siteTile")]
    [InlineData("missingDraft")]
    [InlineData("unsupportedArrangement")]
    [InlineData("missingGovernment")]
    [InlineData("changedRule")]
    [InlineData("changedDraft")]
    [InlineData("missingPendingDraft")]
    [InlineData("pendingSiteTile")]
    [InlineData("nullLaw")]
    [InlineData("nullVersion")]
    [InlineData("nullDraft")]
    public void DamagedLawRecordsAreRefused(string damage)
    {
        using var world = NormalPathWorld.CreateGenerated("town-law-damage", _ => new ActionCoverageRecorder(chooseIdle: true));
        var state = world.ExportState();
        var town = state.Towns![0];
        var day = state.WorldSystems!.Config.TicksPerDay;
        var ids = town.ResidentIds;
        var site = state.TownLandTitles![0].Tiles.Take(2).ToArray();
        var (council, government) = TownLawRules.ProposeAdoption(town.Governance!, town.Government!, town.Id, ids[0], "Grove: Leave the saplings.",
            TownLawRules.Site, site, ids, 0, day);
        foreach (var voter in ids.Take(3)) council = TownGovernanceRules.VoteProposal(council, council.Proposals[0].Id, voter, true, 0);
        (council, government) = TownLawRules.Enact(council, government, town.Id, town.Name, 0);
        (council, government) = TownLawRules.ProposeAdoption(council, government, town.Id, ids[0], "Paths: Leave the path clear.",
            TownLawRules.Site, site, ids, 0, day);
        var healthy = state with { Towns = [town with { Governance = council, Government = government }] };
        var encoded = PrivateWorldRuntimeCodec.Encode(healthy);
        using (var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(encoded)))
            Assert.Equal(encoded, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));

        var document = JsonNode.Parse(encoded)!;
        var saved = document["state"]!["towns"]![0]!;
        var savedGovernment = saved["government"]!;
        switch (damage)
        {
            case "adoptedTick": savedGovernment["laws"]![0]!["versions"]![0]!["adoptedTick"] = 1; break;
            case "siteTile": savedGovernment["laws"]![0]!["versions"]![0]!["siteTiles"]![0]!["x"] = -1; break;
            case "missingDraft": savedGovernment["lawDrafts"] = new JsonArray(); break;
            case "unsupportedArrangement": savedGovernment["arrangement"]!["ordinary"] = "king"; break;
            case "missingGovernment": saved.AsObject().Remove("government"); break;
            case "changedRule": savedGovernment["laws"]![0]!["versions"]![0]!["rule"] = "Cut every tree."; break;
            case "changedDraft": savedGovernment["lawDrafts"]![0]!["rule"] = "Cut every tree."; break;
            case "missingPendingDraft": savedGovernment["lawDrafts"]!.AsArray().RemoveAt(1); break;
            case "pendingSiteTile": savedGovernment["lawDrafts"]![1]!["siteTiles"]![0]!["x"] = -1; break;
            case "nullLaw": savedGovernment["laws"]!.AsArray().Add(null); break;
            case "nullVersion": savedGovernment["laws"]![0]!["versions"]!.AsArray().Add(null); break;
            case "nullDraft": savedGovernment["lawDrafts"]!.AsArray().Add(null); break;
        }
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Decode(Encoding.UTF8.GetBytes(document.ToJsonString())));
    }

    [Fact]
    public void OlderActiveLawsRemainOfferedAndDelayedChangesCannotRetargetANewerVersion()
    {
        using var generated = NormalPathWorld.CreateGenerated("law-version-bound-actions", _ => new ActionCoverageRecorder(chooseIdle: true));
        var initial = generated.ExportState();
        var town = initial.Towns![0];
        var day = initial.WorldSystems!.Config.TicksPerDay;
        var adults = town.ResidentIds;
        var council = town.Governance!;
        var government = town.Government!;
        for (var i = 0; i < 8; i++)
        {
            (council, government) = TownLawRules.ProposeAdoption(council, government, town.Id, adults[0],
                $"Rule {i}: Share work {i}.", TownLawRules.ResidentDuty, [], adults, 0, day);
            foreach (var voter in adults.Take(3)) council = TownGovernanceRules.VoteProposal(council, council.Proposals[^1].Id, voter, true, 0);
            (council, government) = TownLawRules.Enact(council, government, town.Id, town.Name, 0);
        }
        council = TownGovernanceRules.LearnNotices(council, adults[0], council.Notices.Select(n => n.Id), 0);
        town = town with { Governance = council, Government = government };
        using var world = PrivateWorldRuntime.Restore(initial with { Towns = [town] });
        var candidates = new List<CognitionCandidate>();
        typeof(PrivateWorldRuntime).GetMethod("AddTownLawCandidates", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(world, [candidates, adults[0], town]);
        Assert.Equal(8, candidates.Count(c => c.Id.Contains("|amend|", StringComparison.Ordinal)));
        var first = government.Laws[0];
        var stale = candidates.Single(c => c.Id.Contains("|repeal|" + first.Id + "|", StringComparison.Ordinal)).Id;
        Assert.EndsWith("|1", stale, StringComparison.Ordinal);
        (council, government) = TownLawRules.ProposeAmendment(council, government, town.Id, adults[1], first.Id,
            "Rule zero: Share the agreed work.", adults, 0, day);
        foreach (var voter in adults.Take(3)) council = TownGovernanceRules.VoteProposal(council, council.Proposals[^1].Id, voter, true, 0);
        (council, government) = TownLawRules.Enact(council, government, town.Id, town.Name, 0);
        Assert.Throws<InvalidOperationException>(() => TownLawRules.ProposeRepeal(council, government, town.Id, adults[0], first.Id, adults, 0, day, expectedVersion: 1));
        // Even after learning the new wording, an earlier delayed action cannot become consent to repeal it.
        council = TownGovernanceRules.LearnNotices(council, adults[0], council.Notices.Select(n => n.Id), 0);
        using var updated = PrivateWorldRuntime.Restore(initial with { Towns = [town with { Governance = council, Government = government }] });
        var before = PrivateWorldRuntimeCodec.Encode(updated.ExportState());
        typeof(PrivateWorldRuntime).GetMethod("ApplyTownCivicCandidate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(updated, [adults[0], stale, null, null, null, null, null]);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(updated.ExportState()));
    }

    private sealed class LawProvider(string actor) : IDecisionProvider
    {
        public ConcurrentQueue<string> Seen { get; } = new();
        public ConcurrentQueue<string> Notes { get; } = new();
        public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
        public long ProviderEpoch => 1;

        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var o = request.Observation;
            foreach (var candidate in o.Candidates) Seen.Enqueue(candidate.Id);
            if (o.Self?.CivicNote is { } note) Notes.Enqueue(note);
            var civic = o.Candidates.Where(c => c.Id.StartsWith("civic|town:first|", StringComparison.Ordinal)).ToArray();
            var note2 = o.Self?.CivicNote ?? "";
            string? text = null;
            var selected = civic.FirstOrDefault(c => c.Id.Contains("|yes|", StringComparison.Ordinal)) ??
                civic.FirstOrDefault(c => c.Id.Contains("|read|", StringComparison.Ordinal));
            if (selected is null && actor == Author)
            {
                if (!note2.Contains("Law 1", StringComparison.Ordinal) && !note2.Contains("Grove", StringComparison.Ordinal))
                {
                    selected = civic.FirstOrDefault(c => c.Id.Contains("|propose|site:", StringComparison.Ordinal));
                    text = Grove;
                }
                else if (note2.Contains("Law 1 adopted", StringComparison.Ordinal))
                {
                    selected = civic.FirstOrDefault(c => c.Id.Contains("|amend|", StringComparison.Ordinal));
                    text = Amended;
                }
            }
            selected ??= o.Candidates.Single(c => c.Id == "safe_idle");
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, o.InhabitantId, Kind, ProviderEpoch,
                o.RunEpoch, o.DecisionGeneration, o.ObservationDigest, selected.Id, 1,
                o.Candidates.ToDictionary(c => c.Id, c => c.Id == selected.Id ? 1d : 0d, StringComparer.Ordinal),
                CivicProposal: selected.Id.Contains("|propose|", StringComparison.Ordinal) || selected.Id.Contains("|amend|", StringComparison.Ordinal) ? text : null));
        }
    }
}

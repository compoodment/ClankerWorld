using System.Text;
using System.Text.Json.Nodes;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class TownGovernanceSaveValidationTests
{
    [Theory]
    [InlineData("notice:0")]
    [InlineData("notice:02")]
    [InlineData("notice:3")]
    public void NoticeIdsMustMatchTheirAppendPositionEvenWhenTheReceiptMatches(string id)
    {
        var state = NewCheckpoint();
        var town = state.Towns![0];
        var governance = TownGovernanceRules.Register(town.Governance!, town.ResidentIds[0], true, null, town.ResidentIds, 0);
        governance = TownGovernanceRules.Nominate(governance, town.ResidentIds[0], town.ResidentIds[1], town.ResidentIds, 0);
        governance = TownGovernanceRules.LearnNotice(governance, town.ResidentIds[0], governance.Notices[1].Id, 0);

        AssertDamagedCheckpointRefused(WithGovernance(state, governance), saved =>
        {
            saved["notices"]![1]!["id"] = id;
            saved["knowledge"]![0]!["noticeId"] = id;
        });
    }

    [Fact]
    public void ReorderedNoticeIdsAreRefused()
    {
        var state = NewCheckpoint();
        var town = state.Towns![0];
        var governance = TownGovernanceRules.Register(town.Governance!, town.ResidentIds[0], true, null, town.ResidentIds, 0);
        governance = TownGovernanceRules.Nominate(governance, town.ResidentIds[0], town.ResidentIds[1], town.ResidentIds, 0);

        AssertDamagedCheckpointRefused(WithGovernance(state, governance), saved =>
        {
            saved["notices"]![0]!["id"] = "notice:2";
            saved["notices"]![1]!["id"] = "notice:1";
        });
    }

    [Theory]
    [InlineData("proposal")]
    [InlineData("election")]
    public void SavedSequenceCannotPrecedeAnExistingCivicRecord(string kind)
    {
        var state = CheckpointWithCivicRecord(kind);
        AssertDamagedCheckpointRefused(state, saved => saved["sequence"] = 0);
    }

    [Theory]
    [InlineData("proposal", "0")]
    [InlineData("proposal", "01")]
    [InlineData("proposal", "+1")]
    [InlineData("proposal", "2")]
    [InlineData("proposal", "not-a-number")]
    [InlineData("election", "0")]
    [InlineData("election", "01")]
    [InlineData("election", "+1")]
    [InlineData("election", "2")]
    [InlineData("election", "not-a-number")]
    public void CivicRecordIdsRequireAPositiveCanonicalNumberWithinTheSequence(string kind, string suffix)
    {
        var state = CheckpointWithCivicRecord(kind);
        var id = state.Towns![0].Id + ":" + kind + ":" + suffix;
        AssertDamagedCheckpointRefused(state, saved =>
        {
            var record = kind == "proposal" ? saved["proposals"]![0]! : saved["election"]!;
            record["id"] = id;
        });
    }

    [Fact]
    public void HistoricalElectionIdsAlsoRequireTheSavedSequence()
    {
        var state = CheckpointWithCivicRecord("election");
        var governance = state.Towns![0].Governance!;
        governance = governance with
        {
            Election = null,
            ElectionHistory = [governance.Election! with { Stage = "cancelled" }],
        };
        AssertDamagedCheckpointRefused(WithGovernance(state, governance), saved => saved["sequence"] = 0);
    }

    [Fact]
    public void PassedFourPersonAdmissionCannotClaimApprovalWithOneYesVote()
    {
        var state = NewCheckpoint();
        var town = state.Towns![0];
        var day = state.WorldSystems!.Config.TicksPerDay;
        var subject = town.ResidentIds[3];
        var governance = TownGovernanceRules.SubmitProposal(town.Governance!, town.Id, town.ResidentIds[0],
            "admission", subject, "Approve this admission request.", "same", town.ResidentIds, 0, day);
        foreach (var voter in town.ResidentIds.Take(3))
            governance = TownGovernanceRules.VoteProposal(governance, governance.Proposals[0].Id, voter, true, 0);
        Assert.Equal("passed", governance.Proposals[0].Status);
        // The subject already lives there, so the passed admission's one outcome is a lapse.
        var withVote = WithGovernance(state, governance);
        var healthy = withVote with
        {
            Towns = [withVote.Towns![0] with
            {
                Admissions = [new(governance.Proposals[0].Id, subject, "lapsed", 0, Reason: "already_resident")],
            }],
        };
        AssertRoundtrips(healthy);

        AssertDamagedCheckpointRefused(healthy, saved =>
        {
            var proposal = saved["proposals"]![0]!;
            proposal["requiredYes"] = 1;
            var votes = proposal["votes"]!.AsArray();
            while (votes.Count > 1) votes.RemoveAt(votes.Count - 1);
        });
    }

    [Theory]
    [InlineData("rejected", 1)]
    [InlineData("cancelled", 2)]
    [InlineData("withdrawn", 4)]
    public void SettledProposalThresholdStillMatchesItsHistoricalRoster(string status, int requiredYes)
    {
        var state = NewCheckpoint();
        var town = state.Towns![0];
        var governance = TownGovernanceRules.SubmitProposal(town.Governance!, town.Id, town.ResidentIds[0],
            "law", null, "Post harvest dates.", "same", town.ResidentIds, 0, state.WorldSystems!.Config.TicksPerDay);
        governance = TownGovernanceRules.WithdrawProposal(governance, governance.Proposals[0].Id, town.ResidentIds[0], 0);

        AssertDamagedCheckpointRefused(WithGovernance(state, governance), saved =>
        {
            saved["proposals"]![0]!["status"] = status;
            saved["proposals"]![0]!["requiredYes"] = requiredYes;
        });
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(0, 2)]
    [InlineData(1, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 2)]
    [InlineData(3, 2)]
    [InlineData(4, 3)]
    public void LegitimateHistoricalThresholdsSurviveRosterAndGoverningFormChanges(int voters, int requiredYes)
    {
        var state = NewCheckpoint();
        var town = state.Towns![0];
        var governance = TownGovernanceRules.SubmitProposal(town.Governance!, town.Id, town.ResidentIds[0],
            "law", null, "Post harvest dates.", "same", town.ResidentIds, 0, state.WorldSystems!.Config.TicksPerDay);
        governance = governance with
        {
            Revision = 1,
            Proposals = [governance.Proposals[0] with
            {
                Voters = town.ResidentIds.Take(voters).ToArray(),
                RequiredYes = requiredYes,
                Status = "cancelled",
                SettledTick = 0,
            }],
        };

        AssertRoundtrips(WithGovernance(state, governance));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RelayProvenanceRequiresADirectReadOutsideTheSourceCycle(bool mutual)
    {
        var state = CheckpointWithSameTickRelays();
        var ids = state.Towns![0].ResidentIds;
        AssertDamagedCheckpointRefused(state, saved =>
            saved["knowledge"]![0]!["sourceAgentId"] = mutual ? ids[1] : ids[0]);
    }

    [Fact]
    public async Task SameTickRelayChainsReloadInAnyReceiptOrderAndCanAdvanceNormally()
    {
        var state = CheckpointWithSameTickRelays();
        var governance = state.Towns![0].Governance!;
        state = WithGovernance(state, governance with { Knowledge = governance.Knowledge.Reverse().ToArray() });
        var encoded = PrivateWorldRuntimeCodec.Encode(state);
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(encoded),
            _ => new ActionCoverageRecorder(chooseIdle: true));
        Assert.Equal(encoded, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(governance.Knowledge.Count, restored.Towns[0].Governance!.Knowledge.Count);
        restored.Validate();
    }

    [Fact]
    public void CanonicalHistoriesReloadAndAllocateDistinctNextRecordsAndNotices()
    {
        var state = CheckpointWithCivicRecord("election");
        var town = state.Towns![0];
        var governance = town.Governance!;
        governance = governance with
        {
            Election = null,
            ElectionHistory = [governance.Election! with { Stage = "cancelled" }],
        };
        governance = TownGovernanceRules.SubmitProposal(governance, town.Id, town.ResidentIds[0], "law", null,
            "Post harvest dates.", "same", town.ResidentIds, 0, state.WorldSystems!.Config.TicksPerDay);
        state = WithGovernance(state, governance);
        AssertRoundtrips(state);

        var decoded = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state));
        governance = TownGovernanceRules.SubmitProposal(decoded.Towns![0].Governance!, town.Id, town.ResidentIds[0],
            "law", null, "Post storm notices.", "same", town.ResidentIds, 0, state.WorldSystems!.Config.TicksPerDay);
        var next = WithGovernance(decoded, governance);
        AssertRoundtrips(next);
        Assert.Equal(town.Id + ":proposal:3", governance.Proposals[^1].Id);
        Assert.Equal(Enumerable.Range(1, governance.Notices.Count).Select(number => "notice:" + number),
            governance.Notices.Select(notice => notice.Id));
        Assert.Equal(governance.Notices.Count, governance.Notices.Select(notice => notice.Id).Distinct().Count());
    }

    private static PrivateWorldRuntimeState NewCheckpoint()
    {
        using var world = NormalPathWorld.CreateGenerated("civic-save-validation", _ => new ActionCoverageRecorder(chooseIdle: true));
        return world.ExportState();
    }

    private static PrivateWorldRuntimeState CheckpointWithCivicRecord(string kind)
    {
        var state = NewCheckpoint();
        var town = state.Towns![0];
        var day = state.WorldSystems!.Config.TicksPerDay;
        var governance = town.Governance!;
        if (kind == "proposal")
            governance = TownGovernanceRules.SubmitProposal(governance, town.Id, town.ResidentIds[0], "law", null,
                "Post harvest dates.", "same", town.ResidentIds, 0, day);
        else
        {
            governance = governance with
            {
                Form = "representative",
                Fallback = "none",
                Members = town.ResidentIds.Take(2).ToArray(),
                TermEndTick = (long)day * TownGovernanceRules.TermDays,
            };
            governance = TownGovernanceRules.Register(governance, town.ResidentIds[2], true, null, town.ResidentIds, 0);
            governance = TownGovernanceRules.Advance(governance, town.Id, state.WorldSeed, town.ResidentIds, 0, day);
            Assert.NotNull(governance.Election);
        }
        return WithGovernance(state, governance);
    }

    private static PrivateWorldRuntimeState CheckpointWithSameTickRelays()
    {
        var state = NewCheckpoint();
        var town = state.Towns![0];
        var ids = town.ResidentIds;
        var governance = TownGovernanceRules.Register(town.Governance!, ids[0], true, null, ids, 0);
        var notice = governance.Notices[0].Id;
        for (var index = 0; index < ids.Count; index++)
            governance = TownGovernanceRules.LearnNotice(governance, ids[index], notice, 0,
                index == 0 ? null : ids[index - 1]);
        return WithGovernance(state, governance);
    }

    private static PrivateWorldRuntimeState WithGovernance(PrivateWorldRuntimeState state, TownGovernanceState governance) =>
        state with { Towns = [state.Towns![0] with { Governance = governance }] };

    private static void AssertDamagedCheckpointRefused(PrivateWorldRuntimeState healthy, Action<JsonNode> damage)
    {
        var document = JsonNode.Parse(PrivateWorldRuntimeCodec.Encode(healthy))!;
        damage(document["state"]!["towns"]![0]!["governance"]!);
        var bytes = Encoding.UTF8.GetBytes(document.ToJsonString());
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Decode(bytes));
    }

    private static void AssertRoundtrips(PrivateWorldRuntimeState state)
    {
        var encoded = PrivateWorldRuntimeCodec.Encode(state);
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(encoded));
        Assert.Equal(encoded, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }
}

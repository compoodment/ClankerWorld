using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Tests;

public sealed class AgentMarriageRulesTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SurnameSessionStopsAtConsensusOrFourValidAlternatingChoices(bool disagree)
    {
        var consent = AgentConversationRules.Propose("conversation:marriage-unit", "agent-a", "agent-b", 0, 1);
        Assert.True(AgentConversationRules.TryAcceptProposal(consent, "agent-b", 0, 1, out consent));
        for (var index = 0; index < 7; index++)
        {
            Assert.True(AgentConversationRules.TryBeginTurn(consent, 0, 1, out var started));
            var purpose = index == 6 ? AgentConversationPurpose.WrapUp : AgentConversationPurpose.PublicTurn;
            var request = Request(started, purpose);
            var reply = Reply(request, null) with { Effect = index == 6 ? AgentConversationEffect.Marriage : AgentConversationEffect.None };
            Assert.True(AgentConversationRules.TryAdmitTurn(started, request, reply, [request.OtherParticipantId], 0, 1, out consent, out _));
        }
        Assert.True(AgentConversationRules.TryAcceptWrapUp(consent, "agent-a", 0, out consent, out var firstConsent));
        Assert.False(firstConsent);
        Assert.True(AgentConversationRules.TryAcceptWrapUp(consent, "agent-b", 0, out consent, out var marriageConsent));
        Assert.True(marriageConsent);
        var marriage = new AgentMarriage(AgentMarriageRules.Id(consent.Id),
            new SocietyRelationship("partnership:unit", 2, SocietyRelationshipType.Partnership, "agent-a", "agent-b",
                SocietyRelationshipState.Accepted, SocietyConsentState.Accepted, 0, 0, "private", AcceptedBy: ["agent-a", "agent-b"]),
            consent, "Aster Ash", "Rowan Reed", "conversation:surname-unit", 0);
        var surname = AgentConversationRules.Propose(marriage.SurnameConversationId, "agent-a", "agent-b", 0, 1) with
        {
            Kind = AgentConversationKind.MarriageSurname,
            Status = AgentConversationStatus.Ready,
            AcceptedParticipantIds = ["agent-a", "agent-b"],
            CurrentSpeakerId = "agent-a",
        };
        for (var index = 0; index < (disagree ? 4 : 2); index++)
        {
            Assert.True(AgentConversationRules.TryBeginTurn(surname, 0, 1, out var started));
            var request = Request(started, AgentConversationPurpose.SurnameChoice);
            var choice = disagree && index % 2 == 1 ? "Reed" : "Ash";
            Assert.False(AgentConversationRules.TryAdmitTurn(started, request, Reply(request, "Invented"),
                [request.OtherParticipantId], 0, 1, out var rejected, out _));
            Assert.Equal(started, rejected);
            Assert.False(AgentConversationRules.TryAdmitTurn(started, request,
                Reply(request, choice) with { Disposition = AgentConversationDisposition.Withdraw },
                [request.OtherParticipantId], 0, 1, out rejected, out _));
            Assert.Equal(started, rejected);
            Assert.True(AgentConversationRules.TryAdmitTurn(started, request, Reply(request, choice),
                [request.OtherParticipantId], 0, 1, out surname, out _));
            AgentConversationRules.Validate(surname, 0);
        }
        Assert.Equal(AgentConversationStatus.Closed, surname.Status);
        Assert.False(AgentConversationRules.TryBeginTurn(surname, 0, 1, out _));
        Assert.Equal(disagree ? "surname_draw" : "surname_agreed", surname.Outcome);
        var result = AgentMarriageRules.ResolveSurname("surname-unit-test", marriage, surname, out var usedDraw);
        Assert.Equal(disagree, usedDraw);
        Assert.Equal(disagree ? "Reed" : "Ash", result);
        Assert.Equal(result, AgentMarriageRules.ResolveSurname("surname-unit-test", marriage, surname, out _));
    }

    private static AgentConversationTurnRequest Request(AgentConversation started, AgentConversationPurpose purpose) => new(
        "request:unit", started.Id, started.Revision, started.RunEpoch, 0, purpose, started.CurrentSpeakerId!,
        started.CurrentSpeakerId!, started.CurrentSpeakerId == "agent-a" ? "agent-b" : "agent-a",
        "Other person", "patient", "share a life", started.Turns,
        purpose == AgentConversationPurpose.WrapUp ? [AgentConversationEffect.None, AgentConversationEffect.Marriage] : [AgentConversationEffect.None])
    {
        AllowedSurnames = purpose == AgentConversationPurpose.SurnameChoice ? ["Ash", "Reed"] : [],
    };

    private static AgentConversationTurnResponse Reply(AgentConversationTurnRequest request, string? surname) => new(
        request.RequestId, request.ConversationId, request.Revision, request.RunEpoch, request.SpeakerId,
        "This is my own choice.", AgentConversationDisposition.Continue, SurnameChoice: surname);
}

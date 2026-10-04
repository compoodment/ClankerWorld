using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>A player's later surname change, distinct from the couple's original choice.</summary>
public sealed record AgentMarriageRename(string AgentId, string Surname, long WorldTick);

/// <summary>Marriage consent and its bounded surname session survive dialogue-history compaction.</summary>
public sealed record AgentMarriage(string Id, [property: JsonRequired] SocietyRelationship PartnershipReceipt, AgentConversation Consent,
    string InitiatorNameAtAcceptance, string InviteeNameAtAcceptance, string SurnameConversationId,
    long AcceptedTick, string? ChosenSurname = null, long? CompletedTick = null, bool UsedTieBreak = false)
{
    public AgentMarriageRename? LatestPlayerRename { get; init; }

    public AgentConversation? SurnameReceipt { get; init; }

    [JsonIgnore]
    public string InitiatorId => Consent.InitiatorId;

    [JsonIgnore]
    public string InviteeId => Consent.InviteeId;

    [JsonIgnore]
    public string PartnershipId => PartnershipReceipt.Id;

    [JsonIgnore]
    public string? CurrentSurname => LatestPlayerRename?.Surname ?? ChosenSurname;
}

/// <summary>Marriage never follows from a partnership, spoken words or a surname draw alone.</summary>
public static class AgentMarriageRules
{
    public const int MaximumSurnameTurns = 4;

    public static bool IsAdult(SocietyInhabitant person) => person.Status == SocietyInhabitantStatus.Active &&
        person.AgeBand is SocietyAgeBand.Adult or SocietyAgeBand.Elder;

    public static SocietyRelationship? Partnership(SocietyCheckpoint society, string first, string second) =>
        society.Relationships.SingleOrDefault(relationship => relationship.Type == SocietyRelationshipType.Partnership &&
            relationship.State == SocietyRelationshipState.Accepted &&
            (relationship.ProposerId == first && relationship.TargetId == second ||
             relationship.ProposerId == second && relationship.TargetId == first));

    public static bool CanPropose(SocietyCheckpoint society, IEnumerable<AgentMarriage> marriages,
        string first, string second) => first != second &&
        society.Inhabitants.SingleOrDefault(person => person.Id == first) is { } initiator && IsAdult(initiator) &&
        society.Inhabitants.SingleOrDefault(person => person.Id == second) is { } invitee && IsAdult(invitee) &&
        initiator.HasChosenName && invitee.HasChosenName &&
        InhabitantNameRules.SurnameKey(initiator.Name) is not null && InhabitantNameRules.SurnameKey(invitee.Name) is not null &&
        Partnership(society, first, second) is not null &&
        !marriages.Any(marriage => HasParticipant(marriage, first) || HasParticipant(marriage, second));

    public static bool HasParticipant(AgentMarriage marriage, string agentId) =>
        marriage.InitiatorId == agentId || marriage.InviteeId == agentId;

    public static string Note(AgentMarriage marriage, string agentId, SocietyCheckpoint society)
    {
        var otherId = agentId == marriage.InitiatorId ? marriage.InviteeId : marriage.InitiatorId;
        var other = society.GetInhabitant(otherId).Name;
        return marriage.CompletedTick is null
            ? $"Marriage agreed with {other}; the shared surname is still undecided." +
                (marriage.SurnameReceipt?.Outcome == "participant_unavailable" ? " The surname conversation stopped because a participant is unavailable." : string.Empty)
            : $"Married to {other}. Shared surname: {marriage.CurrentSurname}." +
                (marriage.UsedTieBreak ? " The original surname was chosen by a draw after four turns without agreement." : string.Empty);
    }

    public static string Id(string consentId) => "marriage:" +
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(consentId)));

    public static IReadOnlyList<string> AllowedSurnames(AgentMarriage marriage) =>
        new[] { InhabitantNameRules.SurnameKey(marriage.InitiatorNameAtAcceptance)!,
                InhabitantNameRules.SurnameKey(marriage.InviteeNameAtAcceptance)! }
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(surname => WithSurname(marriage.InitiatorNameAtAcceptance, surname).Length <= 48 &&
                WithSurname(marriage.InviteeNameAtAcceptance, surname).Length <= 48)
            .Order(StringComparer.Ordinal).ToArray();

    public static bool CanKeepSurnameChoices(AgentMarriage marriage, string name) =>
        AllowedSurnames(marriage).All(surname => WithSurname(name, surname).Length <= 48);

    public static string WithSurname(string name, string surname)
    {
        var canonical = InhabitantNameRules.CanonicalKey(name);
        var separator = canonical?.LastIndexOf(' ') ?? -1;
        if (canonical is null ||
            InhabitantNameRules.CanonicalKey(surname) != surname || surname.Contains(' '))
            throw new ArgumentException("A married agent needs a first name and shared surname.");
        return (separator < 0 ? canonical : canonical[..separator]) + " " + surname;
    }

    public static string ResolveSurname(string worldSeed, AgentMarriage marriage, AgentConversation conversation,
        out bool usedTieBreak)
    {
        var turns = conversation.Turns;
        if (conversation.Kind != AgentConversationKind.MarriageSurname || turns.Count is < 2 or > MaximumSurnameTurns ||
            turns.Any(turn => turn.SurnameChoice is null ||
                !AllowedSurnames(marriage).Contains(turn.SurnameChoice, StringComparer.Ordinal)))
            throw new InvalidOperationException("The surname session has no complete pair of valid choices.");
        usedTieBreak = !StringComparer.Ordinal.Equals(turns[^1].SurnameChoice, turns[^2].SurnameChoice);
        if (!usedTieBreak) return turns[^1].SurnameChoice!;
        if (turns.Count != MaximumSurnameTurns)
            throw new InvalidOperationException("A surname draw requires all four alternating valid turns.");
        var candidates = AllowedSurnames(marriage);
        // Two choices make a single digest bit unbiased; this stream is tied to this world's consent receipt.
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(worldSeed + "\nmarriage-surname\n" + marriage.Id));
        return candidates[digest[0] & 1];
    }
}

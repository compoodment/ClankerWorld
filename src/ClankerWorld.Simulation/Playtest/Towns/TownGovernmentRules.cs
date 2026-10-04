using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>Protected resident decisions and elected mandates. Law text never grants authority.</summary>
public static partial class TownGovernmentRules
{
    private static bool Has(IEnumerable<string> values, string value) => values.Contains(value, StringComparer.Ordinal);
    private static string[] Ordered(IEnumerable<string> values) => TownGovernanceRules.Ordered(values);
    public static TownGovernmentChange? ActiveChange(TownGovernmentState state) =>
        state.Changes.SingleOrDefault(c => c.Status is "voting" or "handover");
    public static string[] Mandates(TownArrangement arrangement) =>
        TownArrangementRules.Mandates(arrangement)?.Split('+') ?? [];
    public static string VoteNoticeToken(TownGovernmentChange change) =>
        change.Id + ":vote:" + change.OpenedTick?.ToString(CultureInfo.InvariantCulture);
    public static string RoundToken(TownMayoralContest contest) =>
        $"{contest.Id}:{contest.Round.ToString(CultureInfo.InvariantCulture)}:{contest.RoundOpenedTick?.ToString(CultureInfo.InvariantCulture)}";
    public static TownOffice? GoverningOffice(TownGovernmentState state) =>
        state.Offices.SingleOrDefault(o => o.Mandates == "ordinary");
    private static string Fingerprint(IEnumerable<string> fields) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', fields))));
    private static string Circumstances(TownGovernmentState state, string[] adults) =>
        Fingerprint(adults.Prepend(TownArrangementRules.Key(state.Arrangement))
            .Concat(state.Offices.OrderBy(o => o.Mandates, StringComparer.Ordinal).Select(o => o.Mandates + ":" + o.HolderId)));
    private static string ElectionCircumstances(TownGovernmentState state, string[] adults, string mandates) =>
        Fingerprint(adults.Prepend(mandates).Concat(Willing(state, adults, mandates).Select(id => "candidate:" + id)));
    private static string[] Willing(TownGovernmentState state, string[] adults, string mandates) =>
        adults.Where(id => state.Consents.Any(c => c.AgentId == id && c.Mandates == mandates)).ToArray();
    private static string[] RequiredSuccessorMandates(TownArrangement current, TownGovernmentChange change) =>
        change.Kind == "replace_mayor" ? Mandates(change.Target) :
            Mandates(change.Target).Except(Mandates(current), StringComparer.Ordinal).ToArray();
    private static TownGovernmentState Replace(TownGovernmentState state, TownGovernmentChange change) =>
        state with { Changes = state.Changes.Select(c => c.Id == change.Id ? change : c).ToArray() };
    private static TownGovernanceState Notice(TownGovernanceState council, string kind, string subject, string text, long tick) =>
        TownGovernanceRules.PostNotice(council, kind, subject, text, tick);

    public static (TownGovernanceState Council, TownGovernmentState Government) Propose(
        TownGovernanceState council, TownGovernmentState state, string townId, string actor,
        TownArrangement target, bool replaceMayor, IEnumerable<string> adultResidents, long tick, int day)
    {
        var adults = Ordered(adultResidents);
        if (!Has(adults, actor) || !TownArrangementRules.IsSupported(target) ||
            replaceMayor && (target != state.Arrangement || !TownArrangementRules.HasOffice(target)) ||
            !replaceMayor && target == state.Arrangement)
            throw new InvalidOperationException("Only an adult resident may propose a supported, different arrangement or replace an existing elected office.");
        var kind = replaceMayor ? "replace_mayor" : "arrangement";
        var key = kind + ":" + TownArrangementRules.Key(target);
        if (state.Changes.Any(c => c.RequestKey == key && c.Status is "queued" or "voting" or "handover"))
            return (council, state);
        var circumstances = Circumstances(state, adults);
        if (state.Changes.LastOrDefault(c => c.RequestKey == key) is { Status: "rejected" or "withdrawn" } prior &&
            tick < prior.SettledTick + day && prior.Circumstances == circumstances)
            throw new InvalidOperationException("This resident proposal must wait one unpaused day or a material change before retrying.");
        var id = townId + ":government:" + (state.Sequence + 1).ToString(CultureInfo.InvariantCulture);
        var change = new TownGovernmentChange(id, key, kind, target, actor, circumstances, tick, "queued", null, null, [], []);
        state = state with { Sequence = state.Sequence + 1, Changes = state.Changes.Append(change).ToArray() };
        council = Notice(council, "government", id, $"{actor} proposed a protected resident vote. " +
            (replaceMayor ? "Elect a replacement for the current mayoral mandates. " : "") +
            TownArrangementRules.Declaration(target) + " The resident-majority safeguard cannot be removed.", tick);
        return OpenNext(council, state, adults, tick, day);
    }

    public static TownGovernmentState Vote(TownGovernmentState state, string id, string actor, bool yes, long tick)
    {
        var change = state.Changes.SingleOrDefault(c => c.Id == id);
        if (change is not { Status: "voting" } || tick >= change.DeadlineTick || !Has(change.Voters, actor) ||
            change.Votes.Any(v => v.AgentId == actor))
            throw new InvalidOperationException("This resident vote is stale, final or ineligible.");
        return Replace(state, change with { Votes = change.Votes.Append(new(actor, yes)).ToArray() });
    }

    public static (TownGovernanceState Council, TownGovernmentState Government) Withdraw(
        TownGovernanceState council, TownGovernmentState state, string id, string actor, long tick)
    {
        var change = state.Changes.SingleOrDefault(c => c.Id == id);
        if (change is null || change.AuthorId != actor || change.Status is not ("queued" or "voting"))
            throw new InvalidOperationException("Only the author may withdraw a queued or unfinished resident vote.");
        state = Replace(state, change with { Status = "withdrawn", SettledTick = tick, Reason = "Withdrawn by its author." });
        return (Notice(council, "government", id, "Resident government proposal withdrawn.", tick), state);
    }

    private static (TownGovernanceState, TownGovernmentState) OpenNext(TownGovernanceState council,
        TownGovernmentState state, string[] adults, long tick, int day)
    {
        if (ActiveChange(state) is not null) return (council, state);
        foreach (var queued in state.Changes.Where(c => c.Status == "queued").ToArray())
        {
            if (!Has(adults, queued.AuthorId) || queued.Kind == "arrangement" && queued.Target == state.Arrangement ||
                queued.Kind == "replace_mayor" && queued.Target != state.Arrangement)
            {
                state = Replace(state, queued with { Status = "cancelled", SettledTick = tick, Reason = "The queued request is no longer applicable." });
                council = Notice(council, "government", queued.Id, "Queued government proposal cancelled because its author or requested arrangement changed.", tick);
                continue;
            }
            state = Replace(state, queued with
            {
                Status = "voting",
                OpenedTick = tick,
                DeadlineTick = tick + day,
                Voters = adults,
                OpeningVoters = adults,
                Votes = [],
                Circumstances = Circumstances(state, adults)
            });
            council = Notice(council, "government", VoteNoticeToken(state.Changes.Single(c => c.Id == queued.Id)),
                $"Resident government vote opened: {TownArrangementRules.Declaration(queued.Target)} " +
                $"Needs more than half of the {adults.Length} eligible opening residents to vote yes by tick {tick + day}. Votes are final; silence is not approval.", tick);
            break;
        }
        return (council, state);
    }

    public static (TownGovernanceState Council, TownGovernmentState Government) Advance(
        TownGovernanceState council, TownGovernmentState state, string townId, string townName, string seed,
        IEnumerable<string> adultResidents, long tick, int day)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(day);
        var adults = Ordered(adultResidents);
        state = state with { Consents = state.Consents.Where(c => Has(adults, c.AgentId)).ToArray() };
        foreach (var office in state.Offices.Where(o => o.HolderId is not null).ToArray())
            if (!Has(adults, office.HolderId!) || tick >= office.TermEndTick)
                (council, state) = EndOffice(council, state, office, tick,
                    !Has(adults, office.HolderId!) ? "The holder died or left Town membership." : "The term ended.");

        if (ActiveChange(state) is { Status: "voting" } voting)
        {
            var voters = voting.Voters.Where(id => Has(adults, id)).ToArray();
            voting = voting with { Voters = voters, Votes = voting.Votes.Where(v => Has(voters, v.AgentId)).ToArray() };
            var yes = voting.Votes.Count(v => v.Yes);
            var required = voters.Length / 2 + 1;
            if (yes >= required && tick <= voting.DeadlineTick)
            {
                voting = voting with { Status = "handover", ApprovedTick = tick, HandoverDeadlineTick = tick + (long)TownArrangementRules.HandoverDays * day };
                council = Notice(council, "government", voting.Id,
                    $"Residents approved the government proposal. Incumbent authority continues until a valid handover, due by tick {voting.HandoverDeadlineTick}.", tick);
            }
            else if (tick >= voting.DeadlineTick || yes + voters.Length - voting.Votes.Count < required)
            {
                voting = voting with { Status = "rejected", SettledTick = tick, Reason = "No resident majority approved the proposal." };
                council = Notice(council, "government", voting.Id, "Resident government proposal failed: no required majority.", tick);
            }
            state = Replace(state, voting);
        }

        var handover = ActiveChange(state) is { Status: "handover" } active ? active : null;
        var changesOrdinaryAuthority = handover is not null && handover.Target.Ordinary != state.Arrangement.Ordinary;
        var targetNeedsCouncil = changesOrdinaryAuthority && NeedsElectedCouncil(handover!.Target, adults.Length);
        var currentNeedsCouncil = state.Arrangement.Ordinary is TownArrangementRules.Council or TownArrangementRules.ElectedCouncil;
        // Whether the current arrangement elects or keeps representatives without this change: its own
        // threshold, a seated representative council kept above three adults, or one retrying for candidates.
        var currentElectsCouncil = currentNeedsCouncil && (NeedsElectedCouncil(state.Arrangement, adults.Length) ||
            adults.Length > TownGovernanceRules.Seats && (council.Form == "representative" || council.Fallback == "candidates"));
        var leader = GoverningOffice(state);
        if (state.Arrangement.Ordinary == TownArrangementRules.Mayor)
            council = TownGovernanceRules.ChangeCouncil(council, leader?.HolderId is { } holder ? [holder] : adults,
                leader?.HolderId is not null ? "leader" : "all_adult", "arrangement", null, tick);
        else if (state.Arrangement.Ordinary == TownArrangementRules.AllAdultCouncil)
            council = TownGovernanceRules.ChangeCouncil(council, adults, "all_adult", "arrangement", null, tick);
        // A ready winner waiting for this handover must not hold back the council election it also needs.
        var holdOtherElections = state.Contest is { Stage: "voting" } or { Stage: "ready", Purpose: not "handover" };
        // Representatives the current arrangement would not elect wait for the handover to complete.
        var deferFullHandover = targetNeedsCouncil && !currentElectsCouncil;
        var fallbackBefore = council.Fallback;
        council = TownGovernanceRules.Advance(council, townId, seed, adults, tick, day,
            allowNewElections: (currentNeedsCouncil || targetNeedsCouncil) && !holdOtherElections,
            forceRepresentation: targetNeedsCouncil || state.Arrangement.Ordinary == TownArrangementRules.ElectedCouncil,
            deferFullHandover: deferFullHandover);
        // A failed election forced by this handover must not leave the council retrying for
        // candidates: residents never approved that representation.
        if (deferFullHandover && council.Fallback == "candidates" && fallbackBefore != "candidates")
            council = TownGovernanceRules.ChangeCouncil(council, council.Members, council.Form, fallbackBefore, council.TermEndTick, tick);
        (council, state) = TownLawRules.Enact(council, state, townId, townName, tick);

        (council, state) = AdvanceMayor(council, state, townId, adults, tick, day);
        if (handover is not null)
        {
            var winner = state.Contest is { Stage: "ready", Purpose: "handover" } contest && contest.ChangeId == handover.Id ? contest : null;
            var officeReady = RequiredSuccessorMandates(state.Arrangement, handover).All(m =>
                winner is not null && Has(winner.Mandates.Split('+'), m));
            var councilReady = !targetNeedsCouncil || council.Form == "representative" && council.Members.Count == TownGovernanceRules.Seats ||
                council.Election is { Stage: "ready", SettledSeats.Count: TownGovernanceRules.Seats };
            if (officeReady && councilReady && tick <= handover.HandoverDeadlineTick)
            {
                if (winner is not null) (council, state) = SeatMayor(council, state, winner, tick, day);
                if (state.Contest?.ChangeId == handover.Id)
                    (council, state) = ArchiveContest(council, state, "cancelled", "The government handover completed without needing this election.", adults, tick, day);
                foreach (var office in state.Offices.Where(o => !Has(Mandates(handover.Target), o.Mandates)).ToArray())
                {
                    if (office.HolderId is not null) (council, state) = EndOffice(council, state, office, tick, "Residents ended this mandate.");
                    state = state with { Offices = state.Offices.Where(o => o.Mandates != office.Mandates).ToArray() };
                }
                state = state with { Arrangement = handover.Target };
                if (state.Contest is { Purpose: not "handover" } obsolete &&
                    obsolete.Mandates.Split('+').Any(m => !Has(Mandates(handover.Target), m)))
                    (council, state) = ArchiveContest(council, state, "cancelled", "The protected handover ended a mandate this contest would fill.", adults, tick, day);
                state = Replace(state, handover with { Status = "completed", SettledTick = tick, SuccessorId = winner?.WinnerId });
                if (targetNeedsCouncil && council.Election is { Stage: "ready" } successor)
                    council = TownGovernanceRules.SeatFullCouncil(council, successor, tick, day);
                else if (changesOrdinaryAuthority && !targetNeedsCouncil)
                {
                    council = TownGovernanceRules.CancelElection(council, tick, "The protected government handover changed ordinary authority.");
                    var governing = GoverningOffice(state)?.HolderId;
                    council = TownGovernanceRules.ChangeCouncil(council,
                        handover.Target.Ordinary == TownArrangementRules.Mayor && governing is not null ? [governing] : adults,
                        handover.Target.Ordinary == TownArrangementRules.Mayor && governing is not null ? "leader" : "all_adult", "arrangement", null, tick);
                }
                council = Notice(council, "government", handover.Id, "Government handover completed. " + TownArrangementRules.Declaration(handover.Target), tick);
            }
            else if (tick >= handover.HandoverDeadlineTick)
            {
                state = Replace(state, handover with { Status = "cancelled", SettledTick = tick, Reason = "No valid successor was ready within three days." });
                if (state.Contest?.ChangeId == handover.Id)
                    (council, state) = ArchiveContest(council, state, "cancelled", "The creating government transition expired.", adults, tick, day);
                if (deferFullHandover)
                    council = TownGovernanceRules.CancelElection(council, tick, "The creating government transition expired.");
                council = Notice(council, "government", handover.Id, "Government handover cancelled: no valid successor was ready within three days. Existing lawful authority remains.", tick);
            }
        }
        // A new leader elected after a vacancy resumes the already approved authority.
        if (state.Arrangement.Ordinary == TownArrangementRules.Mayor)
        {
            var holder = GoverningOffice(state)?.HolderId;
            council = TownGovernanceRules.ChangeCouncil(council, holder is not null ? [holder] : adults,
                holder is not null ? "leader" : "all_adult", "arrangement", null, tick);
        }
        (council, state) = TownLawRules.Enact(council, state, townId, townName, tick);
        return OpenNext(council, state, adults, tick, day);
    }

    private static bool NeedsElectedCouncil(TownArrangement arrangement, int adults) =>
        arrangement.Ordinary == TownArrangementRules.ElectedCouncil && adults > TownGovernanceRules.Seats ||
        arrangement.Ordinary == TownArrangementRules.Council && adults >= TownGovernanceRules.RepresentationThreshold;
}

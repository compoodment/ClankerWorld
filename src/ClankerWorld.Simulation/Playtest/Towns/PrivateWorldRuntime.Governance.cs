using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.Harness;
using System.Text.RegularExpressions;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private readonly Dictionary<string, CivicHistoryIndex> civicHistoryIndexes = new(StringComparer.Ordinal);

    private sealed class CivicHistoryIndex
    {
        private static readonly IReadOnlySet<string> Empty = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, HashSet<string>> knownByActor = new(StringComparer.Ordinal);
        private readonly Dictionary<string, HashSet<string>> subjectsByActor = new(StringComparer.Ordinal);
        private readonly Dictionary<string, HashSet<(string Kind, string Subject)>> kindsByActor = new(StringComparer.Ordinal);
        private readonly Dictionary<string, TownCivicReceipt[]> recentByActor = new(StringComparer.Ordinal);
        public TownGovernanceState State { get; }
        public Dictionary<string, TownCivicNotice> Notices { get; }

        public CivicHistoryIndex(TownGovernanceState state)
        {
            State = state;
            Notices = state.Notices.ToDictionary(n => n.Id, StringComparer.Ordinal);
            foreach (var actor in state.Knowledge.GroupBy(k => k.AgentId, StringComparer.Ordinal))
            {
                knownByActor[actor.Key] = actor.Select(k => k.NoticeId).ToHashSet(StringComparer.Ordinal);
                subjectsByActor[actor.Key] = actor.Select(k => Notices[k.NoticeId].SubjectId).ToHashSet(StringComparer.Ordinal);
                kindsByActor[actor.Key] = actor.Select(k => (Notices[k.NoticeId].Kind, Notices[k.NoticeId].SubjectId)).ToHashSet();
                recentByActor[actor.Key] = actor.OrderByDescending(k => k.LearnedTick)
                    .ThenByDescending(k => Notices[k.NoticeId].PostedTick).Take(3).ToArray();
            }
        }

        public IReadOnlySet<string> Known(string actor) => knownByActor.TryGetValue(actor, out var known) ? known : Empty;
        public bool Knows(string actor, string subject) => subjectsByActor.TryGetValue(actor, out var known) && known.Contains(subject);
        public bool Knows(string actor, string kind, string subject) => kindsByActor.TryGetValue(actor, out var known) && known.Contains((kind, subject));
        public TownCivicReceipt[] Recent(string actor) => recentByActor.TryGetValue(actor, out var recent) ? recent : [];
    }

    private CivicHistoryIndex CivicHistory(TownRuntimeState town)
    {
        if (!civicHistoryIndexes.TryGetValue(town.Id, out var history) || !ReferenceEquals(history.State, town.Governance))
            civicHistoryIndexes[town.Id] = history = new CivicHistoryIndex(town.Governance!);
        return history;
    }

    private string[] TownAdults(TownRuntimeState town) => town.ResidentIds.Where(id =>
        society.Checkpoint.Inhabitants.Any(p => p.Id == id && p.Status == SocietyInhabitantStatus.Active &&
            p.AgeBand is SocietyAgeBand.Adult or SocietyAgeBand.Elder)).Order(StringComparer.Ordinal).ToArray();
    private int CivicDay => worldSystems.Config.TicksPerDay;
    private string? CivicNote(string actor)
    {
        var learned = towns.Where(t => t.Governance is not null).SelectMany(t =>
        {
            var history = CivicHistory(t);
            return history.Recent(actor).Select(k => (Receipt: k, Notice: history.Notices[k.NoticeId], Town: t.Name));
        })
            .OrderByDescending(n => n.Receipt.LearnedTick).ThenByDescending(n => n.Notice.PostedTick).Take(3)
            .Select(n => (n.Receipt.SourceAgentId is { } source ? $"Heard from {society.Checkpoint.GetInhabitant(source).Name}: " : "Read Town notice: ") +
                n.Town + ": " + ReadableCivicNotice(n.Notice.Text)).ToArray();
        var excerpt = string.Join(" | ", learned);
        return learned.Length == 0 ? null : excerpt.Length > 1024 ? excerpt[..1024] : excerpt;
    }


    [GeneratedRegex(@"\btick (\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex CivicTickText();

    private string ReadableCivicNotice(string text)
    {
        foreach (var person in society.Checkpoint.Inhabitants.OrderByDescending(p => p.Id.Length))
            text = text.Replace(person.Id, person.Name, StringComparison.Ordinal);
        text = CivicTickText().Replace(text, match =>
            long.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var tick) && tick / CivicDay < long.MaxValue
                ? "world day " + (tick / CivicDay + 1).ToString(CultureInfo.InvariantCulture)
                : match.Value);
        return text.Length > 270 ? text[..270] : text;
    }

    private void AdvanceTownGovernance()
    {
        foreach (var town in towns.ToArray())
        {
            if (town.FoundingState != "founded") continue;
            var adults = TownAdults(town);
            var governance = town.Governance ?? TownGovernanceState.Create(adults);
            SaveTownGovernance(town, TownGovernanceRules.Advance(governance, town.Id, worldSeed, adults, WorldTick, CivicDay));
        }
    }

    private void SaveTownGovernance(TownRuntimeState town, TownGovernanceState updated)
    {
        if (town.Governance == updated) return;
        var priorNotices = town.Governance?.Notices.Count ?? 0;
        SetTown(town with { Governance = updated });
        foreach (var notice in updated.Notices.Skip(priorNotices))
            AppendEvent("town_civic_" + notice.Kind, $"{town.Id}|{notice.SubjectId}|{notice.Text}", CivicBoard(town));
    }

    private GridPoint? CivicBoard(TownRuntimeState town) =>
        worldSimulation.Buildings.Where(b => b.TownId == town.Id && b.HouseholdId is null &&
            worldContent.Buildings.Any(d => d.CanonicalId == b.DefinitionId && d.Tags.Contains(TownHallContent.HallTag, StringComparer.Ordinal)))
            .OrderBy(b => b.PlacedTick).ThenBy(b => b.InstanceId, StringComparer.Ordinal)
            .Select(b => (GridPoint?)(b.Entrance ?? TownHallContent.Entrance(b.Position))).FirstOrDefault() ?? town.OriginSite ??
        worldSimulation.Buildings.FirstOrDefault(b => b.TownId == town.Id && b.HouseholdId is null)?.Position ??
        map.CampObjects.FirstOrDefault(p => p.Kind == "storage" && town.BorderTiles.Contains(p.Position))?.Position ??
        town.BorderTiles.Where(map.IsBuildable).OrderBy(p => p.Y).ThenBy(p => p.X).Select(p => (GridPoint?)p).FirstOrDefault();
    private bool NearCivicBoard(string actor, TownRuntimeState town) => CivicBoard(town) is { } board &&
        IsWithinInteractionRange(inhabitants[actor].Position, board, ResourceInteractionRange);
    private static string CivicAgentToken(string id) => id.Length <= 128 ? id :
        "agent-sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(id)));
    private string ResolveCivicAgentToken(string token) => society.Checkpoint.Inhabitants.FirstOrDefault(p =>
        p.Id == token || CivicAgentToken(p.Id) == token)?.Id ?? token;
    private string CivicAction(string town, string kind, string subject = "", string choice = "") =>
        $"civic|{town}|{kind}|{(society.Checkpoint.Inhabitants.Any(p => p.Id == subject) ? CivicAgentToken(subject) : subject)}|{CivicAgentToken(choice)}";
    private static string CivicRoundToken(TownElection election) =>
        $"{election.Id}:{election.Stage}:{election.OpenedTick.ToString(CultureInfo.InvariantCulture)}";

    private void AddTownCivicCandidates(List<CognitionCandidate> candidates, string actor)
    {
        // Civic agreement and votes are explicit personal choices, never a built-in idle alternative.
        if (NeedsUrgentWarmth(inhabitants[actor])) return;
        foreach (var town in towns.Where(t => t.Governance is not null))
        {
            var state = town.Governance!;
            var history = CivicHistory(town);
            var known = history.Known(actor);
            var adults = TownAdults(town);
            var resident = adults.Contains(actor, StringComparer.Ordinal);
            if (NearCivicBoard(actor, town) && known.Count < state.Notices.Count)
                candidates.Add(new(CivicAction(town.Id, "read"), $"Read the actual civic notices posted at {town.Name}.", 155));
            if (resident)
            {
                if (!NearCivicBoard(actor, town) && CivicBoard(town) is not null)
                    candidates.Add(new(CivicAction(town.Id, "visit"), $"Visit {town.Name}\'s public notice place to see what has been posted.", 175));
                if (!state.Candidates.Any(c => c.AgentId == actor && c.FullTerm))
                    candidates.Add(new(CivicAction(town.Id, "register"), $"Personally agree to stand for full council terms in {town.Name}.", 185));
                if (state.Form == "representative" && state.TermEndTick is { } end &&
                    !state.Candidates.Any(c => c.AgentId == actor && c.RemainderTermEndTick == end))
                    candidates.Add(new(CivicAction(town.Id, "remainder", end.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                        $"Agree only to fill a vacancy until this council term ends in {town.Name}; this does not enter the next full-term contest.", 186));
                if (state.Candidates.Any(c => c.AgentId == actor))
                    candidates.Add(new(CivicAction(town.Id, "withdraw_candidate"), $"Withdraw your willingness to stand in {town.Name}.", 190));
                foreach (var nearby in inhabitants.Values.Where(p => p.InhabitantId != actor && AdultResident(p.InhabitantId) &&
                    IsWithinInteractionRange(p.Position, inhabitants[actor].Position, ResourceInteractionRange)))
                {
                    var nominee = nearby.InhabitantId;
                    if (resident && adults.Contains(nominee, StringComparer.Ordinal) &&
                        !state.Candidates.Any(c => c.AgentId == nominee && c.FullTerm) &&
                        !state.Notices.Any(n => n.Kind == "nomination" && n.SubjectId == "nomination:" + actor + "->" + nominee))
                        candidates.Add(new(CivicAction(town.Id, "nominate", nominee),
                            $"Nominate {society.Checkpoint.GetInhabitant(nominee).Name} for full council terms in {town.Name}; they must personally agree before becoming a candidate.", 188));
                    if (TownForResident(nominee) is null)
                        candidates.Add(new(CivicAction(town.Id, "request_admission", nominee),
                            $"Ask {town.Name}'s council to approve admission of {society.Checkpoint.GetInhabitant(nominee).Name}, the adult nearby. A request grants no membership or stock access.", 188));
                }
                candidates.Add(new(CivicAction(town.Id, "propose"), $"Submit an ordinary social-law proposal to {town.Name}; include civic_proposal text. It needs the current council's votes and changes no physical rights.", 190));
                AddTownProjectProposalCandidates(candidates, actor, town);
            }
            else if (!towns.Any(t => t.ResidentIds.Contains(actor, StringComparer.Ordinal)) && AdultResident(actor) && NearCivicBoard(actor, town))
                candidates.Add(new(CivicAction(town.Id, "admission"), $"Ask {town.Name}'s council to approve your admission. The request grants no membership or stock access.", 170));
            foreach (var proposal in state.Proposals.Where(p => p.Status == "pending" && history.Knows(actor, p.Id)))
            {
                if (proposal.AuthorId == actor)
                    candidates.Add(new(CivicAction(town.Id, "withdraw_proposal", proposal.Id), $"Withdraw your pending proposal: {proposal.Text}", 190));
                if (!proposal.Voters.Contains(actor, StringComparer.Ordinal) || proposal.Votes.Any(v => v.AgentId == actor) ||
                    proposal.Kind == "admission" && proposal.SubjectId == actor) continue;
                candidates.Add(new(CivicAction(town.Id, "yes", proposal.Id), $"Cast your final yes vote on {proposal.Text} in {town.Name}. {proposal.RequiredYes} yes votes required.", 165));
                candidates.Add(new(CivicAction(town.Id, "no", proposal.Id), $"Cast your final no vote on {proposal.Text} in {town.Name}.", 166));
            }
            if (state.Election is { Stage: "main" or "runoff" } election && election.Voters.Contains(actor, StringComparer.Ordinal) &&
                history.Knows(actor, election.Stage == "main" ? "election" : "runoff", election.Id))
            {
                candidates.Add(new(CivicAction(town.Id, "ballot", CivicRoundToken(election)), $"Submit or revise your {election.Stage} ballot in {town.Name}; choose up to {election.Seats} distinct IDs via civic_ballot. " +
                    $"Willing candidates: {string.Join(", ", election.Candidates.Select(id => society.Checkpoint.GetInhabitant(id).Name + " (" + CivicAgentToken(id) + ")"))}. Self-voting is allowed. " +
                    $"Voting closes on world day {election.DeadlineTick / CivicDay + 1}.", 165));
                foreach (var id in election.Candidates)
                    candidates.Add(new(CivicAction(town.Id, "single", CivicRoundToken(election), id), $"Submit or revise your ballot to support only {society.Checkpoint.GetInhabitant(id).Name} in {town.Name}. Other previous choices are replaced.", 167));
            }
            foreach (var recipient in inhabitants.Values.Where(p => p.InhabitantId != actor &&
                IsWithinInteractionRange(p.Position, inhabitants[actor].Position, ResourceInteractionRange)))
            {
                var recipientKnows = history.Known(recipient.InhabitantId);
                if (!known.Any(id => !recipientKnows.Contains(id))) continue;
                candidates.Add(new(CivicAction(town.Id, "relay", recipient.InhabitantId), $"Relay the civic notices you actually learned in {town.Name} to {society.Checkpoint.GetInhabitant(recipient.InhabitantId).Name} nearby.", 180));
            }
        }
    }

    private void ContinueTownCivicVisit(string actor, string candidate)
    {
        var parts = candidate.Split('|');
        if (parts.Length != 5 || parts[2] != "visit" || NeedsUrgentWarmth(inhabitants[actor])) return;
        var town = towns.SingleOrDefault(t => t.Id == parts[1]);
        if (town?.Governance is null || !TownAdults(town).Contains(actor, StringComparer.Ordinal) ||
            CivicBoard(town) is not { } destination ||
            IsWithinInteractionRange(inhabitants[actor].Position, destination, ResourceInteractionRange)) return;
        MoveToward(actor, inhabitants[actor], destination, "town_notices", ResourceInteractionRange);
    }

    private void ApplyTownCivicCandidate(string actor, string candidate, string? proposalText = null, IReadOnlyList<string>? ballot = null)
    {
        var parts = candidate.Split('|');
        if (parts.Length != 5) return;
        var selectedId = candidate;
        if (parts[2] is "nominate" or "relay" or "request_admission") parts[3] = ResolveCivicAgentToken(parts[3]);
        if (parts[2] == "single") parts[4] = ResolveCivicAgentToken(parts[4]);
        var town = towns.SingleOrDefault(t => t.Id == parts[1]);
        if (town?.Governance is not { } state) return;
        // Current authority, live notice knowledge and exact contest IDs are checked again when delayed responses arrive.
        var current = new List<CognitionCandidate>();
        AddTownCivicCandidates(current, actor);
        if (!current.Any(c => c.Id == selectedId)) return;
        try
        {
            state = TownGovernanceRules.Advance(state, town.Id, worldSeed, TownAdults(town), WorldTick, CivicDay);
            if (parts[2] is "ballot" or "single" &&
                (state.Election is not { } currentRound || CivicRoundToken(currentRound) != parts[3])) return;
            switch (parts[2])
            {
                case "visit":
                    if (CivicBoard(town) is { } destination) MoveToward(actor, inhabitants[actor], destination, "town_notices", ResourceInteractionRange);
                    break;
                case "read":
                    state = TownGovernanceRules.LearnNotices(state, actor, state.Notices.Select(n => n.Id), WorldTick);
                    break;
                case "relay":
                    var known = CivicHistory(town).Known(actor);
                    state = TownGovernanceRules.LearnNotices(state, parts[3], state.Notices.Where(n => known.Contains(n.Id)).Select(n => n.Id), WorldTick, actor);
                    break;
                case "nominate": state = TownGovernanceRules.Nominate(state, actor, parts[3], TownAdults(town), WorldTick); break;
                case "request_admission":
                    state = TownGovernanceRules.SubmitProposal(state, town.Id, actor, "admission", parts[3],
                        $"Admit {society.Checkpoint.GetInhabitant(parts[3]).Name} as a Town resident.", "council:" + state.Revision,
                        TownAdults(town), WorldTick, CivicDay);
                    break;
                case "register": state = TownGovernanceRules.Register(state, actor, true, null, TownAdults(town), WorldTick); break;
                case "remainder": state = TownGovernanceRules.Register(state, actor, false, state.TermEndTick, TownAdults(town), WorldTick); break;
                case "withdraw_candidate": state = TownGovernanceRules.WithdrawCandidate(state, actor, WorldTick); break;
                case "propose":
                    if (proposalText is null) return;
                    state = TownGovernanceRules.SubmitProposal(state, town.Id, actor, "law", null, proposalText,
                        "council:" + state.Revision, TownAdults(town), WorldTick, CivicDay);
                    break;
                case "project":
                    var plan = OfferedTownProjectPlan(town, actor, parts[3], parts[4], proposalText);
                    if (plan is null) return;
                    state = TownGovernanceRules.SubmitProposal(state, town.Id, actor, "project", null,
                        TownProjectRules.ProposalText(plan), "council:" + state.Revision,
                        TownAdults(town), WorldTick, CivicDay, project: plan);
                    break;
                case "admission":
                    state = TownGovernanceRules.SubmitProposal(state, town.Id, actor, "admission", actor,
                        $"Admit {society.Checkpoint.GetInhabitant(actor).Name} as a Town resident.", "council:" + state.Revision,
                        TownAdults(town), WorldTick, CivicDay);
                    break;
                case "yes": case "no": state = TownGovernanceRules.VoteProposal(state, parts[3], actor, parts[2] == "yes", WorldTick); break;
                case "withdraw_proposal": state = TownGovernanceRules.WithdrawProposal(state, parts[3], actor, WorldTick); break;
                case "ballot":
                    if (ballot is null) return;
                    state = TownGovernanceRules.VoteElection(state, state.Election!.Id, actor, ballot.Select(ResolveCivicAgentToken).ToArray(), WorldTick); break;
                case "single": state = TownGovernanceRules.VoteElection(state, state.Election!.Id, actor, [parts[4]], WorldTick); break;
            }
            state = TownGovernanceRules.Advance(state, town.Id, worldSeed, TownAdults(town), WorldTick, CivicDay);
            // Submitting and registering are actual notice interactions, so the actor knows their own posted notice.
            state = TownGovernanceRules.LearnNotices(state, actor,
                state.Notices.Skip(town.Governance.Notices.Count).Select(n => n.Id), WorldTick);
            SaveTownGovernance(town, state);
            MaintainTownProjects();
            AppendEvent("town_civic_action", $"{town.Id}|{actor}|{parts[2]}", inhabitants[actor].Position);
        }
        catch (InvalidOperationException)
        {
            AppendEvent("town_civic_action_rejected", $"{town.Id}|{actor}|{parts[2]}");
        }
    }
}

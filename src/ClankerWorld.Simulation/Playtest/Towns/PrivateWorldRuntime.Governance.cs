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
            var governance = town.Governance ?? TownGovernanceState.Create(TownAdults(town));
            var (council, government) = AdvanceCivic(town, governance, town.Government ?? TownGovernmentState.Create());
            SaveTownGovernance(town, council, government);
        }
    }

    private void SaveTownGovernance(TownRuntimeState town, TownGovernanceState updated, TownGovernmentState? government = null)
    {
        town = town with { LandHearings = towns.Single(current => current.Id == town.Id).LandHearings };
        government ??= town.Government;
        var priorNotices = town.Governance?.Notices.Count ?? 0;
        (town, updated) = ApplyApprovedTownLandClaims(town, updated);
        updated = ResolveHouseholdLandRequests(town, updated);
        var priorHearings = town.LandHearings;
        var transferUpdate = AdvanceTownLandTransfers(town with { Governance = updated, Government = government }, updated);
        updated = transferUpdate.Council;
        town = town with { LandHearings = transferUpdate.LandHearings };
        var hearingUpdate = AdvanceTownLandHearings(town with { Governance = updated, Government = government },
            updated, government ?? TownGovernmentState.Create());
        updated = hearingUpdate.Council;
        town = town with { LandHearings = hearingUpdate.LandHearings };
        if (town.Governance == updated && town.Government == government && priorHearings == town.LandHearings) return;
        SetTown(town with { Governance = updated, Government = government });
        foreach (var notice in updated.Notices.Skip(priorNotices))
            AppendEvent("town_civic_" + notice.Kind, $"{town.Id}|{notice.SubjectId}|{notice.Text}", CivicBoard(town));
    }

    private GridPoint? CivicBoard(TownRuntimeState town) => town.OriginSite ??
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
                    if (MayBeSponsoredForAdmission(nominee, town))
                        candidates.Add(new(CivicAction(town.Id, "request_admission", nominee),
                            $"Ask {town.Name}'s council to approve admission of {society.Checkpoint.GetInhabitant(nominee).Name}, the adult nearby. A request grants no membership or Warehouse access.", 188));
                }
                AddTownLawCandidates(candidates, actor, town);
                if (town.Government is not null) AddTownGovernmentCandidates(candidates, actor, town);
                var here = inhabitants[actor].Position;
                // The model sees no map grid, so name real claimable tiles it can choose from.
                if (TownLandClaimRules.ClaimableNear(map, town, townLandTitles, here, 6) is { Length: > 0 } nearest)
                    candidates.Add(new(CivicAction(town.Id, "claim_land"), $"Ask {town.Name}'s council to claim a connected plot of adjoining unclaimed land; include its exact coordinates in civic_land_tiles. " +
                        FormattableString.Invariant($"You stand at ({here.X}, {here.Y}); unclaimed tiles beside the Town's land nearest you: {string.Join("; ", nearest.Select(tile => FormattableString.Invariant($"({tile.X}, {tile.Y})")))}. ") +
                        "Existing titles, household rights, buildings and goods stay with their holders.", 190));
                if (society.Checkpoint.GetInhabitant(actor).HouseholdId is not null && RequestableLandNear(town, here, 6) is { Length: > 0 } free)
                    candidates.Add(new(CivicAction(town.Id, "request_land_use"), $"Ask {town.Name}'s council for household use of a connected plot of Town-titled land; include exact coordinates in civic_land_tiles. " +
                        FormattableString.Invariant($"You stand at ({here.X}, {here.Y}); free Town land nearest you: {string.Join("; ", free.Select(tile => FormattableString.Invariant($"({tile.X}, {tile.Y})")))}. ") +
                        "Filing grants nothing and supplies no household acceptance.", 190));
            }
            else if (NearCivicBoard(actor, town))
            {
                if (MayRequestOwnAdmission(actor, town))
                    candidates.Add(new(CivicAction(town.Id, "admission"), AdmissionRequestText(actor, town), 170));
            }
            else if (CivicBoard(town) is not null && MayVisitAsNewcomer(actor, town))
                candidates.Add(new(CivicAction(town.Id, "visit"), $"Walk to {town.Name}'s public notice place to read what is posted. Visiting grants no membership.", 175));
            AddTownAdmissionCandidates(candidates, actor, town);
            AddHouseholdLandCandidates(candidates, actor, town);
            AddTownLandHearingCandidates(candidates, actor, town);
            AddTownLandTransferCandidates(candidates, actor, town);
            foreach (var proposal in state.Proposals.Where(p => p.Status == "pending" && history.Knows(actor, p.Id)))
            {
                var draft = town.Government?.LawDrafts.SingleOrDefault(d => d.ProposalId == proposal.Id);
                var voteText = draft is not null ? TownLawRules.VoteText(draft) : proposal.Text;
                if (proposal.AuthorId == actor)
                    candidates.Add(new(CivicAction(town.Id, "withdraw_proposal", proposal.Id), $"Withdraw your pending proposal: {voteText}", 190));
                if (!proposal.Voters.Contains(actor, StringComparer.Ordinal) || proposal.Votes.Any(v => v.AgentId == actor) ||
                    proposal.Kind == "admission" && proposal.SubjectId == actor) continue;
                var plot = proposal.LandClaimTiles is { } tiles ? " Exact tiles: " + TownLandClaimRules.DescribeTiles(tiles) + "." :
                    proposal.Kind == "land_use" && householdLandUseRequests.SingleOrDefault(r => r.Id == proposal.SubjectId) is { } request
                        ? " " + LandUseTerms(request) : proposal.LandHearingRequest is { } hearingRequest
                            ? " " + TownLandGovernmentFilingRules.Describe(hearingRequest) : "";
                candidates.Add(new(CivicAction(town.Id, "yes", proposal.Id), $"Cast your final yes vote on {voteText} in {town.Name}. {proposal.RequiredYes} yes votes required.{plot}", 165));
                candidates.Add(new(CivicAction(town.Id, "no", proposal.Id), $"Cast your final no vote on {voteText} in {town.Name}.{plot}", 166));
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
        if (town?.Governance is null || !TownAdults(town).Contains(actor, StringComparer.Ordinal) && !MayVisitAsNewcomer(actor, town) && !MayVisitLandHearing(actor, town) && !MayVisitLandTransfer(actor, town) ||
            CivicBoard(town) is not { } destination ||
            IsWithinInteractionRange(inhabitants[actor].Position, destination, ResourceInteractionRange)) return;
        MoveToward(actor, inhabitants[actor], destination, "town_notices", ResourceInteractionRange);
    }

    private void ApplyTownCivicCandidate(string actor, string candidate, string? proposalText = null, IReadOnlyList<string>? ballot = null,
        IReadOnlyList<CognitionLandTile>? landTiles = null, CognitionLandHearingChoice? hearingChoice = null)
    {
        var parts = candidate.Split('|');
        if (parts.Length != 5) return;
        var selectedId = candidate;
        if (parts[2] is "nominate" or "relay" or "request_admission") parts[3] = ResolveCivicAgentToken(parts[3]);
        if (parts[2] == "single") parts[4] = ResolveCivicAgentToken(parts[4]);
        var town = towns.SingleOrDefault(t => t.Id == parts[1]);
        if (town?.Governance is not { } state) return;
        var government = town.Government ?? TownGovernmentState.Create();
        // Current authority, live notice knowledge and exact contest IDs are checked again when delayed responses arrive.
        var current = new List<CognitionCandidate>();
        AddTownCivicCandidates(current, actor);
        if (!current.Any(c => c.Id == selectedId)) return;
        if (parts[2] == "accept_admission")
        {
            AcceptTownAdmission(actor, town.Id, parts[3]);
            return;
        }
        try
        {
            (state, government) = AdvanceCivic(town, state, government);
            if (parts[2].StartsWith("hearing_", StringComparison.Ordinal))
            {
                var hearingUpdate = ApplyTownLandHearingAction(town with { Governance = state, Government = government },
                    actor, parts[2], parts[3], parts[4],
                    proposalText, landTiles, hearingChoice, state, government);
                state = hearingUpdate.Council;
                town = town with { LandHearings = hearingUpdate.LandHearings };
                SetTown(town with { Governance = state, Government = government });
            }
            else if (parts[2].StartsWith("land_transfer_", StringComparison.Ordinal))
            {
                var transferUpdate = ApplyTownLandTransferAction(town with { Governance = state, Government = government },
                    actor, parts[2], parts[3], parts[4], landTiles, hearingChoice, state);
                state = transferUpdate.Council;
                town = town with { LandHearings = transferUpdate.LandHearings };
                SetTown(town with { Governance = state, Government = government });
            }
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
                case "propose" or "amend" or "repeal":
                    if (proposalText is null && parts[2] != "repeal") return;
                    (state, government) = ApplyTownLawAction(town, actor, parts[2], parts[3], parts[4], proposalText, state, government);
                    break;
                case "claim_land": state = SubmitTownLandClaim(town, state, actor, landTiles); break;
                case "request_land_use": state = SubmitPersonalLandUseRequest(town, state, actor, landTiles); break;
                case "request_expansion_land": state = SubmitExpansionLandRequest(town, state, actor, parts[3]); break;
                case "accept_land_use":
                case "decline_land_use":
                case "withdraw_land_use":
                    state = DecideHouseholdLandUse(state, actor, parts[3], parts[2]); break;
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
                case "government_propose" or "government_replace" or "government_yes" or "government_no" or "government_withdraw" or
                    "mayor_register" or "mayor_withdraw" or "mayor_resign" or "mayor_vote":
                    (state, government) = ApplyTownGovernmentAction(town, actor, parts[2], parts[3], parts[4], state, government);
                    break;
                case "single": state = TownGovernanceRules.VoteElection(state, state.Election!.Id, actor, [parts[4]], WorldTick); break;
            }
            (state, government) = AdvanceCivic(town, state, government);
            // Submitting and registering are actual notice interactions, so the actor knows their own posted notice.
            state = TownGovernanceRules.LearnNotices(state, actor,
                state.Notices.Skip(town.Governance.Notices.Count).Select(n => n.Id), WorldTick);
            SaveTownGovernance(town, state, government);
            AppendEvent("town_civic_action", $"{town.Id}|{actor}|{parts[2]}", inhabitants[actor].Position);
            SettleTownAdmissions();
        }
        catch (InvalidOperationException)
        {
            AppendEvent("town_civic_action_rejected", $"{town.Id}|{actor}|{parts[2]}");
        }
    }
}

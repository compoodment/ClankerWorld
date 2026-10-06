using ClankerWorld.Simulation.Cognition;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private void AddTownGovernmentCandidates(List<CognitionCandidate> candidates, string actor, TownRuntimeState town)
    {
        var government = town.Government!;
        var history = CivicHistory(town);
        // Each choice costs prompt space on every model call, so protected proposals are
        // made at the notice place, like admission requests.
        var atNoticePlace = NearCivicBoard(actor, town);
        foreach (var target in TownArrangementRules.Supported.Where(a => atNoticePlace && a != government.Arrangement))
            candidates.Add(new(CivicAction(town.Id, "government_propose", TownArrangementRules.Key(target)),
                $"Initiate a protected resident vote in {town.Name}: {TownArrangementRules.Declaration(target)} " +
                "More than half the eligible adult residents must approve; incumbent permission is not needed. No power changes before a valid handover.", 195));
        if (atNoticePlace && government.Arrangement.NonLand == "none")
            foreach (var office in government.Offices.Where(office => office.Mandates is "land" or "ordinary" && office.HolderId is not null && office.TermEndTick > WorldTick))
                candidates.Add(new(CivicAction(town.Id, "government_extend_non_land", TownArrangementRules.Key(government.Arrangement with { NonLand = TownArrangementRules.Mayor }), NonLandExtensionToken(office)),
                    "Propose adding nonviolent civil adjudication to " + society.Checkpoint.GetInhabitant(office.HolderId!).Name +
                    "'s existing " + TownArrangementRules.MandateLabel(office.Mandates) + " term. Resident majority and their separate personal consent are required; the existing term deadline stays unchanged.", 195));
        foreach (var change in government.Changes.Where(change => change.Status is "queued" or "voting" or "handover" &&
                     change.NonLandExtension is { ConsentTick: null } extension && extension.HolderId == actor &&
                     TownGovernmentRules.CanAcceptNonLandDuties(town.Governance!, government, change.Id, actor, TownAdults(town), WorldTick)))
            candidates.Add(new(CivicAction(town.Id, "government_accept_non_land", change.Id),
                "Personally accept these proposed additional nonviolent adjudication duties for your existing term. This supplies no resident vote, and does not restart your term.", 185));
        if (atNoticePlace && TownArrangementRules.HasOffice(government.Arrangement))
            candidates.Add(new(CivicAction(town.Id, "government_replace", TownArrangementRules.Key(government.Arrangement)),
                $"Ask {town.Name}'s adult residents to approve early replacement of the elected mayoral mandates. Approval requires a resident majority and a valid successor election.", 196));
        foreach (var change in government.Changes.Where(c => c.Status is "queued" or "voting" && history.Knows(actor, c.Id)))
        {
            if (change.AuthorId == actor)
                candidates.Add(new(CivicAction(town.Id, "government_withdraw", change.Id), "Withdraw your unfinished protected government proposal.", 197));
            if (change.Status != "voting" || !history.Knows(actor, TownGovernmentRules.VoteNoticeToken(change)) ||
                !change.Voters.Contains(actor, StringComparer.Ordinal) || change.Votes.Any(v => v.AgentId == actor)) continue;
            candidates.Add(new(CivicAction(town.Id, "government_yes", change.Id),
                $"Cast your final yes vote on the protected resident proposal in {town.Name}. {TownArrangementRules.Declaration(change.Target)}", 165));
            candidates.Add(new(CivicAction(town.Id, "government_no", change.Id),
                $"Cast your final no vote on the protected resident proposal in {town.Name}. {TownArrangementRules.Declaration(change.Target)}", 166));
        }
        var officeInPlay = TownArrangementRules.HasOffice(government.Arrangement) || government.Contest is not null ||
            government.Changes.Any(c => c.Status is "queued" or "voting" or "handover" && TownArrangementRules.HasOffice(c.Target));
        foreach (var mandates in TownArrangementRules.SupportedMandates)
        {
            var willing = government.Consents.Any(c => c.AgentId == actor && c.Mandates == mandates);
            if (!officeInPlay && !willing) continue;
            candidates.Add(new(CivicAction(town.Id, willing ? "mayor_withdraw" : "mayor_register", mandates),
                willing ? $"Withdraw willingness to seek election for {TownArrangementRules.MandateLabel(mandates)} in {town.Name}; this does not resign a held office."
                    : $"Personally agree to seek election for {TownArrangementRules.MandateLabel(mandates)} in {town.Name}, if residents authorize that office. This grants no authority or Council candidacy. To add duties to a current office, use its protected extension; this registration cannot restart that term.", 190));
        }
        foreach (var office in government.Offices.Where(o => o.HolderId == actor))
            candidates.Add(new(CivicAction(town.Id, "mayor_resign", office.Mandates),
                $"Resign only your mandate for {TownArrangementRules.MandateLabel(office.Mandates)} in {town.Name}. Any separate mandate continues.", 198));
        if (government.Contest is { Stage: "voting", RoundDeadlineTick: { } closes } contest && contest.Voters.Contains(actor, StringComparer.Ordinal) &&
            history.Knows(actor, TownGovernmentRules.RoundToken(contest)))
            foreach (var id in contest.Candidates)
                candidates.Add(new(CivicAction(town.Id, "mayor_vote", TownGovernmentRules.RoundToken(contest), id),
                    $"Submit or revise your mayoral ballot for {society.Checkpoint.GetInhabitant(id).Name} in {town.Name}, for {TownArrangementRules.MandateLabel(contest.Mandates)}. " +
                    $"Round {contest.Round} closes on world day {CivicDayNumber(closes)}. A tie requires another vote, never a draw.", 167));
    }

    private static string NonLandExtensionToken(TownOffice office) => NonviolentToken(office.Mandates + "|" + office.ElectionId);
    private static TownOffice NonLandExtensionOffice(TownGovernmentState government, string token) =>
        government.Offices.SingleOrDefault(office => NonLandExtensionToken(office) == token) ??
            throw new InvalidOperationException("The proposed added duties refer to an earlier office term.");

    private (TownGovernanceState Council, TownGovernmentState Government) ApplyTownGovernmentAction(
        TownRuntimeState town, string actor, string action, string subject, string choice,
        TownGovernanceState council, TownGovernmentState government) => action switch
        {
            "government_propose" or "government_replace" => TownGovernmentRules.Propose(council, government, town.Id, actor,
                TownArrangementRules.Parse(subject) ?? throw new InvalidOperationException("Unsupported government arrangement."),
                action == "government_replace", TownAdults(town), WorldTick, CivicDay),
            "government_extend_non_land" => TownGovernmentRules.Propose(council, government, town.Id, actor,
                TownArrangementRules.Parse(subject) ?? throw new InvalidOperationException("Unsupported government arrangement."),
                false, TownAdults(town), WorldTick, CivicDay,
                nonLandBaseMandate: NonLandExtensionOffice(government, choice).Mandates,
                expectedNonLandBaseElectionId: NonLandExtensionOffice(government, choice).ElectionId),
            "government_accept_non_land" => TownGovernmentRules.AcceptNonLandDuties(council, government, subject, actor, TownAdults(town), WorldTick),
            "government_yes" or "government_no" => (council, TownGovernmentRules.Vote(government, subject, actor, action == "government_yes", WorldTick)),
            "government_withdraw" => TownGovernmentRules.Withdraw(council, government, subject, actor, WorldTick),
            "mayor_register" => TownGovernmentRules.RegisterMayor(council, government, actor, subject, TownAdults(town), WorldTick),
            "mayor_withdraw" => TownGovernmentRules.WithdrawMayor(council, government, actor, subject, WorldTick),
            "mayor_resign" => TownGovernmentRules.Resign(council, government, actor, subject, WorldTick),
            "mayor_vote" => (council, TownGovernmentRules.VoteMayor(government, subject, actor, ResolveCivicAgentToken(choice), WorldTick)),
            _ => throw new InvalidOperationException("Unsupported government action."),
        };
}

namespace ClankerWorld.Simulation.Playtest;

public static partial class TownGovernmentRules
{
    private static string ExtensionDeclaration(TownNonLandExtension? extension) => extension is null ? "" :
        $" Non-land duties would be added to {extension.HolderId}'s current {extension.BaseMandate} elected term, only with their personal acceptance; its ending stays at tick {extension.TermEndTick}.";

    public static string ChangeRequestKey(string kind, TownArrangement target, TownNonLandExtension? extension = null) =>
        kind + ":" + TownArrangementRules.Key(target) + (extension is null ? "" :
            ":extend:" + extension.BaseMandate + ":" + extension.BaseElectionId);

    /// <summary>An incumbent takes newly added duties through the named extension, preserving the elected term.</summary>
    public static bool CanStandForMayoralContest(TownGovernmentState state, string actor, string mandates, string? changeId) =>
        !mandates.Split('+').Contains("non_land") ||
        state.Changes.SingleOrDefault(change => change.Id == changeId) is not
            { Kind: "arrangement", NonLandExtension: null, Target.NonLand: TownArrangementRules.Mayor } ||
        state.Arrangement.NonLand != TownArrangementRules.NoOffice ||
        !state.Offices.Any(office => office.HolderId == actor && office.Mandates is "land" or "ordinary");

    /// <summary>Personally accepts only the added duties in an actually learned, exact incumbent proposal.</summary>
    public static (TownGovernanceState Council, TownGovernmentState Government) AcceptNonLandDuties(
        TownGovernanceState council, TownGovernmentState state, string changeId, string actor,
        IEnumerable<string> adultResidents, long tick)
    {
        if (!CanAcceptNonLandDuties(council, state, changeId, actor, adultResidents, tick))
            throw new InvalidOperationException("The named current officeholder must personally accept the added duties after learning this exact protected proposal.");
        var change = state.Changes.Single(item => item.Id == changeId);
        var extension = change.NonLandExtension!;
        if (extension.ConsentTick is not null) return (council, state);
        state = Replace(state, change with { NonLandExtension = extension with { ConsentTick = tick } });
        return (Notice(council, "government", change.Id,
            actor + " accepted the proposed non-land adjudication duties for the remainder of the named elected term. Authority still waits for protected handover.", tick), state);
    }

    public static bool CanAcceptNonLandDuties(TownGovernanceState council, TownGovernmentState state,
        string changeId, string actor, IEnumerable<string> adultResidents, long tick) =>
        state.Changes.SingleOrDefault(item => item.Id == changeId) is
            { Status: "queued" or "voting" or "handover", NonLandExtension: { } extension } change &&
        state.Arrangement.NonLand == TownArrangementRules.NoOffice &&
        extension.HolderId == actor && Has(adultResidents, actor) && ExtensionBaseCurrent(state, extension, tick) &&
        council.Knowledge.Any(receipt => receipt.AgentId == actor && receipt.LearnedTick <= tick &&
            council.Notices.Any(notice => notice.Id == receipt.NoticeId && notice.Kind == "government" &&
                (notice.SubjectId == change.Id || notice.SubjectId == VoteNoticeToken(change))));

    /// <summary>The scope approved by residents at the given time, independent of who currently holds it.</summary>
    public static bool NonLandScopeAt(TownGovernmentState state, long tick, bool includeEndingTick = false)
    {
        if (!includeEndingTick)
            return (state.Changes.LastOrDefault(change => change.Status == "completed" && change.SettledTick <= tick)?.Target ??
                TownArrangementRules.Initial).NonLand == TownArrangementRules.Mayor;
        // Multiple actors can act in one committed tick. History cannot claim sub-tick
        // ordering, so a scope that ended during this tick can support an earlier finding.
        return (state.Changes.LastOrDefault(change => change.Status == "completed" && change.SettledTick < tick)?.Target ??
                TownArrangementRules.Initial).NonLand == TownArrangementRules.Mayor ||
            state.Changes.Any(change => change.Status == "completed" && change.SettledTick == tick &&
                change.Target.NonLand == TownArrangementRules.Mayor);
    }

    public static TownNonLandAuthority? CurrentNonLandAuthority(TownGovernmentState state, long tick)
    {
        if (state.Arrangement.NonLand != TownArrangementRules.Mayor ||
            state.Offices.SingleOrDefault(office => office.Mandates == "non_land") is not
                { HolderId: { } holder, ElectionId: { } authority, TermStartTick: { } start, TermEndTick: { } end } || end <= tick)
            return null;
        var effective = state.NonLandGrants.SingleOrDefault(grant => grant.Id == authority)?.EffectiveTick ?? start;
        return effective <= tick ? new(holder, authority, effective, start, end) : null;
    }

    /// <summary>Historical authority requires the exact elected term or accepted extension and scope then in force.</summary>
    public static bool NonLandAuthorityAt(TownGovernmentState state, string actorId, string authorityId, long tick)
    {
        if (!NonLandScopeAt(state, tick, includeEndingTick: true)) return false;
        var grant = state.NonLandGrants.SingleOrDefault(item => item.Id == authorityId);
        if (grant is not null && (grant.HolderId != actorId || tick < grant.EffectiveTick)) return false;
        return state.Offices.Any(office => office.Mandates == "non_land" && office.HolderId == actorId &&
                office.ElectionId == authorityId && office.TermStartTick <= tick && tick < office.TermEndTick) ||
            state.OfficeHistory.Any(term => term.Mandates == "non_land" && term.HolderId == actorId &&
                term.ElectionId == authorityId && term.StartTick <= tick && tick <= term.EndTick);
    }

    private static bool ExtensionBaseCurrent(TownGovernmentState state, TownNonLandExtension extension, long tick) =>
        state.Offices.Any(office => office.Mandates == extension.BaseMandate && office.HolderId == extension.HolderId &&
            office.ElectionId == extension.BaseElectionId && office.TermStartTick == extension.TermStartTick &&
            office.TermEndTick == extension.TermEndTick && office.TermEndTick > tick);

    private static bool ExtensionReady(TownGovernmentState state, TownGovernmentChange change, long tick) =>
        state.Arrangement.NonLand == TownArrangementRules.NoOffice &&
        change.NonLandExtension is { ConsentTick: not null } extension && ExtensionBaseCurrent(state, extension, tick);

    private static (TownGovernanceState, TownGovernmentState) ExtendNonLandOffice(TownGovernanceState council,
        TownGovernmentState state, TownGovernmentChange change, long tick)
    {
        var extension = change.NonLandExtension!;
        var grant = new TownNonLandMandateGrant(change.Id + ":non-land", change.Id, extension.HolderId,
            extension.BaseMandate, extension.BaseElectionId, extension.ConsentTick!.Value, tick,
            extension.TermStartTick, extension.TermEndTick);
        var office = new TownOffice("non_land", extension.HolderId, extension.TermStartTick, extension.TermEndTick,
            null, null, grant.Id);
        state = state with
        {
            NonLandGrants = state.NonLandGrants.Append(grant).ToArray(),
            Offices = state.Offices.Where(item => item.Mandates != "non_land").Append(office)
                .OrderBy(item => item.Mandates, StringComparer.Ordinal).ToArray()
        };
        return (Notice(council, "government", grant.Id,
            extension.HolderId + " took the approved non-land duties without restarting the existing elected term.", tick), state);
    }
}

namespace ClankerWorld.Simulation.Playtest;

/// <summary>Voluntary named commitments. Only completed native effects count as performance.</summary>
public static class TownRemedyRules
{
    public static string TermsHash(IReadOnlyList<TownRemedyTerm> terms, long completionTicks) =>
        TownHearingProcedure.Digest(new { Terms = terms, CompletionTicks = completionTicks });
    internal static bool ValidTerm(TownRemedyTerm? term) => term is not null && TownHearingProcedure.Id(term.Id) &&
        TownHearingProcedure.Id(term.ContributorId) && term.Quantity > 0 && (term.Kind switch
        {
            "return_goods" => TownHearingProcedure.Id(term.BeneficiaryId) && term.BeneficiaryId != term.ContributorId &&
                TownHearingProcedure.Id(term.ItemKind) && (term.TargetId is null || TownHearingProcedure.Id(term.TargetId)),
            "repair_equipment" => TownHearingProcedure.Id(term.TargetId) && term.Quantity == 1 && TownHearingProcedure.Id(term.ItemKind) &&
                (term.BeneficiaryId is null || TownHearingProcedure.Id(term.BeneficiaryId)),
            "public_service_goods" => TownHearingProcedure.Id(term.BeneficiaryId) && TownHearingProcedure.Id(term.ItemKind) && TownHearingProcedure.Id(term.TargetId),
            _ => false,
        });
    internal static bool ValidTerms(IReadOnlyList<TownRemedyTerm>? terms) => terms is { Count: > 0 and <= 8 } &&
        terms.All(ValidTerm) && terms.Select(t => t.Id).Distinct(StringComparer.Ordinal).Count() == terms.Count;
    private static TownNonviolentState Replace(TownNonviolentState state, TownRemedyOffer offer) =>
        state with { Offers = state.Offers.Select(o => o.Id == offer.Id ? offer : o).ToArray() };

    public static TownNonviolentState Offer(TownNonviolentState state, string caseId, string findingId, string author,
        IReadOnlyList<TownRemedyTerm> terms, long completionTicks, string reason, long tick, int day, string noticeId,
        bool feasible, string? replacesOfferId = null, string? replacesAgreementId = null)
    {
        var item = state.Cases.SingleOrDefault(c => c.Id == caseId && c.Status == "settled");
        var finding = item is { Findings.Count: > 0 } ? item.Findings[^1] : null;
        if (replacesAgreementId is not null)
        {
            var previous = state.Agreements.SingleOrDefault(a => a.Id == replacesAgreementId && a.Status != "completed" && !IsSuperseded(state, a.Id));
            if (previous is null || replacesOfferId is not null && replacesOfferId != previous.OfferId)
                throw new InvalidOperationException("Renegotiation must identify the existing unfinished agreement.");
            replacesOfferId = previous.OfferId;
        }
        var prior = replacesOfferId is null ? null : state.Offers.SingleOrDefault(o => o.Id == replacesOfferId && o.CaseId == caseId);
        if (finding is null || finding.Id != findingId || finding.Result != "supported" || !feasible || !ValidTerms(terms) ||
            !TownHearingProcedure.Text(reason) || !TownHearingProcedure.Id(noticeId) || completionTicks <= 0 || day <= 0 || tick < finding.Tick ||
            (prior is null ? finding.Judge.AgentId != author && !item!.Revisions[^1].Parties.Any(p => p.SubjectId == author || p.RespondingAdultId == author) || replacesOfferId is not null :
                prior.Status is not ("countered" or "accepted") || !prior.Terms.Any(t => t.ContributorId == author)) ||
            !TownNonviolentRules.ReadCurrent(item!, author, tick) ||
            state.Offers.Any(o => o.CaseId == caseId && o.FindingId == findingId && o.Status == "pending"))
            throw new InvalidOperationException("A voluntary offer needs a supported finding, feasible named contributions and current informed terms.");
        var duration = completionTicks;
        var id = TownNonviolentRules.NextId(state, "offer");
        var offer = new TownRemedyOffer(id, caseId, findingId, prior is null ? 1 : prior.Revision + 1, terms.ToArray(),
            TermsHash(terms, duration), reason, noticeId, tick, checked(tick + day), duration, "pending", [], replacesOfferId);
        return state with { Sequence = checked(state.Sequence + 1), Offers = state.Offers.Append(offer).ToArray() };
    }

    public static TownNonviolentState Respond(TownNonviolentState state, string offerId, int revision, string actor,
        string kind, string? reason, long tick, int day, IReadOnlyList<TownCivicReceipt> receipts, bool feasible)
    {
        var offer = state.Offers.SingleOrDefault(o => o.Id == offerId && o.Revision == revision && o.Status == "pending");
        if (offer is null || tick < offer.PublishedTick || tick >= offer.ResponseDeadlineTick || day <= 0 ||
            kind is not ("accept" or "decline" or "counter") || !offer.Terms.Any(t => t.ContributorId == actor) ||
            offer.Responses.Any(r => r.AgentId == actor) || reason is not null && !TownHearingProcedure.Text(reason) ||
            !TownHearingProcedure.HasNotice(offer.NoticeId, offer.PublishedTick, actor, tick, receipts) || kind == "accept" && !feasible)
            throw new InvalidOperationException("Respond personally to the current offer after learning it; consent cannot promise impossible contributions.");
        offer = offer with { Responses = offer.Responses.Append(new(actor, revision, offer.TermsHash, kind, tick, offer.NoticeId, reason)).ToArray() };
        if (kind != "accept") return Replace(state, offer with { Status = kind == "decline" ? "declined" : "countered" });
        var contributors = offer.Terms.Select(t => t.ContributorId).Distinct(StringComparer.Ordinal);
        if (!contributors.All(id => offer.Responses.Any(r => r.AgentId == id && r.Kind == "accept"))) return Replace(state, offer);
        var previousAgreement = offer.ReplacesOfferId is null ? null : state.Offers.Single(o => o.Id == offer.ReplacesOfferId).AgreementId;
        // Renegotiation preserves already performed work. New terms are remaining commitments,
        // never a mechanism to copy old physical receipts into another agreement.
        var agreement = new TownRestorativeAgreement(TownNonviolentRules.NextId(state, "agreement"), offer.Id, revision,
            offer.TermsHash, offer.Terms, offer.Responses, tick, checked(tick + offer.CompletionTicks), "pending", previousAgreement);
        offer = offer with { Status = "accepted", AgreementId = agreement.Id };
        state = Replace(state with { Sequence = checked(state.Sequence + 1), Agreements = state.Agreements.Append(agreement).ToArray() }, offer);
        if (previousAgreement is not null && offer.ReplacesOfferId is { } oldId)
        {
            var old = state.Offers.Single(o => o.Id == oldId);
            state = Replace(state, old with { Status = "superseded" });
        }
        return state;
    }

    public static int CompletedQuantity(TownNonviolentState state, string agreementId, string termId) =>
        checked(state.Effects.Where(e => e.AgreementId == agreementId && e.TermId == termId).Sum(e => e.Quantity));
    public static bool IsSuperseded(TownNonviolentState state, string agreementId) =>
        state.Agreements.Any(a => a.ReplacesAgreementId == agreementId);
    public static TownNonviolentState RecordEffect(TownNonviolentState state, TownRemedyEffect effect)
    {
        var agreement = state.Agreements.SingleOrDefault(a => a.Id == effect.AgreementId);
        var term = agreement?.Terms.SingleOrDefault(t => t.Id == effect.TermId);
        if (agreement is null || term is null || IsSuperseded(state, agreement.Id) || agreement.Status == "completed" ||
            !TownHearingProcedure.Id(effect.Id) || !TownHearingProcedure.Id(effect.NativeReceiptId) ||
            !TownHearingProcedure.Id(effect.NativeReceiptVersion) || effect.Tick < agreement.AcceptedTick || effect.Quantity <= 0 ||
            effect.ActorId != term.ContributorId || effect.Kind != term.Kind || effect.BeneficiaryId != term.BeneficiaryId ||
            effect.ItemKind != term.ItemKind || effect.TargetId != term.TargetId ||
            state.Effects.Any(e => e.Id == effect.Id || e.NativeReceiptId == effect.NativeReceiptId) ||
            effect.Quantity > term.Quantity - CompletedQuantity(state, agreement.Id, term.Id))
            throw new InvalidOperationException("Only this contributor's actual new agreed physical effect can count once toward performance.");
        state = state with { Effects = state.Effects.Append(effect).ToArray() };
        return Advance(state, effect.Tick);
    }

    public static TownNonviolentState Advance(TownNonviolentState state, long tick) => state with
    {
        Offers = state.Offers.Select(o => o.Status == "pending" && tick >= o.ResponseDeadlineTick ? o with { Status = "unanswered" } : o).ToArray(),
        Agreements = state.Agreements.Select(a => a with { Status = a.Terms.All(t => CompletedQuantity(state, a.Id, t.Id) == t.Quantity)
            ? "completed" : tick > a.DeadlineTick ? "overdue" : "pending" }).ToArray()
    };
}

using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private async Task RunNonviolentTownUiChecks(OwnerWorldSnapshot sample, OwnerWorldTown town)
    {
        var wasVisible = worldInfoPanel.Visible;
        var showedTowns = WorldInfoShowsTowns;
        var judge = new OwnerCaseJudge("judge-ui", "Nia Moss", "case_elected", "case-election-ui", 3_700);
        var parties = new OwnerCaseParty[]
        {
            new("subject-ui", "subject", "subject-ui", "Mira Vale", "subject-ui", "Mira Vale", "Vale household", false),
            new("child-ui", "affected", "child-ui", "Ash Vale", "caregiver-ui", "Sol Reed", "Reed household", true),
        };
        var evidence = new OwnerCaseEvidence("evidence-ui", 1, "observation", "firsthand", "witness-ui", "Sol Reed",
            "act-ui", "version-ui", 3_500, "witness-ui", "Sol Reed", 3_610, "Saw the grove harvest.");
        var finding = new OwnerViolationFinding("finding-ui", 1, judge, 3_800, "supported", "more_likely_than_not",
            [evidence.Id], "The firsthand account supports this finding.", "The amount taken is uncertain.", "warning", parties);
        var term = new OwnerRemedyTerm("term-ui", "return_goods", "subject-ui", "Mira Vale", "witness-ui", "Sol Reed", "wood", 2, null, null);
        var consent = new OwnerRemedyResponse("subject-ui", "Mira Vale", 1, "accept", 3_830, null);
        var offer = new OwnerRemedyOffer("offer-ui", finding.Id, 1, [term], "Return the borrowed wood.", "offer-notice-ui",
            3_810, 4_170, 1_080, "pending", [], [], null, null);
        var agreement = new OwnerRestorativeAgreement("agreement-ui", offer.Id, 1, [term], [consent], 3_830, 4_910,
            "pending", null, []);
        var item = new OwnerTownNonviolentCase("case-ui", "settled", 3_600, 3_800, "subject-ui", "Mira Vale",
            "harvest", new(1, 1), 3_500, "A report about the protected grove.",
            new("law-ui", "Grove harvest", "Leave young trees standing.", "site", 1, 1, 3_000, null) { Site = [new(1, 1)] }, 1,
            [new(1, "case-notice-ui", 3_600, 3_960, parties)],
            [new("witness-ui", "Sol Reed", "witness", 3_600, "I saw the harvest.", [evidence.Id])],
            [evidence], [new(1, "judge-ui", "Nia Moss", 3_750, [evidence.Id], [], null)],
            [new(1, "child-ui", "caregiver-ui", "Sol Reed", "child-ui", "Ash Vale", "answer", "I am supporting Ash's response.", 3_720)],
            [finding], null, [], null, null, [], [offer], []);
        try
        {
            var projectedTown = town with
            {
                Government = town.Government! with
                {
                    NonLandAuthorized = true,
                    NonLandAuthority = new("mayor-ui", "Tess Rowan", "grant-ui", 3_650, 2_000, 9_200),
                },
                NonviolentCaseCount = 1,
                NonviolentCases = [item],
            };
            ShowWorldInfoPage(towns: true);
            worldInfoPanel.Show();
            Render(sample with { Towns = [projectedTown], WorldTick = 3_900 }, []);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            string VisibleText() => string.Join("\n", townList.FindChildren("*", nameof(Label), recursive: true, owned: false)
                .OfType<Label>().Where(label => label.IsVisibleInTree()).Select(label => label.Text));
            var published = VisibleText();
            foreach (var phrase in new[] { "Non-land hearing", "Allegation:", "Law at the reported time:", "version 1",
                         "caregiver support: Sol Reed", "No receipt of this notice", "firsthand observation", "Finding 1",
                         "more likely than not", "The amount taken is uncertain.", "awaiting personal responses",
                         "has no recorded receipt of these offer terms", "Non-land authority: Tess Rowan" })
                if (!published.Contains(phrase, StringComparison.Ordinal))
                    throw new InvalidOperationException("Visible Town hearing controls lost a public record or confused its status: " + phrase);
            if (published.Contains("Voluntary agreement", StringComparison.Ordinal) || published.Contains("Actual delivery", StringComparison.Ordinal) ||
                published.Contains("case-ui", StringComparison.Ordinal) || published.Contains("grant-ui", StringComparison.Ordinal))
                throw new InvalidOperationException("Publishing a remedy offer must not display agreement, completed work or internal identifiers.");

            var eventSnapshot = sample with { Towns = [projectedTown] };
            foreach (var kind in new[] { "town_civic_nonviolent_hearing", "town_civic_law_case", "town_civic_remedy", "law_case_offer_response", "law_case_remedy_effect" })
            {
                var description = WorldEventText.Describe(new(1, 3_900, kind, projectedTown.Id + "|record-ui|actor-ui"), eventSnapshot);
                if (!GameUiText.IsPlayerFacingEvent(kind) || description.Contains("record-ui", StringComparison.Ordinal) ||
                    description.Contains("actor-ui", StringComparison.Ordinal) ||
                    description.Contains("agreement completed", StringComparison.OrdinalIgnoreCase) ||
                    description.Contains("remedy completed", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Hearing notices and individual contributions must produce readable events without claiming a whole remedy completed.");
            }

            var pending = item with
            {
                Status = "pending",
                SettledTick = null,
                Findings = [],
                Offers = [],
                CurrentParties = [parties[0], parties[1] with { RespondingAdultId = "new-caregiver-ui", RespondingAdultName = "Tess Rowan", NoticeAware = false }],
            };
            Render(sample with { Towns = [projectedTown with { NonviolentCases = [pending] }], WorldTick = 3_900 }, []);
            var supported = VisibleText();
            if (!supported.Contains("At this notice: Ash Vale · affected · caregiver support: Sol Reed", StringComparison.Ordinal) ||
                !supported.Contains("Current required responders:", StringComparison.Ordinal) ||
                !supported.Contains("Ash Vale · caregiver support: Tess Rowan · has not learned this notice", StringComparison.Ordinal))
                throw new InvalidOperationException("Current caregiver response authority must not overwrite the historical notice or inherit another caregiver's receipt.");

            // Same Town, tick and membership: only personal consent changes. The real list cache must refresh.
            projectedTown = projectedTown with
            {
                NonviolentCases = [item with
            {
                Offers = [offer with { Status = "accepted", Responses = [consent], NoticeAwareContributorIds = [term.ContributorId], AgreementId = agreement.Id }],
                Agreements = [agreement],
            }]
            };
            Render(sample with { Towns = [projectedTown], WorldTick = 3_900 }, []);
            if (!VisibleText().Contains("accepted; work still pending", StringComparison.Ordinal) ||
                !VisibleText().Contains("Recorded completion 0/2", StringComparison.Ordinal) ||
                VisibleText().Contains("Actual delivery", StringComparison.Ordinal))
                throw new InvalidOperationException("Personal remedy acceptance must refresh the Town panel without inventing physical completion.");
            var effect = new OwnerRemedyEffect("effect-ui", term.Id, term.ContributorId, term.ContributorName, 5_000,
                term.Kind, term.BeneficiaryName, term.ItemKind, 2, null, "native-receipt-ui");
            foreach (var completed in new[] { false, true })
            {
                var changed = agreement with { Status = completed ? "completed" : "overdue", Effects = completed ? [effect] : [] };
                var changedTown = projectedTown with { NonviolentCases = [projectedTown.NonviolentCases[0] with { Agreements = [changed] }] };
                Render(sample with { Towns = [changedTown], WorldTick = 5_000 }, []);
                var visible = VisibleText();
                if (!visible.Contains(completed ? "completed through recorded work" : "overdue; incomplete work remains voluntary", StringComparison.Ordinal) ||
                    !visible.Contains(completed ? "Recorded completion 2/2" : "Recorded completion 0/2", StringComparison.Ordinal) ||
                    completed != visible.Contains("Actual delivery by Mira Vale", StringComparison.Ordinal) ||
                    !visible.Contains("missed deadlines do not create a new violation", StringComparison.Ordinal))
                    throw new InvalidOperationException("The Town panel must distinguish overdue consent from actual completed delivery without automatic punishment.");
            }
            var replacedTown = projectedTown with
            {
                NonviolentCases = [projectedTown.NonviolentCases[0] with
            {
                Agreements = [agreement with { Superseded = true }],
            }]
            };
            Render(sample with { Towns = [replacedTown], WorldTick = 5_000 }, []);
            if (!VisibleText().Contains("superseded by a later agreement; retained history", StringComparison.Ordinal) ||
                VisibleText().Contains("Voluntary agreement · accepted; work still pending", StringComparison.Ordinal))
                throw new InvalidOperationException("A replaced agreement must remain inspectable without presenting its old obligations as current work.");
            foreach (var (status, expected) in new[] { ("declined", "declined"), ("unanswered", "unanswered when the response period ended"),
                         ("countered", "counteroffer proposed"), ("superseded", "replaced by later terms") })
            {
                var changedTown = projectedTown with { NonviolentCases = [item with { Offers = [offer with { Status = status }], Agreements = [] }] };
                Render(sample with { Towns = [changedTown], WorldTick = 5_000 }, []);
                if (!VisibleText().Contains("Voluntary remedy offer · " + expected, StringComparison.Ordinal) ||
                    VisibleText().Contains("Voluntary agreement", StringComparison.Ordinal))
                    throw new InvalidOperationException("A nonaccepted remedy response must remain distinct from an agreement: " + status);
            }
        }
        finally
        {
            Render(sample with { Towns = [town] }, []);
            ShowWorldInfoPage(showedTowns);
            worldInfoPanel.Visible = wasVisible;
        }
    }
}

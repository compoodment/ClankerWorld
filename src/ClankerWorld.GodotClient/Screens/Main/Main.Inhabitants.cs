using ClankerWorld.GodotClient.ClientState;
using ClankerWorld.GodotClient.Pairing;
using ClankerWorld.GodotClient.UI;
using Godot;
using System.Globalization;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private void RenderInhabitantList(OwnerWorldSnapshot snapshot)
    {
        var previousSelection = selectedInhabitantId;
        var selectionFound = false;
        inhabitantList.Clear();
        // The living come first, each with what they are doing; the deceased
        // follow under their own heading so history stays inspectable.
        var inhabitants = snapshot.Inhabitants
            .Where(inhabitant => !inhabitant.IsDraft)
            .OrderBy(inhabitant => IsLiving(inhabitant) ? 0 : 1)
            .ThenBy(inhabitant => inhabitant.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var living = inhabitants.Count(IsLiving);
        var deceased = inhabitants.Length - living;
        rosterSummaryLabel.Text = inhabitants.Length == 0
            ? "No one lives here yet."
            : deceased == 0 ? $"{living} living" : $"{living} living · {deceased} deceased";

        foreach (var inhabitant in inhabitants)
        {
            if (!IsLiving(inhabitant) && living > 0 && inhabitantList.ItemCount == living)
            {
                var header = inhabitantList.AddItem("Deceased", selectable: false);
                inhabitantList.SetItemCustomFgColor(header, UiTheme.Current.InkMuted);
            }
            var rowText = RosterRow(inhabitant);
            var row = inhabitantList.AddItem(rowText);
            inhabitantList.SetItemMetadata(row, inhabitant.Id);
            // The full row, in case a long activity is clipped at this width.
            inhabitantList.SetItemTooltip(row, rowText + "\n" + (IsLiving(inhabitant)
                ? "Select to find this agent on the map and open their card."
                : "Select to open this historical profile."));
            if (!IsLiving(inhabitant)) inhabitantList.SetItemCustomFgColor(row, UiTheme.Current.InkFaint);
            if (string.Equals(inhabitant.Id, previousSelection, StringComparison.Ordinal))
            {
                selectionFound = true;
                inhabitantList.Select(row);
            }
        }
        // Fit the list to its rows instead of reserving a tall empty box.
        inhabitantList.Visible = inhabitantList.ItemCount > 0;
        var rowHeight = inhabitantList.GetThemeFont("font").GetHeight(inhabitantList.GetThemeFontSize("font_size")) +
            inhabitantList.GetThemeConstant("v_separation") + 4;
        var uiScale = DisplayUiScalePolicy.ScaleFactor(displayPreferences.UiScalePercent);
        inhabitantList.CustomMinimumSize = new Vector2(inhabitantList.CustomMinimumSize.X,
            Math.Clamp(inhabitantList.ItemCount * rowHeight + 12, 40, 360 * uiScale));
        rosterPanel.Size = rosterPanel.GetCombinedMinimumSize();

        if (!selectionFound)
        {
            selectedInhabitantId = null;
            inhabitantList.DeselectAll();
        }
    }

    private static bool IsLiving(OwnerWorldInhabitant inhabitant) =>
        string.Equals(inhabitant.Lifecycle, "active", StringComparison.OrdinalIgnoreCase);

    private static string RosterRow(OwnerWorldInhabitant inhabitant)
    {
        if (!IsLiving(inhabitant)) return $"{inhabitant.DisplayName}  ·  died";
        var activity = inhabitant.DecisionFactors.Any(factor => factor.Key == "decision-pending")
            ? "deciding what to do"
            : GameUiText.ActivityPhrase(inhabitant.PublicIntention?.CandidateId, inhabitant.PublicIntention?.Summary);
        var fullness = GameUiText.FullnessState(inhabitant.HungerBasisPoints);
        return $"{inhabitant.DisplayName}  ·  {activity}" +
            (fullness is "hungry" or "very hungry" ? $"  ·  {fullness}" : string.Empty);
    }

    /// <summary>Moves the camera to a living agent; historical profiles have no map position to show.</summary>
    private void CenterOnInhabitant(string inhabitantId)
    {
        if (renderedMapSnapshot?.Inhabitants.FirstOrDefault(person => person.Id == inhabitantId) is not { } person ||
            person.IsDraft || !IsLiving(person))
            return;
        CenterCameraAt(new Vector2(person.Position.X + 0.5f, person.Position.Y + 0.5f));
    }

    private void RenderInhabitantDetails(OwnerWorldSnapshot snapshot)
    {
        var inhabitant = snapshot.Inhabitants.FirstOrDefault(item =>
            string.Equals(item.Id, selectedInhabitantId, StringComparison.Ordinal));
        if (inhabitant is null)
        {
            SetPanelText(inhabitantDetails, string.Empty);
            return;
        }

        if (string.Equals(inhabitant.Lifecycle, "dead", StringComparison.OrdinalIgnoreCase))
        {
            SetPanelText(inhabitantDetails, "Deceased · historical record; no current activity or carried inventory.");
            return;
        }

        var inventory = inhabitant.Inventory.Count == 0
            ? "nothing"
            : string.Join(", ", inhabitant.Inventory.Select(item => $"{Pretty(item.Kind)}: {item.Quantity}"));
        var currentActivity = string.IsNullOrWhiteSpace(inhabitant.Route.Status)
            ? "wandering"
            : Pretty(inhabitant.Route.Status);
        var destination = inhabitant.Route.Destination is { } routeDestination
            ? $" toward {routeDestination.X}, {routeDestination.Y}"
            : string.Empty;
        SetPanelText(inhabitantDetails,
            $"{currentActivity}{destination}\n" +
            $"Fullness {NeedPercent(inhabitant.HungerBasisPoints)}%\n" +
            $"Carrying {inventory}");
    }

    private void RenderSelectedInhabitantCard(OwnerWorldSnapshot snapshot)
    {
        var inhabitant = snapshot.Inhabitants.FirstOrDefault(item =>
            string.Equals(item.Id, selectedInhabitantId, StringComparison.Ordinal));
        if (inhabitant is null)
        {
            CloseAgentModelEditor();
            selectedActorNameLabel.Text = string.Empty;
            renamingAgentId = null;
            selectedActorSummaryLabel.Text = string.Empty;
            selectedActorConditionLabel.Text = string.Empty;
            SetPanelText(inhabitantSocialDetails, string.Empty);
            SetPanelText(privateThoughtHistory, string.Empty);
            SetPanelText(memoryHistory, string.Empty);
            memoriesPanel.Hide();
            selectedInhabitantCard.Hide();
            return;
        }

        selectedActorNameLabel.Text = inhabitant.DisplayName;
        if (renamingAgentId != inhabitant.Id || !renameAgentInput.HasFocus())
        {
            renameAgentInput.Text = inhabitant.DisplayName;
            renamingAgentId = inhabitant.Id;
        }
        var ageBand = inhabitant.DecisionFactors.FirstOrDefault(factor => factor.Key == "age-band")?.Detail;
        var ageYears = inhabitant.DecisionFactors.FirstOrDefault(factor => factor.Key == "age-years")?.Detail;
        var ageDays = inhabitant.DecisionFactors.FirstOrDefault(factor => factor.Key == "age-days")?.Detail;
        var deathTick = inhabitant.DecisionFactors.FirstOrDefault(factor => factor.Key == "death-tick")?.Detail;
        var deathCause = inhabitant.DecisionFactors.FirstOrDefault(factor => factor.Key == "death-cause")?.Detail;
        var willStatus = inhabitant.DecisionFactors.FirstOrDefault(factor => factor.Key == "will-status")?.Detail;
        var willHeir = inhabitant.DecisionFactors.FirstOrDefault(factor => factor.Key == "will-heir")?.Detail;
        var isDeceased = string.Equals(inhabitant.Lifecycle, "dead", StringComparison.OrdinalIgnoreCase);
        modelSettingsButton.Disabled = isDeceased || registration is null;
        findAgentButton.Visible = !isDeceased;
        if (selectedAgentModelScroll.Visible && SelectedCognitionTarget() != inhabitant.Id)
            CloseAgentModelEditor();
        var waitingForDecision = inhabitant.DecisionFactors.Any(factor => factor.Key == "decision-pending");
        selectedActorSummaryLabel.Text = Pretty(inhabitant.Lifecycle) + (ageBand is null ? "" : " · " + Pretty(ageBand)) +
            (ageYears is null ? "" : " · " + ageYears + " years") +
            (ageDays is null ? "" : " · " + ageDays + " days") +
            (deathTick is not null && long.TryParse(deathTick, CultureInfo.InvariantCulture, out var finalTick)
                ? $" · {DisplayWorldClock(finalTick)}" : "");
        var intention = isDeceased
            ? $"Life ended{(deathCause is null ? "" : " · " + Pretty(deathCause))}. No current thoughts or activity." +
              (willStatus == "accepted" ? $" Final will: personal estate to {willHeir}." :
                  willStatus == "pending" ? " Final will pending." :
                  willStatus == "default" ? " Personal estate follows household inheritance." : "")
            : waitingForDecision
            ? "Decision pending."
            : inhabitant.PublicIntention is { } publicIntention
            // The summary is a gerund phrase ("keeping a safe routine"); the
            // candidate reads as a verb phrase that fits "Wants to".
            ? $"Wants to {GameUiText.HumanizeIdentifier(publicIntention.CandidateId).ToLowerInvariant()}."
            : "Taking in their surroundings.";
        var relationships = inhabitant.Relationships.Count == 0
            ? "No close relationships yet."
            : string.Join("; ", inhabitant.Relationships.Select(relationship => GameUiText.RelationshipSummary(
                relationship.Type, relationship.State, GameUiText.PartyName(snapshot, relationship.OtherPartyId),
                relationship.Direction)));
        var decision = snapshot.Cognition?.Decisions?.FirstOrDefault(item => item.InhabitantId == inhabitant.Id);
        var activity = waitingForDecision ? "Decision pending" : decision is null
            ? "No decision yet"
            : decision.FellBack
            ? $"Model did not provide a usable choice · built-in rules chose to {GameUiText.HumanizeIdentifier(decision.CandidateId).ToLowerInvariant()}"
            : $"{ProviderDisplayName(decision.Provider)} chose to {GameUiText.HumanizeIdentifier(decision.CandidateId).ToLowerInvariant()}";
        var projectText = inhabitant.Project is { } project
            ? $"{project.Label} · {Pretty(project.Stage)} · {project.WorkDone}/{project.WorkRequired}" +
                (project.Blocker is null ? "" : $"\n{project.Blocker}")
            : "No active building project";
        var socialNotes = inhabitant.SocialNotes.Count == 0 ? "" : "\n" + string.Join("\n", inhabitant.SocialNotes);
        var standing = inhabitant.SocialStanding.Count == 0 ? "" : "\n" + string.Join(" · ",
            inhabitant.SocialStanding.Select(item => $"Trust in {item.SubjectName} {item.Trust}/10"));
        var condition = inhabitant.Survival is { } survival
            ? $"{(isDeceased ? "At death · " : "")}Warmth {survival.WarmthBasisPoints / 100}% · Illness {survival.IllnessBasisPoints / 100}%" +
                $" · Diet {survival.NutritionBasisPoints / 100}%\n" +
                $"{(survival.HasClothing ? "Clothed" : "No warm clothing")} · {(survival.HasTool ? "Tool equipped" : "Working by hand")}" :
                string.Empty;
        // Omit the condition line until the host reports it, rather than
        // filling the card with an "unavailable" placeholder.
        selectedActorConditionLabel.Text = condition;
        selectedActorConditionLabel.Visible = condition.Length > 0;
        var role = inhabitant.DecisionFactors.FirstOrDefault(factor => factor.Key == "role")?.Detail;
        var learning = inhabitant.Lesson is { } lesson
            ? $"\nLearning {Pretty(lesson.Role)} with {lesson.TeacherName} · {Pretty(lesson.Stage)} · {lesson.Progress}/{lesson.Required}" : "";
        if (inhabitant.Proficiency is { } practice)
            learning += $"\nPractice · Building {practice.Building}/30 · Farming {practice.Farming}/30 · Crafting {practice.Crafting}/30";
        var socialText = $"{(role is null or "unassigned" ? "" : $"Role: {Pretty(role)}\n")}{(inhabitant.Project is null ? intention : projectText)}{learning}\n{relationships}{standing}{socialNotes}\n{activity}";
        SetPanelText(inhabitantSocialDetails, socialText);
        var thoughtHeading = isDeceased ? "Private thoughts · historical" : "Private thoughts";
        SetPanelText(privateThoughtHistory, inhabitant.RecentPrivateThoughts.Count == 0
            ? thoughtHeading + "\nNone recorded yet."
            : thoughtHeading + "\n" + string.Join("\n", inhabitant.RecentPrivateThoughts
                .Reverse().Select(thought => $"{DisplayWorldClock(thought.WorldTick)}  {thought.Text}")));
        var memoryRows = new List<(long WorldTick, int Kind, string Text)>();
        memoryRows.AddRange(inhabitant.RecentBeliefs.Select(belief =>
        {
            var evidence = belief.Provenance switch
            {
                "firsthand" => "witnessed",
                "hearsay" when belief.SourceAgentName is { } source => $"heard from {source}",
                "hearsay" => "heard from someone",
                _ => "inferred",
            };
            var subject = belief.AboutInhabitantId is { } subjectId
                ? snapshot.Inhabitants.FirstOrDefault(person => person.Id == subjectId)?.DisplayName
                : null;
            var context = $"Belief · {evidence} · {belief.ConfidenceBasisPoints / 100}% sure" +
                (subject is null ? "" : $" · about {subject}") +
                (belief.IsCorrected
                    ? $" · corrected{(belief.CorrectedTick is { } correctedTick ? $" at {DisplayWorldClock(correctedTick)}" : "")}" : "");
            return (belief.WorldTick, 0,
                $"{DisplayWorldClock(belief.WorldTick)} · {context}\n{belief.Statement}");
        }));
        memoryRows.AddRange(inhabitant.RecentMemories.Select(memory =>
            (memory.WorldTick, 1,
                $"{DisplayWorldClock(memory.WorldTick)} · {Pretty(memory.Visibility)} · about {memory.SubjectName}\n{memory.Summary}")));
        memoryRows.AddRange(inhabitant.RecentKnowledgeFacts.Select(fact =>
        {
            var acquisition = fact.Acquisition == "firsthand"
                ? $"discovered by {fact.DiscovererName}"
                : $"{Pretty(fact.Acquisition)} from {fact.SourceAgentName ?? "another agent"}; discovered by {fact.DiscovererName}";
            var resources = fact.ResourceKinds.Count == 0 ? "no recorded resource site" :
                "resources · " + string.Join(", ", fact.ResourceKinds.Select(Pretty));
            return (fact.WorldTick, 2,
                $"{DisplayWorldClock(fact.WorldTick)} · Map fact · {acquisition}\n" +
                $"{Pretty(fact.Terrain)} at ({fact.X}, {fact.Y}) · {resources}");
        }));
        memoryRows.AddRange(inhabitant.KnowledgeArtifacts.Select(artifact =>
        {
            var sites = string.Join("\n", artifact.Sites.Select(site =>
                $"  {Pretty(site.Terrain)} at ({site.X}, {site.Y})" +
                (site.ResourceKinds.Count == 0 ? "" : " · " + string.Join(", ", site.ResourceKinds.Select(Pretty)))));
            return (artifact.CreatedTick, 3,
                $"{DisplayWorldClock(artifact.CreatedTick)} · {Pretty(artifact.Kind)} · {artifact.Title} · by {artifact.CreatorName}\n{sites}");
        }));
        SetPanelText(memoryHistory, memoryRows.Count == 0
            ? "No saved memories, beliefs, or map records for this agent yet."
            : string.Join("\n\n", memoryRows.OrderByDescending(item => item.WorldTick)
                .ThenBy(item => item.Kind).Select(item => item.Text)));
        inhabitantSocialDetails.TooltipText = decision is null ? "" :
            $"Last accepted decision\nRole: {decision.Role ?? "not reported"}\nModel: {decision.Model ?? "not reported"}\nConfidence: {decision.Confidence:P0}\n" +
            $"Latency: {decision.LatencyMilliseconds?.ToString(CultureInfo.CurrentCulture) ?? "—"} ms\n" +
            $"Tokens in/out: {decision.InputTokens?.ToString(CultureInfo.CurrentCulture) ?? "—"}/{decision.OutputTokens?.ToString(CultureInfo.CurrentCulture) ?? "—"}";
        if (inhabitant.Proficiency is not null)
            inhabitantSocialDetails.TooltipText += "\nPractice: each completed project earns one point in its domain, up to 30. Every 10 points adds one work per preparation step. Materials, permissions and crop growth time are unchanged.";
        selectedInhabitantCard.Show();
        PositionSelectedInhabitantCard(snapshot);
    }

}

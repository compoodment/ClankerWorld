using ClankerWorld.GodotClient.ClientState;
using ClankerWorld.GodotClient.Pairing;
using ClankerWorld.GodotClient.UI;
using Godot;
using System.Globalization;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private readonly record struct RosterPresentation(string Id, string Name, bool Living,
        string Activity, string Fullness, bool Cold, bool Ill, int PortraitStage);
    private RosterPresentation[]? renderedRosterPresentation;
    private string? renderedRosterWorldId;
    private string? renderedRosterTheme;

    private static RosterPresentation RosterPresentationFor(OwnerWorldInhabitant person)
    {
        var living = IsLiving(person);
        var activity = !living ? string.Empty : person.DecisionFactors.Any(factor => factor.Key == "decision-pending")
            ? "deciding what to do"
            : GameUiText.ActivityPhrase(person.PublicIntention?.CandidateId, person.PublicIntention?.Summary);
        var fullness = living ? GameUiText.FullnessState(person.HungerBasisPoints) : string.Empty;
        return new(person.Id, person.DisplayName, living, activity,
            fullness is "hungry" or "very hungry" ? fullness : string.Empty,
            living && person.Survival is { WarmthBasisPoints: < 4_000 },
            living && person.Survival is { IllnessBasisPoints: >= 1_500 },
            AgentSprites.StageIndex(person.DecisionFactors.FirstOrDefault(factor => factor.Key == "age-band")?.Detail));
    }

    private void RenderInhabitantList(OwnerWorldSnapshot snapshot)
    {
        var previousSelection = selectedInhabitantId;
        var selectionFound = false;
        // The living come first, each with what they are doing; the deceased
        // follow under their own heading so history stays inspectable.
        var inhabitants = snapshot.Inhabitants
            .Where(inhabitant => !inhabitant.IsDraft)
            .OrderBy(inhabitant => IsLiving(inhabitant) ? 0 : 1)
            .ThenBy(inhabitant => inhabitant.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var presentation = inhabitants.Select(RosterPresentationFor).ToArray();
        if (renderedRosterWorldId == snapshot.WorldId && renderedRosterTheme == UiTheme.Current.Name &&
            renderedRosterPresentation is { } previous && presentation.SequenceEqual(previous))
        {
            RefreshRosterSelection();
            return;
        }
        inhabitantList.Clear();
        var living = inhabitants.Count(IsLiving);
        var deceased = inhabitants.Length - living;
        var hungry = inhabitants.Count(person => IsLiving(person) &&
            GameUiText.FullnessState(person.HungerBasisPoints) is "hungry" or "very hungry");
        rosterSummaryLabel.Text = inhabitants.Length == 0
            ? "No one lives here yet."
            : (deceased == 0 ? $"{living} living" : $"{living} living · {deceased} deceased") +
              (hungry == 0 ? string.Empty : $" · {hungry} hungry");

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
        var rowHeight = inhabitantList.GetThemeFont("font").GetHeight(inhabitantList.GetThemeFontSize("font_size")) +
            inhabitantList.GetThemeConstant("v_separation") + 4;
        inhabitantList.CustomMinimumSize = new Vector2(inhabitantList.CustomMinimumSize.X,
            Math.Clamp(inhabitantList.ItemCount * rowHeight + 12, 40, 360));
        rosterPanel.Size = rosterPanel.GetCombinedMinimumSize();

        if (!selectionFound)
        {
            selectedInhabitantId = null;
            inhabitantList.DeselectAll();
        }
        RenderRosterCards(inhabitants);
        rosterPanel.Size = rosterPanel.GetCombinedMinimumSize();
        renderedRosterPresentation = presentation;
        renderedRosterWorldId = snapshot.WorldId;
        renderedRosterTheme = UiTheme.Current.Name;
    }

    private void RefreshRosterSelection()
    {
        var card = selectedInhabitantId is { } id ? rosterCardIds.IndexOf(id) : -1;
        if (card < 0) selectedInhabitantId = null;
        var selectedCards = rosterCards.GetSelectedItems();
        var cardMatches = card < 0 ? selectedCards.Length == 0 : selectedCards.Length == 1 && selectedCards[0] == card;
        var selectedRows = inhabitantList.GetSelectedItems();
        var selectedRowId = selectedRows.Length == 1 ? inhabitantList.GetItemMetadata(selectedRows[0]).AsString() : null;
        if (cardMatches && selectedRowId == selectedInhabitantId) return;
        inhabitantList.DeselectAll();
        if (card >= 0)
            for (var row = 0; row < inhabitantList.ItemCount; row++)
                if (inhabitantList.GetItemMetadata(row).AsString() == selectedInhabitantId)
                {
                    inhabitantList.Select(row);
                    break;
                }
        if (!cardMatches) rosterCards.Select(card);
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
}

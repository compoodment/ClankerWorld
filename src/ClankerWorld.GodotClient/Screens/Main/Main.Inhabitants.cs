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
        inhabitantList.CustomMinimumSize = new Vector2(inhabitantList.CustomMinimumSize.X,
            Math.Clamp(inhabitantList.ItemCount * rowHeight + 12, 40, 360));
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
}

using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

/// <summary>
/// The Profile's People section as short rows with an icon for each kind of
/// tie: a heart for a partner, a figure for parents and children, a house for
/// their household, and how much they trust someone as a small meter.
/// </summary>
public partial class Main
{
    private readonly HFlowContainer profilePeople = new();
    private string? renderedProfilePeople;

    private void BuildProfilePeople(VBoxContainer body)
    {
        profilePeople.AddThemeConstantOverride("h_separation", 12);
        profilePeople.AddThemeConstantOverride("v_separation", 3);
        body.AddChild(profilePeople);
    }

    private void RenderProfilePeople(OwnerWorldSnapshot snapshot, OwnerWorldInhabitant inhabitant)
    {
        var ties = inhabitant.Relationships.Select(link => (link, Name: GameUiText.PartyName(snapshot, link.OtherPartyId))).ToArray();
        var signature = string.Join("|", UiTheme.Current.Name,
            string.Join(",", ties.Select(tie => $"{tie.link.Type}:{tie.link.State}:{tie.link.Direction}:{tie.Name}")),
            string.Join(",", inhabitant.SocialStanding.Select(standing => $"{standing.SubjectName}:{standing.Trust}")),
            string.Join(",", inhabitant.SocialNotes));
        if (signature == renderedProfilePeople) return;
        renderedProfilePeople = signature;
        foreach (var child in profilePeople.GetChildren())
        {
            profilePeople.RemoveChild(child);
            child.QueueFree();
        }

        var p = UiTheme.Current;
        void Row(Texture2D icon, string relation, string name, Control? extra = null)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 6);
            row.AddChild(new TextureRect { Texture = icon, StretchMode = TextureRect.StretchModeEnum.KeepCentered, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
            row.AddChild(new Label { Text = relation, ThemeTypeVariation = "DimLabel", SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
            row.AddChild(new Label { Text = name, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
            if (extra is not null) row.AddChild(extra);
            profilePeople.AddChild(row);
        }
        var figure = PixelIcons.Texture(PixelGlyph.Person, p.Ink, p.Ink, 1);
        var wood = p.Name == "dark" ? new Color("D89A5A") : new Color("B8733A");
        foreach (var (link, name) in ties)
        {
            // A tie that is no longer current keeps its state beside the name, as before.
            var shownName = link.State is "accepted" || string.IsNullOrWhiteSpace(link.State) ? name : $"{name} · {link.State.Replace('_', ' ')}";
            switch (link.Type)
            {
                case "partnership":
                    Row(PixelIcons.Texture(PixelGlyph.Heart, p.Partner, p.Partner, 1), "Partner", shownName);
                    break;
                case "biological_parentage" when link.Direction is "parent" or "child":
                    Row(figure, link.Direction == "parent" ? "Parent of" : "Child of", shownName);
                    break;
                case "household_membership":
                    Row(PixelIcons.Texture(PixelGlyph.House, p.Ink, wood, 1), "Member of", shownName);
                    break;
                default:
                    Row(figure, $"{Pretty(link.Type)} with", shownName);
                    break;
            }
        }
        foreach (var standing in inhabitant.SocialStanding)
            Row(PixelIcons.Texture(PixelGlyph.Person, p.Ink, p.Primary, 1), "Trusts", standing.SubjectName,
                Sureness(standing.Trust * 1000, $"Trust {standing.Trust} of 10"));
        foreach (var note in inhabitant.SocialNotes)
            profilePeople.AddChild(new Label { Text = GameUiText.PlainEllipses(note), ThemeTypeVariation = "DimLabel", AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(240, 0) });
        if (profilePeople.GetChildCount() == 0)
            profilePeople.AddChild(new Label { Text = "No close relationships yet.", ThemeTypeVariation = "DimLabel" });
    }

    /// <summary>Each People row as one line of text, for checks and assistive reading.</summary>
    private string ProfilePeopleText() => string.Join("\n", profilePeople.GetChildren().OfType<Control>().Select(row => row is Label label
        ? label.Text
        : string.Join(" ", row.GetChildren().OfType<Label>().Select(part => part.Text))));
}

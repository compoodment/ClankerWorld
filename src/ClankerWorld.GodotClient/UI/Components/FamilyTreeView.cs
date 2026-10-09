using Godot;

namespace ClankerWorld.GodotClient.UI;

/// <summary>
/// An owner-only ancestry graph. Household and caregiver links are deliberately
/// excluded: sharing a home or caring for someone does not create ancestry.
/// </summary>
public partial class FamilyTreeView : Control
{
    private const float NodeWidth = 132;
    private const float NodeHeight = 36;
    private const float ColumnWidth = 150;
    private const float RowHeight = 76;
    private const float Margin = 8;
    private readonly Dictionary<string, Button> buttons = new(StringComparer.Ordinal);
    private readonly List<(string Parent, string Child)> parentEdges = [];
    private readonly List<(string First, string Second)> partnerEdges = [];
    private string? currentSignature;

    public event Action<string>? PersonRequested;

    /// <summary>Draws a person's small portrait for their box; the flag is whether they are alive.</summary>
    public Func<OwnerWorldInhabitant, bool, Texture2D>? Portrait { get; set; }

    public IReadOnlyCollection<string> VisiblePersonIds => buttons.Keys.ToArray();
    public int ParentEdgeCount => parentEdges.Count;
    public int PartnerEdgeCount => partnerEdges.Count;

    public void SetPeople(string worldId, IReadOnlyList<OwnerWorldInhabitant> people, string centerId)
    {
        ArgumentNullException.ThrowIfNull(people);
        var signature = worldId + ":" + centerId + ":" + UiTheme.Current.Name + ":" + string.Join('|', people
            .OrderBy(person => person.Id, StringComparer.Ordinal)
            .Select(person => person.Id + ":" + person.DisplayName + ":" + person.Lifecycle + ":" +
                person.DecisionFactors.FirstOrDefault(factor => factor.Key == "age-band")?.Detail + ":" +
                string.Join(',', person.Relationships.OrderBy(item => item.RelationshipId, StringComparer.Ordinal)
                    .Select(item => item.RelationshipId + ":" + item.Type + ":" + item.State + ":" + item.Direction))));
        if (signature == currentSignature) return;
        currentSignature = signature;

        foreach (var button in buttons.Values)
        {
            RemoveChild(button);
            button.QueueFree();
        }
        buttons.Clear();
        parentEdges.Clear();
        partnerEdges.Clear();

        var byId = people.ToDictionary(person => person.Id, StringComparer.Ordinal);
        if (!byId.ContainsKey(centerId))
        {
            QueueRedraw();
            return;
        }

        var parentLinks = new HashSet<(string Parent, string Child)>();
        var partnerLinks = new HashSet<(string First, string Second)>();
        foreach (var person in people)
        {
            foreach (var link in person.Relationships)
            {
                if (!byId.ContainsKey(link.OtherPartyId)) continue;
                if (link.Type == "biological_parentage" &&
                    link.State is "accepted" or "ended_by_death")
                {
                    if (link.Direction == "parent") parentLinks.Add((person.Id, link.OtherPartyId));
                    if (link.Direction == "child") parentLinks.Add((link.OtherPartyId, person.Id));
                }
                else if (link.Type == "partnership" &&
                         link.State is "accepted" or "ended_by_death")
                {
                    var first = string.CompareOrdinal(person.Id, link.OtherPartyId) < 0 ? person.Id : link.OtherPartyId;
                    var second = first == person.Id ? link.OtherPartyId : person.Id;
                    partnerLinks.Add((first, second));
                }
            }
        }

        var neighbors = byId.Keys.ToDictionary(id => id,
            _ => new List<(string Id, int GenerationDelta)>(), StringComparer.Ordinal);
        foreach (var (parent, child) in parentLinks)
        {
            neighbors[parent].Add((child, 1));
            neighbors[child].Add((parent, -1));
        }
        foreach (var (first, second) in partnerLinks)
        {
            neighbors[first].Add((second, 0));
            neighbors[second].Add((first, 0));
        }

        var generation = new Dictionary<string, int>(StringComparer.Ordinal) { [centerId] = 0 };
        var remaining = new Queue<string>();
        remaining.Enqueue(centerId);
        while (remaining.TryDequeue(out var id))
        {
            foreach (var (other, delta) in neighbors[id].OrderBy(item => item.Id, StringComparer.Ordinal))
            {
                if (generation.TryAdd(other, generation[id] + delta)) remaining.Enqueue(other);
            }
        }

        var rows = generation.GroupBy(item => item.Value).OrderBy(group => group.Key).ToArray();
        // Just big enough for the widest generation, so the panel can fit the tree.
        var width = rows.Max(group => group.Count()) * ColumnWidth - (ColumnWidth - NodeWidth) + 2 * Margin;
        CustomMinimumSize = new Vector2(width, rows.Length * RowHeight - (RowHeight - NodeHeight) + 2 * Margin);
        for (var rowIndex = 0; rowIndex < rows.Length; rowIndex++)
        {
            var row = rows[rowIndex].Select(item => byId[item.Key])
                .OrderBy(person => person.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(person => person.Id, StringComparer.Ordinal).ToArray();
            var left = (width - row.Length * ColumnWidth) / 2 + (ColumnWidth - NodeWidth) / 2;
            for (var column = 0; column < row.Length; column++)
            {
                var person = row[column];
                var deceased = person.Lifecycle.Equals("dead", StringComparison.OrdinalIgnoreCase);
                var button = new Button
                {
                    Text = deceased ? person.DisplayName + " · died" : person.DisplayName,
                    Icon = Portrait?.Invoke(person, !deceased),
                    Alignment = HorizontalAlignment.Left,
                    ClipText = true,
                    Position = new Vector2(left + column * ColumnWidth, Margin + rowIndex * RowHeight),
                    Size = new Vector2(NodeWidth, NodeHeight),
                    TooltipText = "Open " + person.DisplayName + "'s profile",
                    // The current person stands out; deceased people have an explicit "died" label.
                    ThemeTypeVariation = person.Id == centerId ? "PrimaryButton" : string.Empty,
                };
                var personId = person.Id;
                button.Pressed += () => PersonRequested?.Invoke(personId);
                buttons.Add(person.Id, button);
                AddChild(button);
            }
        }

        parentEdges.AddRange(parentLinks.Where(link => buttons.ContainsKey(link.Parent) && buttons.ContainsKey(link.Child))
            .OrderBy(link => link.Parent, StringComparer.Ordinal).ThenBy(link => link.Child, StringComparer.Ordinal));
        partnerEdges.AddRange(partnerLinks.Where(link => buttons.ContainsKey(link.First) && buttons.ContainsKey(link.Second))
            .OrderBy(link => link.First, StringComparer.Ordinal).ThenBy(link => link.Second, StringComparer.Ordinal));
        QueueRedraw();
    }

    public override void _Draw()
    {
        foreach (var (parent, child) in parentEdges)
        {
            var from = buttons[parent].Position + new Vector2(NodeWidth / 2, NodeHeight);
            var to = buttons[child].Position + new Vector2(NodeWidth / 2, 0);
            var midpoint = (from.Y + to.Y) / 2;
            var kin = UiTheme.Current.Primary;
            DrawLine(from, new Vector2(from.X, midpoint), kin, 2);
            DrawLine(new Vector2(from.X, midpoint), new Vector2(to.X, midpoint), kin, 2);
            DrawLine(new Vector2(to.X, midpoint), to, kin, 2);
        }
        foreach (var (first, second) in partnerEdges)
        {
            var a = buttons[first].Position + new Vector2(NodeWidth / 2, NodeHeight / 2);
            var b = buttons[second].Position + new Vector2(NodeWidth / 2, NodeHeight / 2);
            DrawLine(a, b, UiTheme.Current.Partner, 2);
            // A heart on the line where it shows between the two boxes.
            var gapLeft = Math.Min(a.X, b.X) + NodeWidth / 2;
            var gapRight = Math.Max(a.X, b.X) - NodeWidth / 2;
            var heart = PixelIcons.Texture(PixelGlyph.Heart, UiTheme.Current.Partner, UiTheme.Current.Partner, 1);
            DrawTexture(heart, new Vector2(Mathf.Floor((gapLeft + gapRight) / 2), Mathf.Floor(a.Y)) - heart.GetSize() / 2);
        }
    }
}

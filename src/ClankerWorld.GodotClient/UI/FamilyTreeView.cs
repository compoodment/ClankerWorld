using Godot;

namespace ClankerWorld.GodotClient.UI;

/// <summary>
/// An owner-only ancestry graph. Household and caregiver links are deliberately
/// excluded: sharing a home or caring for someone does not create ancestry.
/// </summary>
public partial class FamilyTreeView : Control
{
    private const float NodeWidth = 156;
    private const float NodeHeight = 62;
    private const float ColumnWidth = 190;
    private const float RowHeight = 120;
    private readonly Dictionary<string, Button> buttons = new(StringComparer.Ordinal);
    private readonly List<(string Parent, string Child)> parentEdges = [];
    private readonly List<(string First, string Second)> partnerEdges = [];
    private string? currentSignature;

    public event Action<string>? PersonRequested;

    public IReadOnlyCollection<string> VisiblePersonIds => buttons.Keys.ToArray();
    public int ParentEdgeCount => parentEdges.Count;
    public int PartnerEdgeCount => partnerEdges.Count;

    public void SetPeople(string worldId, IReadOnlyList<OwnerWorldInhabitant> people, string centerId)
    {
        ArgumentNullException.ThrowIfNull(people);
        var signature = worldId + ":" + centerId + ":" + string.Join('|', people
            .OrderBy(person => person.Id, StringComparer.Ordinal)
            .Select(person => person.Id + ":" + person.DisplayName + ":" + person.Lifecycle + ":" +
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
        var width = Math.Max(500, rows.Max(group => group.Count()) * ColumnWidth + 40);
        CustomMinimumSize = new Vector2(width, rows.Length * RowHeight + 40);
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
                    Text = person.DisplayName + (deceased ? "\nDeceased" : "\nLiving"),
                    Position = new Vector2(left + column * ColumnWidth, 24 + rowIndex * RowHeight),
                    Size = new Vector2(NodeWidth, NodeHeight),
                    TooltipText = "Open " + person.DisplayName + "'s profile",
                    // The person whose tree this is stands out; the deceased are faded.
                    ThemeTypeVariation = person.Id == centerId ? "PrimaryButton" : string.Empty,
                    Modulate = deceased ? new Color(1, 1, 1, 0.68f) : Colors.White,
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
        }
    }
}

using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private static readonly string[] DeveloperEditOperations =
        ["set_need", "give_goods", "remove_goods", "add_skill", "remove_skill", "start_partnership", "end_partnership", "add_animal"];
    private static readonly string[] DeveloperEditGoods =
        ["berries", "fruit", "wild_greens", "cultivated_greens", "grain", "flour", "wood", "stone",
         "fiber", "rope", "cloth", "clothing", "padded_coat", "rain_cloak", "basket", "sack",
         "wooden_axe", "wooden_pickaxe", "iron_ore", "iron", "storage_pot", "water_jug", "medicine", "tree_seed",
         "eggs", "wool", "hide", "leather", "leather_sack", "saddle", "cooked_eggs", "milk_porridge", "rich_meal"];
    private readonly Label developerEditAgent = new();
    private readonly Label developerEditHint = new();
    private readonly OptionButton developerEditKind = new();
    private readonly OptionButton developerEditValue = new();
    private readonly OptionButton developerEditOther = new();
    private readonly SpinBox developerEditAmount = new() { MinValue = 0, MaxValue = 100, Step = 1, Value = 50 };
    private readonly Button developerEditApply = new();
    private string? developerEditPeopleKey;

    private void BuildDeveloperEdits()
    {
        var body = new VBoxContainer();
        developerEditAgent.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        body.AddChild(developerEditAgent);
        foreach (var label in new[] { "Set a need", "Give goods", "Remove goods", "Add a skill", "Remove a skill", "Start a partnership", "End a partnership", "Add a household animal" })
            developerEditKind.AddItem(label);
        developerEditKind.ItemSelected += _ => ConfigureDeveloperEdit();
        body.AddChild(developerEditKind);
        body.AddChild(developerEditValue);
        body.AddChild(developerEditAmount);
        body.AddChild(developerEditOther);
        developerEditHint.Text = "Pause with Space and select a living agent; every accepted edit is saved and marked Developer edit in the Event Log. Goods must fit the agent's carried load; equipped items and filled vessels are protected.";
        developerEditHint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        developerEditHint.ThemeTypeVariation = "DimLabel";
        body.AddChild(developerEditHint);
        developerEditApply.Text = "Apply developer edit";
        developerEditApply.Pressed += () => _ = ApplyDeveloperEditAsync();
        StyleButton(developerEditApply);
        body.AddChild(developerEditApply);
        developerBody.AddChild(NewPanel("Edit selected agent", body));
        ConfigureDeveloperEdit();
    }

    private string DeveloperEditOperation => DeveloperEditOperations[Math.Max(0, developerEditKind.Selected)];

    private void ConfigureDeveloperEdit()
    {
        var operation = DeveloperEditOperation;
        var partnership = operation.EndsWith("partnership", StringComparison.Ordinal);
        developerEditValue.Clear();
        var values = operation switch
        {
            "set_need" => new[] { "fullness", "warmth", "illness", "nutrition" },
            "give_goods" or "remove_goods" => DeveloperEditGoods,
            "add_skill" or "remove_skill" => new[] { "building", "farming", "crafting", "smithing" },
            "add_animal" => new[] { "chicken:female", "chicken:male", "sheep:female", "sheep:male", "cow:female", "cow:male", "horse:female", "horse:male" },
            _ => new[] { "partnership" },
        };
        foreach (var value in values)
        {
            developerEditValue.AddItem(GameUiText.HumanizeIdentifier(value));
            developerEditValue.SetItemMetadata(developerEditValue.ItemCount - 1, value);
        }
        developerEditValue.Visible = !partnership;
        developerEditOther.Visible = partnership;
        developerEditAmount.Visible = operation is "set_need" or "give_goods" or "remove_goods";
        developerEditAmount.MinValue = operation == "set_need" ? 0 : 1;
        developerEditAmount.Suffix = operation == "set_need" ? "%" : "goods";
        RefreshControlAvailability();
    }

    private void RenderDeveloperEdits(OwnerWorldSnapshot? snapshot, bool actionDisabled)
    {
        var selected = snapshot?.Inhabitants.FirstOrDefault(person => person.Id == selectedInhabitantId);
        developerEditAgent.Text = selected is null ? "Select an agent on the map or above." : $"Selected: {selected.DisplayName}";
        var people = snapshot?.Inhabitants.Where(person => !person.IsDraft && IsLiving(person) && person.Id != selectedInhabitantId)
            .OrderBy(person => person.DisplayName, StringComparer.Ordinal).ThenBy(person => person.Id, StringComparer.Ordinal).ToArray() ?? [];
        var key = string.Join('\n', people.Select(person => person.Id + "\t" + person.DisplayName));
        if (key != developerEditPeopleKey)
        {
            var previous = developerEditOther.Selected >= 0 ? developerEditOther.GetSelectedMetadata().AsString() : null;
            developerEditPeopleKey = key;
            developerEditOther.Clear();
            foreach (var person in people)
            {
                developerEditOther.AddItem(person.DisplayName);
                var index = developerEditOther.ItemCount - 1;
                developerEditOther.SetItemMetadata(index, person.Id);
                if (person.Id == previous) developerEditOther.Select(index);
            }
        }
        var disabled = actionDisabled || snapshot?.Authoring?.IsPaused != true || selected is null || selected.IsDraft || !IsLiving(selected);
        developerEditKind.Disabled = disabled;
        developerEditValue.Disabled = disabled;
        developerEditOther.Disabled = disabled;
        developerEditAmount.Editable = !disabled;
        developerEditApply.Disabled = disabled || developerEditOther.Visible && developerEditOther.Selected < 0;
    }

    private async Task ApplyDeveloperEditAsync()
    {
        if (!TryGetOwner(out var authority, out var deviceId, out var signer) ||
            renderedMapSnapshot is not { } snapshot || selectedInhabitantId is not { } agent)
            return;
        var operation = DeveloperEditOperation;
        var action = new OwnerDeveloperEditAction(snapshot.WorldId, snapshot.LatestEventId, agent, operation,
            developerEditValue.GetSelectedMetadata().AsString(),
            developerEditAmount.Visible ? (int)developerEditAmount.Value : 0,
            operation.EndsWith("partnership", StringComparison.Ordinal) && developerEditOther.Selected >= 0
                ? developerEditOther.GetSelectedMetadata().AsString() : null);
        await RunOwnerActionAsync(async () =>
        {
            await AwaitCurrentWorldResultAsync(ownerApi.ApplyDeveloperEditAsync(ResolveWorldUri(), authority, deviceId, action, signer, CancellationToken.None));
            return "Developer edit saved. See the Event Log for the change.";
        }, conflictMessage: "The developer edit was refused; nothing changed. Refresh, then check the selected need, available goods, skill or partnership.");
    }
}

using Godot;

namespace ClankerWorld.GodotClient.UI;

/// <summary>
/// Chooses an agent's model from the list a provider key offers, with the
/// recommended model marked and a "Type a model name…" choice for anything the
/// list leaves out. While the list loads, or when it can't be read, the owner
/// can still keep the current model or type one.
/// </summary>
public partial class ModelPicker : VBoxContainer
{
    public const string TypeOwnText = "Type a model name…";
    private const string TypeOwnId = "\u0001type";

    private readonly OptionButton choice = new();
    private readonly LineEdit typed = new();
    private readonly HBoxContainer problemRow = new();
    private readonly Label problem = new();
    private readonly Button retry = new();
    private IReadOnlyList<string> models = [];
    private string recommended = string.Empty;
    private string current = string.Empty;
    private string? error;
    private bool loading;
    private bool listed;
    private bool typing;
    private int request;

    public ModelPicker()
    {
        AddThemeConstantOverride("separation", 4);
        choice.FitToLongestItem = false;
        choice.ClipText = true;
        choice.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        choice.TooltipText = "Models this key can use. Pick Type a model name… for one that isn't listed.";
        choice.ItemSelected += OnItemSelected;
        AddChild(choice);

        typed.PlaceholderText = "Model name";
        typed.Visible = false;
        typed.TextChanged += _ => ModelChanged?.Invoke();
        AddChild(typed);

        problem.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        problem.ThemeTypeVariation = "DimLabel";
        problem.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        problemRow.AddThemeConstantOverride("separation", 6);
        problemRow.AddChild(problem);
        retry.Text = "Retry";
        retry.TooltipText = "Ask the provider for its model list again.";
        retry.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        retry.Pressed += () => RetryRequested?.Invoke();
        problemRow.AddChild(retry);
        problemRow.Visible = false;
        AddChild(problemRow);
        Rebuild();
    }

    /// <summary>Raised when the owner asks for the list again after it failed.</summary>
    public event Action? RetryRequested;

    /// <summary>Raised when the owner picks or types a different model.</summary>
    public event Action? ModelChanged;

    /// <summary>The model to save: the typed name, or the chosen list entry.</summary>
    public string Model => (typing ? typed.Text : current).Trim();

    public bool IsTyping => typing;

    public bool Editable
    {
        get => !choice.Disabled;
        set
        {
            choice.Disabled = !value;
            typed.Editable = value;
            retry.Disabled = !value;
        }
    }

    // Read by the interface smoke checks.
    public OptionButton Choice => choice;
    public LineEdit TypedInput => typed;
    public string Problem => problemRow.Visible ? problem.Text : string.Empty;
    public bool CanRetry => problemRow.Visible && retry.Visible;

    /// <summary>Shows a model as chosen, for example after the provider changes.</summary>
    public void SetModel(string model)
    {
        current = model.Trim();
        typing = false;
        typed.Text = current;
        Rebuild();
    }

    /// <summary>
    /// Starts a new lookup and returns its number. Only the latest lookup's
    /// answer is shown, so a slow reply for an old key can't replace a newer list.
    /// </summary>
    public int BeginLoading(string recommendedModel)
    {
        recommended = recommendedModel;
        loading = true;
        listed = false;
        error = null;
        models = [];
        Rebuild();
        return ++request;
    }

    public bool IsLatest(int lookup) => lookup == request;

    public void ShowList(IReadOnlyList<string> offered, string recommendedModel)
    {
        recommended = recommendedModel;
        models = offered;
        loading = false;
        listed = true;
        error = offered.Count == 0 ? "This key doesn't offer any chat models. Type a model name instead." : null;
        if (current.Length == 0 && offered.Count > 0)
            current = offered.Contains(recommended, StringComparer.Ordinal) ? recommended : offered[0];
        Rebuild(canRetry: false);
    }

    public void ShowError(string message, string recommendedModel, bool canRetry = true)
    {
        recommended = recommendedModel;
        models = [];
        loading = false;
        listed = false;
        error = message;
        if (current.Length == 0) current = recommended;
        Rebuild(canRetry);
    }

    /// <summary>For a provider without a model list: only the typed name.</summary>
    public void ShowTypedOnly(string model)
    {
        ++request;
        current = model.Trim();
        typed.Text = current;
        typing = true;
        loading = false;
        listed = false;
        error = null;
        models = [];
        choice.Visible = false;
        typed.Visible = true;
        problemRow.Visible = false;
    }

    private void Rebuild(bool canRetry = true)
    {
        choice.Visible = true;
        choice.Clear();
        var known = models.Contains(current, StringComparer.Ordinal);
        if (current.Length > 0 && !known)
        {
            // Keep the agent's model even when this key's list leaves it out.
            AddModel(current, listed ? $"{current} (not offered by this key)" :
                current == recommended ? $"{current} (recommended)" : current);
        }
        foreach (var model in models)
            AddModel(model, model == recommended ? $"{model} (recommended)" : model);
        if (loading)
        {
            choice.AddItem("Loading models…");
            choice.SetItemDisabled(choice.ItemCount - 1, true);
            choice.SetItemMetadata(choice.ItemCount - 1, string.Empty);
        }
        if (choice.ItemCount > 0) choice.AddSeparator();
        choice.AddItem(TypeOwnText);
        choice.SetItemMetadata(choice.ItemCount - 1, TypeOwnId);
        SelectCurrent();
        typed.Visible = typing;
        problem.Text = error ?? string.Empty;
        retry.Visible = canRetry;
        problemRow.Visible = error is not null;
    }

    private void AddModel(string model, string text)
    {
        choice.AddItem(text);
        choice.SetItemMetadata(choice.ItemCount - 1, model);
        choice.SetItemTooltip(choice.ItemCount - 1, model);
    }

    private void SelectCurrent()
    {
        for (var index = 0; index < choice.ItemCount; index++)
        {
            var id = choice.GetItemMetadata(index).AsString();
            if (typing ? id == TypeOwnId : id.Length > 0 && id == current)
            {
                choice.Select(index);
                return;
            }
        }
        // Nothing chosen yet: wait for the list, or ask for a typed name.
        if (loading && current.Length == 0)
        {
            choice.Select(0);
            return;
        }
        choice.Select(choice.ItemCount - 1);
        typing = true;
    }

    private void OnItemSelected(long index)
    {
        var id = choice.GetItemMetadata((int)index).AsString();
        if (id == TypeOwnId)
        {
            if (!typing) typed.Text = current;
            typing = true;
            typed.Visible = true;
            typed.GrabFocus();
            typed.CaretColumn = typed.Text.Length;
        }
        else if (id.Length > 0)
        {
            typing = false;
            typed.Visible = false;
            current = id;
        }
        ModelChanged?.Invoke();
    }
}

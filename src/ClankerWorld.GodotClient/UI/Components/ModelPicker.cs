using Godot;

namespace ClankerWorld.GodotClient.UI;

/// <summary>
/// Chooses an agent's model from the game's list for a provider, newest at the top,
/// with a "Type a model name…" choice for any other model. Models the chosen
/// key can't use are shown but can't be picked. While the list loads, or when
/// it can't be read, the owner can still keep the current model or type one.
/// </summary>
public partial class ModelPicker : VBoxContainer
{
    public const string TypeOwnText = "Type a model name…";
    public const string UnavailableNote = " (not available with this key)";
    public const string ChooseText = "Choose a model";
    private const string TypeOwnId = "\u0001type";

    private readonly OptionButton choice = new();
    private readonly LineEdit typed = new();
    private readonly HBoxContainer problemRow = new();
    private readonly Label problem = new();
    private readonly Button retry = new();
    private IReadOnlyList<OwnerProviderModelChoice> models = [];
    private string defaultModel = string.Empty;
    private string current = string.Empty;
    private string? error;
    private bool loading;
    private bool listed;
    private bool typing;
    private bool mustChoose;
    private bool newAgent;
    private int request;

    public ModelPicker()
    {
        AddThemeConstantOverride("separation", 4);
        choice.FitToLongestItem = false;
        choice.ClipText = true;
        choice.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        choice.TooltipText = "The game's models for this provider, newest at the top. Pick Type a model name… for any other model.";
        choice.ItemSelected += OnItemSelected;
        AddChild(choice);

        typed.PlaceholderText = "Model name";
        typed.Visible = false;
        typed.TextChanged += _ => { newAgent = false; ModelChanged?.Invoke(); };
        AddChild(typed);

        problem.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        problem.ThemeTypeVariation = "DimLabel";
        problem.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        problemRow.AddThemeConstantOverride("separation", 6);
        problemRow.AddChild(problem);
        retry.Text = "Retry";
        retry.TooltipText = "Check the key with the provider again.";
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

    /// <summary>
    /// Shows a model as chosen, for example after the provider changes. A model
    /// that isn't in the game's list is shown as a typed name. For a
    /// <paramref name="isNewAgent"/>, a starting model the key can't use is
    /// cleared so the owner picks one; an existing agent's model stays shown.
    /// </summary>
    public void SetModel(string model, bool isNewAgent = false)
    {
        current = model.Trim();
        typing = false;
        typed.Text = current;
        newAgent = isNewAgent;
        ChooseWhenEmpty();
        FollowList();
        AskWhenUnusable();
        Rebuild();
    }

    /// <summary>
    /// Starts a new lookup and returns its number. Only the latest lookup's
    /// answer is shown, so a slow reply for an old key can't replace a newer list.
    /// </summary>
    public int BeginLoading(string fallbackModel)
    {
        defaultModel = fallbackModel;
        loading = true;
        listed = false;
        mustChoose = false;
        error = null;
        models = [];
        Rebuild();
        return ++request;
    }

    public bool IsLatest(int lookup) => lookup == request;

    /// <summary>
    /// Shows the game's models in the order given. With nothing chosen yet, the
    /// default model is picked when the key can use it. If the key can't use
    /// the chosen or default model, nothing is picked and the owner is asked to
    /// choose, so no other model is chosen for them. <paramref name="note"/>
    /// explains a key that couldn't be checked.
    /// </summary>
    public void ShowList(IReadOnlyList<OwnerProviderModelChoice> choices, string fallbackModel,
        string? note = null, bool canRetry = true)
    {
        defaultModel = fallbackModel;
        models = choices;
        loading = false;
        listed = true;
        mustChoose = false;
        error = note;
        // Keep a name the owner typed, but an empty text box gives way to the list.
        if (typing && typed.Text.Trim().Length == 0 && choices.Count > 0) typing = false;
        ChooseWhenEmpty();
        FollowList();
        AskWhenUnusable();
        Rebuild(canRetry);
    }

    public void ShowError(string message, string fallbackModel, bool canRetry = true)
    {
        defaultModel = fallbackModel;
        models = [];
        loading = false;
        listed = false;
        mustChoose = false;
        error = message;
        if (current.Length == 0) current = defaultModel;
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
        mustChoose = false;
        error = null;
        models = [];
        choice.Visible = false;
        typed.Visible = true;
        problemRow.Visible = false;
    }

    private bool Lists(string model) => models.Any(item => item.Model == model);

    // With nothing chosen, start on the default model when the key can use it.
    private void ChooseWhenEmpty()
    {
        if (current.Length == 0 && models.Any(item => item.Model == defaultModel && item.Available))
            current = defaultModel;
    }

    // No other model is ever chosen in place of one the key can't use: the
    // owner picks. A new agent's starting model is cleared so nothing costly is
    // chosen for them; an existing or hand-picked model stays shown, greyed.
    private void AskWhenUnusable()
    {
        if (mustChoose)
        {
            mustChoose = false;
            error = null;
        }
        if (typing || models.Count == 0) return;
        var currentUnusable = models.Any(item => item.Model == current && !item.Available);
        var unusable = currentUnusable ? current :
            current.Length == 0 && models.Any(item => item.Model == defaultModel) ? defaultModel : null;
        if (unusable is null && current.Length > 0) return;
        if (!currentUnusable || newAgent) current = string.Empty;
        mustChoose = true;
        error = unusable is null ? "Choose a model." : $"This key can't use {unusable}. Choose a model it can use.";
    }

    // An agent's model that isn't in the game's list stays, as a typed name.
    private void FollowList()
    {
        if (!listed || typing || current.Length == 0 || Lists(current)) return;
        typing = true;
        typed.Text = current;
    }

    private void Rebuild(bool canRetry = true)
    {
        choice.Visible = true;
        choice.Clear();
        if (!listed && !typing && current.Length > 0) AddModel(current, current, available: true);
        if (listed && !typing && current.Length == 0 && models.Count > 0)
        {
            choice.AddItem(ChooseText);
            choice.SetItemDisabled(choice.ItemCount - 1, true);
            choice.SetItemMetadata(choice.ItemCount - 1, string.Empty);
        }
        foreach (var item in models)
            AddModel(item.Model, item.Available ? item.Model : item.Model + UnavailableNote, item.Available);
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
        retry.Visible = canRetry && !mustChoose;
        problemRow.Visible = error is not null;
    }

    private void AddModel(string model, string text, bool available)
    {
        choice.AddItem(text);
        choice.SetItemMetadata(choice.ItemCount - 1, model);
        choice.SetItemTooltip(choice.ItemCount - 1, model);
        choice.SetItemDisabled(choice.ItemCount - 1, !available);
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
        // Nothing chosen yet: wait for the list, ask the owner to choose from
        // it, or ask for a typed name.
        if (current.Length == 0 && (loading || listed && models.Count > 0))
        {
            choice.Select(0);
            return;
        }
        choice.Select(choice.ItemCount - 1);
        typing = true;
    }

    private void OnItemSelected(long index)
    {
        // A model the owner picks by hand is theirs, and stays shown later.
        newAgent = false;
        var id = choice.GetItemMetadata((int)index).AsString();
        if (mustChoose && id.Length > 0)
        {
            // The owner has chosen, so the request to choose goes away.
            mustChoose = false;
            error = null;
            problemRow.Visible = false;
        }
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

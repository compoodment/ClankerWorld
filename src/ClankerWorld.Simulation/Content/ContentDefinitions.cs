using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClankerWorld.Simulation.Content;

/// <summary>
/// A positive amount of a declarative resource used by a building or recipe.
/// Resource IDs are intentionally local IDs until the resource-definition lane
/// is introduced; no executable behavior is attached to a quantity.
/// </summary>
public readonly record struct ContentQuantity(string ResourceId, int Amount)
{
    public void Validate()
    {
        ContentPackageRules.ValidateLocalId(ResourceId);
        if (Amount is < 1 or > ContentDefinitionRules.MaxQuantity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Amount),
                Amount,
                $"Content quantities must be between {ContentDefinitionRules.MaxQuantity}.");
        }
    }
}

/// <summary>
/// An immutable, data-only building definition. Its immutable ID follows the
/// package definition convention and its payload digest is derived from the
/// canonical definition fields.
/// </summary>
public sealed class BuildingDefinition
{
    public const string SchemaKind = "building";
    private string? canonicalId;

    public BuildingDefinition(
        string packageDigest,
        string localId,
        ContentVersion version,
        string displayName,
        int width,
        int height,
        int capacity,
        IEnumerable<ContentQuantity>? buildCosts = null,
        IEnumerable<string>? tags = null,
        string? payloadDigest = null)
    {
        PackageDigest = packageDigest;
        LocalId = localId;
        Version = version;
        DisplayName = displayName;
        Width = width;
        Height = height;
        Capacity = capacity;
        BuildCosts = ContentDefinitionRules.CopyQuantities(buildCosts);
        Tags = ContentDefinitionRules.CopyTags(tags);
        PayloadDigest = payloadDigest ?? ContentDefinitionRules.ComputeBuildingPayloadDigest(
            PackageDigest,
            LocalId,
            Version,
            DisplayName,
            Width,
            Height,
            Capacity,
            BuildCosts,
            Tags);
    }

    public string PackageDigest { get; }

    public string LocalId { get; }

    public ContentVersion Version { get; }

    public string DisplayName { get; }

    public int Width { get; }

    public int Height { get; }

    public int Capacity { get; }

    public IReadOnlyList<ContentQuantity> BuildCosts { get; }

    public IReadOnlyList<string> Tags { get; }

    public string PayloadDigest { get; }

    // Every part of the ID is fixed at construction, and runtime checks compare
    // it constantly, so validate and format it once.
    public string CanonicalId => canonicalId ??= ContentPackageRules.CanonicalDefinitionId(
        PackageDigest,
        SchemaKind,
        LocalId,
        Version);

    public void Validate()
    {
        ContentDefinitionRules.ValidateIdentity(PackageDigest, LocalId, Version);
        ContentDefinitionRules.ValidateDisplayName(DisplayName);
        if (Width is < 1 or > ContentDefinitionRules.MaxBuildingDimension)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Width),
                Width,
                $"Building width must be between 1 and {ContentDefinitionRules.MaxBuildingDimension}.");
        }

        if (Height is < 1 or > ContentDefinitionRules.MaxBuildingDimension)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Height),
                Height,
                $"Building height must be between 1 and {ContentDefinitionRules.MaxBuildingDimension}.");
        }

        if (Capacity is < 0 or > ContentDefinitionRules.MaxCapacity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Capacity),
                Capacity,
                $"Building capacity must be between 0 and {ContentDefinitionRules.MaxCapacity}.");
        }

        ContentDefinitionRules.ValidateQuantities(BuildCosts, nameof(BuildCosts), allowEmpty: true);
        ContentDefinitionRules.ValidateTags(Tags, nameof(Tags));
        ContentDefinitionRules.ValidatePayloadDigest(
            PayloadDigest,
            ContentDefinitionRules.ComputeBuildingPayloadDigest(
                PackageDigest,
                LocalId,
                Version,
                DisplayName,
                Width,
                Height,
                Capacity,
                BuildCosts,
                Tags));
    }
}

/// <summary>
/// An immutable, data-only recipe definition. Recipes describe resource
/// transformations and an optional building reference; they cannot execute
/// code or access a host capability.
/// </summary>
public sealed class RecipeDefinition
{
    public const string SchemaKind = "recipe";
    private string? canonicalId;

    public RecipeDefinition(
        string packageDigest,
        string localId,
        ContentVersion version,
        string displayName,
        IEnumerable<ContentQuantity>? inputs,
        IEnumerable<ContentQuantity>? outputs,
        int durationTicks,
        string? workstationBuildingId = null,
        IEnumerable<string>? tags = null,
        string? payloadDigest = null)
    {
        PackageDigest = packageDigest;
        LocalId = localId;
        Version = version;
        DisplayName = displayName;
        Inputs = ContentDefinitionRules.CopyQuantities(inputs);
        Outputs = ContentDefinitionRules.CopyQuantities(outputs);
        DurationTicks = durationTicks;
        WorkstationBuildingId = workstationBuildingId;
        Tags = ContentDefinitionRules.CopyTags(tags);
        PayloadDigest = payloadDigest ?? ContentDefinitionRules.ComputeRecipePayloadDigest(
            PackageDigest,
            LocalId,
            Version,
            DisplayName,
            Inputs,
            Outputs,
            DurationTicks,
            WorkstationBuildingId,
            Tags);
    }

    public string PackageDigest { get; }

    public string LocalId { get; }

    public ContentVersion Version { get; }

    public string DisplayName { get; }

    public IReadOnlyList<ContentQuantity> Inputs { get; }

    public IReadOnlyList<ContentQuantity> Outputs { get; }

    public int DurationTicks { get; }

    public string? WorkstationBuildingId { get; }

    public IReadOnlyList<string> Tags { get; }

    public string PayloadDigest { get; }

    /// <summary>
    /// A crop is a declarative recipe that can be built on generated fertile
    /// land instead of requiring a workstation building. The tag is data, not
    /// executable behaviour; the authoritative world runtime supplies the
    /// corresponding deterministic build-site rule.
    /// </summary>
    public bool IsCrop => Tags.Contains("crop", StringComparer.Ordinal);

    // Every part of the ID is fixed at construction, and runtime checks compare
    // it constantly, so validate and format it once.
    public string CanonicalId => canonicalId ??= ContentPackageRules.CanonicalDefinitionId(
        PackageDigest,
        SchemaKind,
        LocalId,
        Version);

    public void Validate()
    {
        ContentDefinitionRules.ValidateIdentity(PackageDigest, LocalId, Version);
        ContentDefinitionRules.ValidateDisplayName(DisplayName);
        ContentDefinitionRules.ValidateQuantities(Inputs, nameof(Inputs), allowEmpty: IsCrop);
        ContentDefinitionRules.ValidateQuantities(Outputs, nameof(Outputs), allowEmpty: false);
        if (DurationTicks is < 1 or > ContentDefinitionRules.MaxDurationTicks)
        {
            throw new ArgumentOutOfRangeException(
                nameof(DurationTicks),
                DurationTicks,
                $"Recipe duration must be between 1 and {ContentDefinitionRules.MaxDurationTicks} ticks.");
        }

        if (WorkstationBuildingId is not null)
        {
            if (IsCrop)
            {
                throw new InvalidDataException("A crop recipe must use generated fertile land rather than a workstation building.");
            }

            ContentDefinitionRules.ValidateBuildingReference(WorkstationBuildingId);
        }

        ContentDefinitionRules.ValidateTags(Tags, nameof(Tags));
        ContentDefinitionRules.ValidatePayloadDigest(
            PayloadDigest,
            ContentDefinitionRules.ComputeRecipePayloadDigest(
                PackageDigest,
                LocalId,
                Version,
                DisplayName,
                Inputs,
                Outputs,
                DurationTicks,
                WorkstationBuildingId,
                Tags));
    }
}

/// <summary>
/// The small declarative world-content projection that can later be composed
/// into the private runtime. It contains definitions only; activation and
/// simulation behavior remain outside this bounded lane.
/// </summary>
[JsonConverter(typeof(DeclarativeWorldContentStateJsonConverter))]
public sealed class DeclarativeWorldContentState
{
    public DeclarativeWorldContentState(
        IEnumerable<BuildingDefinition> buildings,
        IEnumerable<RecipeDefinition> recipes)
    {
        ArgumentNullException.ThrowIfNull(buildings);
        ArgumentNullException.ThrowIfNull(recipes);

        var buildingCopy = buildings.ToArray();
        var recipeCopy = recipes.ToArray();
        foreach (var building in buildingCopy)
        {
            ArgumentNullException.ThrowIfNull(building);
            building.Validate();
        }

        foreach (var recipe in recipeCopy)
        {
            ArgumentNullException.ThrowIfNull(recipe);
            recipe.Validate();
        }

        Buildings = new ReadOnlyCollection<BuildingDefinition>(
            buildingCopy
                .OrderBy(item => item.CanonicalId, StringComparer.Ordinal)
                .ToArray());
        Recipes = new ReadOnlyCollection<RecipeDefinition>(
            recipeCopy
                .OrderBy(item => item.CanonicalId, StringComparer.Ordinal)
                .ToArray());
        ValidateUniqueIds(Buildings, "building");
        ValidateUniqueIds(Recipes, "recipe");
        ValidateRecipeReferences(Buildings, Recipes);
        StateDigest = ContentDefinitionRules.ComputeStateDigest(Buildings, Recipes);
    }

    public IReadOnlyList<BuildingDefinition> Buildings { get; }

    public IReadOnlyList<RecipeDefinition> Recipes { get; }

    public string StateDigest { get; }

    public void Validate()
    {
        foreach (var building in Buildings)
        {
            building.Validate();
        }

        foreach (var recipe in Recipes)
        {
            recipe.Validate();
        }

        if (!Buildings.Select(item => item.CanonicalId).SequenceEqual(
                Buildings.Select(item => item.CanonicalId).Order(StringComparer.Ordinal),
                StringComparer.Ordinal) ||
            !Recipes.Select(item => item.CanonicalId).SequenceEqual(
                Recipes.Select(item => item.CanonicalId).Order(StringComparer.Ordinal),
                StringComparer.Ordinal))
        {
            throw new InvalidDataException("Declarative world-content definitions are not in canonical order.");
        }

        ValidateUniqueIds(Buildings, "building");
        ValidateUniqueIds(Recipes, "recipe");
        ValidateRecipeReferences(Buildings, Recipes);
        var expectedDigest = ContentDefinitionRules.ComputeStateDigest(Buildings, Recipes);
        if (!string.Equals(StateDigest, expectedDigest, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The declarative world-content state digest does not match its definitions.");
        }
    }

    private static void ValidateUniqueIds<T>(IEnumerable<T> definitions, string kind)
        where T : class
    {
        var ids = definitions.Select(definition => definition switch
        {
            BuildingDefinition building => building.CanonicalId,
            RecipeDefinition recipe => recipe.CanonicalId,
            _ => throw new InvalidDataException($"Unknown {kind} definition type.")
        }).ToArray();
        if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Length)
        {
            throw new InvalidOperationException($"Duplicate {kind} definition IDs cannot be registered.");
        }
    }

    private static void ValidateRecipeReferences(
        IReadOnlyList<BuildingDefinition> buildings,
        IReadOnlyList<RecipeDefinition> recipes)
    {
        var buildingIds = buildings
            .Select(item => item.CanonicalId)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var recipe in recipes)
        {
            if (recipe.WorkstationBuildingId is not null &&
                !buildingIds.Contains(recipe.WorkstationBuildingId))
            {
                throw new InvalidDataException(
                    $"Recipe '{recipe.CanonicalId}' references unregistered building '{recipe.WorkstationBuildingId}'.");
            }
        }
    }
}

/// <summary>
/// Deterministically applies new definitions to an immutable declarative
/// content state. Re-registration of an immutable ID is rejected rather than
/// silently replacing content.
/// </summary>
public static class ContentDefinitionApplicator
{
    public static DeclarativeWorldContentState Apply(
        IEnumerable<BuildingDefinition> buildings,
        IEnumerable<RecipeDefinition> recipes) =>
        new(buildings, recipes);

    public static DeclarativeWorldContentState Apply(
        DeclarativeWorldContentState existing,
        IEnumerable<BuildingDefinition> buildings,
        IEnumerable<RecipeDefinition> recipes)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(buildings);
        ArgumentNullException.ThrowIfNull(recipes);
        return new(
            existing.Buildings.Concat(buildings),
            existing.Recipes.Concat(recipes));
    }

    public static DeclarativeWorldContentState RemovePackage(
        DeclarativeWorldContentState existing,
        string packageDigest)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ContentPackageRules.ValidateDigest(packageDigest, nameof(packageDigest));
        return new(
            existing.Buildings.Where(item => !string.Equals(item.PackageDigest, packageDigest, StringComparison.Ordinal)),
            existing.Recipes.Where(item => !string.Equals(item.PackageDigest, packageDigest, StringComparison.Ordinal)));
    }
}

internal sealed class DeclarativeWorldContentStateJsonConverter : JsonConverter<DeclarativeWorldContentState>
{
    public override DeclarativeWorldContentState Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return DeclarativeWorldContentCodec.Decode(
            Encoding.UTF8.GetBytes(document.RootElement.GetRawText()));
    }

    public override void Write(
        Utf8JsonWriter writer,
        DeclarativeWorldContentState value,
        JsonSerializerOptions options)
    {
        using var document = JsonDocument.Parse(DeclarativeWorldContentCodec.Encode(value));
        document.RootElement.WriteTo(writer);
    }
}

/// <summary>
/// Canonical JSON codec for the declarative state. The wire shape contains
/// source fields only; derived payload and state digests are recomputed while
/// decoding, so a round-trip cannot smuggle an unverified digest into state.
/// </summary>
public static class DeclarativeWorldContentCodec
{
    private static readonly JsonSerializerOptions CanonicalJsonOptions = new()
    {
        AllowTrailingCommas = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = JavaScriptEncoder.Default,
        PropertyNameCaseInsensitive = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = false
    };

    public static byte[] Encode(DeclarativeWorldContentState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        state.Validate();
        var wire = new StateWire(
            state.Buildings.Select(ToWire).ToArray(),
            state.Recipes.Select(ToWire).ToArray());
        return JsonSerializer.SerializeToUtf8Bytes(wire, CanonicalJsonOptions);
    }

    public static DeclarativeWorldContentState Decode(ReadOnlySpan<byte> utf8Json)
    {
        var wire = JsonSerializer.Deserialize<StateWire>(utf8Json, CanonicalJsonOptions)
            ?? throw new InvalidDataException("Declarative world-content state cannot be null.");
        if (wire.Buildings is null || wire.Recipes is null)
        {
            throw new InvalidDataException("Declarative world-content state must contain buildings and recipes arrays.");
        }

        var buildings = wire.Buildings.Select(item => new BuildingDefinition(
            item.PackageDigest,
            item.LocalId,
            ContentVersion.Parse(item.Version),
            item.DisplayName,
            item.Width,
            item.Height,
            item.Capacity,
            item.BuildCosts?.Select(quantity => new ContentQuantity(quantity.ResourceId, quantity.Amount)),
            item.Tags,
            item.PayloadDigest)).ToArray();
        var recipes = wire.Recipes.Select(item => new RecipeDefinition(
            item.PackageDigest,
            item.LocalId,
            ContentVersion.Parse(item.Version),
            item.DisplayName,
            item.Inputs?.Select(quantity => new ContentQuantity(quantity.ResourceId, quantity.Amount)),
            item.Outputs?.Select(quantity => new ContentQuantity(quantity.ResourceId, quantity.Amount)),
            item.DurationTicks,
            item.WorkstationBuildingId,
            item.Tags,
            item.PayloadDigest)).ToArray();
        return ContentDefinitionApplicator.Apply(buildings, recipes);
    }

    private static BuildingWire ToWire(BuildingDefinition definition) => new(
        definition.PackageDigest,
        definition.LocalId,
        definition.Version.ToString(),
        definition.DisplayName,
        definition.Width,
        definition.Height,
        definition.Capacity,
        definition.BuildCosts.Select(quantity => new QuantityWire(quantity.ResourceId, quantity.Amount)).ToArray(),
        definition.Tags.ToArray(),
        definition.PayloadDigest);

    private static RecipeWire ToWire(RecipeDefinition definition) => new(
        definition.PackageDigest,
        definition.LocalId,
        definition.Version.ToString(),
        definition.DisplayName,
        definition.Inputs.Select(quantity => new QuantityWire(quantity.ResourceId, quantity.Amount)).ToArray(),
        definition.Outputs.Select(quantity => new QuantityWire(quantity.ResourceId, quantity.Amount)).ToArray(),
        definition.DurationTicks,
        definition.WorkstationBuildingId,
        definition.Tags.ToArray(),
        definition.PayloadDigest);

    public sealed record StateWire(BuildingWire[] Buildings, RecipeWire[] Recipes);

    public sealed record BuildingWire(
        string PackageDigest,
        string LocalId,
        string Version,
        string DisplayName,
        int Width,
        int Height,
        int Capacity,
        QuantityWire[]? BuildCosts,
        string[]? Tags,
        string PayloadDigest);

    public sealed record RecipeWire(
        string PackageDigest,
        string LocalId,
        string Version,
        string DisplayName,
        QuantityWire[]? Inputs,
        QuantityWire[]? Outputs,
        int DurationTicks,
        string? WorkstationBuildingId,
        string[]? Tags,
        string PayloadDigest);

    public sealed record QuantityWire(string ResourceId, int Amount);
}

/// <summary>
/// Materializes the optional typed payloads carried by a data-only package.
/// Opaque definitions remain valid package metadata; only a definition with a
/// declared typed payload enters the declarative world-content projection.
/// </summary>
public static class ContentDefinitionPayloadCodec
{
    private static readonly JsonSerializerOptions PayloadJsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = false,
    };

    public static DeclarativeWorldContentState ApplyPackage(
        DeclarativeWorldContentState existing,
        ContentPackageManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(manifest);
        manifest.Validate();
        var buildings = new List<BuildingDefinition>();
        var recipes = new List<RecipeDefinition>();
        foreach (var definition in manifest.Definitions)
        {
            if (string.IsNullOrWhiteSpace(definition.PayloadJson))
            {
                continue;
            }

            switch (definition.Kind)
            {
                case BuildingDefinition.SchemaKind:
                    buildings.Add(ParseBuilding(manifest, definition));
                    break;
                case RecipeDefinition.SchemaKind:
                    recipes.Add(ParseRecipe(manifest, definition));
                    break;
            }
        }

        return ContentDefinitionApplicator.Apply(existing, buildings, recipes);
    }

    private static BuildingDefinition ParseBuilding(
        ContentPackageManifest manifest,
        ContentDefinition definition)
    {
        var payload = Deserialize<BuildingPayload>(definition.PayloadJson!, definition);
        if (!string.Equals(payload.Schema, "building/v1", StringComparison.Ordinal) ||
            payload.BuildCosts is null || payload.Tags is null)
        {
            throw new InvalidDataException(
                $"Building definition '{definition.CanonicalId(manifest.PackageDigest)}' has an invalid typed payload.");
        }

        return new BuildingDefinition(
            manifest.PackageDigest,
            definition.LocalId,
            definition.Version,
            definition.DisplayName,
            payload.Width,
            payload.Height,
            payload.Capacity,
            payload.BuildCosts.Select(quantity => new ContentQuantity(quantity.ResourceId, quantity.Amount)),
            payload.Tags,
            definition.PayloadDigest);
    }

    private static RecipeDefinition ParseRecipe(
        ContentPackageManifest manifest,
        ContentDefinition definition)
    {
        var payload = Deserialize<RecipePayload>(definition.PayloadJson!, definition);
        if (!string.Equals(payload.Schema, "recipe/v1", StringComparison.Ordinal) ||
            payload.Inputs is null || payload.Outputs is null || payload.Tags is null)
        {
            throw new InvalidDataException(
                $"Recipe definition '{definition.CanonicalId(manifest.PackageDigest)}' has an invalid typed payload.");
        }

        return new RecipeDefinition(
            manifest.PackageDigest,
            definition.LocalId,
            definition.Version,
            definition.DisplayName,
            payload.Inputs.Select(quantity => new ContentQuantity(quantity.ResourceId, quantity.Amount)),
            payload.Outputs.Select(quantity => new ContentQuantity(quantity.ResourceId, quantity.Amount)),
            payload.DurationTicks,
            payload.WorkstationBuildingId,
            payload.Tags,
            definition.PayloadDigest);
    }

    private static T Deserialize<T>(string payloadJson, ContentDefinition definition)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(payloadJson, PayloadJsonOptions)
                ?? throw new InvalidDataException("The typed content payload cannot be null.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"Content definition '{definition.LocalId}' has malformed typed payload JSON.",
                exception);
        }
    }

    public sealed record BuildingPayload(
        string Schema,
        int Width,
        int Height,
        int Capacity,
        QuantityPayload[]? BuildCosts,
        string[]? Tags);

    public sealed record RecipePayload(
        string Schema,
        QuantityPayload[]? Inputs,
        QuantityPayload[]? Outputs,
        int DurationTicks,
        string? WorkstationBuildingId,
        string[]? Tags);

    public sealed record QuantityPayload(string ResourceId, int Amount);
}

public static class ContentDefinitionRules
{
    public const int MaxBuildingDimension = 32;
    public const int MaxCapacity = 100_000;
    public const int MaxDurationTicks = 1_000_000;
    public const int MaxQuantity = 1_000_000;
    public const int MaxListEntries = 64;
    public const int MaxTagLength = 32;

    internal static IReadOnlyList<ContentQuantity> CopyQuantities(IEnumerable<ContentQuantity>? values)
    {
        var copy = (values ?? []).OrderBy(value => value.ResourceId, StringComparer.Ordinal).ToArray();
        return new ReadOnlyCollection<ContentQuantity>(copy);
    }

    internal static IReadOnlyList<string> CopyTags(IEnumerable<string>? values)
    {
        var copy = (values ?? []).Order(StringComparer.Ordinal).ToArray();
        return new ReadOnlyCollection<string>(copy);
    }

    internal static void ValidateIdentity(
        string packageDigest,
        string localId,
        ContentVersion version)
    {
        ContentPackageRules.ValidateDigest(packageDigest, nameof(packageDigest));
        ContentPackageRules.ValidateLocalId(localId);
        if (version.Major < 0 || version.Minor < 0 || version.Patch < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(version), version, "Content versions cannot contain negative components.");
        }
    }

    internal static void ValidateDisplayName(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value != value.Trim() || value.Length > 96 || value.Any(char.IsControl))
        {
            throw new ArgumentException(
                "Display names must be trimmed, printable, and at most 96 characters.",
                nameof(value));
        }
    }

    internal static void ValidateQuantities(
        IReadOnlyList<ContentQuantity> values,
        string parameterName,
        bool allowEmpty)
    {
        ArgumentNullException.ThrowIfNull(values, parameterName);
        if (!allowEmpty && values.Count == 0)
        {
            throw new ArgumentException("At least one content quantity is required.", parameterName);
        }

        if (values.Count > MaxListEntries)
        {
            throw new ArgumentException($"A content quantity list cannot exceed {MaxListEntries} entries.", parameterName);
        }

        string? previousResourceId = null;
        foreach (var value in values)
        {
            value.Validate();
            if (previousResourceId is not null &&
                string.Compare(previousResourceId, value.ResourceId, StringComparison.Ordinal) >= 0)
            {
                throw new ArgumentException(
                    $"{parameterName} must be sorted by unique resource ID.",
                    parameterName);
            }

            previousResourceId = value.ResourceId;
        }
    }

    internal static void ValidateTags(IReadOnlyList<string> values, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(values, parameterName);
        if (values.Count > MaxListEntries)
        {
            throw new ArgumentException($"A tag list cannot exceed {MaxListEntries} entries.", parameterName);
        }

        string? previousTag = null;
        foreach (var tag in values)
        {
            ContentPackageRules.ValidateLocalId(tag);
            if (tag.Length > MaxTagLength ||
                (previousTag is not null &&
                    string.Compare(previousTag, tag, StringComparison.Ordinal) >= 0))
            {
                throw new ArgumentException(
                    $"{parameterName} must be sorted, unique, and use tags of at most {MaxTagLength} characters.",
                    parameterName);
            }

            previousTag = tag;
        }
    }

    internal static void ValidatePayloadDigest(string actual, string expected)
    {
        ContentPackageRules.ValidateDigest(actual, nameof(actual));
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The definition payload digest does not match its canonical payload.");
        }
    }

    internal static void ValidateBuildingReference(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var firstSlash = value.IndexOf('/');
        var secondSlash = firstSlash < 0 ? -1 : value.IndexOf('/', firstSlash + 1);
        var at = value.LastIndexOf('@');
        if (firstSlash <= 0 || secondSlash <= firstSlash + 1 || at <= secondSlash + 1 || at == value.Length - 1)
        {
            throw new ArgumentException(
                $"Building reference '{value}' is not a canonical content definition ID.",
                nameof(value));
        }

        var packageDigest = value[..firstSlash];
        var kind = value[(firstSlash + 1)..secondSlash];
        var localId = value[(secondSlash + 1)..at];
        ContentVersion version;
        try
        {
            version = ContentVersion.Parse(value[(at + 1)..]);
        }
        catch (FormatException exception)
        {
            throw new ArgumentException($"Building reference '{value}' has an invalid version.", nameof(value), exception);
        }

        var canonical = ContentPackageRules.CanonicalDefinitionId(packageDigest, kind, localId, version);
        if (!string.Equals(kind, BuildingDefinition.SchemaKind, StringComparison.Ordinal) ||
            !string.Equals(value, canonical, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Building reference '{value}' is not a canonical building definition ID.",
                nameof(value));
        }
    }

    internal static string ComputeBuildingPayloadDigest(
        string packageDigest,
        string localId,
        ContentVersion version,
        string displayName,
        int width,
        int height,
        int capacity,
        IReadOnlyList<ContentQuantity> buildCosts,
        IReadOnlyList<string> tags) =>
        ComputeDigest(BuildingPayload(
            packageDigest,
            localId,
            version,
            displayName,
            width,
            height,
            capacity,
            buildCosts,
            tags));

    internal static string ComputeRecipePayloadDigest(
        string packageDigest,
        string localId,
        ContentVersion version,
        string displayName,
        IReadOnlyList<ContentQuantity> inputs,
        IReadOnlyList<ContentQuantity> outputs,
        int durationTicks,
        string? workstationBuildingId,
        IReadOnlyList<string> tags) =>
        ComputeDigest(RecipePayload(
            packageDigest,
            localId,
            version,
            displayName,
            inputs,
            outputs,
            durationTicks,
            workstationBuildingId,
            tags));

    internal static string ComputeStateDigest(
        IReadOnlyList<BuildingDefinition> buildings,
        IReadOnlyList<RecipeDefinition> recipes)
    {
        var builder = new StringBuilder("clankerworld-world-content.v1");
        foreach (var building in buildings.OrderBy(item => item.CanonicalId, StringComparer.Ordinal))
        {
            AppendValue(builder, "building");
            AppendValue(builder, building.CanonicalId);
            AppendValue(builder, building.PayloadDigest);
        }

        foreach (var recipe in recipes.OrderBy(item => item.CanonicalId, StringComparer.Ordinal))
        {
            AppendValue(builder, "recipe");
            AppendValue(builder, recipe.CanonicalId);
            AppendValue(builder, recipe.PayloadDigest);
        }

        return ComputeDigest(builder.ToString());
    }

    private static string BuildingPayload(
        string packageDigest,
        string localId,
        ContentVersion version,
        string displayName,
        int width,
        int height,
        int capacity,
        IReadOnlyList<ContentQuantity> buildCosts,
        IReadOnlyList<string> tags)
    {
        var builder = new StringBuilder("clankerworld-content-definition.v1");
        AppendValue(builder, BuildingDefinition.SchemaKind);
        AppendValue(builder, packageDigest);
        AppendValue(builder, localId);
        AppendValue(builder, version.ToString());
        AppendValue(builder, displayName);
        AppendValue(builder, width.ToString(CultureInfo.InvariantCulture));
        AppendValue(builder, height.ToString(CultureInfo.InvariantCulture));
        AppendValue(builder, capacity.ToString(CultureInfo.InvariantCulture));
        AppendQuantities(builder, buildCosts);
        AppendStrings(builder, tags);
        return builder.ToString();
    }

    private static string RecipePayload(
        string packageDigest,
        string localId,
        ContentVersion version,
        string displayName,
        IReadOnlyList<ContentQuantity> inputs,
        IReadOnlyList<ContentQuantity> outputs,
        int durationTicks,
        string? workstationBuildingId,
        IReadOnlyList<string> tags)
    {
        var builder = new StringBuilder("clankerworld-content-definition.v1");
        AppendValue(builder, RecipeDefinition.SchemaKind);
        AppendValue(builder, packageDigest);
        AppendValue(builder, localId);
        AppendValue(builder, version.ToString());
        AppendValue(builder, displayName);
        AppendQuantities(builder, inputs);
        AppendQuantities(builder, outputs);
        AppendValue(builder, durationTicks.ToString(CultureInfo.InvariantCulture));
        AppendValue(builder, workstationBuildingId);
        AppendStrings(builder, tags);
        return builder.ToString();
    }

    private static void AppendQuantities(StringBuilder builder, IEnumerable<ContentQuantity> values)
    {
        foreach (var value in values)
        {
            AppendValue(builder, value.ResourceId);
            AppendValue(builder, value.Amount.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static void AppendStrings(StringBuilder builder, IEnumerable<string> values)
    {
        foreach (var value in values)
        {
            AppendValue(builder, value);
        }
    }

    private static void AppendValue(StringBuilder builder, string? value)
    {
        builder.Append('|');
        if (value is null)
        {
            builder.Append("-1:");
            return;
        }

        builder
            .Append(value.Length.ToString(CultureInfo.InvariantCulture))
            .Append(':')
            .Append(value);
    }

    private static string ComputeDigest(string canonicalPayload) =>
        $"sha256:{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalPayload)))}";
}

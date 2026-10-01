using System.Text.Json.Nodes;

namespace ClankerWorld.Simulation.Cognition;

/// <summary>How a decision adapter describes the agent's needs to its model.</summary>
public enum ModelNeedFormat
{
    /// <summary>Exact 0–10,000 values, with each scale explained in the instructions.</summary>
    Numbers,

    /// <summary>One agreed word per need plus its whole scale, worst to best, and no exact value.</summary>
    Words,
}

/// <summary>
/// The agreed need words from #646. Fullness and warmth follow the survival
/// comfort and urgency references (40%/20% fullness, 60%/35% warmth), with
/// "full" from 70%. Illness follows the cut points where it slows work and
/// travel (25%, 50% and 75%). The words are model-facing only: the
/// simulation and admission still use the exact values.
/// </summary>
public static class ModelNeedWords
{
    /// <summary>
    /// Requests keep today's numbers until the words have been compared with
    /// them against a real model, as the #646 decision requires.
    /// </summary>
    public const ModelNeedFormat DefaultFormat = ModelNeedFormat.Numbers;

    private static readonly string[] FullnessScale = ["starving", "hungry", "fine", "full"];
    private static readonly string[] WarmthScale = ["freezing", "chilly", "warm"];
    private static readonly string[] IllnessScale = ["very ill", "ill", "unwell", "well"];

    public static string FullnessLevel(int fullnessBasisPoints) => fullnessBasisPoints switch
    {
        >= 7_000 => "full",
        >= 4_000 => "fine",
        >= 2_000 => "hungry",
        _ => "starving",
    };

    public static string WarmthLevel(int warmthBasisPoints) => warmthBasisPoints switch
    {
        >= 6_000 => "warm",
        >= 3_500 => "chilly",
        _ => "freezing",
    };

    public static string IllnessLevel(int illnessBasisPoints) => illnessBasisPoints switch
    {
        < 2_500 => "well",
        < 5_000 => "unwell",
        < 7_500 => "ill",
        _ => "very ill",
    };

    /// <summary>For example "hungry (starving, hungry, fine, full; starving is worst, full is best)".</summary>
    public static string Fullness(int fullnessBasisPoints) => Describe(FullnessLevel(fullnessBasisPoints), FullnessScale);

    public static string Warmth(int warmthBasisPoints) => Describe(WarmthLevel(warmthBasisPoints), WarmthScale);

    public static string Illness(int illnessBasisPoints) => Describe(IllnessLevel(illnessBasisPoints), IllnessScale);

    /// <summary>
    /// Replaces an exact need field with its words at the same position, so
    /// both formats send the same request apart from how needs are shown. An
    /// unknown need stays null under its new name.
    /// </summary>
    internal static void ReplaceNumber(JsonObject request, string numberField, string wordsField, string? words)
    {
        var index = request.IndexOf(numberField);
        if (index < 0)
            throw new InvalidOperationException($"The request has no {numberField} field to describe in words.");
        request.SetAt(index, wordsField, words is null ? null : JsonValue.Create(words));
    }

    private static string Describe(string level, string[] scale) =>
        $"{level} ({string.Join(", ", scale)}; {scale[0]} is worst, {scale[^1]} is best)";
}

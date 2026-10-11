using System.Globalization;
using System.Text.RegularExpressions;

namespace ClankerWorld.GodotClient.Launcher;

/// <summary>
/// A game version such as <c>0.1.0-alpha.1</c>, ordered by Semantic Versioning
/// so the newest release sorts first. It is also the name of the folder the
/// version is installed in, so only safe characters are accepted.
/// </summary>
public sealed partial record GameVersionName(int Major, int Minor, int Patch, string? PreRelease)
    : IComparable<GameVersionName>
{
    public static GameVersionName? Parse(string? text)
    {
        if (text is null) return null;
        var match = VersionPattern().Match(text);
        if (!match.Success) return null;
        if (!int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var major) ||
            !int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var minor) ||
            !int.TryParse(match.Groups[3].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var patch))
            return null;
        return new GameVersionName(major, minor, patch, match.Groups[4].Success ? match.Groups[4].Value : null);
    }

    public bool IsPreRelease => PreRelease is not null;

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}") +
        (PreRelease is null ? "" : "-" + PreRelease);

    public int CompareTo(GameVersionName? other)
    {
        if (other is null) return 1;
        var core = (Major, Minor, Patch).CompareTo((other.Major, other.Minor, other.Patch));
        if (core != 0) return core;
        // A release outranks its own pre-releases.
        if (PreRelease is null || other.PreRelease is null)
            return PreRelease is null ? (other.PreRelease is null ? 0 : 1) : -1;
        var mine = PreRelease.Split('.');
        var theirs = other.PreRelease.Split('.');
        for (var i = 0; i < Math.Min(mine.Length, theirs.Length); i++)
        {
            var mineNumeric = int.TryParse(mine[i], NumberStyles.None, CultureInfo.InvariantCulture, out var a);
            var theirsNumeric = int.TryParse(theirs[i], NumberStyles.None, CultureInfo.InvariantCulture, out var b);
            var order = (mineNumeric, theirsNumeric) switch
            {
                (true, true) => a.CompareTo(b),
                (true, false) => -1,
                (false, true) => 1,
                _ => string.CompareOrdinal(mine[i], theirs[i]),
            };
            if (order != 0) return order;
        }
        return mine.Length.CompareTo(theirs.Length);
    }

    public static bool operator <(GameVersionName? left, GameVersionName? right) => Compare(left, right) < 0;
    public static bool operator <=(GameVersionName? left, GameVersionName? right) => Compare(left, right) <= 0;
    public static bool operator >(GameVersionName? left, GameVersionName? right) => Compare(left, right) > 0;
    public static bool operator >=(GameVersionName? left, GameVersionName? right) => Compare(left, right) >= 0;

    private static int Compare(GameVersionName? left, GameVersionName? right) =>
        left is null ? (right is null ? 0 : -1) : left.CompareTo(right);

    [GeneratedRegex(@"^(0|[1-9][0-9]{0,8})\.(0|[1-9][0-9]{0,8})\.(0|[1-9][0-9]{0,8})(?:-([0-9A-Za-z]+(?:\.[0-9A-Za-z]+){0,7}))?$",
        RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();
}

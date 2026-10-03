using System.Reflection;

namespace ClankerWorld;

/// <summary>The version and source revision embedded in this executable's assembly.</summary>
public static class BuildInformation
{
    private static readonly string InformationalVersion = typeof(BuildInformation).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
    private static readonly int RevisionSeparator = InformationalVersion.IndexOf('+');

    public static string Version { get; } = RevisionSeparator < 0
        ? InformationalVersion : InformationalVersion[..RevisionSeparator];

    public static string SourceRevision { get; } = RevisionSeparator < 0
        ? "unknown" : InformationalVersion[(RevisionSeparator + 1)..];

    public static string Display { get; } = $"Build {Version}+{SourceRevision[..Math.Min(7, SourceRevision.Length)]}";
}

// Only the two palette facts the icon code reads: the theme's name and ink.
using Godot;

namespace ClankerWorld.GodotClient.UI;

public sealed record UiPalette(string Name, Color Ink);

public static class UiTheme
{
    public static readonly UiPalette Light = new("light", new Color("33261A"));
    public static readonly UiPalette Dark = new("dark", new Color("EADFC4"));
    public static UiPalette Current { get; set; } = Light;
}

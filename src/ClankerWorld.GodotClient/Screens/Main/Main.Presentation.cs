using ClankerWorld.GodotClient.ClientState;
using ClankerWorld.GodotClient.Pairing;
using ClankerWorld.GodotClient.UI;
using Godot;
using System.Globalization;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private void SetStatus(string text, bool good, StatusToastKind kind = StatusToastKind.Message)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            statusToast.Hide();
            return;
        }

        statusLabel.Text = text;
        statusLabel.ThemeTypeVariation = good ? "GoodLabel" : "BadLabel";
        statusToastKind = kind;
        statusToastShownAtMsec = (long)Time.GetTicksMsec();
        statusToast.Show();
        ApplyResponsiveLayout();
    }

    /// <summary>
    /// Runs on every one-second pulse and after each successful observation
    /// refresh, which must not erase an action result before the player can
    /// read it. Connection and usage-limit notices clear once a refresh
    /// succeeds without them.
    /// </summary>
    private void ExpireStatusToast(bool refreshSucceeded)
    {
        if (!statusToast.Visible) return;
        var expired = statusToastKind switch
        {
            StatusToastKind.Connection or StatusToastKind.UsageLimit => refreshSucceeded,
            // A mode instruction outlives its mode only as an ordinary message.
            StatusToastKind.Sticky when choosingFirstTownSite || movingFounderId is not null => false,
            _ => (long)Time.GetTicksMsec() - statusToastShownAtMsec >= StatusToastMilliseconds,
        };
        if (expired) statusToast.Hide();
    }

    private void ShowHeldState(string reason)
    {
        var heldTick = observationSession.Current?.Baseline.Snapshot.WorldTick;
        SetStatus(
            heldTick is not { } tick
                ? $"Connection lost · {reason}"
                : $"Connection lost · showing the world as of {DisplayWorldClock(tick)} · {reason}",
            good: false, StatusToastKind.Connection);
    }

    private static string FriendlyFailure(Exception exception)
    {
        // Keep diagnostics bounded and separate; exception messages can contain
        // private paths, server payloads or credentials.
        GD.PushWarning($"owner_action_failed type={exception.GetType().Name}");
        return GameUiText.FriendlyFailure(exception);
    }

    private static string DescribeWorldEvent(OwnerWorldEvent worldEvent, OwnerWorldSnapshot? snapshot) =>
        WorldEventText.Describe(worldEvent, snapshot);

    private static string PositionKey(OwnerWorldPosition position) => $"{position.X},{position.Y}";

    private static string ResourceGlyph(string kind, string? naturalObjectKind) => naturalObjectKind switch
    {
        "berry_bush" => "●",
        "wild_greens" => "❧",
        "fiber_plant" => "♧",
        "reeds" => "≋",
        "stone_outcrop" => "⬟",
        "iron_outcrop" => "⬣",
        "gold_outcrop" => "◆",
        "diamond_outcrop" => "◇",
        "clay_bank" => "▰",
        "wild_seed_patch" => "✦",
        "fertile_soil" => "▤",
        _ => ResourceGlyph(kind),
    };

    private static string ResourceGlyph(string kind) => kind switch
    {
        "food" => "●",
        "construction" => "▰",
        "stone" => "⬟",
        "fiber" => "♧",
        "seed" => "✦",
        _ => "◆",
    };

    // Objects drawn as camp art are named like buildings; the rest keep a
    // short uppercase marker beside their symbol.
    private static string ObjectMarker(string kind) => kind switch
    {
        "campfire" or "cooking" => "Campfire",
        "bedroll" => "Bedroll",
        "shelter" => "Shelter",
        "storage" => "Storage",
        "workshop" => "Workshop",
        "path" => "Path",
        "tree" => "TREE",
        _ => ShortMarker(kind),
    };

    private static string ObjectGlyph(string kind) => kind switch
    {
        "campfire" => "✦",
        "shelter" => "⌂",
        "tree" => "♣",
        _ => "■",
    };

    private static string ShortMarker(string value)
    {
        var compact = value.Trim().Replace('_', ' ');
        return compact.Length <= 6 ? compact.ToUpperInvariant() : $"{compact[..5].ToUpperInvariant()}…";
    }

    private static string Pretty(string value) => string.IsNullOrWhiteSpace(value)
        ? "unknown"
        : string.Join(' ', value.Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Length == 1
                ? part.ToUpperInvariant()
                : char.ToUpperInvariant(part[0]) + part[1..].ToLowerInvariant()));

    private static int NeedPercent(int basisPoints) => Math.Clamp(basisPoints / 100, 0, 100);
}

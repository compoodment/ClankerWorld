using ClankerWorld.GodotClient.ClientState;
using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    /// <summary>
    /// Season dates are the default; Game Settings offers them beside the three
    /// numeric orders and applies and remembers the choice at once; the top bar
    /// names the season only once; and a long season date still fits the top
    /// bar at narrow widths.
    /// </summary>
    private async Task VerifySeasonDatesAsync(OwnerWorldSnapshot sample)
    {
        var installed = displayPreferences;
        var previousObservation = observationSession.Current;
        var previousEvents = knownEvents.Values.ToArray();
        // Narrow widths are drawn by changing only the render size. The game
        // would match it back to the headless window at once, so that stays
        // off here, and the earlier checks' render size is put back after.
        var window = GetWindow();
        var originalRenderSize = window.ContentScaleSize;
        window.SizeChanged -= RefreshRenderSize;
        // Year 99, day 30 of the year (the last day of Autumn) at 12:00: the
        // widest date a season-and-day calendar normally shows.
        var autumn = sample with
        {
            WorldTick = 98 * 14_400 + 29 * 360 + 180 - 90,
            CalendarPace = new OwnerWorldCalendarPace(360, 40, 10, 10, 10, 10, CalendarOffsetTicks: 90),
            Authoring = new OwnerWorldAuthoringState(false, 0, 0, 0, "ui-map", "ui-map", "cloudy", "autumn", []),
        };
        var handshake = new OwnerWorldHandshake(new(1, 1),
            ["owner-observation.read.v1", "inhabitant-inspection.read.v1", "spatial-knowledge.read.v1",
             "owner-control.request.v1", "paused-authoring.request.v1"], []);
        try
        {
            if (new GameDisplayPreferences().DateStyle != GameUiText.SeasonDates ||
                dateFormatChoice.ItemCount != 4 || dateFormatChoice.GetItemText(0) != "Season (Autumn 2, Year 1)" ||
                dateFormatChoice.GetItemText(1) != "DD-MM-YYYY" || dateFormatChoice.GetItemText(2) != "MM-DD-YYYY" ||
                dateFormatChoice.GetItemText(3) != "YYYY-MM-DD")
                throw new InvalidOperationException("A new installation must show season dates, and Game Settings must offer them first, then DD-MM-YYYY, MM-DD-YYYY and YYYY-MM-DD.");

            // Raw tick zero is morning in a newly created world. The same
            // saved offset dates old events after the live clock passes midnight.
            displayPreferences = installed with { DateStyle = GameUiText.SeasonDates, UseTwelveHourClock = false };
            OwnerWorldEvent[] firstMorningEvents = [new(1, 0, "town_founded", "town:first")];
            var morning = sample with
            {
                WorldTick = 0,
                LatestEventId = 1,
                CalendarPace = autumn.CalendarPace,
                DarknessBasisPoints = 0,
            };
            foreach (var (snapshot, expectedClock, expectedDarkness) in new[]
                     {
                         (morning, "Spring 1, Year 1 · 06:00", 0f),
                         (morning with { WorldTick = 270, DarknessBasisPoints = 10_000 }, "Spring 2, Year 1 · 00:00", 1f),
                     })
            {
                observationSession.ResetAfterLoad();
                if (!observationSession.TryAccept(new(handshake, new(snapshot, new(snapshot.WorldTick, 0, firstMorningEvents))), 0, out var morningFailure))
                    throw new InvalidOperationException("Morning observation fixture was refused: " + morningFailure);
                knownEvents.Clear();
                Render(snapshot, firstMorningEvents);
                nightLayer.Settle();
                var history = eventLog.GetParsedText();
                if (clockLabel.Text != expectedClock || nightLayer.ShownDarkness != expectedDarkness ||
                    !history.Contains("Spring 1, Year 1", StringComparison.Ordinal) ||
                    !history.Contains("06:00", StringComparison.Ordinal) || history.Contains("12:00", StringComparison.Ordinal))
                    throw new InvalidOperationException($"The live clock and daylight must advance while raw tick-zero history stays at 06:00: clock={clockLabel.Text}, darkness={nightLayer.ShownDarkness}, history={history}.");
            }
            knownEvents.Clear();

            // A choice in Settings redraws the world at once and is saved.
            observationSession.ResetAfterLoad();
            if (!observationSession.TryAccept(new(handshake, new(autumn, new(autumn.WorldTick, 0, []))), 0, out var failure))
                throw new InvalidOperationException("Season date observation fixture was refused: " + failure);
            displayPreferences = installed with { DateStyle = new GameDisplayPreferences().DateStyle, UseTwelveHourClock = false };
            Render(autumn, []);
            if (clockLabel.Text != "Autumn 10, Year 99 · 12:00")
                throw new InvalidOperationException($"New installations must show the season date on the top bar: {clockLabel.Text}");
            foreach (var (index, style, twelveHour, expected) in new[]
                     {
                         (1, "dmy", false, "10-03-0099 · 12:00"),
                         (2, "mdy", true, "03-10-0099 · 12:00 PM"),
                         (3, "ymd", false, "0099-03-10 · 12:00"),
                         (0, GameUiText.SeasonDates, true, "Autumn 10, Year 99 · 12:00 PM"),
                     })
            {
                clockFormatChoice.Select(twelveHour ? 1 : 0);
                clockFormatChoice.EmitSignal(OptionButton.SignalName.ItemSelected, twelveHour ? 1L : 0L);
                dateFormatChoice.Select(index);
                dateFormatChoice.EmitSignal(OptionButton.SignalName.ItemSelected, (long)index);
                var saved = displayPreferencesStore.Load();
                if (clockLabel.Text != expected || displayPreferences.DateStyle != style ||
                    saved.DateStyle != style || saved.UseTwelveHourClock != twelveHour ||
                    !PageText(worldStatsPage).Contains(expected, StringComparison.Ordinal))
                    throw new InvalidOperationException($"Choosing {dateFormatChoice.GetItemText(index)} must change every full date at once and be remembered: top bar {clockLabel.Text}, saved {saved.DateStyle}.");
            }

            // The season date names the season, so the top bar shows only the
            // weather beside it; a numeric date keeps the season beside it.
            foreach (var size in new[] { new Vector2I(1280, 720), new Vector2I(1100, 620), new Vector2I(960, 540) })
            {
                foreach (var style in new[] { GameUiText.SeasonDates, "dmy" })
                {
                    displayPreferences = displayPreferences with { DateStyle = style, UseTwelveHourClock = true };
                    window.ContentScaleSize = size;
                    for (var frame = 0; frame < 3; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    Render(autumn, []);
                    ApplyResponsiveLayout();
                    for (var frame = 0; frame < 2; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    if (UiSize != new Vector2(size.X, size.Y))
                        throw new InvalidOperationException($"The season date check must lay the interface out at {size}, not {UiSize}.");
                    var seasonDate = style == GameUiText.SeasonDates;
                    var climateShown = UiSize.X >= 1100;
                    if (clockLabel.Text != (seasonDate ? "Autumn 10, Year 99 · 12:00 PM" : "10-03-0099 · 12:00 PM") ||
                        climateBox.IsVisibleInTree() != climateShown ||
                        (climateShown && (seasonLabel.IsVisibleInTree() == seasonDate || seasonIcon.IsVisibleInTree() == seasonDate ||
                            !weatherLabel.IsVisibleInTree() || weatherLabel.Text != "Cloudy")))
                        throw new InvalidOperationException($"The top bar must name the season once at {size} with {style} dates: clock={clockLabel.Text}, season shown={seasonLabel.IsVisibleInTree()}, weather={weatherLabel.Text}.");
                    if (PageText(worldStatsPage).Contains("Autumn · Cloudy", StringComparison.Ordinal) == seasonDate)
                        throw new InvalidOperationException($"World Info must not repeat the season after a season date: {PageText(worldStatsPage).ReplaceLineEndings(" / ")}");
                    var hudGroups = new[] { hudLeft, hudTime, hudRight }.Select(row => row.GetParent<Control>().GetGlobalRect()).ToArray();
                    if (hudGroups.Any(rect => !mapCanvas.GetGlobalRect().Encloses(rect)) ||
                        hudGroups[0].Intersects(hudGroups[1]) || hudGroups[1].Intersects(hudGroups[2]))
                        throw new InvalidOperationException($"The top bar must fit a long {style} date at {size} without overlapping: map={mapCanvas.GetGlobalRect()} groups={string.Join(' ', hudGroups)}.");
                }
            }
        }
        finally
        {
            window.ContentScaleSize = originalRenderSize;
            SaveDisplayPreferences(installed);
            clockFormatChoice.Select(installed.UseTwelveHourClock ? 1 : 0);
            dateFormatChoice.Select(DateStyleIndex(installed.DateStyle));
            observationSession.ResetAfterLoad();
            if (previousObservation is not null)
                observationSession.TryAccept(previousObservation, previousObservation.Baseline.Events.AfterEventId, out _);
            knownEvents.Clear();
            foreach (var worldEvent in previousEvents) knownEvents[worldEvent.EventId] = worldEvent;
            for (var frame = 0; frame < 3; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            window.SizeChanged += RefreshRenderSize;
            Render(sample, []);
            ApplyResponsiveLayout();
        }
    }
}

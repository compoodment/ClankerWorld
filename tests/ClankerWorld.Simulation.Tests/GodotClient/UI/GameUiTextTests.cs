using ClankerWorld.GodotClient.ClientState;
using ClankerWorld.GodotClient.UI;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class GameUiTextTests
{
    [Theory]
    [InlineData("future_internal_diagnostic", false)]
    [InlineData("saved_road_footprints_repaired", false)]
    [InlineData("tick_advanced", false)]
    [InlineData("town_resident_joined", true)]
    [InlineData("child_born", true)]
    [InlineData("build_completed", true)]
    [InlineData("world_started", true)]
    [InlineData("partnership_accepted", true)]
    [InlineData("partnership_ended", true)]
    [InlineData("caregiver_assigned", true)]
    [InlineData("council_policy_adopted", true)]
    [InlineData("settlement_trade_completed", true)]
    [InlineData("paused", true)]
    [InlineData("instruction_not_understood", true)]
    [InlineData("instruction_applied", false)]
    public void EventLogSelectsKnownPlayerEventsInsteadOfPublishingUnknownDiagnostics(string kind, bool visible)
    {
        Assert.Equal(visible, GameUiText.IsPlayerFacingEvent(kind));
    }

    private static readonly int[] UiScalePercentages = [DisplayUiScalePolicy.Automatic, 100, 200, 300, 400];

    [Theory]
    [InlineData("Alexandria Smith", "Alexandria")]
    [InlineData("  Mira   Rowan  ", "Mira")]
    [InlineData("Alexandriannnnnnnn", "A.")]
    [InlineData("Álexandriannnnnnnn", "Á.")]
    [InlineData("李 小龙", "李")]
    [InlineData("  ", "?")]
    public void MapNamesUseWholeGivenNamesOrWholeTextElementInitials(string fullName, string expected) =>
        Assert.Equal(expected, GameUiText.ActorMapLabel(fullName));

    [Fact]
    public void PlayerFailuresDescribeRecoveryWithoutExposingRawExceptionText()
    {
        const string secret = "sk-private /home/private/save provider-response";
        Exception[] failures = [new InvalidOperationException(secret), new IOException(secret),
            new UnauthorizedAccessException(secret), new ArgumentException(secret),
            new System.Text.Json.JsonException(secret), new System.Security.Cryptography.CryptographicException(secret),
            new HttpRequestException(secret, null, System.Net.HttpStatusCode.Forbidden),
            new TaskCanceledException(secret)];
        foreach (var failure in failures)
        {
            var message = GameUiText.FriendlyFailure(failure);
            Assert.DoesNotContain(secret, message, StringComparison.Ordinal);
            Assert.DoesNotContain(failure.GetType().Name, message, StringComparison.Ordinal);
            Assert.NotEmpty(message);
        }
        Assert.Contains("connecting", GameUiText.FriendlyFailure(failures[6]), StringComparison.Ordinal);
        Assert.Contains("storage", GameUiText.FriendlyFailure(failures[1]), StringComparison.Ordinal);
    }

    [Fact]
    public void HouseholdDisplayCleanupPreservesSavedMembershipAndCustomNames()
    {
        using var world = new PrivateWorldRuntime("household-display");
        var state = world.ExportState();
        Assert.Equal("First household", Assert.Single(state.Society.Society.Households).Name);
        foreach (var name in new[] { "Camp Alpha", "River family" })
        {
            var legacy = state with
            {
                Society = state.Society with
                {
                    Society = state.Society.Society with
                    {
                        Households = state.Society.Society.Households.Select(home => home with { Name = name }).ToArray(),
                    },
                },
            };
            using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(legacy)));
            var before = PrivateWorldRuntimeCodec.Encode(restored.ExportState());
            var stockpile = Assert.Single(new OwnerWorldObservationStore(restored).GetSnapshot().Stockpiles);
            Assert.Equal(name == "Camp Alpha" ? "First household" : name, stockpile.Name);
            Assert.Equal("household:camp-alpha", stockpile.OwnerId);
            Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
            Assert.Equal(state.Society.Society.Households[0].MemberIds,
                restored.ExportState().Society.Society.Households[0].MemberIds);
        }
    }

    [Fact]
    public void OwnerSnapshotReportsTheSavedWorldCalendarPace()
    {
        using var world = new PrivateWorldRuntime("calendar-projection");
        var saved = world.ExportState();
        var pace = new OwnerWorldObservationStore(world).GetSnapshot().CalendarPace;
        Assert.NotNull(pace);
        Assert.Equal(saved.WorldSystems!.Config.TicksPerDay, pace.TicksPerDay);
        Assert.Equal(saved.WorldSystems.Config.DaysPerYear, pace.DaysPerYear);
    }

    [Fact]
    public void ResourceHelpDistinguishesExhaustionRegrowthAndLegacyUnknownQuantities()
    {
        var wood = new OwnerWorldResource("wood", "construction", new(0, 0), false, "depleted", 0, 12, 0, 0, "spring");
        Assert.Equal("Wild timber · 0/12\nDepleted\nDoes not grow back.", GameUiText.ResourceTooltip(wood));
        var berries = wood with { Id = "food", Kind = "food", IsRenewable = true, RegenerationAmount = 4, RegenerationIntervalDays = 1 };
        Assert.Contains("+4 every 1 day in Spring", GameUiText.ResourceTooltip(berries), StringComparison.Ordinal);
        Assert.Equal("Soil 3/3", GameUiText.ResourceQuantity("fertile_land", 3, 3));
        var legacy = new OwnerWorldResource("old", "food", new(0, 0), true, "available");
        Assert.Contains("Grows back.", GameUiText.ResourceTooltip(legacy), StringComparison.Ordinal);
        Assert.Equal(string.Empty, GameUiText.ResourceMapCaption(legacy, 16));
        Assert.Equal("Food", GameUiText.ResourceMapCaption(legacy, 48));
        Assert.Equal("Wood 0/12", GameUiText.ResourceMapCaption(wood, 48));
        Assert.Equal("Plot Soil 3/3", GameUiText.ResourceMapCaption(wood with { Kind = "fertile_land", Quantity = 3, Capacity = 3 }, 48));
        Assert.DoesNotContain("0/", GameUiText.ResourceTooltip(legacy), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, "01-01-0001 · 00:00")]
    [InlineData(1_440, "02-01-0001 · 00:00")]
    [InlineData(44_640, "01-02-0001 · 00:00")]
    public void WorldClockUsesTheSavedCalendarInsteadOfRawTicks(long worldTick, string expected)
    {
        Assert.Equal(expected, GameUiText.FormatWorldClock(worldTick));
    }

    [Theory]
    [InlineData(0, "01-01-0001 · 12:00 AM")]
    [InlineData(720, "01-01-0001 · 12:00 PM")]
    [InlineData(780, "01-01-0001 · 1:00 PM")]
    [InlineData(1_439, "01-01-0001 · 11:59 PM")]
    public void WorldClockCanUseTwelveHourDisplayWithoutChangingWorldTime(long worldTick, string expected)
    {
        Assert.Equal(expected, GameUiText.FormatWorldClock(worldTick, useTwelveHourClock: true));
    }

    [Fact]
    public void CustomFortyDayYearUsesFourTenDayMonthsAndScalesClockFromWorldTicks()
    {
        var calendar = new OwnerWorldCalendarPace(360, 40);
        Assert.Equal("01-01-0001 · 00:04", GameUiText.FormatWorldClock(1, calendarPace: calendar));
        Assert.Equal("01-02-0001 · 00:00", GameUiText.FormatWorldClock(3_600, calendarPace: calendar));
        Assert.Equal("01-01-0002 · 00:00", GameUiText.FormatWorldClock(14_400, calendarPace: calendar));
        Assert.Equal("02-01-0002 · 12:00 PM", GameUiText.FormatWorldClock(14_940,
            useTwelveHourClock: true, calendarPace: calendar));
        Assert.Equal("01-02-0002 · 12:00", GameUiText.FormatWorldClock(14_940,
            calendarPace: calendar, dateFormat: "mdy"));
        Assert.Equal("0002-01-02 · 12:00", GameUiText.FormatWorldClock(14_940,
            calendarPace: calendar, dateFormat: "ymd"));
    }

    [Fact]
    public void GameClockPreferencePersistsOutsideWorldSave()
    {
        var directory = Path.Combine(Path.GetTempPath(), "clankerworld-display-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new GameDisplayPreferencesStore(Path.Combine(directory, "game-settings.json"));
            Assert.False(store.Load().UseTwelveHourClock);
            store.Save(new GameDisplayPreferences(UseTwelveHourClock: true,
                WindowWidth: 1600, WindowHeight: 900, RenderWidth: 1920, RenderHeight: 1080,
                UiScalePercent: 200, Fullscreen: false));
            var restored = new GameDisplayPreferencesStore(Path.Combine(directory, "game-settings.json")).Load();
            Assert.True(restored.UseTwelveHourClock);
            Assert.Equal((1600, 900), (restored.WindowWidth, restored.WindowHeight));
            Assert.Equal((1920, 1080), (restored.RenderWidth, restored.RenderHeight));
            Assert.False(restored.UsesAutomaticRenderResolution);
            Assert.Equal(200, restored.UiScalePercent);
            Assert.False(restored.UsesFullscreen);
            store.Save(restored with { DateFormat = "ymd" });
            Assert.Equal("ymd", store.Load().DateFormat);
            Assert.Equal((1600, 900), (store.Load().WindowWidth, store.Load().WindowHeight));
            Assert.Equal((1920, 1080), (store.Load().RenderWidth, store.Load().RenderHeight));
            Assert.Equal(200, store.Load().UiScalePercent);
            Assert.False(store.Load().UsesFullscreen);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void UiScaleOffersWholeStepsAndMovesOlderSavedValuesToThem()
    {
        Assert.Equal(UiScalePercentages, DisplayUiScalePolicy.SupportedPercentages);
        Assert.Equal(100, DisplayUiScalePolicy.NormalizePercent(123));
        Assert.Equal(200, DisplayUiScalePolicy.NormalizePercent(150));
        Assert.Equal(200, DisplayUiScalePolicy.NormalizePercent(175));
        Assert.Equal(DisplayUiScalePolicy.Automatic, DisplayUiScalePolicy.NormalizePercent(500));
        Assert.Equal(DisplayUiScalePolicy.Automatic, new GameDisplayPreferences().UiScalePercent);
        Assert.True(new GameDisplayPreferences().UsesFullscreen);

        var directory = Directory.CreateTempSubdirectory("clanker-display-ui-scale-");
        try
        {
            var path = Path.Combine(directory.FullName, "game-settings.json");
            File.WriteAllText(path, "{\"UiScalePercent\":150}");
            var store = new GameDisplayPreferencesStore(path);
            Assert.Equal(200, store.Load().UiScalePercent);
            File.WriteAllText(path, "{\"UiScalePercent\":500}");
            Assert.Equal(DisplayUiScalePolicy.Automatic, store.Load().UiScalePercent);

            store.Save(store.Load() with { UiScalePercent = 300 });
            Assert.Equal(300, store.Load().UiScalePercent);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(1280, 720, 1)]
    [InlineData(1600, 900, 1)]
    [InlineData(1920, 1080, 2)]
    [InlineData(2560, 1440, 2)]
    [InlineData(3840, 2160, 3)]
    public void AutomaticUiScaleKeepsTheInterfaceNear720PixelsTall(int width, int height, int factor) =>
        Assert.Equal(factor, DisplayUiScalePolicy.FittingFactor(DisplayUiScalePolicy.Automatic, width, height));

    [Fact]
    public void ChosenUiScaleDropsToTheLargestStepThatLeavesTheMenusRoom()
    {
        Assert.Equal(2, DisplayUiScalePolicy.FittingFactor(200, 1920, 1080));
        Assert.Equal(1, DisplayUiScalePolicy.FittingFactor(200, 1280, 720));
        Assert.Equal(2, DisplayUiScalePolicy.FittingFactor(400, 2560, 1440));
        Assert.Equal(4, DisplayUiScalePolicy.FittingFactor(400, 3840, 2160));
        Assert.Equal(1, DisplayUiScalePolicy.FittingFactor(100, 3840, 2160));
    }

    [Fact]
    public void AutomaticRenderResolutionFollowsA1440pDisplayAndWindowWithoutHidingExplicitChoices()
    {
        var monitor = new DisplayDimensions(2560, 1440);
        Assert.Equal(monitor, DisplayResolutionPolicy.AutomaticRenderSize(
            monitor, new DisplayDimensions(1600, 900), fullscreen: true));
        Assert.Equal(new DisplayDimensions(1600, 900), DisplayResolutionPolicy.AutomaticRenderSize(
            monitor, new DisplayDimensions(1600, 900), fullscreen: false));
        Assert.Contains(monitor, DisplayResolutionPolicy.FixedRenderSizes(monitor));
        Assert.DoesNotContain(monitor, DisplayResolutionPolicy.FixedRenderSizes(new DisplayDimensions(1920, 1080)));
        Assert.Contains(monitor, DisplayResolutionPolicy.FixedRenderSizes(
            new DisplayDimensions(1920, 1080), saved: monitor));
        Assert.Contains(new DisplayDimensions(1920, 1080), DisplayResolutionPolicy.FixedRenderSizes(
            new DisplayDimensions(1920, 1080), saved: monitor));
    }

    [Fact]
    public void OldDefaultRenderChoiceMigratesToAutomaticWhileExplicitLegacyChoiceStaysFixed()
    {
        var directory = Directory.CreateTempSubdirectory("clanker-display-migration-");
        try
        {
            var path = Path.Combine(directory.FullName, "game-settings.json");
            File.WriteAllText(path, "{\"RenderWidth\":1280,\"RenderHeight\":720}");
            var store = new GameDisplayPreferencesStore(path);
            Assert.True(store.Load().UsesAutomaticRenderResolution);
            store.Save(store.Load() with { AutoRenderResolution = false });
            Assert.False(store.Load().UsesAutomaticRenderResolution);

            File.WriteAllText(path, "{\"RenderWidth\":1920,\"RenderHeight\":1080}");
            Assert.False(store.Load().UsesAutomaticRenderResolution);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("household_membership", "accepted", null, "Member of Camp Alpha")]
    [InlineData("biological_parentage", "accepted", "parent", "Parent of Camp Alpha")]
    [InlineData("biological_parentage", "ended_by_death", "child", "Child of Camp Alpha · ended by death")]
    [InlineData("partnership", "proposed", null, "Partnership with Camp Alpha · proposed")]
    [InlineData("legal_guardian", "accepted", null, "Legal guardian with Camp Alpha")]
    public void RelationshipsReadAsPlainPhrases(string type, string state, string? direction, string expected)
    {
        Assert.Equal(expected, GameUiText.RelationshipSummary(type, state, "Camp Alpha", direction));
    }

    [Theory]
    [InlineData(10_000, "well fed")]
    [InlineData(7_000, "well fed")]
    [InlineData(6_999, "fed")]
    [InlineData(3_499, "hungry")]
    [InlineData(2_499, "very hungry")]
    [InlineData(0, "very hungry")]
    public void FullnessStatesReadLowValuesAsHungry(int fullness, string expected)
    {
        Assert.Equal(expected, GameUiText.FullnessState(fullness));
    }

    [Theory]
    [InlineData("seek_food", "looking for food", "looking for food")]
    [InlineData("build:building:sha256:abcdef/building/stone-hearth@1.0.0", "build:building:sha256:abcdef/building/stone-hearth@1.0.0", "build Stone hearth")]
    [InlineData(null, null, "taking in the surroundings")]
    public void RosterActivitiesReadAsShortPhrases(string? candidate, string? summary, string expected)
    {
        Assert.Equal(expected, GameUiText.ActivityPhrase(candidate, summary));
    }

    [Fact]
    public void PartyNamesUseAgentHouseholdAndTownNamesInsteadOfIds()
    {
        var position = new OwnerWorldPosition(0, 0);
        var agent = new OwnerWorldInhabitant("founder:1", "Rowan", "active", position, 8_000, [], [],
            new OwnerWorldRoute("idle", null, null, [], string.Empty),
            new OwnerWorldSpatialKnowledge(position, [position], [position]), false);
        var snapshot = new OwnerWorldSnapshot("names", 0, "names-map", [], [], [], null, 0)
        {
            Inhabitants = [agent],
            Stockpiles = [new OwnerWorldStockpile("household:camp-alpha", "Camp Alpha", [])],
            Towns = [new OwnerWorldTown("town:first", "First Town", "founding", 0, [], [], [])],
        };
        Assert.Equal("Rowan", GameUiText.PartyName(snapshot, "founder:1"));
        Assert.Equal("Camp Alpha", GameUiText.PartyName(snapshot, "household:camp-alpha"));
        Assert.Equal("First Town", GameUiText.PartyName(snapshot, "town:first"));
        Assert.Equal("a household", GameUiText.PartyName(snapshot, "household:agent:123"));
        Assert.DoesNotContain("household:", GameUiText.PartyName(null, "household:camp-beta"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("build:building:sha256:abcdef/building/stone-hearth@1.0.0", "build Stone hearth")]
    [InlineData("seek_food", "find food")]
    [InlineData("guardian_tend:dependent-42", "look after someone who is ill")]
    [InlineData("trade_propose:offer-1", "offer a trade")]
    [InlineData("council_vote_yes", "vote for a food rule")]
    [InlineData("learn:builder", "ask to be taught a skill")]
    public void InternalIdentifiersBecomeReadablePhrases(string value, string expected)
    {
        Assert.Equal(expected, GameUiText.HumanizeIdentifier(value));
    }
}

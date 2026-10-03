using System.Globalization;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>The newcomer's one unpaused world day to accept an actual sponsored Council approval.</summary>
public static class TownAdmissionDeadlineRules
{
    public static long? AcceptanceDeadline(TownProposal? proposal, int ticksPerDay)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ticksPerDay);
        return proposal is { Kind: "admission", Status: "passed", SubjectId: { } subject, SettledTick: { } settled } &&
            proposal.AuthorId != subject && settled >= 0 && settled <= long.MaxValue - ticksPerDay
                ? settled + ticksPerDay : null;
    }

    public static bool IsOpen(TownProposal? proposal, long worldTick, int ticksPerDay) =>
        AcceptanceDeadline(proposal, ticksPerDay) is { } deadline && proposal!.SettledTick <= worldTick && worldTick < deadline;

    public static bool IsExpired(TownProposal? proposal, long worldTick, int ticksPerDay) =>
        AcceptanceDeadline(proposal, ticksPerDay) is { } deadline && worldTick >= deadline;

    /// <summary>Readable deadline and remaining world time from the same captured clock, without saved state.</summary>
    public static string DescribeWindow(long deadline, long worldTick, int ticksPerDay)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(deadline);
        ArgumentOutOfRangeException.ThrowIfNegative(worldTick);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ticksPerDay);
        // Decimal keeps the one-based day safe even at the largest long-valued
        // deadline. Divide duration before converting to minutes to avoid overflow.
        var dayNumber = (decimal)(deadline / ticksPerDay) + 1;
        var minuteOfDay = (int)((deadline % ticksPerDay) * DaylightRules.ClockMinutesPerDay / ticksPerDay);
        var remaining = deadline > worldTick ? deadline - worldTick : 0;
        var days = remaining / ticksPerDay;
        var minutes = (int)(((remaining % ticksPerDay) * DaylightRules.ClockMinutesPerDay + ticksPerDay - 1) / ticksPerDay);
        if (minutes == DaylightRules.ClockMinutesPerDay)
        {
            days++;
            minutes = 0;
        }
        var parts = new List<string>();
        static string Amount(long count, string unit) => count.ToString(CultureInfo.InvariantCulture) + " " + unit + (count == 1 ? "" : "s");
        if (days > 0) parts.Add(Amount(days, "world day"));
        if (minutes / 60 > 0) parts.Add(Amount(minutes / 60, parts.Count == 0 ? "world hour" : "hour"));
        if (minutes % 60 > 0) parts.Add(Amount(minutes % 60, parts.Count == 0 ? "world minute" : "minute"));
        var duration = parts.Count == 0 ? "no time left" : "about " + string.Join(" ", parts) + " left";
        return FormattableString.Invariant($"world day {dayNumber} at {minuteOfDay / 60:00}:{minuteOfDay % 60:00}; {duration}; paused time does not count");
    }
}

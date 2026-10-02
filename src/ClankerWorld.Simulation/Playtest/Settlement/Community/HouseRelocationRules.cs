using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>
/// Chooses which adults of an overcrowded House receive a move-out notice.
/// Volunteers come first, then the most recently admitted eligible adult who is
/// not part of the House's dominant family (any adult when no family is the
/// majority), with the lower ordinal ID first when arrival times are equal.
/// Only an adult who would leave no dependent behind is eligible: a sole
/// caregiver and their children are never selected by the notice timer.
/// Selection stops as soon as the remaining residents fit the completed
/// footprint, and skips anyone whose leaving would not reduce the crowding.
/// </summary>
public static class HouseRelocationRules
{
    public const string Volunteer = "volunteer";
    public const string LatestUnrelatedArrival = "latest_unrelated_arrival";
    public const string LatestArrival = "latest_arrival";

    public static readonly IReadOnlyList<string> Reasons = [Volunteer, LatestUnrelatedArrival, LatestArrival];

    /// <summary>
    /// An adult resident's facts for selection. <paramref name="MovesAlone"/> is
    /// false while they are primary caregiver of a dependent in the same
    /// household. An existing notice keeps its place in the order.
    /// </summary>
    public sealed record Adult(string Id, long AdmittedTick, bool MovesAlone,
        string? NoticeReason = null, long? NoticeTick = null);

    public sealed record Selection(IReadOnlyList<SelectedAdult> Chosen, HouseResidentCapacityRules.Capacity Remaining)
    {
        public bool Fits => !Remaining.IsOvercrowded;
    }

    public sealed record SelectedAdult(string Id, string Reason);

    public static Selection Select(IEnumerable<SocietyInhabitant> residents, IEnumerable<Adult> adults,
        int footprintWidth, int footprintHeight)
    {
        ArgumentNullException.ThrowIfNull(residents);
        ArgumentNullException.ThrowIfNull(adults);
        var remaining = residents.Where(person => person.Status == SocietyInhabitantStatus.Active).ToList();
        var capacity = HouseResidentCapacityRules.Calculate(remaining, footprintWidth, footprintHeight);
        if (!capacity.IsOvercrowded) return new([], capacity);

        var dominant = DominantFamily(remaining);
        var units = remaining.ToDictionary(person => person.Id, person => person.DomesticFamilyUnitId, StringComparer.Ordinal);
        var known = adults.Where(adult => units.ContainsKey(adult.Id)).ToArray();
        var ordered = known.Where(adult => adult.NoticeReason == Volunteer)
                .OrderBy(adult => adult.NoticeTick).ThenBy(adult => adult.Id, StringComparer.Ordinal)
            .Concat(known.Where(adult => adult.NoticeReason is not null && adult.NoticeReason != Volunteer)
                .OrderBy(adult => adult.NoticeTick).ThenByDescending(adult => adult.AdmittedTick)
                .ThenBy(adult => adult.Id, StringComparer.Ordinal))
            .Concat(known.Where(adult => adult.NoticeReason is null)
                .OrderByDescending(adult => adult.AdmittedTick).ThenBy(adult => adult.Id, StringComparer.Ordinal));
        var chosen = new List<SelectedAdult>();
        foreach (var adult in ordered)
        {
            var volunteer = adult.NoticeReason == Volunteer;
            if (!adult.MovesAlone || !volunteer && dominant is not null && units[adult.Id] == dominant)
                continue;
            var next = remaining.Where(person => person.Id != adult.Id).ToList();
            var after = HouseResidentCapacityRules.Calculate(next, footprintWidth, footprintHeight);
            if (Excess(after) >= Excess(capacity))
                continue;
            chosen.Add(new(adult.Id, adult.NoticeReason ?? (dominant is null ? LatestArrival : LatestUnrelatedArrival)));
            remaining = next;
            capacity = after;
            if (!capacity.IsOvercrowded) break;
        }
        return new(chosen, capacity);
    }

    /// <summary>The one recorded domestic unit with at least two residents and a strict majority, if any.</summary>
    public static string? DominantFamily(IReadOnlyCollection<SocietyInhabitant> residents) => residents
        .Where(person => person.Status == SocietyInhabitantStatus.Active)
        .GroupBy(person => person.DomesticFamilyUnitId ?? string.Empty, StringComparer.Ordinal)
        .Where(unit => unit.Key.Length > 0 && unit.Count() >= 2 && unit.Count() * 2 > residents.Count(person =>
            person.Status == SocietyInhabitantStatus.Active))
        .Select(unit => unit.Key).SingleOrDefault();

    private static int Excess(HouseResidentCapacityRules.Capacity capacity) => capacity.ResidentCount - capacity.Limit;
}

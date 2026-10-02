using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>Resident places are derived from a House's completed footprint and recorded family units.</summary>
public static class HouseResidentCapacityRules
{
    public const int OrdinaryPlacesPerTile = 3;
    public const int DominantFamilyPlacesPerTile = 4;

    public sealed record Capacity(
        int ResidentCount,
        int OrdinaryLimit,
        int FamilyLimit,
        bool HasDominantFamily)
    {
        public int Limit => HasDominantFamily ? FamilyLimit : OrdinaryLimit;
        public bool HasFreePlace => ResidentCount < Limit;
        public bool IsOvercrowded => ResidentCount > Limit;
    }

    public static Capacity Calculate(
        IEnumerable<SocietyInhabitant> residents,
        int footprintWidth,
        int footprintHeight)
    {
        ArgumentNullException.ThrowIfNull(residents);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(footprintWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(footprintHeight);

        var activeResidents = residents
            .Where(person => person.Status == SocietyInhabitantStatus.Active)
            .ToArray();
        if (activeResidents.Any(person => string.IsNullOrWhiteSpace(person.DomesticFamilyUnitId)))
            throw new InvalidDataException("A House resident has no recorded domestic family unit.");

        var tiles = checked(footprintWidth * footprintHeight);
        var count = activeResidents.Length;
        var dominant = activeResidents
            .GroupBy(person => person.DomesticFamilyUnitId!, StringComparer.Ordinal)
            .Any(unit => unit.Count() >= 2 && unit.Count() * 2 > count);
        return new Capacity(
            count,
            checked(tiles * OrdinaryPlacesPerTile),
            checked(tiles * DominantFamilyPlacesPerTile),
            dominant);
    }
}

using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>Checks the saved arrangement, protected government changes and mayor's office.</summary>
internal static class TownGovernmentStateValidation
{
    public static void Validate(TownRuntimeState town, TownGovernmentState state, TownGovernanceState council,
        SocietyCheckpoint society, int day)
    {
        if (state.Arrangement != TownArrangementRules.Initial || state.Changes.Count > 0 || state.Office is not null ||
            state.OfficeHistory.Count > 0 || state.Consents.Count > 0 || state.Contest is not null || state.ContestHistory.Count > 0)
            throw new InvalidDataException("This Town's saved government record is not supported yet.");
    }
}

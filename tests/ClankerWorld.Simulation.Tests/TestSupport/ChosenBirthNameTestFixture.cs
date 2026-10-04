using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Tests;

/// <summary>Gives unrelated family fixtures a legal chosen child name without introducing pending naming work.</summary>
internal static class ChosenBirthNameTestFixture
{
    internal static SocietyCheckpoint NameParent(SocietyCheckpoint state, string parentId)
    {
        var parent = state.GetInhabitant(parentId);
        if (parent.HasChosenName && InhabitantNameRules.SurnameKey(parent.Name) is not null) return state;
        var first = parent.HasChosenName ? InhabitantNameRules.FirstNameKey(parent.Name)!
            : "Parent" + Array.FindIndex(state.Inhabitants.ToArray(), person => person.Id == parentId);
        return SocietyFixture.RenameInhabitant(state, parentId, first + " Vale").Checkpoint;
    }

    internal static string ChildName(SocietyCheckpoint state, string parentId, string firstName) =>
        firstName + " " + (InhabitantNameRules.SurnameKey(state.GetInhabitant(parentId).Name)
            ?? throw new InvalidOperationException("The fixture parent must have a chosen surname before birth."));
}

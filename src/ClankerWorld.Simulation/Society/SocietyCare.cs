namespace ClankerWorld.Simulation.Society;

public static partial class SocietyFixture
{
    /// <summary>Recover existing injuries without creating a new damage or combat rule.</summary>
    public static SocietyOperationResult RecoverHealth(SocietyCheckpoint checkpoint, string actor, int amount)
    {
        Validate(checkpoint);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);
        var person = checkpoint.GetInhabitant(actor);
        if (person.Status != SocietyInhabitantStatus.Active)
            throw new InvalidOperationException("Only a living inhabitant can recover health.");
        var next = checkpoint with
        {
            Inhabitants = checkpoint.Inhabitants.Select(item => item.Id == actor
                ? item with { HealthBasisPoints = checked((int)Math.Min(10_000L, (long)item.HealthBasisPoints + amount)) }
                : item).ToArray(),
        };
        return Commit(next, "injury_treated", actor, actor);
    }
}

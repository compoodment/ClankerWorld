using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Tests;

internal static class InstructionRecipientFixture
{
    public static PrivateWorldRuntimeState AfterDeath(PrivateWorldRuntimeState state, string targetId)
    {
        var checkpoint = SocietyFixture.Kill(state.Society.Society, targetId, SocietyDeathCause.Accident).Checkpoint;
        var physical = state.Inhabitants.Single(item => item.InhabitantId == targetId);
        var deceased = checkpoint.GetInhabitant(targetId);
        return state with
        {
            Society = state.Society with { Society = checkpoint },
            Inhabitants = state.Inhabitants.Where(item => item.InhabitantId != targetId).ToArray(),
            DeceasedInhabitants = (state.DeceasedInhabitants ?? []).Append(
                new PlaytestDeceasedInhabitantState(targetId, checkpoint.WorldTick,
                    checkpoint.AgeAt(deceased, checkpoint.WorldTick), physical)).ToArray(),
        };
    }
}

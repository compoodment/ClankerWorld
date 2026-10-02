namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    // The common workstation supply path gathers and carries herbs, fuel and
    // whole water jugs. Production reserves their actual on-site contents.
    private void StageCareContent() => StageBuiltInContent(CareContent.PackageId, TailorContent.PackageId,
        CareContent.Create, "care_content_staged");
}

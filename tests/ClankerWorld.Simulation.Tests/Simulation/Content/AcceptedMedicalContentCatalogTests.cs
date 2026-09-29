using ClankerWorld.Simulation.Content;

namespace ClankerWorld.Simulation.Tests;

public sealed class AcceptedMedicalContentCatalogTests
{
    [Fact]
    public void AcceptedMedicalContentKeepsDistinctStableIdsAndClinicFootprints()
    {
        Assert.Equal(["bandage", "medicine", "clinic"], AcceptedMedicalContentCatalog.Entries.Select(item => item.Id));
        Assert.Equal(3, AcceptedMedicalContentCatalog.Entries.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            [AcceptedMedicalContentKind.Item, AcceptedMedicalContentKind.Item, AcceptedMedicalContentKind.Building],
            AcceptedMedicalContentCatalog.Entries.Select(item => item.Kind));
        Assert.Equal([new AcceptedMedicalFootprint(1, 1), new AcceptedMedicalFootprint(1, 2)],
            AcceptedMedicalContentCatalog.ClinicFootprints);
    }
}

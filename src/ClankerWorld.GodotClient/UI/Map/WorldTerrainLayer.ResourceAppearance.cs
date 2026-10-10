namespace ClankerWorld.GodotClient.UI;

public partial class WorldTerrainLayer
{
    // Copy the relevant immutable records when appearance changes. Keeping the
    // caller's list would miss in-place replacement of a resource in that list.
    private OwnerWorldResource[]? treeAppearanceInputs;
    private OwnerWorldResource[]? naturalAppearanceInputs;
    private OwnerWorldResource[]? campAppearanceInputs;
    private byte[]? naturalAppearanceTrees;
    private byte[]? campAppearanceTrees;
    private byte[]? campAppearanceNaturalObjects;

    private void ResetResourceAppearanceInputs()
    {
        treeAppearanceInputs = null;
        naturalAppearanceInputs = null;
        campAppearanceInputs = null;
        naturalAppearanceTrees = null;
        campAppearanceTrees = null;
        campAppearanceNaturalObjects = null;
    }

    private bool ResourceInBounds(OwnerWorldResource resource) => resource.Position.X >= 0 && resource.Position.X < world!.Width &&
        resource.Position.Y >= 0 && resource.Position.Y < world.Height;

    private static string VisibleTreeStage(OwnerWorldResource resource) => resource.TreeStage ??
        (resource.TreeKind == "orchard" ? "fruiting" : resource.IsPlanted ? "sapling" :
            resource.Quantity == 0 || resource.State != "available" ? "stump" : "mature");

    private static byte VisibleNaturalStage(OwnerWorldResource resource) =>
        resource.Quantity == 0 || resource.State != "available" ? resource.IsRenewable ? (byte)2 : (byte)1 : (byte)0;

    private static NatureSprite? CampAppearance(OwnerWorldResource resource) =>
        resource.TreeKind is not null || resource.NaturalObjectKind is not null ? null :
            NatureSprites.ForCampResource(resource.Kind, resource.Quantity == 0 || resource.State != "available", resource.IsRenewable);

    private bool TreeAppearanceMatches(IReadOnlyList<OwnerWorldResource> resources)
    {
        if (treeAppearanceInputs is null) return false;
        var index = 0;
        for (var source = 0; source < resources.Count; source++)
        {
            var resource = resources[source];
            if (resource.TreeKind is null || !ResourceInBounds(resource)) continue;
            if (index >= treeAppearanceInputs.Length) return false;
            var previous = treeAppearanceInputs[index++];
            if (resource.Position != previous.Position || resource.TreeKind != previous.TreeKind ||
                VisibleTreeStage(resource) != VisibleTreeStage(previous)) return false;
        }
        return index == treeAppearanceInputs.Length;
    }

    private bool NaturalAppearanceMatches(IReadOnlyList<OwnerWorldResource> resources)
    {
        if (naturalAppearanceInputs is null) return false;
        var index = 0;
        for (var source = 0; source < resources.Count; source++)
        {
            var resource = resources[source];
            var kind = NatureSprites.NaturalObjectCode(resource.NaturalObjectKind);
            if (kind == 0 || !ResourceInBounds(resource)) continue;
            if (index >= naturalAppearanceInputs.Length) return false;
            var previous = naturalAppearanceInputs[index++];
            if (resource.Position != previous.Position || kind != NatureSprites.NaturalObjectCode(previous.NaturalObjectKind) ||
                VisibleNaturalStage(resource) != VisibleNaturalStage(previous)) return false;
        }
        return index == naturalAppearanceInputs.Length;
    }

    private bool CampAppearanceMatches(IReadOnlyList<OwnerWorldResource> resources)
    {
        if (campAppearanceInputs is null) return false;
        var index = 0;
        for (var source = 0; source < resources.Count; source++)
        {
            var resource = resources[source];
            var sprite = CampAppearance(resource);
            if (sprite is null || !ResourceInBounds(resource)) continue;
            if (index >= campAppearanceInputs.Length) return false;
            var previous = campAppearanceInputs[index++];
            if (resource.Position != previous.Position || sprite != CampAppearance(previous)) return false;
        }
        return index == campAppearanceInputs.Length;
    }
}

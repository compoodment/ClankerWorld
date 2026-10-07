namespace ClankerWorld.Simulation.Kernel;

public static partial class InventoryFixture
{
    /// <summary>Moves an animal's complete held product without releasing its custody.</summary>
    public static InventoryCheckpoint MoveAnimalProduct(InventoryCheckpoint checkpoint, string productId,
        string reservationId, string animalId, InventoryGroundPosition from, InventoryGroundPosition to, int wrappingWidth = 0)
    {
        ValidateCheckpoint(checkpoint);
        var product = checkpoint.GetLot(productId);
        var held = checkpoint.GetReservation(reservationId);
        if (product.ItemKind is not ("eggs" or "wool" or "milk") || product.GroundPosition != from ||
            product.CarrierId is not null || product.StorageBuildingId is not null || product.ContainerLotId is not null ||
            held.LotId != productId || held.OwnerId != product.OwnerId || held.Quantity != product.Quantity ||
            held.State != InventoryReservationState.Reserved || !held.IsExclusive || held.Purpose != "animal-product:" + animalId ||
            to.X < 0 || to.Y < 0 || (Math.Abs(to.X - from.X) > 1 &&
                !(wrappingWidth > 1 && from.X < wrappingWidth && to.X < wrappingWidth && Math.Abs(to.X - from.X) == wrappingWidth - 1)) || Math.Abs(to.Y - from.Y) > 1)
            throw new InvalidOperationException("A held product must follow its animal's physical step.");
        return Commit(checkpoint, lots: checkpoint.Lots.Select(lot => lot.Id == productId ? lot with { GroundPosition = to } : lot).ToArray(),
            eventKind: "animal_product_moved", detail: $"{animalId}:{productId}:{to.X},{to.Y}");
    }

    /// <summary>Moves one fitted saddle with its animal after the runtime checks the physical step.</summary>
    public static InventoryCheckpoint MoveAnimalSaddle(InventoryCheckpoint checkpoint, string saddleId,
        string reservationId, string animalId, InventoryGroundPosition from, InventoryGroundPosition to, int wrappingWidth = 0)
    {
        ValidateCheckpoint(checkpoint);
        var saddle = checkpoint.GetLot(saddleId);
        var held = checkpoint.GetReservation(reservationId);
        if (saddle.ItemKind != "saddle" || saddle.Quantity != 1 || saddle.GroundPosition != from ||
            saddle.CarrierId is not null || saddle.StorageBuildingId is not null || saddle.ContainerLotId is not null ||
            held.LotId != saddleId || held.OwnerId != saddle.OwnerId || held.Quantity != 1 ||
            held.State != InventoryReservationState.Reserved || !held.IsExclusive || held.Purpose != "animal-saddle:" + animalId ||
            to.X < 0 || to.Y < 0 || (Math.Abs(to.X - from.X) > 1 &&
                !(wrappingWidth > 1 && from.X < wrappingWidth && to.X < wrappingWidth && Math.Abs(to.X - from.X) == wrappingWidth - 1)) || Math.Abs(to.Y - from.Y) > 1)
            throw new InvalidOperationException("A fitted saddle must follow its animal's physical step.");
        return Commit(checkpoint, lots: checkpoint.Lots.Select(lot => lot.Id == saddleId ? lot with { GroundPosition = to } : lot).ToArray(),
            eventKind: "animal_saddle_moved", detail: $"{animalId}:{saddleId}:{to.X},{to.Y}");
    }
}

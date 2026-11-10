using Obsidian.Entities;

namespace Obsidian.WorldData;

public abstract partial class AbstractLevel
{
    /// <summary>
    /// Lifts entities out of the new collision band when a surface covering a block's whole footprint rises.
    /// Heights are collision heights relative to the block, so snow and farmland conversion can share this.
    /// Call before storing the replacement block, under the block tick lock.
    /// </summary>
    internal void LiftEntitiesForSurfaceRise(Vector position, double oldHeight, double newHeight)
    {
        if (newHeight <= oldHeight)
            return;

        var min = new VectorD(position.X, position.Y + oldHeight, position.Z);
        var max = new VectorD(position.X + 1, position.Y + newHeight, position.Z + 1);
        foreach (var entity in this.EntitiesIn(min, max).OfType<Entity>().ToArray())
        {
            // For a flat collision band, vanilla's pushEntitiesUp resolves a one-block downward sweep from above
            // to this distance. An entity whose feet remain below the band even after that sweep is not lifted.
            if (entity is IPlayer { GameMode: GameMode.Spectator })
                continue;

            var distance = max.Y - entity.CurrentBox.Min.Y;
            if (distance is > 0 and <= 1 && entity.Level == this)
                entity.LiftForBlockChange(distance);
        }
    }
}

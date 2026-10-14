using Obsidian.API.Blocks;

namespace Obsidian.WorldData;

public abstract partial class AbstractLevel
{
    /// <inheritdoc/>
    public bool IsFaceSturdy(Vector position, BlockFace face, SupportType support = SupportType.Full)
    {
        if (face is < BlockFace.Down or > BlockFace.East)
            throw new ArgumentOutOfRangeException(nameof(face));

        if (support is < SupportType.Full or > SupportType.Rigid)
            throw new ArgumentOutOfRangeException(nameof(support));

        lock (this.Fluids.TickLock)
        {
            if (this.IsOutsideBuildHeight(position.Y) || !this.Fluids.TryGetChunk(position, out var chunk))
                return false;

            var block = chunk.GetBlock(position.X, position.Y, position.Z);
            if (BlockEntityShapes.Get(this, position, block) is BlockShapes shapes)
                return ShapeSupport.IsFaceSturdy(ShapeSupport.Face(shapes.Collision, face), support);

            return block.IsFaceSturdy(face, support);
        }
    }
}

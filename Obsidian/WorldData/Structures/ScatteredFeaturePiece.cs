using System.Threading;

namespace Obsidian.WorldData.Structures;

/// <summary>
/// A single-piece structure (temples, swamp huts) that settles on the ground when first placed, like vanilla's
/// <c>ScatteredFeaturePiece</c>.
/// </summary>
public abstract class ScatteredFeaturePiece : StructurePiece
{
    private readonly Lock heightLock = new();

    protected ScatteredFeaturePiece(int x, int y, int z, int width, int height, int depth, BlockFace orientation)
        : base(0, MakeBoundingBox(x, y, z, orientation, width, height, depth))
    {
        this.Width = width;
        this.Height = height;
        this.Depth = depth;
        this.Orientation = orientation;
    }

    protected int Width { get; }

    protected int Height { get; }

    protected int Depth { get; }

    /// <summary>
    /// The ground height the piece settled on, or -1 before it's placed.
    /// </summary>
    protected int HeightPosition { get; private set; } = -1;

    /// <summary>
    /// Vanilla <c>updateAverageGroundHeight</c>: on first placement, moves the piece onto the average
    /// <c>MOTION_BLOCKING_NO_LEAVES</c> height of its columns inside <paramref name="box"/>, plus <paramref name="offset"/>.
    /// </summary>
    /// <returns>Whether the piece has a height and can be placed.</returns>
    /// <remarks>
    /// Like vanilla, the height comes from the columns of the first chunk the piece is placed in. Pieces are shared by every
    /// chunk the structure reaches, so the first placement takes a lock.
    /// </remarks>
    protected bool UpdateAverageGroundHeight(IWorldGenLevel level, BlockBox box, int offset)
    {
        lock (this.heightLock)
        {
            if (this.HeightPosition >= 0)
                return true;

            var total = 0;
            var count = 0;
            for (var z = this.BoundingBox.MinZ; z <= this.BoundingBox.MaxZ; z++)
            {
                for (var x = this.BoundingBox.MinX; x <= this.BoundingBox.MaxX; x++)
                {
                    if (!box.IsInside(x, 64, z))
                        continue;

                    total += level.GetHeight(HeightmapType.MotionBlockingNoLeaves, x, z);
                    count++;
                }
            }

            if (count == 0)
                return false;

            this.HeightPosition = total / count;
            this.Move(0, this.HeightPosition - this.BoundingBox.MinY + offset, 0);
            return true;
        }
    }

    /// <summary>
    /// Vanilla <c>updateHeightPositionToLowestGroundHeight</c>: on first placement, moves the piece onto the lowest
    /// <c>MOTION_BLOCKING_NO_LEAVES</c> height of all its columns, plus <paramref name="offset"/>.
    /// </summary>
    protected bool UpdateHeightPositionToLowestGroundHeight(IWorldGenLevel level, int offset)
    {
        lock (this.heightLock)
        {
            if (this.HeightPosition >= 0)
                return true;

            var lowest = level.MinY + level.Height;
            for (var z = this.BoundingBox.MinZ; z <= this.BoundingBox.MaxZ; z++)
            {
                for (var x = this.BoundingBox.MinX; x <= this.BoundingBox.MaxX; x++)
                    lowest = Math.Min(lowest, level.GetHeight(HeightmapType.MotionBlockingNoLeaves, x, z));
            }

            this.HeightPosition = lowest;
            this.Move(0, this.HeightPosition - this.BoundingBox.MinY + offset, 0);
            return true;
        }
    }
}

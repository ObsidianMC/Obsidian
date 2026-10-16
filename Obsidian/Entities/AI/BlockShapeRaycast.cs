using Obsidian.API.Blocks;

namespace Obsidian.Entities.AI;

/// <summary>Finds the nearest box, keeping the index so multipart blocks can identify the selected part.</summary>
internal static class BlockShapeRaycast
{
    public static (double Distance, BlockShapeHit Hit)? Clip(
        IReadOnlyList<BoundingBox> boxes,
        Vector position,
        VectorD offset,
        VectorD start,
        VectorD direction)
    {
        (double Distance, BlockShapeHit Hit)? best = null;

        for (var index = 0; index < boxes.Count; index++)
        {
            var box = boxes[index].OffsetBy((VectorD)position + offset);
            var point = start + direction * 0.001;

            // Vanilla's VoxelShape.clip probes .001 along the complete segment for an inside hit.
            if (point.X >= box.Min.X && point.X < box.Max.X
                && point.Y >= box.Min.Y && point.Y < box.Max.Y
                && point.Z >= box.Min.Z && point.Z < box.Max.Z)
            {
                return (0.001, new BlockShapeHit(position, point - (VectorD)position,
                    EntryFace(direction), index));
            }

            var near = 0d;
            var far = 1d;
            var face = BlockFace.Up;
            if (Axis(start.X, direction.X, box.Min.X, box.Max.X, BlockFace.West, BlockFace.East)
                && Axis(start.Y, direction.Y, box.Min.Y, box.Max.Y, BlockFace.Down, BlockFace.Up)
                && Axis(start.Z, direction.Z, box.Min.Z, box.Max.Z, BlockFace.North, BlockFace.South)
                && near > 0 && near <= 1
                && (best is null || near < best.Value.Distance))
            {
                best = (near, new BlockShapeHit(position, start + direction * near - (VectorD)position, face, index));
            }

            bool Axis(double origin, double delta, double min, double max, BlockFace negative, BlockFace positive)
            {
                if (Math.Abs(delta) < 1e-12)
                    return origin >= min && origin <= max;

                var entry = ((delta > 0 ? min : max) - origin) / delta;
                var exit = ((delta > 0 ? max : min) - origin) / delta;
                if (entry > near)
                {
                    near = entry;
                    face = delta > 0 ? negative : positive;
                }

                far = Math.Min(far, exit);
                return near <= far;
            }
        }

        return best;
    }

    private static BlockFace EntryFace(VectorD direction) =>
        Math.Abs(direction.X) > Math.Abs(direction.Y) && Math.Abs(direction.X) > Math.Abs(direction.Z)
            ? direction.X > 0 ? BlockFace.West : BlockFace.East
            : Math.Abs(direction.Y) > Math.Abs(direction.Z)
                ? direction.Y > 0 ? BlockFace.Down : BlockFace.Up
                : direction.Z > 0 ? BlockFace.North : BlockFace.South;
}

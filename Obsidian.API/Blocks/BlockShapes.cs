namespace Obsidian.API.Blocks;

/// <summary>
/// Immutable collision and selection boxes, in block-local coordinates. Empty lists mean no shape; a null
/// shape override uses the block state's usual shapes. Boxes must fit inside the block's unit cube.
/// </summary>
public sealed class BlockShapes
{
    /// <summary>The boxes that stop entities moving through the block.</summary>
    public IReadOnlyList<BoundingBox> Collision { get; }

    /// <summary>The boxes used for picking and the selection outline, in part index order.</summary>
    public IReadOnlyList<BoundingBox> Outline { get; }

    /// <summary>Copies both box lists, rejecting invalid or out-of-block bounds.</summary>
    public BlockShapes(IEnumerable<BoundingBox> collision, IEnumerable<BoundingBox> outline)
    {
        this.Collision = Copy(collision);
        this.Outline = Copy(outline);
    }

    private static IReadOnlyList<BoundingBox> Copy(IEnumerable<BoundingBox> boxes)
    {
        ArgumentNullException.ThrowIfNull(boxes);
        var copied = boxes.ToArray();

        foreach (var box in copied)
        {
            if (!Valid(box.Min.X, box.Max.X)
                || !Valid(box.Min.Y, box.Max.Y)
                || !Valid(box.Min.Z, box.Max.Z))
            {
                throw new ArgumentException(
                    "Shape boxes must have positive volume inside the unit cube.", nameof(boxes));
            }
        }

        return Array.AsReadOnly(copied);
    }

    private static bool Valid(double min, double max) =>
        double.IsFinite(min) && double.IsFinite(max) && min >= 0 && max <= 1 && min < max;
}

/// <summary>The nearest outline box hit, including its index and the hit location relative to the block.</summary>
public readonly record struct BlockShapeHit(Vector Position, VectorD ClickedAt, BlockFace Face, int BoxIndex);

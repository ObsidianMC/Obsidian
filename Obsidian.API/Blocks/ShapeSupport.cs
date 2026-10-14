namespace Obsidian.API.Blocks;

// Vanilla 1.21.11 Block.isFaceFull and SupportType: FULL covers the unit face, CENTER covers the
// central 2/16 square, and RIGID covers the outer 2/16 rim. Test the union, not its individual boxes.
internal static class ShapeSupport
{
    private const double Epsilon = 1e-7;

    public static bool IsFaceSturdy(double[][] rectangles, SupportType support) => support switch
    {
        SupportType.Full => Covers(rectangles, 0, 0, 1, 1),
        SupportType.Center => Covers(rectangles, 7d / 16, 7d / 16, 9d / 16, 9d / 16),
        SupportType.Rigid => Covers(rectangles, 0, 0, 1, 2d / 16)
            && Covers(rectangles, 0, 14d / 16, 1, 1)
            && Covers(rectangles, 0, 2d / 16, 2d / 16, 14d / 16)
            && Covers(rectangles, 14d / 16, 2d / 16, 1, 14d / 16),
        _ => throw new ArgumentOutOfRangeException(nameof(support))
    };

    public static int Flags(IReadOnlyList<BoundingBox> boxes, double[][][] faces)
    {
        if (boxes.Count == 0)
            return 1 << 28;

        var flags = 0;
        var edges = boxes
            .SelectMany(box => new[]
            {
                box.Min.X,
                box.Max.X
            })
            .Append(0)
            .Append(1)
            .Distinct()
            .Order()
            .ToArray();
        var full = true;
        for (var i = 0; i < edges.Length - 1 && full; i++)
        {
            if (edges[i + 1] - edges[i] < Epsilon)
                continue;

            var x = (edges[i] + edges[i + 1]) / 2;
            var section = boxes.Where(box => box.Min.X <= x && box.Max.X >= x)
                .Select(box => new[]
                {
                    box.Min.Y,
                    box.Min.Z,
                    box.Max.Y,
                    box.Max.Z
                }).ToArray();
            full = Covers(section, 0, 0, 1, 1);
        }

        if (full)
            flags |= 1 << 6;

        for (var face = BlockFace.Down; face <= BlockFace.East; face++)
        {
            var rectangles = faces[(int)face];
            var fullFace = IsFaceSturdy(rectangles, SupportType.Full);
            if (fullFace)
                flags |= 1 << (11 + (int)face);

            if (face == BlockFace.Up)
            {
                if (fullFace)
                    flags |= 1 << 29;

                if (IsFaceSturdy(rectangles, SupportType.Center))
                    flags |= 1 << 7;

                if (IsFaceSturdy(rectangles, SupportType.Rigid))
                {
                    flags |= 1 << 8;
                }
            }

            if (face == BlockFace.Down && IsFaceSturdy(rectangles, SupportType.Center))
                flags |= 1 << 31;
        }

        return flags;
    }

    public static double[][] Face(IReadOnlyList<BoundingBox> boxes, BlockFace face)
    {
        var axis = face switch
        {
            BlockFace.Down or BlockFace.Up => 1,
            BlockFace.North or BlockFace.South => 2,
            _ => 0
        };
        var positive = face is BlockFace.Up or BlockFace.South or BlockFace.East;
        return boxes.Where(box => positive
                ? Math.Abs(Coordinate(box.Max, axis) - 1) < Epsilon
                : Math.Abs(Coordinate(box.Min, axis)) < Epsilon)
            .Select(box => axis switch
            {
                0 => new[]
                {
                    box.Min.Y,
                    box.Min.Z,
                    box.Max.Y,
                    box.Max.Z
                },
                1 => new[]
                {
                    box.Min.X,
                    box.Min.Z,
                    box.Max.X,
                    box.Max.Z
                },
                _ => new[]
                {
                    box.Min.X,
                    box.Min.Y,
                    box.Max.X,
                    box.Max.Y
                }
            }).ToArray();
    }

    private static double Coordinate(VectorD point, int axis) => axis switch
    {
        0 => point.X,
        1 => point.Y,
        _ => point.Z
    };

    private static bool Covers(double[][] boxes, double minU, double minV, double maxU, double maxV)
    {
        var us = Edges(boxes, 0, minU, maxU);
        var vs = Edges(boxes, 1, minV, maxV);
        for (var i = 0; i < us.Length - 1; i++)
        for (var j = 0; j < vs.Length - 1; j++)
        {
            if (us[i + 1] - us[i] < Epsilon || vs[j + 1] - vs[j] < Epsilon)
                continue;

            var u = (us[i] + us[i + 1]) / 2;
            var v = (vs[j] + vs[j + 1]) / 2;
            if (!boxes.Any(box => box[0] <= u && box[2] >= u && box[1] <= v && box[3] >= v))
                return false;
        }

        return true;
    }

    private static double[] Edges(double[][] boxes, int axis, double min, double max) => boxes
        .SelectMany(box => new[]
        {
            Math.Clamp(box[axis], min, max),
            Math.Clamp(box[axis + 2], min, max)
        })
        .Append(min)
        .Append(max)
        .Distinct()
        .Order()
        .ToArray();
}

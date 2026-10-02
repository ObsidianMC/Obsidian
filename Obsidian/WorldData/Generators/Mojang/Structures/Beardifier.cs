using Obsidian.WorldData.Structures;

namespace Obsidian.WorldData.Generators.Mojang.Structures;

/// <summary>
/// Density added around the pieces of structures with terrain adaptation during the noise step, so terrain fills under
/// and clears over villages, buries strongholds and so on, like vanilla's <c>Beardifier</c>.
/// </summary>
internal sealed class Beardifier
{
    private const int KernelRadius = 12;
    private const int KernelSize = 24;

    // Vanilla's BEARD_KERNEL: exp(-d²/16) around the piece edge, as floats, indexed [z][x][y].
    private static readonly float[] kernel = CreateKernel();

    public static Beardifier Empty { get; } = new([], [], null);

    private readonly List<Rigid> pieces;
    private readonly List<JigsawJunction> junctions;
    private readonly BlockBox? affectedBox;

    private Beardifier(List<Rigid> pieces, List<JigsawJunction> junctions, BlockBox? affectedBox)
    {
        this.pieces = pieces;
        this.junctions = junctions;
        this.affectedBox = affectedBox;
    }

    /// <summary>
    /// Vanilla <c>forStructuresInChunk</c>: the pieces and junctions of <paramref name="starts"/> within 12 blocks of the chunk.
    /// </summary>
    public static Beardifier ForStructuresInChunk(IEnumerable<StructureStart> starts, int chunkX, int chunkZ)
    {
        var minX = chunkX << 4;
        var minZ = chunkZ << 4;
        var pieces = new List<Rigid>();
        var junctions = new List<JigsawJunction>();
        BlockBox? box = null;

        foreach (var start in starts)
        {
            var adjustment = start.Structure.TerrainAdaptation;

            foreach (var piece in start.Pieces)
            {
                if (!piece.IsCloseToChunk(chunkX, chunkZ, KernelRadius))
                    continue;

                if (piece is IJigsawPiece jigsaw)
                {
                    if (jigsaw.IsRigid)
                    {
                        pieces.Add(new Rigid(piece.BoundingBox, adjustment, jigsaw.GroundLevelDelta));
                        box = Include(box, piece.BoundingBox);
                    }

                    foreach (var junction in jigsaw.Junctions)
                    {
                        if (junction.SourceX > minX - KernelRadius && junction.SourceZ > minZ - KernelRadius
                            && junction.SourceX < minX + 15 + KernelRadius && junction.SourceZ < minZ + 15 + KernelRadius)
                        {
                            junctions.Add(junction);
                            var position = new Vector(junction.SourceX, junction.SourceGroundY, junction.SourceZ);
                            box = Include(box, new BlockBox(position, position));
                        }
                    }
                }
                else
                {
                    pieces.Add(new Rigid(piece.BoundingBox, adjustment, 0));
                    box = Include(box, piece.BoundingBox);
                }
            }
        }

        return box is null ? Empty : new Beardifier(pieces, junctions, box.Value.InflatedBy(KernelSize));
    }

    /// <summary>
    /// The density to add at a block.
    /// </summary>
    public double Compute(int x, int y, int z)
    {
        if (this.affectedBox is null || !this.affectedBox.Value.IsInside(x, y, z))
            return 0.0;

        var density = 0.0;

        foreach (var piece in this.pieces)
        {
            var box = piece.Box;
            var distanceX = Math.Max(0, Math.Max(box.MinX - x, x - box.MaxX));
            var distanceZ = Math.Max(0, Math.Max(box.MinZ - z, z - box.MaxZ));
            var groundY = box.MinY + piece.GroundLevelDelta;
            var aboveGround = y - groundY;

            var distanceY = piece.Adjustment switch
            {
                TerrainAdjustment.Bury or TerrainAdjustment.BeardThin => aboveGround,
                TerrainAdjustment.BeardBox => Math.Max(0, Math.Max(groundY - y, y - box.MaxY)),
                TerrainAdjustment.Encapsulate => Math.Max(0, Math.Max(box.MinY - y, y - box.MaxY)),
                _ => 0
            };

            density += piece.Adjustment switch
            {
                TerrainAdjustment.Bury => BuryContribution(distanceX, distanceY / 2.0, distanceZ),
                TerrainAdjustment.BeardThin or TerrainAdjustment.BeardBox => BeardContribution(distanceX, distanceY, distanceZ, aboveGround) * 0.8,
                TerrainAdjustment.Encapsulate => BuryContribution(distanceX / 2.0, distanceY / 2.0, distanceZ / 2.0) * 0.8,
                _ => 0.0
            };
        }

        foreach (var junction in this.junctions)
        {
            var dy = y - junction.SourceGroundY;
            density += BeardContribution(x - junction.SourceX, dy, z - junction.SourceZ, dy) * 0.4;
        }

        return density;
    }

    private static double BuryContribution(double x, double y, double z)
    {
        var length = Math.Sqrt(x * x + y * y + z * z);
        var delta = length / 6.0;

        // Mth.clampedMap(length, 0, 6, 1, 0).
        return delta < 0.0 ? 1.0 : delta > 1.0 ? 0.0 : 1.0 + delta * (0.0 - 1.0);
    }

    private static double BeardContribution(int x, int y, int z, int aboveGround)
    {
        var kernelX = x + KernelRadius;
        var kernelY = y + KernelRadius;
        var kernelZ = z + KernelRadius;
        if (!IsInKernelRange(kernelX) || !IsInKernelRange(kernelY) || !IsInKernelRange(kernelZ))
            return 0.0;

        var dy = aboveGround + 0.5;
        var lengthSquared = (double)x * x + dy * dy + (double)z * z;
        var value = -dy * FastInvSqrt(lengthSquared / 2.0) / 2.0;
        return value * kernel[kernelZ * KernelSize * KernelSize + kernelX * KernelSize + kernelY];
    }

    private static bool IsInKernelRange(int value) => value >= 0 && value < KernelSize;

    /// <summary>Vanilla <c>Mth.fastInvSqrt</c>: one Newton step from the bit-hack estimate.</summary>
    private static double FastInvSqrt(double value)
    {
        var half = 0.5 * value;
        var bits = BitConverter.DoubleToInt64Bits(value);
        bits = 6910469410427058090L - (bits >> 1);
        var estimate = BitConverter.Int64BitsToDouble(bits);
        return estimate * (1.5 - half * estimate * estimate);
    }

    private static float[] CreateKernel()
    {
        var values = new float[KernelSize * KernelSize * KernelSize];
        for (var z = 0; z < KernelSize; z++)
        {
            for (var x = 0; x < KernelSize; x++)
            {
                for (var y = 0; y < KernelSize; y++)
                {
                    double dx = x - KernelRadius;
                    var dy = y - KernelRadius + 0.5;
                    double dz = z - KernelRadius;
                    values[z * KernelSize * KernelSize + x * KernelSize + y] = (float)Math.Pow(Math.E, -(dx * dx + dy * dy + dz * dz) / 16.0);
                }
            }
        }

        return values;
    }

    private static BlockBox Include(BlockBox? box, BlockBox other) => box is null ? other : box.Value.Encapsulate(other);

    private readonly record struct Rigid(BlockBox Box, TerrainAdjustment Adjustment, int GroundLevelDelta);
}

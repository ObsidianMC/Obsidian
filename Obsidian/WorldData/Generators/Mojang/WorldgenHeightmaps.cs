using Obsidian.WorldData.Generators.Mojang.Features;

namespace Obsidian.WorldData.Generators.Mojang;

/// <summary>
/// Maintains the heightmaps vanilla keeps while generating: the world generation ones
/// (<see cref="HeightmapType.WorldSurfaceWG"/> and <see cref="HeightmapType.OceanFloorWG"/>) and the final ones.
/// </summary>
internal static class WorldgenHeightmaps
{
    /// <summary>
    /// <see cref="Mask"/> bit of <see cref="HeightmapType.OceanFloor"/> and <see cref="HeightmapType.OceanFloorWG"/>.
    /// </summary>
    public const int OceanFloorBit = 1;

    /// <summary>
    /// <see cref="Mask"/> bit of <see cref="HeightmapType.WorldSurface"/> and <see cref="HeightmapType.WorldSurfaceWG"/>.
    /// </summary>
    public const int WorldSurfaceBit = 2;

    /// <summary>
    /// <see cref="Mask"/> bit of <see cref="HeightmapType.MotionBlocking"/>.
    /// </summary>
    public const int MotionBlockingBit = 4;

    /// <summary>
    /// <see cref="Mask"/> bit of <see cref="HeightmapType.MotionBlockingNoLeaves"/>.
    /// </summary>
    public const int MotionBlockingNoLeavesBit = 8;

    private static readonly HeightmapType[] finalHeightmaps =
        [HeightmapType.WorldSurface, HeightmapType.OceanFloor, HeightmapType.MotionBlocking, HeightmapType.MotionBlockingNoLeaves];

    // The mask of every block state, indexed by state id: generation checks the predicates for every block it writes.
    private static readonly byte[] masks = BuildMasks();

    /// <summary>
    /// Recomputes both heightmaps from the chunk's blocks. Values are the first free Y above the highest
    /// matching block, like vanilla.
    /// </summary>
    /// <remarks>
    /// The ocean floor stops at the first block that blocks motion, so it passes through fluids.
    /// </remarks>
    public static void Update(IChunk chunk, int minY, int height)
    {
        Span<int> worldSurface = stackalloc int[256];
        Span<int> oceanFloor = stackalloc int[256];

        for (var localZ = 0; localZ < 16; localZ++)
        {
            for (var localX = 0; localX < 16; localX++)
            {
                var column = localZ * 16 + localX;
                worldSurface[column] = minY;
                oceanFloor[column] = minY;

                for (var y = minY + height - 1; y >= minY; y--)
                {
                    var block = chunk.GetBlock(localX, y, localZ);
                    if (block.IsAir)
                        continue;

                    if (worldSurface[column] == minY)
                        worldSurface[column] = y + 1;

                    if (block.BlocksMotion())
                    {
                        oceanFloor[column] = y + 1;
                        break;
                    }
                }
            }
        }

        Set(chunk, HeightmapType.WorldSurfaceWG, worldSurface);
        Set(chunk, HeightmapType.OceanFloorWG, oceanFloor);
    }

    /// <summary>
    /// Writes the final heightmaps (<see cref="HeightmapType.WorldSurface"/>, <see cref="HeightmapType.OceanFloor"/>,
    /// <see cref="HeightmapType.MotionBlocking"/> and <see cref="HeightmapType.MotionBlockingNoLeaves"/>) the chunk still has:
    /// the heights generation tracked (<see cref="Chunk.FinalHeightmaps"/>), or else heights computed from its blocks.
    /// </summary>
    /// <remarks>
    /// Features track these heights only while decorating, so they're written back once every feature that can reach the
    /// chunk has run.
    /// </remarks>
    public static void UpdateFinal(IChunk chunk, int minY, int height) =>
        StoreFinal(chunk, (chunk as Chunk)?.FinalHeightmaps ?? new FinalHeightmaps(chunk, minY, height));

    /// <summary>
    /// Writes <paramref name="heights"/> to the final heightmaps the chunk still has.
    /// </summary>
    public static void StoreFinal(IChunk chunk, FinalHeightmaps heights)
    {
        Span<int> values = stackalloc int[256];

        foreach (var type in finalHeightmaps)
        {
            if (!chunk.Heightmaps.ContainsKey(type))
                continue;

            heights.CopyTo(type, values);
            Set(chunk, type, values);
        }
    }

    /// <summary>
    /// Whether a block counts for a heightmap, using vanilla's Heightmap.Types predicates.
    /// </summary>
    public static bool Matches(HeightmapType type, IBlock block) => (Mask(block) & Bit(type)) != 0;

    /// <summary>
    /// The heightmaps a block counts for, as a combination of the <c>*Bit</c> constants.
    /// </summary>
    public static int Mask(IBlock block)
    {
        var id = block.GetHashCode();
        return (uint)id < (uint)masks.Length ? masks[id] : ComputeMask(block);
    }

    /// <summary>
    /// <see cref="Mask(IBlock)"/> by state id.
    /// </summary>
    public static int Mask(int stateId) =>
        (uint)stateId < (uint)masks.Length ? masks[stateId] : ComputeMask(BlocksRegistry.Get(stateId));

    /// <summary>
    /// The <see cref="Mask"/> bit of a heightmap.
    /// </summary>
    public static int Bit(HeightmapType type) => type switch
    {
        HeightmapType.WorldSurface or HeightmapType.WorldSurfaceWG => WorldSurfaceBit,
        HeightmapType.OceanFloor or HeightmapType.OceanFloorWG => OceanFloorBit,
        HeightmapType.MotionBlocking => MotionBlockingBit,
        HeightmapType.MotionBlockingNoLeaves => MotionBlockingNoLeavesBit,
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    public static void Set(IChunk chunk, HeightmapType type, ReadOnlySpan<int> heights)
    {
        if (!chunk.Heightmaps.TryGetValue(type, out var heightmap))
            return;

        for (var column = 0; column < heights.Length; column++)
            heightmap.Set(column % 16, column / 16, heights[column]);
    }

    private static byte[] BuildMasks()
    {
        var values = new byte[BlocksRegistry.StateToNumeric.Length];
        for (var stateId = 0; stateId < values.Length; stateId++)
            values[stateId] = (byte)ComputeMask(BlocksRegistry.Get(stateId));

        return values;
    }

    // Vanilla's Heightmap.Types predicates.
    private static int ComputeMask(IBlock block)
    {
        var mask = 0;
        if (!block.IsAir)
            mask |= WorldSurfaceBit;

        if (block.BlocksMotion())
            mask |= OceanFloorBit;

        if (block.BlocksMotion() || block.HasFluid())
        {
            mask |= MotionBlockingBit;

            // Vanilla's LeavesBlock is abstract; the concrete leaves classes all end with its name.
            if (!block.BlockClass().EndsWith("LeavesBlock", StringComparison.Ordinal))
                mask |= MotionBlockingNoLeavesBit;
        }

        return mask;
    }
}

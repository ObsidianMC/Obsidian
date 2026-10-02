using Obsidian.ChunkData;
using System.Numerics;

namespace Obsidian.WorldData.Generators.Mojang.Features;

/// <summary>
/// The final heightmaps (<see cref="HeightmapType.OceanFloor"/>, <see cref="HeightmapType.WorldSurface"/>,
/// <see cref="HeightmapType.MotionBlocking"/> and <see cref="HeightmapType.MotionBlockingNoLeaves"/>) of a chunk as
/// generation changes its blocks, like the heightmaps a vanilla proto chunk keeps.
/// </summary>
/// <remarks>
/// Heights start from the chunk's blocks and follow each block passed to <see cref="Update"/> like vanilla's
/// <c>Heightmap.update</c>, which keeps them exact as long as every block change goes through it.
/// </remarks>
internal sealed class FinalHeightmaps
{
    // One layer of 256 columns (z * 16 + x) per heightmap, in the order of WorldgenHeightmaps' mask bits.
    private const int LayerCount = 4;

    private readonly int[] heights = new int[LayerCount * 256];
    private readonly IChunk chunk;
    private readonly int minY;

    /// <summary>
    /// Computes the heights from the chunk's current blocks.
    /// </summary>
    public FinalHeightmaps(IChunk chunk, int minY, int height)
    {
        this.chunk = chunk;
        this.minY = minY;
        this.Prime(height);
    }

    /// <summary>
    /// The first free Y above the highest block matching the heightmap at local column (<paramref name="x"/>,
    /// <paramref name="z"/>).
    /// </summary>
    public int GetHeight(HeightmapType type, int x, int z) => this.heights[Layer(type) << 8 | (z & 15) << 4 | (x & 15)];

    /// <summary>
    /// Copies a heightmap's 256 heights, indexed by z * 16 + x.
    /// </summary>
    public void CopyTo(HeightmapType type, Span<int> destination) => this.heights.AsSpan(Layer(type) << 8, 256).CopyTo(destination);

    /// <summary>
    /// Updates the heights after a block was written at (<paramref name="x"/>, <paramref name="y"/>, <paramref name="z"/>),
    /// like vanilla's <c>Heightmap.update</c> for each heightmap.
    /// </summary>
    /// <param name="mask">The block's <see cref="WorldgenHeightmaps.Mask"/>.</param>
    public void Update(int x, int y, int z, int mask)
    {
        var column = (z & 15) << 4 | (x & 15);

        for (var layer = 0; layer < LayerCount; layer++)
        {
            ref var height = ref this.heights[layer << 8 | column];
            if (y <= height - 2)
                continue;

            if ((mask >> layer & 1) != 0)
            {
                if (y >= height)
                    height = y + 1;
            }
            else if (height - 1 == y)
            {
                height = this.FindHeight(x, y - 1, z, 1 << layer);
            }
        }
    }

    // The first free Y above the highest block at or below startY that matches one of the bits.
    private int FindHeight(int x, int startY, int z, int bit)
    {
        var y = startY;
        while (y >= this.minY && (this.MaskAt(x, y, z) & bit) == 0)
            y--;

        return y + 1;
    }

    private int MaskAt(int x, int y, int z) => this.chunk.Sections[(y - this.chunk.MinY) >> 4] is ChunkSection section
        ? WorldgenHeightmaps.Mask(section.GetStateId(x & 15, y & 15, z & 15))
        : WorldgenHeightmaps.Mask(this.chunk.GetBlock(x, y, z));

    // Scans every column from the top for all four heightmaps at once, skipping sections of a single block that
    // matches nothing.
    private void Prime(int height)
    {
        Span<byte> remaining = stackalloc byte[256];
        remaining.Fill((1 << LayerCount) - 1);
        var unresolved = 256;

        var sections = this.chunk.Sections;
        for (var sectionIndex = Math.Min(sections.Length, height >> 4) - 1; sectionIndex >= 0 && unresolved > 0; sectionIndex--)
        {
            var section = sections[sectionIndex];
            var baseY = this.minY + (sectionIndex << 4);

            if (section.BlockStateContainer.Palette is SingleValuePalette<IBlock> { IsFull: true } single)
            {
                var sectionMask = WorldgenHeightmaps.Mask(single.Value);
                if (sectionMask == 0)
                    continue;

                for (var column = 0; column < 256; column++)
                    this.Resolve(remaining, column, sectionMask, baseY + 15, ref unresolved);

                continue;
            }

            var chunkSection = section as ChunkSection;
            for (var column = 0; column < 256; column++)
            {
                for (var localY = 15; localY >= 0 && remaining[column] != 0; localY--)
                {
                    var mask = chunkSection is not null
                        ? WorldgenHeightmaps.Mask(chunkSection.GetStateId(column & 15, localY, column >> 4))
                        : WorldgenHeightmaps.Mask(section.GetBlock(column & 15, localY, column >> 4));

                    this.Resolve(remaining, column, mask, baseY + localY, ref unresolved);
                }
            }
        }

        for (var column = 0; column < 256; column++)
            this.Resolve(remaining, column, (1 << LayerCount) - 1, this.minY - 1, ref unresolved);
    }

    // Sets the heights of the column's remaining heightmaps that the mask matches to just above y.
    private void Resolve(Span<byte> remaining, int column, int mask, int y, ref int unresolved)
    {
        var found = remaining[column] & mask;
        if (found == 0)
            return;

        remaining[column] &= (byte)~found;
        if (remaining[column] == 0)
            unresolved--;

        while (found != 0)
        {
            this.heights[BitOperations.TrailingZeroCount(found) << 8 | column] = y + 1;
            found &= found - 1;
        }
    }

    private static int Layer(HeightmapType type) => BitOperations.TrailingZeroCount(WorldgenHeightmaps.Bit(type));
}

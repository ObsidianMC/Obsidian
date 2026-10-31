using Obsidian.ChunkData;
using Obsidian.WorldData.Generators.Density;
using Obsidian.WorldData.Generators.Mojang;
using Obsidian.WorldData.Generators.Mojang.Structures;
using Obsidian.WorldData.Structures;

namespace Obsidian.WorldData.Generators.Terrain;

/// <summary>
/// The noise fill of one chunk: its blocks from the final density and the aquifer, the fluids it marks for
/// post-processing and its generation heightmaps.
/// </summary>
/// <remarks>
/// <para>
/// Blocks are decided in vanilla's z, x, descending-y order into a byte code per block, then written a layer at a
/// time. A section's palette lists blocks in the order they were first written and saved chunks keep that order, so
/// each section is first given its blocks in the order per-block writes would have met them.
/// </para>
/// <para>
/// The fill assumes the chunk has no blocks yet, as noise is the first status to write any: the generation
/// heightmaps come from the codes rather than from reading the chunk back.
/// </para>
/// </remarks>
internal sealed class TerrainFill
{
    // Structure density only reaches a few blocks, so it is restricted to the pieces near each interpolation cell.
    private const int BeardCellWidth = 4;
    private const int BeardCellHeight = 8;

    // A fill's buffers, kept by each thread for its next fill.
    [ThreadStatic]
    private static TerrainFill? free;

    private readonly FillPalette palette = new();
    private readonly int[] worldSurface = new int[256];
    private readonly int[] oceanFloor = new int[256];
    private readonly byte[] sectionCodes = new byte[16 * 256];
    private byte[] codes = [];

    // The code of the global fluid at each y of the volume, or -1 until a position there needs it.
    private short[] globalCodes = [];

    /// <summary>Fills the context's chunk from the final density plus <paramref name="beardifier"/>'s.</summary>
    /// <remarks>
    /// Every 26.3 noise setting ends its final density with <c>add(..., beardifier)</c>, which the router compiles
    /// to 0, so the structures' density is added here, in float like vanilla's addition.
    /// </remarks>
    public static void Run(TerrainContext context, Beardifier? beardifier)
    {
        var fill = free ?? new TerrainFill();
        free = null;

        fill.Fill(context, beardifier);
        free = fill;
    }

    private void Fill(TerrainContext context, Beardifier? beardifier)
    {
        var generator = context.Generator;
        var volume = context.Volume;
        using var density = DensityBuffer.Sample(generator.Router.FinalDensity, volume);

        if (beardifier is not null)
            AddBeardifier(density.Span, volume, beardifier);

        if (this.codes.Length < volume.Size)
            this.codes = new byte[volume.Size];

        this.palette.Reset(generator.DefaultBlock);
        this.Decide(context, density.Span);
        this.Write(context.Chunk, volume);

        var chunk = context.Chunk;
        WorldgenHeightmaps.Set(chunk, HeightmapType.WorldSurfaceWG, this.worldSurface);
        WorldgenHeightmaps.Set(chunk, HeightmapType.OceanFloorWG, this.oceanFloor);
        WorldgenHeightmaps.UpdateFinal(chunk, chunk.MinY, chunk.Height);
    }

    // Adds the structures' density cell by cell. Pieces out of a cell's reach add exactly 0 there, and leaving out a
    // 0 doesn't change a float sum that starts at 0, so each block gets the value the whole beardifier gives it.
    private static void AddBeardifier(Span<float> density, in DensityVolume volume, Beardifier beardifier)
    {
        var maxX = volume.MinBlockX + volume.SizeX - 1;
        var maxY = volume.MinBlockY + volume.SizeY - 1;
        var maxZ = volume.MinBlockZ + volume.SizeZ - 1;
        var volumeBox = BlockBox.Create(volume.MinBlockX, volume.MinBlockY, volume.MinBlockZ, maxX, maxY, maxZ);
        var nearVolume = beardifier.Within(volumeBox, beardifier.CreateBuffer());

        if (nearVolume == Beardifier.Empty)
            return;

        var cellBuffer = nearVolume.CreateBuffer();

        for (var cellZ = 0; cellZ < volume.SizeZ; cellZ += BeardCellWidth)
        {
            for (var cellX = 0; cellX < volume.SizeX; cellX += BeardCellWidth)
            {
                for (var cellY = 0; cellY < volume.SizeY; cellY += BeardCellHeight)
                {
                    var endX = Math.Min(cellX + BeardCellWidth, volume.SizeX) - 1;
                    var endY = Math.Min(cellY + BeardCellHeight, volume.SizeY) - 1;
                    var endZ = Math.Min(cellZ + BeardCellWidth, volume.SizeZ) - 1;
                    var cellBox = BlockBox.Create(
                        volume.BlockX(cellX),
                        volume.BlockY(cellY),
                        volume.BlockZ(cellZ),
                        volume.BlockX(endX),
                        volume.BlockY(endY),
                        volume.BlockZ(endZ));
                    var nearCell = nearVolume.Within(cellBox, cellBuffer);

                    if (nearCell == Beardifier.Empty)
                        continue;

                    for (var z = cellZ; z <= endZ; z++)
                    {
                        for (var x = cellX; x <= endX; x++)
                        {
                            for (var y = cellY; y <= endY; y++)
                            {
                                var value = nearCell.Sample(volume.BlockX(x), volume.BlockY(y), volume.BlockZ(z));
                                density[volume.Index(x, y, z)] += value;
                            }
                        }
                    }
                }
            }
        }
    }

    // Decides every block in vanilla's write order, which is also the order the aquifer marks fluids in, and tracks
    // the generation heightmaps from the top of each column.
    private void Decide(TerrainContext context, ReadOnlySpan<float> density)
    {
        var volume = context.Volume;
        var aquifer = context.Aquifer;
        var codes = this.codes.AsSpan(0, volume.Size);
        var globalFluidAboveY = aquifer.GlobalFluidAboveY;

        if (this.globalCodes.Length < volume.SizeY)
            this.globalCodes = new short[volume.SizeY];

        this.globalCodes.AsSpan(0, volume.SizeY).Fill(-1);

        for (var z = 0; z < volume.SizeZ; z++)
        {
            var blockZ = volume.BlockZ(z);

            for (var x = 0; x < volume.SizeX; x++)
            {
                var blockX = volume.BlockX(x);
                var columnStart = volume.Index(x, 0, z);
                var surface = volume.MinBlockY;
                var floor = volume.MinBlockY;

                for (var y = volume.SizeY - 1; y >= 0; y--)
                {
                    var blockY = volume.BlockY(y);
                    var value = density[columnStart + y];
                    var code = FillPalette.DefaultCode;

                    // Aquifers leave positive densities to the default block, which no fluid update follows. Above
                    // their sampling range they give the rest the global fluid at that y, with no update either.
                    if (!(value > 0.0f) && blockY > globalFluidAboveY)
                    {
                        code = this.GlobalCode(aquifer, blockX, y, blockZ, volume);
                    }
                    else if (!(value > 0.0f))
                    {
                        var substance = aquifer.ComputeSubstance(blockX, blockY, blockZ, value);
                        code = this.palette.CodeOf(substance ?? this.palette.DefaultBlock);

                        if (this.palette.IsLiquid(code) && aquifer.ShouldScheduleFluidUpdate)
                            context.MarkForPostProcessing(blockX, blockY, blockZ);
                    }

                    codes[columnStart + y] = code;

                    // A column's heights are settled once a block blocks motion.
                    if (floor != volume.MinBlockY)
                        continue;

                    var mask = this.palette.Mask(code);

                    if (surface == volume.MinBlockY && (mask & WorldgenHeightmaps.WorldSurfaceBit) != 0)
                        surface = blockY + 1;

                    if ((mask & WorldgenHeightmaps.OceanFloorBit) != 0)
                        floor = blockY + 1;
                }

                this.worldSurface[z * 16 + x] = surface;
                this.oceanFloor[z * 16 + x] = floor;
            }
        }
    }

    private byte GlobalCode(TerrainAquifer aquifer, int blockX, int y, int blockZ, in DensityVolume volume)
    {
        var code = this.globalCodes[y];

        if (code < 0)
        {
            var block = aquifer.ComputeSubstance(blockX, volume.BlockY(y), blockZ, 0.0) ?? this.palette.DefaultBlock;
            code = this.palette.CodeOf(block);
            this.globalCodes[y] = code;
        }

        return (byte)code;
    }

    private void Write(IChunk chunk, in DensityVolume volume)
    {
        var maxY = volume.MinBlockY + volume.SizeY - 1;
        var firstSection = (volume.MinBlockY - chunk.MinY) >> 4;
        var lastSection = (maxY - chunk.MinY) >> 4;

        for (var sectionIndex = firstSection; sectionIndex <= lastSection; sectionIndex++)
        {
            var sectionMinY = chunk.MinY + (sectionIndex << 4);
            var fromY = Math.Max(sectionMinY, volume.MinBlockY);
            var toY = Math.Min(sectionMinY + 15, maxY);

            if (chunk.Sections[sectionIndex] is ChunkSection section)
                this.WriteSection(section, volume, fromY, toY);
            else
                this.WriteBlocks(chunk, volume, fromY, toY);
        }
    }

    // Writes a section a layer at a time. Gathering its codes in per-block write order (z, x, descending y) also finds
    // where each block was first written; writing those first lists them in the section's palette in that order. A
    // section of air alone needs no writes. Chunk volumes are 16 blocks wide, a layer's width.
    private void WriteSection(ChunkSection section, in DensityVolume volume, int fromY, int toY)
    {
        Span<bool> seen = stackalloc bool[FillPalette.Capacity];
        var sectionCodes = this.sectionCodes;
        var hasBlocks = false;

        for (var z = 0; z < volume.SizeZ; z++)
        {
            for (var x = 0; x < volume.SizeX; x++)
            {
                // The column's codes are consecutive: its block y's code is at column + y.
                var column = volume.Index(x, 0, z) - volume.MinBlockY;

                for (var blockY = toY; blockY >= fromY; blockY--)
                {
                    var code = this.codes[column + blockY];
                    sectionCodes[(blockY & 15) << 8 | z << 4 | x] = code;

                    if (seen[code])
                        continue;

                    seen[code] = true;

                    if (this.palette.Block(code) is IBlock block)
                    {
                        section.SetBlockUntracked(x, blockY & 15, z, block);
                        hasBlocks = true;
                    }
                }
            }
        }

        if (!hasBlocks)
            return;

        var blocks = this.palette.Blocks;

        for (var blockY = toY; blockY >= fromY; blockY--)
            section.SetBlockLayer(blockY & 15, sectionCodes.AsSpan((blockY & 15) << 8, 256), blocks);
    }

    // Sections without direct storage take each block in write order.
    private void WriteBlocks(IChunk chunk, in DensityVolume volume, int fromY, int toY)
    {
        for (var z = 0; z < volume.SizeZ; z++)
        {
            for (var x = 0; x < volume.SizeX; x++)
            {
                for (var blockY = toY; blockY >= fromY; blockY--)
                {
                    var code = this.codes[volume.Index(x, blockY - volume.MinBlockY, z)];

                    if (this.palette.Block(code) is IBlock block)
                        chunk.SetBlock(x, blockY, z, block);
                }
            }
        }
    }

    /// <summary>
    /// The distinct blocks of one fill, numbered in order of appearance so the fill stores a byte per block. Air is
    /// numbered too, but kept as a null block, which layer writes skip.
    /// </summary>
    private sealed class FillPalette
    {
        /// <summary>The code of the settings' default block, which most blocks are.</summary>
        public const byte DefaultCode = 0;

        public const int Capacity = 256;

        private readonly IBlock[] keys = new IBlock[Capacity];
        private readonly IBlock?[] blocks = new IBlock?[Capacity];
        private readonly bool[] liquid = new bool[Capacity];
        private readonly byte[] masks = new byte[Capacity];
        private IBlock? last;
        private byte lastCode;

        public int Count { get; private set; }

        public IBlock DefaultBlock => this.keys[DefaultCode];

        /// <summary>The non-air blocks by code, with null for air.</summary>
        public ReadOnlySpan<IBlock?> Blocks => this.blocks.AsSpan(0, this.Count);

        /// <summary>Empties the palette for a new fill, whose default block gets <see cref="DefaultCode"/>.</summary>
        public void Reset(IBlock defaultBlock)
        {
            this.Count = 0;
            this.last = null;
            this.CodeOf(defaultBlock);
        }

        public byte CodeOf(IBlock block)
        {
            if (ReferenceEquals(block, this.last))
                return this.lastCode;

            var code = 0;
            while (code < this.Count && !ReferenceEquals(this.keys[code], block))
                code++;

            if (code == this.Count)
            {
                if (this.Count == Capacity)
                    throw new InvalidOperationException("Too many distinct blocks in one terrain fill.");

                this.keys[code] = block;
                this.blocks[code] = block.IsAir ? null : block;
                this.liquid[code] = block.IsLiquid;
                this.masks[code] = (byte)WorldgenHeightmaps.Mask(block);
                this.Count++;
            }

            this.last = block;
            this.lastCode = (byte)code;

            return this.lastCode;
        }

        public IBlock? Block(byte code) => this.blocks[code];

        public bool IsLiquid(byte code) => this.liquid[code];

        /// <summary>The block's <see cref="WorldgenHeightmaps.Mask(IBlock)"/>.</summary>
        public int Mask(byte code) => this.masks[code];
    }
}

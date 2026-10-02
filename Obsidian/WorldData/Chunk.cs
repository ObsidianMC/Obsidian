using Obsidian.API;
using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.Blocks;
using Obsidian.ChunkData;

namespace Obsidian.WorldData;

public sealed class Chunk : IChunk
{
    public int X { get; }
    public int Z { get; }

    public bool IsGenerated => ChunkStatus == ChunkGenStage.full;

    public ChunkGenStage ChunkStatus { get; private set; } = ChunkGenStage.empty;

    private const int width = 16;

    /// <summary>
    /// The lowest block Y of the chunk (a multiple of 16).
    /// </summary>
    public int MinY { get; }

    /// <summary>
    /// The number of block layers in the chunk.
    /// </summary>
    public int Height => this.Sections.Length << 4;

    //TODO try and do some temp caching
    public Dictionary<short, BlockMeta> BlockMetaStore { get; private set; } = new Dictionary<short, BlockMeta>();
    public Dictionary<int, IBlockEntity> BlockEntities { get; private set; } = new Dictionary<int, IBlockEntity>();

    /// <summary>
    /// Entities placed by world generation that haven't been spawned yet.
    /// </summary>
    internal List<GeneratedEntity> PendingEntities { get; } = [];

    public IChunkSection[] Sections { get; private set; }
    public IDictionary<HeightmapType, Heightmap> Heightmaps { get; }

    public Chunk(int x, int z, ChunkGenStage status = ChunkGenStage.empty) : this(x, z, -64, 384, status)
    {
    }

    /// <param name="minY">The dimension's lowest block Y; must be a multiple of 16.</param>
    /// <param name="height">The dimension's height in blocks; must be a multiple of 16.</param>
    public Chunk(int x, int z, int minY, int height, ChunkGenStage status = ChunkGenStage.empty)
    {
        X = x;
        Z = z;
        MinY = minY;

        // Sections come first: heightmaps size their entries from the chunk height.
        Sections = new ChunkSection[height >> 4];
        for (int i = 0; i < Sections.Length; i++)
        {
            Sections[i] = new ChunkSection(yBase: i + (minY >> 4));
        }

        Heightmaps = new Dictionary<HeightmapType, Heightmap>()
        {
            { HeightmapType.MotionBlocking, new Heightmap(HeightmapType.MotionBlocking, this) },
            { HeightmapType.OceanFloor, new Heightmap(HeightmapType.OceanFloor, this) },
            { HeightmapType.WorldSurface, new Heightmap(HeightmapType.WorldSurface, this) },
            { HeightmapType.WorldSurfaceWG, new Heightmap(HeightmapType.WorldSurfaceWG, this) },
            { HeightmapType.OceanFloorWG, new Heightmap(HeightmapType.OceanFloorWG, this) },
            { HeightmapType.MotionBlockingNoLeaves, new Heightmap(HeightmapType.MotionBlockingNoLeaves, this) }
        };
    }

    private Chunk(int x, int z, IChunkSection[] sections, Dictionary<HeightmapType, Heightmap> heightmaps)
    {
        X = x;
        Z = z;
        MinY = sections[0].YBase!.Value << 4;

        Heightmaps = heightmaps;
        Sections = sections;
    }

    public IBlock GetBlock(int x, int y, int z)
    {
        var i = SectionIndex(y);

        x = NumericsHelper.Modulo(x, 16);
        y = NumericsHelper.Modulo(y, 16);
        z = NumericsHelper.Modulo(z, 16);

        return Sections[i].GetBlock(x, y, z);
    }

    public BiomeCodec GetBiome(int x, int y, int z)
    {
        var i = SectionIndex(y);

        x = NumericsHelper.Modulo(x, 16) >> 2;
        z = NumericsHelper.Modulo(z, 16) >> 2;
        y = NumericsHelper.Modulo(y + 64, 16) >> 2;

        return Sections[i].GetBiome(x, y, z);
    }

    public void SetBiome(int x, int y, int z, BiomeCodec biome)
    {
        int i = SectionIndex(y);

        x = NumericsHelper.Modulo(x, 16) >> 2;
        y = NumericsHelper.Modulo(y + 64, 16) >> 2;
        z = NumericsHelper.Modulo(z, 16) >> 2;

        Sections[i].SetBiome(x, y, z, biome);
    }

    public IBlockEntity GetBlockEntity(int x, int y, int z) => this.BlockEntities.GetValueOrDefault(this.BlockEntityKey(x, y, z));

    public void SetBlockEntity(int x, int y, int z, IBlockEntity tileEntityData) =>
        this.BlockEntities[this.BlockEntityKey(x, y, z)] = tileEntityData;

    public void RemoveBlockEntity(int x, int y, int z) => this.BlockEntities.Remove(this.BlockEntityKey(x, y, z));

    public IReadOnlyCollection<IBlockEntity> GetBlockEntities() => this.BlockEntities.Values;

    private int BlockEntityKey(int x, int y, int z) =>
        (y - this.MinY) << 8 | NumericsHelper.Modulo(z, 16) << 4 | NumericsHelper.Modulo(x, 16);

    public void SetBlock(int x, int y, int z, IBlock block)
    {
        int i = SectionIndex(y);

        x = NumericsHelper.Modulo(x, 16);
        y = NumericsHelper.Modulo(y, 16);
        z = NumericsHelper.Modulo(z, 16);

        Sections[i].SetBlock(x, y, z, block);
    }

    public BlockMeta GetBlockMeta(int x, int y, int z)
    {
        x = NumericsHelper.Modulo(x, 16);
        y = NumericsHelper.Modulo(y, 16);
        z = NumericsHelper.Modulo(z, 16);
        var value = (short)((x << 8) | (z << 4) | y);

        return BlockMetaStore.GetValueOrDefault(value);
    }

    public void SetBlockMeta(int x, int y, int z, BlockMeta meta)
    {
        x = NumericsHelper.Modulo(x, 16);
        y = NumericsHelper.Modulo(y, 16);
        z = NumericsHelper.Modulo(z, 16);
        var value = (short)((x << 8) | (z << 4) | y);

        BlockMetaStore[value] = meta;
    }

    public void SetLightLevel(int x, int y, int z, LightType lt, int level)
    {
        var sec = Sections[SectionIndex(y)];
        x = NumericsHelper.Modulo(x, 16);
        y = NumericsHelper.Modulo(y, 16);
        z = NumericsHelper.Modulo(z, 16);
        sec.SetLightLevel(x, y, z, lt, level);
    }

    public int GetLightLevel(int x, int y, int z, LightType lt)
    {
        var sec = Sections[SectionIndex(y)];
        x = NumericsHelper.Modulo(x, 16);
        y = NumericsHelper.Modulo(y, 16);
        z = NumericsHelper.Modulo(z, 16);
        return sec.GetLightLevel(x, y, z, lt);
    }

    public void CalculateHeightmap()
    {
        Heightmap target = Heightmaps[HeightmapType.MotionBlocking];
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < width; z++)
            {
                for (int y = this.MinY + this.Height - 1; y >= this.MinY; y--)
                {
                    var block = GetBlock(x, y, z);
                    if (block.Material == Material.Air)
                        continue;

                    target.Set(x, z, value: y);
                    break;
                }
            }
        }
    }

    public void WriteLightMaskTo(INetStreamWriter writer, LightType lt)
    {
        /*
         * BitSet containing bits for each section in the world + 2. 
         * Each set bit indicates that the corresponding 16×16×16 chunk section 
         * has data in the Sky Light array below. 
         * The least significant bit is for blocks 16 blocks to 1 block below 
         * the min world height (one section below the world), 
         * while the most significant bit covers blocks 1 to 16 blocks 
         * above the max world height (one section above the world). 
         * */
        var bs = new BitSet();
        for (int i = 0; i < Sections.Length + 2; i++)
        {
            if (i == 0 || i == Sections.Length + 1)
            {
                continue;
            }
            else
            {
                var hasLight = lt == LightType.Sky ? Sections[i - 1].HasSkyLight : Sections[i - 1].HasBlockLight;
                bs.SetBit(i, hasLight);
            }
        }
        writer.WriteVarInt(bs.DataStorage.Length);
        if (bs.DataStorage.Length != 0)
            writer.WriteLongArray(bs.DataStorage.ToArray());
    }

    public void WriteEmptyLightMaskTo(INetStreamWriter writer, LightType lt)
    {
        var bs = new BitSet();
        for (int i = 0; i < Sections.Length + 2; i++)
        {
            if (i == 0 || i == Sections.Length + 1)
            {
                continue;
            }
            else
            {
                var hasLight = lt == LightType.Sky ? Sections[i - 1].HasSkyLight : Sections[i - 1].HasBlockLight;
                bs.SetBit(i, !hasLight);
            }
        }
        writer.WriteVarInt(bs.DataStorage.Length);
        if (bs.DataStorage.Length != 0)
            writer.WriteLongArray(bs.DataStorage.ToArray());
    }

    public void WriteLightTo(INetStreamWriter writer, LightType lt)
    {
        // Sanity check
        var litSections = Sections.Count(s => lt == LightType.Sky ? s.HasSkyLight : s.HasBlockLight);
        writer.WriteVarInt(litSections);

        if (litSections == 0) { return; }

        for (int a = 0; a < Sections.Length; a++)
        {
            if (lt == LightType.Sky && Sections[a].HasSkyLight)
            {
                writer.WriteVarInt(Sections[a].SkyLightArray.Length);
                writer.WriteByteArray(Sections[a].SkyLightArray.ToArray());
            }
            else if (lt == LightType.Block && Sections[a].HasBlockLight)
            {
                writer.WriteVarInt(Sections[a].BlockLightArray.Length);
                writer.WriteByteArray(Sections[a].BlockLightArray.ToArray());
            }
        }
    }

    public IChunk Clone(int x, int z)
    {
        var sections = new IChunkSection[Sections.Length];
        for (int i = 0; i < sections.Length; i++)
        {
            sections[i] = Sections[i].Clone();
        }

        var heightmaps = new Dictionary<HeightmapType, Heightmap>();

        var chunk = new Chunk(x, z, sections, heightmaps);

        foreach (var (type, heightmap) in Heightmaps)
        {
            heightmaps.Add(type, heightmap.Clone(chunk));
        }

        chunk.SetChunkStatus(ChunkStatus);

        return chunk;
    }

    public void SetChunkStatus(ChunkGenStage status)
    {
        this.ChunkStatus = status;

        if (this.ChunkStatus == ChunkGenStage.full)
        {
            // Free memory safely by removing optional maps
            this.Heightmaps.Remove(HeightmapType.WorldSurfaceWG);
            this.Heightmaps.Remove(HeightmapType.OceanFloor);
            this.Heightmaps.Remove(HeightmapType.OceanFloorWG); // no-op if absent
            this.Heightmaps.Remove(HeightmapType.MotionBlockingNoLeaves);
        }
    }

    private int SectionIndex(int y) => (y - this.MinY) >> 4;
}

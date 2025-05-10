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
    private const int worldHeight = 320;
    private const int worldFloor = -64;

    //TODO try and do some temp caching
    public Dictionary<short, BlockMeta> BlockMetaStore { get; private set; } = new Dictionary<short, BlockMeta>();
    public Dictionary<short, IBlockEntity> BlockEntities { get; private set; } = new Dictionary<short, IBlockEntity>();

    public IChunkSection[] Sections { get; private set; } = new IChunkSection[24];
    public IDictionary<HeightmapType, Heightmap> Heightmaps { get; }

    public Chunk(int x, int z, ChunkGenStage status = ChunkGenStage.empty)
    {
        X = x;
        Z = z;

        Heightmaps = new Dictionary<HeightmapType, Heightmap>()
        {
            { HeightmapType.MotionBlocking, new Heightmap(HeightmapType.MotionBlocking, this) },
            { HeightmapType.OceanFloor, new Heightmap(HeightmapType.OceanFloor, this) },
            { HeightmapType.WorldSurface, new Heightmap(HeightmapType.WorldSurface, this) },
            { HeightmapType.WorldSurfaceWG, new Heightmap(HeightmapType.WorldSurfaceWG, this) },
            { HeightmapType.MotionBlockingNoLeaves, new Heightmap(HeightmapType.MotionBlockingNoLeaves, this) }
        };

        Sections = new ChunkSection[24];
        for (int i = 0; i < Sections.Length; i++)
        {
            Sections[i] = new ChunkSection(4, yBase: i - 4);
        }


    }

    private Chunk(int x, int z, IChunkSection[] sections, Dictionary<HeightmapType, Heightmap> heightmaps)
    {
        X = x;
        Z = z;

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

    public Biome GetBiome(int x, int y, int z)
    {
        var i = SectionIndex(y);

        x = NumericsHelper.Modulo(x, 16) >> 2;
        z = NumericsHelper.Modulo(z, 16) >> 2;
        y = NumericsHelper.Modulo(y + 64, 16) >> 2;

        return Sections[i].GetBiome(x, y, z);
    }

    public void SetBiome(int x, int y, int z, Biome biome)
    {
        int i = SectionIndex(y);

        x = NumericsHelper.Modulo(x, 16) >> 2;
        y = NumericsHelper.Modulo(y + 64, 16) >> 2;
        z = NumericsHelper.Modulo(z, 16) >> 2;

        Sections[i].SetBiome(x, y, z, biome);
    }

    public IBlockEntity GetBlockEntity(int x, int y, int z)
    {
        x = NumericsHelper.Modulo(x, 16);
        z = NumericsHelper.Modulo(z, 16);
        var value = (short)((x << 8) | (z << 4) | y);

        return this.BlockEntities.GetValueOrDefault(value);
    }

    public void SetBlockEntity(int x, int y, int z, IBlockEntity tileEntityData)
    {
        x = NumericsHelper.Modulo(x, 16);
        z = NumericsHelper.Modulo(z, 16);
        var value = (short)((x << 8) | (z << 4) | y);

        this.BlockEntities[value] = tileEntityData;
    }

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
                for (int y = worldHeight - 1; y >= worldFloor; y--)
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

    public void SetChunkStatus(ChunkGenStage status) => this.ChunkStatus = status;

    private static int SectionIndex(int y) => (y >> 4) + 4;
}

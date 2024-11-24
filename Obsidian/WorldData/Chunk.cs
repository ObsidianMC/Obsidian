using Obsidian.Blocks;
using Obsidian.ChunkData;
using Obsidian.Nbt;
using Obsidian.Net;

namespace Obsidian.WorldData;

public class Chunk
{
    public int X { get; }
    public int Z { get; }

    public bool IsGenerated => chunkStatus == ChunkStatus.full;

    public ChunkStatus chunkStatus = ChunkStatus.empty;

    private const int width = 16;
    private const int worldHeight = 320;
    private const int worldFloor = -64;

    //TODO try and do some temp caching
    public Dictionary<short, BlockMeta> BlockMetaStore { get; private set; } = new Dictionary<short, BlockMeta>();
    public Dictionary<short, NbtCompound> BlockEntities { get; private set; } = new Dictionary<short, NbtCompound>();

    public ChunkSection[] Sections { get; private set; } = new ChunkSection[24];
    public Dictionary<HeightmapType, Heightmap> Heightmaps { get; private set; } = new Dictionary<HeightmapType, Heightmap>();


    public Chunk(int x, int z)
    {
        X = x;
        Z = z;

        Heightmaps = new()
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

    private Chunk(int x, int z, ChunkSection[] sections, Dictionary<HeightmapType, Heightmap> heightmaps)
    {
        X = x;
        Z = z;

        Heightmaps = heightmaps;
        Sections = sections;
    }

    public IBlock GetBlock(Vector position) => GetBlock(position.X, position.Y, position.Z);

    public IBlock GetBlock(int x, int y, int z)
    {
        var i = SectionIndex(y);

        x = NumericsHelper.Modulo(x, 16);
        y = NumericsHelper.Modulo(y, 16);
        z = NumericsHelper.Modulo(z, 16);

        return Sections[i].GetBlock(x, y, z);
    }

    public Biome GetBiome(Vector position) => GetBiome(position.X, position.Y, position.Z);

    public Biome GetBiome(int x, int y, int z)
    {
        var i = SectionIndex(y);

        x = NumericsHelper.Modulo(x, 16) >> 2;
        z = NumericsHelper.Modulo(z, 16) >> 2;
        y = NumericsHelper.Modulo(y + 64, 16) >> 2;

        return Sections[i].GetBiome(x, y, z);
    }

    public void SetBiome(Vector position, Biome biome) => SetBiome(position.X, position.Y, position.Z, biome);

    public void SetBiome(int x, int y, int z, Biome biome)
    {
        int i = SectionIndex(y);

        x = NumericsHelper.Modulo(x, 16) >> 2;
        y = NumericsHelper.Modulo(y + 64, 16) >> 2;
        z = NumericsHelper.Modulo(z, 16) >> 2;

        Sections[i].SetBiome(x, y, z, biome);
    }

    public NbtCompound GetBlockEntity(Vector position) => this.GetBlockEntity(position.X, position.Y, position.Z);

    public NbtCompound GetBlockEntity(int x, int y, int z)
    {
        x = NumericsHelper.Modulo(x, 16);
        z = NumericsHelper.Modulo(z, 16);
        var value = (short)((x << 8) | (z << 4) | y);

        return this.BlockEntities.GetValueOrDefault(value);
    }

    public void SetBlockEntity(Vector position, NbtCompound tileEntityData) => this.SetBlockEntity(position.X, position.Y, position.Z, tileEntityData);

    public void SetBlockEntity(int x, int y, int z, NbtCompound tileEntityData)
    {
        x = NumericsHelper.Modulo(x, 16);
        z = NumericsHelper.Modulo(z, 16);
        var value = (short)((x << 8) | (z << 4) | y);

        this.BlockEntities[value] = tileEntityData;
    }

    public void SetBlock(Vector position, IBlock block) => SetBlock(position.X, position.Y, position.Z, block);

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

    public BlockMeta GetBlockMeta(Vector position) => GetBlockMeta(position.X, position.Y, position.Z);

    public void SetBlockMeta(int x, int y, int z, BlockMeta meta)
    {
        x = NumericsHelper.Modulo(x, 16);
        y = NumericsHelper.Modulo(y, 16);
        z = NumericsHelper.Modulo(z, 16);
        var value = (short)((x << 8) | (z << 4) | y);

        BlockMetaStore[value] = meta;
    }

    public void SetBlockMeta(Vector position, BlockMeta meta) => SetBlockMeta(position.X, position.Y, position.Z, meta);

    public void SetLightLevel(Vector position, LightType lt, int light) => this.SetLightLevel(position.X, position.Y, position.Z, lt, light);
    public void SetLightLevel(int x, int y, int z, LightType lt, int level)
    {
        var sec = Sections[SectionIndex(y)];
        x = NumericsHelper.Modulo(x, 16);
        y = NumericsHelper.Modulo(y, 16);
        z = NumericsHelper.Modulo(z, 16);
        sec.SetLightLevel(x, y, z, lt, level);
    }

    public int GetLightLevel(Vector position, LightType lt) => GetLightLevel(position.X, position.Y, position.Z, lt);
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

    public Chunk Clone()
    {
        return Clone(X, Z);
    }

    public Chunk Clone(int x, int z)
    {
        var sections = new ChunkSection[Sections.Length];
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

        chunk.chunkStatus = chunkStatus;

        return chunk;
    }

    private static int SectionIndex(int y) => (y >> 4) + 4;
}

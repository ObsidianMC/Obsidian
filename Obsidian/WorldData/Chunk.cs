using Obsidian.Blocks;
using Obsidian.ChunkData;
using Obsidian.Nbt;

namespace Obsidian.WorldData;

public class Chunk
{
    public int X { get; }
    public int Z { get; }

    public bool isGenerated = false;

    private const int width = 16;
    private const int worldHeight = 320;
    private const int worldFloor = -64;

    public Dictionary<short, BlockMeta> BlockMetaStore { get; private set; } = new();

    public ChunkSection[] Sections { get; private set; }
    public List<INbtTag> BlockEntities { get; private set; } = new();

    public Dictionary<HeightmapType, Heightmap> Heightmaps { get; private set; }

    public Chunk(int x, int z)
    {
        X = x;
        Z = z;

        Heightmaps = new();
        Heightmaps.Add(HeightmapType.MotionBlocking, new Heightmap(HeightmapType.MotionBlocking, this));
        Heightmaps.Add(HeightmapType.OceanFloor, new Heightmap(HeightmapType.OceanFloor, this));
        Heightmaps.Add(HeightmapType.WorldSurface, new Heightmap(HeightmapType.WorldSurface, this));

        Sections = new ChunkSection[24];
        for (int i = 0; i < Sections.Length; i++)
        {
            Sections[i] = new ChunkSection(4, yBase: i - 4);
        }
    }

    private Chunk(int x, int z, ChunkSection[] sections, Dictionary<HeightmapType, Heightmap> heightmaps, bool isGenerated)
    {
        X = x;
        Z = z;

        Heightmaps = heightmaps;
        Sections = sections;

        this.isGenerated = isGenerated;
    }

    public Block GetBlock(Vector position) => GetBlock(position.X, position.Y, position.Z);

    public Block GetBlock(int x, int y, int z)
    {
        var i = SectionIndex(y);
        x = NumericsHelper.Modulo(x, 16);
        y = NumericsHelper.Modulo(y, 16);
        z = NumericsHelper.Modulo(z, 16);

        return Sections[i].GetBlock(x, y, z);
    }

    public Biomes GetBiome(Vector position) => GetBiome(position.X, position.Y, position.Z);

    public Biomes GetBiome(int x, int y, int z)
    {
        var i = SectionIndex(y);

        x = NumericsHelper.Modulo(x, 16);
        z = NumericsHelper.Modulo(z, 16);
        y = (y + 64) % 16 / 4;

        return Sections[i].GetBiome(x, y, z);
    }

    public void SetBiome(Vector position, Biomes biome) => SetBiome(position.X, position.Y, position.Z, biome);

    public void SetBiome(int x, int y, int z, Biomes biome)
    {
        int i = SectionIndex(y);

        x = NumericsHelper.Modulo(x, 16);
        z = NumericsHelper.Modulo(z, 16);
        y = (y + 64) % 16 / 4;

        Sections[i].SetBiome(x, y, z, biome);
    }

    public void SetBlock(Vector position, Block block) => SetBlock(position.X, position.Y, position.Z, block);

    public void SetBlock(int x, int y, int z, Block block)
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
                    if (block.IsAir)
                        continue;

                    target.Set(x, z, value: y);
                    break;
                }
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

        var chunk = new Chunk(x, z, sections, heightmaps, isGenerated);

        foreach (var (type, heightmap) in Heightmaps)
        {
            heightmaps.Add(type, heightmap.Clone(chunk));
        }

        return chunk;
    }

    private static int SectionIndex(int y) => (y >> 4) + 4;
}

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

    public Dictionary<short, BlockMeta> BlockMetaStore { get; private set; } = new Dictionary<short, BlockMeta>();

    public ChunkSection[] Sections { get; private set; } = new ChunkSection[24];
    public List<INbtTag> BlockEntities { get; private set; } = new List<INbtTag>();

    public Dictionary<HeightmapType, Heightmap> Heightmaps { get; private set; } = new Dictionary<HeightmapType, Heightmap>();

    public Chunk(int x, int z)
    {
        this.X = x;
        this.Z = z;

        this.Heightmaps.Add(HeightmapType.MotionBlocking, new Heightmap(HeightmapType.MotionBlocking, this));
        this.Heightmaps.Add(HeightmapType.OceanFloor, new Heightmap(HeightmapType.OceanFloor, this));
        this.Heightmaps.Add(HeightmapType.WorldSurface, new Heightmap(HeightmapType.WorldSurface, this));

        this.Init();
    }

    private void Init()
    {
        for (int i = -4; i < 20; i++)
            this.Sections[i + 4] = new ChunkSection(4, yBase: i);
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
        y = NumericsHelper.Modulo(y, 16);
        z = NumericsHelper.Modulo(z, 16);

        return Sections[i].GetBiome(x, y, z);
    }

    public void SetBiome(Vector position, Biomes biome) => SetBiome(position.X, position.Y, position.Z, biome);

    public void SetBiome(int x, int y, int z, Biomes biome)
    {
        int i = SectionIndex(y);

        y = (y + 64) % 16 / 4;

        var success = Sections[i].SetBiome(x, y, z, biome);

        // Palette dynamic sizing
        if (!success)
        {
            var oldSection = Sections[i];
            var bpb = oldSection.BiomeContainer.BitsPerEntry + 1;
            var newSection = new ChunkSection(4, (byte)bpb, yBase: i);
            for (int sx = 0; sx < 4; sx++)
            {
                for (int sy = 0; sy < 4; sy++)
                {
                    for (int sz = 0; sz < 4; sz++)
                    {
                        // Seems to be the safest way to do this. A bit expensive, though...
                        newSection.SetBiome(sx, sy, sz, oldSection.GetBiome(sx, sy, sz));
                    }
                }
            }

            Sections[i] = newSection;
            SetBiome(x, y, z, biome);
        }
    }

    public void SetBlock(Vector position, Block block) => SetBlock(position.X, position.Y, position.Z, block);

    public void SetBlock(int x, int y, int z, Block block)
    {
        int i = SectionIndex(y);

        x = NumericsHelper.Modulo(x, 16);
        y = NumericsHelper.Modulo(y, 16);
        z = NumericsHelper.Modulo(z, 16);

        var success = Sections[i].SetBlock(x, y, z, block);

        // Palette dynamic sizing
        if (!success)
        {
            var oldSection = Sections[i];
            var bpb = oldSection.BlockStateContainer.BitsPerEntry + 1;
            var newSection = new ChunkSection((byte)bpb, yBase: i);
            for (int sx = 0; sx < 16; sx++)
            {
                for (int sy = 0; sy < 16; sy++)
                {
                    for (int sz = 0; sz < 16; sz++)
                    {
                        // Seems to be the safest way to do this. A bit expensive, though...
                        newSection.SetBlock(sx, sy, sz, oldSection.GetBlock(sx, sy, sz));
                    }
                }
            }

            Sections[i] = newSection;
            SetBlock(x, y, z, block);
        }
    }


    public BlockMeta GetBlockMeta(int x, int y, int z)
    {
        x = NumericsHelper.Modulo(x, 16);
        y = NumericsHelper.Modulo(y, 16);
        z = NumericsHelper.Modulo(z, 16);
        var value = (short)((x << 8) | (z << 4) | y);

        return this.BlockMetaStore.GetValueOrDefault(value);
    }

    public BlockMeta GetBlockMeta(Vector position) => this.GetBlockMeta(position.X, position.Y, position.Z);

    public void SetBlockMeta(int x, int y, int z, BlockMeta meta)
    {
        x = NumericsHelper.Modulo(x, 16);
        y = NumericsHelper.Modulo(y, 16);
        z = NumericsHelper.Modulo(z, 16);
        var value = (short)((x << 8) | (z << 4) | y);

        this.BlockMetaStore[value] = meta;
    }

    public void SetBlockMeta(Vector position, BlockMeta meta) => this.SetBlockMeta(position.X, position.Y, position.Z, meta);

    public void CalculateHeightmap()
    {
        Heightmap target = Heightmaps[HeightmapType.MotionBlocking];
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < width; z++)
            {
                for (int y = worldHeight - 1; y >= worldFloor; y--)
                {
                    try
                    {
                        var block = this.GetBlock(x, y, z);
                        if (block.IsAir)
                            continue;
                    }
                    catch (Exception ex)
                    {
                        var a = 0;
                    }


                    target.Set(x, z, value: y);
                    break;
                }
            }
        }
    }

    private static int SectionIndex(int y) => (y >> 4) + 4;
}

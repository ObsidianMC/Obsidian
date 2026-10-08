using Obsidian.API;
using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.Blocks;
using Obsidian.ChunkData;
using Obsidian.Nbt;
using Obsidian.WorldData.Fluids;
using Obsidian.WorldData.Generators.Mojang.Features;
using System.Threading;

namespace Obsidian.WorldData;

public sealed class Chunk : IChunk
{
    public int X { get; }
    public int Z { get; }
    public long InhabitedTime { get; internal set; }
    internal bool MusicRegistered { get; set; }
    internal ConcurrentDictionary<Vector, int> MobEggTicks { get; } = new();
    internal ConcurrentDictionary<Vector, int> FrogspawnTicks { get; } = new();

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
    public int Height => this.sections.Length << 4;

    //TODO try and do some temp caching
    public Dictionary<short, BlockMeta> BlockMetaStore { get; private set; } = new Dictionary<short, BlockMeta>();
    // Concurrent, since players change block entities while saves read them.
    public ConcurrentDictionary<int, IBlockEntity> BlockEntities { get; private set; } = new();

    /// <summary>
    /// Entities of the chunk that haven't been spawned yet: those world generation placed, and once the chunk is complete,
    /// those loaded with it. The level spawns them on its tick.
    /// </summary>
    /// <remarks>
    /// Once the chunk is complete, only touch these under <see cref="EntityLock"/>.
    /// </remarks>
    internal List<GeneratedEntity> PendingEntities { get; } = [];

    // Saved entities that cannot be restored remain in the chunk's entity file.
    internal List<NbtCompound> UnspawnableEntities { get; } = [];

    /// <summary>
    /// Taken while the chunk's entities move between <see cref="PendingEntities"/> and the level, and while they're saved,
    /// so a save sees every entity exactly once.
    /// </summary>
    internal Lock EntityLock { get; } = new();

    /// <summary>
    /// Whether the chunk was unloaded, with its entities saved and taken out of the level; its pending entities must not
    /// spawn anymore. Set under <see cref="EntityLock"/>.
    /// </summary>
    internal bool EntitiesUnloaded { get; set; }

    /// <summary>
    /// The structure starts saved in the chunk (vanilla's <c>structures.starts</c>), as loaded, or <c>null</c>.
    /// </summary>
    internal NbtCompound? StructureStarts { get; set; }

    /// <summary>
    /// Positions generation marked to check once the chunk is complete: fluids that tick right away and blocks whose state
    /// depends on their neighbors (fence connections, torch support...), like vanilla's post-processing list.
    /// </summary>
    internal List<Vector> PostProcessing { get; } = [];

    /// <summary>
    /// The chunk's scheduled fluid ticks, like vanilla's <c>fluid_ticks</c>.
    /// </summary>
    internal ChunkFluidTicks FluidTicks { get; } = new();

    /// <summary>
    /// The final heightmaps world generation keeps in step with its block changes, from the chunk's first decoration until
    /// its final heightmaps are stored; <c>null</c> otherwise. Only generation may change the chunk's blocks meanwhile.
    /// </summary>
    internal FinalHeightmaps? FinalHeightmaps { get; set; }

    private readonly IChunkSection[] sections;

    public ReadOnlySpan<IChunkSection> Sections => this.sections;
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
        this.sections = new ChunkSection[height >> 4];
        for (int i = 0; i < this.sections.Length; i++)
        {
            this.sections[i] = new ChunkSection(yBase: i + (minY >> 4));
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
        this.sections = sections;
    }

    public IBlock GetBlock(int x, int y, int z)
    {
        var i = SectionIndex(y);

        x = (x & 15);
        y = (y & 15);
        z = (z & 15);

        return this.sections[i].GetBlock(x, y, z);
    }

    public BiomeCodec GetBiome(int x, int y, int z)
    {
        var i = SectionIndex(y);

        x = (x & 15) >> 2;
        z = (z & 15) >> 2;
        y = (y & 15) >> 2;

        return this.sections[i].GetBiome(x, y, z);
    }

    public void SetBiome(int x, int y, int z, BiomeCodec biome)
    {
        int i = SectionIndex(y);

        x = (x & 15) >> 2;
        y = (y & 15) >> 2;
        z = (z & 15) >> 2;

        this.sections[i].SetBiome(x, y, z, biome);
    }

    public IBlockEntity GetBlockEntity(int x, int y, int z) => this.BlockEntities.GetValueOrDefault(this.BlockEntityKey(x, y, z));

    public void SetBlockEntity(int x, int y, int z, IBlockEntity tileEntityData) =>
        this.BlockEntities[this.BlockEntityKey(x, y, z)] = tileEntityData;

    public void RemoveBlockEntity(int x, int y, int z) => this.BlockEntities.TryRemove(this.BlockEntityKey(x, y, z), out _);

    public IReadOnlyCollection<IBlockEntity> GetBlockEntities() => [.. this.BlockEntities.Values];

    private int BlockEntityKey(int x, int y, int z) =>
        (y - this.MinY) << 8 | (z & 15) << 4 | (x & 15);

    public void SetBlock(int x, int y, int z, IBlock block)
    {
        // Generation writes through its regions' sections; any other write (a live edit of an unfinished chunk) leaves the
        // heights generation keeps stale, so they're computed again.
        this.FinalHeightmaps = null;

        int i = SectionIndex(y);

        x = (x & 15);
        y = (y & 15);
        z = (z & 15);

        this.sections[i].SetBlock(x, y, z, block);
    }

    public BlockMeta GetBlockMeta(int x, int y, int z)
    {
        x = (x & 15);
        y = (y & 15);
        z = (z & 15);
        var value = (short)((x << 8) | (z << 4) | y);

        return BlockMetaStore.GetValueOrDefault(value);
    }

    public void SetBlockMeta(int x, int y, int z, BlockMeta meta)
    {
        x = (x & 15);
        y = (y & 15);
        z = (z & 15);
        var value = (short)((x << 8) | (z << 4) | y);

        BlockMetaStore[value] = meta;
    }

    public void SetLightLevel(int x, int y, int z, LightType lt, int level)
    {
        var sec = this.sections[SectionIndex(y)];
        x = (x & 15);
        y = (y & 15);
        z = (z & 15);
        sec.SetLightLevel(x, y, z, lt, level);
    }

    public int GetLightLevel(int x, int y, int z, LightType lt)
    {
        var sec = this.sections[SectionIndex(y)];
        x = (x & 15);
        y = (y & 15);
        z = (z & 15);
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
        for (int i = 0; i < this.sections.Length + 2; i++)
        {
            if (i == 0 || i == this.sections.Length + 1)
            {
                continue;
            }
            else
            {
                var hasLight = lt == LightType.Sky ? this.sections[i - 1].HasSkyLight : this.sections[i - 1].HasBlockLight;
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
        for (int i = 0; i < this.sections.Length + 2; i++)
        {
            if (i == 0 || i == this.sections.Length + 1)
            {
                continue;
            }
            else
            {
                var hasLight = lt == LightType.Sky ? this.sections[i - 1].HasSkyLight : this.sections[i - 1].HasBlockLight;
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
        var litSections = this.sections.Count(s => lt == LightType.Sky ? s.HasSkyLight : s.HasBlockLight);
        writer.WriteVarInt(litSections);

        if (litSections == 0) { return; }

        for (int a = 0; a < this.sections.Length; a++)
        {
            if (lt == LightType.Sky && this.sections[a].HasSkyLight)
            {
                writer.WriteVarInt(this.sections[a].SkyLightArray.Length);
                writer.WriteByteArray(this.sections[a].SkyLightArray.ToArray());
            }
            else if (lt == LightType.Block && this.sections[a].HasBlockLight)
            {
                writer.WriteVarInt(this.sections[a].BlockLightArray.Length);
                writer.WriteByteArray(this.sections[a].BlockLightArray.ToArray());
            }
        }
    }

    public IChunk Clone(int x, int z)
    {
        var sections = new IChunkSection[this.sections.Length];
        for (int i = 0; i < sections.Length; i++)
        {
            sections[i] = this.sections[i].Clone();
        }

        var heightmaps = new Dictionary<HeightmapType, Heightmap>();

        var chunk = new Chunk(x, z, sections, heightmaps);

        foreach (var (type, heightmap) in Heightmaps)
        {
            heightmaps.Add(type, heightmap.Clone(chunk));
        }

        chunk.SetChunkStatus(ChunkStatus);
        chunk.InhabitedTime = InhabitedTime;

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

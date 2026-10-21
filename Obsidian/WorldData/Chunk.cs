using Obsidian.API;
using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.Blocks;
using Obsidian.ChunkData;
using Obsidian.Nbt;
using Obsidian.WorldData.Fluids;
using Obsidian.WorldData.Lighting;
using Obsidian.WorldData.Generators.Mojang.Features;
using Obsidian.WorldData.Ticks;
using System.Threading;

namespace Obsidian.WorldData;

public sealed class Chunk : IChunk
{
    private volatile RuntimeLightEngine? runtimeLight;

    internal RuntimeLightEngine? RuntimeLight => this.runtimeLight;

    // The region completes generation and attaches lighting under the tick lock after validating slot identity.
    internal Action<Chunk>? CompleteGeneration { get; set; }

    /// <summary>Attaches live section writes together with the chunk's runtime light hook.</summary>
    internal void AttachRuntimeLight(RuntimeLightEngine? light)
    {
        this.runtimeLight = light;
        foreach (var section in this.sections.OfType<ChunkSection>())
            section.LiveChunk = light is null ? null : this;
    }

    public int X { get; }
    public int Z { get; }

    public long InhabitedTime
    {
        get => Interlocked.Read(ref this.inhabitedTime);
        internal set
        {
            Interlocked.Exchange(ref this.inhabitedTime, value);
            this.MarkUnsaved();
        }
    }

    private long inhabitedTime;

    // 1 while the chunk holds changes its last save doesn't have, besides those its sections, heightmaps and tick lists
    // track (see TakeUnsaved). A chunk is unsaved when created or loaded.
    private int unsaved = 1;

    // The attachments' revision the last save took, so a save can tell whether plugins changed them since.
    private long savedAttachmentRevision = -1;

    // A hash of the saved data of the block entities saved as they are, as the last save took it (see TakeUnsaved).
    private ulong savedLiveBlockEntities;

    // Which heightmaps the chunk had at its last save, since entries can be replaced or removed (see HeightmapSet).
    private int savedHeightmaps;

    // Which block entities the chunk held at its last save, since the dictionary can be changed directly (see
    // BlockEntitySet).
    private int savedBlockEntities;

    /// <summary>
    /// Records a change to what the chunk saves that its sections and tick lists don't track themselves: block
    /// entities, its status, structure data, and the timers it keeps for eggs, frogspawn and portals.
    /// </summary>
    internal void MarkUnsaved() => Volatile.Write(ref this.unsaved, 1);

    /// <summary>
    /// Whether the chunk may differ from its last save, so a save has to write it. Read without locks: a change racing
    /// the read marks the chunk again, and the next save writes it.
    /// </summary>
    /// <remarks>
    /// Block entities saved as they are (<c>BlockEntity.SavesOnAnyThread</c>, like spawners counting down) and
    /// containers, whose items are saved as they are, change without telling the chunk, so a chunk holding one may be
    /// unsaved: <see cref="TakeUnsaved"/> compares their saved data with the last save's.
    /// </remarks>
    internal bool IsUnsaved
    {
        get
        {
            if (Volatile.Read(ref this.unsaved) != 0 || this.BlockTicks.IsUnsaved || this.FluidTicks.IsUnsaved)
                return true;

            foreach (var section in this.sections)
            {
                if (section is not ChunkSection { IsUnsaved: false })
                    return true;
            }

            foreach (var heightmap in this.Heightmaps.Values)
            {
                if (heightmap.IsUnsaved)
                    return true;
            }

            return this.HeightmapSet() != Volatile.Read(ref this.savedHeightmaps)
                || this.BlockEntitySet() != Volatile.Read(ref this.savedBlockEntities)
                || API.Data.AttachmentStorage.For(this).Revision != Interlocked.Read(ref this.savedAttachmentRevision)
                || this.HoldsLiveSavedBlockEntities();
        }
    }

    /// <summary>
    /// Clears every unsaved mark of the chunk, for a save about to snapshot it. Call it under the locks the snapshot
    /// takes, before taking it: a change made after it marks the chunk again, so the next save has it. A save that
    /// fails afterwards marks the chunk again (<see cref="MarkUnsaved"/>).
    /// </summary>
    /// <returns>Whether the chunk was unsaved (see <see cref="IsUnsaved"/>).</returns>
    internal bool TakeUnsaved()
    {
        // Every mark is cleared, so none of these short-circuit.
        var unsaved = Interlocked.Exchange(ref this.unsaved, 0) != 0;
        unsaved |= this.BlockTicks.TakeUnsaved();
        unsaved |= this.FluidTicks.TakeUnsaved();

        foreach (var section in this.sections)
            unsaved |= section is not ChunkSection tracked || tracked.TakeUnsaved();

        foreach (var heightmap in this.Heightmaps.Values)
            unsaved |= heightmap.TakeUnsaved();

        var heightmaps = this.HeightmapSet();
        unsaved |= Interlocked.Exchange(ref this.savedHeightmaps, heightmaps) != heightmaps;

        var blockEntities = this.BlockEntitySet();
        unsaved |= Interlocked.Exchange(ref this.savedBlockEntities, blockEntities) != blockEntities;

        var attachments = API.Data.AttachmentStorage.For(this).Revision;
        unsaved |= Interlocked.Exchange(ref this.savedAttachmentRevision, attachments) != attachments;

        if (!this.HoldsLiveSavedBlockEntities())
        {
            this.savedLiveBlockEntities = 0;
            return unsaved;
        }

        var liveBlockEntities = this.HashLiveSavedBlockEntities();
        unsaved |= liveBlockEntities != this.savedLiveBlockEntities;
        this.savedLiveBlockEntities = liveBlockEntities;
        return unsaved;
    }

    // The heightmaps the chunk has, as instances: an order-independent combination, which changes when one is
    // replaced or removed.
    private int HeightmapSet()
    {
        var set = 0;
        foreach (var (type, heightmap) in this.Heightmaps)
            set += HashCode.Combine(type, System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(heightmap));

        return set;
    }

    // The block entities the chunk holds, by position and instance: an order-independent combination, like
    // HeightmapSet's.
    private int BlockEntitySet()
    {
        var set = 0;
        foreach (var (key, blockEntity) in this.BlockEntities)
            set += HashCode.Combine(key, System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(blockEntity));

        return set;
    }

    // The saved data of the block entities saved as they are, hashed in position order.
    private ulong HashLiveSavedBlockEntities()
    {
        var hash = new System.IO.Hashing.XxHash64();
        foreach (var (_, blockEntity) in this.BlockEntities.OrderBy(entry => entry.Key))
        {
            if (!SavesAsItIs(blockEntity) || BlockEntityNbt.Save(blockEntity) is not NbtCompound saved)
                continue;

            // The writer closes the stream, which can still give its bytes.
            var stream = new System.IO.MemoryStream();
            using (var writer = new NbtWriterStream(stream, NbtCompression.None, ""))
            {
                writer.WriteTag(saved);
                writer.EndCompound();
            }

            hash.Append(stream.ToArray());
        }

        return hash.GetCurrentHashAsUInt64();
    }

    private bool HoldsLiveSavedBlockEntities()
    {
        foreach (var blockEntity in this.BlockEntities.Values)
        {
            if (SavesAsItIs(blockEntity))
                return true;
        }

        return false;
    }

    // Whether a save takes the block entity's data as it is now, rather than as it last announced a change: those
    // saved on any thread, and containers, whose items are saved as they are.
    private static bool SavesAsItIs(IBlockEntity blockEntity) =>
        blockEntity is API.BlockEntities.BlockEntity { SavesOnAnyThread: true } or API.BlockEntities.ContainerBlockEntity;
    internal bool MusicRegistered { get; set; }

    /// <summary>
    /// The block entity type registry's revision the level last made the chunk's kept plugin block entities its
    /// plugins' at (see AbstractLevel.RestoreKeptBlockEntities); -1 until it first did.
    /// </summary>
    internal int RestoredBlockEntityRevision { get; set; } = -1;
    internal ConcurrentDictionary<Vector, int> MobEggTicks { get; } = new();
    internal ConcurrentDictionary<Vector, int> FrogspawnTicks { get; } = new();

    /// <summary>The portal types that lit or built the chunk's portal blocks (see Portals.PortalOwnership).</summary>
    internal ConcurrentDictionary<Vector, Portals.PortalOwner> PortalOwners { get; } = new();

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
    /// How many entities the chunk's entity file listed when the region last wrote it, or -1 until it did. A chunk
    /// whose file lists none needs no entity save while it has no entities.
    /// </summary>
    internal int SavedEntityCount { get; set; } = -1;

    /// <summary>
    /// The structure starts saved in the chunk (vanilla's <c>structures.starts</c>), as loaded, or <c>null</c>.
    /// </summary>
    internal NbtCompound? StructureStarts
    {
        get;
        set
        {
            field = value;
            this.MarkUnsaved();
        }
    }

    /// <summary>
    /// The structure references saved in the chunk (vanilla's <c>structures.References</c>), as loaded, or <c>null</c>.
    /// Obsidian finds references from the starts around the chunk, but vanilla reads them from the save.
    /// </summary>
    internal NbtCompound? StructureReferences
    {
        get;
        set
        {
            field = value;
            this.MarkUnsaved();
        }
    }

    /// <summary>
    /// Top-level tags of the saved chunk that Obsidian doesn't model, like vanilla's <c>blending_data</c>. They're
    /// saved back as loaded, so saving a vanilla world doesn't drop them.
    /// </summary>
    internal List<INbtTag> UnmodeledTags { get; } = [];

    /// <summary>
    /// Positions generation marked to check once the chunk is complete: fluids that tick right away and blocks whose state
    /// depends on their neighbors (fence connections, torch support...), like vanilla's post-processing list.
    /// </summary>
    internal List<Vector> PostProcessing { get; } = [];

    /// <summary>
    /// The chunk's scheduled fluid ticks, like vanilla's <c>fluid_ticks</c>.
    /// </summary>
    internal ChunkTicks<FluidKind> FluidTicks { get; } = new();

    /// <summary>
    /// The chunk's scheduled block ticks, like vanilla's <c>block_ticks</c>, by block identifier. Ticks of a plugin's
    /// block are kept while the plugin is unloaded, and run once it's back.
    /// </summary>
    internal ChunkTicks<string> BlockTicks { get; } = new();

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
        ChunkStatus = status;

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

    public void SetBlockEntity(int x, int y, int z, IBlockEntity tileEntityData)
    {
        this.BlockEntities[this.BlockEntityKey(x, y, z)] = tileEntityData;
        this.MarkUnsaved();
    }

    /// <summary>
    /// Stores <paramref name="blockEntity"/> at the position unless a block entity is there already, in one step, so
    /// that callers racing to create one all get the same.
    /// </summary>
    /// <returns>
    /// The block entity stored at the position: the existing one, or else <paramref name="blockEntity"/>.
    /// </returns>
    internal IBlockEntity GetOrAddBlockEntity(int x, int y, int z, IBlockEntity blockEntity)
    {
        var stored = this.BlockEntities.GetOrAdd(this.BlockEntityKey(x, y, z), blockEntity);
        if (ReferenceEquals(stored, blockEntity))
            this.MarkUnsaved();

        return stored;
    }

    public void RemoveBlockEntity(int x, int y, int z)
    {
        if (this.BlockEntities.TryRemove(this.BlockEntityKey(x, y, z), out _))
            this.MarkUnsaved();
    }

    /// <summary>
    /// Stores <paramref name="replacement"/> in place of <paramref name="current"/>, at its position, unless another
    /// block entity took its place meanwhile.
    /// </summary>
    /// <returns>Whether <paramref name="current"/> was replaced.</returns>
    internal bool ReplaceBlockEntity(IBlockEntity current, IBlockEntity replacement)
    {
        var position = current.BlockPosition;
        var key = this.BlockEntityKey(position.X, position.Y, position.Z);

        if (!this.BlockEntities.TryUpdate(key, replacement, current))
            return false;

        this.MarkUnsaved();
        return true;
    }

    public IReadOnlyCollection<IBlockEntity> GetBlockEntities() => [.. this.BlockEntities.Values];

    private int BlockEntityKey(int x, int y, int z) =>
        (y - this.MinY) << 8 | (z & 15) << 4 | (x & 15);

    public void SetBlock(int x, int y, int z, IBlock block)
    {
        if (this.RuntimeLight is RuntimeLightEngine light)
        {
            lock (light.TickLock)
            {
                var position = new Vector((this.X << 4) + (x & 15), y, (this.Z << 4) + (z & 15));
                // A caller may have captured the hook just before unload detached it.
                if (ReferenceEquals(this.RuntimeLight, light))
                    light.BlockChanging(this, position, block);

                this.SetBlockCore(x, y, z, block);
            }

            return;
        }

        this.SetBlockCore(x, y, z, block);
    }

    private void SetBlockCore(int x, int y, int z, IBlock block)
    {
        // The claim belongs to this block, so even two replacements between portal ticks release it.
        Portals.PortalOwnership.Release(
            this, new Vector((this.X << 4) + (x & 15), y, (this.Z << 4) + (z & 15)), block);

        // Generation writes through its regions' sections; any other write (a live edit of an unfinished chunk) leaves the
        // heights generation keeps stale, so they're computed again.
        this.FinalHeightmaps = null;

        int i = SectionIndex(y);

        x = (x & 15);
        y = (y & 15);
        z = (z & 15);

        ((ChunkSection)this.sections[i]).SetBlockUntracked(x, y, z, block);
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
        this.MarkUnsaved();

        Heightmap target = Heightmaps[HeightmapType.MotionBlocking];
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < width; z++)
            {
                for (int y = this.MinY + this.Height - 1; y >= this.MinY; y--)
                {
                    var block = GetBlock(x, y, z);
                    if (block.Is(BlockIds.Air))
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
        if (status == ChunkGenStage.full && this.CompleteGeneration is Action<Chunk> complete)
        {
            complete(this);
            return;
        }

        this.SetChunkStatusCore(status);
    }

    /// <summary>
    /// Updates generation storage; the region calls this under its tick lock for a live full transition.
    /// </summary>
    internal void SetChunkStatusCore(ChunkGenStage status)
    {
        this.ChunkStatus = status;
        this.MarkUnsaved();

        if (this.ChunkStatus == ChunkGenStage.full)
        {
            // Free memory safely by removing optional maps
            this.Heightmaps.Remove(HeightmapType.WorldSurfaceWG);
            this.Heightmaps.Remove(HeightmapType.OceanFloor);
            this.Heightmaps.Remove(HeightmapType.OceanFloorWG); // no-op if absent
            this.Heightmaps.Remove(HeightmapType.MotionBlockingNoLeaves);
        }
    }

    /// <summary>
    /// Takes a chunk that isn't carved yet back to before its biomes, dropping its blocks, biomes, heightmaps and
    /// post-processing marks, so its steps replay from scratch. Until a chunk is carved, only its own steps have
    /// written into it; what it was loaded with, like its structure starts, stays.
    /// </summary>
    internal void RewindTerrain()
    {
        for (var i = 0; i < this.sections.Length; i++)
            this.sections[i] = new ChunkSection(yBase: i + (this.MinY >> 4));

        foreach (var type in this.Heightmaps.Keys.ToList())
            this.Heightmaps[type] = new Heightmap(type, this);

        this.PostProcessing.Clear();
        this.ChunkStatus = ChunkGenStage.structure_references;
        this.MarkUnsaved();
    }

    private int SectionIndex(int y) => (y - this.MinY) >> 4;
}

internal static class ChunkHeights
{
    /// <summary>
    /// The first y above the column's highest block counted by <paramref name="type"/>; scans the column when the
    /// chunk no longer stores that heightmap (full chunks drop the optional ones).
    /// </summary>
    internal static int GetHeight(this IChunk chunk, HeightmapType type, int x, int z)
    {
        x &= 15;
        z &= 15;
        if (chunk.Heightmaps.TryGetValue(type, out var heightmap))
            return heightmap.GetHeight(x, z);

        for (var y = chunk.MinY + chunk.Height - 1; y >= chunk.MinY; y--)
            if (Generators.Mojang.WorldgenHeightmaps.Matches(type, chunk.GetBlock(x, y, z)))
                return y + 1;

        return chunk.MinY;
    }
}

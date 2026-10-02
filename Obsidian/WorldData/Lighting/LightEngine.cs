using Obsidian.ChunkData;

namespace Obsidian.WorldData.Lighting;

/// <summary>
/// Vanilla's light engine (<c>BlockLightEngine</c> and <c>SkyLightEngine</c>) for chunks whose blocks are final.
/// </summary>
/// <remarks>
/// <para>
/// Vanilla lights chunks incrementally in one engine for the whole level. Once every chunk around a chunk is lit, the
/// chunk's light is the fixed point of the engine's propagation rules, whatever order the chunks were lit in. This engine
/// reaches the same fixed point one chunk at a time: <see cref="LightChunk"/> starts from the chunk's own sources (sky
/// light sources and light emitting blocks) and from the light at the borders of its lit neighbors, then spreads that
/// light through the chunk and its lit neighbors with vanilla's rules. Light never travels further than 15 blocks, so it
/// can't leave those chunks. Unlit neighbors are left alone; lighting them later pulls this chunk's light in.
/// </para>
/// <para>
/// Light only ever rises here, which is all generation needs. Runtime block updates would also need vanilla's decrease
/// pass, which takes light away before spreading it again.
/// </para>
/// <para>
/// Light is stored and spread within the build range. Vanilla also keeps light for one section below and one above it;
/// the only light that matters from them is the sky light above the build range shining down into the top layer, which is
/// spread from a virtual source above it.
/// </para>
/// </remarks>
internal sealed class LightEngine
{
    // A position packs the column's offset from the lit chunk's corner, plus 16 so it's never negative (x in bits 0-5,
    // z in bits 6-11), and the height above the bottom of the build range (bits 12 and up).
    private const int XStep = 1;
    private const int ZStep = 1 << 6;
    private const int YStep = 1 << 12;
    private const int AreaEnd = 48;

    // A queue entry pairs a position (high 32 bits) with vanilla's QueueEntry flags: the light level the entry spreads
    // (bits 0-3), one bit per BlockFace to spread toward (bits 4-9) and whether the level is the block's own emission.
    private const int LevelMask = 15;
    private const int DirectionShift = 4;
    private const int AllDirections = 0b111111 << DirectionShift;
    private const int FromEmission = 1 << 10;

    private static readonly int[] directionSteps = [-YStep, YStep, -ZStep, ZStep, -XStep, XStep];

    // The lit chunk and its neighbors that may be read and written, indexed (dz + 1) * 3 + (dx + 1).
    private readonly IChunk?[] area = new IChunk?[9];

    // The sections of the area's chunks, indexed by area index times the section count plus the section index.
    private readonly IChunkSection?[] sections;

    // The same sections when they're all ChunkSections, whose light storage is read and written directly; otherwise null,
    // and light goes through the IChunkSection methods.
    private readonly ChunkSection?[]? storageSections;
    private readonly int sectionCount;
    private readonly int minY;
    private readonly int height;
    private readonly Queue<long> queue;

    // An emptied queue for the thread's next engine. Taken while in use.
    [ThreadStatic]
    private static Queue<long>? freeQueue;

    private LightEngine(IChunk chunk, IEnumerable<IChunk> litNeighbors, Queue<long> queue)
    {
        this.queue = queue;
        this.area[4] = chunk;
        this.minY = chunk.MinY;
        this.height = chunk.Height;

        foreach (var neighbor in litNeighbors)
        {
            var dx = neighbor.X - chunk.X;
            var dz = neighbor.Z - chunk.Z;
            if (Math.Abs(dx) > 1 || Math.Abs(dz) > 1 || (dx == 0 && dz == 0))
                throw new ArgumentException($"Chunk ({neighbor.X}, {neighbor.Z}) isn't a neighbor of ({chunk.X}, {chunk.Z}).", nameof(litNeighbors));

            this.area[(dz + 1) * 3 + dx + 1] = neighbor;
        }

        var sectionCount = this.sectionCount = chunk.Sections.Length;
        this.sections = new IChunkSection?[9 * sectionCount];
        this.storageSections = new ChunkSection?[9 * sectionCount];
        for (var areaIndex = 0; areaIndex < 9; areaIndex++)
        {
            if (this.area[areaIndex] is not { } areaChunk)
                continue;

            for (var sectionIndex = 0; sectionIndex < sectionCount; sectionIndex++)
            {
                var section = areaChunk.Sections[sectionIndex];
                this.sections[areaIndex * sectionCount + sectionIndex] = section;

                if (section is ChunkSection storageSection && this.storageSections is not null)
                    this.storageSections[areaIndex * sectionCount + sectionIndex] = storageSection;
                else
                    this.storageSections = null;
            }
        }
    }

    /// <summary>
    /// Computes the sky and block light of <paramref name="chunk"/> and spreads light between it and its lit neighbors.
    /// </summary>
    /// <remarks>
    /// The light is final once every neighbor is lit too, provided no block of the chunk or its neighbors changes after
    /// they're lit. The caller must make sure nothing else reads or writes these chunks meanwhile.
    /// </remarks>
    /// <param name="chunk">The chunk to light. Its blocks must be final.</param>
    /// <param name="litNeighbors">The neighbors of <paramref name="chunk"/> (up to 8) that are already lit; light is
    /// written into them.</param>
    /// <param name="hasSkyLight">Whether the dimension has sky light. Without it, the chunk's sky light is left alone.</param>
    public static void LightChunk(IChunk chunk, IEnumerable<IChunk> litNeighbors, bool hasSkyLight)
    {
        var queue = freeQueue ?? new Queue<long>();
        freeQueue = null;

        var engine = new LightEngine(chunk, litNeighbors, queue);
        engine.LightBlocks();

        if (hasSkyLight)
            engine.LightSky();

        // Propagating empties the queue.
        freeQueue = queue;
    }

    /// <summary>
    /// <see cref="LightChunk"/> with the neighbors that <paramref name="level"/> has at <see cref="ChunkGenStage.light"/>
    /// or later.
    /// </summary>
    /// <remarks>
    /// This takes no locks; like <see cref="LightChunk"/>, it relies on the caller to keep the chunks from changing meanwhile.
    /// </remarks>
    public static async ValueTask LightChunkAsync(IChunk chunk, ILevel level, bool hasSkyLight = true)
    {
        var litNeighbors = new List<IChunk>();
        for (var dz = -1; dz <= 1; dz++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dz == 0)
                    continue;

                var neighbor = await level.GetChunkAsync(chunk.X + dx, chunk.Z + dz, scheduleGeneration: false);
                if (neighbor is not null && neighbor.ChunkStatus >= ChunkGenStage.light)
                    litNeighbors.Add(neighbor);
            }
        }

        LightChunk(chunk, litNeighbors, hasSkyLight);
    }

    /// <summary>
    /// Vanilla's <c>BlockLightEngine.propagateLightSources</c>: every light emitting block of the chunk spreads its light.
    /// </summary>
    private void LightBlocks()
    {
        var sections = this.area[4]!.Sections;
        for (var sectionIndex = 0; sectionIndex < sections.Length; sectionIndex++)
        {
            var section = sections[sectionIndex];
            if (this.storageSections is not null)
                Array.Clear(this.storageSections[4 * this.sectionCount + sectionIndex]!.GetLightStorage(LightType.Block));
            else
                section.SetLight(new byte[2048], LightType.Block);

            if (!section.IsEmpty)
                this.EnqueueLightSources(section, sectionIndex);
        }

        this.PullFromNeighbors(LightType.Block);
        this.Propagate(LightType.Block);
    }

    /// <summary>
    /// Queues the light emitting blocks of a section in index order (y, then z, then x). Like vanilla's
    /// <c>PalettedContainer.maybeHas</c>, the palette is checked first, as most sections have no light source; the others
    /// are scanned by palette index rather than block by block. A palette may still list blocks that are gone, which only
    /// costs a scan.
    /// </summary>
    private void EnqueueLightSources(IChunkSection section, int sectionIndex)
    {
        var container = section.BlockStateContainer;
        if (container.Palette is IndirectBlockPalette palette && container.DataArray is { } data)
        {
            Span<int> emissions = stackalloc int[palette.Count];
            var anyEmission = false;
            for (var i = 0; i < emissions.Length; i++)
            {
                emissions[i] = BlocksRegistry.Get(palette.Values[i]).LightEmission();
                anyEmission |= emissions[i] > 0;
            }

            if (!anyEmission)
                return;

            for (var index = 0; index < 4096; index++)
            {
                var emission = emissions[data[index]];
                if (emission > 0)
                    this.Enqueue(Pack(index & 15, (sectionIndex << 4) + (index >> 8), (index >> 4) & 15), emission | AllDirections | FromEmission);
            }

            return;
        }

        if (container.Palette is SingleValuePalette<IBlock> single && single.IsFull && single.Value.LightEmission() == 0)
            return;

        for (var y = 0; y < 16; y++)
        {
            for (var z = 0; z < 16; z++)
            {
                for (var x = 0; x < 16; x++)
                {
                    var emission = section.GetBlock(x, y, z).LightEmission();
                    if (emission > 0)
                        this.Enqueue(Pack(x, (sectionIndex << 4) + y, z), emission | AllDirections | FromEmission);
                }
            }
        }
    }

    /// <summary>
    /// Vanilla's <c>SkyLightEngine.propagateLightSources</c>: every position at or above its column's lowest source gets
    /// sky light 15, and sources spread down from the lowest one and sideways into columns whose sources start higher.
    /// </summary>
    private void LightSky()
    {
        var chunk = this.area[4]!;
        Span<int> lowest = stackalloc int[256];
        for (var z = 0; z < 16; z++)
        {
            for (var x = 0; x < 16; x++)
                lowest[z * 16 + x] = SkyLightSources.LowestSourceY(chunk, x, z);
        }

        // The lowest sources of the neighboring columns across each border. Light isn't spread into unlit neighbors, so
        // theirs count as starting below the world.
        Span<int> north = stackalloc int[16];
        Span<int> south = stackalloc int[16];
        Span<int> west = stackalloc int[16];
        Span<int> east = stackalloc int[16];
        for (var i = 0; i < 16; i++)
        {
            north[i] = this.NeighborLowestSourceY(1, i, 15);
            south[i] = this.NeighborLowestSourceY(7, i, 0);
            west[i] = this.NeighborLowestSourceY(3, 15, i);
            east[i] = this.NeighborLowestSourceY(5, 0, i);
        }

        // Below world sources are int.MinValue, so they count as the lowest.
        var lowestSource = int.MaxValue;
        var highestSource = int.MinValue;
        foreach (var source in lowest)
        {
            lowestSource = Math.Min(lowestSource, source);
            highestSource = Math.Max(highestSource, source);
        }

        for (var sectionIndex = 0; sectionIndex < this.sectionCount; sectionIndex++)
        {
            var bottom = this.minY + (sectionIndex << 4);
            var section = this.sections[4 * this.sectionCount + sectionIndex]!;
            var light = this.storageSections is null
                ? new byte[2048]
                : this.storageSections[4 * this.sectionCount + sectionIndex]!.GetLightStorage(LightType.Sky);

            // Sections that every column's sources cover, or that none reach, need no work per column.
            if (highestSource <= bottom)
            {
                light.AsSpan().Fill(0xFF);
            }
            else
            {
                Array.Clear(light);
            }

            if (highestSource > bottom && lowestSource < bottom + 16)
            {
                for (var z = 0; z < 16; z++)
                {
                    for (var x = 0; x < 16; x++)
                    {
                        var source = lowest[z * 16 + x];
                        for (var y = source == SkyLightSources.BelowWorld ? 0 : Math.Max(source - bottom, 0); y < 16; y++)
                        {
                            var index = (y << 8) | (z << 4) | x;
                            light[index >> 1] |= (byte)(15 << ((index & 1) << 2));
                        }
                    }
                }
            }

            // Records that the section has sky light if any is set, like for a new array.
            section.SetLight(light, LightType.Sky);
        }

        var top = this.minY + this.height;
        for (var z = 0; z < 16; z++)
        {
            for (var x = 0; x < 16; x++)
            {
                var source = lowest[z * 16 + x];
                var northSource = z == 0 ? north[x] : lowest[(z - 1) * 16 + x];
                var southSource = z == 15 ? south[x] : lowest[(z + 1) * 16 + x];
                var westSource = x == 0 ? west[z] : lowest[z * 16 + x - 1];
                var eastSource = x == 15 ? east[z] : lowest[z * 16 + x + 1];
                var highestNeighborSource = Math.Max(Math.Max(northSource, southSource), Math.Max(westSource, eastSource));

                // A column whose top block stops full sky light has its lowest source above the build range, in vanilla's
                // light section above it; that source still shines down into the top block.
                if (source >= top)
                    this.LightFromAbove(x, z);

                // Sources above every neighboring column's sources have nothing to light.
                var end = Math.Min(top, source == SkyLightSources.BelowWorld ? highestNeighborSource : Math.Max(highestNeighborSource, source + 1));
                for (var y = Math.Max(source, this.minY); y < end; y++)
                {
                    var directions = 0;
                    if (y == source)
                        directions |= Bit(BlockFace.Down);
                    if (y < northSource)
                        directions |= Bit(BlockFace.North);
                    if (y < southSource)
                        directions |= Bit(BlockFace.South);
                    if (y < westSource)
                        directions |= Bit(BlockFace.West);
                    if (y < eastSource)
                        directions |= Bit(BlockFace.East);

                    if (directions != 0)
                        this.Enqueue(Pack(x, y - this.minY, z), 15 | directions);
                }
            }
        }

        this.PullFromNeighbors(LightType.Sky);
        this.Propagate(LightType.Sky);
    }

    /// <summary>
    /// Spreads full sky light from just above the build range down into the top block of column (<paramref name="x"/>,
    /// <paramref name="z"/>), like vanilla's sources in the light section above the world.
    /// </summary>
    private void LightFromAbove(int x, int z)
    {
        var position = Pack(x, this.height - 1, z);
        var block = this.GetBlock(position);
        var level = 15 - Math.Max(1, block.LightBlock());
        if (level <= this.GetLight(position, LightType.Sky) || BlockLight.LightShapesOcclude(BlocksRegistry.Air, block, BlockFace.Down))
            return;

        this.SetLight(position, LightType.Sky, level);
        if (level > 1)
            this.Enqueue(position, level | (AllDirections & ~Bit(BlockFace.Up)));
    }

    /// <summary>
    /// The lowest sky light source of a column of a lit neighbor, or <see cref="SkyLightSources.BelowWorld"/> without one.
    /// </summary>
    private int NeighborLowestSourceY(int areaIndex, int x, int z)
    {
        var neighbor = this.area[areaIndex];
        return neighbor is null ? SkyLightSources.BelowWorld : SkyLightSources.LowestSourceY(neighbor, x, z);
    }

    /// <summary>
    /// Queues the light at the borders of the lit orthogonal neighbors to spread into the chunk.
    /// </summary>
    private void PullFromNeighbors(LightType lightType)
    {
        foreach (var (areaIndex, direction) in neighborBorders)
        {
            if (this.area[areaIndex] is null)
                continue;

            var step = directionSteps[(int)direction];
            for (var yIndex = 0; yIndex < this.height; yIndex++)
            {
                for (var i = 0; i < 16; i++)
                {
                    // The neighbor's column next to column i of the chunk's border on that side.
                    var position = direction switch
                    {
                        BlockFace.South => Pack(i, yIndex, -1),
                        BlockFace.North => Pack(i, yIndex, 16),
                        BlockFace.East => Pack(-1, yIndex, i),
                        _ => Pack(16, yIndex, i)
                    };

                    var level = this.GetLight(position, lightType);
                    if (level > 1 && level - 1 > this.GetLight(position + step, lightType))
                        this.Enqueue(position, level | Bit(direction));
                }
            }
        }
    }

    // Each orthogonal neighbor's area index, with the direction from it into the chunk.
    private static readonly (int AreaIndex, BlockFace Direction)[] neighborBorders =
        [(1, BlockFace.South), (7, BlockFace.North), (3, BlockFace.East), (5, BlockFace.West)];

    /// <summary>
    /// Vanilla's <c>LightEngine.propagateIncreases</c>: spreads every queued entry until the queue is empty.
    /// </summary>
    private void Propagate(LightType lightType)
    {
        while (this.queue.TryDequeue(out var entry))
        {
            var position = (int)(entry >> 32);
            var flags = (int)entry;
            var level = flags & LevelMask;

            var stored = this.GetLight(position, lightType);
            if ((flags & FromEmission) != 0 && stored < level)
            {
                this.SetLight(position, lightType, level);
                stored = level;
            }

            // Otherwise the entry is stale: the position got brighter since, and a later entry spreads that.
            if (stored == level)
                this.PropagateIncrease(position, flags, level, lightType);
        }
    }

    /// <summary>
    /// Vanilla's <c>propagateIncrease</c>: lights the neighbors in the entry's directions that end up brighter, and
    /// queues them to spread further.
    /// </summary>
    private void PropagateIncrease(int position, int flags, int level, LightType lightType)
    {
        IBlock? from = null;
        for (var direction = 0; direction < 6; direction++)
        {
            if ((flags & (1 << (direction + DirectionShift))) == 0 || !this.TryMove(position, direction, out var next))
                continue;

            var stored = this.GetLight(next, lightType);
            if (level - 1 <= stored)
                continue;

            var to = this.GetBlock(next);
            var nextLevel = level - Math.Max(1, to.LightBlock());
            if (nextLevel <= stored)
                continue;

            from ??= this.GetBlock(position);
            if (BlockLight.LightShapesOcclude(from, to, (BlockFace)direction))
                continue;

            this.SetLight(next, lightType, nextLevel);

            // Light that comes back the way it spread is never brighter, so that direction is skipped.
            if (nextLevel > 1)
                this.Enqueue(next, nextLevel | (AllDirections & ~(1 << ((direction ^ 1) + DirectionShift))));
        }
    }

    /// <summary>
    /// Moves one block in <paramref name="direction"/>, staying within the build range and the chunks that may be written.
    /// </summary>
    private bool TryMove(int position, int direction, out int next)
    {
        next = position + directionSteps[direction];
        var inside = (BlockFace)direction switch
        {
            BlockFace.Down => position >= YStep,
            BlockFace.Up => (position >> 12) + 1 < this.height,
            BlockFace.North => ((position >> 6) & 63) > 0,
            BlockFace.South => ((position >> 6) & 63) + 1 < AreaEnd,
            BlockFace.West => (position & 63) > 0,
            _ => (position & 63) + 1 < AreaEnd
        };

        return inside && this.area[AreaIndex(next)] is not null;
    }

    private IBlock GetBlock(int position)
    {
        var (x, y, z) = (position & 15, (position >> 12) & 15, (position >> 6) & 15);
        return this.storageSections is not null
            ? this.storageSections[this.SectionSlot(position)]!.GetBlock(x, y, z)
            : this.sections[this.SectionSlot(position)]!.GetBlock(x, y, z);
    }

    // Light is read and written in the sections' storage the way ChunkSection.GetLightLevel and SetLightLevel do.
    private int GetLight(int position, LightType lightType)
    {
        if (this.storageSections is null)
        {
            var (x, y, z) = (position & 15, (position >> 12) & 15, (position >> 6) & 15);
            return this.sections[this.SectionSlot(position)]!.GetLightLevel(x, y, z, lightType);
        }

        var index = SectionIndex(position);
        var shift = (index & 1) << 2;
        return (this.storageSections[this.SectionSlot(position)]!.GetLightStorage(lightType)[index >> 1] >> shift) & 15;
    }

    private void SetLight(int position, LightType lightType, int level)
    {
        if (this.storageSections is null)
        {
            var (x, y, z) = (position & 15, (position >> 12) & 15, (position >> 6) & 15);
            this.sections[this.SectionSlot(position)]!.SetLightLevel(x, y, z, lightType, level);
            return;
        }

        var section = this.storageSections[this.SectionSlot(position)]!;
        var index = SectionIndex(position);
        var shift = (index & 1) << 2;
        ref var stored = ref section.GetLightStorage(lightType)[index >> 1];
        stored = (byte)((stored & (0xF0 >> shift)) | (level << shift));
        section.MarkLight(lightType);
    }

    // The index of a position's section in the section arrays. Offsetting x and z by 16 keeps their low 4 bits the
    // in-chunk coordinate.
    private int SectionSlot(int position) => AreaIndex(position) * this.sectionCount + (position >> 16);

    // The block's index in its section (y, then z, then x).
    private static int SectionIndex(int position) => (((position >> 12) & 15) << 8) | (((position >> 6) & 15) << 4) | (position & 15);

    private void Enqueue(int position, int flags) => this.queue.Enqueue(((long)position << 32) | (uint)flags);

    private static int AreaIndex(int position) => (((position >> 6) & 63) >> 4) * 3 + ((position & 63) >> 4);

    private static int Pack(int x, int yIndex, int z) => (yIndex << 12) | ((z + 16) << 6) | (x + 16);

    private static int Bit(BlockFace direction) => 1 << ((int)direction + DirectionShift);
}

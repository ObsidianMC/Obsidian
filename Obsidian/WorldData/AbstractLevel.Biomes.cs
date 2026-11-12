using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.WorldData.Generators.Mojang;

namespace Obsidian.WorldData;

public abstract partial class AbstractLevel
{
    // Biomes are stored per quart, a 4x4x4 block cell: 4 quarts a side in each chunk section, 64 in all.
    private const int QuartsPerSection = 4;
    private const int QuartsInSection = QuartsPerSection * QuartsPerSection * QuartsPerSection;

    private long? biomeZoomSeed;

    /// <summary>Reads the seed-dependent block biome from loaded storage, shared by spawning and weather.</summary>
    internal BiomeCodec BiomeAt(IChunk fallback, int x, int y, int z)
    {
        var seed = this.biomeZoomSeed ??= BiomeManager.ObfuscateSeed(this.LevelData.RandomSeed);
        return MobSpawner.SpawnBiomeAt(seed, this.GetLoadedChunk, fallback, x, y, z);
    }

    /// <inheritdoc/>
    public int FillBiomes(Vector from, Vector to, BiomeCodec biome, Func<BiomeCodec, bool>? filter = null) =>
        this.FillBiomes(from, to, biome, filter, requireLoaded: false) ?? 0;

    /// <summary>
    /// Like <see cref="FillBiomes(Vector, Vector, BiomeCodec, Func{BiomeCodec, bool}?)"/>, but with
    /// <paramref name="requireLoaded"/> it changes nothing and returns null unless every chunk of the box is loaded,
    /// generated and not being unloaded, checked under the same hold of the tick lock as the fill, as
    /// <c>/fillbiome</c> requires.
    /// </summary>
    internal int? FillBiomes(
        Vector from,
        Vector to,
        BiomeCodec biome,
        Func<BiomeCodec, bool>? filter,
        bool requireLoaded)
    {
        ArgumentNullException.ThrowIfNull(biome);

        var (min, max) = QuartBounds(from, to);
        var changed = 0;

        // Under the tick lock, as block changes are made, so that a flush reads a chunk's biomes before or after a
        // fill, never during one, and a chunk's unload either takes the fill's changes or keeps the fill out. Chunks
        // in vanilla's order, x within z, which the packets list them in.
        lock (this.Fluids.TickLock)
        {
            if (requireLoaded && !this.AreChunksLive(min, max))
                return null;

            for (var chunkZ = min.Z >> 4; chunkZ <= max.Z >> 4; chunkZ++)
            {
                for (var chunkX = min.X >> 4; chunkX <= max.X >> 4; chunkX++)
                {
                    if (this.LiveChunk(chunkX, chunkZ) is not Chunk chunk)
                        continue;

                    var chunkChanged = this.FillChunkBiomes(chunk, min, max, biome, filter);
                    if (chunkChanged == 0)
                        continue;

                    changed += chunkChanged;
                    this.blockChanges.BiomesChanged(chunkX, chunkZ);
                }
            }
        }

        return changed;
    }

    // Under the tick lock: whether every chunk of the box is live (see LiveChunk).
    private bool AreChunksLive(Vector min, Vector max)
    {
        for (var chunkZ = min.Z >> 4; chunkZ <= max.Z >> 4; chunkZ++)
        {
            for (var chunkX = min.X >> 4; chunkX <= max.X >> 4; chunkX++)
            {
                if (this.LiveChunk(chunkX, chunkZ) is null)
                    return false;
            }
        }

        return true;
    }

    // Under the tick lock: the chunk if it's loaded, generated and still resident. One a region unload has already
    // snapshotted is still in its slot, but a change to it would be lost with it, as other live writes check.
    private Chunk? LiveChunk(int chunkX, int chunkZ) =>
        this.GetLoadedChunk(chunkX, chunkZ) is Chunk { IsGenerated: true } chunk && this.Fluids.IsResident(chunk)
            ? chunk
            : null;

    /// <summary>
    /// The corners of the quarts <paramref name="from"/> and <paramref name="to"/> are in, lowest first: the box
    /// <see cref="FillBiomes"/> fills the quarts whose corners lie in, like vanilla's <c>/fillbiome</c>.
    /// </summary>
    internal static (Vector Min, Vector Max) QuartBounds(Vector from, Vector to)
    {
        var first = QuartCorner(from);
        var second = QuartCorner(to);
        var min = new Vector(Math.Min(first.X, second.X), Math.Min(first.Y, second.Y), Math.Min(first.Z, second.Z));
        var max = new Vector(Math.Max(first.X, second.X), Math.Max(first.Y, second.Y), Math.Max(first.Z, second.Z));
        return (min, max);
    }

    private static Vector QuartCorner(Vector position) => new(position.X & ~3, position.Y & ~3, position.Z & ~3);

    // Fills one chunk's quarts in the box, returning how many changed biome. A chunk with changes has every section's
    // biomes rebuilt, as vanilla's fill rebuilds each section's container (see Rebuild).
    //
    // The first pass decides which quarts are filled: those in the box whose current biome the filter accepts, asking
    // the filter once per quart and outside every section's lock, so that it may read the chunk. Then each section is
    // read again and rebuilt under its container's lock, which every biome write takes: the decided quarts take the
    // biome and the others keep what they hold then, so a biome set meanwhile on a quart the fill doesn't take is
    // kept.
    private int FillChunkBiomes(Chunk chunk, Vector min, Vector max, BiomeCodec biome, Func<BiomeCodec, bool>? filter)
    {
        lock (chunk.BlockLock)
        {
            var sections = chunk.Sections;
            var filled = new bool[sections.Length][];
            var changes = 0;
            for (var sectionIndex = 0; sectionIndex < sections.Length; sectionIndex++)
            {
                var biomes = sections[sectionIndex].BiomeContainer;
                var decided = filled[sectionIndex] = new bool[QuartsInSection];
                changes += this.DecideSection(chunk, sectionIndex, biomes, min, max, biome, filter, decided);
            }

            if (changes == 0)
                return 0;

            var values = new BiomeCodec[QuartsInSection];
            var changed = 0;
            for (var sectionIndex = 0; sectionIndex < sections.Length; sectionIndex++)
            {
                var biomes = sections[sectionIndex].BiomeContainer;
                using var scope = biomes.EnterScope();
                changed += ApplySection(biomes, biome, filled[sectionIndex], values);
                Rebuild(biomes, values);
            }

            return changed;
        }
    }

    // Records in filled, x, y, z, which of a section's quarts the fill takes, calling the filter once for each quart in
    // the box, and returns how many of those hold another biome.
    private int DecideSection(
        Chunk chunk,
        int sectionIndex,
        DataContainer<BiomeCodec> biomes,
        Vector min,
        Vector max,
        BiomeCodec biome,
        Func<BiomeCodec, bool>? filter,
        bool[] filled)
    {
        var sectionY = (this.MinY >> 4) + sectionIndex;
        var changes = 0;
        var index = 0;
        for (var x = 0; x < QuartsPerSection; x++)
        {
            for (var y = 0; y < QuartsPerSection; y++)
            {
                for (var z = 0; z < QuartsPerSection; z++)
                {
                    var corner = new Vector(chunk.X << 4 | x << 2, sectionY << 4 | y << 2, chunk.Z << 4 | z << 2);
                    var fills = false;
                    if (IsInside(corner, min, max))
                    {
                        var current = biomes.Get(x, y, z);
                        fills = filter?.Invoke(current) ?? true;
                        if (fills && current.Id != biome.Id)
                            changes++;
                    }

                    filled[index++] = fills;
                }
            }
        }

        return changes;
    }

    // Lists a section's biomes after the fill in values, x, y, z: the biome where filled says so, and what the section
    // holds now elsewhere. Returns how many filled quarts held another biome.
    private static int ApplySection(
        DataContainer<BiomeCodec> biomes,
        BiomeCodec biome,
        bool[] filled,
        BiomeCodec[] values)
    {
        var changed = 0;
        var index = 0;
        for (var x = 0; x < QuartsPerSection; x++)
        {
            for (var y = 0; y < QuartsPerSection; y++)
            {
                for (var z = 0; z < QuartsPerSection; z++)
                {
                    var current = biomes.Get(x, y, z);
                    if (filled[index] && current.Id != biome.Id)
                        changed++;

                    values[index] = filled[index] ? biome : current;
                    index++;
                }
            }
        }

        return changed;
    }

    // Replaces a section's biomes with values, listed x, y, z, in the palette vanilla's fill leaves (see
    // FilledPalette). Obsidian's palette grows at the sizes vanilla's does, so adding the entries in that order gives
    // the same palette and bits. Called holding the container's lock, so that a save snapshots it before or after.
    private static void Rebuild(DataContainer<BiomeCodec> biomes, BiomeCodec[] values)
    {
        var palette = FilledPalette(FirstPaletteEntry(biomes), values);
        biomes.Reset();
        foreach (var entry in palette)
            biomes.Add(entry);

        var index = 0;
        for (var x = 0; x < QuartsPerSection; x++)
        {
            for (var y = 0; y < QuartsPerSection; y++)
            {
                for (var z = 0; z < QuartsPerSection; z++)
                    biomes.Set(x, y, z, values[index++]);
            }
        }
    }

    // What vanilla's fill starts a section's new container with: the old palette's first entry. A section of more than
    // 3 bits is global in vanilla, whose first entry is the biome of registry id 0; one never set is plains.
    private static BiomeCodec FirstPaletteEntry(DataContainer<BiomeCodec> biomes)
    {
        if (biomes.BitsPerEntry > biomes.MaxBitsPerEntry)
            return CodecRegistry.GetBiome(0) ?? CodecRegistry.Biomes.Plains;

        if (biomes.Palette is SingleValuePalette<BiomeCodec> { IsFull: false })
            return CodecRegistry.Biomes.Plains;

        return biomes.Palette.GetValueFromIndex(0) ?? CodecRegistry.Biomes.Plains;
    }

    /// <summary>
    /// The palette, in order, that vanilla's fill leaves a section with: a container holding only
    /// <paramref name="first"/>, set to <paramref name="values"/> in the order x, y, z. A new biome is added at the
    /// end, except that a full palette (of 1, 2 or 4 entries) is first rebuilt from what the section holds then, in
    /// storage order (y, z, x), where quarts not set yet still hold <paramref name="first"/>. Past 8 entries vanilla
    /// sends biome ids instead, so the order no longer matters.
    /// </summary>
    /// <remarks>
    /// Matches vanilla's <c>PalettedContainer</c> resizing. Clients don't depend on it, but it keeps the biomes this
    /// level resends byte for byte the same as vanilla's, which the captured <c>chunks_biomes</c> packets check.
    /// </remarks>
    internal static List<BiomeCodec> FilledPalette(BiomeCodec first, BiomeCodec[] values)
    {
        var palette = new List<BiomeCodec> { first };
        var stored = new BiomeCodec[QuartsInSection];
        Array.Fill(stored, first);

        var index = 0;
        for (var x = 0; x < QuartsPerSection; x++)
        {
            for (var y = 0; y < QuartsPerSection; y++)
            {
                for (var z = 0; z < QuartsPerSection; z++)
                {
                    var value = values[index++];
                    var storageIndex = (y << 2 | z) << 2 | x;
                    if (!palette.Exists(entry => entry.Id == value.Id))
                    {
                        if (palette.Count is 2 or 4)
                            palette = StoredInOrder(stored);

                        palette.Add(value);
                    }

                    stored[storageIndex] = value;
                }
            }
        }

        return palette;
    }

    // The distinct biomes a section holds, in storage order.
    private static List<BiomeCodec> StoredInOrder(BiomeCodec[] stored)
    {
        var distinct = new List<BiomeCodec>();
        foreach (var biome in stored)
        {
            if (!distinct.Exists(entry => entry.Id == biome.Id))
                distinct.Add(biome);
        }

        return distinct;
    }

    private static bool IsInside(Vector position, Vector min, Vector max) =>
        position.X >= min.X && position.X <= max.X
        && position.Y >= min.Y && position.Y <= max.Y
        && position.Z >= min.Z && position.Z <= max.Z;
}

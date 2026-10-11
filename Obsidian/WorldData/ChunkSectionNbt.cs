using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.API.Utilities;
using Obsidian.ChunkData;
using Obsidian.Nbt;
using Obsidian.Registries;
using System.Numerics;

namespace Obsidian.WorldData;

/// <summary>
/// Reads and writes a chunk section's <c>block_states</c> and <c>biomes</c> in vanilla's saved shape: a <c>palette</c> of
/// values (block states by <c>Name</c> and <c>Properties</c>, biomes by name) and, with more than one value, their
/// indices packed in <c>data</c>.
/// </summary>
/// <remarks>
/// Obsidian's previous format had the same shape but named block states by their numeric <c>Id</c>, which is still read.
/// </remarks>
internal static class ChunkSectionNbt
{
    // Blocks are 16 per axis and biomes 4, so their indices use 4 and 2 bits per axis.
    private const int BlockAxisBits = 4;
    private const int BiomeAxisBits = 2;

    public static void WriteBlockStates(NbtWriterStream writer, DataContainer<IBlock> container)
    {
        var (palette, data) = Pack(container, BlocksRegistry.Air);

        writer.WriteCompoundStart("block_states");
        writer.WriteListStart("palette", NbtTagType.Compound, palette.Count);

        foreach (var block in palette)
        {
            writer.WriteCompoundStart();
            writer.WriteString("Name", block.UnlocalizedName);

            var properties = block.GetProperties();
            if (properties.Count > 0)
            {
                writer.WriteCompoundStart("Properties");

                foreach (var (name, value) in properties)
                    writer.WriteString(name, value);

                writer.EndCompound();
            }

            writer.EndCompound();
        }

        writer.EndList();

        if (data is not null)
            writer.WriteArray("data", data);

        writer.EndCompound();
    }

    public static void WriteBiomes(NbtWriterStream writer, DataContainer<BiomeCodec> container)
    {
        var (palette, data) = Pack(container, CodecRegistry.Biomes.Plains);

        writer.WriteCompoundStart("biomes");
        writer.WriteListStart("palette", NbtTagType.String, palette.Count);

        foreach (var biome in palette)
            writer.WriteString(biome.Name);

        writer.EndList();

        if (data is not null)
            writer.WriteArray("data", data);

        writer.EndCompound();
    }

    /// <summary>
    /// Sets a section's block states from a saved <c>block_states</c> compound. Unknown blocks become air.
    /// </summary>
    public static void ReadBlockStates(NbtCompound states, DataContainer<IBlock> container)
    {
        if (!states.TryGetTag<NbtList>("palette", out var paletteTag))
            return;

        var palette = paletteTag.OfType<NbtCompound>().Select(ReadBlockState).ToList();
        Unpack(container, palette, ReadData(states), BlockAxisBits);
    }

    /// <summary>
    /// Sets a section's biomes from a saved <c>biomes</c> compound. Unknown biomes become plains.
    /// </summary>
    public static void ReadBiomes(NbtCompound biomes, DataContainer<BiomeCodec> container)
    {
        if (!biomes.TryGetTag<NbtList>("palette", out var paletteTag))
            return;

        var palette = paletteTag.OfType<NbtTag<string>>()
            .Select(name => CodecRegistry.TryGetBiome(name.Value!, out var biome) ? biome! : CodecRegistry.Biomes.Plains)
            .ToList();

        Unpack(container, palette, ReadData(biomes), BiomeAxisBits);
    }

    private static IBlock ReadBlockState(NbtCompound entry)
    {
        // Obsidian's previous format: the state id.
        if (entry.TryGetTag<NbtTag<int>>("Id", out var id))
            return BlocksRegistry.Get(id.Value);

        var properties = entry.TryGetTag<NbtCompound>("Properties", out var propertiesTag)
            ? propertiesTag.Where(property => property.Value is NbtTag<string>)
                .ToDictionary(property => property.Key, property => ((NbtTag<string>)property.Value).Value!)
            : null;

        var name = entry.TryGetTag<NbtTag<string>>("Name", out var nameTag) ? nameTag.Value : null;
        return name is not null && BlockStateProperties.TryGetState(name, properties, out var state) ? state : BlocksRegistry.Air;
    }

    private static long[]? ReadData(NbtCompound container) =>
        container.TryGetTag<NbtArray<long>>("data", out var data) ? data.GetArray() : null;

    /// <summary>
    /// A container's values as vanilla's <c>PalettedContainer.pack</c> saves them: the palette holds the values in use in
    /// the order they first appear, and the data is <c>null</c> for a single value.
    /// </summary>
    /// <param name="empty">The value of a container nothing was set in.</param>
    private static (List<T> Palette, long[]? Data) Pack<T>(DataContainer<T> container, T empty)
    {
        using var scope = container.EnterScope();

        var source = container.DataArray;
        if (container.IsSingleValued || source is null)
            return ([container.Palette.IsFull ? container.Palette.GetValueFromIndex(0)! : empty], null);

        // The common case: an indirect palette whose storage already has vanilla's bits for its size is saved as it is.
        // Vanilla accepts palette entries that aren't used.
        var paletteCount = container.Palette.Count;
        if (container.Palette is BaseIndirectPalette<T>
            && paletteCount > 1
            && source.BitsPerEntry == StorageBits(paletteCount, container.MinBitsPerEntry))
        {
            var values = Enumerable.Range(0, paletteCount).Select(index => container.Palette.GetValueFromIndex(index)!).ToList();
            return (values, (long[])source.storage.Clone());
        }

        // Otherwise the values in use are collected and packed again.
        var palette = new List<T>();
        var compactIds = new Dictionary<int, int>();
        var ids = new int[container.MaxEntryCount];

        for (var i = 0; i < ids.Length; i++)
        {
            var sourceId = source[i];
            if (!compactIds.TryGetValue(sourceId, out var id))
            {
                id = palette.Count;
                compactIds.Add(sourceId, id);
                palette.Add(container.Palette.GetValueFromIndex(sourceId)!);
            }

            ids[i] = id;
        }

        var bits = StorageBits(palette.Count, container.MinBitsPerEntry);
        if (bits == 0)
            return (palette, null);

        var packed = new DataArray(bits, ids.Length);
        for (var i = 0; i < ids.Length; i++)
            packed[i] = ids[i];

        return (palette, packed.storage);
    }

    /// <summary>
    /// Replaces a container's values with a saved palette and data.
    /// </summary>
    private static void Unpack<T>(DataContainer<T> container, List<T> palette, long[]? data, int axisBits)
    {
        if (palette.Count == 0)
            return;

        // New sections already hold air, which the saved palette needn't start with.
        container.Reset();

        foreach (var value in palette)
            container.Add(value);

        if (palette.Count == 1)
            return;

        // Data that doesn't fit the palette (vanilla refuses to load it) leaves the section filled with the first value.
        var stored = new DataArray(StorageBits(palette.Count, container.MinBitsPerEntry), container.MaxEntryCount);
        if (data is null || data.Length != stored.storage.Length)
            return;

        // When the palette's values are distinct, the container's palette matches it index for index, and its storage is
        // used as it is if it has the same bits.
        var target = container.DataArray;
        if (container.Palette.Count == palette.Count && target is not null && target.BitsPerEntry == stored.BitsPerEntry)
        {
            target.storage = data;
            return;
        }

        stored.storage = data;

        var axisMask = (1 << axisBits) - 1;
        for (var i = 0; i < container.MaxEntryCount; i++)
        {
            var index = stored[i];
            var value = index < palette.Count ? palette[index] : palette[0];

            container.Set(i & axisMask, i >> (2 * axisBits), (i >> axisBits) & axisMask, value);
        }
    }

    /// <summary>
    /// The bits per entry vanilla stores a container's data with: none for one value, otherwise enough for every palette
    /// index but at least <paramref name="minimumBits"/> (4 for blocks, 1 for biomes). Vanilla rejects data with other bits.
    /// </summary>
    private static int StorageBits(int paletteSize, int minimumBits) =>
        paletteSize <= 1 ? 0 : Math.Max(minimumBits, 32 - BitOperations.LeadingZeroCount((uint)paletteSize - 1));
}

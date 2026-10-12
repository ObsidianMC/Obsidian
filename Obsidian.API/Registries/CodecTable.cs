using Obsidian.Nbt;
using System.Collections.Frozen;
using System.Numerics;
using System.Threading;

namespace Obsidian.API.Registries;

/// <summary>
/// The entries of a codec registry that plugins and data packs add to from their data files: vanilla's, then the
/// files' in the order they were first applied.
/// </summary>
/// <remarks>
/// Ids only grow. A plugin's entry keeps its id and instance until the server stops, even once its plugin unloads,
/// since chunks and saves refer to it; applying it again replaces only its element and data. Vanilla's entries may be
/// overridden the same way, keeping their instance, and get vanilla's element back once nothing overrides them.
/// Changes replace the table whole, so lookups never lock.
/// </remarks>
internal sealed class CodecTable<TCodec> where TCodec : class, ICodec
{
    private readonly Lock writing = new();

    // Vanilla's elements of the entries overridden since the server started, by id, to give them back.
    private readonly Dictionary<int, object> vanillaElements = [];

    private Snapshot current;

    /// <param name="vanilla">Vanilla's entries, in id order from 0.</param>
    public CodecTable(TCodec[] vanilla)
    {
        for (var i = 0; i < vanilla.Length; i++)
        {
            if (vanilla[i].Id != i)
                throw new ArgumentException($"{vanilla[i].Name} has id {vanilla[i].Id} rather than {i}.");
        }

        this.VanillaCount = vanilla.Length;
        this.current = new Snapshot([.. vanilla.Select(codec => new Entry(codec, null, null))]);
    }

    /// <summary>
    /// How many of the entries are vanilla's; plugins' ids start here.
    /// </summary>
    public int VanillaCount { get; }

    /// <summary>
    /// The entries by name, vanilla's and plugins'.
    /// </summary>
    public FrozenDictionary<string, TCodec> All => this.current.All;

    /// <summary>
    /// The entries by id, vanilla's and plugins'.
    /// </summary>
    public FrozenDictionary<int, TCodec> ById => this.current.ById;

    /// <summary>
    /// The bits per entry of the registry's global palette (biomes'), worked out from the number of entries like
    /// clients do: vanilla's <c>Mth.ceillog2</c> of the registry's size.
    /// </summary>
    public int GlobalBitsPerEntry => this.current.GlobalBits;

    /// <summary>
    /// The entries' names in id order, each with the data it's sent to clients with: null for vanilla's, which clients
    /// take from the <c>minecraft:core</c> pack, and a file's as NBT otherwise: plugins' entries and the vanilla ones a
    /// file overrides, like vanilla sends the entries a data pack changed.
    /// </summary>
    public IEnumerable<(string Name, NbtCompound? Data)> NetworkEntries =>
        this.current.Entries.Select(entry => (entry.Codec.Name, entry.Data));

    /// <summary>
    /// Whether one of vanilla's entries is overridden by a file.
    /// </summary>
    public bool IsOverridden(string name) =>
        this.current.All.TryGetValue(name, out var codec)
        && codec.Id < this.VanillaCount
        && this.current.Entries[codec.Id].Json is not null;

    /// <summary>
    /// Applies plugins' and data packs' entries: one already in the table gets the new element and data, and the
    /// others are added after the table's entries, in order. Plugins' entries no file has any more stay as they were,
    /// while vanilla's get vanilla's element back.
    /// </summary>
    /// <param name="entries">The entries, with distinct names.</param>
    /// <param name="create">Makes a new entry from its name, id and element.</param>
    /// <param name="element">An entry's element, to keep vanilla's before overriding it.</param>
    /// <param name="update">Gives an existing entry a new element.</param>
    /// <param name="accept">Whether an existing entry may take a new element, a file's or vanilla's; it may when
    /// null. An entry refused keeps its element and data.</param>
    /// <returns>Whether an entry was added or its data changed.</returns>
    public bool Apply<TElement>(
        IEnumerable<PluginCodecEntry<TElement>> entries,
        Func<string, int, TElement, TCodec> create,
        Func<TCodec, TElement> element,
        Action<TCodec, TElement> update,
        Func<TCodec, TElement, bool>? accept = null)
    {
        lock (this.writing)
        {
            var table = this.current.Entries.ToBuilder();
            var overridden = new HashSet<int>();
            var changed = false;

            foreach (var entry in entries)
            {
                if (this.current.All.TryGetValue(entry.Name, out var existing))
                {
                    if (existing.Id < this.VanillaCount)
                    {
                        overridden.Add(existing.Id);
                        this.vanillaElements.TryAdd(existing.Id, element(existing)!);
                    }

                    if (accept?.Invoke(existing, entry.Element) == false)
                        continue;

                    update(existing, entry.Element);

                    if (table[existing.Id].Json != entry.Json)
                    {
                        table[existing.Id] = new Entry(existing, entry.Json, entry.Data);
                        changed = true;
                    }

                    continue;
                }

                var codec = create(entry.Name, table.Count, entry.Element);
                table.Add(new Entry(codec, entry.Json, entry.Data));
                changed = true;
            }

            // Vanilla's entries no file overrides any more are vanilla's again, and sent by name.
            for (var id = 0; id < this.VanillaCount; id++)
            {
                if (table[id].Json is null || overridden.Contains(id))
                    continue;

                var codec = table[id].Codec;
                var vanilla = (TElement)this.vanillaElements[id];

                if (accept?.Invoke(codec, vanilla) == false)
                    continue;

                update(codec, vanilla);
                table[id] = new Entry(codec, null, null);
                changed = true;
            }

            if (changed)
                this.current = new Snapshot(table.ToImmutable());

            return changed;
        }
    }

    // An entry with the file it came from (null for vanilla's).
    private readonly record struct Entry(TCodec Codec, string? Json, NbtCompound? Data);

    private sealed class Snapshot
    {
        public Snapshot(ImmutableArray<Entry> entries)
        {
            this.Entries = entries;
            this.All = entries.ToFrozenDictionary(entry => entry.Codec.Name, entry => entry.Codec);
            this.ById = entries.ToFrozenDictionary(entry => entry.Codec.Id, entry => entry.Codec);

            // Vanilla's Mth.ceillog2: the bits of the smallest power of two holding every id, which clients use to
            // read the global palette, so this has to match.
            this.GlobalBits = entries.Length <= 1
                ? 0
                : 32 - BitOperations.LeadingZeroCount((uint)entries.Length - 1);
        }

        public ImmutableArray<Entry> Entries { get; }

        public FrozenDictionary<string, TCodec> All { get; }

        public FrozenDictionary<int, TCodec> ById { get; }

        public int GlobalBits { get; }
    }
}

/// <summary>
/// An entry of a plugin's data file for a <see cref="CodecTable{TCodec}"/>.
/// </summary>
/// <param name="Name">The entry's identifier, like <c>ruby:crystal_caves</c>.</param>
/// <param name="Element">The file read as the registry's element, for the server to use.</param>
/// <param name="Json">The file's text, which tells whether it changed.</param>
/// <param name="Data">The file as NBT, which clients are sent.</param>
internal sealed record PluginCodecEntry<TElement>(string Name, TElement Element, string Json, NbtCompound Data);

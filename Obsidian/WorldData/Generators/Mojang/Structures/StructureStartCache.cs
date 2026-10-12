using Obsidian.Nbt;
using Obsidian.Plugins;
using Obsidian.Registries;
using Obsidian.WorldData.Structures;
using System.Threading;

namespace Obsidian.WorldData.Generators.Mojang.Structures;

/// <summary>
/// The structure starts a <see cref="StructureManager"/> created, by start chunk, with the state their pieces were
/// placed with. A generator keeps this to save them after it stopped and dropped its builder. Thread-safe.
/// </summary>
/// <remarks>
/// <para>
/// A chunk's starts are made for the structure sets of a <see cref="StructureLayout"/>, one start or none per set.
/// When plugins change the sets, a chunk's starts are made again for the new ones as it's asked for, keeping the
/// starts of the sets both have, so that chunks not generated yet get the new sets' structures and the structures
/// placed partly keep their state.
/// </para>
/// <para>
/// Plugins' starts hold their code, so the starts of sets that are gone, and those made with plugins that unload, are
/// dropped (see <see cref="Prune"/>): they aren't placed any more, and the state of those placed in this session is
/// kept as saved, to be saved and restored into the start made again if its structure comes back.
/// </para>
/// <para>
/// Starts are made without a lock, since that runs plugins' code, then published under one with the starts they
/// replace dropped and their state handed on, so that no state is lost between them. Pruning takes the same lock, and
/// starts made for a layout older than the one pruned for aren't published.
/// </para>
/// </remarks>
internal sealed class StructureStartCache
{
    // Publishing a chunk's starts, pruning, and saving; reading a chunk's current starts doesn't take it.
    private readonly Lock gate = new();

    private readonly ConcurrentDictionary<(int X, int Z), ChunkStarts> starts = new();

    // The saved state of starts dropped, by start chunk and structure identifier. Under the gate.
    private readonly Dictionary<(int X, int Z), ImmutableDictionary<string, NbtCompound>> dropped = [];

    // The version of the structures last pruned for: starts made for older ones may hold what was pruned.
    private long prunedVersion = long.MinValue;

    /// <summary>
    /// Creates the start of a structure set in a chunk, or gives null when the set has none there.
    /// </summary>
    /// <param name="refused">Set when the start couldn't be made since a plugin it runs is unloading; it's made
    /// again once the starts are pruned.</param>
    public delegate StructureStart? StartFactory(
        StructureLayout layout,
        StructureSet set,
        int chunkX,
        int chunkZ,
        out bool refused);

    /// <summary>
    /// The starts of a chunk for the layout's structure sets, created by <paramref name="create"/> for the sets whose
    /// starts weren't created yet. A chunk whose starts were made for a later layout keeps those, even when some of
    /// that layout's are missing.
    /// </summary>
    /// <returns>The chunk's starts, or null when the starts were pruned for a later layout than this one: ask
    /// again with the current layout.</returns>
    public ChunkStarts? Get(int chunkX, int chunkZ, StructureLayout layout, StartFactory create)
    {
        var key = (chunkX, chunkZ);

        while (true)
        {
            // Starts made for a later layout aren't made again for an earlier one, which would drop the later's.
            this.starts.TryGetValue(key, out var previous);
            if (previous is not null
                && (previous.Version > layout.Version || previous.Complete && previous.Version == layout.Version))
            {
                return previous;
            }

            if (layout.Version < Volatile.Read(ref this.prunedVersion))
                return null;

            var made = ChunkStarts.Make(layout, previous, chunkX, chunkZ, create);

            lock (this.gate)
            {
                if (layout.Version < this.prunedVersion)
                    return null;

                // Another thread published the chunk's starts meanwhile, or pruned them: start over from those.
                this.starts.TryGetValue(key, out var current);
                if (!ReferenceEquals(current, previous))
                    continue;

                if (previous is not null)
                    this.Drop(key, previous, made);

                this.Adopt(key, made, previous);
                this.starts[key] = made;
                return made;
            }
        }
    }

    /// <summary>
    /// Drops the starts of the structure sets that aren't in <paramref name="structures"/>, and those made with a
    /// plugin that's unloading, so that the plugins' code isn't kept; the sets that are still there get their starts
    /// made again as they're asked for. The state of those placed in this session is kept as saved.
    /// </summary>
    public void Prune(WorldgenStructures structures)
    {
        var live = structures.Sets.Values.ToHashSet();

        lock (this.gate)
        {
            Volatile.Write(ref this.prunedVersion, Math.Max(this.prunedVersion, structures.Version));

            foreach (var (key, entry) in this.starts)
            {
                var kept = entry.Keep(live);
                if (kept == entry)
                    continue;

                this.Drop(key, entry, kept);
                this.starts[key] = kept;
            }
        }
    }

    /// <summary>
    /// The structure starts to save in their start chunk (vanilla's <c>structures.starts</c>, keyed by structure id):
    /// those the chunk was loaded with, where starts restored in this session, or dropped since, replace theirs with
    /// their pieces' current state. A start whose structure is gone stays as it was saved.
    /// </summary>
    /// <remarks>
    /// A start that wasn't restored hasn't been placed in this session, so what the chunk was loaded with is still
    /// current. Saving a start made of plugins' types is a call into them; one whose plugins are unloading keeps the
    /// state it's saved with as it's dropped.
    /// </remarks>
    public NbtCompound? Save(int chunkX, int chunkZ, NbtCompound? loaded)
    {
        var key = (chunkX, chunkZ);
        ImmutableDictionary<string, NbtCompound>? states;
        StructureStart[] restored;

        // Taken together, so that a start being dropped is in one or the other.
        lock (this.gate)
        {
            this.dropped.TryGetValue(key, out states);
            restored = this.starts.TryGetValue(key, out var cached)
                ? [.. cached.Starts.Where(start => start.IsStateRestored)]
                : [];
        }

        if (restored.Length == 0 && states is null)
            return loaded;

        var result = new NbtCompound("starts");
        if (loaded is not null)
        {
            foreach (var (name, start) in loaded)
                result.Add(name, start);
        }

        foreach (var (name, state) in states ?? ImmutableDictionary<string, NbtCompound>.Empty)
        {
            result.Remove(name);
            result.Add(state);
        }

        foreach (var start in restored)
        {
            if (!GeneratorCalls.TryEnter(start.Owners, out var calls))
                continue;

            using (calls)
            {
                result.Remove(start.Structure.Identifier);
                result.Add(start.SaveState());
            }
        }

        return result;
    }

    // New starts of a structure whose start was dropped in the chunk take its state, before any is placed, and it's
    // no longer kept. Under the gate.
    private void Adopt((int X, int Z) key, ChunkStarts made, ChunkStarts? previous)
    {
        if (!this.dropped.TryGetValue(key, out var states))
            return;

        foreach (var start in made.Starts)
        {
            if (previous?.Starts.Contains(start) is true
                || !states.TryGetValue(start.Structure.Identifier, out var state))
            {
                continue;
            }

            start.RestoreState(state);
            states = states.Remove(start.Structure.Identifier);
        }

        if (states.IsEmpty)
            this.dropped.Remove(key);
        else
            this.dropped[key] = states;
    }

    // Retires the starts the chunk had that it no longer has, keeping the state of those placed in this session.
    // Under the gate.
    private void Drop((int X, int Z) key, ChunkStarts previous, ChunkStarts current)
    {
        foreach (var start in previous.Starts)
        {
            if (current.Starts.Contains(start) || start.Retire() is not NbtCompound state)
                continue;

            var states = this.dropped.GetValueOrDefault(key) ?? ImmutableDictionary<string, NbtCompound>.Empty;
            this.dropped[key] = states.SetItem(start.Structure.Identifier, state);
        }
    }

    /// <summary>
    /// A chunk's starts, one or none for each structure set of the layout they were made for.
    /// </summary>
    internal sealed class ChunkStarts
    {
        private readonly StructureStart?[] perSet;

        private ChunkStarts(long version, StructureSet[] sets, StructureStart?[] perSet, bool complete)
        {
            this.Version = version;
            this.Sets = sets;
            this.perSet = perSet;
            this.Complete = complete;
            this.Starts = [.. perSet.OfType<StructureStart>()];
        }

        /// <summary>
        /// The version of the layout the starts were made for.
        /// </summary>
        public long Version { get; }

        /// <summary>
        /// Whether there's a start, or none, for every set of that layout; the others are made when asked for.
        /// </summary>
        public bool Complete { get; }

        public StructureSet[] Sets { get; }

        /// <summary>
        /// The starts in set order, which is the order vanilla creates them in.
        /// </summary>
        public StructureStart[] Starts { get; }

        // The starts of the sets the previous starts were made for are kept, so their state carries over. A set whose
        // start was refused is left out, to be made again.
        public static ChunkStarts Make(
            StructureLayout layout,
            ChunkStarts? previous,
            int chunkX,
            int chunkZ,
            StartFactory create)
        {
            var sets = new List<StructureSet>(layout.Sets.Count);
            var perSet = new List<StructureStart?>(layout.Sets.Count);

            foreach (var set in layout.Sets)
            {
                var reused = previous is null ? -1 : Array.IndexOf(previous.Sets, set);
                if (reused >= 0)
                {
                    sets.Add(set);
                    perSet.Add(previous!.perSet[reused]);
                    continue;
                }

                var start = create(layout, set, chunkX, chunkZ, out var refused);
                if (refused)
                    continue;

                sets.Add(set);
                perSet.Add(start);
            }

            return new ChunkStarts(layout.Version, [.. sets], [.. perSet], sets.Count == layout.Sets.Count);
        }

        // The starts of the live sets not made with a plugin that's unloading; this, when that's all of them.
        public ChunkStarts Keep(IReadOnlySet<StructureSet> live)
        {
            var indices = Enumerable.Range(0, this.Sets.Length)
                .Where(i => live.Contains(this.Sets[i]) && this.perSet[i]?.Owners.Any(IsUnloading) is not true)
                .ToArray();

            if (indices.Length == this.Sets.Length)
                return this;

            return new ChunkStarts(
                this.Version,
                [.. indices.Select(i => this.Sets[i])],
                [.. indices.Select(i => this.perSet[i])],
                complete: false);
        }

        private static bool IsUnloading(PluginContainer plugin) => plugin.IsClosing;
    }
}

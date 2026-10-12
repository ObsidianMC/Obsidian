using Obsidian.Nbt;
using Obsidian.WorldData.Structures;

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
/// Plugins' starts hold their code, so the starts of sets that are gone are dropped (see <see cref="Prune"/>), and
/// the state of those placed in this session is kept as saved, to be saved and restored into the start made again if
/// its structure comes back.
/// </para>
/// </remarks>
internal sealed class StructureStartCache
{
    private readonly ConcurrentDictionary<(int X, int Z), ChunkStarts> starts = new();

    // The saved state of starts dropped with their structure set, by start chunk and structure identifier.
    private readonly ConcurrentDictionary<(int X, int Z), ImmutableDictionary<string, NbtCompound>> dropped = new();

    /// <summary>
    /// The starts of a chunk for the layout's structure sets, created by <paramref name="create"/> for the sets whose
    /// starts weren't created yet. A chunk whose starts were made for a later layout keeps those.
    /// </summary>
    /// <param name="create">Creates the start of a set in the chunk, or gives null when the set has none there.
    /// </param>
    public StructureStart[] Get(
        int chunkX,
        int chunkZ,
        StructureLayout layout,
        Func<StructureLayout, StructureSet, int, int, StructureStart?> create)
    {
        var key = (chunkX, chunkZ);

        while (true)
        {
            var found = this.starts.TryGetValue(key, out var previous);
            if (found && previous!.Version >= layout.Version)
                return previous.Starts;

            var made = ChunkStarts.Make(layout, previous, set => create(layout, set, chunkX, chunkZ));
            this.Adopt(key, made, previous);

            var stored = found ? this.starts.TryUpdate(key, made, previous!) : this.starts.TryAdd(key, made);
            if (!stored)
                continue;

            if (previous is not null)
                this.Drop(key, previous, made);

            if (this.dropped.TryGetValue(key, out var remaining))
                this.Forget(key, remaining, made);

            return made.Starts;
        }
    }

    /// <summary>
    /// Drops the starts of the structure sets not in <paramref name="live"/>, which may hold the code of plugins that
    /// unload, keeping the state of those placed in this session as saved.
    /// </summary>
    public void Prune(IReadOnlySet<StructureSet> live)
    {
        foreach (var key in this.starts.Keys)
        {
            while (this.starts.TryGetValue(key, out var entry) && !entry.Sets.All(live.Contains))
            {
                var kept = entry.Keep(live.Contains);
                if (!this.starts.TryUpdate(key, kept, entry))
                    continue;

                this.Drop(key, entry, kept);
                break;
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
    /// current.
    /// </remarks>
    public NbtCompound? Save(int chunkX, int chunkZ, NbtCompound? loaded)
    {
        var key = (chunkX, chunkZ);
        this.dropped.TryGetValue(key, out var states);

        StructureStart[] restored = this.starts.TryGetValue(key, out var cached)
            ? [.. cached.Starts.Where(start => start.IsStateRestored)]
            : [];

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
            result.Remove(start.Structure.Identifier);
            result.Add(start.SaveState());
        }

        return result;
    }

    // Starts made again for a structure whose start was dropped in the chunk take its state, before any is placed.
    private void Adopt((int X, int Z) key, ChunkStarts made, ChunkStarts? previous)
    {
        if (!this.dropped.TryGetValue(key, out var states))
            return;

        foreach (var start in made.Starts)
        {
            if (previous?.Starts.Contains(start) is not true
                && states.TryGetValue(start.Structure.Identifier, out var state))
            {
                start.RestoreState(state);
            }
        }
    }

    // The starts that took a dropped start's state no longer need it kept.
    private void Forget((int X, int Z) key, ImmutableDictionary<string, NbtCompound> states, ChunkStarts made)
    {
        var adopted = made.Starts
            .Select(start => start.Structure.Identifier)
            .Where(states.ContainsKey)
            .ToList();

        if (adopted.Count == 0)
            return;

        while (this.dropped.TryGetValue(key, out var current))
        {
            var rest = current.RemoveRange(adopted);
            var updated = rest.IsEmpty
                ? this.dropped.TryRemove(KeyValuePair.Create(key, current))
                : this.dropped.TryUpdate(key, rest, current);

            if (updated)
                return;
        }
    }

    // Keeps the state of the starts the chunk had placed in this session that it no longer has.
    private void Drop((int X, int Z) key, ChunkStarts previous, ChunkStarts current)
    {
        foreach (var start in previous.Starts)
        {
            if (current.Starts.Contains(start) || !start.IsStateRestored)
                continue;

            var state = (Id: start.Structure.Identifier, State: start.SaveState());
            this.dropped.AddOrUpdate(
                key,
                static (_, state) => ImmutableDictionary<string, NbtCompound>.Empty.SetItem(state.Id, state.State),
                static (_, states, state) => states.SetItem(state.Id, state.State),
                state);
        }
    }

    // A chunk's starts, one or none for each structure set of the layout they were made for.
    private sealed class ChunkStarts
    {
        private readonly StructureStart?[] perSet;

        private ChunkStarts(long version, StructureSet[] sets, StructureStart?[] perSet)
        {
            this.Version = version;
            this.Sets = sets;
            this.perSet = perSet;
            this.Starts = [.. perSet.OfType<StructureStart>()];
        }

        public long Version { get; }

        public StructureSet[] Sets { get; }

        // The starts in set order, which is the order vanilla creates them in.
        public StructureStart[] Starts { get; }

        // The starts of the sets the previous starts were made for are kept, so their state carries over.
        public static ChunkStarts Make(
            StructureLayout layout,
            ChunkStarts? previous,
            Func<StructureSet, StructureStart?> create)
        {
            var sets = layout.Sets.ToArray();
            var perSet = new StructureStart?[sets.Length];

            for (var i = 0; i < sets.Length; i++)
            {
                var reused = previous is null ? -1 : Array.IndexOf(previous.Sets, sets[i]);
                perSet[i] = reused >= 0 ? previous!.perSet[reused] : create(sets[i]);
            }

            return new ChunkStarts(layout.Version, sets, perSet);
        }

        public ChunkStarts Keep(Func<StructureSet, bool> keep)
        {
            var indices = Enumerable.Range(0, this.Sets.Length)
                .Where(i => keep(this.Sets[i]))
                .ToArray();

            return new ChunkStarts(
                this.Version,
                [.. indices.Select(i => this.Sets[i])],
                [.. indices.Select(i => this.perSet[i])]);
        }
    }
}

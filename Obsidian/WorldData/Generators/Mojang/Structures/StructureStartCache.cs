using Obsidian.Nbt;
using Obsidian.WorldData.Structures;

namespace Obsidian.WorldData.Generators.Mojang.Structures;

/// <summary>
/// The structure starts a <see cref="StructureManager"/> created, by start chunk, with the state their pieces were
/// placed with. Starts hold vanilla data only, so a generator keeps this to save them after it stopped and dropped
/// the parts made of plugins' types. Thread-safe.
/// </summary>
internal sealed class StructureStartCache
{
    private readonly ConcurrentDictionary<(int X, int Z), StructureStart[]> starts = new();

    /// <summary>
    /// The starts of a chunk, created by <paramref name="create"/> the first time.
    /// </summary>
    public StructureStart[] GetOrAdd(int chunkX, int chunkZ, Func<int, int, StructureStart[]> create) =>
        this.starts.GetOrAdd((chunkX, chunkZ), static (key, create) => create(key.X, key.Z), create);

    /// <summary>
    /// The structure starts to save in their start chunk (vanilla's <c>structures.starts</c>, keyed by structure id):
    /// those the chunk was loaded with, where starts restored in this session replace theirs with their pieces'
    /// current state.
    /// </summary>
    /// <remarks>
    /// A start that wasn't restored hasn't been placed in this session, so what the chunk was loaded with is still
    /// current.
    /// </remarks>
    public NbtCompound? Save(int chunkX, int chunkZ, NbtCompound? loaded)
    {
        if (!this.starts.TryGetValue((chunkX, chunkZ), out var cached) || !cached.Any(start => start.IsStateRestored))
            return loaded;

        var result = new NbtCompound("starts");
        if (loaded is not null)
        {
            foreach (var (name, start) in loaded)
                result.Add(name, start);
        }

        foreach (var start in cached)
        {
            if (!start.IsStateRestored)
                continue;

            result.Remove(start.Structure.Identifier);
            result.Add(start.SaveState());
        }

        return result;
    }
}

using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Structures.Pools;

/// <summary>
/// Replaces pool ids within one structure start, like vanilla's <c>PoolAliasBinding</c> (trial chamber spawner mobs).
/// </summary>
public abstract class PoolAliasBinding
{
    public string Type { get; init; } = string.Empty;

    /// <summary>
    /// Vanilla <c>forEachResolved</c>: reports each alias and the pool id it resolves to, drawing from <paramref name="random"/>.
    /// </summary>
    public abstract void ForEachResolved(IRandomSource random, Action<string, string> resolved);

    /// <summary>Vanilla <c>WeightedList.getRandom</c>: one <c>nextInt</c> over the total weight.</summary>
    protected static T? PickWeighted<T>(IRandomSource random, IReadOnlyList<(T Value, int Weight)> entries)
    {
        var total = entries.Sum(entry => entry.Weight);
        if (total == 0)
            return default;

        var pick = random.NextInt(total);
        foreach (var (value, weight) in entries)
        {
            pick -= weight;
            if (pick < 0)
                return value;
        }

        return default;
    }
}

/// <summary>Always maps <see cref="Alias"/> to <see cref="Target"/>.</summary>
[StructureType("minecraft:direct")]
public sealed class DirectPoolAlias : PoolAliasBinding
{
    public required string Alias { get; init; }

    public required string Target { get; init; }

    public override void ForEachResolved(IRandomSource random, Action<string, string> resolved) => resolved(this.Alias, this.Target);
}

/// <summary>Maps <see cref="Alias"/> to one of <see cref="Targets"/>, picked by weight.</summary>
[StructureType("minecraft:random")]
public sealed class RandomPoolAlias : PoolAliasBinding
{
    public required string Alias { get; init; }

    public required WeightedPoolId[] Targets { get; init; }

    public override void ForEachResolved(IRandomSource random, Action<string, string> resolved)
    {
        var target = PickWeighted(random, [.. this.Targets.Select(entry => (entry.Data, entry.Weight))]);
        if (target is not null)
            resolved(this.Alias, target);
    }
}

/// <summary>Resolves one of <see cref="Groups"/>, picked by weight.</summary>
[StructureType("minecraft:random_group")]
public sealed class RandomGroupPoolAlias : PoolAliasBinding
{
    public required WeightedAliasGroup[] Groups { get; init; }

    public override void ForEachResolved(IRandomSource random, Action<string, string> resolved)
    {
        var group = PickWeighted(random, [.. this.Groups.Select(entry => (entry.Data, entry.Weight))]);
        foreach (var binding in group ?? [])
            binding.ForEachResolved(random, resolved);
    }
}

/// <summary>A pool id and its weight.</summary>
public sealed class WeightedPoolId
{
    public required string Data { get; init; }

    public required int Weight { get; init; }
}

/// <summary>A group of aliases and its weight.</summary>
public sealed class WeightedAliasGroup
{
    public required PoolAliasBinding[] Data { get; init; }

    public required int Weight { get; init; }
}

/// <summary>
/// The pool ids a structure start uses in place of others, like vanilla's <c>PoolAliasLookup</c>.
/// </summary>
public sealed class PoolAliasLookup
{
    public static PoolAliasLookup Empty { get; } = new(new Dictionary<string, string>());

    private readonly IReadOnlyDictionary<string, string> aliases;

    private PoolAliasLookup(IReadOnlyDictionary<string, string> aliases) => this.aliases = aliases;

    /// <summary>
    /// Vanilla <c>PoolAliasLookup.create</c>: resolves the bindings with a random seeded by the world seed and the start
    /// position.
    /// </summary>
    public static PoolAliasLookup Create(IReadOnlyList<PoolAliasBinding> bindings, Vector position, long seed)
    {
        if (bindings.Count == 0)
            return Empty;

        var random = new LegacyRandomSource(seed).ForkPositional().At(position.X, position.Y, position.Z);
        var aliases = new Dictionary<string, string>();

        // Like vanilla's ImmutableMap builder, an alias bound twice is an error.
        foreach (var binding in bindings)
            binding.ForEachResolved(random, (alias, target) => aliases.Add(alias, target));

        return new PoolAliasLookup(aliases);
    }

    /// <summary>The pool id to use for <paramref name="pool"/>.</summary>
    public string Lookup(string pool) => this.aliases.GetValueOrDefault(pool, pool);
}

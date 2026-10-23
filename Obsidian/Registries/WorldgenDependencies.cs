using Obsidian.Plugins;
using Obsidian.Plugins.PluginProviders;
using System.Runtime.Loader;

namespace Obsidian.Registries;

/// <summary>
/// Notes what each worldgen entry read from plugins' files depends on, such as the plugins whose types it's made of,
/// counting the entries it refers to: entries are read one inside another as they refer to each other, so a
/// dependency is noted for every entry being read, and an entry used by another passes its dependencies on.
/// </summary>
/// <typeparam name="T">What entries depend on.</typeparam>
internal sealed class WorldgenDependencies<T>
    where T : notnull
{
    // The dependencies of each entry being read so far, the innermost entry's last.
    private readonly Stack<HashSet<T>> reading = [];

    /// <summary>
    /// The dependencies of each entry read, for the entries that have any.
    /// </summary>
    public Dictionary<object, ImmutableArray<T>> Of { get; } = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Reads an entry, noting what it depends on meanwhile.
    /// </summary>
    public TEntry Track<TEntry>(Func<TEntry> read)
        where TEntry : class
    {
        var dependencies = new HashSet<T>();
        this.reading.Push(dependencies);

        try
        {
            var entry = read();
            if (dependencies.Count > 0)
                this.Of[entry] = [.. dependencies];

            return entry;
        }
        finally
        {
            this.reading.Pop();
        }
    }

    /// <summary>
    /// An entry used by the one being read, if any, which then depends on what it depends on.
    /// </summary>
    public TEntry Used<TEntry>(TEntry entry)
        where TEntry : class
    {
        if (this.reading.TryPeek(out var user) && this.Of.TryGetValue(entry, out var dependencies))
            user.UnionWith(dependencies);

        return entry;
    }

    /// <summary>
    /// Notes a dependency of the entry being read, if any.
    /// </summary>
    public void Add(T dependency)
    {
        if (this.reading.TryPeek(out var dependencies))
            dependencies.Add(dependency);
    }

    /// <summary>
    /// The plugin a type belongs to, when it's from a plugin's assembly: creating it reads the plugin's code.
    /// </summary>
    public static PluginContainer? Owner(Type type) =>
        AssemblyLoadContext.GetLoadContext(type.Assembly) is PluginLoadContext { Container: PluginContainer owner }
            ? owner
            : null;
}

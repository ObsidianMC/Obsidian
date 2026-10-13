using Obsidian.Plugins;

namespace Obsidian.Registries;

/// <summary>
/// Level generator types plugins registered with
/// <see cref="API.Plugins.IPluginRegistry.RegisterGenerator{TGenerator}"/>, or with the host's
/// <see cref="WorldData.LevelFactory.RegisterGenerator{T}"/>, which worlds and dimensions name by identifier (see
/// <see cref="WorldData.Generators.PluginLevelGenerator"/>).
/// </summary>
/// <remarks>
/// Unlike the server's own generators, a plugin's are removed when it unloads, so that nothing keeps its types, and
/// registered again when it loads.
/// </remarks>
internal static class PluginGenerators
{
    private static ImmutableDictionary<string, PluginGenerator> registered =
        ImmutableDictionary<string, PluginGenerator>.Empty;

    /// <summary>
    /// Registers <paramref name="type"/> as the generator <paramref name="identifier"/> of <paramref name="plugin"/>.
    /// </summary>
    /// <remarks>
    /// Identifiers are in their plugin's namespace, so one registered by another instance of the plugin, like the one
    /// a reload replaces, is replaced.
    /// </remarks>
    /// <exception cref="ArgumentException">The plugin registered the identifier already.</exception>
    public static void Register(PluginContainer plugin, string identifier, Type type)
    {
        var generator = new PluginGenerator(identifier, type, plugin);

        ImmutableInterlocked.Update(ref registered, current =>
        {
            if (current.TryGetValue(identifier, out var existing) && existing.Plugin == plugin)
                throw new ArgumentException($"{identifier} is already a generator of {plugin.Info.Name}.");

            return current.SetItem(identifier, generator);
        });
    }

    /// <summary>
    /// Removes the generators of an unloading plugin.
    /// </summary>
    public static void Release(PluginContainer plugin) =>
        ImmutableInterlocked.Update(ref registered, current => current.RemoveRange(current.Values
            .Where(generator => generator.Plugin == plugin)
            .Select(generator => generator.Identifier)));

    /// <summary>
    /// The generator <paramref name="identifier"/>, or null when no loaded plugin registered it.
    /// </summary>
    public static PluginGenerator? Find(string identifier) =>
        registered.TryGetValue(identifier, out var generator) ? generator : null;
}

/// <summary>
/// A level generator type a plugin registered.
/// </summary>
/// <param name="Identifier">The identifier worlds and dimensions name it by, in the plugin's namespace.</param>
/// <param name="Type">The <see cref="ILevelGenerator"/> type, created with the plugin's services.</param>
/// <param name="Plugin">The plugin that registered it.</param>
internal sealed record PluginGenerator(string Identifier, Type Type, PluginContainer Plugin);

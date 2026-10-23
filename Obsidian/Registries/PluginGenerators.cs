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
    /// Registers <paramref name="type"/> as the generator <paramref name="identifier"/> of <paramref name="plugin"/>,
    /// in one step, unless the identifier is taken.
    /// </summary>
    /// <remarks>
    /// The identifier is taken when a plugin registered it, unless that's another instance of the same plugin, like
    /// the one a reload replaces: its generator is replaced. So the first plugin to register an identifier keeps it.
    /// </remarks>
    /// <returns>Whether it was registered; false when the identifier is taken, by this plugin too.</returns>
    /// <exception cref="InvalidOperationException">
    /// The plugin is unloading; nothing it registers stays.
    /// </exception>
    public static bool TryRegister(PluginContainer plugin, string identifier, Type type)
    {
        plugin.ThrowIfClosing();

        var generator = new PluginGenerator(identifier, type, plugin);
        var added = false;

        ImmutableInterlocked.Update(ref registered, current =>
        {
            added = !current.TryGetValue(identifier, out var existing)
                || (existing.Plugin != plugin && existing.Plugin.Info.Id == plugin.Info.Id);

            return added ? current.SetItem(identifier, generator) : current;
        });

        // Unloading closes the plugin before releasing its generators, so one registered meanwhile is released either
        // then or here.
        if (added && plugin.IsClosing)
        {
            Release(plugin);
            plugin.ThrowIfClosing();
        }

        return added;
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

using Obsidian.Plugins;

namespace Obsidian.WorldData.Generators.Mojang;

/// <summary>
/// Calls into the plugins a dimension's generator is made of (see <see cref="LevelStem.Plugins"/>), held while a step
/// of generation runs, which keeps them from being released until it ends.
/// </summary>
internal readonly struct GeneratorCalls : IDisposable
{
    private readonly PluginCall[]? calls;

    private GeneratorCalls(PluginCall[]? calls)
    {
        this.calls = calls;
    }

    /// <summary>
    /// Enters every plugin a dimension generates with, or none.
    /// </summary>
    /// <param name="identifier">The dimension's identifier, for the message.</param>
    /// <exception cref="GeneratorStoppedException">A plugin is unloading.</exception>
    public static GeneratorCalls Enter(string identifier, ImmutableArray<PluginContainer> plugins)
    {
        if (plugins.IsDefaultOrEmpty)
            return default;

        var calls = new PluginCall[plugins.Length];

        for (var i = 0; i < plugins.Length; i++)
        {
            if (plugins[i].TryEnter() is PluginCall call)
            {
                calls[i] = call;
                continue;
            }

            for (var entered = i - 1; entered >= 0; entered--)
                calls[entered].Dispose();

            throw new GeneratorStoppedException(
                $"{identifier} can't generate: {plugins[i].Info.Name}, which it generates with, unloaded.");
        }

        return new GeneratorCalls(calls);
    }

    /// <summary>
    /// Enters every plugin, or none when one is unloading, such as the plugins whose types a structure is made of.
    /// </summary>
    /// <returns>Whether every plugin was entered.</returns>
    public static bool TryEnter(ImmutableArray<PluginContainer> plugins, out GeneratorCalls entered)
    {
        entered = default;
        if (plugins.IsDefaultOrEmpty)
            return true;

        var calls = new PluginCall[plugins.Length];

        for (var i = 0; i < plugins.Length; i++)
        {
            if (plugins[i].TryEnter() is PluginCall call)
            {
                calls[i] = call;
                continue;
            }

            for (var started = i - 1; started >= 0; started--)
                calls[started].Dispose();

            return false;
        }

        entered = new GeneratorCalls(calls);
        return true;
    }

    public void Dispose()
    {
        if (this.calls is null)
            return;

        for (var i = this.calls.Length - 1; i >= 0; i--)
            this.calls[i].Dispose();
    }
}

/// <summary>
/// A dimension can't generate any more: a plugin its generator is made of unloaded.
/// </summary>
internal sealed class GeneratorStoppedException(string message) : InvalidOperationException(message);

/// <summary>
/// Generating failed running plugins' code, such as placing a feature made of their types.
/// </summary>
/// <param name="plugins">The plugins whose code failed.</param>
internal sealed class PluginGenerationException(
    ImmutableArray<PluginContainer> plugins,
    string message,
    Exception innerException) : InvalidOperationException(message, innerException)
{
    public ImmutableArray<PluginContainer> Plugins { get; } = plugins;
}

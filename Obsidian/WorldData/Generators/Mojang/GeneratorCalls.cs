using Obsidian.Plugins;

namespace Obsidian.WorldData.Generators.Mojang;

/// <summary>
/// Calls into the plugins a dimension's generator is made of (see <see cref="MojangDimension.Plugins"/>), held while
/// a step of generation runs, which keeps them from being released until it ends.
/// </summary>
internal readonly struct GeneratorCalls : IDisposable
{
    private readonly PluginCall[]? calls;

    private GeneratorCalls(PluginCall[] calls) => this.calls = calls;

    /// <summary>
    /// Enters every plugin, or none.
    /// </summary>
    /// <exception cref="GeneratorStoppedException">A plugin is unloading.</exception>
    public static GeneratorCalls Enter(MojangDimension dimension)
    {
        var plugins = dimension.Plugins;
        if (plugins.IsEmpty)
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
                $"{dimension.Identifier} can't generate: {plugins[i].Info.Name}, which it generates with, unloaded.");
        }

        return new GeneratorCalls(calls);
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

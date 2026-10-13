namespace Obsidian.Plugins;

/// <summary>
/// Identifies one load of a plugin without keeping its services or collectible code alive. Background work and
/// registered entities keep this token after their call ends; reloading creates a different token.
/// </summary>
internal sealed class PluginOwnership(PluginContainer plugin)
{
    private readonly WeakReference<PluginContainer> plugin = new(plugin);

    public PluginContainer Resolve()
    {
        if (!this.plugin.TryGetTarget(out var owner))
            throw new InvalidOperationException("The plugin owning this work has unloaded.");

        return owner;
    }

    public PluginContainer RequireActive()
    {
        var owner = this.Resolve();
        owner.ThrowIfClosing();

        return owner;
    }
}

namespace Obsidian.Plugins;

/// <summary>
/// Completes once the plugins present at startup have loaded (or failed to), so that the worlds load after plugins
/// registered their content and world generators.
/// </summary>
internal sealed class PluginStartup
{
    private readonly TaskCompletionSource loaded = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Loaded => this.loaded.Task;

    public void Complete() => this.loaded.TrySetResult();
}

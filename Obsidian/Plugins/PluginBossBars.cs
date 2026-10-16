using System.Runtime.CompilerServices;
using System.Threading;

namespace Obsidian.Plugins;

/// <summary>
/// The boss bars a plugin created through its <see cref="IServer"/>, which its unloading retires (see
/// <see cref="BossBar.Retire"/>).
/// </summary>
/// <remarks>
/// A bar shown to players is kept here until it's hidden, so that unloading still hides it after the plugin dropped
/// it; the others are only referenced weakly, since a plugin may create many over time.
/// </remarks>
internal sealed class PluginBossBars
{
    private readonly Lock gate = new();
    private readonly ConditionalWeakTable<BossBar, PluginBossBars> bars = [];
    private readonly HashSet<BossBar> shown = [];
    private bool retired;

    /// <summary>
    /// Adds a bar the plugin created.
    /// </summary>
    /// <returns>False once the plugin's bars were retired: then the bar is retired too.</returns>
    public bool TryAdd(BossBar bar)
    {
        lock (this.gate)
        {
            if (!this.retired)
            {
                bar.Owner = this;
                this.bars.Add(bar, this);
                return true;
            }
        }

        bar.Retire();
        return false;
    }

    /// <summary>
    /// Keeps a bar while it's shown to players and lets it go once it isn't. The bar calls it under its own lock.
    /// </summary>
    public void SetShown(BossBar bar, bool shown)
    {
        lock (this.gate)
        {
            if (this.retired)
                return;

            if (shown)
                this.shown.Add(bar);
            else
                this.shown.Remove(bar);
        }
    }

    /// <summary>
    /// Retires every bar, as the plugin unloads, and those it adds later.
    /// </summary>
    public void Retire()
    {
        List<BossBar> retiring;

        lock (this.gate)
        {
            this.retired = true;
            retiring = [.. this.bars.Select(pair => pair.Key)];
            this.bars.Clear();
            this.shown.Clear();
        }

        // Outside the lock, which bars take inside theirs (see SetShown).
        foreach (var bar in retiring)
            bar.Retire();
    }
}

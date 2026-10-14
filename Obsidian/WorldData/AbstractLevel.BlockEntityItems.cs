using Obsidian.API.BlockEntities;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.Plugins;
using System.Threading;

namespace Obsidian.WorldData;

/// <summary>
/// The level's side of the data plugins' block entities give their blocks' items as the blocks break (see
/// <see cref="BlockEntity.CollectComponents"/>), which their code gives on the level's tick.
/// </summary>
/// <remarks>
/// Breaks run on the tick or elsewhere: a player's break of a plugin's block, and an explosion a ticking entity sets
/// off, run on it; an explosion a player sets off by using a bed doesn't. So the level marks the flow running its tick
/// (<see cref="EnterTick"/>), and a break elsewhere waits for the running tick to end and collects between two ticks,
/// holding the simulation gate like the tick, rather than for the next tick, which a break on the tick would wait for
/// forever.
/// </remarks>
public abstract partial class AbstractLevel
{
    // Await continuations stay on the serial tick flow. Separately scheduled tasks lose its permission as they
    // enter their captured execution context, before any child code (or its later awaits) can use it.
    private static readonly AsyncLocal<object?> tickFlow = new(change =>
    {
        if (change.ThreadContextChanged && change.CurrentValue is not null && Task.CurrentId is not null)
            tickFlow.Value = null;
    });

    private object? runningTick;

    // Whether the current flow is the level's running tick, which holds the simulation gate.
    private bool OnTick => tickFlow.Value is object mark && ReferenceEquals(mark, Volatile.Read(ref this.runningTick));

    /// <summary>
    /// Collects the components a plugin's block entity gives the item its block drops, in a call into its plugin
    /// where its code may run: at once on the level's tick, and otherwise between two ticks, once the running one
    /// ends. Call it holding no lock.
    /// </summary>
    /// <param name="blockEntity">The block entity, whose block is about to break.</param>
    /// <param name="plugin">Its plugin.</param>
    /// <param name="item">The block's item, which the block entity sets components on a stack of.</param>
    /// <returns>
    /// The components it set; none once its plugin started unloading, it was removed, or it failed.
    /// </returns>
    internal async ValueTask<IReadOnlyList<DataComponent>> CollectPluginComponentsAsync(
        BlockEntity blockEntity,
        PluginContainer plugin,
        Item item)
    {
        if (this.OnTick)
            return this.CollectPluginComponents(blockEntity, plugin, item);

        try
        {
            await this.simulationGate.WaitAsync();
        }
        catch (ObjectDisposedException)
        {
            return [];
        }

        try
        {
            return this.CollectPluginComponents(blockEntity, plugin, item);
        }
        finally
        {
            this.simulationGate.Release();
        }
    }

    // Marks the current flow as the level's tick. Called holding the simulation gate, from the async method that
    // holds it: the mark doesn't flow back out of that method.
    private void EnterTick()
    {
        var mark = new object();
        Volatile.Write(ref this.runningTick, mark);
        tickFlow.Value = mark;
    }

    private void ExitTick()
    {
        Volatile.Write(ref this.runningTick, null);
        tickFlow.Value = null;
    }

    private IReadOnlyList<DataComponent> CollectPluginComponents(
        BlockEntity blockEntity,
        PluginContainer plugin,
        Item item)
    {
        if (blockEntity.IsRemoved)
            return [];

        var carrier = new ItemStack(item, 1);
        var collected = this.CallPlugin(blockEntity, plugin, current => current.CollectComponents(carrier));

        return collected ? [.. carrier.Patch] : [];
    }
}

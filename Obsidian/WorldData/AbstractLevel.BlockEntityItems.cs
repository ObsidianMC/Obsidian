using Obsidian.API.BlockEntities;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.Plugins;

namespace Obsidian.WorldData;

/// <summary>
/// The level's side of the data plugins' block entities give their blocks' items as the blocks break (see
/// <see cref="BlockEntity.CollectComponents"/>), which their code gives on the level's tick.
/// </summary>
/// <remarks>
/// Plugin block breaks that need component collection are queued to the tick, for both players and explosions.
/// Only those serial queue callbacks request inline collection explicitly. Other callers wait for the running tick
/// to end and collect between two ticks, holding the simulation gate like the tick.
/// Execution context inherited by a background task never grants permission to bypass that gate.
/// </remarks>
public abstract partial class AbstractLevel
{
    /// <summary>
    /// Collects the components a plugin's block entity gives the item its block drops, in a call into its plugin
    /// where its code may run: at once on the level's tick, and otherwise between two ticks, once the running one
    /// ends. Call it holding no lock.
    /// </summary>
    /// <param name="blockEntity">The block entity, whose block is about to break.</param>
    /// <param name="plugin">Its plugin.</param>
    /// <param name="item">The block's item, which the block entity sets components on a stack of.</param>
    /// <param name="onLevelTick">
    /// True only for an explicitly identified serial tick operation that already holds the simulation gate.
    /// A background task must use false, even when it was started by a tick operation.
    /// </param>
    /// <returns>
    /// The components it set; none once its plugin started unloading, it was removed, or it failed.
    /// </returns>
    internal async ValueTask<IReadOnlyList<DataComponent>> CollectPluginComponentsAsync(
        BlockEntity blockEntity,
        PluginContainer plugin,
        Item item,
        bool onLevelTick = false)
    {
        if (onLevelTick)
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

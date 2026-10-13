using Obsidian.Entities;
using Obsidian.WorldData.Fluids;

namespace Obsidian.WorldData;

/// <summary>
/// Vanilla <c>Level.destroyBlock</c>: a block destroyed by something other than a player's break or an explosion. Callers
/// that vanilla destroys with a drop use this, so the loot comes from the block's loot table.
/// </summary>
public abstract partial class AbstractLevel
{
    /// <summary>
    /// Vanilla <c>destroyBlock(pos, drop, cause)</c>: when <paramref name="drop"/> is set, the block's loot spawns (with
    /// <paramref name="cause"/> as the entity in the loot context). The position takes the fluid there, with block updates, as
    /// vanilla does. Returns false for air.
    /// </summary>
    /// <remarks>
    /// The loot is evaluated with no tool, as vanilla's <c>dropResources</c> does, and doTileDrops isn't checked, as vanilla
    /// doesn't check it there. The experience vanilla drops with a block isn't handled here.
    /// </remarks>
    public async ValueTask<bool> DestroyBlockAsync(Vector position, IEntity? cause, bool drop)
    {
        var block = await this.GetBlockAsync(position);
        if (block is null || block.IsAir)
            return false;

        var drops = drop
            ? await BlockDrops.ForDestroyedAsync(this, block, position, await this.GetBlockEntityAsync(position), cause)
            : [];

        var replacement = FluidState.Of(block).CreateLegacyBlock();
        await this.SetBlockAsync(position, replacement, true);
        this.BroadcastBlockChange(replacement, position);

        foreach (var stack in drops)
            BlockDrops.SpawnDrop(this, () => Server.GetNextEntityId(), position, stack);

        return true;
    }
}

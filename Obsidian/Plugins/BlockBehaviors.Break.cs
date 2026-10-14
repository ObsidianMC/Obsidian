using Obsidian.API.Blocks;
using Obsidian.Entities;
using Obsidian.Entities.AI;
using Obsidian.API.BlockEntities;
using Obsidian.WorldData;

namespace Obsidian.Plugins;

internal static partial class BlockBehaviors
{
    /// <summary>Validates a plugin block's target and invokes its cancellable break hook on the level tick.</summary>
    public static async ValueTask<bool> CancelPlayerBreakAsync(IPlayer player, Vector position, IBlock block)
    {
        if (!TryEnter(block, out var behavior, out var call))
            return HasBehavior(block)
                || (BlocksRegistry.TryGetPluginBlock(block.GetHashCode(), out var type) && type.Binding is null);

        using (call)
        {
            var eye = Player.EyePosition(player);
            var range = player is Player concrete ? concrete.BlockInteractionRange : 4.5;
            var end = eye + (VectorD)player.GetLookDirection() * range;
            var hit = new MobTerrain(player.Level).RaycastOutlines(eye, end);
            if (hit is null || hit.Value.Position != position)
                return true;

            var context = new BlockBreakContext
            {
                Player = player,
                Block = block,
                Hit = hit.Value
            };

            try
            {
                await behavior.BreakAsync(context);
            }
            catch (Exception exception)
            {
                if (player.Level is AbstractLevel failed)
                    HookFailed(failed.Logger, exception, block.UnlocalizedName, nameof(IBlockBehavior.BreakAsync));

                context.IsCancelled = true;
            }

            // A second queued break must see the remaining parts, even before end-of-tick publication.
            if (player.Level is AbstractLevel level
                && await level.GetBlockEntityAsync(position) is BlockEntity entity)
            {
                level.RefreshBlockEntitySnapshot(entity);
            }

            return context.IsCancelled;
        }
    }
}

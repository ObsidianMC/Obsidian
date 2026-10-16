using Obsidian.API.Blocks;
using Obsidian.Entities;
using Obsidian.Entities.AI;
using Obsidian.API.BlockEntities;
using Obsidian.API.Inventory;
using Obsidian.WorldData;

namespace Obsidian.Plugins;

internal static partial class BlockBehaviors
{
    /// <summary>Validates a plugin block's target and invokes its cancellable break hook on the level tick.</summary>
    public static async ValueTask<PlayerBreakResult> EvaluatePlayerBreakAsync(
        IPlayer player, Vector position, IBlock block)
    {
        if (!TryEnter(block, out var behavior, out var call))
            return new PlayerBreakResult(HasBehavior(block)
                || (BlocksRegistry.TryGetPluginBlock(block.GetHashCode(), out var type) && type.Binding is null), []);

        using (call)
        {
            var eye = Player.EyePosition(player);
            var range = Player.BlockInteractionRange(player);
            var end = eye + (VectorD)player.GetLookDirection() * range;
            var hit = new MobTerrain(player.Level).RaycastOutlines(eye, end);
            if (hit is null || hit.Value.Position != position)
                return new PlayerBreakResult(true, []);

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
                context.ExtraDrops.Clear();
            }

            // A second queued break must see the remaining parts, even before end-of-tick publication.
            if (player.Level is AbstractLevel level
                && await level.GetBlockEntityAsync(position) is BlockEntity entity)
            {
                level.RefreshBlockEntitySnapshot(entity);
            }

            return new PlayerBreakResult(context.IsCancelled,
                context.ExtraDrops.Where(stack => !stack.IsAir && stack.Count > 0)
                    .Select(stack => new ItemStack(stack, stack.Count)).ToArray());
        }
    }

    internal sealed record PlayerBreakResult(bool IsCancelled, IReadOnlyList<ItemStack> ExtraDrops);
}

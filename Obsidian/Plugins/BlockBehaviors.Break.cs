using Obsidian.API.Blocks;
using Obsidian.Entities;
using Obsidian.Entities.AI;

namespace Obsidian.Plugins;

internal static partial class BlockBehaviors
{
    /// <summary>Validates a plugin block's target and invokes its cancellable break hook.</summary>
    public static async ValueTask<bool> CancelPlayerBreakAsync(IPlayer player, Vector position, IBlock block)
    {
        if (!TryEnter(block, out var behavior, out var call))
            return false;

        using (call)
        {
            var eye = new VectorD(player.Position.X, player.HeadY, player.Position.Z);
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

            await behavior.BreakAsync(context);
            return context.IsCancelled;
        }
    }
}

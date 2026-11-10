using Obsidian.API.Blocks;

namespace Obsidian.WorldData;

internal static partial class UseBehaviors
{
    internal static IBlockUseBehavior CandleBlock { get; } = new CandleUse(false);

    internal static IBlockUseBehavior CandleCakeBlock { get; } = new CandleUse(true);

    private sealed class CandleUse(bool cake) : IBlockUseBehavior
    {
        public ValueTask<UseResult> UseItemOnAsync(BlockUse use)
        {
            if (cake && use.Item?.Holder.Id is ItemIds.FlintAndSteel or ItemIds.FireCharge)
                return ValueTask.FromResult(UseResult.Pass);

            // Vanilla candles require mayBuild; candle cakes instead distinguish the candle from the cake hit.
            if (use.HasItem || use.Block.GetProperty("lit") != "true"
                || (cake ? use.Cursor.Y <= 0.5 : !use.MayBuild))
            {
                return ValueTask.FromResult(UseResult.TryWithEmptyHand);
            }

            var committed = use.TryCommit(() =>
                use.Level.BlockUpdates.SetBlock(use.Position, use.Block.WithProperty("lit", false),
                    BlockUpdateFlags.AllImmediate));
            if (committed)
                BlockLighting.Extinguished(use.Level, use.Position, use.Block, use.Player);

            return ValueTask.FromResult(committed ? UseResult.Success : UseResult.Fail);
        }
    }
}

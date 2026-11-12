using Obsidian.WorldData.TrialSpawners;

namespace Obsidian.WorldData;

internal static partial class UseBehaviors
{
    internal static IBlockUseBehavior VaultBlock { get; } = new VaultUse();

    private sealed class VaultUse : IBlockUseBehavior
    {
        public ValueTask<UseResult> UseItemOnAsync(BlockUse use)
        {
            if (!use.HasItem || use.Block.GetProperty("vault_state") != "active"
                || use.BlockEntity is not VaultBlockEntity vault)
            {
                return ValueTask.FromResult(UseResult.TryWithEmptyHand);
            }

            var rewarded = false;
            if (!use.TryCommit(() => vault.TryUnlock(use, out rewarded)))
                return ValueTask.FromResult(UseResult.Fail);

            if (rewarded)
                use.Player.Statistics.Increment(use.Item!.Holder.UnlocalizedName, type: "minecraft:used");

            return ValueTask.FromResult(UseResult.SuccessServer);
        }
    }
}

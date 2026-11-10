using Obsidian.API.Inventory;

namespace Obsidian.WorldData;

// The use behaviour of flower pots.
internal static partial class UseBehaviors
{
    internal static IBlockUseBehavior FlowerPotBlock { get; } = new FlowerPotUse();

    /// <summary>
    /// Vanilla's <c>FlowerPotBlock</c>: an empty pot takes one of a plant it can hold, and gives its plant back to a
    /// click without one. A full pot refuses another plant rather than swapping it.
    /// </summary>
    private sealed class FlowerPotUse : IBlockUseBehavior
    {
        public async ValueTask<UseResult> UseItemOnAsync(BlockUse use)
        {
            var potted = use.HasItem ? FlowerPots.PotFor(use.Item!) : null;
            if (potted is null)
                return UseResult.TryWithEmptyHand;

            if (!FlowerPots.IsEmpty(use.Block))
                return UseResult.Consume;

            if (!use.TryCommit(() => use.Level.BlockUpdates.SetBlock(use.Position, potted), consumeItem: true))
                return UseResult.Fail;

            await use.Player.SyncInventorySlotAsync(use.HandSlot);
            use.Level.EmitGameEvent(MobGameEvent.BlockChange, use.Center, use.Player);
            use.Award("minecraft:pot_flower");

            return UseResult.Success;
        }

        public async ValueTask<UseResult> UseWithoutItemAsync(BlockUse use)
        {
            if (FlowerPots.ContentOf(use.Block) is not ItemStack plant)
                return UseResult.Consume;

            BlockUseReturn? given = null;
            if (!use.TryCommit(() =>
            {
                if (!use.Level.BlockUpdates.SetBlock(use.Position, BlocksRegistry.FlowerPot))
                    return false;

                given = BlockUseReturn.Give(use, plant);
                return true;
            }))
                return UseResult.Fail;

            await given!.FinishAsync(use);
            use.Level.EmitGameEvent(MobGameEvent.BlockChange, use.Center, use.Player);

            return UseResult.Success;
        }
    }
}

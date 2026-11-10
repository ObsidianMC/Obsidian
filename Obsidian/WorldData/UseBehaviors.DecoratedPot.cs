using Obsidian.API.Inventory;

namespace Obsidian.WorldData;

// The use behaviour of decorated pots.
internal static partial class UseBehaviors
{
    internal static IBlockUseBehavior DecoratedPotBlock { get; } = new DecoratedPotUse();

    /// <summary>
    /// Vanilla's <c>DecoratedPotBlock</c>: an item goes in while the pot is empty or holds less than a stack of the
    /// same item, and the pot wobbles; anything else only wobbles it the other way.
    /// </summary>
    private sealed class DecoratedPotUse : IBlockUseBehavior
    {
        // Vanilla's DUST_PLUME burst over the pot as an item goes in.
        private const int PlumeCount = 7;

        public async ValueTask<UseResult> UseItemOnAsync(BlockUse use)
        {
            if (use.BlockEntity is not DecoratedPotBlockEntity pot)
                return UseResult.Pass;

            // Vanilla's getTheItem rolls a pot's pending loot, without a player, before anything reads what it holds,
            // even for an empty hand. Rolling may load chunks, so it runs here, holding no lock, and may wait for the
            // pot's inventory; the commit refuses if a loot table is pending again by then.
            if (HasPendingLoot(pot))
                use.Level.UnpackLootTable(pot, opener: null);

            if (!use.HasItem || !pot.Accepts(use.Item!))
                return UseResult.TryWithEmptyHand;

            var item = use.Item!;
            var count = 0;
            if (!use.TryCommit(() => pot.TryPut(item, out count), consumeItem: true, destination: pot.Inventory))
                return UseResult.Fail;

            pot.Shake(DecoratedPotBlockEntity.Wobble.Positive);
            await use.Player.SyncInventorySlotAsync(use.HandSlot);
            use.Player.Statistics.Increment(item.Holder.UnlocalizedName, type: "minecraft:used");

            // Vanilla's pitch rises with how full the pot is.
            var fullness = count / (float)item.MaxStackSize;
            var pitch = 0.7f + 0.5f * fullness;
            use.Level.BlockUpdates.PlaySound(use.Position, "minecraft:block.decorated_pot.insert", 1, pitch);

            var plume = (VectorD)use.Position + new VectorD(0.5, 1.2, 0.5);
            use.Level.SpawnParticles(new Plume(), plume, PlumeCount);

            use.Level.UpdateComparatorsOf(pot);
            use.Level.EmitGameEvent(MobGameEvent.BlockChange, use.Center, use.Player);

            return UseResult.Success;
        }

        public ValueTask<UseResult> UseWithoutItemAsync(BlockUse use)
        {
            if (use.BlockEntity is not DecoratedPotBlockEntity pot)
                return ValueTask.FromResult(UseResult.Pass);

            use.Level.BlockUpdates.PlaySound(use.Position, "minecraft:block.decorated_pot.insert_fail");
            pot.Shake(DecoratedPotBlockEntity.Wobble.Negative);
            use.Level.EmitGameEvent(MobGameEvent.BlockChange, use.Center, use.Player);

            return ValueTask.FromResult(UseResult.Success);
        }

        private static bool HasPendingLoot(DecoratedPotBlockEntity pot)
        {
            lock (pot.LootLock)
                return pot.LootTable is not null;
        }

        private sealed class Plume : ParticleData
        {
            public override ParticleType ParticleType => ParticleType.DustPlume;
        }
    }
}

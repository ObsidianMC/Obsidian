using Obsidian.API.Inventory;
using Obsidian.API.Registries;
using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Clientbound;

namespace Obsidian.WorldData;

public abstract partial class AbstractLevel
{
    // Vanilla's LevelEvent.COMPOSTER_FILL: the data says whether the level rose.
    private const int ComposterFillEvent = 1500;

    /// <summary>
    /// Vanilla's <c>ComposterBlock.useItemOn</c> and <c>useWithoutItem</c>: a compostable item in the held hand goes
    /// in while the level is below full; a ready composter gives its bone meal out, whatever is held.
    /// </summary>
    internal async ValueTask<bool> UseComposterAsync(IClientPlayer player, Vector position, IBlock block, int handSlot)
    {
        ItemStack? held;
        lock (this.Fluids.TickLock)
        {
            if (!block.Is(BlockIds.Composter)
                || player.GameMode == GameMode.Spectator
                || player.Health <= 0
                || (player.Position - (VectorD)position).MagnitudeSquared() > 36)
            {
                return false;
            }

            var level = Composters.LevelOf(block);
            if (level == Composters.Ready)
            {
                this.TakeBoneMeal(position);
                return true;
            }

            using (player.Inventory.Lock())
            {
                var stack = player.Inventory[handSlot];
                if (level >= Composters.Full
                    || stack.IsNullOrAir()
                    || Composters.ChanceOf(stack!.Holder.Id) is not float chance)
                {
                    return false;
                }

                if (player.GameMode != GameMode.Creative)
                    player.Inventory.RemoveItem(handSlot, 1);

                this.Fill(position, chance);
                held = player.Inventory[handSlot] is ItemStack current
                    ? Obsidian.Net.NetworkBuffer.SnapshotItemStack(current)
                    : null;
            }
        }

        await player.QueuePacketAsync(new ContainerSetSlotPacket
        {
            ContainerId = 0,
            Slot = (short)handSlot,
            SlotData = held
        });
        return true;
    }

    /// <summary>
    /// Puts one item in a composter below full, rolling its chance: the level rises if the roll passes. The item is
    /// used up either way. Runs under the tick lock; returns whether the level rose.
    /// </summary>
    internal bool Fill(Vector position, float chance)
    {
        lock (this.Fluids.TickLock)
        {
            var updates = this.BlockUpdates;
            var block = updates.GetBlock(position);
            if (!block.Is(BlockIds.Composter) || Composters.LevelOf(block) >= Composters.Full)
                return false;

            var next = Composters.Filled(block, chance, Random.Shared.NextSingle(), out var rose);
            if (rose)
                updates.SetBlock(position, next);

            this.BroadcastLevelEvent(ComposterFillEvent, position, rose ? 1 : 0);
            updates.UpdateAnalogOutput(position);
            return rose;
        }
    }

    /// <summary>
    /// Empties a ready composter to 0 and returns whether it was ready. The bone meal is the caller's to hand out: a
    /// hopper takes it, a player's click drops it above (see <see cref="TakeBoneMeal"/>).
    /// </summary>
    internal bool EmptyReadyComposter(Vector position)
    {
        lock (this.Fluids.TickLock)
        {
            var updates = this.BlockUpdates;
            var block = updates.GetBlock(position);
            if (!block.Is(BlockIds.Composter) || Composters.LevelOf(block) != Composters.Ready)
                return false;

            updates.SetBlock(position, block.WithProperty("level", "0"));
            updates.UpdateAnalogOutput(position);
            return true;
        }
    }

    /// <summary>A player's use of a ready composter: it drops its bone meal above, with the empty sound.</summary>
    internal void TakeBoneMeal(Vector position)
    {
        lock (this.Fluids.TickLock)
        {
            if (!this.EmptyReadyComposter(position))
                return;

            this.SpawnItem(BoneMealSpawn(position), ItemsRegistry.GetSingleItem(ItemsRegistry.BoneMeal));
            Composters.PlaySound(this, position, Obsidian.API.SoundId.Block.ComposterEmpty);
        }
    }

    // Vanilla's ComposterBlock.extractProduce: the bone meal lands at (0.5, 1.01, 0.5) of the block, offset by up to
    // 0.35 blocks each way on each axis (Vec3.offsetRandom with scale 0.7).
    private static VectorD BoneMealSpawn(Vector position) => new(
        position.X + 0.5 + (Random.Shared.NextSingle() - 0.5) * 0.7,
        position.Y + 1.01 + (Random.Shared.NextSingle() - 0.5) * 0.7,
        position.Z + 0.5 + (Random.Shared.NextSingle() - 0.5) * 0.7);
}

/// <summary>
/// A composter's item access, as vanilla's container for its level: the top takes one compostable item while the
/// level is below full, and the bottom gives out the bone meal of a ready one. Nothing else reaches it.
/// </summary>
internal sealed class ComposterItemAccess(AbstractLevel level, Vector position, BlockFace? side) : IItemAccess
{
    public int SlotCount => this.Accepts || this.Yields ? 1 : 0;

    public ItemStack? GetItem(int slot) => this.Yields && slot == 0 ? BoneMeal() : null;

    public ItemStack? Insert(int slot, ItemStack stack, bool simulate = false)
    {
        var leftover = stack.Count > 1 ? new ItemStack(stack, stack.Count - 1) : null;
        if (slot != 0 || stack.IsNullOrAir() || !this.Accepts || Composters.ChanceOf(stack.Holder.Id) is not float chance)
            return stack;

        if (!simulate)
            level.Fill(position, chance);

        return leftover;
    }

    public ItemStack? Extract(int slot, int count, bool simulate = false)
    {
        if (slot != 0 || count <= 0 || !this.Yields)
            return null;

        // A hopper takes the bone meal itself; nothing drops above, unlike a player's use.
        if (!simulate && !level.EmptyReadyComposter(position))
            return null;

        return BoneMeal();
    }

    private bool Accepts => side == BlockFace.Up && this.Level() is < Composters.Full;

    private bool Yields => side == BlockFace.Down && this.Level() == Composters.Ready;

    private int Level()
    {
        lock (level.Fluids.TickLock)
        {
            return Composters.LevelOf(level.BlockUpdates.GetBlock(position));
        }
    }

    private static ItemStack BoneMeal() => ItemsRegistry.GetSingleItem(ItemsRegistry.BoneMeal);
}

using Obsidian.API.Inventory;
using Obsidian.Net.Packets;
using Obsidian.WorldData;

namespace Obsidian.Entities;

// The player's own items and experience at their death and respawn.
public partial class Player
{
    // Vanilla's Inventory.dropAll order, which decides the order of the item entities: the hotbar, the main inventory,
    // then the equipment in EquipmentSlot order (off hand, feet, legs, chest, head), as the inventory window's slots.
    private static readonly int[] deathDropSlots =
    [
        .. Enumerable.Range(36, 9),
        .. Enumerable.Range(9, 27),
        45,
        8,
        7,
        6,
        5
    ];

    /// <summary>
    /// Whether the player keeps their items and experience through a death: like vanilla, with the
    /// <c>keep_inventory</c> rule on, or as a spectator.
    /// </summary>
    private bool KeepsItemsThroughDeath =>
        this.Level.LevelData.GetBooleanRule("keep_inventory", defaultValue: false)
        || this.GameMode == GameMode.Spectator;

    /// <summary>
    /// Drops what the player loses at their death, unless they keep it (see <see cref="KeepsItemsThroughDeath"/>):
    /// their inventory, armour and off hand like vanilla's <c>Player.dropEquipment</c>, the items of plugins' item
    /// attachments (see <see cref="DropItemAttachments"/>), then their experience like vanilla's
    /// <c>dropExperience</c>. Items with the curse of vanishing are destroyed instead.
    /// </summary>
    /// <remarks>
    /// The slots are emptied under the inventory's click lock, so that a click being applied meanwhile can't move an
    /// item the death drops; the items are dropped once it's released. Drops and orbs are the server's, never the
    /// plugin whose code killed the player.
    /// </remarks>
    private async ValueTask DropDeathLootAsync()
    {
        if (this.KeepsItemsThroughDeath)
            return;

        var drops = new List<ItemStack>();
        List<ClientboundPacket> updates = [];
        ClickWindowState state;

        using (await this.Inventory.LockAsync())
        {
            foreach (var slot in deathDropSlots)
            {
                if (this.Inventory.GetItem(slot) is not ItemStack item)
                    continue;

                this.Inventory.RemoveItem(slot);
                this.CaptureInventorySlot(slot, updates);

                if (item.Count > 0 && !Vanishes(item))
                    drops.Add(item);
            }

            state = ClickWindowState.Of(this);
        }

        // Vanilla's dead player still sends their emptied slots, before they respawn.
        if (updates.Count > 0)
            await this.WindowSnapshots.SendChangeAsync(this, state, updates);

        foreach (var item in drops)
            ItemEntity.DropAround(this, item);

        this.DropItemAttachments();
        this.DropExperience();
    }

    // Vanilla's Player.getBaseExperienceReward, min(level * 7, 100), split into orbs like ExperienceOrb.award. A
    // player drops it whoever killed them, and whatever the mob_drops rule says.
    private void DropExperience()
    {
        if (this.Level is not AbstractLevel level)
            return;

        var remaining = Math.Min(this.XpLevel * 7, 100);

        while (remaining > 0)
        {
            var value = ExperienceOrb.SplitValue(remaining);
            var orb = new ExperienceOrb
            {
                Level = level,
                EntityId = Obsidian.Server.GetNextEntityId(),
                Position = this.Position,
                Value = value
            };

            // An orb outside the loaded regions is lost, as there's nowhere to keep it.
            level.TrySpawnEntity(orb, hostOwned: true);
            remaining -= value;
        }
    }

    /// <summary>
    /// Starts the player's next life, as they respawn after a death, like vanilla's <c>ServerPlayer.restoreFrom</c>
    /// for a respawn: the item on their cursor and in their crafting grid drop, whatever the rule, as vanilla drops
    /// them with the dead player's menu. Unless they keep their items (see <see cref="KeepsItemsThroughDeath"/>), their
    /// inventory starts empty and their experience, and with it their score, at zero.
    /// </summary>
    /// <remarks>
    /// The inventory was emptied at the death (see <see cref="DropDeathLootAsync"/>); what reached it since is lost,
    /// like vanilla's new player doesn't have it. The client is sent the inventory and experience with the respawn.
    /// </remarks>
    private async ValueTask StartLifeAfterDeathAsync()
    {
        var keeps = this.KeepsItemsThroughDeath;
        var drops = new List<ItemStack>();

        using (await this.Inventory.LockAsync())
        {
            if (this.CarriedItem is { Count: > 0 } carried)
                drops.Add(carried);

            this.CarriedItem = null;

            // The crafting grid's result and its four slots, then, unless the player keeps their items, the rest.
            var emptied = keeps ? 5 : this.Inventory.Size;

            for (var slot = 0; slot < emptied; slot++)
            {
                if (this.Inventory.GetItem(slot) is not ItemStack item)
                    continue;

                this.Inventory.RemoveItem(slot);

                if (slot is >= 1 and <= 4 && item.Count > 0)
                    drops.Add(item);
            }
        }

        // Vanilla throws them where the player looks; dropping them around the dead player is as good.
        foreach (var item in drops)
            ItemEntity.DropAround(this, item);

        if (keeps)
            return;

        this.XpLevel = 0;
        this.XpTotal = 0;
        this.XpP = 0;
    }
}

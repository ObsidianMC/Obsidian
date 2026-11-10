using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.WorldData;
using System.Text.Json;

namespace Obsidian.Entities;

/// <summary>
/// A player's use of an armor stand as it was when the click arrived: who, with which hand, what they held, and how
/// high on the stand they clicked. The stand's tick commits it only if the hand is still what it was.
/// </summary>
internal sealed record ArmorStandUse(
    PlayerBase Player,
    InteractionHand Hand,
    int HandSlot,
    ItemStack? Item,
    ItemStack? UsedItem,
    double ClickedHeight)
{
    /// <summary>Reads the hand under the inventory's lock, outside the level's tick lock.</summary>
    internal static async ValueTask<ArmorStandUse> CaptureAsync(PlayerBase player, InteractionHand hand,
        VectorF? location)
    {
        var slot = hand == InteractionHand.OffHand ? 45 : player.CurrentHeldItemSlot;
        using (await player.Inventory.LockAsync())
        {
            var item = player.Inventory.GetItem(slot);
            var used = item is null ? null : new ItemStack(item, item.Count);
            return new ArmorStandUse(player, hand, slot, item, used, location?.Y ?? 0);
        }
    }
}

public sealed partial class ArmorStand
{
    // Vanilla's EquipmentSlot.getFilterBit(0) of each slot the stand has.
    private static int FilterBit(EquipmentSlot slot) => slot switch
    {
        EquipmentSlot.MainHand => 0,
        EquipmentSlot.Boots => 1,
        EquipmentSlot.Leggings => 2,
        EquipmentSlot.Chestplate => 3,
        EquipmentSlot.Helmet => 4,
        EquipmentSlot.OffHand => 5,
        _ => -1
    };

    private static bool IsHand(EquipmentSlot slot) => slot is EquipmentSlot.MainHand or EquipmentSlot.OffHand;

    /// <summary>
    /// What one use changes: the stand's slot gets <see cref="SlotItem"/>, and the hand gets <see cref="HandItem"/>,
    /// or keeps its stack less <see cref="Taken"/> items when that is null.
    /// </summary>
    private sealed record Exchange(EquipmentSlot Slot, ItemStack SlotItem, ItemStack? HandItem, int Taken);

    /// <summary>
    /// Vanilla's <c>ArmorStand.interact</c> through <c>Player.interactOn</c>: the held item goes to its own slot,
    /// swapping with what is there, and an empty hand takes the item of the slot clicked. Runs on the level's tick.
    /// The stand and the hand change together under the tick lock with the inventory tried, not waited for: a busy
    /// inventory, a hand that changed since the click, a player who left, died, changed level or is being teleported,
    /// or a stand that is gone refuses the use without changing anything. Sounds, game events and packets go out
    /// once the locks are released.
    /// </summary>
    /// <returns>
    /// Whether the use is done with; false lets the held item's own use on a living entity run, as vanilla's
    /// <c>interactOn</c> does when the stand passes or fails.
    /// </returns>
    internal async ValueTask<bool> UseAsync(ArmorStandUse use)
    {
        var player = use.Player;

        // Vanilla's interactOn: a spectator's use does nothing, not even the item's own use.
        if (player.GameMode == GameMode.Spectator)
            return true;

        if (this.Marker || use.Item?.Is(ItemsRegistry.NameTag) == true || this.Level is not AbstractLevel level)
            return false;

        Exchange? exchange = null;
        ItemStack? previous = null;
        var refused = false;
        lock (level.Fluids.TickLock)
        {
            if (!this.CanCommit(use, level) || !player.Inventory.TryLock(out var handLock))
            {
                refused = true;
            }
            else
            {
                using (handLock)
                {
                    var held = player.Inventory.GetItem(use.HandSlot);
                    if (!SameHand(held, use))
                    {
                        refused = true;
                    }
                    else
                    {
                        var creative = player.GameMode == GameMode.Creative;
                        exchange = this.Decide(held ?? ItemStack.Air, use.ClickedHeight, creative);
                        if (exchange is not null)
                        {
                            previous = this.GetEquipment(exchange.Slot);
                            this.Apply(exchange, player, use.HandSlot, held);
                        }
                    }
                }
            }
        }

        if (refused || exchange is null)
        {
            await player.SyncInventorySlotAsync(use.HandSlot);
            return refused;
        }

        this.PublishEquip(level, exchange.Slot, previous!, exchange.SlotItem);
        await player.SyncInventorySlotAsync(use.HandSlot);

        // Vanilla's handleInteract: the trigger with the item as it was, and a swing every player sees.
        await player.FireProgressAsync("player_interacted_with_entity", item: use.UsedItem, entity: this);
        player.SwingForUse(use.Hand, use.UsedItem, includeSelf: true);
        return true;
    }

    // The click still stands: the player can act here, still holds the slot they clicked with, and the stand is
    // still alive in this level.
    private bool CanCommit(ArmorStandUse use, AbstractLevel level)
    {
        var player = use.Player;
        if (!player.Connected || !player.Alive || player.Respawning || player.HasPendingTeleport
            || !ReferenceEquals(player.Level, level))
        {
            return false;
        }

        if (use.Hand == InteractionHand.MainHand && player.CurrentHeldItemSlot != use.HandSlot)
            return false;

        if (!this.Alive || !ReferenceEquals(this.Level, level))
            return false;

        return level.GetRegionForLocation(this.Position)?.Entities.TryGetValue(this.EntityId, out var resident) == true
            && ReferenceEquals(resident, this);
    }

    // The hand is the stack the click saw, with the same count and components.
    private static bool SameHand(ItemStack? held, ArmorStandUse use)
    {
        if (!ReferenceEquals(held, use.Item))
            return false;

        if (held is null)
            return true;

        return held.Count == use.UsedItem?.Count && held.Equals(use.UsedItem);
    }

    // Vanilla's interact, getClickedSlot and swapItem, deciding what changes without changing it.
    private Exchange? Decide(ItemStack held, double clickedHeight, bool creative)
    {
        var empty = held is not { IsAir: false, Count: > 0 };
        var heldSlot = this.SlotFor(held, empty);
        if (empty)
        {
            var clicked = this.ClickedSlot(clickedHeight);
            var target = this.IsDisabled(clicked) ? heldSlot : clicked;
            if (this.GetEquipment(target).IsAir)
                return null;

            return this.Swap(target, held, empty, creative);
        }

        if (this.IsDisabled(heldSlot) || IsHand(heldSlot) && !this.ShowArms)
            return null;

        return this.Swap(heldSlot, held, empty, creative);
    }

    // Vanilla's LivingEntity.getEquipmentSlotForItem: the item's equippable slot when the stand can use it, otherwise
    // the main hand.
    private EquipmentSlot SlotFor(ItemStack held, bool empty)
    {
        if (empty || EquippableSlot(held) is not EquipmentSlot slot)
            return EquipmentSlot.MainHand;

        var usable = FilterBit(slot) >= 0 && !this.IsDisabled(slot);
        return usable ? slot : EquipmentSlot.MainHand;
    }

    // Vanilla's getClickedSlot: the height of the click, in the stand's own scale, picks among the filled slots, in
    // this order.
    private EquipmentSlot ClickedSlot(double clickedHeight)
    {
        var y = clickedHeight / (this.Small ? 0.5 : 1.0);
        var small = this.Small;
        var filled = (EquipmentSlot slot) => !this.GetEquipment(slot).IsAir;

        if (y >= 0.1 && y < 0.1 + (small ? 0.8 : 0.45) && filled(EquipmentSlot.Boots))
            return EquipmentSlot.Boots;

        if (y >= 0.9 + (small ? 0.3 : 0.0) && y < 0.9 + (small ? 1.0 : 0.7) && filled(EquipmentSlot.Chestplate))
            return EquipmentSlot.Chestplate;

        if (y >= 0.4 && y < 0.4 + (small ? 1.0 : 0.8) && filled(EquipmentSlot.Leggings))
            return EquipmentSlot.Leggings;

        if (y >= 1.6 && filled(EquipmentSlot.Helmet))
            return EquipmentSlot.Helmet;

        if (!filled(EquipmentSlot.MainHand) && filled(EquipmentSlot.OffHand))
            return EquipmentSlot.OffHand;

        return EquipmentSlot.MainHand;
    }

    private bool IsDisabled(EquipmentSlot slot) =>
        (this.DisabledSlots & 1 << FilterBit(slot)) != 0 || IsHand(slot) && !this.ShowArms;

    // Vanilla's swapItem: locked slots refuse, creative players put a copy of one item, a single item or an empty
    // hand swaps, and a stack gives one item to an empty slot.
    private Exchange? Swap(EquipmentSlot slot, ItemStack held, bool empty, bool creative)
    {
        var current = this.GetEquipment(slot);
        var bit = FilterBit(slot);
        if (!current.IsAir && (this.DisabledSlots & 1 << (bit + 8)) != 0)
            return null;

        if (current.IsAir && (this.DisabledSlots & 1 << (bit + 16)) != 0)
            return null;

        if (creative && current.IsAir && !empty)
            return new Exchange(slot, new ItemStack(held, 1), HandItem: null, Taken: 0);

        if (empty || held.Count <= 1)
            return new Exchange(slot, empty ? ItemStack.Air : held, HandItem: current, Taken: 0);

        if (!current.IsAir)
            return null;

        return new Exchange(slot, new ItemStack(held, 1), HandItem: null, Taken: 1);
    }

    private void Apply(Exchange exchange, PlayerBase player, int handSlot, ItemStack? held)
    {
        this.StoreEquipment(exchange.Slot, exchange.SlotItem);
        if (exchange.HandItem is ItemStack swapped)
        {
            player.Inventory.SetItem(handSlot, swapped.IsAir ? null : swapped);
        }
        else if (exchange.Taken > 0 && held is not null)
        {
            player.Inventory.RemoveItem(handSlot, exchange.Taken);
        }
    }

    // Vanilla's LivingEntity.onEquipItem for the stand's slot: a different item plays its equip sound when it goes
    // in its own slot, and every change is an equip or unequip game event. Then the players who see the stand are
    // shown the slot, as its next tick would.
    private void PublishEquip(AbstractLevel level, EquipmentSlot slot, ItemStack previous, ItemStack next)
    {
        this.BroadcastEquipment(slot);
        if (SameItem(previous, next))
            return;

        var equippable = EquippableSlot(next);
        if (!this.Silent && equippable == slot && EquipSound(next) is string sound)
        {
            var packet = new SoundPacket
            {
                SoundLocation = sound,
                SoundPosition = new SoundPosition(this.Position.X, this.Position.Y, this.Position.Z),
                Category = SoundCategory.Neutral,
                Volume = 1,
                Pitch = 1,
                Seed = Random.Shared.NextInt64()
            };
            this.PacketBroadcaster.QueuePacketToLevelInRange(level, this.Position, packet);
        }

        var kind = equippable is null ? MobGameEvent.Unequip : MobGameEvent.Equip;
        level.EmitGameEvent(kind, this.Position, this);
    }

    // Vanilla's ItemStack.isSameItemSameComponents, with air as the empty stack.
    private static bool SameItem(ItemStack previous, ItemStack next)
    {
        if (previous.IsAir || next.IsAir)
            return previous.IsAir && next.IsAir;

        return previous.Equals(next);
    }

    // The slot of the item's equippable component, from the stack's own component or its item's default.
    private static EquipmentSlot? EquippableSlot(ItemStack item)
    {
        if (item.IsAir || item.RemoveComponents.Contains(DataComponentType.Equippable))
            return null;

        if (item.GetComponent<EquippableDataComponent>(DataComponentType.Equippable) is { } custom)
            return custom.Slot;

        var component = CombatItems.Component(item, "equippable");
        if (component.ValueKind != JsonValueKind.Object || !component.TryGetProperty("slot", out var slot))
            return null;

        return slot.GetString() switch
        {
            "mainhand" => EquipmentSlot.MainHand,
            "offhand" => EquipmentSlot.OffHand,
            "feet" => EquipmentSlot.Boots,
            "legs" => EquipmentSlot.Leggings,
            "chest" => EquipmentSlot.Chestplate,
            "head" => EquipmentSlot.Helmet,
            "body" => EquipmentSlot.Body,
            "saddle" => EquipmentSlot.Saddle,
            _ => null
        };
    }

    private static string? EquipSound(ItemStack item)
    {
        if (item.GetComponent<EquippableDataComponent>(DataComponentType.Equippable) is { } custom)
            return custom.EquipSound.ResourceLocation;

        var component = CombatItems.Component(item, "equippable");
        if (component.ValueKind != JsonValueKind.Object || !component.TryGetProperty("equip_sound", out var sound))
            return "minecraft:item.armor.equip_generic";

        return sound.ValueKind == JsonValueKind.String ? sound.GetString() : null;
    }
}

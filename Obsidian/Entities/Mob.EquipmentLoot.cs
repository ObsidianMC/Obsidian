using Obsidian.API.Inventory;
using Obsidian.API.Loot;
using Obsidian.Nbt;
using Obsidian.WorldData;
using Obsidian.WorldData.Dispensing;

namespace Obsidian.Entities;

public partial class Mob
{
    internal static readonly (EquipmentSlot Slot, string Name)[] SavedEquipmentSlots =
    [
        (EquipmentSlot.MainHand, "mainhand"), (EquipmentSlot.OffHand, "offhand"),
        (EquipmentSlot.Boots, "feet"), (EquipmentSlot.Leggings, "legs"),
        (EquipmentSlot.Chestplate, "chest"), (EquipmentSlot.Helmet, "head"),
        (EquipmentSlot.Body, "body"), (EquipmentSlot.Saddle, "saddle")
    ];

    /// <summary>Rolls spawn-data equipment and fills each resolved slot once, in loot order.</summary>
    internal void EquipFromTable(NbtCompound specification)
    {
        if (this.Level is not AbstractLevel level
            || !specification.TryGetTagValue<string>("loot_table", out var id))
        {
            return;
        }

        var resolver = PluginLootTables.CaptureResolver();
        if (resolver(id) is not LootTable table)
            return;

        var random = table.RandomSequence is string sequence
            ? BlockLoot.SequencesFor(level).Get(sequence)
            : level.GrowthRandom;
        List<ItemStack> items;
        lock (random)
        {
            items = table.GetRandomItems(new LootContext
            {
                Random = random,
                Origin = this.Position,
                ThisEntity = this,
                ResolveTable = resolver
            });
        }

        var used = new HashSet<EquipmentSlot>();
        foreach (var item in items)
        {
            if (item.IsAir || item.Count <= 0)
                continue;

            var slot = Wearable.Of(item)?.Slot ?? EquipmentSlot.MainHand;
            if (!used.Add(slot))
                continue;

            var count = slot is EquipmentSlot.MainHand or EquipmentSlot.OffHand ? item.Count : 1;
            this.SetEquipment(slot, new ItemStack(item, count));
            if (specification.TryGetNumber<float>("slot_drop_chances", out var all))
            {
                this.SetEquipmentDropChance(slot, all);
            }
            else if (specification.TryGetTag<NbtCompound>("slot_drop_chances", out var chances))
            {
                var name = SavedEquipmentSlots.Single(entry => entry.Slot == slot).Name;
                if (chances.TryGetNumber<float>(name, out var chance))
                    this.SetEquipmentDropChance(slot, chance);
            }
        }
    }

    /// <summary>A discard drops equipment whose chance is greater than one, including picked-up items.</summary>
    internal void DropPreservedEquipment()
    {
        if (this.Level is not AbstractLevel level)
            return;

        foreach (var (slot, _) in SavedEquipmentSlots)
        {
            var item = this.GetEquipment(slot);
            if (item.IsAir || this.GetEquipmentDropChance(slot) <= 1f)
                continue;

            this.SetEquipment(slot, ItemStack.Air);
            level.EmitGameEvent(MobGameEvent.Unequip, this.Position, this);
            var drop = OminousItemSpawner.LooseItem(level, this.Position, item);
            drop.CanPickup = false;
            level.TrySpawnEntity(drop, hostOwned: true);
        }
    }

    // Vanilla spawn-data can carry equipment directly as well as through its equipment loot table. Obsidian's
    // own storage takes precedence on reload; these fields cover mobs loaded from structures and spawn data.
    private void ReadVanillaEquipment(NbtCompound tag)
    {
        if (tag.HasTag("ObsidianEquipment"))
            return;

        if (tag.TryGetTag<NbtCompound>("equipment", out var equipment))
        {
            foreach (var (slot, name) in SavedEquipmentSlots)
            {
                if (equipment.TryGetTag<NbtCompound>(name, out var stack) && stack.ItemFromNbt() is ItemStack item)
                    this.SetEquipment(slot, item);
            }
        }

        if (tag.TryGetTag<NbtCompound>("drop_chances", out var chances))
        {
            foreach (var (slot, name) in SavedEquipmentSlots)
            {
                if (chances.TryGetNumber<float>(name, out var chance))
                    this.SetEquipmentDropChance(slot, chance);
            }
        }
    }
}

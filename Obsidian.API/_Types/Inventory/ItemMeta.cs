namespace Obsidian.API;

public struct ItemMeta : IEquatable<ItemMeta>
{
    internal int CustomModelData { get; set; }

    public ChatMessage Name { get; internal set; }

    public int RepairAmount { get; internal set; }

    public int Durability { get; internal set; }

    public bool Unbreakable { get; internal set; }

    public IReadOnlyDictionary<EnchantmentType, Enchantment> Enchantments { get; internal set; }
    public IReadOnlyDictionary<EnchantmentType, Enchantment> StoredEnchantments { get; internal set; }

    public IReadOnlyList<string> CanDestroy { get; internal set; }

    public IReadOnlyList<ChatMessage> Lore { get; internal set; }

    public bool HasTags() => Name != null || Lore?.Count > 0 || Durability > 0 || Unbreakable || RepairAmount > 0;

    public bool Equals(ItemMeta other) =>
        (CustomModelData, Name, RepairAmount, Durability, Unbreakable, Enchantments, StoredEnchantments, CanDestroy, Lore) ==
        (other.CustomModelData, other.Name, other.RepairAmount, other.Durability, other.Unbreakable, other.Enchantments, other.StoredEnchantments, other.CanDestroy, other.Lore);

    public override bool Equals(object? obj) => obj is ItemMeta meta && Equals(meta);

    public static bool operator ==(ItemMeta left, ItemMeta right) => left.Equals(right);

    public static bool operator !=(ItemMeta left, ItemMeta right) => !(left == right);

    public override int GetHashCode() =>
        (CustomModelData, Name, RepairAmount, Durability, Unbreakable, Enchantments, StoredEnchantments, CanDestroy, Lore).GetHashCode();
}

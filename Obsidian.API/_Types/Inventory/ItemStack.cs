namespace Obsidian.API;

public class ItemStack : IEquatable<ItemStack>
{
    public readonly static ItemStack Air = new(Material.Air, 0);

    internal int Slot { get; set; }

    public int Count { get; internal set; }

    public ItemMeta ItemMeta { get; internal set; }

    public Material Type { get; }

    public bool IsAir => this.Type == Material.Air;

    public ItemStack(Material type, int count = 1, ItemMeta? meta = null)
    {
        this.Type = type;
        this.Count = count;

        if (meta.HasValue)
            this.ItemMeta = meta.Value;
    }

    public static ItemStack operator -(ItemStack item, int value)
    {
        if (item.Count <= 0)
            return Air;

        item.Count = Math.Max(0, item.Count - value);

        return item;
    }

    public static ItemStack operator +(ItemStack item, int value)
    {
        if (item.Count >= 64)//TODO use max stack size
            return item;

        item.Count = Math.Min(64, item.Count + value);

        return item;
    }

    public static bool operator ==(ItemStack? left, ItemStack? right)
    {
        if (ReferenceEquals(left, right))
            return true;

        if (left is null || right is null)
            return false;

        return left.Equals(right);
    }

    public static bool operator !=(ItemStack? left, ItemStack? right) => !(left == right);

    public bool Equals(ItemStack? other) => (this.Type, this.ItemMeta) == (other?.Type, other?.ItemMeta);

    public override bool Equals(object? obj) => obj is ItemStack itemStack && Equals(itemStack);

    public override int GetHashCode() =>
        (this.Count, this.ItemMeta).GetHashCode();
}

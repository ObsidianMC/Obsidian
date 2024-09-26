using Obsidian.API.Inventory;
using Obsidian.API.ItemComponents;
using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace Obsidian.API;

public sealed class ItemStack : IEquatable<ItemStack>, ISlot, IDictionary<ItemComponentType, ItemComponent>
{
    private readonly Dictionary<ItemComponentType, ItemComponent> components = [];

    public static readonly ItemStack Air = new(Material.Air, 0);

    public ushort Slot { get; set; }

    public int Count { get; set; }

    public Material Type { get; }

    public bool IsAir => this.Type == Material.Air;

    public ICollection<ItemComponentType> Keys => components.Keys;

    public ICollection<ItemComponent> Values => components.Values;

    public bool IsReadOnly => false;

    public ItemComponent this[ItemComponentType key] { get => this.components[key]; set => this.components[key] = value; }

    public ItemStack(Material type, short count = 1, IDictionary<ItemComponentType, ItemComponent>? components = null)
    {
        this.Type = type;
        this.Count = count;
        if (count > 0) { Present = true; }
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
        (this.Present, this.Count, this.ItemMeta).GetHashCode();
    public void Add(ItemComponentType key, ItemComponent value) => throw new NotImplementedException();
    public bool ContainsKey(ItemComponentType key) => throw new NotImplementedException();
    public bool Remove(ItemComponentType key) => throw new NotImplementedException();
    public bool TryGetValue(ItemComponentType key, [MaybeNullWhen(false)] out ItemComponent value) => throw new NotImplementedException();
    public void Add(KeyValuePair<ItemComponentType, ItemComponent> item) => throw new NotImplementedException();
    public void Clear() => throw new NotImplementedException();
    public bool Contains(KeyValuePair<ItemComponentType, ItemComponent> item) => throw new NotImplementedException();
    public void CopyTo(KeyValuePair<ItemComponentType, ItemComponent>[] array, int arrayIndex) => throw new NotImplementedException();
    public bool Remove(KeyValuePair<ItemComponentType, ItemComponent> item) => throw new NotImplementedException();
    public IEnumerator<KeyValuePair<ItemComponentType, ItemComponent>> GetEnumerator() => throw new NotImplementedException();
    IEnumerator IEnumerable.GetEnumerator() => throw new NotImplementedException();
}

using Obsidian.API.Inventory;
using Obsidian.API.ItemComponents;
using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace Obsidian.API;

public sealed class ItemStack : IEquatable<ItemStack>, IDictionary<ItemComponentType, IItemComponent>
{
    private static readonly int MaxComponents = Enum.GetValues<ItemComponentType>().Length;
    private readonly IDictionary<ItemComponentType, IItemComponent> components = new Dictionary<ItemComponentType, IItemComponent>(MaxComponents);

    public static readonly ItemStack Air = new(Material.Air, 0);

    public ChatMessage Name => this.GetName();

    public short Slot { get; set; }

    public int Count { get; set; }

    public int MaxStackSize => this.Get<MaxStackSizeItemComponent>(ItemComponentType.MaxStackSize).MaxStackSize;

    public Material Type { get; }

    public bool IsAir => this.Type == Material.Air;

    public ICollection<ItemComponentType> Keys => components.Keys;

    public ICollection<IItemComponent> Values => components.Values;

    public bool IsReadOnly => false;

    public IItemComponent this[ItemComponentType key] { get => this.components[key]; set => this.components[key] = value; }

    public ItemStack(Material type, short count = 1, IDictionary<ItemComponentType, IItemComponent>? components = null)
    {
        this.Type = type;
        this.Count = count;

        if (components != null)
            this.components = components;
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
        if (item.Count >= item.MaxStackSize)
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

    public bool Equals(ItemStack? other) => (this.Type, this.components) == (other?.Type, other?.components);

    public override bool Equals(object? obj) => obj is ItemStack itemStack && Equals(itemStack);

    public override int GetHashCode() => (this.Count, this.components).GetHashCode();

    public TItemComponent Get<TItemComponent>(ItemComponentType key) where TItemComponent : IItemComponent =>
        (TItemComponent)this[key];

    public void Add(ItemComponentType key, IItemComponent value) => this.components.Add(key, value);
    public bool ContainsKey(ItemComponentType key) => this.components.ContainsKey(key);
    public bool Remove(ItemComponentType key) => this.components.Remove(key);

    public bool TryGetValue<TItemComponent>(ItemComponentType key, [MaybeNullWhen(false)] out TItemComponent component) where TItemComponent : IItemComponent
    {
        if(this.TryGetValue(key, out IItemComponent? foundComponent))
        {
            component = (TItemComponent)foundComponent;
            return true;
        }

        component = default;

        return false;
    }

    public bool TryGetValue(ItemComponentType key, [MaybeNullWhen(false)] out IItemComponent value) => 
        this.components.TryGetValue(key, out value);
    public void Add(KeyValuePair<ItemComponentType, IItemComponent> item) => this.components.Add(item);
    public void Clear() => this.components.Clear();
    public bool Contains(KeyValuePair<ItemComponentType, IItemComponent> item) => this.components.Contains(item);
    public void CopyTo(KeyValuePair<ItemComponentType, IItemComponent>[] array, int arrayIndex) => this.components.CopyTo(array, arrayIndex);
    public bool Remove(KeyValuePair<ItemComponentType, IItemComponent> item) => this.components.Remove(item);
    public IEnumerator<KeyValuePair<ItemComponentType, IItemComponent>> GetEnumerator() => this.components.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();

    private ChatMessage GetName()
    {
        if (this.TryGetValue(ItemComponentType.CustomName, out var customName))
            return ((NamedItemComponent)customName).Name;

        return this.Get<NamedItemComponent>(ItemComponentType.ItemName).Name;
    }
}

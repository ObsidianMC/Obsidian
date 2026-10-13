using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Registries;
using Obsidian.Nbt;
using System.Diagnostics.CodeAnalysis;

namespace Obsidian.API.Inventory;

public sealed class ItemStack : DataComponentsStorage, IEquatable<ItemStack>
{
    public static readonly ItemStack Air = new(ItemsRegistry.Air, 0);

    // A change moves the stack's Version on, so that the container holding it sees it (see BaseContainer.Revision).
    public int Count
    {
        get;
        set
        {
            if (field == value)
                return;

            field = value;
            this.MarkChanged();
        }
    }

    public Item Holder { get; }

    // A marker component: the item is unbreakable when it has one.
    public bool Unbreakable => this.ContainsKey(DataComponentType.Unbreakable);
    public int MaxStackSize => this.GetComponent<SimpleDataComponent<int>>(DataComponentType.MaxStackSize)?.Value ?? this.Holder.MaxStackSize;
    public ChatMessage? CustomName => this.GetComponent<SimpleDataComponent<ChatMessage>>(DataComponentType.CustomName)?.Value;
    public ChatMessage? ItemName => this.GetComponent<SimpleDataComponent<ChatMessage>>(DataComponentType.ItemName)?.Value;

    public int Damage => this.GetComponent<SimpleDataComponent<int>>(DataComponentType.Damage)?.Value ?? 0;

    public int MaxDamage => this.RemoveComponents.Contains(DataComponentType.MaxDamage) ? 0 :
        this.GetComponent<SimpleDataComponent<int>>(DataComponentType.MaxDamage)?.Value ?? this.Holder.MaxDamage;
    public IReadOnlyList<ChatMessage> Lore => this.GetComponent<SimpleDataComponent<ImmutableArray<ChatMessage>>>(
        DataComponentType.Lore)?.Value ?? [];
    public IReadOnlyList<Enchantment> Enchantments =>
        this.GetComponent<SimpleDataComponent<ImmutableArray<Enchantment>>>(
            DataComponentType.Enchantments)?.Value ?? [];
    public bool HasEnchantmentGlint => this.GetComponent<SimpleDataComponent<bool>>(
        DataComponentType.EnchantmentGlintOverride)?.Value ??
        (!this.RemoveComponents.Contains(DataComponentType.EnchantmentGlintOverride) &&
            this.Holder.Id is ItemIds.EnchantedBook or ItemIds.EnchantedGoldenApple or ItemIds.ExperienceBottle
                or ItemIds.WrittenBook or ItemIds.NetherStar or ItemIds.EndCrystal or ItemIds.DebugStick ||
            this.Enchantments.Count > 0
            || this.Is(ItemsRegistry.Compass) && this.ContainsKey(DataComponentType.LodestoneTracker));

    public bool IsAir => this.Holder == ItemsRegistry.Air;

    /// <summary>
    /// Whether the stack holds <paramref name="item"/>.
    /// </summary>
    public bool Is(Item item) => this.Holder == item;

    /// <summary>
    /// Saved components Obsidian doesn't model (e.g. <c>minecraft:custom_data</c>), kept as they were loaded so saving the
    /// stack again doesn't drop them. Copies of the stack own an isolated compound.
    /// </summary>
    /// <remarks>
    /// They aren't sent to clients, but take part in <see cref="Equals(ItemStack?)"/>, so stacks that differ only in
    /// them don't merge.
    /// </remarks>
    internal NbtCompound? UnmodeledComponents { get; init; }

    public ItemStack(Item holder, int count = 1, params IEnumerable<DataComponent> components)
    {
        this.Holder = holder;
        this.Count = count;

        this.InitializeComponents(components);
    }

    public ItemStack([DisallowNull] ItemStack item, int count = 1) : this(item.Holder, count, item.Patch)
    {
        foreach (var type in item.RemoveComponents)
            this.Remove(type);

        this.UnmodeledComponents = ComponentCopies.Nbt(item.UnmodeledComponents);
    }

    /// <summary>
    /// Copies this stack, with its count and the components set on it, as a stack of <paramref name="holder"/> (vanilla's
    /// <c>transmuteCopy</c>), e.g. to turn a book into an enchanted book.
    /// </summary>
    public ItemStack TransmuteCopy(Item holder)
    {
        var copy = new ItemStack(holder, this.Count, this.Patch)
        {
            UnmodeledComponents = ComponentCopies.Nbt(this.UnmodeledComponents)
        };
        foreach (var type in this.RemoveComponents)
            copy.Remove(type);

        return copy;
    }

    public static ItemStack operator -(ItemStack item, int value)
    {
        if (item.Count <= 0)
            return Air;

        item.Count = Math.Max(0, item.Count - value);

        return item;
    }

    public static ItemStack operator /(ItemStack item, int value)
    {
        if (item.Count <= 0)
            return Air;

        item.Count = Math.Max(0, item.Count / value);

        Console.WriteLine($"New Count: {item.Count}");

        return item;
    }

    public static ItemStack operator +(ItemStack item, int value)
    {
        if (item.Count >= item.MaxStackSize)
            return item;

        item.Count = Math.Min(item.MaxStackSize, item.Count + value);

        return item;
    }

    public static ItemStack operator +(ItemStack item, ItemStack value)
    {
        if (item.Count >= item.MaxStackSize)
            return item;

        item.Count = Math.Min(item.MaxStackSize, item.Count + value.Count);

        return item;
    }

    public static bool operator ==(ItemStack? left, ItemStack? right) => ReferenceEquals(left, right) || (left is not null && left.Equals(right));

    public static bool operator !=(ItemStack? left, ItemStack? right) => !(left == right);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(this.Holder);
        foreach (var component in this.InternalStorage)
            hash.Add(component);

        hash.Add(new ComponentValueKey(this.UnmodeledComponents));
        return hash.ToHashCode();
    }

    private void InitializeComponents(params IEnumerable<DataComponent> components)
    {
        foreach (var defaultComponent in ComponentBuilder.DefaultItemComponents)
        {
            // The stack size placeholder is the item's own, so the server agrees with the client's default.
            var placeholder = defaultComponent.Type == DataComponentType.MaxStackSize
                ? ComponentBuilder.MaxStackSize with { Value = this.Holder.MaxStackSize }
                : defaultComponent;

            this.Add(placeholder);

            // Immutable defaults can be shared until a component is explicitly replaced.
            this.Placeholders[placeholder.Type] = placeholder;
        }

        foreach (var component in components)
            this[component.Type] = component;
    }

    public override string ToString() => $"{this.Holder.UnlocalizedName}";

    public override bool Equals(object obj) => Equals(obj as ItemStack);

    public bool Equals(ItemStack? other) => other is not null && this.Holder.Equals(other.Holder) &&
        this.InternalStorage.SequenceEqual(other.InternalStorage) &&
        UnmodeledEquals(this.UnmodeledComponents, other.UnmodeledComponents);

    private static bool UnmodeledEquals(NbtCompound? left, NbtCompound? right) =>
        ReferenceEquals(left, right) || (left is not null && right is not null && NbtEquals(left, right));

    // Structural equality of saved NBT. A compound's entries may come in any order.
    private static bool NbtEquals(INbtTag left, INbtTag right) => (left, right) switch
    {
        (NbtCompound a, NbtCompound b) => a.Count == b.Count
            && a.All(entry => b.TryGetTag(entry.Key, out var other) && NbtEquals(entry.Value, other)),
        (NbtList a, NbtList b) => a.Count == b.Count && a.Zip(b).All(pair => NbtEquals(pair.First, pair.Second)),
        (NbtArray<byte> a, NbtArray<byte> b) => a.GetArray().AsSpan().SequenceEqual(b.GetArray()),
        (NbtArray<int> a, NbtArray<int> b) => a.GetArray().AsSpan().SequenceEqual(b.GetArray()),
        (NbtArray<long> a, NbtArray<long> b) => a.GetArray().AsSpan().SequenceEqual(b.GetArray()),
        (NbtTag<byte> a, NbtTag<byte> b) => a.Value == b.Value,
        (NbtTag<bool> a, NbtTag<bool> b) => a.Value == b.Value,
        (NbtTag<short> a, NbtTag<short> b) => a.Value == b.Value,
        (NbtTag<int> a, NbtTag<int> b) => a.Value == b.Value,
        (NbtTag<long> a, NbtTag<long> b) => a.Value == b.Value,
        (NbtTag<float> a, NbtTag<float> b) => a.Value.Equals(b.Value),
        (NbtTag<double> a, NbtTag<double> b) => a.Value.Equals(b.Value),
        (NbtTag<string> a, NbtTag<string> b) => a.Value == b.Value,
        _ => false
    };
}

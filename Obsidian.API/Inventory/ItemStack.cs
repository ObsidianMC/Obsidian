using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Registries;
using Obsidian.Nbt;
using System.Diagnostics.CodeAnalysis;

namespace Obsidian.API.Inventory;

public sealed class ItemStack : DataComponentsStorage, IEquatable<ItemStack>
{
    public static readonly ItemStack Air = new(ItemsRegistry.Air, 0);

    public int Count { get; set; }

    public Item Holder { get; }

    public Material Type => this.Holder.Type;

    // A marker component: the item is unbreakable when it has one.
    public bool Unbreakable => this.ContainsKey(DataComponentType.Unbreakable);
    public int MaxStackSize => this.GetComponent<SimpleDataComponent<int>>(DataComponentType.MaxStackSize)?.Value ?? this.Holder.MaxStackSize;
    public ChatMessage? CustomName => this.GetComponent<SimpleDataComponent<ChatMessage>>(DataComponentType.CustomName)?.Value;
    public ChatMessage? ItemName => this.GetComponent<SimpleDataComponent<ChatMessage>>(DataComponentType.ItemName)?.Value;

    public int Damage => this.GetComponent<SimpleDataComponent<int>>(DataComponentType.Damage)?.Value ?? 0;

    public int MaxDamage => this.RemoveComponents.Contains(DataComponentType.MaxDamage) ? 0 :
        this.GetComponent<SimpleDataComponent<int>>(DataComponentType.MaxDamage)?.Value ?? this.Holder.MaxDamage;
    public IReadOnlyList<ChatMessage> Lore => this.GetComponent<SimpleDataComponent<ChatMessage[]>>(DataComponentType.Lore)?.Value ?? [];
    public IReadOnlyList<Enchantment> Enchantments =>
        this.GetComponent<SimpleDataComponent<Enchantment[]>>(DataComponentType.Enchantments)?.Value ?? [];
    public bool HasEnchantmentGlint => this.GetComponent<SimpleDataComponent<bool>>(DataComponentType.EnchantmentGlintOverride)?.Value ??
        (!this.RemoveComponents.Contains(DataComponentType.EnchantmentGlintOverride) &&
            this.Type is Material.EnchantedBook or Material.EnchantedGoldenApple or Material.ExperienceBottle or
                Material.WrittenBook or Material.NetherStar or Material.EndCrystal or Material.DebugStick ||
            this.Enchantments.Count > 0 || this.Type == Material.Compass && this.ContainsKey(DataComponentType.LodestoneTracker));

    public bool IsAir => Type == Material.Air;

    /// <summary>
    /// Saved components Obsidian doesn't model (e.g. <c>minecraft:custom_data</c>), kept as they were loaded so saving the
    /// stack again doesn't drop them. Copies of the stack share the compound, so it's never changed.
    /// </summary>
    /// <remarks>
    /// They aren't sent to clients and don't take part in <see cref="Equals(ItemStack?)"/>.
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

        this.UnmodeledComponents = item.UnmodeledComponents;
    }

    /// <summary>
    /// Copies this stack, with its count and the components set on it, as a stack of <paramref name="holder"/> (vanilla's
    /// <c>transmuteCopy</c>), e.g. to turn a book into an enchanted book.
    /// </summary>
    public ItemStack TransmuteCopy(Item holder)
    {
        var copy = new ItemStack(holder, this.Count, this.Patch) { UnmodeledComponents = this.UnmodeledComponents };
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

    public override int GetHashCode() => HashCode.Combine(this.Holder, this.InternalStorage);

    private void InitializeComponents(params IEnumerable<DataComponent> components)
    {
        foreach (var defaultComponent in ComponentBuilder.DefaultItemComponents)
        {
            // The stack size placeholder is the item's own, so the server agrees with the client's default.
            var placeholder = defaultComponent.Type == DataComponentType.MaxStackSize
                ? ComponentBuilder.MaxStackSize with { Value = this.Holder.MaxStackSize }
                : defaultComponent;

            this.Add(placeholder);

            // A copy, so changing the placeholder's value puts it in the patch.
            this.Placeholders[placeholder.Type] = placeholder with { };
        }

        foreach (var component in components)
            this[component.Type] = component;
    }

    public override string ToString() => $"{this.Holder.UnlocalizedName}";

    public override bool Equals(object obj) => Equals(obj as ItemStack);

    public bool Equals(ItemStack? other) => other is not null && this.Holder.Equals(other.Holder) &&
        this.InternalStorage.SequenceEqual(other.InternalStorage);
}

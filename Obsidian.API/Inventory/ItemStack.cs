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
    /// They take part in <see cref="Equals(ItemStack?)"/>. Once a plugin registers an entry's type, the canonical
    /// projection treats it like a modeled component, including for networking, without changing the saved stack.
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

    /// <summary>
    /// Reads the stack's value of a plugin's component type, decoded with the type's codec.
    /// </summary>
    /// <returns>Whether the stack has a value of the type.</returns>
    /// <remarks>
    /// A value saved before the type was first registered in this process is read too.
    /// </remarks>
    public bool TryGet<TValue>(PluginComponentType<TValue> type, [MaybeNullWhen(false)] out TValue value)
    {
        using var call = type.TryEnter();
        if (call is null)
        {
            value = default;
            return false;
        }

        INbtTag? saved = null;
        if (this.InternalStorage.GetValueOrDefault(type.Type) is PluginDataComponent component)
            saved = component.ToNbt();
        else if (this.UnmodeledComponents?.TryGetTag(type.Identifier, out var unmodeled) == true)
            saved = new PluginDataComponent(type.Type, type.Identifier, unmodeled).ToNbt();

        value = saved is not null ? type.Codec.Decode(saved) : default;
        return saved is not null;
    }

    /// <summary>
    /// The stack's value of a plugin's component type, or <c>default</c> when it has none.
    /// </summary>
    public TValue? Get<TValue>(PluginComponentType<TValue> type) => this.TryGet(type, out var value) ? value : default;

    /// <summary>
    /// Sets the stack's value of a plugin's component type, encoded with the type's codec. The stack keeps only the
    /// saved form, so it holds nothing of the plugin's.
    /// </summary>
    public void Set<TValue>(PluginComponentType<TValue> type, TValue value)
    {
        using var call = type.TryEnter()
            ?? throw new InvalidOperationException($"{type.Identifier}'s plugin is unloading.");

        this[type.Type] = new PluginDataComponent(type.Type, type.Identifier, type.Codec.Encode(value));
        this.UnmodeledComponents?.Remove(type.Identifier);
    }

    /// <summary>
    /// Removes the stack's value of a plugin's component type.
    /// </summary>
    /// <returns>Whether the stack had one.</returns>
    public bool Remove<TValue>(PluginComponentType<TValue> type)
    {
        var unmodeled = this.UnmodeledComponents?.Remove(type.Identifier) == true;

        return this.Remove(type.Type) || unmodeled;
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
        foreach (var component in this.ProjectComponents(this.InternalStorage.Values).OrderBy(value => value.Type))
            hash.Add(component);

        hash.Add(new ComponentValueKey(this.UnrecognizedComponents()));
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

        // A plugin item's own defaults, which replace the common ones of the same type (its attribute modifiers).
        foreach (var placeholder in this.Holder.DefaultComponents)
        {
            this.InternalStorage[placeholder.Type] = placeholder;
            this.Placeholders[placeholder.Type] = placeholder;
        }

        foreach (var component in components)
            this[component.Type] = component;
    }

    public override string ToString() => $"{this.Holder.UnlocalizedName}";

    public override bool Equals(object obj) => Equals(obj as ItemStack);

    public bool Equals(ItemStack? other) => other is not null && (ReferenceEquals(this, other) || this.Holder.Equals(other.Holder)
        && (this.HasNoComponents && other.HasNoComponents || this.ComponentsEqual(other)));

    // Without modeled or saved components there's nothing to compare, which is most stacks: players' equipment is
    // compared every tick.
    private bool HasNoComponents => this.InternalStorage.Count == 0 && this.UnmodeledComponents is null;

    private bool ComponentsEqual(ItemStack other) =>
        this.ProjectComponents(this.InternalStorage.Values).OrderBy(value => value.Type)
            .SequenceEqual(other.ProjectComponents(other.InternalStorage.Values).OrderBy(value => value.Type))
        && new ComponentValueKey(this.UnrecognizedComponents())
            .Equals(new ComponentValueKey(other.UnrecognizedComponents()));

    /// <summary>
    /// Projects components saved before a plugin's first registration without mutating the stack. Networking and
    /// equality share this interpretation; an explicit modeled value takes precedence over a saved entry.
    /// </summary>
    internal IEnumerable<DataComponent> ProjectComponents(IEnumerable<DataComponent> modeled)
    {
        foreach (var component in modeled)
            yield return component;

        foreach (var (identifier, tag) in this.UnmodeledComponents ?? [])
        {
            if (DataComponentTypesRegistry.TryGet(identifier, out var type)
                && !this.InternalStorage.ContainsKey(type.Type)
                && !this.RemoveComponents.Contains(type.Type))
                yield return new PluginDataComponent(type.Type, type.Identifier, tag);
        }
    }

    private NbtCompound? UnrecognizedComponents()
    {
        var remaining = ComponentCopies.Nbt(this.UnmodeledComponents);
        if (remaining is null)
            return null;

        foreach (var identifier in remaining.Select(entry => entry.Key).ToArray())
        {
            if (DataComponentTypesRegistry.TryGet(identifier, out _))
                remaining.Remove(identifier);
        }

        return remaining.Count == 0 ? null : remaining;
    }
}

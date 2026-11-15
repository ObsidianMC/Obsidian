using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Registries;
using Obsidian.Nbt;
using System.Diagnostics.CodeAnalysis;

namespace Obsidian.API.Inventory;

/// <summary>
/// A number of one item with its components: what an inventory slot holds.
/// </summary>
/// <remarks>
/// A stack is mutable and not thread-safe: its count and components change in place. Components themselves are
/// immutable; replace one through the indexer to change it, for example
/// <c>stack[DataComponentType.Damage] = ComponentBuilder.Damage with { Value = 3 }</c>. Two stacks are equal when they
/// hold the same item with the same components, whatever their counts.
/// </remarks>
public sealed class ItemStack : DataComponentsStorage, IEquatable<ItemStack>
{
    /// <summary>
    /// An empty stack of air with a count of 0, shared by every caller; don't change it.
    /// </summary>
    public static readonly ItemStack Air = new(ItemsRegistry.Air, 0);

    // A change moves the stack's Version on, so that the container holding it sees it (see BaseContainer.Revision).
    /// <summary>
    /// Gets or sets how many items the stack holds. Setting a new value moves the stack's change stamp on, so the
    /// container holding it sees the change.
    /// </summary>
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

    /// <summary>
    /// Gets the item the stack holds.
    /// </summary>
    public Item Holder { get; }

    // A marker component: the item is unbreakable when it has one.
    /// <summary>
    /// Gets whether the stack has a <c>minecraft:unbreakable</c> component, so it takes no damage.
    /// </summary>
    public bool Unbreakable => this.ContainsKey(DataComponentType.Unbreakable);
    /// <summary>
    /// Gets how many of the item fit in one stack: the stack's <c>minecraft:max_stack_size</c>, or its item's default.
    /// </summary>
    public int MaxStackSize => this.GetComponent<SimpleDataComponent<int>>(DataComponentType.MaxStackSize)?.Value ?? this.Holder.MaxStackSize;

    /// <summary>
    /// <see cref="MaxStackSize"/> read without its component's copy, which may be a plugin's delegate: for code holding
    /// an inventory's lock, where plugin code reading the same inventory would wait on that lock.
    /// </summary>
    internal int StoredMaxStackSize =>
        this.GetComponent<SimpleDataComponent<int>>(DataComponentType.MaxStackSize)?.StoredValue
        ?? this.Holder.MaxStackSize;

    /// <summary>
    /// Gets the stack's <c>minecraft:custom_name</c>, or null when it has none.
    /// </summary>
    public ChatMessage? CustomName => this.GetComponent<SimpleDataComponent<ChatMessage>>(DataComponentType.CustomName)?.Value;
    /// <summary>
    /// Gets the stack's <c>minecraft:item_name</c>, or null when it has none.
    /// </summary>
    public ChatMessage? ItemName => this.GetComponent<SimpleDataComponent<ChatMessage>>(DataComponentType.ItemName)?.Value;

    /// <summary>
    /// Gets how much damage the stack has taken (<c>minecraft:damage</c>), 0 when it has none.
    /// </summary>
    public int Damage => this.GetComponent<SimpleDataComponent<int>>(DataComponentType.Damage)?.Value ?? 0;

    /// <summary>
    /// Gets how much damage the stack takes before it breaks: its <c>minecraft:max_damage</c>, or its item's default,
    /// or 0 when the component was removed.
    /// </summary>
    public int MaxDamage => this.RemoveComponents.Contains(DataComponentType.MaxDamage) ? 0 :
        this.GetComponent<SimpleDataComponent<int>>(DataComponentType.MaxDamage)?.Value ?? this.Holder.MaxDamage;
    /// <summary>
    /// Gets the stack's extra tooltip lines (<c>minecraft:lore</c>), empty when it has none.
    /// </summary>
    public IReadOnlyList<ChatMessage> Lore => this.GetComponent<SimpleDataComponent<ImmutableArray<ChatMessage>>>(
        DataComponentType.Lore)?.Value ?? [];
    /// <summary>
    /// Gets the stack's enchantments (<c>minecraft:enchantments</c>), empty when it has none.
    /// </summary>
    public IReadOnlyList<Enchantment> Enchantments =>
        this.GetComponent<SimpleDataComponent<ImmutableArray<Enchantment>>>(
            DataComponentType.Enchantments)?.Value ?? [];
    /// <summary>
    /// Gets whether clients draw the stack with the enchantment glint: its <c>minecraft:enchantment_glint_override</c>
    /// when set, otherwise whether it's enchanted or an item that always glints, like an enchanted book or a lodestone
    /// compass.
    /// </summary>
    public bool HasEnchantmentGlint => this.GetComponent<SimpleDataComponent<bool>>(
        DataComponentType.EnchantmentGlintOverride)?.Value ??
        (!this.RemoveComponents.Contains(DataComponentType.EnchantmentGlintOverride) &&
            this.Holder.Id is ItemIds.EnchantedBook or ItemIds.EnchantedGoldenApple or ItemIds.ExperienceBottle
                or ItemIds.WrittenBook or ItemIds.NetherStar or ItemIds.EndCrystal or ItemIds.DebugStick ||
            this.Enchantments.Count > 0
            || this.Is(ItemsRegistry.Compass) && this.ContainsKey(DataComponentType.LodestoneTracker));

    /// <summary>
    /// Gets whether the stack holds air, the empty item.
    /// </summary>
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

    // The bundle slot the player selected (vanilla's selected item index); -1 when none. Not saved or compared.
    internal int BundleSelection { get; set; } = -1;

    /// <summary>
    /// Creates a stack of <paramref name="holder"/> with the common placeholder components (stack size, lore,
    /// enchantments, repair cost, attribute modifiers and rarity), then <see cref="Item.DefaultComponents"/>, then
    /// <paramref name="components"/>, each replacing earlier ones of the same type.
    /// </summary>
    /// <remarks>
    /// A vanilla item's <see cref="Item.DefaultComponents"/> is empty, so its other vanilla defaults aren't on the
    /// stack: a new stack of a vanilla food has no <c>minecraft:food</c> component for
    /// <see cref="DataComponentsStorage.GetComponent{TComponent}"/> to return.
    /// </remarks>
    public ItemStack(Item holder, int count = 1, params IEnumerable<DataComponent> components)
    {
        this.Holder = holder;
        this.Count = count;

        this.InitializeComponents(components);
    }

    /// <summary>
    /// Copies <paramref name="item"/>'s item and components, including removed defaults, into a new stack of
    /// <paramref name="count"/>, which is 1 by default rather than <paramref name="item"/>'s count.
    /// </summary>
    public ItemStack([DisallowNull] ItemStack item, int count = 1) : this(item.Holder, count, item.Patch)
    {
        foreach (var type in item.RemoveComponents)
            this.Remove(type);

        this.UnmodeledComponents = ComponentCopies.Nbt(item.UnmodeledComponents);
        this.BundleSelection = item.BundleSelection;
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

        if (saved is not null)
            ComponentExecutionGuard.Check($"persistent decoder ({type.Identifier})");

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

        ComponentExecutionGuard.Check($"persistent encoder ({type.Identifier})");
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

    /// <summary>
    /// Takes <paramref name="value"/> from the stack's count, not going below 0, and returns the same stack; returns
    /// <see cref="Air"/> instead when the stack was already empty.
    /// </summary>
    public static ItemStack operator -(ItemStack item, int value)
    {
        if (item.Count <= 0)
            return Air;

        item.Count = Math.Max(0, item.Count - value);

        return item;
    }

    /// <summary>
    /// Divides the stack's count by <paramref name="value"/>, rounding down, and returns the same stack; returns
    /// <see cref="Air"/> instead when the stack was already empty. Also prints the new count to the console.
    /// </summary>
    public static ItemStack operator /(ItemStack item, int value)
    {
        if (item.Count <= 0)
            return Air;

        item.Count = Math.Max(0, item.Count / value);

        Console.WriteLine($"New Count: {item.Count}");

        return item;
    }

    /// <summary>
    /// Adds <paramref name="value"/> to the stack's count, up to <see cref="MaxStackSize"/>, and returns the same
    /// stack.
    /// </summary>
    public static ItemStack operator +(ItemStack item, int value)
    {
        if (item.Count >= item.MaxStackSize)
            return item;

        item.Count = Math.Min(item.MaxStackSize, item.Count + value);

        return item;
    }

    /// <summary>
    /// Adds <paramref name="value"/>'s count to the stack's, up to <see cref="MaxStackSize"/>, and returns the same
    /// stack. The item and components aren't compared.
    /// </summary>
    public static ItemStack operator +(ItemStack item, ItemStack value)
    {
        if (item.Count >= item.MaxStackSize)
            return item;

        item.Count = Math.Min(item.MaxStackSize, item.Count + value.Count);

        return item;
    }

    /// <summary>
    /// Whether both stacks are null, the same stack, or equal (see <see cref="Equals(ItemStack?)"/>).
    /// </summary>
    public static bool operator ==(ItemStack? left, ItemStack? right) => ReferenceEquals(left, right) || (left is not null && left.Equals(right));

    /// <summary>
    /// Whether the stacks aren't equal (see <see cref="Equals(ItemStack?)"/>).
    /// </summary>
    public static bool operator !=(ItemStack? left, ItemStack? right) => !(left == right);

    /// <summary>
    /// Hashes the stack's item and components, not its count.
    /// </summary>
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

    /// <summary>
    /// Returns the identifier of the stack's item.
    /// </summary>
    public override string ToString() => $"{this.Holder.UnlocalizedName}";

    /// <summary>
    /// Whether <paramref name="obj"/> is an equal stack (see <see cref="Equals(ItemStack?)"/>).
    /// </summary>
    public override bool Equals(object obj) => Equals(obj as ItemStack);

    /// <summary>
    /// Whether <paramref name="other"/> holds the same item with the same components, including saved components
    /// Obsidian doesn't model. Counts aren't compared.
    /// </summary>
    public bool Equals(ItemStack? other) => other is not null && (ReferenceEquals(this, other) || this.Holder.Equals(other.Holder)
        && (this.HasNoComponents && other.HasNoComponents || this.ComponentsEqual(other)));

    // Without modeled or saved components there's nothing to compare, which is most stacks: players' equipment is
    // compared every tick.
    private bool HasNoComponents => this.InternalStorage.Count == 0 && this.UnmodeledComponents is null;

    private bool ComponentsEqual(ItemStack other)
    {
        if (this.UnmodeledComponents is null && other.UnmodeledComponents is null)
            return this.ModeledComponentsEqual(other);

        return this.ProjectedComponentsEqual(other);
    }

    // Without saved plugin components, the components are just the modeled ones, one per type: compared by type
    // without sorting or copying them, since equipment is compared every tick.
    private bool ModeledComponentsEqual(ItemStack other)
    {
        if (this.InternalStorage.Count != other.InternalStorage.Count)
            return false;

        foreach (var (type, component) in this.InternalStorage)
        {
            if (!other.InternalStorage.TryGetValue(type, out var otherComponent))
                return false;

            if (!component.Equals(otherComponent))
                return false;
        }

        return true;
    }

    /// <summary>Compares effective component maps, resolving item defaults before applying patches and removals.</summary>
    internal bool HasSameEffectiveComponents(ItemStack other, ItemStack defaults) =>
        this.Holder.Equals(other.Holder)
        && this.ResolveForComparison(defaults).Equals(other.ResolveForComparison(defaults));

    private ItemStack ResolveForComparison(ItemStack defaults)
    {
        var saved = ComponentCopies.Nbt(defaults.UnmodeledComponents) ?? new NbtCompound();
        foreach (var (name, value) in ComponentCopies.Nbt(this.UnmodeledComponents) ?? [])
        {
            saved.Remove(name);
            saved.Add(name, value);
        }

        foreach (var type in this.RemoveComponents)
            saved.Remove(OpaqueDataComponent.GetIdentifier(type));

        var resolved = new ItemStack(this) { UnmodeledComponents = saved.Count == 0 ? null : saved };
        resolved.InternalStorage.Clear();
        resolved.Placeholders.Clear();
        foreach (var component in defaults.Holder.DefaultComponents.Concat(defaults.Patch).Concat(this.Patch))
            resolved.InternalStorage[component.Type] = component;

        foreach (var type in this.RemoveComponents)
            resolved.InternalStorage.Remove(type);

        return resolved;
    }

    private bool ProjectedComponentsEqual(ItemStack other) =>
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
            {
                yield return new PluginDataComponent(type.Type, type.Identifier, tag);
            }
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

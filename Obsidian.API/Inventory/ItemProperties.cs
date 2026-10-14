using Obsidian.API.Inventory.DataComponents;

namespace Obsidian.API.Inventory;

/// <summary>
/// How a plugin's item behaves, given when registering it (see <see cref="Plugins.IPluginRegistry.RegisterItem"/>).
/// </summary>
/// <remarks>
/// <see cref="Tool"/>, <see cref="Food"/>, <see cref="Equippable"/> and <see cref="AttributeModifiers"/> become the
/// item's default components (see <see cref="Item.DefaultComponents"/>): every stack of the item has them unless the
/// stack sets or removes its own, like a vanilla item's. Build them from <see cref="ComponentBuilder"/> with a
/// <c>with</c> expression, for example <c>ComponentBuilder.Food with { Nutrition = 4, SaturationModifier = 0.3f }</c>.
/// </remarks>
public sealed class ItemProperties
{
    /// <summary>
    /// The item's default <c>minecraft:max_stack_size</c>, from 1 to 99.
    /// </summary>
    public int MaxStackSize { get; init; } = 64;

    /// <summary>
    /// The item's default <c>minecraft:max_damage</c>, or 0 when the item can't be damaged.
    /// </summary>
    public int MaxDamage { get; init; }

    /// <summary>
    /// The item's default <c>minecraft:enchantable</c> value, or 0 when it can't be enchanted.
    /// </summary>
    public int Enchantable { get; init; }

    /// <summary>
    /// The item's default <c>minecraft:tool</c>, or null when it isn't a tool: its rules give its mining speed and
    /// whether it's the correct tool for drops, by block tag or blocks, the first matching rule deciding; otherwise
    /// it mines at its default speed. Breaking a block that isn't broken instantly wears it by its damage per block.
    /// </summary>
    public ToolDataComponent? Tool { get; init; }

    /// <summary>
    /// The item's default <c>minecraft:food</c>, or null when it can't be eaten.
    /// </summary>
    public FoodDataComponent? Food { get; init; }

    /// <summary>
    /// How the item's <see cref="Food"/> is eaten (how long it takes, its animation and sound, the effects it gives),
    /// the item's default <c>minecraft:consumable</c>. Vanilla's food default
    /// (<see cref="ComponentBuilder.Consumable"/>) when null; ignored without <see cref="Food"/>.
    /// </summary>
    public ConsumableDataComponent? Consumable { get; init; }

    /// <summary>
    /// The item's default <c>minecraft:equippable</c>, or null: the slot it's worn in, which a player equips it to by
    /// using it, and its equip sound and model. Start from <see cref="ComponentBuilder.Equippable"/>, which has
    /// vanilla's defaults. Give armour its armour points with <see cref="AttributeModifiers"/>.
    /// </summary>
    public EquippableDataComponent? Equippable { get; init; }

    /// <summary>
    /// The item's default <c>minecraft:attribute_modifiers</c>: what it adds to its holder's attributes while it's in
    /// the modifier's slot, such as armour while worn or attack damage while held (see <see cref="AttributeIds"/>).
    /// </summary>
    public ImmutableArray<AttributeModifier> AttributeModifiers { get; init; } = [];

    /// <summary>
    /// What the item does when used.
    /// </summary>
    public IItemBehavior? Behavior { get; init; }

    /// <summary>
    /// The item's default components besides its stack size, durability and enchantability, which are kept as
    /// <see cref="Item"/>'s own properties.
    /// </summary>
    internal ImmutableArray<DataComponent> BuildDefaultComponents()
    {
        var components = ImmutableArray.CreateBuilder<DataComponent>();

        if (this.Tool is not null)
            components.Add(this.Tool);

        // Like vanilla's Item.Properties.food, food is consumable too, by default as vanilla's food is eaten.
        if (this.Food is not null)
        {
            components.Add(this.Food);
            components.Add(this.Consumable ?? ComponentBuilder.Consumable);
        }

        if (this.Equippable is not null)
            components.Add(this.Equippable);

        if (!this.AttributeModifiers.IsDefaultOrEmpty)
            components.Add(ComponentBuilder.AttributeModifiers with { Value = this.AttributeModifiers });

        return components.ToImmutable();
    }
}

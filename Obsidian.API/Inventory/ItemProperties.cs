namespace Obsidian.API.Inventory;

/// <summary>
/// How a plugin's item behaves, given when registering it (see <see cref="Plugins.IPluginRegistry.RegisterItem"/>).
/// </summary>
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
    /// What the item does when used.
    /// </summary>
    public IItemBehavior? Behavior { get; init; }
}

using Obsidian.API.Plugins;
using Obsidian.API.Registries;

namespace Obsidian.API.Inventory;

/// <summary>
/// A kind of item, like a diamond or a bow, which item stacks hold. Vanilla's items are the static properties of
/// <see cref="ItemsRegistry"/>; plugins register more with <see cref="IPluginRegistry.RegisterItem"/>.
/// </summary>
/// <remarks>
/// There is one instance per item, so items compare by reference. An item is plain data owned by the server: a plugin's
/// item keeps its instance, identifier and id when the plugin reloads, and only its <see cref="Behavior"/> is replaced.
/// </remarks>
public sealed class Item : INetworkSerializable<Item>
{
    internal Item(int id, string unlocalizedName, Material? type)
    {
        this.Id = (short)id;
        this.UnlocalizedName = unlocalizedName;
        this.Type = type;
    }

    /// <summary>
    /// The item's identifier, like <c>minecraft:diamond</c>.
    /// </summary>
    public string UnlocalizedName { get; }

    /// <summary>
    /// The vanilla material naming the item, or null for an item a plugin registered.
    /// </summary>
    public Material? Type { get; }

    /// <summary>
    /// The item's network id. Vanilla's come first, in vanilla's order; plugins' follow, in the order they were first
    /// registered.
    /// </summary>
    public short Id { get; }

    /// <summary>
    /// The item's default <c>minecraft:max_stack_size</c>.
    /// </summary>
    public int MaxStackSize { get; internal set; } = 64;

    /// <summary>
    /// The item's default <c>minecraft:max_damage</c>, or 0 when the item can't be damaged.
    /// </summary>
    public int MaxDamage { get; internal set; }

    /// <summary>
    /// The item's default <c>minecraft:enchantable</c> value (higher values give better enchantments), or 0 when the
    /// item can't be enchanted.
    /// </summary>
    public int Enchantable { get; internal set; }

    /// <summary>
    /// What the item does when used, given by the plugin that registered it, or null for vanilla items and while the
    /// plugin is unloaded.
    /// </summary>
    public IItemBehavior? Behavior => this.Binding?.Behavior;

    /// <summary>
    /// The plugin that registered the item and provides its behaviour, or null for vanilla items and while the plugin
    /// is unloaded.
    /// </summary>
    public IPluginContainer? Owner => this.Binding?.Owner;

    // The plugin and its behaviour, replaced together so that a use never pairs one plugin's behaviour with another.
    internal ItemBinding? Binding { get; set; }

    public static void Write(Item value, INetStreamWriter writer) => writer.WriteVarInt(value.Id);
    public static Item Read(INetStreamReader reader) => ItemsRegistry.Get(reader);

    public override string ToString() => $"{{{this.UnlocalizedName}}}";
}

/// <summary>
/// The plugin that registered an item, and what the item does.
/// </summary>
internal sealed record ItemBinding(IPluginContainer Owner, IItemBehavior? Behavior);

using Obsidian.API.Registries;
using System.Diagnostics.CodeAnalysis;

namespace Obsidian.API.Inventory;
public readonly record struct Item : INetworkSerializable<Item>
{
    public required string UnlocalizedName { get; init; }

    public required Material Type { get; init; }

    public required short Id { get; init; }

    /// <summary>
    /// The item's default <c>minecraft:max_stack_size</c>.
    /// </summary>
    public int MaxStackSize { get; init; } = 64;

    /// <summary>
    /// The item's default <c>minecraft:max_damage</c>, or 0 when the item can't be damaged.
    /// </summary>
    public int MaxDamage { get; init; }

    /// <summary>
    /// The item's default <c>minecraft:enchantable</c> value (higher values give better enchantments), or 0 when the
    /// item can't be enchanted.
    /// </summary>
    public int Enchantable { get; init; }

    [SetsRequiredMembers]
    public Item(int id, string unlocalizedName, Material type)
    {
        Id = (short)id;
        UnlocalizedName = unlocalizedName;
        Type = type;
    }

    [SetsRequiredMembers]
    public Item(Item item)
    {
        Id = item.Id;
        UnlocalizedName = item.UnlocalizedName;
        Type = item.Type;
        MaxStackSize = item.MaxStackSize;
        MaxDamage = item.MaxDamage;
        Enchantable = item.Enchantable;
    }

    public static void Write(Item value, INetStreamWriter writer) => writer.WriteVarInt(value.Id);
    public static Item Read(INetStreamReader reader) => ItemsRegistry.Get(reader);

    public override string ToString() => $"{{{this.UnlocalizedName}}}";
}

namespace Obsidian.API.Inventory.DataComponents;
public static class ComponentBuilder
{
    public static SimpleDataComponent<int> MaxStackSize => BuildSimpleComponent(DataComponentType.MaxStackSize, "minecraft:max_stack_size",
        (writer, value) => writer.WriteVarInt(value),
        (reader) => reader.ReadVarInt());

    public static SimpleDataComponent<int> MaxDamage => BuildSimpleComponent(DataComponentType.MaxDamage, "minecraft:max_damage",
        (writer, value) => writer.WriteVarInt(value),
        (reader) => reader.ReadVarInt());

    public static SimpleDataComponent<int> Damage => BuildSimpleComponent(DataComponentType.Damage, "minecraft:damage",
        (writer, value) => writer.WriteVarInt(value),
        (reader) => reader.ReadVarInt());

    public static SimpleDataComponent<bool> Unbreakable => BuildSimpleComponent(DataComponentType.Unbreakable, "minecraft:unbreakable",
        (writer, value) => writer.WriteBoolean(value),
        (reader) => reader.ReadBoolean());

    public static SimpleDataComponent<ChatMessage> CustomName => BuildSimpleComponent(DataComponentType.CustomName, "minecraft:custom_name",
        (writer, value) => writer.WriteChat(value),
        (reader) => reader.ReadChat());

    public static SimpleDataComponent<ChatMessage> ItemName => BuildSimpleComponent(DataComponentType.ItemName, "minecraft:item_name",
        (writer, value) => writer.WriteChat(value),
        (reader) => reader.ReadChat());

    public static SimpleDataComponent<int> ItemModel => BuildSimpleComponent(DataComponentType.ItemModel, "minecraft:custom_model_data",
        (writer, value) => writer.WriteVarInt(value),
        (reader) => reader.ReadVarInt());

    public static SimpleDataComponent<List<ChatMessage>> Lore => BuildSimpleComponent(DataComponentType.Lore, "minecraft:lore",
        (writer, values) => writer.WriteLengthPrefixedArray(writer.WriteChat, values),
        (reader) => reader.ReadLengthPrefixedArray(reader.ReadChat));

    public static SimpleDataComponent<ItemRarity> Rarity => BuildSimpleComponent(DataComponentType.Rarity, "minecraft:rarity",
        (writer, value) => writer.WriteVarInt(value),
        (reader) => reader.ReadVarInt<ItemRarity>());

    public static SimpleDataComponent<ChatMessage> CustomModelData => BuildSimpleComponent(DataComponentType.CustomModelData, "minecraft:item_name",
        (writer, value) => writer.WriteChat(value),
        (reader) => reader.ReadChat());

    public static SimpleDataComponent HideAdditionalTooltip => new(DataComponentType.HideAdditionalTooltip, "minecraft:hide_additional_tooltip");

    public static SimpleDataComponent HideTooltip => new(DataComponentType.HideTooltip, "minecraft:hide_tooltip");

    /// <summary>
    /// Accumulated anvil usage cost. The client displays "Too Expensive" if the value is greater than 40 and the player is not in creative mode 
    /// (more specifically, if they don't have the insta-build flag enabled).
    /// This behavior can be overridden by setting the level with the Set Container Property packet.
    /// </summary>
    public static SimpleDataComponent<int> RepairCost => BuildSimpleComponent(DataComponentType.RepairCost, "minecraft:repair_cost",
        (writer, value) => writer.WriteVarInt(value),
        (reader) => reader.ReadVarInt());


    /// <summary>
    /// Marks the item as non-interactive on the creative inventory (the first 5 rows of items).
    /// This is used internally by the client on the paper icon in the saved hot-bars tab.
    /// </summary>
    public static SimpleDataComponent CreativeSlotLock => new(DataComponentType.CreativeSlotLock, "minecraft:creative_slot_lock");

    public static SimpleDataComponent<int> EnchantmentGlintOverride => BuildSimpleComponent(DataComponentType.EnchantmentGlintOverride, "minecraft:enchantment_glint_override",
        (writer, value) => writer.WriteVarInt(value),
        (reader) => reader.ReadVarInt());

    /// <summary>
    /// Marks the projectile as intangible (cannot be picked-up).
    /// </summary>
    public static SimpleDataComponent IntangibleProjectile => new(DataComponentType.IntangibleProjectile, "minecraft:intangible_projectile");

    /// <summary>
    /// Marks this item as damage resistant.
    /// The client won't render the item as being on-fire if this component is present.
    /// </summary>
    public static SimpleDataComponent DamageResistant => new(DataComponentType.DamageResistant, "minecraft:damage_resistant");

    public static SimpleDataComponent<TValue> BuildSimpleComponent<TValue>(DataComponentType type, string identifier,
        Action<INetStreamWriter, TValue> writer,
        Func<INetStreamReader, TValue> reader) => new(type, identifier, writer, reader);
}

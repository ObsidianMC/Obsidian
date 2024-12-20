using Obsidian.API.Effects;

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

    public static SimpleDataComponent<string> ItemModel => BuildSimpleComponent(DataComponentType.ItemModel, "minecraft:custom_model_data",
        (writer, value) => writer.WriteString(value),
        (reader) => reader.ReadString());

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

    public static SimpleDataComponent<ItemStack?> UseRemainder => BuildSimpleComponent(DataComponentType.UseRemainder, "minecraft:use_remainder",
        (writer, value) => writer.WriteItemStack(value),
        reader => reader.ReadItemStack());

    /// <summary>
    /// Marks this item as damage resistant.
    /// The client won't render the item as being on-fire if this component is present.
    /// </summary>
    public static SimpleDataComponent DamageResistant => new(DataComponentType.DamageResistant, "minecraft:damage_resistant");

    public static SimpleDataComponent<int> Enchantable => BuildSimpleComponent(DataComponentType.Enchantable, "minecraft:enchantable",
        (writer, value) => writer.WriteVarInt(value),
        reader => reader.ReadVarInt());

    public static SimpleDataComponent<List<string>> Repairable => BuildSimpleComponent(DataComponentType.Repairable, "minecraft:repairable",
        (writer, values) => writer.WriteLengthPrefixedArray((value) => writer.WriteString(value), values),
        reader => reader.ReadLengthPrefixedArray(() => reader.ReadString()));

    // Don't know what this is for
    public static SimpleDataComponent Glider => new(DataComponentType.Glider, "minecraft:glider");

    public static SimpleDataComponent<string> TooltipStyle => BuildSimpleComponent(DataComponentType.TooltipStyle, "minecraft:tooltip_style",
      (writer, value) => writer.WriteString(value),
      reader => reader.ReadString());

    public static SimpleDataComponent<List<IConsumeEffect>> DeathProtection => BuildSimpleComponent(DataComponentType.DeathProtection, "minecraft:death_protection",
      (writer, values) =>
      {
          writer.WriteLengthPrefixedArray((value) =>
          {
              writer.WriteString(value.Type);
              value.Write(writer);
          }, values);
      },
      reader => reader.ReadLengthPrefixedArray(() =>
      {
          var type = reader.ReadString();

          var effect = ConsumeEffects.Compile(type);

          return effect;
      }));

    public static TooltipSimpleDataComponent<List<Enchantment>> ShowInTooltip => BuildTooltipSimpleDataComponent(DataComponentType.StoredEnchantments, "minecraft:stored_enchantments",
        (writer, values) => writer.WriteLengthPrefixedArray(writer.WriteEnchantment, values),
        reader => reader.ReadLengthPrefixedArray(reader.ReadEnchantment));

    public static TooltipSimpleDataComponent<List<AttributeModifier>> AttributeModifiers => BuildTooltipSimpleDataComponent(DataComponentType.AttributeModifiers, "minecraft:attribute_modifiers",
       (writer, values) => writer.WriteLengthPrefixedArray(writer.WriteAttributeModifier, values),
       reader => reader.ReadLengthPrefixedArray(reader.ReadAttributeModifier));

    public static TooltipSimpleDataComponent<int> DyedColor => BuildTooltipSimpleDataComponent(DataComponentType.DyedColor, "minecraft:dyed_color",
       (writer, value) => writer.WriteInt(value),
       reader => reader.ReadInt());

    public static SimpleDataComponent<int> MapColor => BuildTooltipSimpleDataComponent(DataComponentType.MapColor, "minecraft:map_color",
       (writer, value) => writer.WriteInt(value),
       reader => reader.ReadInt());

    public static SimpleDataComponent<int> MapId => BuildTooltipSimpleDataComponent(DataComponentType.MapId, "minecraft:map_id",
       (writer, value) => writer.WriteVarInt(value),
       reader => reader.ReadVarInt());

    public static SimpleDataComponent<MapPostProcessingType> MapPostProcessing => BuildTooltipSimpleDataComponent(DataComponentType.MapPostProcessing, "minecraft:map_post_processing",
       (writer, value) => writer.WriteVarInt(value),
       reader => reader.ReadVarInt<MapPostProcessingType>());

    /// <summary>
    /// Marks the projectile as intangible (cannot be picked-up).
    /// </summary>
    public static SimpleDataComponent IntangibleProjectile => new(DataComponentType.IntangibleProjectile, "minecraft:intangible_projectile");

    public static SimpleDataComponent<TValue> BuildSimpleComponent<TValue>(DataComponentType type, string identifier,
        Action<INetStreamWriter, TValue> writer,
        Func<INetStreamReader, TValue> reader) => new(type, identifier, writer, reader);

    public static TooltipSimpleDataComponent<TValue> BuildTooltipSimpleDataComponent<TValue>(DataComponentType type, string identifier,
        Action<INetStreamWriter, TValue> writer,
        Func<INetStreamReader, TValue> reader) => new(type, identifier, writer, reader);
}

public enum MapPostProcessingType
{
    Lock,
    Scale
}

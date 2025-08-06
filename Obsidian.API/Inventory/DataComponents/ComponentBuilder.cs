using Obsidian.API.Effects;

namespace Obsidian.API.Inventory.DataComponents;
public static partial class ComponentBuilder
{
    public static SimpleDataComponent CustomData => new(DataComponentType.CustomData, "minecraft:custom_data");

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

    public static SimpleDataComponent<ChatMessage[]> Lore => BuildSimpleComponent(DataComponentType.Lore, "minecraft:lore",
        (writer, values) => writer.WriteLengthPrefixedArray(writer.WriteChat, values),
        (reader) => reader.ReadLengthPrefixedArray(reader.ReadChat));

    public static SimpleDataComponent<ItemRarity> Rarity => BuildSimpleComponent(DataComponentType.Rarity, "minecraft:rarity",
        (writer, value) => writer.WriteVarInt(value),
        (reader) => reader.ReadVarInt<ItemRarity>());

    public static SimpleDataComponent<Enchantment[]> Enchantments => BuildSimpleComponent(DataComponentType.Enchantments,
        "minecraft:enchantments",
        (writer, values) => writer.WriteLengthPrefixedArray(writer.WriteEnchantment, values),
        (reader) => reader.ReadLengthPrefixedArray(reader.ReadEnchantment));

    public static SimpleDataComponent<ChatMessage> CustomModelData => BuildSimpleComponent(DataComponentType.CustomModelData, "minecraft:item_name",
        (writer, value) => writer.WriteChat(value),
        (reader) => reader.ReadChat());

    //public static SimpleDataComponent HideAdditionalTooltip => new(DataComponentType.HideAdditionalTooltip, "minecraft:hide_additional_tooltip");

    //public static SimpleDataComponent HideTooltip => new(DataComponentType.HideTooltip, "minecraft:hide_tooltip");

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

    public static SimpleDataComponent<bool> EnchantmentGlintOverride => BuildSimpleComponent(DataComponentType.EnchantmentGlintOverride, "minecraft:enchantment_glint_override",
        (writer, value) => writer.WriteBoolean(value),
        (reader) => reader.ReadBoolean());

    public static SimpleDataComponent IntangibleProjectile => new(DataComponentType.IntangibleProjectile, "minecraft:intangible_projectile");

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

    public static SimpleDataComponent<string[]> Repairable => BuildSimpleComponent(DataComponentType.Repairable, "minecraft:repairable",
        (writer, values) => writer.WriteLengthPrefixedArray((value) => writer.WriteString(value), values),
        reader => reader.ReadLengthPrefixedArray(() => reader.ReadString()));

    // Don't know what this is for
    public static SimpleDataComponent Glider => new(DataComponentType.Glider, "minecraft:glider");

    public static SimpleDataComponent<string> TooltipStyle => BuildSimpleComponent(DataComponentType.TooltipStyle, "minecraft:tooltip_style",
      (writer, value) => writer.WriteString(value),
      reader => reader.ReadString());

    public static SimpleDataComponent<IConsumeEffect[]> DeathProtection => BuildSimpleComponent(DataComponentType.DeathProtection, "minecraft:death_protection",
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

    public static TooltipSimpleDataComponent<Enchantment[]> StoredEnchantments => BuildTooltipSimpleDataComponent(DataComponentType.StoredEnchantments, "minecraft:stored_enchantments",
        (writer, values) => writer.WriteLengthPrefixedArray(writer.WriteEnchantment, values),
        reader => reader.ReadLengthPrefixedArray(reader.ReadEnchantment));

    public static SimpleDataComponent<AttributeModifier[]> AttributeModifiers => BuildSimpleComponent(DataComponentType.AttributeModifiers, "minecraft:attribute_modifiers",
       (writer, values) => writer.WriteLengthPrefixedArray(writer.WriteAttributeModifier, values),
       reader => reader.ReadLengthPrefixedArray(reader.ReadAttributeModifier));

    public static SimpleDataComponent<int> MapColor => BuildSimpleComponent(DataComponentType.MapColor, "minecraft:map_color",
       (writer, value) => writer.WriteInt(value),
       reader => reader.ReadInt());

    public static SimpleDataComponent<int> MapId => BuildSimpleComponent(DataComponentType.MapId, "minecraft:map_id",
       (writer, value) => writer.WriteVarInt(value),
       reader => reader.ReadVarInt());

    public static SimpleDataComponent<MapPostProcessingType> MapPostProcessing => BuildSimpleComponent(DataComponentType.MapPostProcessing, "minecraft:map_post_processing",
       (writer, value) => writer.WriteVarInt(value),
       reader => reader.ReadVarInt<MapPostProcessingType>());

    public static SimpleDataComponent<ItemStack[]> ChargedProjectiles => BuildSimpleComponent(DataComponentType.ChargedProjectiles, "minecraft:charged_projectiles",
        (writer, values) => writer.WriteLengthPrefixedArray(item => writer.WriteItemStack(item), values),
        reader => reader.ReadLengthPrefixedArray(() => reader.ReadItemStack()));

    public static SimpleDataComponent<ItemStack[]> BundleContents => BuildSimpleComponent(DataComponentType.BundleContents, "minecraft:bundle_contents",
        (writer, values) => writer.WriteLengthPrefixedArray(item => writer.WriteItemStack(item), values),
        reader => reader.ReadLengthPrefixedArray(() => reader.ReadItemStack()));

    public static SimpleDataComponent<SuspiciousStewEffect[]> SuspiciousStewEffects => BuildSimpleComponent(DataComponentType.SuspiciousStewEffects,
        "minecraft:suspicious_stew_effects",
        (writer, values) => writer.WriteLengthPrefixedArray(value => SuspiciousStewEffect.Write(value, writer), values),
        reader => reader.ReadLengthPrefixedArray(() => SuspiciousStewEffect.Read(reader)));

    public static SimpleDataComponent<Page[]> WritableBookContent => BuildSimpleComponent(DataComponentType.WritableBookContent, "minecraft:writable_book_content",
        (writer, values) => writer.WriteLengthPrefixedArray((value) => Page.Write(value, writer), values),
        reader => reader.ReadLengthPrefixedArray(() => Page.Read(reader)));

    public static SimpleDataComponent<Page[]> WrittenBookContent => BuildSimpleComponent(DataComponentType.WrittenBookContent, "minecraft:written_book_content",
    (writer, values) => writer.WriteLengthPrefixedArray((value) => Page.Write(value, writer), values),
    reader => reader.ReadLengthPrefixedArray(() => Page.Read(reader)));

    //TODO WE NEED NBT ACCESS IN API
    public static SimpleDataComponent DebugStickState => new(DataComponentType.DebugStickState, "minecraft:debug_stick_state");
    public static SimpleDataComponent EntityData => new(DataComponentType.EntityData, "minecraft:entity_data");
    public static SimpleDataComponent BucketEntityData => new(DataComponentType.BucketEntityData, "minecraft:bucket_entity_data");
    public static SimpleDataComponent BlockEntityData => new(DataComponentType.BlockEntityData, "minecraft:block_entity_data");

    public static SimpleDataComponent<InstrumentData> Instrument => BuildSimpleComponent(DataComponentType.Instrument, "minecraft:instrument",
        (writer, value) => InstrumentData.Write(value, writer),
        InstrumentData.Read);

    public static SimpleDataComponent<int> OminousBottleAmplifier => BuildSimpleComponent(DataComponentType.OminousBottleAmplifier, "minecraft:ominous_bottle_amplifier",
        (writer, value) => writer.WriteVarInt(value),
        reader => reader.ReadVarInt());

    //NBT
    public static SimpleDataComponent Recipes => new(DataComponentType.Recipes, "minecraft:recipes");

    public static SimpleDataComponent<FireworkExplosion> FireworkExplosion => BuildSimpleComponent(DataComponentType.FireworkExplosion, "minecraft:firework_explosion",
        (writer, value) => API.FireworkExplosion.Write(value, writer),
        API.FireworkExplosion.Read);

    public static SimpleDataComponent<string> NoteBlockSound => BuildSimpleComponent(DataComponentType.NoteBlockSound, "minecraft:note_block_sound",
        (writer, value) => writer.WriteString(value),
        reader => reader.ReadString());

    public static SimpleDataComponent<BannerPatternLayer[]> BannerPatterns => BuildSimpleComponent(DataComponentType.BannerPatterns,
        "minecraft:banner_patterns",
        (writer, values) => writer.WriteLengthPrefixedArray((value) => BannerPatternLayer.Write(value, writer), values),
        reader => reader.ReadLengthPrefixedArray(() => BannerPatternLayer.Read(reader)));

    public static SimpleDataComponent<Dye> BaseColor => BuildSimpleComponent(DataComponentType.BaseColor, "minecraft:base_color",
        (writer, value) => writer.WriteVarInt(value),
        reader => reader.ReadVarInt<Dye>());

    public static SimpleDataComponent<Dye> DyedColor => BuildSimpleComponent(DataComponentType.DyedColor, "minecraft:dye_color",
        (writer, value) => writer.WriteVarInt(value),
        reader => reader.ReadVarInt<Dye>());

    public static SimpleDataComponent<Item[]> PotDecorations => BuildSimpleComponent(DataComponentType.PotDecorations,
        "minecraft:pot_decorations",
        (writer, values) => writer.WriteLengthPrefixedArray((value) => Item.Write(value, writer), values),
        reader => reader.ReadLengthPrefixedArray(() => Item.Read(reader)));

    public static SimpleDataComponent<ItemStack[]> Container => BuildSimpleComponent(DataComponentType.Container,
         "minecraft:container",
         (writer, values) => writer.WriteLengthPrefixedArray((value) => writer.WriteItemStack(value), values),
         reader => reader.ReadLengthPrefixedArray(() => reader.ReadItemStack()));

    public static SimpleDataComponent<BlockStateProperty[]> BlockState => BuildSimpleComponent(DataComponentType.BlockState,
        "minecraft:block_state",
        (writer, values) => writer.WriteLengthPrefixedArray((value) => BlockStateProperty.Write(value, writer), values),
        reader => reader.ReadLengthPrefixedArray(() => BlockStateProperty.Read(reader)));

    //REQUIRES NBT
    public static SimpleDataComponent Bees => new(DataComponentType.Bees, "minecraft:bees");

    public static SimpleDataComponent Lock => new(DataComponentType.Lock, "minecraft:lock");

    //MORE NBT
    public static SimpleDataComponent ContainerLoot => new(DataComponentType.ContainerLoot, "minecraft:container_loot");

    public static List<DataComponent> DefaultItemComponents =>
    [
        MaxStackSize with { Value = 64 },
        Lore with { Value = [] },
        Enchantments with { Value = [] },
        RepairCost with { Value = 0 },
        AttributeModifiers with { Value = [] },
        Rarity with { Value = ItemRarity.Common },
    ];

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

using Obsidian.API.Effects;

namespace Obsidian.API.Inventory.DataComponents;
public static partial class ComponentBuilder
{
    public static SimpleDataComponent<Obsidian.Nbt.NbtCompound> CustomData => NbtComponent(DataComponentType.CustomData, "minecraft:custom_data");

    public static SimpleDataComponent<int> MaxStackSize => BuildSimpleComponent(DataComponentType.MaxStackSize, "minecraft:max_stack_size",
        (writer, value) => writer.WriteVarInt(value),
        (reader) => reader.ReadVarInt());

    public static SimpleDataComponent<int> MaxDamage => BuildSimpleComponent(DataComponentType.MaxDamage, "minecraft:max_damage",
        (writer, value) => writer.WriteVarInt(value),
        (reader) => reader.ReadVarInt());

    public static SimpleDataComponent<int> Damage => BuildSimpleComponent(DataComponentType.Damage, "minecraft:damage",
        (writer, value) => writer.WriteVarInt(value),
        (reader) => reader.ReadVarInt());

    // A marker: vanilla sends no data, the component being present is what makes the item unbreakable.
    public static SimpleDataComponent<bool> Unbreakable => BuildSimpleComponent(DataComponentType.Unbreakable, "minecraft:unbreakable",
        (writer, value) => { },
        (reader) => true);

    public static SimpleDataComponent<ChatMessage> CustomName => BuildSimpleComponent(DataComponentType.CustomName, "minecraft:custom_name",
        (writer, value) => writer.WriteChat(value),
        (reader) => reader.ReadChat());

    public static SimpleDataComponent<ChatMessage> ItemName => BuildSimpleComponent(DataComponentType.ItemName, "minecraft:item_name",
        (writer, value) => writer.WriteChat(value),
        (reader) => reader.ReadChat());

    public static SimpleDataComponent<string> ItemModel => BuildSimpleComponent(DataComponentType.ItemModel, "minecraft:item_model",
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

    public static CustomModelDataComponent CustomModelData => new();

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

    public static SimpleDataComponent<Obsidian.Nbt.NbtCompound> IntangibleProjectile => NbtComponent(DataComponentType.IntangibleProjectile, "minecraft:intangible_projectile");

    public static SimpleDataComponent<ItemStack?> UseRemainder => BuildSimpleComponent(DataComponentType.UseRemainder, "minecraft:use_remainder",
        (writer, value) => writer.WriteRequiredItemStack(value!),
        reader => reader.ReadRequiredItemStack());

    /// <summary>
    /// Marks this item as damage resistant.
    /// The client won't render the item as being on-fire if this component is present.
    /// </summary>
    public static SimpleDataComponent<string> DamageResistant => BuildSimpleComponent(DataComponentType.DamageResistant, "minecraft:damage_resistant",
        (writer, value) => writer.WriteString(value), reader => reader.ReadString());

    public static SimpleDataComponent<int> Enchantable => BuildSimpleComponent(DataComponentType.Enchantable, "minecraft:enchantable",
        (writer, value) => writer.WriteVarInt(value),
        reader => reader.ReadVarInt());

    public static SimpleDataComponent<IdSet> Repairable => BuildSimpleComponent(DataComponentType.Repairable, "minecraft:repairable",
        (writer, value) => IdSet.Write(value, writer), IdSet.Read);

    // Don't know what this is for
    public static SimpleDataComponent Glider => new(DataComponentType.Glider, "minecraft:glider");

    public static SimpleDataComponent<string> TooltipStyle => BuildSimpleComponent(DataComponentType.TooltipStyle, "minecraft:tooltip_style",
      (writer, value) => writer.WriteString(value),
      reader => reader.ReadString());

    public static SimpleDataComponent<IConsumeEffect[]> DeathProtection => BuildSimpleComponent(DataComponentType.DeathProtection, "minecraft:death_protection",
        (writer, values) => writer.WriteLengthPrefixedArray(value => ComponentConsumeEffect.WriteValue(value, writer), values),
        reader => reader.ReadLengthPrefixedArray<IConsumeEffect>(() => ComponentConsumeEffect.ReadValue(reader)));

    public static SimpleDataComponent<Enchantment[]> StoredEnchantments => BuildSimpleComponent(DataComponentType.StoredEnchantments, "minecraft:stored_enchantments",
        (writer, values) => writer.WriteLengthPrefixedArray(writer.WriteEnchantment, values),
        reader => reader.ReadLengthPrefixedArray(reader.ReadEnchantment));

    public static AttributeModifiersDataComponent AttributeModifiers => new();

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
        (writer, values) => writer.WriteLengthPrefixedArray(item => writer.WriteRequiredItemStack(item), values),
        reader => reader.ReadLengthPrefixedArray(() => reader.ReadRequiredItemStack()));

    public static SimpleDataComponent<ItemStack[]> BundleContents => BuildSimpleComponent(DataComponentType.BundleContents, "minecraft:bundle_contents",
        (writer, values) => writer.WriteLengthPrefixedArray(item => writer.WriteRequiredItemStack(item), values),
        reader => reader.ReadLengthPrefixedArray(() => reader.ReadRequiredItemStack()));

    public static SimpleDataComponent<SuspiciousStewEffect[]> SuspiciousStewEffects => BuildSimpleComponent(DataComponentType.SuspiciousStewEffects,
        "minecraft:suspicious_stew_effects",
        (writer, values) => writer.WriteLengthPrefixedArray(value => SuspiciousStewEffect.Write(value, writer), values),
        reader => reader.ReadLengthPrefixedArray(() => SuspiciousStewEffect.Read(reader)));

    public static SimpleDataComponent<Page[]> WritableBookContent => BuildSimpleComponent(DataComponentType.WritableBookContent, "minecraft:writable_book_content",
        (writer, values) => writer.WriteLengthPrefixedArray((value) => Page.Write(value, writer), values),
        reader => reader.ReadLengthPrefixedArray(() => Page.Read(reader)));

    public static WrittenBookDataComponent WrittenBookContent => new();

    public static SimpleDataComponent<Obsidian.Nbt.NbtCompound> DebugStickState => NbtComponent(DataComponentType.DebugStickState, "minecraft:debug_stick_state");
    public static EntityDataComponent EntityData => new(DataComponentType.EntityData);
    public static SimpleDataComponent<Obsidian.Nbt.NbtCompound> BucketEntityData => NbtComponent(DataComponentType.BucketEntityData, "minecraft:bucket_entity_data");
    public static EntityDataComponent BlockEntityData => new(DataComponentType.BlockEntityData);

    public static InstrumentDataComponent Instrument => new();

    public static SimpleDataComponent<int> OminousBottleAmplifier => BuildSimpleComponent(DataComponentType.OminousBottleAmplifier, "minecraft:ominous_bottle_amplifier",
        (writer, value) => writer.WriteVarInt(value),
        reader => reader.ReadVarInt());

    //NBT
    public static SimpleDataComponent<string[]> Recipes => BuildSimpleComponent(DataComponentType.Recipes, "minecraft:recipes",
        (writer, values) =>
        {
            writer.WriteByte((byte)9); writer.WriteByte((byte)8); writer.WriteInt(values.Length);
            foreach (var value in values)
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(value);
                writer.WriteUnsignedShort(checked((ushort)bytes.Length));
                foreach (var part in bytes) writer.WriteByte(part);
            }
        }, reader =>
        {
            if (reader.ReadByte() != 9) throw new System.IO.InvalidDataException("Expected recipe NBT list.");
            var element = reader.ReadByte(); var count = reader.ReadInt();
            if (count < 0 || count > 65536 || (count > 0 && element != 8)) throw new System.IO.InvalidDataException("Invalid recipe list.");
            var values = new string[count];
            for (var i = 0; i < count; i++)
            {
                var length = reader.ReadUnsignedShort(); var bytes = new byte[length];
                for (var j = 0; j < length; j++) bytes[j] = reader.ReadByte();
                values[i] = System.Text.Encoding.UTF8.GetString(bytes);
            }
            return values;
        });

    public static SimpleDataComponent<FireworkExplosion> FireworkExplosion => BuildSimpleComponent(DataComponentType.FireworkExplosion, "minecraft:firework_explosion",
        (writer, value) => ComponentValueCodecs.WriteExplosion(value, writer),
        ComponentValueCodecs.ReadExplosion);

    public static SimpleDataComponent<string> NoteBlockSound => BuildSimpleComponent(DataComponentType.NoteBlockSound, "minecraft:note_block_sound",
        (writer, value) => writer.WriteString(value),
        reader => reader.ReadString());

    public static BannerPatternsDataComponent BannerPatterns => new();

    public static SimpleDataComponent<Dye> BaseColor => BuildSimpleComponent(DataComponentType.BaseColor, "minecraft:base_color",
        (writer, value) => writer.WriteVarInt(value),
        reader => reader.ReadVarInt<Dye>());

    public static SimpleDataComponent<int> DyedColor => BuildSimpleComponent(DataComponentType.DyedColor, "minecraft:dyed_color",
        (writer, value) => writer.WriteInt(value),
        reader => reader.ReadInt());

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

    public static BeesDataComponent Bees => new();

    public static SimpleDataComponent<Obsidian.Nbt.NbtCompound> Lock => NbtComponent(DataComponentType.Lock, "minecraft:lock");

    public static SimpleDataComponent<Obsidian.Nbt.NbtCompound> ContainerLoot => NbtComponent(DataComponentType.ContainerLoot, "minecraft:container_loot");

    private static SimpleDataComponent<Obsidian.Nbt.NbtCompound> NbtComponent(DataComponentType type, string identifier) =>
        BuildSimpleComponent<Obsidian.Nbt.NbtCompound>(type, identifier, (writer, value) => writer.WriteNbtCompound(value ?? new()), reader => reader.ReadNbtCompound());

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
}

public enum MapPostProcessingType
{
    Lock,
    Scale
}

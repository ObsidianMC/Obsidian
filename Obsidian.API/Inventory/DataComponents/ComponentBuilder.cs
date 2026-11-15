using Obsidian.API.Effects;
using Obsidian.Nbt;

namespace Obsidian.API.Inventory.DataComponents;

/// <summary>
/// Templates of vanilla's item components. Each property returns a new component with an empty or default value: set it
/// with a <c>with</c> expression, like <c>ComponentBuilder.MaxStackSize with { Value = 16 }</c>, and assign it to a
/// stack through its indexer.
/// </summary>
public static partial class ComponentBuilder
{
    /// <summary>
    /// Gets a new <c>minecraft:custom_data</c> component: free-form NBT kept with the stack, which the game doesn't
    /// read.
    /// </summary>
    public static SimpleDataComponent<NbtCompound> CustomData =>
        NbtComponent(DataComponentType.CustomData, "minecraft:custom_data");

    /// <summary>
    /// Gets a new <c>minecraft:max_stack_size</c> component: how many of the item fit in a stack, from 1 to 99.
    /// </summary>
    public static SimpleDataComponent<int> MaxStackSize =>
        BuildSimpleComponent(
            DataComponentType.MaxStackSize,
            "minecraft:max_stack_size",
            (writer, value) => writer.WriteVarInt(value),
            (reader) => reader.ReadVarInt());

    /// <summary>
    /// Gets a new <c>minecraft:max_damage</c> component: how much damage the item takes before it breaks.
    /// </summary>
    public static SimpleDataComponent<int> MaxDamage =>
        BuildSimpleComponent(
            DataComponentType.MaxDamage,
            "minecraft:max_damage",
            (writer, value) => writer.WriteVarInt(value),
            (reader) => reader.ReadVarInt());

    /// <summary>
    /// Gets a new <c>minecraft:damage</c> component: how much damage the item has taken.
    /// </summary>
    public static SimpleDataComponent<int> Damage =>
        BuildSimpleComponent(
            DataComponentType.Damage,
            "minecraft:damage",
            (writer, value) => writer.WriteVarInt(value),
            (reader) => reader.ReadVarInt());

    // A marker: vanilla sends no data, the component being present is what makes the item unbreakable.
    /// <summary>
    /// Gets a new <c>minecraft:unbreakable</c> component: a marker whose presence stops the item taking damage; its
    /// value isn't sent.
    /// </summary>
    public static SimpleDataComponent<bool> Unbreakable =>
        BuildSimpleComponent(
            DataComponentType.Unbreakable,
            "minecraft:unbreakable",
            (writer, value) => { },
            (reader) => true);

    /// <summary>
    /// Gets a new <c>minecraft:custom_name</c> component: a name given to the item, like an anvil gives it.
    /// </summary>
    public static SimpleDataComponent<ChatMessage> CustomName =>
        BuildSimpleComponent(
            DataComponentType.CustomName,
            "minecraft:custom_name",
            (writer, value) => writer.WriteChat(value),
            (reader) => reader.ReadChat(),
            ComponentCopies.Chat);

    /// <summary>
    /// Gets a new <c>minecraft:item_name</c> component: the item's default name, which a custom name overrides.
    /// </summary>
    public static SimpleDataComponent<ChatMessage> ItemName =>
        BuildSimpleComponent(
            DataComponentType.ItemName,
            "minecraft:item_name",
            (writer, value) => writer.WriteChat(value),
            (reader) => reader.ReadChat(),
            ComponentCopies.Chat);

    /// <summary>
    /// Gets a new <c>minecraft:item_model</c> component: the id of the model clients draw the item with.
    /// </summary>
    public static SimpleDataComponent<string> ItemModel =>
        BuildSimpleComponent(
            DataComponentType.ItemModel,
            "minecraft:item_model",
            (writer, value) => writer.WriteString(value),
            (reader) => reader.ReadString());

    /// <summary>
    /// Gets a new <c>minecraft:lore</c> component: extra tooltip lines.
    /// </summary>
    public static SimpleDataComponent<ImmutableArray<ChatMessage>> Lore =>
        BuildSimpleComponent(
            DataComponentType.Lore,
            "minecraft:lore",
            (writer, values) => writer.WriteLengthPrefixedArray(writer.WriteChat, values.AsSpan()),
            (reader) => reader.ReadLengthPrefixedArray(reader.ReadChat).ToImmutableArray(),
            ComponentCopies.Chats);

    /// <summary>
    /// Gets a new <c>minecraft:rarity</c> component: the item's rarity, which colors its name.
    /// </summary>
    public static SimpleDataComponent<ItemRarity> Rarity =>
        BuildSimpleComponent(
            DataComponentType.Rarity,
            "minecraft:rarity",
            (writer, value) => writer.WriteVarInt(value),
            (reader) => reader.ReadVarInt<ItemRarity>());

    /// <summary>
    /// Gets a new <c>minecraft:enchantments</c> component: the item's enchantments and their levels.
    /// </summary>
    public static SimpleDataComponent<ImmutableArray<Enchantment>> Enchantments =>
        BuildSimpleComponent(
            DataComponentType.Enchantments,
            "minecraft:enchantments",
            (writer, values) => writer.WriteLengthPrefixedArray(writer.WriteEnchantment, values.AsSpan()),
            (reader) => reader.ReadLengthPrefixedArray(reader.ReadEnchantment).ToImmutableArray());

    /// <summary>
    /// Gets a new <c>minecraft:custom_model_data</c> component: values resource packs' item models can select on.
    /// </summary>
    public static CustomModelDataComponent CustomModelData =>
        new();

    //public static SimpleDataComponent HideAdditionalTooltip => new(DataComponentType.HideAdditionalTooltip, "minecraft:hide_additional_tooltip");

    //public static SimpleDataComponent HideTooltip => new(DataComponentType.HideTooltip, "minecraft:hide_tooltip");

    /// <summary>
    /// Accumulated anvil usage cost. The client displays "Too Expensive" if the value is greater than 40 and the player is not in creative mode 
    /// (more specifically, if they don't have the insta-build flag enabled).
    /// This behavior can be overridden by setting the level with the Set Container Property packet.
    /// </summary>
    public static SimpleDataComponent<int> RepairCost =>
        BuildSimpleComponent(
            DataComponentType.RepairCost,
            "minecraft:repair_cost",
            (writer, value) => writer.WriteVarInt(value),
            (reader) => reader.ReadVarInt());


    /// <summary>
    /// Marks the item as non-interactive on the creative inventory (the first 5 rows of items).
    /// This is used internally by the client on the paper icon in the saved hot-bars tab.
    /// </summary>
    public static SimpleDataComponent CreativeSlotLock =>
        new(DataComponentType.CreativeSlotLock, "minecraft:creative_slot_lock");

    /// <summary>
    /// Gets a new <c>minecraft:enchantment_glint_override</c> component: whether the item shows the enchantment glint,
    /// whatever its enchantments.
    /// </summary>
    public static SimpleDataComponent<bool> EnchantmentGlintOverride =>
        BuildSimpleComponent(
            DataComponentType.EnchantmentGlintOverride,
            "minecraft:enchantment_glint_override",
            (writer, value) => writer.WriteBoolean(value),
            (reader) => reader.ReadBoolean());

    /// <summary>
    /// Gets a new <c>minecraft:intangible_projectile</c> component: marks a projectile item that can't be picked up,
    /// like an arrow shot in creative mode.
    /// </summary>
    public static SimpleDataComponent<NbtCompound> IntangibleProjectile =>
        NbtComponent(DataComponentType.IntangibleProjectile, "minecraft:intangible_projectile");

    /// <summary>
    /// Gets a new <c>minecraft:use_remainder</c> component: the stack the item leaves once used up, like a bowl after
    /// stew.
    /// </summary>
    public static SimpleDataComponent<ItemStack?> UseRemainder =>
        BuildSimpleComponent(
            DataComponentType.UseRemainder,
            "minecraft:use_remainder",
            (writer, value) => writer.WriteItemStackTemplate(value!),
            reader => reader.ReadItemStackTemplate(),
            ComponentCopies.Stack);

    /// <summary>
    /// Marks this item as damage resistant.
    /// The client won't render the item as being on-fire if this component is present.
    /// </summary>
    public static SimpleDataComponent<IdSet> DamageResistant =>
        BuildSimpleComponent(
            DataComponentType.DamageResistant,
            "minecraft:damage_resistant",
            (writer, value) => IdSet.Write(value, writer, "minecraft:damage_type"),
            reader => IdSet.Read(reader, "minecraft:damage_type"));

    /// <summary>
    /// Gets a new <c>minecraft:enchantable</c> component: how well the item enchants; higher values give better
    /// enchantments.
    /// </summary>
    public static SimpleDataComponent<int> Enchantable =>
        BuildSimpleComponent(
            DataComponentType.Enchantable,
            "minecraft:enchantable",
            (writer, value) => writer.WriteVarInt(value),
            reader => reader.ReadVarInt());

    /// <summary>
    /// Gets a new <c>minecraft:repairable</c> component: the items an anvil repairs the item with.
    /// </summary>
    public static SimpleDataComponent<IdSet> Repairable =>
        BuildSimpleComponent(
            DataComponentType.Repairable,
            "minecraft:repairable",
            (writer, value) => IdSet.Write(value, writer, ComponentValueCodecs.Items),
            reader => IdSet.Read(reader, ComponentValueCodecs.Items));

    // Don't know what this is for
    /// <summary>
    /// Gets a new <c>minecraft:glider</c> component: a marker that lets the item glide like an elytra when worn on the
    /// chest.
    /// </summary>
    public static SimpleDataComponent Glider =>
        new(DataComponentType.Glider, "minecraft:glider");

    /// <summary>
    /// Gets a new <c>minecraft:tooltip_style</c> component: the id of the sprites clients draw the item's tooltip with.
    /// </summary>
    public static SimpleDataComponent<string> TooltipStyle =>
        BuildSimpleComponent(
            DataComponentType.TooltipStyle,
            "minecraft:tooltip_style",
      (writer, value) => writer.WriteString(value),
      reader => reader.ReadString());

    /// <summary>
    /// Gets a new <c>minecraft:death_protection</c> component: saves its holder from dying, like a totem of undying,
    /// and applies its effects.
    /// </summary>
    public static SimpleDataComponent<ImmutableArray<IConsumeEffect>> DeathProtection =>
        BuildSimpleComponent(
            DataComponentType.DeathProtection,
            "minecraft:death_protection",
            (writer, values) => writer.WriteLengthPrefixedArray(
                value => ComponentConsumeEffect.WriteValue(value, writer), values.AsSpan()),
            reader => reader.ReadLengthPrefixedArray<IConsumeEffect>(() => ComponentConsumeEffect.ReadValue(reader))
                .ToImmutableArray());

    /// <summary>
    /// Gets a new <c>minecraft:stored_enchantments</c> component: the enchantments an enchanted book holds for an anvil
    /// to apply.
    /// </summary>
    public static SimpleDataComponent<ImmutableArray<Enchantment>> StoredEnchantments =>
        BuildSimpleComponent(
            DataComponentType.StoredEnchantments,
            "minecraft:stored_enchantments",
            (writer, values) => writer.WriteLengthPrefixedArray(writer.WriteEnchantment, values.AsSpan()),
            reader => reader.ReadLengthPrefixedArray(reader.ReadEnchantment).ToImmutableArray());

    /// <summary>
    /// Gets a new <c>minecraft:attribute_modifiers</c> component: what the item adds to its holder's attributes.
    /// </summary>
    public static AttributeModifiersDataComponent AttributeModifiers =>
        new();

    /// <summary>
    /// A <c>minecraft:tool</c> without rules, mining at speed 1 and taking 1 damage per block, like vanilla's defaults.
    /// </summary>
    public static ToolDataComponent Tool => new()
    {
        Rules = [],
        DefaultMiningSpeed = 1,
        DamagePerBlock = 1
    };

    /// <summary>
    /// A <c>minecraft:food</c> giving nothing; set its nutrition and saturation modifier.
    /// </summary>
    public static FoodDataComponent Food => new();

    /// <summary>
    /// Vanilla's <c>minecraft:consumable</c> for food: eaten in 1.6 seconds, with the eating animation, sound and
    /// particles, and no effects.
    /// </summary>
    public static ConsumableDataComponent Consumable => new()
    {
        ConsumeSeconds = 1.6f,
        Animation = ItemAnimation.Eat,
        Sound = new SoundEvent { ResourceLocation = "minecraft:entity.generic.eat" },
        HasConsumeParticles = true
    };

    /// <summary>
    /// A <c>minecraft:equippable</c> with vanilla's defaults: the generic armour equip sound, dispensable, swappable
    /// by using it, and damaged when its wearer is hurt. Set its slot.
    /// </summary>
    public static EquippableDataComponent Equippable => new()
    {
        EquipSound = new SoundEvent { ResourceLocation = "minecraft:item.armor.equip_generic" },
        Dispensable = true,
        Swappable = true,
        DamageOnHurt = true,
        ShearingSound = new SoundEvent { ResourceLocation = "minecraft:item.shears.snip" }
    };

    /// <summary>
    /// Gets a new <c>minecraft:map_id</c> component: the id of the map a filled map shows.
    /// </summary>
    public static SimpleDataComponent<int> MapId =>
        BuildSimpleComponent(
            DataComponentType.MapId,
            "minecraft:map_id",
       (writer, value) => writer.WriteVarInt(value),
       reader => reader.ReadVarInt());

    /// <summary>
    /// Gets a new <c>minecraft:map_post_processing</c> component: what a cartography table does to a map once it's
    /// taken out.
    /// </summary>
    public static SimpleDataComponent<MapPostProcessingType> MapPostProcessing =>
        BuildSimpleComponent(
            DataComponentType.MapPostProcessing,
            "minecraft:map_post_processing",
       (writer, value) => writer.WriteVarInt(value),
       reader => reader.ReadVarInt<MapPostProcessingType>());

    /// <summary>
    /// Gets a new <c>minecraft:charged_projectiles</c> component: the projectiles a crossbow is loaded with.
    /// </summary>
    public static SimpleDataComponent<ImmutableArray<ItemStack>> ChargedProjectiles =>
        BuildSimpleComponent(
            DataComponentType.ChargedProjectiles,
            "minecraft:charged_projectiles",
            (writer, values) => writer.WriteLengthPrefixedArray(writer.WriteItemStackTemplate, values.AsSpan()),
            reader => reader.ReadLengthPrefixedArray(reader.ReadItemStackTemplate).ToImmutableArray(),
            ComponentCopies.Stacks);

    /// <summary>
    /// Gets a new <c>minecraft:bundle_contents</c> component: the stacks inside a bundle.
    /// </summary>
    public static SimpleDataComponent<ImmutableArray<ItemStack>> BundleContents =>
        BuildSimpleComponent(
            DataComponentType.BundleContents,
            "minecraft:bundle_contents",
            (writer, values) => writer.WriteLengthPrefixedArray(writer.WriteItemStackTemplate, values.AsSpan()),
            reader => reader.ReadLengthPrefixedArray(reader.ReadItemStackTemplate).ToImmutableArray(),
            ComponentCopies.Stacks);

    /// <summary>
    /// Gets a new <c>minecraft:suspicious_stew_effects</c> component: the effects a suspicious stew gives, with
    /// durations in ticks.
    /// </summary>
    public static SimpleDataComponent<ImmutableArray<SuspiciousStewEffect>> SuspiciousStewEffects =>
        BuildSimpleComponent(
            DataComponentType.SuspiciousStewEffects,
            "minecraft:suspicious_stew_effects",
            (writer, values) => writer.WriteLengthPrefixedArray(
                value => SuspiciousStewEffect.Write(value, writer), values.AsSpan()),
            reader => reader.ReadLengthPrefixedArray(() => SuspiciousStewEffect.Read(reader)).ToImmutableArray());

    /// <summary>
    /// Gets a new <c>minecraft:writable_book_content</c> component: the pages of a book and quill.
    /// </summary>
    public static SimpleDataComponent<ImmutableArray<Page>> WritableBookContent =>
        BuildSimpleComponent(
            DataComponentType.WritableBookContent,
            "minecraft:writable_book_content",
            (writer, values) => writer.WriteLengthPrefixedArray((value) => Page.Write(value, writer), values.AsSpan()),
            reader => reader.ReadLengthPrefixedArray(() => Page.Read(reader)).ToImmutableArray());

    /// <summary>
    /// Gets a new <c>minecraft:written_book_content</c> component: a written book's title, author and pages.
    /// </summary>
    public static WrittenBookDataComponent WrittenBookContent =>
        new();

    /// <summary>
    /// Gets a new <c>minecraft:debug_stick_state</c> component: the block property a debug stick changes, by block.
    /// </summary>
    public static SimpleDataComponent<NbtCompound> DebugStickState =>
        NbtComponent(DataComponentType.DebugStickState, "minecraft:debug_stick_state");
    /// <summary>
    /// Gets a new <c>minecraft:entity_data</c> component: the type and saved data of the entity the item spawns, like a
    /// spawn egg's.
    /// </summary>
    public static EntityDataComponent EntityData =>
        new(DataComponentType.EntityData);
    /// <summary>
    /// Gets a new <c>minecraft:bucket_entity_data</c> component: the saved data of a mob in a bucket.
    /// </summary>
    public static BucketEntityDataComponent BucketEntityData =>
        new();
    /// <summary>
    /// Gets a new <c>minecraft:block_entity_data</c> component: block entity data applied when the item places its
    /// block.
    /// </summary>
    public static EntityDataComponent BlockEntityData =>
        new(DataComponentType.BlockEntityData);

    /// <summary>
    /// Gets a new <c>minecraft:instrument</c> component: the instrument a goat horn plays.
    /// </summary>
    public static InstrumentDataComponent Instrument =>
        new();

    /// <summary>
    /// Gets a new <c>minecraft:ominous_bottle_amplifier</c> component: the Bad Omen amplifier an ominous bottle gives,
    /// from 0 to 4.
    /// </summary>
    public static SimpleDataComponent<int> OminousBottleAmplifier =>
        BuildSimpleComponent(
            DataComponentType.OminousBottleAmplifier,
            "minecraft:ominous_bottle_amplifier",
            (writer, value) => writer.WriteVarInt(value),
            reader => reader.ReadVarInt());

    //NBT
    /// <summary>
    /// Gets a new <c>minecraft:recipes</c> component: the recipe ids a knowledge book unlocks.
    /// </summary>
    public static SimpleDataComponent<ImmutableArray<string>> Recipes =>
        BuildSimpleComponent(
            DataComponentType.Recipes,
            "minecraft:recipes",
            (writer, values) =>
            {
                writer.WriteByte((byte)9);
                writer.WriteByte((byte)8);
                writer.WriteInt(values.Length);
                foreach (var value in values)
                {
                    var bytes = System.Text.Encoding.UTF8.GetBytes(value);
                    writer.WriteUnsignedShort(checked((ushort)bytes.Length));
                    foreach (var part in bytes)
                        writer.WriteByte(part);
                }
            }, reader =>
            {
                if (reader.ReadByte() != 9)
                    throw new System.IO.InvalidDataException("Expected recipe NBT list.");

                var element = reader.ReadByte();
                var count = reader.ReadInt();
                if (count < 0 || count > 65536 || (count > 0 && element != 8))
                    throw new System.IO.InvalidDataException("Invalid recipe list.");

                var values = new string[count];
                for (var i = 0; i < count; i++)
                {
                    var length = reader.ReadUnsignedShort();
                    var bytes = new byte[length];
                    for (var j = 0; j < length; j++)
                        bytes[j] = reader.ReadByte();

                    values[i] = System.Text.Encoding.UTF8.GetString(bytes);
                }

                return values.ToImmutableArray();
            });

    /// <summary>
    /// Gets a new <c>minecraft:firework_explosion</c> component: a firework star's explosion.
    /// </summary>
    public static SimpleDataComponent<FireworkExplosion> FireworkExplosion =>
        BuildSimpleComponent(
            DataComponentType.FireworkExplosion,
            "minecraft:firework_explosion",
            (writer, value) => ComponentValueCodecs.WriteExplosion(value, writer),
            ComponentValueCodecs.ReadExplosion);

    /// <summary>
    /// Gets a new <c>minecraft:note_block_sound</c> component: the sound id a player head makes a note block play.
    /// </summary>
    public static SimpleDataComponent<string> NoteBlockSound =>
        BuildSimpleComponent(
            DataComponentType.NoteBlockSound,
            "minecraft:note_block_sound",
            (writer, value) => writer.WriteString(value),
            reader => reader.ReadString());

    /// <summary>
    /// Gets a new <c>minecraft:banner_patterns</c> component: the pattern layers of a banner or shield.
    /// </summary>
    public static BannerPatternsDataComponent BannerPatterns =>
        new();

    /// <summary>
    /// Gets a new <c>minecraft:base_color</c> component: a shield's base color, from the banner put on it.
    /// </summary>
    public static SimpleDataComponent<Dye> BaseColor =>
        BuildSimpleComponent(
            DataComponentType.BaseColor,
            "minecraft:base_color",
            (writer, value) => writer.WriteVarInt(value),
            reader => reader.ReadVarInt<Dye>());

    /// <summary>
    /// Gets a new <c>minecraft:dyed_color</c> component: the RGB color of dyed leather armour and other dyeable items.
    /// </summary>
    public static SimpleDataComponent<int> DyedColor =>
        BuildSimpleComponent(
            DataComponentType.DyedColor,
            "minecraft:dyed_color",
            (writer, value) => writer.WriteInt(value),
            reader => reader.ReadInt());

    /// <summary>Back, left, right and front templates; absent faces are null.</summary>
    public static SimpleDataComponent<ImmutableArray<ItemStack>> PotDecorations =>
        BuildSimpleComponent(DataComponentType.PotDecorations, "minecraft:pot_decorations",
            (writer, values) =>
            {
                if (values.Length != 4)
                    throw new ArgumentException("Pot decorations require four faces.", nameof(values));
                foreach (var value in values)
                {
                    writer.WriteBoolean(value is not null);
                    if (value is not null)
                        writer.WriteItemStackTemplate(value);
                }
            }, reader => Enumerable.Range(0, 4)
                .Select(_ => reader.ReadBoolean() ? reader.ReadItemStackTemplate() : null!)
                .ToImmutableArray(), ComponentCopies.Stacks);

    /// <summary>
    /// Gets a new <c>minecraft:container</c> component: the stacks of a shulker box or other container item, by slot;
    /// empty slots are null.
    /// </summary>
    public static SimpleDataComponent<ImmutableArray<ItemStack>> Container =>
        BuildSimpleComponent(
            DataComponentType.Container,
            "minecraft:container",
            (writer, values) => writer.WriteLengthPrefixedArray(value =>
            {
                var present = value is not null && !value.IsAir && value.Count > 0;
                writer.WriteBoolean(present);
                if (present)
                    writer.WriteItemStackTemplate(value!);
            }, values.AsSpan()),
            reader => reader.ReadLengthPrefixedArray(() => reader.ReadBoolean()
                ? reader.ReadItemStackTemplate() : null!).ToImmutableArray(),
            ComponentCopies.Stacks);

    /// <summary>
    /// Gets a new <c>minecraft:block_state</c> component: block state properties the item's block is placed with.
    /// </summary>
    public static SimpleDataComponent<ImmutableArray<BlockStateProperty>> BlockState =>
        BuildSimpleComponent(
            DataComponentType.BlockState,
            "minecraft:block_state",
            (writer, values) => writer.WriteLengthPrefixedArray(
                value => BlockStateProperty.Write(value, writer), values.AsSpan()),
            reader => reader.ReadLengthPrefixedArray(() => BlockStateProperty.Read(reader)).ToImmutableArray());

    /// <summary>
    /// Gets a new <c>minecraft:bees</c> component: the bees inside a beehive or bee nest item.
    /// </summary>
    public static BeesDataComponent Bees =>
        new();

    /// <summary>
    /// Gets a new <c>minecraft:lock</c> component: the item predicate the key that opens a container must match.
    /// </summary>
    public static SimpleDataComponent<NbtCompound> Lock =>
        NbtComponent(DataComponentType.Lock, "minecraft:lock");

    /// <summary>
    /// Gets a new <c>minecraft:container_loot</c> component: the loot table a container item fills from once placed and
    /// opened.
    /// </summary>
    public static SimpleDataComponent<NbtCompound> ContainerLoot =>
        NbtComponent(DataComponentType.ContainerLoot, "minecraft:container_loot");

    private static SimpleDataComponent<NbtCompound> NbtComponent(DataComponentType type, string identifier) =>
        BuildSimpleComponent(type, identifier,
            (writer, value) => writer.WriteNbtCompound(value ?? new()),
            reader => reader.ReadNbtCompound(),
            ComponentCopies.Nbt);

    /// <summary>
    /// Gets new copies of the placeholders every stack starts with: a stack size of 64, no lore or enchantments, a
    /// repair cost of 0, no attribute modifiers and common rarity. Stacks replace the stack size with their item's own.
    /// </summary>
    public static List<DataComponent> DefaultItemComponents =>
    [
        MaxStackSize with { Value = 64 },
        Lore with { Value = [] },
        Enchantments with { Value = [] },
        RepairCost with { Value = 0 },
        AttributeModifiers with { Value = [] },
        Rarity with { Value = ItemRarity.Common },
    ];

    /// <summary>
    /// Creates a component of <paramref name="type"/> that writes and reads its value with the given delegates, for a
    /// component without a property here.
    /// </summary>
    /// <param name="type">The component's type.</param>
    /// <param name="identifier">The component's identifier, like <c>minecraft:max_stack_size</c>.</param>
    /// <param name="writer">Writes a value as clients read it.</param>
    /// <param name="reader">Reads a value as clients write it.</param>
    /// <param name="copy">Copies a mutable value, so that the component never shares it; null for immutable
    /// values.</param>
    public static SimpleDataComponent<TValue> BuildSimpleComponent<TValue>(DataComponentType type, string identifier,
        Action<INetStreamWriter, TValue> writer,
        Func<INetStreamReader, TValue> reader,
        Func<TValue, TValue>? copy = null) => new(type, identifier, writer, reader, copy);
}

/// <summary>
/// What a cartography table does to a map (<c>minecraft:map_post_processing</c>).
/// </summary>
public enum MapPostProcessingType
{
    /// <summary>
    /// Locks the map so that it no longer changes.
    /// </summary>
    Lock,
    /// <summary>
    /// Zooms the map out by one level.
    /// </summary>
    Scale
}

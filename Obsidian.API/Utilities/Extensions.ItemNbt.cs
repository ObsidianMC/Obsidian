using Obsidian.API.Effects;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Registries;
using Obsidian.Nbt;
using System.Diagnostics.CodeAnalysis;

namespace Obsidian.API.Utilities;

// Saved item stacks, in vanilla's ItemStack.CODEC form: { id, count, components }.
public partial class Extensions
{
    /// <summary>
    /// Saves a stack like vanilla's <c>ItemStack.CODEC</c>: its <c>id</c>, <c>count</c> and the <c>components</c> set on
    /// it (its patch).
    /// </summary>
    /// <remarks>
    /// Only the components Obsidian can generate are saved (damage, names, enchantments, potions, stew effects,
    /// instruments...); others are left out.
    /// </remarks>
    public static NbtCompound ToNbt(this ItemStack item, string name = "")
    {
        var compound = new NbtCompound(name)
        {
            new NbtTag<string>("id", item.Holder.UnlocalizedName),
            new NbtTag<int>("count", item.Count)
        };

        var components = new NbtCompound("components");
        foreach (var component in item.Patch)
        {
            var tag = ComponentToNbt(component);
            if (tag is not null)
                components.Add(tag);
        }

        if (components.Count > 0)
            compound.Add(components);

        return compound;
    }

    /// <summary>
    /// Reads a stack saved like vanilla's <c>ItemStack.CODEC</c>, or <c>null</c> when the compound is missing or its item
    /// is unknown. Components Obsidian doesn't support are skipped.
    /// </summary>
    public static ItemStack? ItemFromNbt(this NbtCompound? item)
    {
        if (item is null || !item.TryGetTag<NbtTag<string>>("id", out var id))
            return null;

        // Unknown ids give the default item.
        var holder = ItemsRegistry.Get(id.Value!);
        if (holder.UnlocalizedName is null)
            return null;

        var count = item.TryGetTag<NbtTag<int>>("count", out var countTag) ? countTag.Value : 1;

        var components = new List<DataComponent>();
        if (item.TryGetTag<NbtCompound>("components", out var componentsCompound))
        {
            foreach (var (componentName, tag) in componentsCompound)
            {
                var component = ComponentFromNbt(componentName, tag);
                if (component is not null)
                    components.Add(component);
            }
        }

        return new ItemStack(holder, count, components);
    }

    /// <summary>
    /// Reads a text component saved by vanilla's <c>ComponentSerialization.CODEC</c>: a plain string, a compound, or
    /// a list whose first component has the rest as extras.
    /// </summary>
    internal static ChatMessage? TextFromNbt(this INbtTag tag) => tag switch
    {
        NbtTag<string> text => ChatMessage.Simple(text.Value ?? string.Empty),
        NbtCompound compound => ChatMessage.Empty.FromNbt(compound),
        NbtList { Count: > 0 } list when list[0].TextFromNbt() is { } first =>
            first.AddExtra(list.Skip(1).Select(TextFromNbt).OfType<ChatMessage>()),
        _ => null
    };

    /// <summary>
    /// A boolean field, which reads back from disk as a byte.
    /// </summary>
    internal static bool TryGetBool(this NbtCompound compound, string name, out bool value)
    {
        if (compound.TryGetTag<NbtTag<bool>>(name, out var flag))
        {
            value = flag.Value;
            return true;
        }

        if (compound.TryGetTag<NbtTag<byte>>(name, out var number))
        {
            value = number.Value != 0;
            return true;
        }

        value = false;
        return false;
    }

    private static INbtTag? ComponentToNbt(DataComponent component)
    {
        var name = component.Identifier;
        return component switch
        {
            SimpleDataComponent<int> number when component.Type is DataComponentType.Damage or DataComponentType.MaxDamage
                or DataComponentType.RepairCost or DataComponentType.OminousBottleAmplifier => new NbtTag<int>(name, number.Value),
            // Vanilla's Unit codec: an empty compound.
            SimpleDataComponent<bool> when component.Type == DataComponentType.Unbreakable => new NbtCompound(name),
            SimpleDataComponent<ChatMessage> text when text.Value is not null => text.Value.ToNbt(name),
            SimpleDataComponent<Enchantment[]> enchantments => EnchantmentsToNbt(name, enchantments.Value ?? []),
            SimpleDataComponent<SuspiciousStewEffect[]> effects => StewEffectsToNbt(name, effects.Value ?? []),
            SimpleDataComponent<InstrumentData> instrument when instrument.Value?.Identifier is not null =>
                new NbtTag<string>(name, instrument.Value.Identifier),
            PotionContentsDataComponent potion => PotionContentsToNbt(name, potion),
            _ => null
        };
    }

    [SuppressMessage("Performance", "CA1859", Justification = "Returns many component types; the analyzer only sees the potion contents one.")]
    private static DataComponent? ComponentFromNbt(string name, INbtTag tag)
    {
        switch (name)
        {
            case "minecraft:damage" when tag is NbtTag<int> damage:
                return ComponentBuilder.Damage with { Value = damage.Value };
            case "minecraft:max_damage" when tag is NbtTag<int> maxDamage:
                return ComponentBuilder.MaxDamage with { Value = maxDamage.Value };
            case "minecraft:repair_cost" when tag is NbtTag<int> repairCost:
                return ComponentBuilder.RepairCost with { Value = repairCost.Value };
            case "minecraft:ominous_bottle_amplifier" when tag is NbtTag<int> amplifier:
                return ComponentBuilder.OminousBottleAmplifier with { Value = amplifier.Value };
            case "minecraft:unbreakable":
                return ComponentBuilder.Unbreakable with { Value = true };
            case "minecraft:custom_name" when tag.TextFromNbt() is ChatMessage customName:
                return ComponentBuilder.CustomName with { Value = customName };
            case "minecraft:item_name" when tag.TextFromNbt() is ChatMessage itemName:
                return ComponentBuilder.ItemName with { Value = itemName };
            case "minecraft:enchantments" when tag is NbtCompound enchantments:
                return ComponentBuilder.Enchantments with { Value = EnchantmentsFromNbt(enchantments) };
            case "minecraft:stored_enchantments" when tag is NbtCompound stored:
                return ComponentBuilder.StoredEnchantments with { Value = EnchantmentsFromNbt(stored) };
            case "minecraft:suspicious_stew_effects" when tag is NbtList effects:
                return ComponentBuilder.SuspiciousStewEffects with { Value = StewEffectsFromNbt(effects) };
            case "minecraft:instrument" when tag is NbtTag<string> instrument:
                var registered = InstrumentsRegistry.All.FirstOrDefault(entry => entry.Identifier == instrument.Value);
                return ComponentBuilder.Instrument with
                {
                    Value = registered is not null ? registered.ToInstrumentData() : new() { Identifier = instrument.Value }
                };
            case "minecraft:potion_contents":
                return PotionContentsFromNbt(tag);
            default:
                return null;
        }
    }

    // Vanilla's ItemEnchantments.CODEC: a map of enchantment id to level.
    private static NbtCompound EnchantmentsToNbt(string name, Enchantment[] enchantments)
    {
        var compound = new NbtCompound(name);
        foreach (var enchantment in enchantments)
            compound.Add(new NbtTag<int>(EnchantmentsRegistry.All[enchantment.Id].Identifier, enchantment.Level));

        return compound;
    }

    private static Enchantment[] EnchantmentsFromNbt(NbtCompound compound)
    {
        var enchantments = new List<Enchantment>();
        foreach (var (id, tag) in compound)
        {
            var definition = EnchantmentsRegistry.All.FirstOrDefault(entry => entry.Identifier == id);
            if (definition is not null && tag is NbtTag<int> level)
                enchantments.Add(new Enchantment { Id = definition.Id, Level = level.Value });
        }

        return [.. enchantments];
    }

    private static NbtList StewEffectsToNbt(string name, SuspiciousStewEffect[] effects)
    {
        var list = new NbtList(NbtTagType.Compound, name);
        foreach (var effect in effects)
        {
            list.Add(new NbtCompound
            {
                new NbtTag<string>("id", EnumId((MobEffect)effect.EffectId)),
                new NbtTag<int>("duration", effect.Duration)
            });
        }

        return list;
    }

    private static SuspiciousStewEffect[] StewEffectsFromNbt(NbtList list)
    {
        var effects = new List<SuspiciousStewEffect>();
        foreach (var entry in list.OfType<NbtCompound>())
        {
            if (!entry.TryGetTag<NbtTag<string>>("id", out var id) || !TryParseEnumId<MobEffect>(id.Value, out var effect))
                continue;

            // Vanilla's default duration.
            var duration = entry.TryGetTag<NbtTag<int>>("duration", out var durationTag) ? durationTag.Value : 160;
            effects.Add(new SuspiciousStewEffect { EffectId = (int)effect, Duration = duration });
        }

        return [.. effects];
    }

    // Vanilla's PotionContents full codec; all fields are optional.
    private static NbtCompound PotionContentsToNbt(string name, PotionContentsDataComponent potion)
    {
        var compound = new NbtCompound(name);
        if (potion.Potion is not null)
            compound.Add(new NbtTag<string>("potion", EnumId(potion.Potion.Value)));
        if (potion.CustomColor is not null)
            compound.Add(new NbtTag<int>("custom_color", potion.CustomColor.Value));

        if (potion.CustomEffects.Length > 0)
        {
            var effects = new NbtList(NbtTagType.Compound, "custom_effects");
            foreach (var effect in potion.CustomEffects)
            {
                effects.Add(new NbtCompound
                {
                    new NbtTag<string>("id", EnumId((MobEffect)effect.Id)),
                    new NbtTag<byte>("amplifier", (byte)effect.Amplifier),
                    new NbtTag<int>("duration", effect.Duration),
                    new NbtTag<bool>("ambient", effect.Ambient),
                    new NbtTag<bool>("show_particles", effect.ShowParticles),
                    new NbtTag<bool>("show_icon", effect.ShowIcon)
                });
            }

            compound.Add(effects);
        }

        if (potion.CustomName is not null)
            compound.Add(new NbtTag<string>("custom_name", potion.CustomName));

        return compound;
    }

    private static PotionContentsDataComponent? PotionContentsFromNbt(INbtTag tag)
    {
        // The alternative form is just the potion id.
        if (tag is NbtTag<string> potionId)
            return TryParseEnumId<Potion>(potionId.Value, out var simple) ? new PotionContentsDataComponent { Potion = simple } : null;

        if (tag is not NbtCompound compound)
            return null;

        var contents = new PotionContentsDataComponent();
        if (compound.TryGetTag<NbtTag<string>>("potion", out var id) && TryParseEnumId<Potion>(id.Value, out var potion))
            contents.Potion = potion;
        if (compound.TryGetTag<NbtTag<int>>("custom_color", out var color))
            contents.CustomColor = color.Value;
        if (compound.TryGetTag<NbtTag<string>>("custom_name", out var customName))
            contents.CustomName = customName.Value;

        if (compound.TryGetTag<NbtList>("custom_effects", out var effects))
        {
            var customEffects = new List<PotionEffectData>();
            foreach (var effect in effects.OfType<NbtCompound>())
            {
                if (!effect.TryGetTag<NbtTag<string>>("id", out var effectId) || !TryParseEnumId<MobEffect>(effectId.Value, out var type))
                    continue;

                customEffects.Add(new PotionEffectData
                {
                    Id = (int)type,
                    Amplifier = effect.TryGetTag<NbtTag<byte>>("amplifier", out var amplifier) ? amplifier.Value : 0,
                    Duration = effect.TryGetTag<NbtTag<int>>("duration", out var duration) ? duration.Value : 0,
                    Ambient = effect.TryGetBool("ambient", out var ambient) && ambient,
                    ShowParticles = !effect.TryGetBool("show_particles", out var particles) || particles,
                    ShowIcon = !effect.TryGetBool("show_icon", out var icon) || icon
                });
            }

            contents.CustomEffects = [.. customEffects];
        }

        return contents;
    }

    // Registry ids of the generated enums, e.g. Potion.LongSwiftness is minecraft:long_swiftness.
    private static string EnumId<TEnum>(TEnum value) where TEnum : struct, Enum => $"minecraft:{value.ToString().ToSnakeCase()}";

    private static bool TryParseEnumId<TEnum>(string? id, out TEnum value) where TEnum : struct, Enum
    {
        value = default;
        return id is not null && Enum.TryParse(id[(id.IndexOf(':') + 1)..].Replace("_", string.Empty), ignoreCase: true, out value);
    }
}

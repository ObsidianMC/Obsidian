using System.Collections.Immutable;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Loot;
using System.Text.Json;

namespace Obsidian.Entities;

internal static class CombatItems
{
    private static readonly Dictionary<string, JsonElement> defaults = LoadDefaults();

    private static Dictionary<string, JsonElement> LoadDefaults()
    {
        using var stream = typeof(CombatItems).Assembly.GetManifestResourceStream("Obsidian.Assets.item_components.json")!;
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.EnumerateObject().ToDictionary(item => item.Name,
            item => item.Value.GetProperty("components").Clone());
    }

    internal static JsonElement Component(ItemStack? item, string name)
        => item != null && defaults.TryGetValue(item.Holder.UnlocalizedName, out var components) &&
            components.TryGetProperty("minecraft:" + name, out var component) ? component : default;

    internal static float Number(JsonElement component, string name, float fallback = 0)
        => component.ValueKind == JsonValueKind.Object && component.TryGetProperty(name, out var value)
            ? value.GetSingle() : fallback;

    internal static int EnchantmentLevel(ItemStack? item, EnchantmentDefinition enchantment)
        => item is { Count: > 0 } && enchantment.CanEnchant(item)
            ? item.GetComponent<SimpleDataComponent<ImmutableArray<Enchantment>>>(DataComponentType.Enchantments)?.Value
                .Where(entry => entry.Id == enchantment.Id).Select(entry => entry.Level).DefaultIfEmpty().Max() ?? 0
            : 0;

    // Vanilla's EnchantmentHelper.getEnchantmentLevel takes the maximum across the living attacker's current
    // equipment in the enchantment's applicable slot groups. It doesn't read the projectile's captured weapon.
    internal static int LootEnchantmentLevel(IEntity? attacker, string identifier)
    {
        var definition = EnchantmentsRegistry.All.FirstOrDefault(enchantment => enchantment.Identifier == identifier);
        if (definition is null || attacker is not Player && attacker is not Mob)
            return 0;

        var level = 0;
        foreach (var slot in Enum.GetValues<EquipmentSlot>())
        {
            var name = slot switch
            {
                EquipmentSlot.MainHand => "mainhand",
                EquipmentSlot.OffHand => "offhand",
                EquipmentSlot.Boots => "feet",
                EquipmentSlot.Leggings => "legs",
                EquipmentSlot.Chestplate => "chest",
                EquipmentSlot.Helmet => "head",
                EquipmentSlot.Body => "body",
                _ => "saddle"
            };
            if (!definition.Slots.Any(group => MatchesSlot(group, name)))
                continue;

            var item = attacker switch
            {
                Mob mob => mob.GetEquipment(slot),
                Player player => slot switch
                {
                    EquipmentSlot.MainHand => player.GetHeldItem(),
                    EquipmentSlot.OffHand => player.Inventory.GetItem(45),
                    EquipmentSlot.Boots => player.Inventory.GetItem(8),
                    EquipmentSlot.Leggings => player.Inventory.GetItem(7),
                    EquipmentSlot.Chestplate => player.Inventory.GetItem(6),
                    EquipmentSlot.Helmet => player.Inventory.GetItem(5),
                    _ => null
                },
                _ => null
            };
            if (item is { Count: > 0 })
            {
                var equippedLevel = item.Enchantments
                    .Where(enchantment => enchantment.Id == definition.Id)
                    .Select(enchantment => enchantment.Level)
                    .DefaultIfEmpty()
                    .Max();
                level = Math.Max(level, equippedLevel);
            }
        }

        return level;
    }

    internal static float Attribute(ItemStack? item, string name, string slot, float baseValue = 0)
    {
        if (item is not { Count: > 0 } || item.RemoveComponents.Contains(DataComponentType.AttributeModifiers)) return baseValue;
        var additions = 0f;
        var baseMultiplier = 0f;
        var totalMultiplier = 1f;
        void Apply(float value, string operation)
        {
            if (operation == "add_value") additions += value;
            else if (operation == "add_multiplied_base") baseMultiplier += value;
            else if (operation == "add_multiplied_total") totalMultiplier *= 1 + value;
        }
        // Effective components include the defaults captured when this stack was made, even after a plugin reload.
        var own = item.Holder.Id >= ItemsRegistry.VanillaCount
            ? item.GetComponent<SimpleDataComponent<ImmutableArray<AttributeModifier>>>(
                DataComponentType.AttributeModifiers)
            : item.Patch.FirstOrDefault(component => component.Type == DataComponentType.AttributeModifiers);
        if (own is SimpleDataComponent<ImmutableArray<AttributeModifier>> custom)
        {
            var id = name switch
            {
                "armor" => AttributeIds.Armor,
                "armor_toughness" => AttributeIds.ArmorToughness,
                "attack_damage" => AttributeIds.AttackDamage,
                "attack_knockback" => AttributeIds.AttackKnockback,
                "attack_speed" => AttributeIds.AttackSpeed,
                "entity_interaction_range" => AttributeIds.EntityInteractionRange,
                "fall_damage_multiplier" => AttributeIds.FallDamageMultiplier,
                "knockback_resistance" => AttributeIds.KnockbackResistance,
                "safe_fall_distance" => AttributeIds.SafeFallDistance,
                "sweeping_damage_ratio" => AttributeIds.SweepingDamageRatio,
                _ => -1
            };
            foreach (var modifier in custom.Value)
            {
                if (modifier.Id != id || !MatchesSlot(modifier.Slot.ToString().ToLowerInvariant(), slot)) continue;
                Apply((float)modifier.Value, modifier.Operation switch
                { AttributeOperation.Add => "add_value", AttributeOperation.MulBase => "add_multiplied_base", _ => "add_multiplied_total" });
            }
        }
        else if (item.Holder.Id < ItemsRegistry.VanillaCount
            && Component(item, "attribute_modifiers") is { ValueKind: JsonValueKind.Array } modifiers)
        {
            foreach (var modifier in modifiers.EnumerateArray())
                if (modifier.GetProperty("type").GetString() == "minecraft:" + name &&
                    MatchesSlot(modifier.TryGetProperty("slot", out var group) ? group.GetString()! : "any", slot))
                    Apply(modifier.GetProperty("amount").GetSingle(), modifier.GetProperty("operation").GetString()!);
        }
        return (baseValue + additions) * (1 + baseMultiplier) * totalMultiplier;
    }

    /// <summary>
    /// The attribute modifiers an item applies in an equipment slot (<c>mainhand</c>, <c>offhand</c>, <c>head</c>,
    /// <c>chest</c>, <c>legs</c> or <c>feet</c>), like vanilla's <c>ItemStack.forEachModifier</c>: its own
    /// <c>attribute_modifiers</c> component, or else its item's default, by attribute name.
    /// </summary>
    internal static List<(string Attribute, EntityAttributeModifier Modifier)> Modifiers(ItemStack? item, string slot)
    {
        var result = new List<(string, EntityAttributeModifier)>();
        if (item is not { Count: > 0 } || item.RemoveComponents.Contains(DataComponentType.AttributeModifiers))
            return result;

        // Effective components include the defaults captured when this stack was made, even after a plugin reload.
        var own = item.Holder.Id >= ItemsRegistry.VanillaCount
            ? item.GetComponent<SimpleDataComponent<ImmutableArray<AttributeModifier>>>(
                DataComponentType.AttributeModifiers)
            : item.Patch.FirstOrDefault(component => component.Type == DataComponentType.AttributeModifiers);

        if (own is SimpleDataComponent<ImmutableArray<AttributeModifier>> custom)
        {
            foreach (var modifier in custom.Value)
            {
                var group = modifier.Slot.ToString().ToLowerInvariant();
                if (VanillaAttributes.NameOf(modifier.Id) is not string attribute || !MatchesSlot(group, slot))
                    continue;

                result.Add((attribute, new EntityAttributeModifier(modifier.Name, modifier.Value, modifier.Operation)));
            }
        }
        else if (item.Holder.Id < ItemsRegistry.VanillaCount
            && Component(item, "attribute_modifiers") is { ValueKind: JsonValueKind.Array } modifiers)
        {
            foreach (var modifier in modifiers.EnumerateArray())
            {
                var group = modifier.TryGetProperty("slot", out var value) ? value.GetString()! : "any";
                if (!MatchesSlot(group, slot))
                    continue;

                var operation = modifier.GetProperty("operation").GetString() switch
                {
                    "add_multiplied_base" => AttributeOperation.MulBase,
                    "add_multiplied_total" => AttributeOperation.MulTotal,
                    _ => AttributeOperation.Add
                };

                var entry = new EntityAttributeModifier(
                    modifier.GetProperty("id").GetString()!,
                    modifier.GetProperty("amount").GetDouble(),
                    operation);

                result.Add((modifier.GetProperty("type").GetString()!, entry));
            }
        }

        return result;
    }

    private static bool MatchesSlot(string group, string slot) => group == "any" || group == slot ||
        group == "hand" && slot is "mainhand" or "offhand" ||
        group == "armor" && slot is "head" or "chest" or "legs" or "feet";

    internal static float ReduceArmor(float damage, float armor, float toughness, float effectiveness = 1)
    {
        armor = Math.Clamp(armor, 0, 30);
        toughness = Math.Clamp(toughness, 0, 20);
        var reduction = Math.Clamp(armor - damage / (2 + toughness / 4), armor * 0.2f, 20) / 25;
        return damage * (1 - Math.Clamp(reduction * effectiveness, 0, 1));
    }

    internal static float FallDamage(float distance, int jumpBoost = 0)
        => Math.Max(0, MathF.Ceiling(distance - 3 - jumpBoost));

    internal static float SmashBonus(float distance)
        => distance <= 3 ? distance * 4 : distance <= 8 ? 12 + (distance - 3) * 2 : 22 + distance - 8;

    internal static int MaxDamage(ItemStack item) => item.RemoveComponents.Contains(DataComponentType.MaxDamage) ? 0 :
        item.GetComponent<SimpleDataComponent<int>>(DataComponentType.MaxDamage)?.Value ?? item.Holder.MaxDamage;

    /// <summary>
    /// How much breaking <paramref name="block"/> wears <paramref name="tool"/>, like vanilla's <c>Item.mineBlock</c>:
    /// its tool component's damage per block, or 0 for a block that breaks instantly or an item that isn't a tool.
    /// </summary>
    internal static int ToolDamage(ItemStack? tool, IBlock block)
    {
        if (tool is not { Count: > 0 }
            || tool.RemoveComponents.Contains(DataComponentType.Tool)
            || BlockPhysics.MiningHardness(block.GetHashCode()) == 0)
        {
            return 0;
        }

        if (tool.GetComponent<ToolDataComponent>(DataComponentType.Tool) is ToolDataComponent component)
            return component.DamagePerBlock;

        var vanilla = Component(tool, "tool");

        return vanilla.ValueKind == JsonValueKind.Object ? (int)Number(vanilla, "damage_per_block", 1) : 0;
    }

    internal static int PlayerArmorSlot(ItemStack? item)
    {
        if (item == null || item.RemoveComponents.Contains(DataComponentType.Equippable)) return -1;
        if (item.GetComponent<EquippableDataComponent>(DataComponentType.Equippable) is { } custom)
            return custom.Slot switch { EquipmentSlot.Helmet => 5, EquipmentSlot.Chestplate => 6, EquipmentSlot.Leggings => 7, EquipmentSlot.Boots => 8, _ => -1 };
        var component = Component(item, "equippable");
        return component.ValueKind == JsonValueKind.Object && component.TryGetProperty("slot", out var slot)
            ? slot.GetString() switch { "head" => 5, "chest" => 6, "legs" => 7, "feet" => 8, _ => -1 } : -1;
    }

    /// <summary>
    /// Whether the item equips in the off hand, like a shield (its <c>equippable</c> component's slot).
    /// </summary>
    internal static bool IsOffhandItem(ItemStack? item)
    {
        if (item == null || item.RemoveComponents.Contains(DataComponentType.Equippable))
            return false;

        if (item.GetComponent<EquippableDataComponent>(DataComponentType.Equippable) is { } custom)
            return custom.Slot == EquipmentSlot.OffHand;

        var component = Component(item, "equippable");

        return component.ValueKind == JsonValueKind.Object
            && component.TryGetProperty("slot", out var slot)
            && slot.GetString() == "offhand";
    }

    internal static bool HurtItem(ItemStack item, int amount, bool armor = false)
    {
        var maximum = MaxDamage(item);
        if (item.Unbreakable || maximum <= 0 || item.Count <= 0) return false;
        var unbreaking = EnchantmentLevel(item, EnchantmentsRegistry.Unbreaking);
        var applied = 0;
        for (var point = 0; point < amount; point++)
            if (unbreaking == 0 || armor && Globals.Random.NextSingle() < 0.6f || Globals.Random.Next(unbreaking + 1) == 0)
                applied++;
        if (applied == 0) return false;
        item[DataComponentType.Damage] = ComponentBuilder.Damage with { Value = item.Damage + applied };
        if (item.Damage < maximum) return false;
        item.Count--;
        item[DataComponentType.Damage] = ComponentBuilder.Damage with { Value = 0 };
        return true;
    }
}

internal enum CombatDamageKind { Melee, Projectile, Fall, Fire, Explosion, Magic, Starvation, Void, SonicBoom, Suffocation, Freezing, FireContact, Thorns }

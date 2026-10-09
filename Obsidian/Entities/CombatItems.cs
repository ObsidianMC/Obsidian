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
            ? item.GetComponent<SimpleDataComponent<Enchantment[]>>(DataComponentType.Enchantments)?.Value?
                .Where(entry => entry.Id == enchantment.Id).Select(entry => entry.Level).DefaultIfEmpty().Max() ?? 0
            : 0;

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
        if (item.Patch.FirstOrDefault(component => component.Type == DataComponentType.AttributeModifiers) is
            SimpleDataComponent<AttributeModifier[]> custom)
        {
            // Protocol ids from the 1.21.11 vanilla attribute registry.
            var id = name switch
            {
                "armor" => 0, "armor_toughness" => 1, "attack_damage" => 2, "attack_knockback" => 3,
                "attack_speed" => 4, "entity_interaction_range" => 10, "fall_damage_multiplier" => 11,
                "knockback_resistance" => 16, "safe_fall_distance" => 24, "sweeping_damage_ratio" => 30, _ => -1
            };
            foreach (var modifier in custom.Value ?? [])
            {
                if (modifier.Id != id || !MatchesSlot(modifier.Slot.ToString().ToLowerInvariant(), slot)) continue;
                Apply((float)modifier.Value, modifier.Operation switch
                { AttributeOperation.Add => "add_value", AttributeOperation.MulBase => "add_multiplied_base", _ => "add_multiplied_total" });
            }
        }
        else if (Component(item, "attribute_modifiers") is { ValueKind: JsonValueKind.Array } modifiers)
        {
            foreach (var modifier in modifiers.EnumerateArray())
                if (modifier.GetProperty("type").GetString() == "minecraft:" + name &&
                    MatchesSlot(modifier.TryGetProperty("slot", out var group) ? group.GetString()! : "any", slot))
                    Apply(modifier.GetProperty("amount").GetSingle(), modifier.GetProperty("operation").GetString()!);
        }
        return (baseValue + additions) * (1 + baseMultiplier) * totalMultiplier;
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

    internal static int PlayerArmorSlot(ItemStack? item)
    {
        if (item == null || item.RemoveComponents.Contains(DataComponentType.Equippable)) return -1;
        if (item.GetComponent<EquippableDataComponent>(DataComponentType.Equippable) is { } custom)
            return custom.Slot switch { EquipmentSlot.Helmet => 5, EquipmentSlot.Chestplate => 6, EquipmentSlot.Leggings => 7, EquipmentSlot.Boots => 8, _ => -1 };
        var component = Component(item, "equippable");
        return component.ValueKind == JsonValueKind.Object && component.TryGetProperty("slot", out var slot)
            ? slot.GetString() switch { "head" => 5, "chest" => 6, "legs" => 7, "feet" => 8, _ => -1 } : -1;
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

internal enum CombatDamageKind { Melee, Projectile, Fall, Fire, Explosion, Magic, Starvation, Void }

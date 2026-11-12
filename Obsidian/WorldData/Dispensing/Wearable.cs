using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.Entities;
using System.Text.Json;

namespace Obsidian.WorldData.Dispensing;

/// <summary>
/// What a stack's <c>minecraft:equippable</c> component says about wearing it, from the stack's own component or else
/// its item's vanilla default: the slot it goes in, whether a dispenser may put it on a mob, the sound of putting it
/// on, and which entities it fits.
/// </summary>
internal sealed record Wearable(EquipmentSlot Slot, bool Dispensable, string EquipSound, Func<EntityType, bool> Fits)
{
    // Vanilla's Equippable defaults.
    private const string DefaultEquipSound = "minecraft:item.armor.equip_generic";

    /// <summary>The stack's equippable component, or null when it has none.</summary>
    internal static Wearable? Of(ItemStack stack)
    {
        if (stack.RemoveComponents.Contains(DataComponentType.Equippable))
            return null;

        if (stack.GetComponent<EquippableDataComponent>(DataComponentType.Equippable) is { } own)
        {
            return new Wearable(
                own.Slot,
                own.Dispensable,
                own.EquipSound.ResourceLocation,
                type => FitsComponent(own, type));
        }

        var component = CombatItems.Component(stack, "equippable");
        if (component.ValueKind != JsonValueKind.Object
            || !component.TryGetProperty("slot", out var slotName)
            || SlotNamed(slotName.GetString()) is not EquipmentSlot slot)
        {
            return null;
        }

        var dispensable = !component.TryGetProperty("dispensable", out var flag) || flag.GetBoolean();
        var sound = component.TryGetProperty("equip_sound", out var named)
            ? named.GetString() ?? DefaultEquipSound
            : DefaultEquipSound;
        var allowed = component.TryGetProperty("allowed_entities", out var entities) ? Allowed(entities) : null;

        return new Wearable(slot, dispensable, sound, type => allowed is null || allowed.Contains((int)type));
    }

    // Vanilla's EquipmentSlot names.
    private static EquipmentSlot? SlotNamed(string? name) => name switch
    {
        "head" => EquipmentSlot.Helmet,
        "chest" => EquipmentSlot.Chestplate,
        "legs" => EquipmentSlot.Leggings,
        "feet" => EquipmentSlot.Boots,
        "mainhand" => EquipmentSlot.MainHand,
        "offhand" => EquipmentSlot.OffHand,
        "body" => EquipmentSlot.Body,
        "saddle" => EquipmentSlot.Saddle,
        _ => null
    };

    // The entity type ids an allowed_entities value names: a tag (#...), one type, or a list of types.
    private static HashSet<int> Allowed(JsonElement entities)
    {
        var ids = new HashSet<int>();
        if (entities.ValueKind == JsonValueKind.Array)
        {
            foreach (var entity in entities.EnumerateArray())
                AddType(ids, entity.GetString());

            return ids;
        }

        var name = entities.GetString() ?? "";
        if (!name.StartsWith('#'))
        {
            AddType(ids, name);
            return ids;
        }

        if (EntityTag(name[1..]) is { } tag)
            ids.UnionWith(tag);

        return ids;
    }

    private static void AddType(HashSet<int> ids, string? name)
    {
        if (EntityNbt.TryParseType(name, out var type))
            ids.Add((int)type);
    }

    private static IEnumerable<int>? EntityTag(string name)
    {
        var path = name.StartsWith("minecraft:", StringComparison.Ordinal) ? name["minecraft:".Length..] : name;
        if (TagsRegistry.TryGet("entity_type", name, out var tag) || TagsRegistry.TryGet("entity_type", path, out tag))
            return tag.Entries;

        return null;
    }

    // Vanilla's Equippable.canBeEquippedBy, for a stack's own component: the entities it allows, by id or by tag, or
    // any.
    private static bool FitsComponent(EquippableDataComponent equippable, EntityType type)
    {
        if (equippable.AllowedEntities is { } ids)
            return ids.Contains((int)type);

        if (equippable.AllowedEntitySet is not { } set)
            return true;

        if (set.Ids is { } setIds)
            return setIds.Contains((int)type);

        return set.TagName is string tagName && EntityTag(tagName.TrimStart('#')) is { } tag && tag.Contains((int)type);
    }
}

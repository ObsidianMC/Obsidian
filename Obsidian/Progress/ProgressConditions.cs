using Obsidian.API.Inventory;
using Obsidian.API.Loot;
using Obsidian.Entities;
using Obsidian.Entities.AI;
using System.IO;
using System.Text.Json;

namespace Obsidian.Progress;

/// <summary>Supported vanilla predicates share loot's item, registry-set and block-state matching.</summary>
internal static class ProgressConditions
{
    public static bool Match(IPlayer player, string trigger, JsonElement conditions, ItemStack? item,
        IEntity? entity, IBlock? block, Vector? position, string? recipe, string? from, string? to, int? levels)
    {
        foreach (var condition in conditions.EnumerateObject())
        {
            var matched = condition.Name switch
            {
                "player" => Entity(player, condition.Value),
                "items" when trigger == "inventory_changed" => Inventory(player, condition.Value),
                "slots" when trigger == "inventory_changed" => Slots(player, condition.Value),
                "item" when trigger is "consume_item" or "enchanted_item" => Item(item, condition.Value),
                "entity" when trigger is "player_killed_entity" or "entity_killed_player" =>
                    entity is not null && Entity(entity, condition.Value),
                "recipe" when trigger == "recipe_unlocked" =>
                    recipe == AdvancementsRegistry.Qualify(condition.Value.GetString()!),
                "recipe_id" when trigger == "recipe_crafted" =>
                    recipe == AdvancementsRegistry.Qualify(condition.Value.GetString()!),
                "from" when trigger == "changed_dimension" =>
                    from == AdvancementsRegistry.Qualify(condition.Value.GetString()!),
                "to" when trigger == "changed_dimension" =>
                    to == AdvancementsRegistry.Qualify(condition.Value.GetString()!),
                "levels" when trigger == "enchanted_item" =>
                    levels is int value && NumberRange(condition.Value)
                        && LootTableJson.MatchesRange(value, condition.Value),
                "location" when trigger is "placed_block" or "item_used_on_block" =>
                    LocationEvent(player, condition.Value, item, block, position),
                _ => false
            };
            if (!matched)
                return false;
        }

        return trigger != "impossible";
    }

    private static bool LocationEvent(IPlayer player, JsonElement conditions, ItemStack? item,
        IBlock? block, Vector? position)
    {
        if (conditions.ValueKind != JsonValueKind.Array)
            return false;

        foreach (var condition in conditions.EnumerateArray())
        {
            var type = AdvancementsRegistry.Qualify(condition.GetProperty("condition").GetString()!);
            var matched = type switch
            {
                "minecraft:match_tool" => condition.EnumerateObject()
                    .All(property => property.Name is "condition" or "predicate")
                    && Item(item, condition.GetProperty("predicate")),
                "minecraft:block_state_property" => condition.EnumerateObject()
                    .All(property => property.Name is "condition" or "block" or "properties")
                    && block is not null
                    && block.UnlocalizedName
                        == AdvancementsRegistry.Qualify(condition.GetProperty("block").GetString()!)
                    && (!condition.TryGetProperty("properties", out var properties)
                        || LootTableJson.MatchesProperties(block.GetProperties(), properties)),
                "minecraft:location_check" => condition.EnumerateObject()
                    .All(property => property.Name is "condition" or "predicate")
                    && Location(player, condition.GetProperty("predicate"), position),
                _ => false
            };
            if (!matched)
                return false;
        }

        return true;
    }

    private static bool Inventory(IPlayer player, JsonElement predicates)
    {
        var items = player.Inventory.Skip(9).Take(36).Where(item => item is { Count: > 0 }).ToArray();
        return predicates.EnumerateArray().All(predicate => items.Any(item => Item(item, predicate)));
    }

    private static bool Slots(IPlayer player, JsonElement predicates)
    {
        var slots = player.Inventory.Skip(9).Take(36).ToArray();
        var occupied = slots.Count(item => item is { Count: > 0 });
        var full = slots.Count(item => item is { Count: > 0 } && item.Count >= item.MaxStackSize);
        return predicates.EnumerateObject().All(property => NumberRange(property.Value) && (property.Name switch
        {
            "occupied" => LootTableJson.MatchesRange(occupied, property.Value),
            "empty" => LootTableJson.MatchesRange(slots.Length - occupied, property.Value),
            "full" => LootTableJson.MatchesRange(full, property.Value),
            _ => false
        }));
    }

    private static bool Item(ItemStack? item, JsonElement predicate)
    {
        try
        {
            if (predicate.TryGetProperty("count", out var count) && !NumberRange(count))
                return false;

            return LootTableJson.MatchesTool(item, predicate);
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    private static bool Entity(IEntity entity, JsonElement predicate)
    {
        if (predicate.ValueKind == JsonValueKind.Array)
            return predicate.EnumerateArray().All(condition => LootCondition(entity, condition));

        return predicate.EnumerateObject().All(property => property.Name switch
        {
            "type" => EntityIdentifier(entity) is string identifier
                && entity is Entities.Entity concrete
                && LootTableJson.MatchesRegistrySet(property.Value, "entity_type", identifier, concrete.TypeId),
            "location" => Location(entity, property.Value),
            _ => false
        });
    }

    private static bool LootCondition(IEntity entity, JsonElement condition)
    {
        var type = AdvancementsRegistry.Qualify(condition.GetProperty("condition").GetString()!);
        if (type == "minecraft:all_of")
        {
            return condition.EnumerateObject().All(property => property.Name is "condition" or "terms")
                && condition.GetProperty("terms").EnumerateArray().All(term => LootCondition(entity, term));
        }

        if (type == "minecraft:entity_properties"
            && condition.EnumerateObject().All(property => property.Name is "condition" or "entity" or "predicate")
            && condition.GetProperty("entity").GetString() == "this")
        {
            return Entity(entity, condition.GetProperty("predicate"));
        }

        if (type == "minecraft:location_check")
        {
            if (condition.EnumerateObject().Any(property => property.Name is not ("condition" or "predicate")))
                return false;

            return Location(entity, condition.GetProperty("predicate"));
        }

        return false;
    }

    private static bool Location(IEntity entity, JsonElement predicate, Vector? position = null)
    {
        var origin = position is Vector blockPosition ? (VectorD)blockPosition : entity.Position;
        foreach (var property in predicate.EnumerateObject())
        {
            if (property.Name == "dimension")
            {
                if (entity.Level.DimensionName != AdvancementsRegistry.Qualify(property.Value.GetString()!))
                    return false;
            }
            else if (property.Name == "position")
            {
                foreach (var axis in property.Value.EnumerateObject())
                {
                    var coordinate = axis.Name switch
                    {
                        "x" => origin.X,
                        "y" => origin.Y,
                        "z" => origin.Z,
                        _ => double.NaN
                    };
                    if (!Range(coordinate, axis.Value))
                        return false;
                }
            }
            else if (property.Name == "block")
            {
                var block = new MobTerrain(entity.Level).GetBlock((Vector)origin.Floor());
                if (block is null || !Block(block, property.Value))
                    return false;
            }
            else
            {
                return false;
            }
        }

        return true;
    }

    private static bool Block(IBlock block, JsonElement predicate) => predicate.EnumerateObject().All(property =>
        property.Name switch
        {
            "blocks" => LootTableJson.MatchesRegistrySet(property.Value, "block", block.UnlocalizedName, block.BaseId),
            "state" => LootTableJson.MatchesProperties(block.GetProperties(), property.Value),
            _ => false
        });

    private static bool NumberRange(JsonElement predicate) => predicate.ValueKind == JsonValueKind.Number
        || predicate.ValueKind == JsonValueKind.Object
            && predicate.EnumerateObject().All(property => property.Name is "min" or "max"
                && property.Value.ValueKind == JsonValueKind.Number);

    private static bool Range(double value, JsonElement predicate) => double.IsFinite(value) && NumberRange(predicate)
        && (predicate.ValueKind == JsonValueKind.Number ? value == predicate.GetDouble()
            : (!predicate.TryGetProperty("min", out var min) || value >= min.GetDouble())
                && (!predicate.TryGetProperty("max", out var max) || value <= max.GetDouble()));

    public static string? EntityIdentifier(IEntity entity) => entity is Entities.Entity concrete
        ? concrete.Definition?.Identifier ?? (concrete.Type is EntityType type ? EntityNbt.TypeId(type) : null)
        : null;
}

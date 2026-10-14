using Obsidian.API.Inventory;
using Obsidian.API.Loot.Conditions;
using Obsidian.API.Loot.Numbers;
using Obsidian.API.Registries;
using Obsidian.API.Utilities;
using System.IO;
using System.Text.Json;

namespace Obsidian.API.Loot;

public static partial class LootTableJson
{
    private static ImmutableArray<ILootCondition> ReadConditions(JsonElement json) =>
        ReadArray(json, "conditions", ReadCondition);

    private static ILootCondition ReadCondition(JsonElement json)
    {
        var type = Qualify(Text(json, "condition"));
        return type switch
        {
            "minecraft:all_of" or "minecraft:any_of" => Terms(
                ReadArray(json, "terms", ReadCondition), type == "minecraft:all_of"),
            "minecraft:inverted" => Invert(ReadCondition(json.GetProperty("term"))),
            "minecraft:survives_explosion" => new PredicateCondition(context =>
                context.ExplosionRadius is not float radius || context.Random.NextFloat() <= 1 / radius),
            "minecraft:random_chance" => Chance(ReadNumber(json.GetProperty("chance"))),
            "minecraft:table_bonus" => TableBonus(json),
            "minecraft:match_tool" => ToolPredicate(json.GetProperty("predicate").Clone()),
            "minecraft:block_state_property" => StatePredicate(json.Clone()),
            "minecraft:location_check" => LocationPredicate(json.Clone()),
            "minecraft:entity_properties" => EntityPredicate(json.Clone()),
            "minecraft:killed_by_player" => new PredicateCondition(context => context.LastDamagePlayer is not null),
            "minecraft:random_chance_with_enchanted_bonus" => EnchantedChance(json),
            _ => throw new InvalidDataException($"Unsupported loot condition {type}.")
        };
    }

    private static ILootCondition Invert(ILootCondition term) =>
        new PredicateCondition(context => !term.Test(context));

    private static ILootCondition Terms(ImmutableArray<ILootCondition> terms, bool all) =>
        new PredicateCondition(context => TestTerms(terms, context, all));

    private static bool TestTerms(ImmutableArray<ILootCondition> terms, LootContext context, bool all)
    {
        foreach (var term in terms)
        {
            if (term.Test(context) != all)
                return !all;
        }

        return all;
    }

    private static ILootCondition Chance(INumberProvider chance) => new PredicateCondition(context =>
        context.Random.NextFloat() < chance.GetFloat(context));

    // Vanilla's LootItemRandomChanceWithEnchantedBonusCondition: the attacker's enchantment level picks the enchanted
    // chance for that level, or the unenchanted chance at level 0, and one draw decides.
    private static ILootCondition EnchantedChance(JsonElement json)
    {
        var enchantment = Qualify(Text(json, "enchantment"));
        var unenchanted = json.GetProperty("unenchanted_chance").GetSingle();
        var enchanted = ReadLevelBasedValue(json.GetProperty("enchanted_chance"));

        return new PredicateCondition(context =>
        {
            var level = context.AttackerEnchantmentLevel(enchantment);
            var chance = level > 0 ? enchanted(level) : unenchanted;

            return context.Random.NextFloat() < chance;
        });
    }

    // Vanilla's LevelBasedValue, as a number (a constant) or a linear value: its base plus its increase per level
    // above the first.
    internal static Func<int, float> ReadLevelBasedValue(JsonElement json)
    {
        if (json.ValueKind == JsonValueKind.Number)
        {
            var constant = json.GetSingle();
            return _ => constant;
        }

        if (Qualify(Text(json, "type")) != "minecraft:linear")
            throw new InvalidDataException($"Unsupported level-based value {Text(json, "type")}.");

        var start = json.GetProperty("base").GetSingle();
        var step = json.GetProperty("per_level_above_first").GetSingle();

        return level => start + step * (level - 1);
    }

    private static ILootCondition TableBonus(JsonElement json)
    {
        var enchantment = Qualify(Text(json, "enchantment"));
        var chances = json.GetProperty("chances").EnumerateArray().Select(value => value.GetSingle()).ToArray();
        if (chances.Length == 0)
            throw new InvalidDataException("table_bonus needs at least one chance.");

        return new PredicateCondition(context => context.Random.NextFloat()
            < chances[Math.Min(EnchantmentLevel(context.Tool, enchantment), chances.Length - 1)]);
    }

    private static ILootCondition ToolPredicate(JsonElement predicate)
    {
        foreach (var property in predicate.EnumerateObject())
        {
            if (property.Name is not ("items" or "count" or "predicates"))
                throw new InvalidDataException($"Unsupported tool predicate {property.Name}.");

            if (property.Name == "predicates"
                && property.Value.EnumerateObject().Any(value => value.Name != "minecraft:enchantments"))
                throw new InvalidDataException("Only active enchantments are supported in tool component predicates.");
        }

        return new PredicateCondition(context => MatchesTool(context.Tool, predicate));
    }

    private static ILootCondition StatePredicate(JsonElement json) => new PredicateCondition(context =>
        context.BlockState is IBlock block && block.UnlocalizedName == Qualify(Text(json, "block"))
            && (!json.TryGetProperty("properties", out var properties)
                || MatchesProperties(context.BlockProperties, properties)));

    private static ILootCondition LocationPredicate(JsonElement json)
    {
        var predicate = json.GetProperty("predicate");
        if (predicate.EnumerateObject().Any(value => value.Name != "block"))
            throw new InvalidDataException("Only block predicates are supported by block loot location checks.");

        if (predicate.TryGetProperty("block", out var block)
            && block.EnumerateObject().Any(value => value.Name is not ("blocks" or "state")))
            throw new InvalidDataException("Location block predicates support blocks and state.");

        return new PredicateCondition(context => context.TestLocation?.Invoke(json) == true);
    }

    private static ILootCondition EntityPredicate(JsonElement json)
    {
        var target = Text(json, "entity");
        if (target is not ("this" or "attacker" or "direct_attacker" or "attacking_player"))
            throw new InvalidDataException($"Unsupported entity target {target}.");

        var predicate = json.GetProperty("predicate");
        ValidateEntityPredicate(predicate);
        return new PredicateCondition(context => MatchesEntity(target switch
        {
            "this" => context.ThisEntity,
            "attacker" => context.AttackingEntity,
            "direct_attacker" => context.DirectAttackingEntity,
            _ => context.LastDamagePlayer
        }, predicate, context));
    }

    private static void ValidateEntityPredicate(JsonElement predicate)
    {
        foreach (var property in predicate.EnumerateObject())
        {
            switch (property.Name)
            {
                case "type":
                    break;
                case "vehicle":
                    ValidateEntityPredicate(property.Value);
                    break;
                case "equipment":
                    foreach (var slot in property.Value.EnumerateObject())
                    {
                        if (slot.Name is not ("mainhand" or "offhand" or "head" or "chest" or "legs"
                            or "feet" or "body" or "saddle"))
                        {
                            throw new InvalidDataException($"Unsupported equipment slot {slot.Name}.");
                        }

                        _ = ToolPredicate(slot.Value);
                    }

                    break;
                case "flags":
                    foreach (var flag in property.Value.EnumerateObject())
                    {
                        if (flag.Name is not ("is_on_fire" or "is_sneaking" or "is_sprinting" or "is_swimming"
                            or "is_flying" or "is_on_ground" or "is_baby"))
                        {
                            throw new InvalidDataException($"Unsupported entity flag {flag.Name}.");
                        }
                    }

                    break;
                default:
                    throw new InvalidDataException($"Unsupported entity predicate {property.Name}.");
            }
        }
    }

    private static bool MatchesEntity(IEntity? entity, JsonElement predicate, LootContext context)
    {
        if (entity is null)
            return false;

        foreach (var property in predicate.EnumerateObject())
        {
            switch (property.Name)
            {
                case "type":
                    var id = entity.Definition?.Id ?? (int)(entity.Type ?? (EntityType)(-1));
                    var name = entity.Definition?.Identifier ?? "minecraft:" + entity.Type?.ToString().ToSnakeCase();
                    if (!MatchesRegistrySet(property.Value, "entity_type", name, id))
                        return false;

                    break;
                case "vehicle":
                    if (!MatchesEntity((entity as IPlayer)?.Vehicle, property.Value, context))
                        return false;

                    break;
                case "equipment":
                    foreach (var slot in property.Value.EnumerateObject())
                    {
                        if (!MatchesTool(context.EntityEquipment?.Invoke(entity, slot.Name), slot.Value))
                            return false;
                    }

                    break;
                case "flags":
                    foreach (var flag in property.Value.EnumerateObject())
                    {
                        var value = flag.Name switch
                        {
                            "is_on_fire" => context.EntityIsOnFire?.Invoke(entity) ?? entity.Burning,
                            "is_sneaking" => entity.Sneaking,
                            "is_sprinting" => entity.Sprinting,
                            "is_swimming" => entity.Swimming,
                            "is_flying" => entity.FlyingWithElytra,
                            "is_on_ground" => entity.MovementFlags.HasFlag(MovementFlags.OnGround),
                            _ => context.EntityIsBaby?.Invoke(entity) == true
                        };
                        if (value != flag.Value.GetBoolean())
                            return false;
                    }

                    break;
            }
        }

        return true;
    }

    private sealed class PredicateCondition(Func<LootContext, bool> test) : ILootCondition
    {
        public bool Test(LootContext context) => test(context);
    }

    internal static int EnchantmentLevel(ItemStack? tool, string identifier)
    {
        var enchantment = EnchantmentsRegistry.All.FirstOrDefault(value => value.Identifier == identifier);
        if (tool is null || tool.Count <= 0 || enchantment is null)
            return 0;

        // Stored enchantments on a book aren't active tool enchantments.
        return tool.Enchantments.FirstOrDefault(value => value.Id == enchantment.Id).Level;
    }

    internal static bool MatchesTool(ItemStack? tool, JsonElement predicate)
    {
        if (tool is null || tool.Count <= 0 || tool.Holder == ItemsRegistry.Air)
            return false;

        foreach (var property in predicate.EnumerateObject())
        {
            switch (property.Name)
            {
                case "items":
                    if (!MatchesRegistrySet(property.Value, "item", tool.Holder.UnlocalizedName, tool.Holder.Id))
                        return false;

                    break;
                case "count":
                    if (!MatchesRange(tool.Count, property.Value))
                        return false;

                    break;
                case "predicates":
                    foreach (var component in property.Value.EnumerateObject())
                    {
                        if (component.Name != "minecraft:enchantments")
                            throw new InvalidDataException($"Unsupported tool predicate {component.Name}.");

                        foreach (var enchantment in component.Value.EnumerateArray())
                        {
                            var ids = enchantment.GetProperty("enchantments");
                            var matches = EnchantmentsRegistry.All.Any(definition =>
                                MatchesRegistrySet(ids, "enchantment", definition.Identifier, definition.Id)
                                && EnchantmentLevel(tool, definition.Identifier) > 0
                                && (!enchantment.TryGetProperty("levels", out var levels)
                                    || MatchesRange(EnchantmentLevel(tool, definition.Identifier), levels)));
                            if (!matches)
                                return false;
                        }
                    }

                    break;
                default:
                    throw new InvalidDataException($"Unsupported tool predicate {property.Name}.");
            }
        }

        return true;
    }

    /// <summary>Tests an identifier, a list of identifiers, or a tag using the live registry tags.</summary>
    internal static bool MatchesRegistrySet(JsonElement set, string registry, string identifier, int id)
    {
        if (set.ValueKind == JsonValueKind.Array)
            return set.EnumerateArray().Any(value => MatchesRegistrySet(value, registry, identifier, id));

        var name = set.GetString()!;
        return name.StartsWith('#')
            ? TagsRegistry.Contains(registry, Qualify(name[1..]), id)
            : Qualify(name) == identifier;
    }

    /// <summary>Tests exact state values or inclusive numeric min/max ranges.</summary>
    internal static bool MatchesProperties(IReadOnlyDictionary<string, string> state, JsonElement properties) =>
        properties.EnumerateObject().All(property => state.TryGetValue(property.Name, out var value)
            && (property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() == value
                : int.TryParse(value, out var number) && MatchesRange(number, property.Value)));

    internal static bool MatchesRange(int value, JsonElement range) => range.ValueKind == JsonValueKind.Number
        ? value == (int)range.GetSingle()
        : (!range.TryGetProperty("min", out var min) || value >= ReadInteger(min))
            && (!range.TryGetProperty("max", out var max) || value <= ReadInteger(max));

    private static int ReadInteger(JsonElement value) => value.ValueKind == JsonValueKind.String
        ? int.Parse(value.GetString()!, System.Globalization.CultureInfo.InvariantCulture)
        : (int)value.GetSingle();
}

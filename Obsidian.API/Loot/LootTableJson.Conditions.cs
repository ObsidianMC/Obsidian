using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Data;
using Obsidian.API.Loot.Conditions;
using Obsidian.API.Loot.Numbers;
using Obsidian.API.Registries;
using Obsidian.API.Utilities;
using System.IO;
using System.Text.Json;

namespace Obsidian.API.Loot;

public static partial class LootTableJson
{
    private static ImmutableArray<ILootCondition> ReadConditions(JsonElement json)
    {
        ValidateContainerKeys(json);
        if (!json.TryGetProperty("condition", out var condition))
            return [];

        return [ReadCondition(condition)];
    }

    /// <summary>Reads a 26.3 predicate or a reference to a built-in predicate.</summary>
    public static ILootCondition ReadCondition(JsonElement json)
    {
        if (json.ValueKind == JsonValueKind.String)
        {
            var id = Qualify(json.GetString()!);
            return ContextNumberProviders.Predicates.TryGetValue(id, out var predicate)
                ? ReadCondition(predicate) : throw new InvalidDataException($"Unknown predicate {id}.");
        }

        var type = Qualify(Text(json, "type"));
        return type switch
        {
            "minecraft:all_of" or "minecraft:any_of" => Terms(
                ReadArray(json, "terms", ReadCondition), type == "minecraft:all_of"),
            "minecraft:inverted" => Invert(ReadCondition(json.GetProperty("term"))),
            "minecraft:survives_explosion" => new PredicateCondition(context =>
                context.ExplosionRadius is not float radius || context.Random.NextFloat() <= 1 / radius),
            "minecraft:random_chance" => Chance(ReadFloatNumber(json.GetProperty("chance"))),
            "minecraft:table_bonus" => TableBonus(json),
            "minecraft:match_tool" => ToolPredicate(json.GetProperty("predicate").Clone()),
            "minecraft:match_block" => StatePredicate(json.Clone()),
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
    {
        var threshold = chance.GetFloat(context);
        return context.Random.NextFloat() < threshold;
    });

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
                && property.Value.EnumerateObject().Any(value => value.Name is not ("minecraft:enchantments"
                    or "minecraft:stored_enchantments" or "minecraft:map_id" or "minecraft:dyed_color")))
                throw new InvalidDataException("Unsupported item component predicate.");
        }

        return new PredicateCondition(context => MatchesTool(context.Tool, predicate));
    }

    private static ILootCondition StatePredicate(JsonElement json) => new PredicateCondition(context =>
        context.BlockState is IBlock block
            && (!json.TryGetProperty("blocks", out var blocks)
                || MatchesRegistrySet(blocks, "block", block.UnlocalizedName, block.RegistryId))
            && (!json.TryGetProperty("state", out var properties)
                || MatchesProperties(context.BlockProperties, properties)));

    private static ILootCondition LocationPredicate(JsonElement json)
    {
        var predicate = json.GetProperty("predicate");
        if (predicate.TryGetProperty("biomes", out var biomes))
        {
            if (predicate.EnumerateObject().Any(value => value.Name != "biomes"))
                throw new InvalidDataException("Biome location checks support only biomes.");

            var set = biomes.Clone();
            return new PredicateCondition(context => MatchesOriginBiome(context, set));
        }

        if (predicate.EnumerateObject().Any(value => value.Name != "block"))
            throw new InvalidDataException("Only block predicates are supported by block loot location checks.");

        if (predicate.TryGetProperty("block", out var block)
            && block.EnumerateObject().Any(value => value.Name is not ("blocks" or "state")))
            throw new InvalidDataException("Location block predicates support blocks and state.");

        return new PredicateCondition(context => context.TestLocation?.Invoke(json) == true);
    }

    private static bool MatchesOriginBiome(LootContext context, JsonElement set)
    {
        if (context.Origin is not VectorD origin)
            return false;

        var name = context.OriginBiome?.Invoke(origin);
        if (name is null || !CodecRegistry.TryGetBiome(name, out var biome))
            return false;

        return MatchesRegistrySet(set, "worldgen/biome", name, biome.Id);
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
            switch (Qualify(property.Name))
            {
                case "minecraft:entity_type":
                    break;
                case "minecraft:vehicle":
                    ValidateEntityPredicate(property.Value);
                    break;
                case "minecraft:equipment":
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
                case "minecraft:flags":
                    foreach (var flag in property.Value.EnumerateObject())
                    {
                        if (flag.Name is not ("is_on_fire" or "is_sneaking" or "is_sprinting" or "is_swimming"
                            or "is_flying" or "is_on_ground" or "is_baby"))
                        {
                            throw new InvalidDataException($"Unsupported entity flag {flag.Name}.");
                        }
                    }

                    break;
                case "minecraft:type_specific/fishing_hook":
                    ValidateTypeSpecific(property.Value);
                    break;
                case "minecraft:predicates":
                    if (property.Value.EnumerateObject().Any(value => value.Name != "minecraft:villager/variant"))
                        throw new InvalidDataException("Entity component predicates support only villager/variant.");

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
            switch (Qualify(property.Name))
            {
                case "minecraft:entity_type":
                    var id = entity.Definition?.Id ?? (int)(entity.Type ?? (EntityType)(-1));
                    var name = entity.Definition?.Identifier ?? "minecraft:" + entity.Type?.ToString().ToSnakeCase();
                    if (!MatchesRegistrySet(property.Value, "entity_type", name, id))
                        return false;

                    break;
                case "minecraft:type_specific/fishing_hook":
                    if (!MatchesTypeSpecific(entity, property.Value, context))
                        return false;

                    break;
                case "minecraft:vehicle":
                    if (!MatchesEntity(entity.Vehicle, property.Value, context))
                        return false;

                    break;
                case "minecraft:predicates":
                    var variant = context.EntityVillagerVariant?.Invoke(entity);
                    var variants = property.Value.GetProperty("minecraft:villager/variant");
                    if (variant is null || !MatchesIdentifiers(variants, variant))
                        return false;

                    break;
                case "minecraft:equipment":
                    foreach (var slot in property.Value.EnumerateObject())
                    {
                        if (!MatchesTool(context.EntityEquipment?.Invoke(entity, slot.Name), slot.Value))
                            return false;
                    }

                    break;
                case "minecraft:flags":
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

    // Vanilla's fishing_hook type-specific predicate: only the hook's open-water state is modelled.
    private static void ValidateTypeSpecific(JsonElement predicate)
    {
        foreach (var property in predicate.EnumerateObject())
        {
            if (property.Name != "in_open_water")
                throw new InvalidDataException($"Unsupported fishing_hook predicate {property.Name}.");
        }
    }

    private static bool MatchesTypeSpecific(IEntity? entity, JsonElement predicate, LootContext context)
    {
        var open = entity is null ? null : context.EntityFishingOpenWater?.Invoke(entity);
        if (open is null)
            return false;

        return !predicate.TryGetProperty("in_open_water", out var expected) || open == expected.GetBoolean();
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
                        if (!MatchesComponent(tool, component))
                            return false;
                    }

                    break;
                default:
                    throw new InvalidDataException($"Unsupported tool predicate {property.Name}.");
            }
        }

        return true;
    }

    private static bool MatchesComponent(ItemStack tool, JsonProperty component)
    {
        switch (component.Name)
        {
            case "minecraft:map_id":
                if (component.Value.EnumerateObject().Any())
                    throw new InvalidDataException("Only map_id presence checks are supported.");

                return tool.ContainsKey(DataComponentType.MapId);
            case "minecraft:dyed_color":
                if (component.Value.EnumerateObject().Any())
                    throw new InvalidDataException("Only dyed_color presence checks are supported.");

                return tool.ContainsKey(DataComponentType.DyedColor);
            case "minecraft:enchantments":
                return MatchesEnchantments(tool.Enchantments, component.Value);
            case "minecraft:stored_enchantments":
                var stored = tool.GetComponent<SimpleDataComponent<ImmutableArray<Enchantment>>>(
                    DataComponentType.StoredEnchantments)?.Value ?? [];
                return MatchesEnchantments(stored, component.Value);
            default:
                throw new InvalidDataException($"Unsupported tool predicate {component.Name}.");
        }
    }

    // Each entry must match one of the enchantments: one of its ids (any id when absent) at a level in its range (any
    // level when absent). So [{}] matches any enchanted stack.
    private static bool MatchesEnchantments(IEnumerable<Enchantment> enchantments, JsonElement predicates)
    {
        var present = enchantments.Where(enchantment => enchantment.Level > 0).ToList();
        foreach (var predicate in predicates.EnumerateArray())
        {
            var matches = present.Any(enchantment =>
            {
                var definition = EnchantmentsRegistry.All[enchantment.Id];
                var idMatches = !predicate.TryGetProperty("enchantments", out var ids)
                    || MatchesRegistrySet(ids, "enchantment", definition.Identifier, definition.Id);
                var levelMatches = !predicate.TryGetProperty("levels", out var levels)
                    || MatchesRange(enchantment.Level, levels);
                return idMatches && levelMatches;
            });
            if (!matches)
                return false;
        }

        return true;
    }

    // An identifier or a list of identifiers, for registries without tags here.
    private static bool MatchesIdentifiers(JsonElement set, string identifier)
    {
        if (set.ValueKind == JsonValueKind.Array)
            return set.EnumerateArray().Any(value => Qualify(value.GetString()!) == identifier);

        return Qualify(set.GetString()!) == identifier;
    }

    private static IEnumerable<string> RegistrySetIds(JsonElement set, string registry)
    {
        if (set.ValueKind == JsonValueKind.Array)
            return set.EnumerateArray().SelectMany(value => RegistrySetIds(value, registry));

        var name = set.GetString()!;
        if (!name.StartsWith('#'))
            return [Qualify(name)];

        if (!TagsRegistry.TryGet(registry, Qualify(name[1..]), out var tag))
            throw new InvalidDataException($"Unknown {registry} tag {name}.");

        return tag.Entries.Select(id => registry == "item" ? ItemsRegistry.Get(id).UnlocalizedName
            : throw new InvalidDataException($"Unsupported registry set {registry}."));
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

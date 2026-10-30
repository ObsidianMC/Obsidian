using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Loot.Functions;
using Obsidian.API.Registries;
using Obsidian.API.Utilities;
using System.IO;
using System.Text.Json;

namespace Obsidian.API.Loot;

public static partial class LootTableJson
{
    private static ImmutableArray<LootFunction> ReadFunctions(JsonElement json)
    {
        if (!json.TryGetProperty("modifier", out var modifier))
            return [];

        return ReadModifiers(modifier);
    }

    private static ImmutableArray<LootFunction> ReadModifiers(JsonElement json)
    {
        if (json.ValueKind == JsonValueKind.Array)
            return json.EnumerateArray().Select(ReadFunction).ToImmutableArray();

        return [ReadFunction(json)];
    }

    /// <summary>Reads a modifier or modifier sequence for generated tables and plugins.</summary>
    public static LootFunction ReadModifier(string json)
    {
        using var document = JsonDocument.Parse(json);
        var functions = ReadModifiers(document.RootElement);
        return new DataFunction((stack, context) => LootFunction.ApplyAll(functions, stack, context));
    }

    // The enchantments an enchant_with_levels function picks from. The generator expands tags before the runtime
    // reads a gameplay table, so options are ids; every enchantment when absent.
    private static ImmutableArray<EnchantmentDefinition>? ReadEnchantmentOptions(JsonElement json)
    {
        if (!json.TryGetProperty("options", out var options))
            return null;

        var ids = options.ValueKind == JsonValueKind.Array
            ? options.EnumerateArray().Select(id => id.GetString()!)
            : [options.GetString()!];
        return ids.Select(id => EnchantmentsRegistry.All.FirstOrDefault(definition => definition.Identifier == Qualify(id))
            ?? throw new InvalidDataException($"Unknown enchantment option {id}.")).ToImmutableArray();
    }

    private static LootFunction ReadFunction(JsonElement json)
    {
        if (json.ValueKind == JsonValueKind.Array)
        {
            var functions = ReadModifiers(json);
            return new DataFunction((stack, context) => LootFunction.ApplyAll(functions, stack, context));
        }

        var type = Qualify(Text(json, "type"));
        var conditions = ReadConditions(json);
        switch (type)
        {
            case "minecraft:discard":
                return new DataFunction((_, _) => new ItemStack(ItemsRegistry.Air, 0))
                {
                    Conditions = conditions
                };
            case "minecraft:filtered":
                var filterJson = json.GetProperty("item_filter").Clone();
                _ = ToolPredicate(filterJson);
                var onPass = ReadOptionalModifiers(json, "on_pass");
                var onFail = ReadOptionalModifiers(json, "on_fail");
                return new DataFunction((stack, context) =>
                {
                    var functions = MatchesTool(stack, filterJson) ? onPass : onFail;
                    return LootFunction.ApplyAll(functions, stack, context);
                }) { Conditions = conditions };
            case "minecraft:set_count":
                return new SetCountFunction
                {
                    Conditions = conditions,
                    Count = ReadIntNumber(json.GetProperty("count")),
                    Add = json.TryGetProperty("add", out var add) && add.GetBoolean()
                };
            case "minecraft:furnace_smelt":
                // SmeltItem supplies one recipe result; vanilla preserves the input count, without random draws.
                return new DataFunction((stack, context) =>
                {
                    if (stack.Count <= 0 || context.SmeltItem?.Invoke(stack) is not ItemStack result)
                        return stack;

                    return new ItemStack(result, stack.Count);
                }) { Conditions = conditions };
            case "minecraft:set_damage":
                return new SetDamageFunction
                {
                    Conditions = conditions,
                    Damage = ReadFloatNumber(json.GetProperty("damage")),
                    Add = json.TryGetProperty("add", out var addDamage) && addDamage.GetBoolean()
                };
            case "minecraft:enchant_with_levels":
                return new EnchantWithLevelsFunction
                {
                    Conditions = conditions,
                    Levels = ReadIntNumber(json.GetProperty("levels")),
                    Options = ReadEnchantmentOptions(json)
                };
            case "minecraft:set_potion":
                return new SetPotionFunction
                {
                    Conditions = conditions,
                    Id = Enum.Parse<Potion>(Text(json, "id").Split(':')[^1].Replace("_", ""), true)
                };
            case "minecraft:explosion_decay":
                return new DataFunction(Decay) { Conditions = conditions };
            case "minecraft:apply_bonus":
                return Bonus(json, conditions);
            case "minecraft:enchanted_count_increase":
                return EnchantedCountIncrease(json, conditions);
            case "minecraft:limit_count":
                var limit = json.GetProperty("limit");
                var min = limit.ValueKind == JsonValueKind.Number ? CountLimit(limit)
                    : limit.TryGetProperty("min", out var minimum) ? CountLimit(minimum) : 0;
                var max = limit.ValueKind == JsonValueKind.Number ? CountLimit(limit)
                    : limit.TryGetProperty("max", out var maximum) ? CountLimit(maximum) : int.MaxValue;
                if (min > max)
                    throw new InvalidDataException("limit_count requires min <= max.");

                return new DataFunction((stack, _) =>
                {
                    stack.Count = Math.Clamp(stack.Count, min, max);
                    return stack;
                }) { Conditions = conditions };
            case "minecraft:copy_state":
                var block = Qualify(Text(json, "block"));
                var properties = json.GetProperty("properties").EnumerateArray()
                    .Select(value => value.GetString()!).ToArray();
                return new DataFunction((stack, context) => CopyState(stack, context, block, properties))
                {
                    Conditions = conditions
                };
            case "minecraft:copy_components":
                if (Text(json, "source") != "block_entity")
                    throw new InvalidDataException("copy_components requires the block_entity source.");

                var include = ReadArray(json, "include", value => Qualify(value.GetString()!));
                var includesAll = !json.TryGetProperty("include", out _);
                var exclude = ReadArray(json, "exclude", value => Qualify(value.GetString()!));
                return new DataFunction((stack, context) =>
                {
                    foreach (var component in context.BlockEntityComponents)
                    {
                        if ((includesAll || include.Contains(component.Identifier))
                            && !exclude.Contains(component.Identifier))
                            stack[component.Type] = component;
                    }

                    return stack;
                }) { Conditions = conditions };
            default:
                throw new InvalidDataException($"Unsupported loot function {type}.");
        }
    }

    private static ImmutableArray<LootFunction> ReadOptionalModifiers(JsonElement json, string name)
    {
        if (!json.TryGetProperty(name, out var modifier))
            return [];

        return ReadModifiers(modifier);
    }

    private static int CountLimit(JsonElement value)
    {
        var number = value.GetDouble();
        if (!double.IsFinite(number) || number < int.MinValue || number > int.MaxValue)
            throw new InvalidDataException("limit_count requires finite limits in the integer range.");

        return (int)number;
    }

    private static LootFunction Bonus(JsonElement json,
        ImmutableArray<Conditions.ILootCondition> conditions)
    {
        var enchantment = Qualify(Text(json, "enchantment"));
        var formula = Qualify(Text(json, "formula"));
        if (formula is not ("minecraft:ore_drops" or "minecraft:uniform_bonus_count"
            or "minecraft:binomial_with_bonus_count"))
            throw new InvalidDataException($"Unsupported bonus formula {formula}.");

        var parameters = json.TryGetProperty("parameters", out var value) ? value.Clone() : default;
        var multiplier = formula == "minecraft:uniform_bonus_count"
            ? parameters.GetProperty("bonusMultiplier").GetInt32() : 0;
        var extra = formula == "minecraft:binomial_with_bonus_count" ? parameters.GetProperty("extra").GetInt32() : 0;
        var probability = formula == "minecraft:binomial_with_bonus_count"
            ? parameters.GetProperty("probability").GetSingle() : 0;

        return new DataFunction((stack, context) =>
        {
            // ApplyBonusCount skips the formula entirely when the TOOL parameter is absent. With a tool, even
            // level zero draws for uniform and binomial bonuses; ore_drops draws only with a positive level.
            if (context.Tool is null)
                return stack;

            var level = EnchantmentLevel(context.Tool, enchantment);
            switch (formula)
            {
                case "minecraft:ore_drops":
                    if (level > 0)
                        stack.Count *= Math.Max(0, context.Random.NextInt(level + 2) - 1) + 1;

                    break;
                case "minecraft:uniform_bonus_count":
                    stack.Count += context.Random.NextInt(multiplier * level + 1);
                    break;
                case "minecraft:binomial_with_bonus_count":
                    for (var i = 0; i < level + extra; i++)
                    {
                        if (context.Random.NextFloat() < probability)
                            stack.Count++;
                    }

                    break;
            }

            return stack;
        }) { Conditions = conditions };
    }

    // Vanilla's EnchantedCountIncreaseFunction, looting's extra drops: with the attacker's enchantment level above 0,
    // the count grows by the level times the drawn count, rounded, then is cut to the limit if there's one.
    private static LootFunction EnchantedCountIncrease(JsonElement json,
        ImmutableArray<Conditions.ILootCondition> conditions)
    {
        var enchantment = Qualify(Text(json, "enchantment"));
        var count = ReadFloatNumber(json.GetProperty("count"));
        var limit = json.TryGetProperty("limit", out var value) ? value.GetInt32() : 0;

        return new DataFunction((stack, context) =>
        {
            if (context.AttackingEntity is null)
                return stack;

            var level = context.AttackerEnchantmentLevel(enchantment);
            if (level == 0)
                return stack;

            // Java's Math.round: half up.
            stack.Count += (int)MathF.Floor(level * count.GetFloat(context) + 0.5f);

            if (limit > 0)
                stack.Count = Math.Min(stack.Count, limit);

            return stack;
        }) { Conditions = conditions };
    }

    private static ItemStack Decay(ItemStack stack, LootContext context)
    {
        if (context.ExplosionRadius is not float radius)
            return stack;

        // Vanilla samples once for every item, including items later discarded, in stack order.
        var survivors = 0;
        for (var i = 0; i < stack.Count; i++)
        {
            if (context.Random.NextFloat() <= 1 / radius)
                survivors++;
        }

        stack.Count = survivors;
        return stack;
    }

    private static ItemStack CopyState(ItemStack stack, LootContext context, string block, string[] properties)
    {
        if (context.BlockState?.UnlocalizedName != block)
            return stack;

        var values = stack.GetComponent<SimpleDataComponent<ImmutableArray<BlockStateProperty>>>(
            DataComponentType.BlockState)?.Value.ToDictionary(value => value.Name, value => value.Value) ?? [];
        foreach (var property in properties)
        {
            if (context.BlockProperties.TryGetValue(property, out var value))
                values[property] = value;
        }

        stack[DataComponentType.BlockState] = ComponentBuilder.BlockState with
        {
            Value = values.Select(value => new BlockStateProperty
            {
                Name = value.Key,
                Value = value.Value
            }).ToImmutableArray()
        };
        return stack;
    }

    private sealed class DataFunction(Func<ItemStack, LootContext, ItemStack> run) : LootFunction
    {
        protected override ItemStack Run(ItemStack stack, LootContext context) => run(stack, context);
    }
}

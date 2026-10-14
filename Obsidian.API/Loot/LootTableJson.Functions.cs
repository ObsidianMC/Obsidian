using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Loot.Functions;
using System.IO;
using System.Text.Json;

namespace Obsidian.API.Loot;

public static partial class LootTableJson
{
    private static ImmutableArray<LootFunction> ReadFunctions(JsonElement json) =>
        ReadArray(json, "functions", ReadFunction);

    private static LootFunction ReadFunction(JsonElement json)
    {
        var type = Qualify(Text(json, "function"));
        var conditions = ReadConditions(json);
        switch (type)
        {
            case "minecraft:set_count":
                return new SetCountFunction
                {
                    Conditions = conditions,
                    Count = ReadNumber(json.GetProperty("count")),
                    Add = json.TryGetProperty("add", out var add) && add.GetBoolean()
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
        var count = ReadNumber(json.GetProperty("count"));
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

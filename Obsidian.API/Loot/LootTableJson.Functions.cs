using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Loot.Functions;
using Obsidian.API.Registries;
using Obsidian.API.Utilities;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

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

    private static JsonElement? ReadOptionsSet(JsonElement json) =>
        json.TryGetProperty("options", out var options) ? options.Clone() : null;

    // Holder sets keep their tags until evaluation so runtime-loaded tables see the current data pack tags.
    internal static ImmutableArray<EnchantmentDefinition>? ReadEnchantmentOptions(JsonElement? options)
    {
        if (options is not JsonElement set)
            return null;

        return RegistrySetIds(set, "enchantment")
            .Select(id => EnchantmentsRegistry.All.FirstOrDefault(definition => definition.Identifier == id)
                ?? throw new InvalidDataException($"Unknown enchantment option {id}."))
            .ToImmutableArray();
    }

    internal static ImmutableArray<InstrumentDefinition>? ReadInstrumentOptions(JsonElement? options)
    {
        if (options is not JsonElement set)
            return null;

        return RegistrySetIds(set, "instrument")
            .Select(id => InstrumentsRegistry.All.FirstOrDefault(definition => definition.Identifier == id)
                ?? throw new InvalidDataException($"Unknown instrument option {id}."))
            .ToImmutableArray();
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
                    OptionsSet = ReadOptionsSet(json),
                    IncludeAdditionalCostComponent = ReadFlag(json, "include_additional_cost_component", false)
                };
            case "minecraft:enchant_randomly":
                return new EnchantRandomlyFunction
                {
                    Conditions = conditions,
                    OptionsSet = ReadOptionsSet(json),
                    OnlyCompatible = ReadFlag(json, "only_compatible", true),
                    IncludeAdditionalCostComponent = ReadFlag(json, "include_additional_cost_component", false)
                };
            case "minecraft:exploration_map":
                return ReadExplorationMap(json, conditions);
            case "minecraft:set_name":
                return new SetNameFunction
                {
                    Conditions = conditions,
                    Name = json.TryGetProperty("name", out var name) ? ReadText(name) : null,
                    Target = ReadNameTarget(json)
                };
            case "minecraft:set_lore":
                var lore = ReadArray(json, "lore", ReadText);
                var operation = ReadLoreOperation(json);
                return new DataFunction((stack, _) => SetLore(stack, lore, operation))
                {
                    Conditions = conditions
                };
            case "minecraft:set_components":
                var (components, removed) = Extensions.ComponentPatchFromJson(json.GetProperty("components"));
                return new DataFunction((stack, _) => SetComponents(stack, components, removed))
                {
                    Conditions = conditions
                };
            case "minecraft:set_instrument":
                var instruments = json.GetProperty("options").Clone();
                _ = ReadInstrumentOptions(instruments);
                return new SetInstrumentFunction
                {
                    Conditions = conditions,
                    OptionsSet = instruments
                };
            case "minecraft:set_stew_effect":
                return new SetStewEffectFunction
                {
                    Conditions = conditions,
                    Effects = ReadArray(json, "effects", ReadStewEffect)
                };
            case "minecraft:set_random_dyes":
                return RandomDyes(json, conditions);
            case "minecraft:set_random_potion":
                return RandomPotion(json, conditions);
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
                        {
                            stack[component.Type] = component;
                        }
                    }

                    return stack;
                }) { Conditions = conditions };
            default:
                throw new InvalidDataException($"Unsupported loot function {type}.");
        }
    }

    // A text component as data packs write it, read like a saved one: vanilla's text codec reads either.
    private static ChatMessage ReadText(JsonElement json)
    {
        var text = JsonSerializer.SerializeToElement(Homogeneous(json));

        return JsonNbt.ToNbt(string.Empty, text).TextFromNbt()
            ?? throw new InvalidDataException($"{json.GetRawText()} isn't a text component.");
    }

    // Vanilla's text codec reads a string, an object or a list of texts at any depth, a list's first text holding the
    // rest as its extras, which may mix strings and objects. An NBT list holds one type, so every text in a list (and
    // in an object's extra or with) becomes an object.
    private static JsonNode? Homogeneous(JsonElement text)
    {
        if (text.ValueKind == JsonValueKind.Array)
            return new JsonArray([.. text.EnumerateArray().Select(TextObject)]);

        if (text.ValueKind != JsonValueKind.Object)
            return JsonNode.Parse(text.GetRawText());

        var result = new JsonObject();

        foreach (var property in text.EnumerateObject())
        {
            var isTextList = property.Name is "extra" or "with" && property.Value.ValueKind == JsonValueKind.Array;
            result[property.Name] = isTextList ? Homogeneous(property.Value) : JsonNode.Parse(property.Value.GetRawText());
        }

        return result;
    }

    // A text in a list as an object: a string or a translation's number argument as plain text, and a nested list as
    // an empty text with the list as its extras.
    private static JsonNode? TextObject(JsonElement text) => text.ValueKind switch
    {
        JsonValueKind.Object => Homogeneous(text),
        JsonValueKind.Array => new JsonObject { ["text"] = string.Empty, ["extra"] = Homogeneous(text) },
        JsonValueKind.String => new JsonObject { ["text"] = text.GetString() },
        _ => new JsonObject { ["text"] = text.GetRawText() }
    };

    private static NameTarget ReadNameTarget(JsonElement json)
    {
        if (!json.TryGetProperty("target", out var target))
            return NameTarget.CustomName;

        return target.GetString() switch
        {
            "custom_name" => NameTarget.CustomName,
            "item_name" => NameTarget.ItemName,
            var other => throw new InvalidDataException($"Unknown set_name target {other}.")
        };
    }

    // Vanilla's ListOperation for set_lore's "mode": where the new lines go in the stack's lore.
    private sealed record LoreOperation(string Mode, int Offset, int? Size);

    private static LoreOperation ReadLoreOperation(JsonElement json)
    {
        var mode = Text(json, "mode");
        if (mode is not ("append" or "insert" or "replace_all" or "replace_section"))
            throw new InvalidDataException($"Unknown set_lore mode {mode}.");

        var offset = json.TryGetProperty("offset", out var offsetJson) ? offsetJson.GetInt32() : 0;
        int? size = json.TryGetProperty("size", out var sizeJson) ? sizeJson.GetInt32() : null;

        // Vanilla's list operations take only offsets and sizes from 0.
        if (offset < 0 || size < 0)
            throw new InvalidDataException("set_lore's offset and size can't be negative.");

        return new LoreOperation(mode, offset, size);
    }

    // Like vanilla's list operations, an offset past the lore's end, or lore longer than its 256 lines, keeps the
    // lore as it was.
    private static ItemStack SetLore(ItemStack stack, ImmutableArray<ChatMessage> lines, LoreOperation operation)
    {
        const int maxLines = 256;

        var lore = stack.Lore;
        var offset = operation.Offset;
        if (operation.Mode is "insert" or "replace_section" && offset > lore.Count)
            return stack;

        // A replaced section is as long as the new lines unless the operation gives its size.
        var replacedEnd = offset + (operation.Size ?? lines.Length);

        List<ChatMessage> result = operation.Mode switch
        {
            "append" => [.. lore, .. lines],
            "insert" => [.. lore.Take(offset), .. lines, .. lore.Skip(offset)],
            "replace_section" => [.. lore.Take(offset), .. lines, .. lore.Skip(replacedEnd)],
            _ => [.. lines]
        };

        if (result.Count > maxLines)
            return stack;

        LootItems.Set(stack, ComponentBuilder.Lore with { Value = [.. result] });
        return stack;
    }

    // Vanilla's set_components applies its patch to the stack: each component it sets replaces the stack's, and each
    // one it removes is gone. Like vanilla's applyComponentsAndValidate, a patch leaving the stack invalid (a
    // damageable item made stackable) is undone. Components are immutable, so stacks can share them.
    private static ItemStack SetComponents(
        ItemStack stack,
        ImmutableArray<DataComponent> components,
        ImmutableArray<DataComponentType> removed)
    {
        var patched = new ItemStack(stack, stack.Count);

        foreach (var component in components)
            LootItems.Set(patched, component);

        foreach (var type in removed)
            patched.Remove(type);

        return ItemStackValidation.IsValid(patched) ? patched : stack;
    }

    private static bool ReadFlag(JsonElement json, string name, bool fallback) =>
        json.TryGetProperty(name, out var value) ? value.GetBoolean() : fallback;

    // Absent fields keep the function's defaults. A structure tag destination may be written with or without its '#'.
    private static ExplorationMapFunction ReadExplorationMap(JsonElement json,
        ImmutableArray<Conditions.ILootCondition> conditions)
    {
        var defaults = new ExplorationMapFunction();
        var destination = json.TryGetProperty("destination", out var tag)
            ? Qualify(tag.GetString()!.TrimStart('#'))
            : defaults.Destination;
        var decoration = json.TryGetProperty("decoration", out var marker)
            ? Qualify(marker.GetString()!)
            : defaults.Decoration;

        return new ExplorationMapFunction
        {
            Conditions = conditions,
            Destination = destination,
            Decoration = decoration,
            Zoom = json.TryGetProperty("zoom", out var zoom) ? zoom.GetInt32() : defaults.Zoom,
            SearchRadius = json.TryGetProperty("search_radius", out var radius)
                ? radius.GetInt32()
                : defaults.SearchRadius,
            SkipExistingChunks = ReadFlag(json, "skip_existing_chunks", defaults.SkipExistingChunks)
        };
    }

    private static StewEffect ReadStewEffect(JsonElement json) => new()
    {
        Type = Enum.Parse<MobEffect>(Text(json, "type").Split(':')[^1].Replace("_", ""), true),
        Duration = ReadIntNumber(json.GetProperty("duration"))
    };

    // Vanilla's SetRandomDyesFunction: the drawn number of dyes, each a random dye color, mixed into the stack's
    // color like crafting dyed armor does. The result is a single item.
    private static LootFunction RandomDyes(JsonElement json, ImmutableArray<Conditions.ILootCondition> conditions)
    {
        var count = ReadIntNumber(json.GetProperty("number_of_dyes"));
        return new DataFunction((stack, context) =>
        {
            var dyes = count.GetInt(context);
            if (dyes <= 0)
                return stack;

            var colors = new List<int>(dyes);
            for (var i = 0; i < dyes; i++)
            {
                colors.Add(DyeColors.Diffuse[context.Random.NextInt(DyeColors.Diffuse.Length)]);
            }

            var current = stack.GetComponent<SimpleDataComponent<int>>(DataComponentType.DyedColor)?.Value;
            var dyed = new ItemStack(stack, 1);
            LootItems.Set(dyed, ComponentBuilder.DyedColor with { Value = DyeColors.Mix(current, colors) });
            return dyed;
        }) { Conditions = conditions };
    }

    // Vanilla's SetRandomPotionFunction: a random potion of the options (every potion when absent) replaces the
    // stack's potion, keeping its other potion contents.
    private static LootFunction RandomPotion(JsonElement json, ImmutableArray<Conditions.ILootCondition> conditions)
    {
        var set = ReadOptionsSet(json);
        return new DataFunction((stack, context) =>
        {
            var options = set is JsonElement ids
                ? RegistrySetIds(ids, "potion").Select(ParsePotion).ToImmutableArray()
                : [.. Enum.GetValues<Potion>()];
            if (options.IsEmpty)
                return stack;

            var potion = options[context.Random.NextInt(options.Length)];
            var contents = stack.GetComponent<PotionContentsDataComponent>(DataComponentType.PotionContents);
            var updated = contents is null
                ? new PotionContentsDataComponent { Potion = potion }
                : contents with { Potion = potion };
            LootItems.Set(stack, updated);
            return stack;
        }) { Conditions = conditions };
    }

    private static Potion ParsePotion(string id) => Enum.Parse<Potion>(id.Split(':')[^1].Replace("_", ""), true);

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
        {
            throw new InvalidDataException($"Unsupported bonus formula {formula}.");
        }

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

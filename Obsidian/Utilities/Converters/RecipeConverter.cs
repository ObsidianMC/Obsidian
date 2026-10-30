using Obsidian.API.Crafting;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Utilities;
using Obsidian.Nbt;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Obsidian.Utilities.Converters;
public sealed class RecipesConverter : JsonConverter<IRecipe[]>
{
    public override IRecipe[]? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var recipes = new List<IRecipe>();

        var element = JsonDocument.ParseValue(ref reader).RootElement;

        foreach (var recipeElement in element.EnumerateObject())
        {
            var recipeName = recipeElement.Name;
            var value = recipeElement.Value;

            var typeId = value.GetProperty("type").GetString()!;
            var type = typeId.TrimResourceTag();
            var group = value.TryGetProperty("group", out var groupValue) ? groupValue.GetString()! : string.Empty;

            if (!Enum.TryParse<CraftingType>(type, true, out var craftingType))
                throw new JsonException($"Unknown recipe type '{type}' in {recipeName}.");

            if (craftingType == CraftingType.SmithingTrim)
            {
                recipes.Add(new SmithingTrimRecipe
                {
                    Identifier = recipeName,
                    Type = craftingType,
                    Group = group,
                    Template = value.GetProperty("template").Deserialize<Ingredient>(options)!,
                    Addition = value.GetProperty("addition").Deserialize<Ingredient>(options)!,
                    Base = value.GetProperty("base").Deserialize<Ingredient>(options)!,
                    Pattern = value.TryGetProperty("pattern", out var pattern) ? pattern.GetString() : null
                });
                continue;
            }

            if (craftingType == CraftingType.Brewing)
            {
                recipes.Add(new BrewingRecipe
                {
                    Identifier = recipeName,
                    Type = craftingType,
                    Group = group,
                    Input = ReadPotionIngredient(value.GetProperty("input"), options),
                    Reagent = ReadPotionIngredient(value.GetProperty("reagent"), options),
                    Output = ReadBrewingOutput(value.GetProperty("output"))
                });
                continue;
            }

            if (craftingType is CraftingType.CraftingDye
                or CraftingType.CraftingImbue
                or CraftingType.CraftingTransmute)
            {
                recipes.Add(ReadConfigurableSpecial(recipeName, craftingType, group, value, options));
                continue;
            }

            // The older special recipes are built in code. Since 26.1 most of them name a result too, which isn't
            // always an item (map extending's is empty), so only the types below read one.
            if (craftingType is not (CraftingType.CraftingShaped or CraftingType.CraftingShapeless or CraftingType.Blasting
                or CraftingType.Smelting or CraftingType.Smoking or CraftingType.CampfireCooking
                or CraftingType.Stonecutting or CraftingType.SmithingTransform))
            {
                continue;
            }

            var result = value.GetProperty("result").Deserialize<Ingredient>(options)!;

            switch (craftingType)
            {
                case CraftingType.CraftingShaped:
                    recipes.Add(new ShapedRecipe
                    {
                        Identifier = recipeName,
                        Type = craftingType,
                        Group = group,
                        Category = CraftingCategory(value),
                        Key = value.GetProperty("key").Deserialize<Dictionary<char, Ingredient>>(options)!.AsReadOnly(),
                        Pattern = value.GetProperty("pattern").Deserialize<string[]>(options)!.AsReadOnly(),
                        Result = result
                    });
                    break;
                case CraftingType.CraftingShapeless:
                    recipes.Add(new ShapelessRecipe
                    {
                        Identifier = recipeName,
                        Type = craftingType,
                        Group = group,
                        Category = CraftingCategory(value),
                        Ingredients = value.GetProperty("ingredients").Deserialize<Ingredient[]>(options)!,
                        Result = result
                    });
                    break;
                case CraftingType.Blasting:
                case CraftingType.Smelting:
                case CraftingType.Smoking:
                case CraftingType.CampfireCooking:
                    recipes.Add(new SmeltingRecipe
                    {
                        Identifier = recipeName,
                        Type = craftingType,
                        Group = group,
                        Category = value.TryGetProperty("category", out var cooking)
                            ? Enum.Parse<CookingBookCategory>(cooking.GetString()!, true)
                            : CookingBookCategory.Misc,
                        CookingTime = value.GetProperty("cookingtime").GetInt32(),
                        Experience = value.TryGetProperty("experience", out var experience) ? experience.GetSingle() : 0f,
                        Ingredient = value.GetProperty("ingredient").Deserialize<Ingredient>(options)!,
                        Result = result
                    });
                    break;
                case CraftingType.Stonecutting:
                    var ingredient = value.GetProperty("ingredient");
                    recipes.Add(new CuttingRecipe
                    {
                        Identifier = recipeName,
                        Type = craftingType,
                        Group = group,
                        Ingredient = ingredient.Deserialize<Ingredient>(options)!,
                        Count = result.Count,//TODO the recipe format more or less changed look into this.
                        Result = result
                    });
                    break;
                case CraftingType.SmithingTransform:
                    recipes.Add(new SmithingTransformRecipe
                    {
                        Identifier = recipeName,
                        Type = craftingType,
                        Group = group,
                        Template = value.GetProperty("template").Deserialize<Ingredient>(options)!,
                        Addition = value.GetProperty("addition").Deserialize<Ingredient>(options)!,
                        Base = value.GetProperty("base").Deserialize<Ingredient>(options)!,
                        Result = result
                    });
                    break;
                default:
                    break;
            }
        }

        return recipes.ToArray();
    }

    // 26.1's configurable special crafting recipes, which name their ingredients and result like other recipes.
    private static ICraftingRecipe ReadConfigurableSpecial(
        string identifier,
        CraftingType type,
        string group,
        JsonElement value,
        JsonSerializerOptions options)
    {
        var category = CraftingCategory(value);

        switch (type)
        {
            case CraftingType.CraftingDye:
                return new DyeRecipe
                {
                    Identifier = identifier,
                    Type = type,
                    Group = group,
                    Category = category,
                    Target = value.GetProperty("target").Deserialize<Ingredient>(options)!,
                    Dye = value.GetProperty("dye").Deserialize<Ingredient>(options)!,
                    Result = ReadResultStack(value.GetProperty("result"))
                };
            case CraftingType.CraftingImbue:
                return new ImbueRecipe
                {
                    Identifier = identifier,
                    Type = type,
                    Group = group,
                    Category = category,
                    Source = value.GetProperty("source").Deserialize<Ingredient>(options)!,
                    Material = value.GetProperty("material").Deserialize<Ingredient>(options)!,
                    Result = ReadResultStack(value.GetProperty("result"))
                };
            default:
                return ReadTransmute(identifier, type, group, category, value, options);
        }
    }

    private static TransmuteRecipe ReadTransmute(
        string identifier,
        CraftingType type,
        string group,
        CraftingBookCategory category,
        JsonElement value,
        JsonSerializerOptions options)
    {
        var (min, max) = ReadMaterialCount(value);
        var (resultItem, resultCount, resultTemplate) = ReadTransmuteResult(value.GetProperty("result"));
        var addMaterialCount = value.TryGetProperty("add_material_count_to_result", out var add) && add.GetBoolean();

        return new TransmuteRecipe
        {
            Identifier = identifier,
            Type = type,
            Group = group,
            Category = category,
            Input = value.GetProperty("input").Deserialize<Ingredient>(options)!,
            Material = value.GetProperty("material").Deserialize<Ingredient>(options)!,
            MinMaterialCount = min,
            MaxMaterialCount = max,
            ResultItem = resultItem,
            ResultCount = resultCount,
            AddMaterialCountToResult = addMaterialCount,
            ResultComponents = [.. resultTemplate.Patch],
            RemovedResultComponents = [.. resultTemplate.RemoveComponents]
        };
    }

    // A transmute recipe's material count: exactly 1 by default, else exact (a number) or a range whose missing ends
    // are 1 and 8.
    private static (int Min, int Max) ReadMaterialCount(JsonElement recipe)
    {
        if (!recipe.TryGetProperty("material_count", out var count))
            return (1, 1);

        var bounds = count.ValueKind == JsonValueKind.Number
            ? (Min: count.GetInt32(), Max: count.GetInt32())
            : (Min: OptionalInt(count, "min", 1), Max: OptionalInt(count, "max", 8));

        if (bounds.Min < 1 || bounds.Max > 8 || bounds.Min > bounds.Max)
            throw new JsonException($"A material count must be within 1 to 8, not {bounds.Min} to {bounds.Max}.");

        return bounds;
    }

    // A transmute recipe's result: an item's name, or an object whose item, when it's left out, is the input's, and
    // whose components are set and removed over the input's. The template holds the result's components.
    private static (Item? Item, int Count, ItemStack Template) ReadTransmuteResult(JsonElement result)
    {
        if (result.ValueKind == JsonValueKind.String)
        {
            var named = ItemsRegistry.Get(result.GetString()!);
            return (named, 1, new ItemStack(named));
        }

        var item = result.TryGetProperty("id", out var id) ? ItemsRegistry.Get(id.GetString()!) : null;
        var count = ReadResultCount(result);

        // Reading components doesn't depend on the item, so a result that keeps the input's reads them onto stone.
        var template = ReadResultTemplate(result, item ?? ItemsRegistry.Stone, count);

        return (item, count, template);
    }

    // A dye or imbue recipe's result (vanilla's ItemStackTemplate): an item's name, or an object naming an item, a
    // count and the components it sets and removes.
    private static ItemStack ReadResultStack(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String)
            return new ItemStack(ItemsRegistry.Get(value.GetString()!));

        var item = ItemsRegistry.Get(value.GetProperty("id").GetString()!);
        return ReadResultTemplate(value, item, ReadResultCount(value));
    }

    // A stack of the result's item and count with the components its "components" patch sets and removes, read like
    // a saved stack's. A component Obsidian doesn't model fails the recipe rather than going missing from what it
    // crafts.
    private static ItemStack ReadResultTemplate(JsonElement result, Item item, int count)
    {
        var saved = new NbtCompound
        {
            new NbtTag<string>("id", item.UnlocalizedName),
            new NbtTag<int>("count", count)
        };

        if (result.TryGetProperty("components", out var components))
            saved.Add(JsonNbt.ToNbt("components", components));

        var template = saved.ItemFromNbt()!;
        if (template.UnmodeledComponents is NbtCompound unmodeled)
        {
            var names = string.Join(", ", unmodeled.Select(entry => entry.Key));
            throw new JsonException($"Recipe results can't set {names} yet.");
        }

        return template;
    }

    // A result's count, which vanilla's result codecs take from 1 to 99 (whether the item stacks that high is only
    // checked when the result is crafted).
    private static int ReadResultCount(JsonElement result)
    {
        var count = OptionalInt(result, "count", 1);
        if (count is < 1 or > 99)
            throw new JsonException($"A result's count must be within 1 to 99, not {count}.");

        return count;
    }

    private static int OptionalInt(JsonElement value, string name, int fallback) =>
        value.TryGetProperty(name, out var number) ? number.GetInt32() : fallback;

    // Vanilla's PotionIngredient: an item ingredient, and optionally the potions (potion_contents' "potions", one or a
    // list) its item holds.
    private static PotionIngredient ReadPotionIngredient(JsonElement value, JsonSerializerOptions options)
    {
        var items = value.GetProperty("item").Deserialize<Ingredient>(options)!;
        if (!value.TryGetProperty("potion_contents", out var contents))
            return new PotionIngredient(items, null);

        var names = contents.GetProperty("potions");
        var potions = names.ValueKind == JsonValueKind.Array
            ? names.EnumerateArray().Select(name => ReadPotion(name.GetString()!)).ToArray()
            : [ReadPotion(names.GetString()!)];

        return new PotionIngredient(items, potions);
    }

    // A brewing recipe's output: an item and its potion, the only component vanilla's brewing outputs set.
    private static ItemStack ReadBrewingOutput(JsonElement value)
    {
        var item = ItemsRegistry.Get(value.GetProperty("id").GetString()!);
        var count = value.TryGetProperty("count", out var countValue) ? countValue.GetInt32() : 1;
        if (!value.TryGetProperty("components", out var components))
            return new ItemStack(item, count);

        foreach (var component in components.EnumerateObject())
        {
            if (component.Name != "minecraft:potion_contents")
                throw new JsonException($"Brewing outputs can't set {component.Name} yet.");
        }

        var potion = components.GetProperty("minecraft:potion_contents").GetProperty("potion").GetString()!;
        return new ItemStack(item, count, new PotionContentsDataComponent { Potion = ReadPotion(potion) });
    }

    private static Potion ReadPotion(string name)
    {
        if (name.StartsWith('#'))
            throw new JsonException($"Potion tags ({name}) aren't supported in brewing recipes.");

        var pascal = name.TrimResourceTag(keepUnderscores: true).ToPascalCase();
        return Enum.TryParse<Potion>(pascal, out var potion)
            ? potion
            : throw new JsonException($"Unknown potion '{name}'.");
    }

    // 26.1+ leaves out a crafting recipe's category when it's the default, misc.
    private static CraftingBookCategory CraftingCategory(JsonElement recipe) => recipe.TryGetProperty("category", out var category)
        ? Enum.Parse<CraftingBookCategory>(category.GetString()!, true)
        : CraftingBookCategory.Misc;

    public override void Write(Utf8JsonWriter writer, IRecipe[] value, JsonSerializerOptions options) => throw new NotImplementedException();
}

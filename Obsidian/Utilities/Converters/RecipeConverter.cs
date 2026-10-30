using Obsidian.API.Crafting;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Utilities;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Obsidian.Utilities.Converters;
public sealed class RecipesConverter : JsonConverter<IRecipe[]>
{
    // Special crafting recipes that 26.1 added, which Obsidian doesn't evaluate yet (see docs/known-gaps.md, "Special
    // crafting recipes"), so they're left out.
    private static readonly HashSet<string> unsupportedTypes = ["minecraft:crafting_dye", "minecraft:crafting_imbue"];

    //TODO PARSE SPECIAL RECIPES
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

            if (unsupportedTypes.Contains(typeId))
                continue;

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

            // Special recipes are built in code. Since 26.1 most of them name a result too, which isn't always an item
            // (map extending's is empty), so only the types below read one.
            if (craftingType is not (CraftingType.CraftingShaped or CraftingType.CraftingShapeless or CraftingType.Blasting
                or CraftingType.Smelting or CraftingType.Smoking or CraftingType.CampfireCooking
                or CraftingType.Stonecutting or CraftingType.SmithingTransform))
                continue;

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

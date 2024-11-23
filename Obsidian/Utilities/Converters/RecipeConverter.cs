using Obsidian.API.Crafting;
using Obsidian.API.Utilities;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Obsidian.Utilities.Converters;
public sealed class RecipesConverter : JsonConverter<IRecipe[]>
{
    //TODO PARSE SPECIAL RECIPES
    public override IRecipe[]? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var recipes = new List<IRecipe>();

        var element = JsonDocument.ParseValue(ref reader).RootElement;

        foreach (var recipeElement in element.EnumerateObject())
        {
            var recipeName = recipeElement.Name;
            var value = recipeElement.Value;

            var type = value.GetProperty("type").GetString()!.TrimResourceTag();
            var group = value.TryGetProperty("group", out var groupValue) ? groupValue.GetString()! : string.Empty;

            if (!Enum.TryParse<CraftingType>(type, true, out var craftingType))
                throw new JsonException();

            if (!value.TryGetProperty("result", out var resultValue))
            {
                if (craftingType == CraftingType.SmithingTrim)
                {
                    recipes.Add(new SmithingTrimRecipe
                    {
                        Identifier = recipeName,
                        Type = craftingType,
                        Group = group,
                        Template = value.GetProperty("template").Deserialize<Ingredient>(options)!,
                        Addition = value.GetProperty("addition").Deserialize<Ingredient>(options)!,
                        Base = value.GetProperty("base").Deserialize<Ingredient>(options)!
                    });
                }

                continue;
            }

            var result = resultValue.Deserialize<Ingredient>(options)!;

            switch (craftingType)
            {
                case CraftingType.CraftingShaped:
                    recipes.Add(new ShapedRecipe
                    {
                        Identifier = recipeName,
                        Type = craftingType,
                        Group = group,
                        Category = Enum.Parse<CraftingBookCategory>(value.GetProperty("category").GetString()!, true),
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
                        Category = Enum.Parse<CraftingBookCategory>(value.GetProperty("category").GetString()!, true),
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
                        Category = Enum.Parse<CookingBookCategory>(value.GetProperty("category").GetString()!, true),
                        CookingTime = value.GetProperty("cookingtime").GetInt32(),
                        Experience = value.GetProperty("experience").GetSingle(),
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

    public override void Write(Utf8JsonWriter writer, IRecipe[] value, JsonSerializerOptions options) => throw new NotImplementedException();
}

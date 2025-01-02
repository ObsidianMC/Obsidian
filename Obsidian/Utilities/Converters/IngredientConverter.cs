using Obsidian.API.Crafting;
using Obsidian.API.Inventory;
using Obsidian.API.Utilities;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Obsidian.Utilities.Converters;

public sealed class IngredientConverter : JsonConverter<Ingredient>
{
    public override Ingredient Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var element = JsonDocument.ParseValue(ref reader).RootElement;

        if (element.ValueKind == JsonValueKind.Array)
        {
            var rawRecipeItems = element.Deserialize<string[]>(options);

            var ingredient = new Ingredient();
            foreach (var rawRecipe in rawRecipeItems!)
            {
                var resourceLocation = rawRecipe.TrimResourceTag(true);

                var tag = TagsRegistry.Item.All.FirstOrDefault(x => x.Name.EqualsIgnoreCase(resourceLocation));
                if (tag != null)
                {
                    foreach (var id in tag!.Entries)
                    {
                        var item = ItemsRegistry.Get(id);

                        ingredient.Add(new ItemStack(item, 1));
                    }
                }
                else
                {
                    var i = ItemsRegistry.Get(rawRecipe);

                    ingredient.Add(new ItemStack(i, 1));
                }
            }

            return ingredient;
        }
        else
        {
            var ingredient = element.ValueKind == JsonValueKind.String ? new RawRecipeItem { Item = element.ToString() } : element.Deserialize<RawRecipeItem>(options);

            return [ItemsRegistry.GetSingleItem(ingredient.Item!)];
        }
    }

    public override void Write(Utf8JsonWriter writer, Ingredient value, JsonSerializerOptions options) => throw new NotImplementedException();

    private readonly struct RawRecipeItem
    {
        public string? Item { get; init; }

        public string? Tag { get; init; }

        public int Count { get; init; }
    }
}

using Obsidian.API.Crafting;
using Obsidian.Registries;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Obsidian.Utilities.Converters;

//TODO: re-do this class
public class IngredientConverter : JsonConverter<Ingredient>
{
    public override Ingredient Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var element = JsonDocument.ParseValue(ref reader).RootElement;

        if (element.ValueKind == JsonValueKind.Array)
        {
            var rawRecipeItems = element.ToString().FromJson<List<RawRecipeItem>>();

            var ingredient = new Ingredient();
            foreach (var rawRecipe in rawRecipeItems)
            {
                if (rawRecipe.Item == null && rawRecipe.Tag != null)
                {
                    var tag = TagsRegistry.Items.All.FirstOrDefault(x => x.Name.EqualsIgnoreCase(rawRecipe.Tag.Replace("minecraft:", "")));
                    foreach (var id in tag.Entries)
                    {
                        var item = ItemsRegistry.Get(id);

                        ingredient.Add(new ItemStack(item.Type, (short)rawRecipe.Count));
                    }
                }
                else
                {
                    var i = ItemsRegistry.Get(rawRecipe.Item);

                    ingredient.Add(new ItemStack(i.Type, (short)rawRecipe.Count));
                }
            }

            return ingredient;
        }
        else
        {
            var ingredient = element.ValueKind == JsonValueKind.String ? new RawRecipeItem { Item = element.ToString() } : element.ToString().FromJson<RawRecipeItem>(); ;

            return new Ingredient { ItemsRegistry.GetSingleItem(ingredient.Item) };
        }
    }

    public override void Write(Utf8JsonWriter writer, Ingredient value, JsonSerializerOptions options) => throw new NotImplementedException();

    private class RawRecipeItem
    {
        public string Item { get; set; }

        public string Tag { get; set; }

        public int Count { get; set; }
    }
}

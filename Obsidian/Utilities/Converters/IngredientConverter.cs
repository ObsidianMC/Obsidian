using Obsidian.API.Crafting;
using Obsidian.API.Inventory;
using Obsidian.API.Utilities;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Obsidian.Utilities.Converters;

public sealed class IngredientConverter : JsonConverter<Ingredient>
{
    public override Ingredient Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var element = JsonDocument.ParseValue(ref reader).RootElement;

        var ingredient = new Ingredient();

        if (element.ValueKind == JsonValueKind.Array)
        {
            var rawRecipeItems = element.Deserialize<string[]>(options);

           
            foreach (var rawRecipe in rawRecipeItems!)
            {
                var resourceLocation = rawRecipe.TrimResourceTag(true);

                var tag = TagsRegistry.Item.All.FirstOrDefault(x => x.Name == resourceLocation);
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

           
        }
        else
        {
            RawRecipeItem rawRecipe;
            if (element.ValueKind == JsonValueKind.String)
            {
                var tagOrId = element.ToString();

                rawRecipe = tagOrId.StartsWith('#') ? new() { Tag = tagOrId.TrimResourceTag(true) } : new() { Id = tagOrId };
            }
            else
                rawRecipe = element.Deserialize<RawRecipeItem>(options);

            if (!rawRecipe.Id.IsNullOrEmpty())
                ingredient.Add(ItemsRegistry.GetSingleItem(rawRecipe.Id));
            else
            {
                var tag = TagsRegistry.Item.All.FirstOrDefault(x => x.Name == rawRecipe.Tag);

                foreach (var id in tag!.Entries)
                {
                    var item = ItemsRegistry.Get(id);

                    ingredient.Add(new ItemStack(item, 1));
                }
            }
        }

        return ingredient;
    }

    public override void Write(Utf8JsonWriter writer, Ingredient value, JsonSerializerOptions options) => throw new NotImplementedException();

    private readonly struct RawRecipeItem
    {
        public string? Id { get; init; }

        public string? Tag { get; init; }

        public int Count { get; init; }
    }
}

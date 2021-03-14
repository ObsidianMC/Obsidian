using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Obsidian.API;
using Obsidian.API.Crafting;
using Obsidian.Items;
using Obsidian.Utilities.Extensions;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using static Obsidian.Utilities.Registry.Registry;

namespace Obsidian.Utilities.Converters
{
    public class IngredientConverter : JsonConverter<Ingredient>
    {
        public override Ingredient ReadJson(JsonReader reader, Type objectType, [AllowNull] Ingredient existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            var token = serializer.Deserialize<JToken>(reader);

            if (token.Type == JTokenType.Array)
            {
                var items = token.ToObject<List<RecipeItem>>();

                var ingredient = new Ingredient();

                foreach (var recipeItem in items)
                {
                    if (recipeItem.Item == null && recipeItem.Tag != null)
                    {
                        var tag = Tags["items"].FirstOrDefault(x => x.Name.EqualsIgnoreCase(recipeItem.Tag.Replace("minecraft:", "")));
                        foreach (var id in tag.Entries)
                        {
                            var item = GetItem(id);

                            ingredient.Add(new ItemStack(item.Type, (short)recipeItem.Count));
                        }
                    }
                    else
                    {
                        var i = GetItem(recipeItem.Item);

                        ingredient.Add(new ItemStack(i.Type, (short)recipeItem.Count));
                    }
                }

                return ingredient;
            }
            else if (token.Type == JTokenType.String)
            {
                var recipeItem = token.ToObject<string>();

                return new Ingredient { GetSingleItem(recipeItem) }; ;
            }
            else
            {
                var recipeItem = token.ToObject<RecipeItem>();

                var ingredient = new Ingredient();

                if (recipeItem.Item == null && recipeItem.Tag != null)
                {
                    var tag = Tags["items"].FirstOrDefault(x => x.Name.EqualsIgnoreCase(recipeItem.Tag.Replace("minecraft:", "")));
                    foreach (var id in tag.Entries)
                    {
                        var item = GetItem(id);

                        ingredient.Add(new ItemStack(item.Type, (short)recipeItem.Count));
                    }
                }
                else
                {
                    var i = GetItem(recipeItem.Item);

                    ingredient.Add(new ItemStack(i.Type, (short)recipeItem.Count));
                }

                return ingredient;
            }
        }

        public override void WriteJson(JsonWriter writer, [AllowNull] Ingredient value, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }
    }
}

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Obsidian.API;
using Obsidian.API.Crafting;
using Obsidian.Items;
using Obsidian.Util.Extensions;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using static Obsidian.Util.Registry.Registry;

namespace Obsidian.Util.Converters
{
    public class CraftingKeyConverter : JsonConverter<IReadOnlyDictionary<char, Ingredient>>
    {
        public CraftingKeyConverter()
        {
        }

        public override IReadOnlyDictionary<char, Ingredient> ReadJson(JsonReader reader, Type objectType, [AllowNull] IReadOnlyDictionary<char, Ingredient> existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            var token = serializer.Deserialize<JToken>(reader);

            var dict = token.ToObject<Dictionary<char, JToken>>();

            var readDict = new Dictionary<char, Ingredient>();

            foreach (var (k, v) in dict)
            {
                if (v.Type == JTokenType.Array)
                {
                    var items = v.ToObject<List<RecipeItem>>();

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

                    readDict.Add(k, ingredient);
                }
                else
                {
                    var recipeItem = v.ToObject<RecipeItem>();

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

                    readDict.Add(k, ingredient);
                }
            }

            return new ReadOnlyDictionary<char, Ingredient>(readDict);
        }

        public override void WriteJson(JsonWriter writer, IReadOnlyDictionary<char, Ingredient> value, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }
    }
}

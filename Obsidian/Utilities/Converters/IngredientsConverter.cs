namespace Obsidian.Utilities.Converters
{
    /*public class IngredientsConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType) => objectType is IReadOnlyList<Ingredient>;

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            var token = serializer.Deserialize<JToken>(reader);

            var list = token.ToObject<List<JToken>>();

            var ingredients = new List<Ingredient>();

            foreach(var itemToken in list)
            {
                if (itemToken.Type == JTokenType.Array)
                {
                    var items = itemToken.ToObject<List<RecipeItem>>();

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

                    ingredients.Add(ingredient);
                }
                else
                {
                    var recipeItem = itemToken.ToObject<RecipeItem>();

                    var ingredient = new Ingredient();

                    if (recipeItem.Item == null && recipeItem.Tag != null)
                    {
                        var tag = Tags["items"].FirstOrDefault(x => x.Name.EqualsIgnoreCase(recipeItem.Tag.Replace("minecraft:", "")));
                        foreach (var id in tag.Entries)
                        {
                            var item = GetItem(id);

                            ingredient.Add(new ItemStack(item.Type, (short)ingredient.Count));
                        }
                    }
                    else
                    {
                        var i = GetItem(recipeItem.Item);

                        ingredient.Add(new ItemStack(i.Type, (short)ingredient.Count));
                    }

                    ingredients.Add(ingredient);
                }
            }

            return new ReadOnlyCollection<Ingredient>(ingredients);
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }
    }*/
}

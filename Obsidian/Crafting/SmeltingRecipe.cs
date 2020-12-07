using Newtonsoft.Json;
using Obsidian.Util.Converters;

namespace Obsidian.Crafting
{
    public class SmeltingRecipe : IRecipe<string>
    {
        public string Type { get; set; }

        public string Group { get; set; }

        [JsonConverter(typeof(IngredientConverter))]
        public Ingredient Ingredient { get; set; }

        public string Result { get; set; }

        public float Experience { get; set; }

        public int Cookingtime { get; set; }
    }
}

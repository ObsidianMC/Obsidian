using Newtonsoft.Json.Linq;

namespace Obsidian.Crafting
{
    public class SmeltingRecipe : IRecipe<string>
    {
        public string Id { get; set; }

        public string Type { get; set; }

        public string Group { get; set; }

        public JToken Ingredient { get; set; }

        public string Result { get; set; }

        public float Experience { get; set; }

        public int Cookingtime { get; set; }
    }
}

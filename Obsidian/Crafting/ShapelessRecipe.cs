using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace Obsidian.Crafting
{
    public class ShapelessRecipe : IRecipe<RecipeItem>
    {
        public string Id { get; set; }

        public string Type { get; set; }

        public string Group { get; set; }

        public List<JToken> Ingredients { get; set; }

        public RecipeItem Result { get; set; }
    }
}

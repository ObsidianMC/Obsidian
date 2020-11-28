using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace Obsidian.Crafting
{
    public class ShapedRecipe : IRecipe<RecipeItem>
    {
        public string Id { get; set; }
        public string Type { get; set; }

        public string Group { get; set; }
        public List<string> Pattern { get; set; }

        public Dictionary<char, JToken> Key { get; set; }

        public RecipeItem Result { get; set; }
      
    }
}

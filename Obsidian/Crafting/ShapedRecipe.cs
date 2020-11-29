using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace Obsidian.Crafting
{
    public class ShapedRecipe : IRecipe<Ingedient>
    {
        public string Type { get; set; }

        public string Group { get; set; }
        public List<string> Pattern { get; set; }

        public Dictionary<char, JToken> Key { get; set; }

        public Ingedient Result { get; set; }
      
    }
}

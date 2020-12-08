using System.Collections.Generic;

namespace Obsidian.Crafting
{
    public sealed class ShapedRecipe : IRecipe
    {
        public string Name { get; set; }

        public string Type { get; set; }

        public string Group { get; set; }

        public IReadOnlyList<string> Pattern { get; set; }

        public IReadOnlyDictionary<char, Ingredient> Key { get; set; }

        public Ingredient Result { get; set; }
    }
}

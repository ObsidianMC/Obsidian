using System.Collections.Generic;

namespace Obsidian.Crafting
{
    public sealed class ShapedRecipe : Recipe
    {

        public IReadOnlyList<string> Pattern { get; internal set; }

        public IReadOnlyDictionary<char, Ingredient> Key { get; internal set; }
    }
}

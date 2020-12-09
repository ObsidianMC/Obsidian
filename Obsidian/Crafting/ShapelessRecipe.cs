using System.Collections.Generic;

namespace Obsidian.Crafting
{
    public sealed class ShapelessRecipe : Recipe
    {
        public IReadOnlyList<Ingredient> Ingredients { get; internal set; }
    }
}

using System.Collections.Generic;

namespace Obsidian.Crafting
{
    public class ShapelessRecipe : IRecipe<Ingredient>
    {
        public string Type { get; set; }

        public string Group { get; set; }

        public IReadOnlyList<Ingredient> Ingredients { get; set; }

        public Ingredient Result { get; set; }
    }
}

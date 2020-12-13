using System.Collections.Generic;

namespace Obsidian.Crafting
{
    public sealed class ShapelessRecipe : IRecipe
    {
        public string Name { get; set; }

        public string Type { get; set; }

        public string Group { get; set; }

        public Ingredient Result { get; set; }

        public IReadOnlyList<Ingredient> Ingredients { get; set; }
    }
}

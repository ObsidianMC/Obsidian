using System.Collections.Generic;

namespace Obsidian.API.Crafting
{
    public sealed class ShapelessRecipe : IRecipe
    {
        public string Name { get; set; }

        public CraftingType Type { get; set; }

        public string? Group { get; set; }

        public Ingredient Result { get; set; }

        public IReadOnlyList<Ingredient> Ingredients { get; set; }
    }
}

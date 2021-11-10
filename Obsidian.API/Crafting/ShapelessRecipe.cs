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

        public ShapelessRecipe(string name, CraftingType type, string? group, Ingredient result, IReadOnlyList<Ingredient> ingredients)
        {
            Name = name;
            Type = type;
            Group = group;
            Result = result;
            Ingredients = ingredients;
        }
    }
}

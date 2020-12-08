using System;

namespace Obsidian.Crafting
{
    public class CuttingRecipe : IRecipe
    {
        public string Name { get; set; }

        public string Type { get; set; }

        public string Group { get; set; }

        public Ingredient Ingredient { get; set; }

        public Ingredient Result { get; set; }

        public int Count { get; set; }
    }
}

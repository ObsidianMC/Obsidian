namespace Obsidian.Crafting
{
    public class CuttingRecipe : IRecipe<string>
    {
        public string Id { get; set; }

        public string Type { get; set; }

        public string Group { get; set; }

        public RecipeItem Ingredient { get; set; }

        public string Result { get; set; }

        public int Count { get; set; }
    }
}

namespace Obsidian.Crafting
{
    public sealed class CuttingRecipe : Recipe
    {
        public Ingredient Ingredient { get; internal set; }

        public int Count { get; internal set; }
    }
}

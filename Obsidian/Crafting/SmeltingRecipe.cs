namespace Obsidian.Crafting
{
    public sealed class SmeltingRecipe : Recipe
    {
        public Ingredient Ingredient { get; internal set; }

        public float Experience { get; internal set; }

        public int Cookingtime { get; internal set; }
    }
}

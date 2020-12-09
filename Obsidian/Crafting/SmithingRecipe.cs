namespace Obsidian.Crafting
{
    public sealed class SmithingRecipe : Recipe
    {

        public Ingredient Base { get; internal set; }

        public Ingredient Addition { get; internal set; }
    }
}

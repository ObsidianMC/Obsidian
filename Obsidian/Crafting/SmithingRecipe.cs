namespace Obsidian.Crafting
{
    public class SmithingRecipe : IRecipe<RecipeItem>
    {
        public string Id { get; set; }

        public string Type { get; set; }

        public string Group { get; set; }

        public RecipeItem Base { get; set; }

        public RecipeItem Addition { get; set; }

        public RecipeItem Result { get; set; }
    }
}

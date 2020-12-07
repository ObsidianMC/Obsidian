namespace Obsidian.Crafting
{
    public class SmithingRecipe : IRecipe<Ingredient>
    {
        public string Type { get; set; }

        public string Group { get; set; }

        public Ingredient Base { get; set; }

        public Ingredient Addition { get; set; }

        public Ingredient Result { get; set; }
    }
}

namespace Obsidian.API.Crafting
{
    public sealed class SmithingRecipe : IRecipe
    {
        public string Name { get; set; }

        public string Type { get; set; }

        public string? Group { get; set; }

        public Ingredient Result { get; set; }

        public Ingredient Base { get; set; }

        public Ingredient Addition { get; set; }
    }
}

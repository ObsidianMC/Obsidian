namespace Obsidian.API.Crafting
{
    public sealed class CuttingRecipe : IRecipe
    {
        public string Name { get; set; }

        public string Type { get; set; }

        public string? Group { get; set; }

        public Ingredient Result { get; set; }

        public Ingredient Ingredient { get; set; }

        public int Count { get; set; }
    }
}

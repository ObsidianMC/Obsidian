namespace Obsidian.API.Crafting
{
    public sealed class SmeltingRecipe : IRecipe
    {
        public string Name { get; set; }

        public string Type { get; set; }

        public string? Group { get; set; }

        public Ingredient Result { get; set; }

        public Ingredient Ingredient { get; set; }

        public float Experience { get; set; }

        public int Cookingtime { get; set; }
    }
}

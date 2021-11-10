namespace Obsidian.API.Crafting
{
    public sealed class CuttingRecipe : IRecipe
    {
        public string Name { get; set; }

        public CraftingType Type { get; set; }

        public string? Group { get; set; }

        public Ingredient Result { get; set; }

        public Ingredient Ingredient { get; set; }

        public int Count { get; set; }

        public CuttingRecipe(string name, CraftingType type, string? group, Ingredient result, Ingredient ingredient, int count)
        {
            Name = name;
            Type = type;
            Group = group;
            Result = result;
            Ingredient = ingredient;
            Count = count;
        }
    }
}

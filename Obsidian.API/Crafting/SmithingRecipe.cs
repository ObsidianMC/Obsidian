namespace Obsidian.API.Crafting
{
    public sealed class SmithingRecipe : IRecipe
    {
        public string Name { get; set; }

        public CraftingType Type { get; set; }

        public string? Group { get; set; }

        public Ingredient Result { get; set; }

        public Ingredient Base { get; set; }

        public Ingredient Addition { get; set; }

        public SmithingRecipe(string name, CraftingType type, string? group, Ingredient result, Ingredient @base, Ingredient addition)
        {
            Name = name;
            Type = type;
            Group = group;
            Result = result;
            Base = @base;
            Addition = addition;
        }
    }
}

namespace Obsidian.API.Crafting
{
    public interface IRecipe
    {
        public string Name { get; set; }

        public CraftingType Type { get; set; }

        public string Group { get; set; }

        public Ingredient Result { get; set; }
    }
}

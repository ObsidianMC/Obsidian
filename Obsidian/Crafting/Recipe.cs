namespace Obsidian.Crafting
{
    public class Recipe
    {
        public string Name { get; internal set; }

        public string Type { get; internal set; }

        public string Group { get; internal set; }

        public Ingredient Result { get; internal set; }
    }
}

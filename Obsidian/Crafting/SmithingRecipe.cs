namespace Obsidian.Crafting
{
    public class SmithingRecipe : IRecipe<Ingedient>
    {
        public string Type { get; set; }

        public string Group { get; set; }

        public Ingedient Base { get; set; }

        public Ingedient Addition { get; set; }

        public Ingedient Result { get; set; }
    }
}

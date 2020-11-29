namespace Obsidian.Crafting
{
    internal interface IRecipe<T>
    {
        public string Type { get; set; }

        public string Group { get; set; }

        public T Result { get; set; }
    }
}

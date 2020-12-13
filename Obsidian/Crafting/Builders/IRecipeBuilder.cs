using Obsidian.Items;

namespace Obsidian.Crafting.Builders
{
    public interface IRecipeBuilder<TBuilder>
    {
        public string Name { get; set; }

        public string Group { get; set; }

        public ItemStack Result { get; set; }

        public TBuilder WithName(string name);

        public TBuilder SetResult(ItemStack result);

        public TBuilder InGroup(string group);

        public IRecipe Build();
    }
}

using Obsidian.Items;
using System;

namespace Obsidian.Crafting.Builders
{
    public abstract class RecipeBuilder<T> where T : Recipe
    {
        public string Name { get; set; }

        public string Group { get; set; }

        public ItemStack Result { get; set; }

        public RecipeBuilder<T> WithName(string name)
        {
            this.Name = name;

            return this;
        }

        public RecipeBuilder<T> SetResult(ItemStack result)
        {
            if (this.Result != null)
                throw new InvalidOperationException("Result is already set.");

            this.Result = result;

            return this;
        }

        public RecipeBuilder<T> InGroup(string group)
        {
            this.Group = group;

            return this;
        }

        public abstract T Build();
    }
}

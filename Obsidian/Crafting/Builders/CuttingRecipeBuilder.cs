using Obsidian.Items;
using System;

namespace Obsidian.Crafting.Builders
{
    public class CuttingRecipeBuilder : RecipeBuilder<CuttingRecipe>
    {
        public Ingredient Ingredient { get; private set; } = new Ingredient();

        public int Count { get; private set; }

        public CuttingRecipeBuilder AddIngredients(params ItemStack[] items)
        {
            foreach (var item in items)
                this.Ingredient.Add(item);

            return this;
        }

        public CuttingRecipeBuilder SetOutputCount(int count)
        {
            this.Count = count;

            return this;
        }

        public override CuttingRecipe Build()
        {
            if (this.Ingredient.Count <= 0)
                throw new InvalidOperationException("Recipe must atleast have 1 item as an ingredient");

            return new CuttingRecipe
            {
                Name = this.Name ?? throw new NullReferenceException("Name must not be null"),
                Type = Globals.Stonecutting,
                Group = this.Group,
                Ingredient = this.Ingredient ?? throw new NullReferenceException("Ingredient must not be null"),
                Count = this.Count,
                Result = this.Result != null ? new Ingredient { this.Result } : throw new NullReferenceException("Result is not set.")
            };
        }
    }
}

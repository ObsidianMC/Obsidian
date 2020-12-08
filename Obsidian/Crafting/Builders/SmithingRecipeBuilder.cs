using Obsidian.Items;
using System;

namespace Obsidian.Crafting.Builders
{
    public class SmithingRecipeBuilder : RecipeBuilder<SmithingRecipe>
    {
        public Ingredient Base { get; internal set; } = new Ingredient();

        public Ingredient Addition { get; internal set; } = new Ingredient();

        public SmithingRecipeBuilder AddBaseIngredients(params ItemStack[] items)
        {
            foreach (var item in items)
                this.Base.Add(item);

            return this;
        }

        // Not sure what to name this without making the name insanely long :D
        public SmithingRecipeBuilder AddSubIngredients(params ItemStack[] items)
        {
            foreach (var item in items)
                this.Addition.Add(item);

            return this;
        }

        public override SmithingRecipe Build()
        {
            if (this.Base.Count <= 0)
                throw new InvalidOperationException("Base ingredients must have atleast 1 item.");

            if (this.Addition.Count <= 0)
                throw new InvalidOperationException("Sub ingredients must have atleast 1 item.");

            return new SmithingRecipe
            {
                Name = this.Name ?? throw new NullReferenceException("Recipe must have a name"),
                Type = Globals.Smithing,
                Group = this.Group,
                Base = this.Base,
                Addition = this.Addition,
                Result = this.Result != null ? new Ingredient { this.Result } : throw new NullReferenceException("Result is not set.")
            };
        }
    }
}

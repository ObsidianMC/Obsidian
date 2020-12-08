using Obsidian.Items;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Obsidian.Crafting.Builders
{
    public class ShapelessRecipeBuilder : RecipeBuilder<ShapelessRecipe>
    {
        public IReadOnlyList<Ingredient> Ingredients { get; }

        private readonly List<Ingredient> ingredients = new List<Ingredient>();

        public ShapelessRecipeBuilder()
        {
            this.Ingredients = new ReadOnlyCollection<Ingredient>(this.ingredients);
        }

        public ShapelessRecipeBuilder AddIngredients(params ItemStack[] items)
        {
            var ingredient = new Ingredient();

            foreach (var item in items)
                ingredient.Add(item);

            return this;
        }

        public override ShapelessRecipe Build()
        {
            if (this.ingredients.Count <= 0)
                throw new InvalidOperationException("Ingredients must be filled with atleast 1 item.");

            return new ShapelessRecipe
            {
                Name = this.Name ?? throw new NullReferenceException("Recipe must have a name"),
                Type = Globals.ShapelessCrafting,
                Group = this.Group,
                Ingredients = new ReadOnlyCollection<Ingredient>(new List<Ingredient>(this.ingredients)),
                Result = this.Result != null ? new Ingredient { this.Result } : throw new NullReferenceException("Result is not set.")
            };
        }
    }
}

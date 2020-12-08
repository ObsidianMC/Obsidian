using Obsidian.Items;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Obsidian.Crafting.Builders
{
    public class ShapedRecipeBuilder : RecipeBuilder<ShapedRecipe>
    {

        public IReadOnlyList<string> Pattern { get; }

        public IReadOnlyDictionary<char, Ingredient> Key { get; }

        private readonly List<string> pattern = new List<string>();

        private readonly Dictionary<char, Ingredient> key = new Dictionary<char, Ingredient>();

        public ShapedRecipeBuilder()
        {
            this.Pattern = new ReadOnlyCollection<string>(this.pattern);

            this.Key = new ReadOnlyDictionary<char, Ingredient>(this.key);
        }

        public ShapedRecipeBuilder WithPattern(params string[] pattern)
        {
            if (pattern.Length > 3)
                throw new InvalidOperationException("Patterns must only have 3 total elements.");

            this.pattern.AddRange(pattern);

            return this;
        }


        public ShapedRecipeBuilder WithKey(char key, params ItemStack[] matches)
        {
            var ingredient = new Ingredient();

            foreach (var match in matches)
                ingredient.Add(match);

            this.key.Add(key, ingredient);

            return this;
        }

        public override ShapedRecipe Build()
        {
            if (this.pattern.Count <= 0)
                throw new InvalidOperationException("Patterns cannot be empty");

            if (this.key.Count <= 0)
                throw new InvalidOperationException("Keys cannot be empty.");

            return new ShapedRecipe
            {
                Name = this.Name ?? throw new NullReferenceException("Recipe must have a name"),
                Type = Globals.ShapedCrafting,
                Group = this.Group,
                Pattern = new ReadOnlyCollection<string>(new List<string>(this.pattern)),
                Result = this.Result != null ? new Ingredient { this.Result } : throw new NullReferenceException("Result is not set."),
                Key = new ReadOnlyDictionary<char, Ingredient>(new Dictionary<char, Ingredient>(this.key))
            };
        }
    }
}

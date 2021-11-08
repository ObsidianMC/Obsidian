using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Obsidian.API.Crafting.Builders
{
    public class ShapedRecipeBuilder : IRecipeBuilder<ShapedRecipeBuilder>
    {
        public string? Name { get; set; }
        public string? Group { get; set; }
        public ItemStack? Result { get; set; }

        public IReadOnlyList<string> Pattern { get; }

        public IReadOnlyDictionary<char, Ingredient> Key { get; }

        private readonly List<string> pattern = new();

        private readonly Dictionary<char, Ingredient> key = new();

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

        public IRecipe Build()
        {
            if (this.pattern.Count <= 0)
                throw new InvalidOperationException("Patterns cannot be empty");

            if (this.key.Count <= 0)
                throw new InvalidOperationException("Keys cannot be empty.");

            return new ShapedRecipe
            (
                this.Name ?? throw new NullReferenceException("Recipe must have a name"),
                CraftingType.CraftingShaped,
                this.Group,
                this.Result != null ? new Ingredient { this.Result } : throw new NullReferenceException("Result is not set."),
                new ReadOnlyCollection<string>(new List<string>(this.pattern)),
                new ReadOnlyDictionary<char, Ingredient>(new Dictionary<char, Ingredient>(this.key))
            );
        }

        public ShapedRecipeBuilder WithName(string name)
        {
            this.Name = name;

            return this;
        }

        public ShapedRecipeBuilder SetResult(ItemStack result)
        {
            if (this.Result != null)
                throw new InvalidOperationException("Result is already set.");

            this.Result = result;

            return this;
        }

        public ShapedRecipeBuilder InGroup(string group)
        {
            this.Group = group;

            return this;
        }
    }
}

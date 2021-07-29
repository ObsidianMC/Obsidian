using System;

namespace Obsidian.API.Crafting.Builders
{
    public class SmeltingRecipeBuilder : IRecipeBuilder<SmeltingRecipeBuilder>
    {
        public string? Name { get; set; }
        public string? Group { get; set; }
        public ItemStack? Result { get; set; }

        public SmeltingType Type { get; private set; }

        public Ingredient Ingredient { get; private set; } = new Ingredient();

        public float Experience { get; private set; }

        public int CookingTime { get; private set; }

        public SmeltingRecipeBuilder WithType(SmeltingType type)
        {
            this.Type = type;

            return this;
        }

        public SmeltingRecipeBuilder AddIngredients(params ItemStack[] items)
        {
            var ingredient = new Ingredient();

            foreach (var item in items)
                ingredient.Add(item);

            return this;
        }

        public SmeltingRecipeBuilder GivesExperience(float exp)
        {
            this.Experience = exp;

            return this;
        }

        public SmeltingRecipeBuilder WithCookingTime(int cookingTime)
        {
            this.CookingTime = cookingTime;

            return this;
        }

        public SmeltingRecipeBuilder WithName(string name)
        {
            this.Name = name;

            return this;
        }

        public SmeltingRecipeBuilder SetResult(ItemStack result)
        {
            if (this.Result != null)
                throw new InvalidOperationException("Result is already set.");

            this.Result = result;

            return this;
        }

        public SmeltingRecipeBuilder InGroup(string group)
        {
            this.Group = group;

            return this;
        }

        public IRecipe Build()
        {
            CraftingType type = Type switch
            {
                SmeltingType.Default => CraftingType.Smelting,
                SmeltingType.Blasting => CraftingType.Blasting,
                SmeltingType.Smoking => CraftingType.Smoking,
                SmeltingType.CampfireCooking => CraftingType.CampfireCooking
            };

            if (this.Ingredient.Count <= 0)
                throw new InvalidOperationException("Recipe must atleast have 1 item as an ingredient");

            return new SmeltingRecipe
            {
                Name = this.Name ?? throw new NullReferenceException("Recipe must have a name"),
                Type = type,
                Group = this.Group,
                Ingredient = this.Ingredient,
                Cookingtime = this.CookingTime,
                Experience = this.Experience,
                Result = this.Result != null ? new Ingredient { this.Result } : throw new NullReferenceException("Result is not set.")
            };
        }
    }

    public enum SmeltingType
    {
        Default,
        Blasting,
        Smoking,
        CampfireCooking
    }
}

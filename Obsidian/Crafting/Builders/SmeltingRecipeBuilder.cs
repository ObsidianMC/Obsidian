using Obsidian.Items;
using System;

namespace Obsidian.Crafting.Builders
{
    public class SmeltingRecipeBuilder : RecipeBuilder<SmeltingRecipe>
    {
        public SmeltingType Type { get; internal set; }

        public Ingredient Ingredient { get; internal set; } = new Ingredient();

        public float Experience { get; internal set; }

        public int CookingTime { get; internal set; }

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

        public override SmeltingRecipe Build()
        {
            var type = "";

            switch (this.Type)
            {
                case SmeltingType.Default:
                    type = Globals.Smelting;
                    break;
                case SmeltingType.Blasting:
                    type = Globals.Blasting;
                    break;
                case SmeltingType.Smoking:
                    type = Globals.Smoking;
                    break;
                case SmeltingType.CampfireCooking:
                    type = Globals.CampfireCooking;
                    break;
                default:
                    break;
            }

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

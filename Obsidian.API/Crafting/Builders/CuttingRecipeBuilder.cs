namespace Obsidian.API.Crafting.Builders;

public class CuttingRecipeBuilder : IRecipeBuilder<CuttingRecipeBuilder>
{
    public string? Name { get; set; }
    public string? Group { get; set; }
    public ItemStack? Result { get; set; }

    public Ingredient Ingredient { get; private set; } = new Ingredient();

    public int Count { get; set; }

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

    public CuttingRecipeBuilder WithName(string name)
    {
        this.Name = name;

        return this;
    }

    public CuttingRecipeBuilder SetResult(ItemStack result)
    {
        if (this.Result != null)
            throw new InvalidOperationException("Result is already set.");

        this.Result = result;

        return this;
    }

    public CuttingRecipeBuilder InGroup(string group)
    {
        this.Group = group;

        return this;
    }

    public IRecipe Build()
    {
        if (this.Ingredient.Count <= 0)
            throw new InvalidOperationException("Recipe must atleast have 1 item as an ingredient");

        return new CuttingRecipe
        {
            Name = this.Name ?? throw new NullReferenceException("Name must not be null"),
            Type = CraftingType.Stonecutting,
            Group = this.Group,
            Result = this.Result != null ? new Ingredient { this.Result } : throw new NullReferenceException("Result is not set."),
            Ingredient = this.Ingredient ?? throw new NullReferenceException("Ingredient must not be null"),
            Count = this.Count
        };
    }
}

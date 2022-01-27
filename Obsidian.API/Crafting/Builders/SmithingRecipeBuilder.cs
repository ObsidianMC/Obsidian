namespace Obsidian.API.Crafting.Builders;

public class SmithingRecipeBuilder : IRecipeBuilder<SmithingRecipeBuilder>
{
    public string? Name { get; set; }
    public string? Group { get; set; }
    public ItemStack? Result { get; set; }

    public Ingredient Base { get; private set; } = new Ingredient();

    public Ingredient Addition { get; private set; } = new Ingredient();

    public SmithingRecipeBuilder AddBaseIngredients(params ItemStack[] items)
    {
        foreach (var item in items)
            Base.Add(item);

        return this;
    }

    // Not sure what to name this without making the name insanely long :D
    public SmithingRecipeBuilder AddSubIngredients(params ItemStack[] items)
    {
        foreach (var item in items)
            Addition.Add(item);

        return this;
    }

    public SmithingRecipeBuilder WithName(string name)
    {
        Name = name;

        return this;
    }

    public SmithingRecipeBuilder SetResult(ItemStack result)
    {
        if (Result != null)
            throw new InvalidOperationException("Result is already set.");

        Result = result;

        return this;
    }

    public SmithingRecipeBuilder InGroup(string group)
    {
        Group = group;

        return this;
    }

    public IRecipe Build()
    {
        if (Base.Count <= 0)
            throw new InvalidOperationException("Base ingredients must have atleast 1 item.");

        if (Addition.Count <= 0)
            throw new InvalidOperationException("Sub ingredients must have atleast 1 item.");

        return new SmithingRecipe
        (
            Name ?? throw new NullReferenceException("Recipe must have a name"),
            CraftingType.Smithing,
            Group,
            Base,
            Addition,
            Result != null ? new Ingredient { Result } : throw new NullReferenceException("Result is not set.")
        );
    }
}

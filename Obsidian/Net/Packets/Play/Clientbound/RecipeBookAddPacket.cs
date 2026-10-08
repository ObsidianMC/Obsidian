using Obsidian.API.Crafting;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class RecipeBookAddPacket
{
    public override void Serialize(INetStreamWriter writer)
    {
        var recipes = RecipesRegistry.RecipeBookRecipes;
        var groups = new Dictionary<(CraftingBookCategory, string), int>();
        writer.WriteVarInt(recipes.Count);
        for (int id = 0; id < recipes.Count; id++)
        {
            var recipe = recipes[id];
            var category = recipe is ShapedRecipe shaped ? shaped.Category : ((ShapelessRecipe)recipe).Category;
            writer.WriteVarInt(id);
            WriteRecipeDisplay(writer, recipe);

            // OPTIONAL_VAR_INT encodes an absent group as 0 and a present group as its ID + 1.
            int group = -1;
            if (!string.IsNullOrEmpty(recipe.Group) && !groups.TryGetValue((category, recipe.Group), out group))
            {
                group = groups.Count;
                groups.Add((category, recipe.Group), group);
            }
            writer.WriteVarInt(group + 1);
            writer.WriteVarInt((int)category);
            writer.WriteBoolean(true);
            var ingredients = GetIngredients(recipe);
            writer.WriteVarInt(ingredients.Count);
            foreach (var ingredient in ingredients)
            {
                var items = ingredient.Select(item => item.Holder.Id).Distinct().ToList();
                // Direct holder sets use count + 1; 0 is reserved for a named tag.
                writer.WriteVarInt(items.Count + 1);
                foreach (var itemId in items)
                    writer.WriteVarInt(itemId);
            }
            writer.WriteByte(0);
        }
        writer.WriteBoolean(true);
    }

    internal static IReadOnlyList<Ingredient> GetIngredients(IRecipeWithResult recipe) => recipe switch
    {
        ShapedRecipe shaped => shaped.Pattern.SelectMany(row => row)
            .Where(symbol => symbol != ' ').Select(symbol => shaped.Key[symbol]).ToList(),
        ShapelessRecipe shapeless => shapeless.Ingredients,
        _ => throw new ArgumentException("Recipe must be a crafting recipe.", nameof(recipe))
    };

    internal static void WriteRecipeDisplay(INetStreamWriter writer, IRecipeWithResult recipe)
    {
        // Vanilla registers shapeless/shaped displays as 0/1 and empty/item/item-stack/composite slots as 0/2/3/7.
        if (recipe is ShapedRecipe shaped)
        {
            writer.WriteVarInt(1);
            writer.WriteVarInt(shaped.Pattern[0].Length);
            writer.WriteVarInt(shaped.Pattern.Count);
            writer.WriteVarInt(shaped.Pattern.Sum(row => row.Length));
            foreach (var row in shaped.Pattern)
            {
                foreach (char symbol in row)
                {
                    if (symbol == ' ')
                        writer.WriteVarInt(0);
                    else
                        WriteIngredientDisplay(writer, shaped.Key[symbol]);
                }
            }
        }
        else if (recipe is ShapelessRecipe shapeless)
        {
            writer.WriteVarInt(0);
            writer.WriteVarInt(shapeless.Ingredients.Count);
            foreach (var ingredient in shapeless.Ingredients)
                WriteIngredientDisplay(writer, ingredient);
        }
        else
            throw new ArgumentException("Recipe must be a crafting recipe.", nameof(recipe));

        writer.WriteVarInt(3);
        writer.WriteItemStack(recipe.Result.First());
        writer.WriteVarInt(2);
        writer.WriteVarInt(ItemsRegistry.Get(Material.CraftingTable).Id);
    }

    private static void WriteIngredientDisplay(INetStreamWriter writer, Ingredient ingredient)
    {
        var items = ingredient.Select(item => item.Holder.Id).Distinct().ToList();
        if (items.Count == 0)
            writer.WriteVarInt(0);
        else if (items.Count == 1)
        {
            writer.WriteVarInt(2);
            writer.WriteVarInt(items[0]);
        }
        else
        {
            writer.WriteVarInt(7);
            writer.WriteVarInt(items.Count);
            foreach (var itemId in items)
            {
                writer.WriteVarInt(2);
                writer.WriteVarInt(itemId);
            }
        }
    }
}

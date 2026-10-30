using Obsidian.API.Crafting;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;

namespace Obsidian.Net.Packets.Play.Clientbound;

/// <summary>
/// Adds recipes to the client's recipe book, or replaces its whole book with them.
/// </summary>
public partial class RecipeBookAddPacket
{
    /// <param name="entries">The recipes, which the client knows by their display ids from now on.</param>
    /// <param name="replace">Whether the client forgets the recipes it had first, as when the player joins.</param>
    internal RecipeBookAddPacket(IReadOnlyList<RecipeBookEntry> entries, bool replace)
    {
        this.Entries = entries;
        this.Replace = replace;
    }

    internal IReadOnlyList<RecipeBookEntry> Entries { get; }

    public bool Replace { get; }

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteVarInt(this.Entries.Count);

        foreach (var entry in this.Entries)
        {
            writer.WriteVarInt(entry.DisplayId);
            WriteRecipeDisplay(writer, entry);

            // OPTIONAL_VAR_INT encodes an absent group as 0 and a present group as its ID + 1.
            writer.WriteVarInt(entry.Group is int group ? group + 1 : 0);
            writer.WriteVarInt((int)entry.Category);
            writer.WriteBoolean(true);

            var ingredients = GetIngredients(entry.Recipe);
            writer.WriteVarInt(ingredients.Count);

            foreach (var ingredient in ingredients)
                WriteItems(writer, ingredient);

            // Neither a notification nor a highlight: the recipe is content the server has, not one just unlocked.
            writer.WriteByte(0);
        }

        writer.WriteBoolean(this.Replace);
    }

    /// <summary>
    /// The ingredients the recipe book places in the grid (vanilla's <c>PlacementInfo</c>), in grid order, which the
    /// client checks the player has.
    /// </summary>
    internal static IReadOnlyList<Ingredient> GetIngredients(ICraftingRecipe recipe) => recipe switch
    {
        ShapedRecipe shaped => shaped.Pattern
            .SelectMany(row => row)
            .Where(symbol => symbol != ' ')
            .Select(symbol => shaped.Key[symbol])
            .ToList(),
        ShapelessRecipe shapeless => shapeless.Ingredients,
        DyeRecipe dye => [dye.Target, dye.Dye],
        ImbueRecipe imbue => ImbueGrid(imbue),
        TransmuteRecipe transmute => [transmute.Input, .. Materials(transmute, transmute.MaxMaterialCount)],
        _ => throw new ArgumentException("Recipe must be a crafting recipe.", nameof(recipe))
    };

    /// <summary>Writes the entry's recipe display, which the client shows in the book and as a ghost recipe.</summary>
    internal static void WriteRecipeDisplay(INetStreamWriter writer, RecipeBookEntry entry)
    {
        switch (entry.Recipe)
        {
            case ShapedRecipe shaped:
                WriteShapedDisplay(writer, shaped);
                break;
            case ShapelessRecipe shapeless:
                WriteShapelessDisplay(writer, shapeless);
                break;
            case DyeRecipe dye:
                WriteDyeDisplay(writer, dye);
                break;
            case ImbueRecipe imbue:
                WriteImbueDisplay(writer, imbue);
                break;
            case TransmuteRecipe transmute:
                WriteTransmuteDisplay(writer, transmute, entry.Display);
                break;
            default:
                throw new ArgumentException("Recipe must be a crafting recipe.", nameof(entry));
        }

        writer.WriteVarInt(SlotDisplayTypes.Item);
        writer.WriteVarInt(ItemsRegistry.CraftingTable.Id);
    }

    private static void WriteShapedDisplay(INetStreamWriter writer, ShapedRecipe shaped)
    {
        WriteShapedHeader(writer, shaped.Pattern[0].Length, shaped.Pattern.Count);

        foreach (var row in shaped.Pattern)
        {
            foreach (char symbol in row)
            {
                if (symbol == ' ')
                    writer.WriteVarInt(SlotDisplayTypes.Empty);
                else
                    WriteIngredientDisplay(writer, shaped.Key[symbol]);
            }
        }

        WriteStackDisplay(writer, shaped.Result.First());
    }

    private static void WriteShapelessDisplay(INetStreamWriter writer, ShapelessRecipe shapeless)
    {
        WriteShapelessHeader(writer, shapeless.Ingredients.Count);

        foreach (var ingredient in shapeless.Ingredients)
            WriteIngredientDisplay(writer, ingredient);

        WriteStackDisplay(writer, shapeless.Result.First());
    }

    // The target and a dye that has a dye color, crafting the target dyed.
    private static void WriteDyeDisplay(INetStreamWriter writer, DyeRecipe dye)
    {
        WriteShapelessHeader(writer, 2);
        WriteIngredientDisplay(writer, dye.Target);
        WriteDyeIngredientDisplay(writer, dye.Dye);

        writer.WriteVarInt(SlotDisplayTypes.Dyed);
        WriteDyeIngredientDisplay(writer, dye.Dye);
        WriteIngredientDisplay(writer, dye.Target);
    }

    private static void WriteDyeIngredientDisplay(INetStreamWriter writer, Ingredient dyes)
    {
        writer.WriteVarInt(SlotDisplayTypes.OnlyWithComponent);
        WriteIngredientDisplay(writer, dyes);
        writer.WriteRegistryId(ComponentValueCodecs.DataComponentTypes, (int)DataComponentType.Dye);
    }

    // The full 3x3 grid with the source holding any potion, crafting the result holding it too.
    private static void WriteImbueDisplay(INetStreamWriter writer, ImbueRecipe imbue)
    {
        WriteShapedHeader(writer, 3, 3);

        var grid = ImbueGrid(imbue);
        for (int slot = 0; slot < grid.Length; slot++)
        {
            if (slot == ImbueSourceSlot)
                writer.WriteVarInt(SlotDisplayTypes.WithAnyPotion);

            WriteIngredientDisplay(writer, grid[slot]);
        }

        writer.WriteVarInt(SlotDisplayTypes.WithAnyPotion);
        WriteStackDisplay(writer, imbue.Result);
    }

    // The display for one number of materials: the input and materials, crafting the result item, or each of the
    // input's items when the result keeps the input's. Like vanilla's, the nth display shows n materials whatever the
    // fewest the recipe takes, and crafts what the fewest plus n - 1 craft.
    private static void WriteTransmuteDisplay(INetStreamWriter writer, TransmuteRecipe transmute, int display)
    {
        var shown = display + 1;
        WriteShapelessHeader(writer, 1 + shown);
        WriteIngredientDisplay(writer, transmute.Input);

        foreach (var material in Materials(transmute, shown))
            WriteIngredientDisplay(writer, material);

        var materials = transmute.MinMaterialCount + display;
        var count = transmute.AddMaterialCountToResult ? transmute.ResultCount + materials : transmute.ResultCount;

        if (transmute.ResultItem is Item item)
        {
            WriteStackDisplay(writer, new ItemStack(item, count));
            return;
        }

        var inputs = Items(transmute.Input);
        writer.WriteVarInt(SlotDisplayTypes.Composite);
        writer.WriteVarInt(inputs.Count);

        foreach (var input in inputs)
            WriteStackDisplay(writer, new ItemStack(ItemsRegistry.Get(input), count));
    }

    // Vanilla registers shapeless/shaped displays as 0/1.
    private static void WriteShapelessHeader(INetStreamWriter writer, int ingredients)
    {
        writer.WriteVarInt(0);
        writer.WriteVarInt(ingredients);
    }

    private static void WriteShapedHeader(INetStreamWriter writer, int width, int height)
    {
        writer.WriteVarInt(1);
        writer.WriteVarInt(width);
        writer.WriteVarInt(height);
        writer.WriteVarInt(width * height);
    }

    private static void WriteStackDisplay(INetStreamWriter writer, ItemStack stack)
    {
        writer.WriteVarInt(SlotDisplayTypes.ItemStack);
        writer.WriteItemStackTemplate(stack);
    }

    // Like vanilla's Ingredient.display, a tag display of the ingredient's items. Obsidian flattens a tag ingredient
    // into its items when it loads a recipe, so a tag's items go as a list rather than the tag's name.
    private static void WriteIngredientDisplay(INetStreamWriter writer, Ingredient ingredient)
    {
        if (ingredient.Count == 0)
        {
            writer.WriteVarInt(SlotDisplayTypes.Empty);
            return;
        }

        writer.WriteVarInt(SlotDisplayTypes.Tag);
        WriteItems(writer, ingredient);
    }

    // An item holder set: a list writes its count + 1 and the ids, as 0 is reserved for a named tag.
    private static void WriteItems(INetStreamWriter writer, Ingredient ingredient)
    {
        var items = Items(ingredient);
        writer.WriteVarInt(items.Count + 1);

        foreach (var itemId in items)
            writer.WriteVarInt(itemId);
    }

    private static List<int> Items(Ingredient ingredient) => ingredient
        .Select(item => (int)item.Holder.Id)
        .Distinct()
        .ToList();

    private static IEnumerable<Ingredient> Materials(TransmuteRecipe transmute, int count) =>
        Enumerable.Repeat(transmute.Material, count);

    // The material around the source in the middle, row by row.
    private static Ingredient[] ImbueGrid(ImbueRecipe imbue)
    {
        var grid = Enumerable.Repeat(imbue.Material, 9).ToArray();
        grid[ImbueSourceSlot] = imbue.Source;

        return grid;
    }

    private const int ImbueSourceSlot = 4;
}

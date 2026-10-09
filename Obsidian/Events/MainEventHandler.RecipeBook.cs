using Obsidian.API.Containers;
using Obsidian.API.Crafting;
using Obsidian.API.Inventory;
using Obsidian.Net.Packets.Play.Clientbound;
using System.Diagnostics;

namespace Obsidian.Events;

public partial class MainEventHandler
{
    internal static async ValueTask PlaceBookRecipeAsync(IPlayer player, int containerId, int displayId, bool makeAll)
    {
        if (containerId != (player.OpenedContainer is null ? 0 : player.CurrentContainerId) ||
            displayId < 0 || displayId >= RecipesRegistry.RecipeBookRecipes.Count)
            return;

        BaseContainer grid;
        int width;
        if (containerId == 0)
        {
            grid = player.Inventory;
            width = 2;
        }
        else if (player.OpenedContainer is CraftingTable { Type: InventoryType.Crafting } table)
        {
            grid = table;
            width = 3;
        }
        else
            return;

        var recipe = RecipesRegistry.RecipeBookRecipes[displayId];
        var ingredients = RecipeBookAddPacket.GetIngredients(recipe);
        if (ingredients.Count == 0 || ingredients.Count > width * width ||
            (recipe is ShapedRecipe shaped && (shaped.Pattern.Count > width || shaped.Pattern[0].Length > width)))
            return;

        var inventory = new Container(45);
        var gridItems = new ItemStack?[width * width];
        var stock = new List<ItemStack>();
        for (int slot = 9; slot < 45; slot++)
        {
            var item = player.Inventory.GetItem(slot);
            if (item.IsNullOrAir() || item.Count <= 0)
                continue;
            inventory.SetItem(slot, new ItemStack(item, item.Count));
            AddRecipeStock(stock, item);
        }
        for (int slot = 1; slot <= width * width; slot++)
        {
            var item = grid.GetItem(slot);
            if (item.IsNullOrAir() || item.Count <= 0)
                continue;
            gridItems[slot - 1] = new ItemStack(item, item.Count);
            AddRecipeStock(stock, item);
        }

        int maximum = 0;
        int upper = stock.Count == 0 ? 0 : stock.Max(item => Math.Min(item.Count, item.MaxStackSize));
        while (maximum < upper)
        {
            int count = maximum + (upper - maximum + 1) / 2;
            if (TrySelectRecipeStock(ingredients, stock, count, out _))
                maximum = count;
            else
                upper = count - 1;
        }

        int amount = makeAll ? maximum : Math.Min(1, maximum);
        if (!makeAll && maximum > 0 && RecipesRegistry.FindRecipe(grid, width)?.Identifier == recipe.Identifier)
            amount = Math.Min(maximum, gridItems.Where(item => !item.IsNullOrAir()).Min(item => item!.Count) + 1);

        var placedItems = new List<ItemStack>();
        if (amount > 0)
        {
            if (!TrySelectRecipeStock(ingredients, stock, amount, out var selected))
                return;
            foreach (int index in selected)
            {
                var item = stock[index];
                int remaining = amount;
                for (int slot = 0; slot < gridItems.Length && remaining > 0; slot++)
                {
                    if (gridItems[slot] != item)
                        continue;
                    int taken = Math.Min(remaining, gridItems[slot]!.Count);
                    gridItems[slot]!.Count -= taken;
                    remaining -= taken;
                }
                for (int slot = 9; slot < 45 && remaining > 0; slot++)
                {
                    var source = inventory.GetItem(slot);
                    if (source != item)
                        continue;
                    int taken = Math.Min(remaining, source!.Count);
                    inventory.RemoveItem(slot, taken);
                    remaining -= taken;
                }
                Debug.Assert(remaining == 0, "Recipe placement must consume exactly the selected stock.");
                if (remaining != 0)
                    return;
                placedItems.Add(new ItemStack(item, amount));
            }
        }

        // Plan on copies so a full inventory cannot lose ingredients when the grid is rearranged.
        foreach (var item in gridItems)
        {
            if (item.IsNullOrAir() || item.Count <= 0)
                continue;
            StoreCraftingItem(inventory, item, 9, 45);
            if (item.Count > 0)
                return;
        }

        for (int slot = 9; slot < 45; slot++)
            player.Inventory.SetItem(slot, inventory.GetItem(slot));
        for (int slot = 1; slot <= width * width; slot++)
            grid.SetItem(slot, null);

        int ingredientIndex = 0;
        if (amount > 0 && recipe is ShapedRecipe pattern)
        {
            int startX = (width - pattern.Pattern[0].Length) / 2;
            int startY = (width - pattern.Pattern.Count) / 2;
            for (int y = 0; y < pattern.Pattern.Count; y++)
            {
                for (int x = 0; x < pattern.Pattern[y].Length; x++)
                {
                    if (pattern.Pattern[y][x] != ' ')
                        grid.SetItem(1 + (startY + y) * width + startX + x, placedItems[ingredientIndex++]);
                }
            }
        }
        else
        {
            foreach (var item in placedItems)
                grid.SetItem(++ingredientIndex, item);
        }

        player.DraggedSlots.Clear();
        player.IsDragging = false;
        RefreshCraftingResult(grid, width);
        var contents = grid.ToList();
        if (width == 3)
            contents.AddRange(player.Inventory.Skip(9).Take(36));
        await player.QueuePacketAsync(new ContainerSetContentPacket(containerId, contents)
        {
            CarriedItem = player.CarriedItem
        });
        if (amount == 0)
            await player.QueuePacketAsync(new PlaceGhostRecipePacket(containerId, recipe.Identifier));
    }

    private static void AddRecipeStock(List<ItemStack> stock, ItemStack item)
    {
        var existing = stock.FirstOrDefault(candidate => candidate == item);
        if (existing is null)
            stock.Add(new ItemStack(item, item.Count));
        else
            existing.Count += item.Count;
    }

    private static bool TrySelectRecipeStock(IReadOnlyList<Ingredient> ingredients, List<ItemStack> stock,
        int amount, out int[] selected)
    {
        var choices = ingredients.Select(ingredient => Enumerable.Range(0, stock.Count)
            .Where(index => stock[index].Count >= amount && stock[index].MaxStackSize >= amount &&
                ingredient.Any(item => item.Type == stock[index].Type)).ToArray()).ToArray();
        var order = Enumerable.Range(0, ingredients.Count).OrderBy(index => choices[index].Length).ToArray();
        var remaining = stock.Select(item => item.Count).ToArray();
        var assignment = new int[ingredients.Count];

        bool Assign(int position)
        {
            if (position == order.Length)
                return true;
            int ingredient = order[position];
            foreach (int index in choices[ingredient])
            {
                if (remaining[index] < amount)
                    continue;
                remaining[index] -= amount;
                assignment[ingredient] = index;
                if (Assign(position + 1))
                    return true;
                remaining[index] += amount;
            }
            return false;
        }

        bool found = Assign(0);
        selected = assignment;
        return found;
    }
}

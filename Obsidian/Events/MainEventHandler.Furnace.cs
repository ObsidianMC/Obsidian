using Obsidian.API.Containers;
using Obsidian.API.Crafting;
using Obsidian.API.Events;
using Obsidian.API.Inventory;

namespace Obsidian.Events;

public partial class MainEventHandler
{
    /// <summary>
    /// Whether furnaces burn this stack: whether it has a <c>minecraft:cooking_fuel</c> (vanilla's
    /// <c>AbstractFurnaceMenu.isFuel</c>).
    /// </summary>
    internal static bool IsFuel(ItemStack item) => WorldData.ItemFuels.CookingFuel(item) is not null;

    // A vanilla furnace, blast furnace or smoker, rather than a plugin's container of another shape.
    private static bool IsFurnace(SmeltingContainer container) =>
        container.Size == 3
        && container.Type is InventoryType.Furnace or InventoryType.BlastFurnace or InventoryType.Smoker;

    /// <summary>
    /// Shift-clicks a stack in a furnace's window like vanilla's furnace menu: from the furnace (its ingredient, fuel
    /// or result) to the player's inventory, the result filling from the hotbar's last slot; from the player's
    /// inventory to the ingredient slot if the furnace cooks it, else to the fuel slot if it burns, else between the
    /// main inventory and the hotbar.
    /// </summary>
    /// <remarks>
    /// Like vanilla, a stack that the furnace cooks but whose ingredient slot is full stays, even if it burns too.
    /// </remarks>
    /// <returns>Whether anything moved.</returns>
    private static bool MoveFurnaceItem(ContainerClickEventArgs args, SmeltingContainer furnace)
    {
        var player = args.Player;
        var inventory = player.Inventory;
        var slot = args.ClickedSlot;
        var item = args.SlotItem;

        if (item.IsNullOrAir())
            return false;

        if (args.Container == furnace)
            return MoveStack(player, furnace, slot, inventory, 9, 45, reverse: slot == 2);

        var recipeType = furnace.Type switch
        {
            InventoryType.BlastFurnace => CraftingType.Blasting,
            InventoryType.Smoker => CraftingType.Smoking,
            _ => CraftingType.Smelting
        };

        if (PlayerRecipes(player).IsCookingIngredient(recipeType, item))
            return MoveStack(player, inventory, slot, furnace, 0, 1, reverse: false);

        if (IsFuel(item))
            return MoveStack(player, inventory, slot, furnace, 1, 2, reverse: false);

        return slot < 36
            ? MoveStack(player, inventory, slot, inventory, 36, 45, reverse: false)
            : MoveStack(player, inventory, slot, inventory, 9, 36, reverse: false);
    }
}

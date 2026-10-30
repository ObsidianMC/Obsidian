using Obsidian.API.Containers;
using Obsidian.API.Events;
using Obsidian.WorldData;

namespace Obsidian.Events;

public partial class MainEventHandler
{
    /// <summary>
    /// Shift-clicks a stack in a brewing stand's window like vanilla's brewing menu: from the stand to the player's
    /// inventory, filling from the hotbar's last slot; from the player's inventory, fuel to the fuel slot, then
    /// ingredients to the ingredient slot, then bottles to the bottle slots, else between the main inventory and the
    /// hotbar.
    /// </summary>
    /// <remarks>
    /// Like vanilla's, a stack that tops up the fuel stops there, even when it's an ingredient too (blaze powder), and
    /// goes to the ingredient slot only when the fuel slot takes none of it; and an ingredient or bottle whose slots
    /// are full stays where it is.
    /// </remarks>
    /// <returns>Whether anything moved and the routing goes on, which a stack moved to the fuel slot doesn't.</returns>
    private static bool MoveBrewingItem(ContainerClickEventArgs args, BrewingStand stand)
    {
        var player = args.Player;
        var inventory = player.Inventory;
        var slot = args.ClickedSlot;
        var item = args.SlotItem;

        if (item.IsNullOrAir())
            return false;

        if (args.Container == stand)
            return MoveStack(player, stand, slot, inventory, 9, 45, reverse: true);

        const int fuel = BrewingStand.FuelSlot;
        var recipes = PlayerRecipes(player);
        const int ingredient = BrewingStand.IngredientSlot;

        if (Brewing.IsFuel(item))
        {
            if (MoveStack(player, inventory, slot, stand, fuel, fuel + 1, reverse: false))
                return false;

            return Brewing.IsIngredient(item, recipes)
                && MoveStack(player, inventory, slot, stand, ingredient, ingredient + 1, reverse: false);
        }

        if (Brewing.IsIngredient(item, recipes))
            return MoveStack(player, inventory, slot, stand, ingredient, ingredient + 1, reverse: false);

        if (Brewing.IsBottle(item, recipes))
            return MoveStack(player, inventory, slot, stand, BrewingStand.FirstBottleSlot, 3, reverse: false);

        return slot < 36
            ? MoveStack(player, inventory, slot, inventory, 36, 45, reverse: false)
            : MoveStack(player, inventory, slot, inventory, 9, 36, reverse: false);
    }
}

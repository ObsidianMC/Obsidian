using Obsidian.API.Containers;
using Obsidian.API.Crafting;
using Obsidian.API.Events;
using Obsidian.API.Inventory;
using System.Collections.Frozen;

namespace Obsidian.Events;

public partial class MainEventHandler
{
    // Vanilla's fuels (FuelValues.vanillaBurnTimes), which decide where shift-clicking sends a stack and what a
    // furnace's fuel slot takes. Only whether an item burns matters so far, so their burn times aren't kept.
    private static readonly FrozenSet<int> fuelItems = new[]
    {
        ItemIds.LavaBucket, ItemIds.CoalBlock, ItemIds.BlazeRod, ItemIds.Coal, ItemIds.Charcoal,
        ItemIds.BambooMosaic, ItemIds.BambooMosaicStairs, ItemIds.BambooMosaicSlab, ItemIds.NoteBlock,
        ItemIds.Bookshelf, ItemIds.ChiseledBookshelf, ItemIds.Lectern, ItemIds.Jukebox, ItemIds.Chest,
        ItemIds.TrappedChest, ItemIds.CraftingTable, ItemIds.DaylightDetector, ItemIds.Bow, ItemIds.FishingRod,
        ItemIds.Ladder, ItemIds.WoodenShovel, ItemIds.WoodenSword, ItemIds.WoodenSpear, ItemIds.WoodenHoe,
        ItemIds.WoodenAxe, ItemIds.WoodenPickaxe, ItemIds.Stick, ItemIds.Bowl, ItemIds.DriedKelpBlock,
        ItemIds.Crossbow, ItemIds.Bamboo, ItemIds.DeadBush, ItemIds.ShortDryGrass, ItemIds.TallDryGrass,
        ItemIds.Scaffolding, ItemIds.Loom, ItemIds.Barrel, ItemIds.CartographyTable, ItemIds.FletchingTable,
        ItemIds.SmithingTable, ItemIds.Composter, ItemIds.Azalea, ItemIds.FloweringAzalea, ItemIds.MangroveRoots,
        ItemIds.LeafLitter
    }.ToFrozenSet();

    private static readonly Tag[] fuelTags =
    [
        TagsRegistry.Item.Logs, TagsRegistry.Item.BambooBlocks, TagsRegistry.Item.Planks,
        TagsRegistry.Item.WoodenStairs, TagsRegistry.Item.WoodenSlabs, TagsRegistry.Item.WoodenTrapdoors,
        TagsRegistry.Item.WoodenPressurePlates, TagsRegistry.Item.WoodenShelves, TagsRegistry.Item.WoodenFences,
        TagsRegistry.Item.FenceGates, TagsRegistry.Item.Banners, TagsRegistry.Item.Signs,
        TagsRegistry.Item.HangingSigns, TagsRegistry.Item.WoodenDoors, TagsRegistry.Item.Boats, TagsRegistry.Item.Wool,
        TagsRegistry.Item.WoodenButtons, TagsRegistry.Item.Saplings, TagsRegistry.Item.WoolCarpets
    ];

    /// <summary>
    /// Whether <paramref name="item"/> burns in a furnace, like vanilla's fuels: the ones it lists, except wood that
    /// doesn't burn (crimson and warped).
    /// </summary>
    private static bool IsFuel(ItemStack item)
    {
        var id = item.Holder.Id;
        if (TagsRegistry.Item.NonFlammableWood.Entries.Contains(id))
            return false;

        return fuelItems.Contains(id) || fuelTags.Any(tag => tag.Entries.Contains(id));
    }

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

        if (RecipesRegistry.IsCookingIngredient(recipeType, item))
            return MoveStack(player, inventory, slot, furnace, 0, 1, reverse: false);

        if (IsFuel(item))
            return MoveStack(player, inventory, slot, furnace, 1, 2, reverse: false);

        return slot < 36
            ? MoveStack(player, inventory, slot, inventory, 36, 45, reverse: false)
            : MoveStack(player, inventory, slot, inventory, 9, 36, reverse: false);
    }
}

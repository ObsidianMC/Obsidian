using Obsidian.API.Containers;
using Obsidian.API.Events;
using Obsidian.API.Crafting;
using Obsidian.API.Inventory;
using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.WorldData;
using Obsidian.WorldData.Furnaces;

namespace Obsidian.Events;

public partial class MainEventHandler
{
    /// <summary>
    /// Vanilla <c>CampfireBlock.useItemOn</c>: food held against a lit campfire goes into its first empty slot. The held item
    /// is taken on success, except in creative.
    /// </summary>
    private static async ValueTask CookOnCampfireAsync(Player player, AbstractLevel level, Vector position, ItemStack food, InteractionHand hand)
    {
        var slot = hand == InteractionHand.OffHand ? 45 : player.CurrentHeldItemSlot;
        var held = player.Inventory.GetItem(slot);
        if (held is null || held.IsAir || held.Type != food.Type)
            return;

        if (!level.TryPlaceCampfireFood(position, held))
            return;

        if (player.GameMode == GameMode.Creative)
            return;

        held.Count--;
        player.Inventory.SetItem(slot, held.Count > 0 ? held : null);
        await player.Client.QueuePacketAsync(new ContainerSetSlotPacket
        {
            ContainerId = 0, Slot = (short)slot, SlotData = player.Inventory.GetItem(slot)
        });
    }
    private const short FurnaceIngredientSlot = 0;
    private const short FurnaceFuelSlot = 1;
    private const short FurnaceResultSlot = 2;

    /// <summary>
    /// Enforces furnace slot rules: the result slot takes no placements, the fuel slot takes only fuel, and quick-move from the
    /// inventory goes to the ingredient or fuel slot it belongs in. Returns true when the click was handled here.
    /// </summary>
    private bool HandleSmeltingClick(ContainerClickEventArgs args)
    {
        if (args.Container is not SmeltingContainer furnace)
            return false;

        var player = args.Player;
        var slot = args.ClickedSlot;

        switch (args.ClickType)
        {
            case ClickType.QuickMove when args.Container != player.Inventory && slot >= 0 && slot < furnace.Size:
                return false;
            case ClickType.QuickMove when args.Container == player.Inventory:
                return MoveIntoFurnace(player, furnace, slot);
            case ClickType.Pickup or ClickType.QuickCraft when !player.CarriedItem.IsNullOrAir() && slot is FurnaceFuelSlot or FurnaceResultSlot:
                return !CanPlaceInFurnace(furnace, slot, player.CarriedItem);
            case ClickType.Swap when args.Button is >= 0 and <= 8 or 40 && slot is FurnaceFuelSlot or FurnaceResultSlot:
                var incoming = args.Button == 40 ? player.GetOffHandItem() : player.Inventory.GetItem(36 + args.Button);
                return !incoming.IsNullOrAir() && !CanPlaceInFurnace(furnace, slot, incoming);
            default:
                return false;
        }
    }

    private static bool CanPlaceInFurnace(SmeltingContainer furnace, short slot, ItemStack item) => slot switch
    {
        FurnaceFuelSlot => FurnaceRules.IsFuel(item),
        FurnaceResultSlot => false,
        _ => true,
    };

    /// <summary>Moves a stack from the player's inventory into the furnace slot it belongs in, like vanilla shift-click.</summary>
    private static bool MoveIntoFurnace(IPlayer player, SmeltingContainer furnace, short fromSlot)
    {
        var source = player.Inventory.GetItem(fromSlot);
        if (source.IsNullOrAir())
            return true;

        short target;
        if (FurnaceRules.FindRecipe(FurnaceRules.KindOf(furnace.Type), source) is not null)
            target = FurnaceIngredientSlot;
        else if (FurnaceRules.IsFuel(source))
            target = FurnaceFuelSlot;
        else
            return true;

        var destination = furnace.GetItem(target);
        if (destination.IsNullOrAir())
        {
            furnace.SetItem(target, new ItemStack(source.Holder, source.Count));
            player.Inventory.RemoveItem(fromSlot);
            return true;
        }

        if (destination.Type != source.Type)
            return true;

        var moved = Math.Min(destination.MaxStackSize - destination.Count, source.Count);
        if (moved <= 0)
            return true;

        furnace.SetItem(target, new ItemStack(destination.Holder, destination.Count + moved));
        if (source.Count - moved <= 0)
            player.Inventory.RemoveItem(fromSlot);
        else
            player.Inventory.SetItem(fromSlot, new ItemStack(source.Holder, source.Count - moved));
        return true;
    }
}

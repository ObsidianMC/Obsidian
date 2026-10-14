using Obsidian.API.Events;
using Obsidian.API.Inventory;
using Obsidian.Entities;
using Obsidian.Plugins;

namespace Obsidian.Events;

// Clicks in plugins' menus: shift-clicks routed by the menu, and the part of a click that runs the menu's plugin and
// moves items through its item access slots.
public partial class MainEventHandler
{
    /// <summary>
    /// Shift-clicks a slot of a plugin's menu window once: the stack goes to the first of the menu's ranges (see
    /// <see cref="MenuContainer.QuickMove"/>) that takes some of it.
    /// </summary>
    /// <returns>Whether anything moved.</returns>
    private static bool MoveMenuItem(ContainerClickEventArgs args, MenuContainer menu)
    {
        var player = args.Player;
        var source = args.Container;
        var slot = args.ClickedSlot;
        var stack = source.GetItem(slot);

        if (stack.IsNullOrAir())
            return false;

        // The player's main inventory and hotbar (their slots 9 to 44) follow the menu's slots in its window.
        var windowSlot = source == menu ? slot : menu.Size + slot - 9;

        foreach (var range in menu.QuickMove(player, windowSlot, new ItemStack(stack, stack.Count)))
        {
            if (!MoveItemStackTo(player, stack, WindowRange(player, menu, range, windowSlot)))
                continue;

            source.SetItem(slot, stack.Count > 0 ? stack : null);

            return true;
        }

        return false;
    }

    // A range of a menu window's slots, in the order it's filled, as the containers that keep them number them; the
    // clicked slot is left out.
    private static (BaseContainer To, int Slot)[] WindowRange(
        IPlayer player,
        MenuContainer menu,
        MenuSlotRange range,
        int clicked)
    {
        var end = Math.Min(range.End, menu.Size + MenuContainer.PlayerSlotCount);
        var slots = Enumerable.Range(Math.Max(0, range.Start), Math.Max(0, end - Math.Max(0, range.Start)))
            .Where(slot => slot != clicked)
            .Select(slot => slot < menu.Size
                ? ((BaseContainer)menu, slot)
                : (player.Inventory, slot - menu.Size + 9));

        return range.Reverse ? [.. slots.Reverse()] : [.. slots];
    }

    /// <summary>
    /// What a click in a plugin's menu adds to a chest's, inside the window's lock: it runs inside a call into the
    /// menu's plugin, only while the menu is valid for the player (see <see cref="Player.IsMenuValid"/>); the items it
    /// moves in and out of item access slots move through the access as it ends (see
    /// <see cref="MenuContainer.CommitStaged"/>); and what it drops waits until then.
    /// </summary>
    /// <remarks>
    /// A click that fails as it ends, or throws, is undone: the player's inventory and cursor and the menu's other
    /// slots get back what they held as it started, which the window's lock kept for it, and nothing drops. So the
    /// click never makes or loses an item, whatever the plugin's code and item access do meanwhile.
    /// </remarks>
    internal sealed class MenuClick : IDisposable
    {
        [ThreadStatic]
        private static MenuClick? current;

        private readonly IPlayer player;

        private readonly MenuContainer menu;

        private readonly IDisposable? call;

        private readonly MenuClick? outer;

        // What the player's inventory, their cursor and the menu's slots not behind item access held as it started.
        private readonly ItemStack?[] inventory = [];

        private readonly ItemStack? cursor;

        private readonly ItemStack?[] slots = [];

        private readonly List<ItemStack> drops = [];

        private bool ended;

        private MenuClick(IPlayer player, MenuContainer menu, IDisposable? call, bool refused)
        {
            this.player = player;
            this.menu = menu;
            this.call = call;
            this.Refused = refused;

            if (refused)
            {
                this.ended = true;
                return;
            }

            this.inventory = [.. player.Inventory.GetItems().Select(Copy)];
            this.cursor = Copy(player.CarriedItem);
            this.slots = new ItemStack?[menu.Size];

            for (var slot = 0; slot < menu.Size; slot++)
            {
                if (!this.KeptInAccess(slot))
                    this.slots[slot] = Copy(menu[slot]);
            }

            if (menu.HasItemAccessSlots)
                menu.BeginStaging();

            this.outer = current;
            current = this;
        }

        /// <summary>
        /// The click in a plugin's menu running on this thread, if any.
        /// </summary>
        public static MenuClick? Current => current;

        /// <summary>
        /// Whether the menu doesn't take the click: its plugin is unloading, or it's no longer valid for the player.
        /// </summary>
        public bool Refused { get; }

        /// <summary>
        /// Starts a click in <paramref name="window"/> if it's a plugin's menu. Call it with the window's lock held.
        /// </summary>
        /// <returns>The click, to dispose with the lock; null for other windows.</returns>
        public static MenuClick? Start(IPlayer player, BaseContainer window)
        {
            if (window is not MenuContainer menu)
                return null;

            var plugin = menu.Menu.Owner as PluginContainer;
            var call = plugin?.TryEnter();

            var refused = plugin is not null && call is null || !Player.IsMenuValid(player, menu);

            return new MenuClick(player, menu, call, refused);
        }

        /// <summary>
        /// Keeps an item the click drops until it ends: it drops only if the click holds.
        /// </summary>
        public void Drop(ItemStack item) => this.drops.Add(item);

        /// <summary>
        /// Ends the click: moves its items through the item access slots, then drops what it dropped and gives the
        /// player what the access didn't take back; or undoes the click when the access no longer holds what it took.
        /// </summary>
        /// <returns>Whether the click holds.</returns>
        public bool Commit()
        {
            if (this.ended)
                return !this.Refused;

            var leftovers = new List<ItemStack>();
            var applied = !this.menu.HasItemAccessSlots || this.menu.CommitStaged(leftovers);

            this.End();

            if (applied)
            {
                foreach (var item in this.drops)
                    ItemEntity.Drop(this.player, item);
            }
            else
            {
                this.Restore();
            }

            foreach (var item in leftovers)
                GiveBack(this.player, item);

            return applied;
        }

        /// <summary>
        /// Undoes a click that threw: nothing it did to the slots stays, and nothing drops.
        /// </summary>
        public void Undo()
        {
            if (this.ended)
                return;

            this.End();
            this.Restore();
        }

        public void Dispose()
        {
            if (!this.ended)
                this.End();

            this.call?.Dispose();
        }

        // Ends the click's staging, so that the item access slots read what the access holds again.
        private void End()
        {
            this.ended = true;

            if (this.menu.HasItemAccessSlots)
                this.menu.EndStaging();

            current = this.outer;
        }

        private void Restore()
        {
            var playerInventory = this.player.Inventory;

            for (var slot = 0; slot < this.inventory.Length; slot++)
                playerInventory.SetItem(slot, this.inventory[slot]);

            this.player.CarriedItem = this.cursor;

            for (var slot = 0; slot < this.slots.Length; slot++)
            {
                if (!this.KeptInAccess(slot))
                    this.menu.SetItem(slot, this.slots[slot]);
            }

            ResetDrag(this.player);
        }

        private bool KeptInAccess(int slot) => this.menu.Slots[slot].Items is not null;

        private static ItemStack? Copy(ItemStack? item) => item is null ? null : new ItemStack(item, item.Count);

        // Gives the player an item that came out of the menu and has nowhere else to go, like a stack swapped out of a
        // slot: into their inventory, dropping what doesn't fit.
        private static void GiveBack(IPlayer player, ItemStack item)
        {
            Player.AddToInventory(player.Inventory, player.CurrentHeldItemSlot, item, infiniteMaterials: false);

            if (item.Count > 0)
                ItemEntity.Drop(player, item);
        }
    }
}

using Obsidian.API.Inventory;

namespace Obsidian.API.Containers;

/// <summary>
/// A crafter's grid, like vanilla's <c>CrafterBlockEntity</c>: nine slots, each of which can be disabled while
/// empty, and whether it's powered.
/// </summary>
public sealed class CrafterContainer : BaseContainer
{
    // Its slots are its own, behind its own lock (see LocksOnlyItself).
    internal override bool LocksOnlyItself => true;

    /// <summary>
    /// The number of grid slots, 3 by 3.
    /// </summary>
    public const int GridSize = 9;

    // The crafter window's result slot, after the player's main inventory and hotbar.
    /// <summary>
    /// The index of the crafter window's result slot, after the grid and the player's inventory.
    /// </summary>
    public const int ResultWindowSlot = GridSize + 36;

    private readonly bool[] disabled = new bool[GridSize];

    /// <summary>
    /// Gets or sets the position of the crafter block.
    /// </summary>
    public Vector BlockPosition { get; set; }

    /// <summary>
    /// Whether a redstone signal powers the crafter, which it crafts on as it rises.
    /// </summary>
    public bool Triggered { get; set; }

    /// <summary>
    /// The game ticks left while it shows itself crafting.
    /// </summary>
    public int CraftingTicksRemaining { get; set; }

    /// <summary>
    /// What its grid crafts now, shown in its window's result slot.
    /// </summary>
    public ItemStack? Result { get; set; }

    /// <summary>
    /// Creates a crafter's nine empty, enabled grid slots.
    /// </summary>
    public CrafterContainer() : base(GridSize, InventoryType.Crafter)
    {
        this.Title = "Crafter";
    }

    /// <summary>
    /// Whether grid slot <paramref name="slot"/> is disabled; false for an index outside the grid.
    /// </summary>
    /// <remarks>
    /// Only players' clicks in the window check it: <see cref="BaseContainer.SetItem"/> and
    /// <see cref="BaseContainer.AddItem"/> still put items in a disabled slot.
    /// </remarks>
    public bool IsSlotDisabled(int slot) => slot is >= 0 and < GridSize && this.disabled[slot];

    /// <summary>
    /// Disables or enables grid slot <paramref name="slot"/>, moving <see cref="BaseContainer.Revision"/> on; an index
    /// outside the grid is ignored.
    /// </summary>
    public void SetSlotDisabled(int slot, bool disabled)
    {
        if (slot is >= 0 and < GridSize)
        {
            this.disabled[slot] = disabled;
            this.MarkChanged();
        }
    }
}

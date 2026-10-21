using Obsidian.API.Inventory;

namespace Obsidian.API.Containers;

/// <summary>
/// A crafter's grid, like vanilla's <c>CrafterBlockEntity</c>: nine slots, each of which can be disabled while
/// empty, and whether it's powered.
/// </summary>
public sealed class CrafterContainer : BaseContainer
{
    public const int GridSize = 9;

    // The crafter window's result slot, after the player's main inventory and hotbar.
    public const int ResultWindowSlot = GridSize + 36;

    private readonly bool[] disabled = new bool[GridSize];

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

    public CrafterContainer() : base(GridSize, InventoryType.Crafter)
    {
        this.Title = "Crafter";
    }

    public bool IsSlotDisabled(int slot) => slot is >= 0 and < GridSize && this.disabled[slot];

    public void SetSlotDisabled(int slot, bool disabled)
    {
        if (slot is >= 0 and < GridSize)
        {
            this.disabled[slot] = disabled;
            this.MarkChanged();
        }
    }
}

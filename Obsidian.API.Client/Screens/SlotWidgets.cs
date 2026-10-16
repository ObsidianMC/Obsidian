using Obsidian.API.Inventory;

namespace Obsidian.API.Client.Screens;

/// <summary>
/// A grid of the open menu's slots, 18 pixels apart, which the player clicks, drags and shift-clicks like any
/// container's: the client draws their stacks and tooltips and sends the clicks to the server. Only a
/// <see cref="ModMenuScreen"/> shows it.
/// </summary>
/// <remarks>
/// The grid's position is where its first slot's item goes (vanilla's slot frames are 18 pixels, one around each
/// 16-pixel item), like <see cref="MenuScreenProperties.Slots"/>. It's as big as its slots; its
/// <see cref="Widget.Width"/> and <see cref="Widget.Height"/> aren't used.
/// </remarks>
public sealed class SlotGrid : Widget
{
    /// <summary>
    /// The menu's slot in the grid's top left: the menu's own slots count from 0, then the player's inventory follows
    /// (see <see cref="IScreenMenu.SlotCount"/>).
    /// </summary>
    public int FirstSlot { get; set; }

    /// <summary>
    /// How many slots each row has.
    /// </summary>
    public int Columns { get; set; } = 1;

    /// <summary>
    /// How many rows there are; the slots are numbered row by row.
    /// </summary>
    public int Rows { get; set; } = 1;
}

/// <summary>
/// A grid of item stacks that aren't the menu's slots, like an AE2 terminal's network contents or a list of recipes:
/// the client draws them 18 pixels apart, with their tooltips, and raises <see cref="Clicked"/> when the player clicks
/// one; the mod decides what that does. Scroll it with a <see cref="ScrollBar"/> by setting <see cref="FirstRow"/>.
/// </summary>
/// <remarks>
/// The grid's position is where its first item goes, as for <see cref="SlotGrid"/>, and it's as big as its cells; its
/// <see cref="Widget.Width"/> and <see cref="Widget.Height"/> aren't used.
/// </remarks>
public sealed class ItemGrid : Widget
{
    /// <summary>
    /// The stacks, row by row; null for an empty cell.
    /// </summary>
    public IReadOnlyList<ItemStack?> Items { get; set; } = [];

    /// <summary>
    /// How many cells each row has.
    /// </summary>
    public int Columns { get; set; } = 9;

    /// <summary>
    /// How many rows are shown.
    /// </summary>
    public int Rows { get; set; } = 1;

    /// <summary>
    /// The row of <see cref="Items"/> shown at the top.
    /// </summary>
    public int FirstRow { get; set; }

    /// <summary>
    /// Raised when the player clicks a cell, even an empty one.
    /// </summary>
    public event Action<ItemGridClick>? Clicked;

    // Called by the client.
    internal void Click(ItemGridClick click) => this.Clicked?.Invoke(click);
}

/// <summary>
/// A click on an <see cref="ItemGrid"/>'s cell.
/// </summary>
/// <param name="Index">
/// The cell's index in <see cref="ItemGrid.Items"/>, scrolled rows included; it can be past the list's end for an
/// empty cell.
/// </param>
/// <param name="Button">The mouse button: 0 left, 1 right, 2 middle, like vanilla's.</param>
/// <param name="Shift">Whether Shift was held.</param>
public readonly record struct ItemGridClick(int Index, int Button, bool Shift);

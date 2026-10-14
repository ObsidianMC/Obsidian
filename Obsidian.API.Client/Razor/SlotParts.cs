using Microsoft.AspNetCore.Components;
using Obsidian.API.Client.Screens;
using Obsidian.API.Inventory;

namespace Obsidian.API.Client.Razor;

/// <summary>
/// The menu's slots from <see cref="FirstSlot"/>, <see cref="Columns"/> by <see cref="Rows"/>, 18 pixels apart, like
/// <see cref="Screens.SlotGrid"/>. Its position is where its first item goes from the top left of the screen's panel,
/// wherever it is in the markup: the menu's container screen draws the slots, which the player clicks, drags,
/// shift-clicks and swaps with hotbar keys like any container's.
/// </summary>
public sealed class SlotGrid : ScreenPart
{
    [Parameter]
    public int FirstSlot { get; set; }

    [Parameter]
    public int Columns { get; set; } = 1;

    [Parameter]
    public int Rows { get; set; } = 1;
}

/// <summary>
/// Stacks that aren't the menu's slots, like <see cref="Screens.ItemGrid"/>: <see cref="Items"/> row by row,
/// <see cref="Columns"/> by <see cref="Rows"/> from <see cref="FirstRow"/>, with their tooltips. Its size is its
/// cells', 18 pixels each.
/// </summary>
public sealed class ItemGrid : ScreenPart
{
    /// <summary>
    /// The stacks, which the client copies as it draws them.
    /// </summary>
    [Parameter]
    public IReadOnlyList<ItemStack?> Items { get; set; } = [];

    [Parameter]
    public int Columns { get; set; } = 9;

    [Parameter]
    public int Rows { get; set; } = 1;

    /// <summary>
    /// The row shown first.
    /// </summary>
    [Parameter]
    public int FirstRow { get; set; }

    /// <summary>
    /// Raised with the new first row as the player turns the mouse wheel over the grid, up to the last row of
    /// <see cref="Items"/>; bind it with <c>@bind-FirstRow</c>. Without it, the wheel does nothing.
    /// </summary>
    [Parameter]
    public EventCallback<int> FirstRowChanged { get; set; }

    /// <summary>
    /// Raised with the clicked cell's index in <see cref="Items"/> (even past its end), the mouse button (0 left, 1
    /// right, 2 middle, like vanilla's) and Shift.
    /// </summary>
    [Parameter]
    public EventCallback<ItemGridClick> OnClick { get; set; }
}

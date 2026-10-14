namespace Obsidian.API.Client;

/// <summary>
/// How the Obsidian client shows a mod's menu type, like a NeoForge mod's container screen: a background texture
/// from the mod's assets with the menu's slots and the player's inventory on it. Register it with
/// <see cref="IClientRegistry.RegisterMenuScreen"/>; without one, the menu shows like a chest, with its rows of slots
/// centred.
/// </summary>
/// <remarks>
/// Positions are in the GUI's pixels from the background's top left, and a slot's is where its item's top left goes
/// (vanilla's slot frames are 18 pixels, one around each 16-pixel item).
/// </remarks>
public sealed class MenuScreenProperties
{
    /// <summary>
    /// The background, for example <c>examplemod:gui/container/pouch</c> for
    /// <c>assets/examplemod/textures/gui/container/pouch.png</c>; its top left <see cref="Width"/> by
    /// <see cref="Height"/> pixels are drawn, like vanilla's container textures.
    /// </summary>
    public required string Texture { get; init; }

    /// <summary>
    /// The background's width, in pixels.
    /// </summary>
    public int Width { get; init; } = 176;

    /// <summary>
    /// The background's height, in pixels.
    /// </summary>
    public int Height { get; init; } = 166;

    /// <summary>
    /// Where the menu's slots go, one for each, in slot order (row by row).
    /// </summary>
    public required IReadOnlyList<GuiPosition> Slots { get; init; }

    /// <summary>
    /// Where the first slot of the player's inventory goes: its three rows of nine, then the hotbar 58 pixels below
    /// the first row, like vanilla's menus.
    /// </summary>
    public GuiPosition Inventory { get; init; } = new(8, 84);

    /// <summary>
    /// Where the menu's title goes.
    /// </summary>
    public GuiPosition Title { get; init; } = new(8, 6);

    /// <summary>
    /// Where the "Inventory" label goes; if null, 12 pixels above the player's inventory, like on vanilla's
    /// 176 by 166 screens.
    /// </summary>
    public GuiPosition? InventoryLabel { get; init; }
}

/// <summary>
/// A position on a screen, in the GUI's pixels.
/// </summary>
/// <param name="X">Pixels from the left.</param>
/// <param name="Y">Pixels from the top.</param>
public readonly record struct GuiPosition(int X, int Y);

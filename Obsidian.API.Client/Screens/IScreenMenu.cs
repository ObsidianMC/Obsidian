using Obsidian.API.Inventory;

namespace Obsidian.API.Client.Screens;

/// <summary>
/// The client's side of a menu the server opened, as a <see cref="ModMenuScreen"/> sees it: what the server sends
/// with it (its slots' stacks, its data slots' values and the extra data it was opened with), and its buttons.
/// </summary>
/// <remarks>
/// It's the server's menu as last sent: the values change as the server sends them, between the screen's ticks.
/// </remarks>
public interface IScreenMenu
{
    /// <summary>
    /// The menu type's identifier, like <c>examplemod:grinder</c>.
    /// </summary>
    public string Type { get; }

    /// <summary>
    /// The title the server opened the menu with.
    /// </summary>
    public string Title { get; }

    /// <summary>
    /// How many slots the menu type has. The player's inventory follows them in the menu: its 27 main slots row by row
    /// from <see cref="SlotCount"/>, then its 9 hotbar slots.
    /// </summary>
    public int SlotCount { get; }

    /// <summary>
    /// The stack in one of the menu's slots, the player's inventory included; null for an empty or unknown slot.
    /// </summary>
    public ItemStack? GetItem(int slot);

    /// <summary>
    /// The value of one of the menu's data slots, like a furnace's progress (the server's
    /// <c>MenuContainer.GetData</c>); 0 until the server sends it.
    /// </summary>
    public int GetData(int index);

    /// <summary>
    /// Reads the extra data the menu was opened with, which the mod's server side wrote in
    /// <c>MenuContainer.WriteOpenData</c>, from its start: a new reader each call. Empty when the server sent none.
    /// </summary>
    public INetStreamReader ReadOpenData();

    /// <summary>
    /// Sends a click on one of the screen's buttons to the server, like vanilla's <c>clickMenuButton</c>: the
    /// server's <c>MenuContainer.ClickButton</c> gets the number, which means whatever the menu says. Does nothing
    /// once the menu closed.
    /// </summary>
    public void ClickButton(int button);
}

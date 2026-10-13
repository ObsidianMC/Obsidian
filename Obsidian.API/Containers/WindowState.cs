using Obsidian.API.Inventory;

namespace Obsidian.API;

/// <summary>
/// What a player's open window shows, by identity and revision: the window and its id, the window's and the player's
/// inventory's <see cref="BaseContainer.Revision"/>, and the cursor's stack and its version. A state is current only
/// while nothing it covers changed.
/// </summary>
/// <remarks>
/// Take it before reading the contents it describes: contents read afterwards are then at least as new, and a change
/// made while they were read leaves the state behind.
/// </remarks>
internal readonly struct WindowState
{
    private readonly BaseContainer? window;

    private readonly int containerId;

    private readonly long windowRevision;

    private readonly long inventoryRevision;

    private readonly ItemStack? cursor;

    private readonly long cursorVersion;

    private WindowState(IPlayer player)
    {
        this.window = player.OpenedContainer ?? player.Inventory;
        this.containerId = player.OpenedContainer is null ? 0 : player.CurrentContainerId;
        this.windowRevision = this.window.Revision;
        this.inventoryRevision = player.Inventory.Revision;
        this.cursor = player.CarriedItem;
        this.cursorVersion = this.cursor?.Version ?? 0;
    }

    /// <summary>
    /// The state of <paramref name="player"/>'s open window now.
    /// </summary>
    public static WindowState Of(IPlayer player) => new(player);

    /// <summary>
    /// Whether <paramref name="player"/>'s open window still shows this state: the same window, under the same id,
    /// with nothing changed in it, in the player's inventory or on the cursor.
    /// </summary>
    public bool IsCurrent(IPlayer player)
    {
        var now = new WindowState(player);

        return ReferenceEquals(this.window, now.window)
            && this.containerId == now.containerId
            && this.windowRevision == now.windowRevision
            && this.inventoryRevision == now.inventoryRevision
            && ReferenceEquals(this.cursor, now.cursor)
            && this.cursorVersion == now.cursorVersion;
    }
}

using Obsidian.API.Inventory;

namespace Obsidian.API.Events;

/// <summary>
/// An event for when a player interacts with a item and/or block.
/// You can get which hand was used by the player when interacting by checking 
/// <seealso cref="PlayerInteractEventArgs.Hand"/>
/// </summary>
public sealed class PlayerInteractEventArgs : PlayerEventArgs, ICancellable
{
    /// <summary>
    /// The item that was being held when interacting.
    /// </summary>
    public ItemStack? Item { get; init; }

    /// <summary>
    /// The block that was clicked. Null if no block was clicked.
    /// </summary>
    public IBlock? Block { get; init; }

    /// <summary>
    /// The location of the clicked block. Null if no block was clicked.
    /// </summary>
    public Vector? BlockLocation { get; init; }

    public Hand Hand { get; init; }

    /// <inheritdoc />
    public bool IsCancelled { get; private set; }

    public PlayerInteractEventArgs(IPlayer player, IServer server) : base(player, server) { }

    /// <inheritdoc />
    public void Cancel()
    {
        IsCancelled = true;
    }
}

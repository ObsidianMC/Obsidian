namespace Obsidian.API.Events;

/// <summary>
/// An event for when a player interacts with a item and/or block.
/// You can get which hand was used by the player when interacting by checking 
/// <seealso cref="IPlayer.MainHand"/>
/// </summary>
public sealed class PlayerInteractEventArgs : BaseEventArgs, IPlayerEvent, ICancellable
{
    internal PlayerInteractEventArgs(ItemStack? item, IPlayer player)
    {
        Item = item;
        Player = player;
    }

    internal PlayerInteractEventArgs(ItemStack? item, IPlayer player, Block? block, Vector? blockLocation)
    {
        Item = item;
        Player = player;
        Block = block;
        BlockLocation = blockLocation;
    }

    /// <summary>
    /// The item that was being held when interacting.
    /// </summary>
    public ItemStack? Item { get; }

    /// <summary>
    /// The block that was clicked. Null if no block was clicked.
    /// </summary>
    public Block? Block { get; }

    /// <summary>
    /// The location of the clicked block. Null if no block was clicked.
    /// </summary>
    public Vector? BlockLocation { get; }

    public IPlayer Player { get; }
    public bool Cancelled { get; set; }
}

namespace Obsidian.API.Events;

/// <summary>
/// Interface containing information related to the block.
/// </summary>
public interface IBlockEvent : IWorldEvent {
    /// <summary>
    /// The block that was affected.
    /// </summary>
    Block Block { get; }

    /// <summary>
    /// Location of the block.
    /// </summary>
    Vector Location { get; }
}

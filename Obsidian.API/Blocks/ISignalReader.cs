namespace Obsidian.API.Blocks;

/// <summary>
/// Reads redstone power, like vanilla's <c>SignalGetter</c>. A block gives each neighbour a weak signal (it powers
/// the neighbour, like a lit torch powers the dust beside it) and a strong one (it also powers through the neighbour
/// when that's a conductor, like a torch powers the block above it). A conductor, like stone, passes on the strong
/// signals it gets as a weak signal to its own neighbours.
/// </summary>
/// <remarks>
/// Directions are vanilla's: a direction points from the block receiving the power to the block at the position
/// asked about, so a block reads its west neighbour with <c>GetSignal(position + west, West)</c>. Powers go from 0 to
/// 15.
/// </remarks>
public interface ISignalReader
{
    /// <summary>
    /// The power the block at <paramref name="position"/> gives the block it's asked from, like vanilla's
    /// <c>getSignal</c>: its weak signal toward it, or, for a conductor, the most strong power it receives itself.
    /// </summary>
    public int GetSignal(Vector position, BlockFace direction);

    /// <summary>
    /// The strong power the block at <paramref name="position"/> gives the block it's asked from, like vanilla's
    /// <c>getDirectSignal</c>.
    /// </summary>
    public int GetDirectSignal(Vector position, BlockFace direction);

    /// <summary>
    /// The most strong power any neighbour gives the block at <paramref name="position"/>, like vanilla's
    /// <c>getDirectSignalTo</c>: what a conductor there passes on.
    /// </summary>
    public int GetDirectSignalTo(Vector position);

    /// <summary>
    /// Whether <see cref="GetSignal"/> is above 0, like vanilla's <c>hasSignal</c>.
    /// </summary>
    public bool HasSignal(Vector position, BlockFace direction);

    /// <summary>
    /// Whether any neighbour of the block at <paramref name="position"/> powers it, like vanilla's
    /// <c>hasNeighborSignal</c>: what a lamp lights by.
    /// </summary>
    public bool HasNeighborSignal(Vector position);

    /// <summary>
    /// The most power any neighbour gives the block at <paramref name="position"/>, like vanilla's
    /// <c>getBestNeighborSignal</c>.
    /// </summary>
    public int GetBestNeighborSignal(Vector position);
}

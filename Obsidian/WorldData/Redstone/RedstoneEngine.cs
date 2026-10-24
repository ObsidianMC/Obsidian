using Obsidian.WorldData.BlockNotifications;
using Obsidian.WorldData.Features;

namespace Obsidian.WorldData.Redstone;

/// <summary>
/// The directional power queries of vanilla's <c>SignalGetter</c>, for a live level. Every read goes through the loaded
/// chunks: an unloaded position reads as no signal and is never loaded or generated.
/// </summary>
/// <remarks>
/// Direction convention: a query for a receiver and a direction looks at the neighbor on that side of the receiver, and passes
/// that direction from the receiver to the neighbor, so that a neighbor's <see cref="IRedstoneBehavior.GetSignal"/> knows which of
/// its own faces is toward the receiver. Neighbors are visited in <see cref="Directions"/> order, which is vanilla's
/// <c>Direction</c> order.
/// </remarks>
internal sealed class RedstoneEngine(IBlockChangeWorld world, RedstoneBehaviors behaviors)
{
    /// <summary>
    /// Vanilla <c>Direction</c> order: down, up, north, south, west, east.
    /// </summary>
    public static readonly BlockFace[] Directions = [BlockFace.Down, BlockFace.Up, BlockFace.North, BlockFace.South, BlockFace.West, BlockFace.East];

    /// <summary>
    /// The strongest signal (vanilla's maximum).
    /// </summary>
    public const int MaxSignal = 15;

    /// <summary>
    /// The signal a position gives toward <paramref name="direction"/>, including what a conductor passes on from its neighbors
    /// (vanilla <c>SignalGetter.getSignal</c>). <paramref name="wiresGiveSignal"/> is false while a wire works out its own power.
    /// </summary>
    public int GetSignal(Vector position, BlockFace direction, bool wiresGiveSignal = true)
    {
        var state = world.GetLoadedBlock(position);
        if (state is null)
            return 0;

        var signal = Clamp(behaviors.For(state).GetSignal(state, position, direction, wiresGiveSignal));
        return state.IsRedstoneConductor() ? Math.Max(signal, this.GetDirectSignalTo(position, wiresGiveSignal)) : signal;
    }

    /// <summary>
    /// The direct signal a position gives toward <paramref name="direction"/> (vanilla <c>SignalGetter.getDirectSignal</c>).
    /// </summary>
    public int GetDirectSignal(Vector position, BlockFace direction, bool wiresGiveSignal = true)
    {
        var state = world.GetLoadedBlock(position);
        return state is null ? 0 : Clamp(behaviors.For(state).GetDirectSignal(state, position, direction, wiresGiveSignal));
    }

    /// <summary>
    /// The strongest direct signal reaching <paramref name="position"/> from its six neighbors (vanilla <c>getDirectSignalTo</c>).
    /// </summary>
    public int GetDirectSignalTo(Vector position, bool wiresGiveSignal = true)
    {
        var best = 0;
        foreach (var face in Directions)
        {
            // Vanilla getDirectSignalTo (checked against bytecode) passes the direction from the receiver to the neighbour: the face itself.
            best = Math.Max(best, this.GetDirectSignal(position.Offset(face), face, wiresGiveSignal));
            if (best >= MaxSignal)
                return MaxSignal;
        }

        return best;
    }

    /// <summary>
    /// Whether the neighbor on <paramref name="direction"/> of <paramref name="receiver"/> gives it a signal.
    /// </summary>
    public bool HasSignal(Vector receiver, BlockFace direction) => this.GetSignal(receiver.Offset(direction), direction) > 0;

    /// <summary>
    /// Whether any of the six neighbors gives <paramref name="position"/> a signal (vanilla <c>hasNeighborSignal</c>).
    /// </summary>
    public bool HasNeighborSignal(Vector position)
    {
        foreach (var face in Directions)
            if (this.GetSignal(position.Offset(face), face) > 0)
                return true;

        return false;
    }

    /// <summary>
    /// The strongest signal from the six neighbors (vanilla <c>getBestNeighborSignal</c>).
    /// </summary>
    public int GetBestNeighborSignal(Vector position, bool wiresGiveSignal = true)
    {
        var best = 0;
        foreach (var face in Directions)
        {
            best = Math.Max(best, this.GetSignal(position.Offset(face), face, wiresGiveSignal));
            if (best >= MaxSignal)
                return MaxSignal;
        }

        return best;
    }

    private static int Clamp(int signal) => Math.Clamp(signal, 0, MaxSignal);
}

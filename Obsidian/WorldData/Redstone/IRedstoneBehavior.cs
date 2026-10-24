namespace Obsidian.WorldData.Redstone;

/// <summary>
/// What a block gives its neighbors, like vanilla's <c>BlockBehaviour.getSignal</c> and <c>getDirectSignal</c>. Blocks without a
/// registration give no signal.
/// </summary>
/// <remarks>
/// <paramref name="direction"/> is the direction from the receiver to this block, as in vanilla's <c>SignalGetter</c>. A block
/// that powers the block below it gets <see cref="BlockFace.Up"/> from that block.
/// </remarks>
internal interface IRedstoneBehavior
{
    /// <summary>
    /// The signal (0–15) this block gives its neighbor in <paramref name="direction"/>.
    /// </summary>
    int GetSignal(IBlock state, Vector position, BlockFace direction, bool wiresGiveSignal) => 0;

    /// <summary>
    /// The direct signal (0–15) this block gives its neighbor in <paramref name="direction"/>. Conductors pass the strongest of these
    /// on to every side.
    /// </summary>
    int GetDirectSignal(IBlock state, Vector position, BlockFace direction, bool wiresGiveSignal) => 0;
}

/// <summary>
/// Redstone behaviors keyed by vanilla block class (for example <c>RedStoneWireBlock</c>), so every state of a class shares one
/// rule. Registrations are made once when the level is created.
/// </summary>
internal sealed class RedstoneBehaviors
{
    private readonly Dictionary<string, IRedstoneBehavior> behaviors = [];

    public void Register(string blockClass, IRedstoneBehavior behavior)
    {
        if (!this.behaviors.TryAdd(blockClass, behavior))
            throw new InvalidOperationException($"A redstone behavior is already registered for {blockClass}.");
    }

    public IRedstoneBehavior For(IBlock state) => this.behaviors.GetValueOrDefault(state.BlockClass()) ?? Inert;

    private sealed class InertBehavior : IRedstoneBehavior { }

    private static readonly IRedstoneBehavior Inert = new InertBehavior();
}

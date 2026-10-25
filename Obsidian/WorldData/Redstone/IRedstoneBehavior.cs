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
/// What a block does to an entity whose box overlaps it, like vanilla's <c>entityInside</c> for redstone blocks (a detector rail).
/// </summary>
internal interface IRedstoneContact
{
    /// <summary>Runs the contact of an entity with the block at <paramref name="position"/>.</summary>
    ValueTask EntityInsideAsync(IRedstoneLevel level, Vector position, IBlock state);
}

/// <summary>
/// Redstone behaviors keyed by vanilla block class (for example <c>RedStoneWireBlock</c>), so every state of a class shares one
/// rule. Registrations are made once when the level is created.
/// </summary>
internal sealed class RedstoneBehaviors(IRedstoneBehavior? pluginBehaviors = null)
{
    private readonly Dictionary<string, IRedstoneBehavior> behaviors = [];
    private readonly Dictionary<string, IRedstoneContact> contacts = [];

    public void RegisterContact(string blockClass, IRedstoneContact contact)
    {
        if (!this.contacts.TryAdd(blockClass, contact))
            throw new InvalidOperationException($"A redstone contact is already registered for {blockClass}.");
    }

    public IRedstoneContact? ContactFor(IBlock state) => this.contacts.GetValueOrDefault(state.BlockClass());

    public void Register(string blockClass, IRedstoneBehavior behavior)
    {
        if (!this.behaviors.TryAdd(blockClass, behavior))
            throw new InvalidOperationException($"A redstone behavior is already registered for {blockClass}.");
    }

    /// <summary>
    /// The behavior of a block: its vanilla class's registration, else the plugin behaviors' (which answer for plugin blocks only),
    /// else none.
    /// </summary>
    public IRedstoneBehavior For(IBlock state) => this.behaviors.GetValueOrDefault(state.BlockClass()) ?? pluginBehaviors ?? Inert;

    private sealed class InertBehavior : IRedstoneBehavior { }

    private static readonly IRedstoneBehavior Inert = new InertBehavior();
}

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
/// Redstone behaviors registered by vanilla class and expanded to registry ids, so every state of a class shares one
/// rule. Registrations are made once when the level is created; plugin behavior is resolved dynamically.
/// </summary>
internal sealed class RedstoneBehaviors(IRedstoneBehavior? pluginBehaviors = null)
{
    private readonly Dictionary<string, IRedstoneBehavior> behaviors = [];
    private readonly IRedstoneBehavior?[] byId = new IRedstoneBehavior?[BlocksRegistry.ResourceIds.Length];
    public void Register(string blockClass, IRedstoneBehavior behavior)
    {
        if (!this.behaviors.TryAdd(blockClass, behavior))
            throw new InvalidOperationException($"A redstone behavior is already registered for {blockClass}.");

        foreach (var id in BlockPhysics.BlocksOfClass(blockClass))
            this.byId[id] = behavior;
    }

    /// <summary>
    /// The behavior of a block: its vanilla class's registration, else the plugin behaviors'
    /// (which answer for plugin blocks only), else none.
    /// </summary>
    public IRedstoneBehavior For(IBlock state)
    {
        var id = state.RegistryId;
        var behavior = (uint)id < (uint)this.byId.Length
            ? this.byId[id] : this.behaviors.GetValueOrDefault(state.BlockClass());

        return behavior ?? pluginBehaviors ?? Inert;
    }

    private sealed class InertBehavior : IRedstoneBehavior { }

    private static readonly IRedstoneBehavior Inert = new InertBehavior();
}

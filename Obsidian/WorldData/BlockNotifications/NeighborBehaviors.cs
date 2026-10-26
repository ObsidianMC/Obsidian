using System.Diagnostics.CodeAnalysis;

namespace Obsidian.WorldData.BlockNotifications;

/// <summary>
/// Reacts to a neighbor changing (vanilla <c>neighborChanged</c>). Behaviors read the committed world and may write through
/// <see cref="IBlockChangeWorld.SetBlockAsync"/>; those writes queue further work instead of running it.
/// </summary>
internal interface INeighborChangedBehavior
{
    ValueTask OnNeighborChangedAsync(NeighborChange change, IBlockChangeWorld world);
}

/// <summary>
/// Reacts when a block is placed at its position (vanilla <c>onPlace</c>): a wire works out its power, for example. Runs after the
/// block's state is committed.
/// </summary>
internal interface IPlacementBehavior
{
    /// <summary>
    /// Called only when the block type at <paramref name="change"/> changed, matching vanilla's <c>Block.onPlace</c> contract.
    /// Both states are supplied so a behavior can distinguish placement from a state-only mutation.
    /// </summary>
    ValueTask OnPlacedAsync(BlockChange change, IBlockChangeWorld world);
}

/// <summary>
/// Reacts when the block's type is replaced, for example when it is broken (vanilla <c>onRemove</c>). Runs after the block at the
/// position has been committed as the new state, so the old state is only available through <see cref="BlockChange.Old"/>.
/// </summary>
internal interface IRemovalBehavior
{
    ValueTask OnRemovedAsync(BlockChange change, IBlockChangeWorld world);
}

/// <summary>
/// A neighbor reaction for the block at <see cref="Position"/>, which sees <see cref="Source"/> changed to <see cref="SourceState"/>.
/// </summary>
internal sealed record NeighborChange(Vector Position, IBlock State, Vector Source, IBlock? SourceState);

/// <summary>
/// Neighbor behaviors keyed by block name. Blocks without a registration have no reaction.
/// </summary>
internal sealed class NeighborBehaviors
{
    private readonly Dictionary<string, INeighborChangedBehavior> behaviors = [];
    private readonly Dictionary<string, INeighborChangedBehavior> classBehaviors = [];
    private readonly Dictionary<string, IPlacementBehavior> placements = [];
    private readonly Dictionary<string, IRemovalBehavior> removals = [];

    public void RegisterRemoved(int blockId, IRemovalBehavior behavior) => this.RegisterRemoved(BlocksRegistry.GetDefault(blockId).UnlocalizedName, behavior);

    public void RegisterRemoved(string blockName, IRemovalBehavior behavior)
    {
        if (!this.removals.TryAdd(blockName, behavior))
            throw new InvalidOperationException($"A removal behavior is already registered for {blockName}.");
    }

    public bool TryGetRemoved(IBlock state, [MaybeNullWhen(false)] out IRemovalBehavior behavior) =>
        this.removals.TryGetValue(state.UnlocalizedName, out behavior);

    public void RegisterPlaced(int blockId, IPlacementBehavior behavior) => this.RegisterPlaced(BlocksRegistry.GetDefault(blockId).UnlocalizedName, behavior);

    public void RegisterPlaced(string blockName, IPlacementBehavior behavior)
    {
        if (!this.placements.TryAdd(blockName, behavior))
            throw new InvalidOperationException($"A placement behavior is already registered for {blockName}.");
    }

    public bool TryGetPlaced(IBlock state, [MaybeNullWhen(false)] out IPlacementBehavior behavior) =>
        this.placements.TryGetValue(state.UnlocalizedName, out behavior);

    /// <summary>Registers by generated block id (<see cref="Obsidian.API.Registries.BlockIds"/>); keys stay the block's unlocalized name.</summary>
    public void Register(int blockId, INeighborChangedBehavior behavior) => this.Register(BlocksRegistry.GetDefault(blockId).UnlocalizedName, behavior);

    public void Register(string blockName, INeighborChangedBehavior behavior)
    {
        if (!this.behaviors.TryAdd(blockName, behavior))
            throw new InvalidOperationException($"A neighbor behavior is already registered for {blockName}.");
    }

    public void RegisterClass(string blockClass, INeighborChangedBehavior behavior)
    {
        if (!this.classBehaviors.TryAdd(blockClass, behavior))
            throw new InvalidOperationException($"A neighbor behavior is already registered for {blockClass}.");
    }

    public bool TryGet(IBlock state, [MaybeNullWhen(false)] out INeighborChangedBehavior behavior) =>
        this.behaviors.TryGetValue(state.UnlocalizedName, out behavior) || this.classBehaviors.TryGetValue(state.BlockClass(), out behavior);
}

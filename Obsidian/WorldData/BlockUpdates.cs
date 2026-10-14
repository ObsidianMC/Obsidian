using Microsoft.Extensions.Logging;
using Obsidian.API.Blocks;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Plugins;
using Obsidian.WorldData.Fluids;
using Obsidian.WorldData.Redstone;
using Obsidian.WorldData.Ticks;
using System.Diagnostics.CodeAnalysis;

namespace Obsidian.WorldData;

/// <summary>
/// A live level's block ticks, and how plugin blocks, vanilla's redstone components and vanilla's falling blocks
/// react to block updates: plugin blocks run their behaviour's hooks (see <see cref="IBlockBehavior"/>), redstone
/// components their rules (see <see cref="VanillaRedstone"/>), and falling blocks schedule the tick they fall on.
/// It's the <see cref="IBlockLevel"/> those hooks get.
/// </summary>
/// <remarks>
/// Everything here runs under the level's tick lock (<see cref="LevelFluids.TickLock"/>), which the
/// <see cref="IBlockLevel"/> methods take for hooks' own calls. Liquids react through <see cref="FluidLevel"/>, which
/// runs the updates.
/// </remarks>
internal sealed partial class LevelBlockUpdates(AbstractLevel level, LevelFluids fluids, FluidLevel updates)
    : IBlockReactions, IBlockLevel
{
    // Vanilla FallingBlock.getDelayAfterPlace: falling blocks check below them this many ticks after they're placed or
    // a neighbour changes.
    private const int FallDelay = 2;

    /// <summary>
    /// The level's block tick scheduler, which the chunks' <see cref="Chunk.BlockTicks"/> are ticked by.
    /// </summary>
    public TickScheduler<string> Scheduler { get; } = new(fluids.Clock);

    /// <inheritdoc/>
    public ILevel Level => level;

    /// <inheritdoc/>
    public IRandomSource Random => level.GrowthRandom;

    /// <summary>
    /// Reads a saved block tick's block, by identifier. Ticks of blocks the server doesn't know are read too: like any
    /// tick, one only runs if its block is there when it's due.
    /// </summary>
    public static bool TryParseTickType(string name, [MaybeNullWhen(false)] out string type)
    {
        type = name;
        return name.Length > 0;
    }

    /// <inheritdoc/>
    public IBlock GetBlock(Vector position) => fluids.GetBlock(position);

    /// <inheritdoc/>
    public bool SetBlock(Vector position, IBlock block, BlockUpdateFlags flags = BlockUpdateFlags.All)
    {
        using var host = PluginCall.EnterHost();

        lock (fluids.TickLock)
            return updates.SetBlockOutsideFluids(position, block, flags);
    }

    /// <inheritdoc/>
    public bool RemoveBlock(Vector position)
    {
        lock (fluids.TickLock)
            return this.SetBlock(position, FluidState.Of(this.GetBlock(position)).CreateLegacyBlock());
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Like vanilla, ignored where the chunk isn't loaded and complete.
    /// </remarks>
    public void ScheduleTick(Vector position, IBlock block, int delay, TickPriority priority = TickPriority.Normal)
    {
        lock (fluids.TickLock)
        {
            if (fluids.TryGetChunk(position, out var chunk))
                chunk.BlockTicks.Schedule(position, block.UnlocalizedName, delay, (int)priority);
        }
    }

    /// <inheritdoc/>
    public bool HasScheduledTick(Vector position, IBlock block)
    {
        lock (fluids.TickLock)
        {
            return fluids.TryGetChunk(position, out var chunk)
                && chunk.BlockTicks.Contains(position, block.UnlocalizedName);
        }
    }

    /// <inheritdoc/>
    public void UpdateNeighbors(Vector position, IBlock source)
    {
        using var host = PluginCall.EnterHost();

        lock (fluids.TickLock)
            updates.UpdateNeighborsAt(position, source);
    }

    /// <summary>
    /// Vanilla <c>ServerLevel.tickBlock</c>: runs a scheduled tick if the block is still the one it was scheduled
    /// for. Plugin blocks run their <see cref="IBlockBehavior.Tick"/>, redstone components their own, and vanilla's
    /// falling blocks fall; other vanilla blocks' ticks (an observer's, from a vanilla save) do nothing yet.
    /// </summary>
    /// <returns>
    /// False to hold the tick: a plugin's block's tick waits while its plugin is unloading or unloaded, as it was
    /// scheduled, so that it's kept (and saved) until the plugin is back to run it.
    /// </returns>
    public bool RunScheduledTick(Vector position, string type)
    {
        var state = this.GetBlock(position);
        if (state.UnlocalizedName != type)
            return true;

        if (BlocksRegistry.TryGetPluginBlock(state.GetHashCode(), out var pluginBlock))
            return this.RunPluginTick(position, state, pluginBlock);

        if (VanillaRedstone.For(state) is RedstoneComponent component)
        {
            component.Tick(this, position, state);
            return true;
        }

        if (IsVanillaFalling(state))
            this.Fall(position, state);

        return true;
    }

    /// <summary>Unavailable plugin types wait outside the runnable tick budget until their binding returns.</summary>
    internal static bool CanRunTickType(string type) =>
        !BlocksRegistry.TryGetPluginBlock(type, out var block)
        || (block.Binding is BlockBinding binding && binding.Owner is not PluginContainer { IsClosing: true });

    /// <summary>
    /// Runs a random tick of a plugin block that gets them (see <see cref="BlockProperties.RandomlyTicks"/>); other
    /// blocks are left to <see cref="Growth"/>.
    /// </summary>
    public void RandomTick(Vector position, IBlock state)
    {
        if (!BlockBehaviors.IsRandomlyTicking(state))
            return;

        this.RunHook(
            state,
            nameof(IBlockBehavior.RandomTick),
            behavior => behavior.RandomTick(this.Context(position, state)));
    }

    /// <inheritdoc/>
    public void OnRemoved(Vector position, IBlock state, IBlock newState)
    {
        VanillaRedstone.For(state)?.OnRemoved(this, position, state);

        this.RunHook(state, nameof(IBlockBehavior.OnRemoved), behavior => behavior.OnRemoved(new BlockRemovedContext
        {
            Level = this,
            Position = position,
            State = state,
            NewState = newState
        }));
    }

    /// <inheritdoc/>
    public void OnPlaced(Vector position, IBlock state, IBlock oldState)
    {
        if (IsVanillaFalling(state))
        {
            this.ScheduleFall(position, state);
            return;
        }

        VanillaRedstone.For(state)?.OnPlaced(this, position, state, oldState);

        this.RunHook(state, nameof(IBlockBehavior.OnPlaced), behavior => behavior.OnPlaced(new BlockPlacedContext
        {
            Level = this,
            Position = position,
            State = state,
            OldState = oldState
        }));
    }

    /// <inheritdoc/>
    public void NeighborChanged(Vector position, IBlock state, IBlock source, Vector sourcePosition)
    {
        VanillaRedstone.For(state)?.NeighborChanged(this, position, state);

        this.RunHook(
            state,
            nameof(IBlockBehavior.NeighborChanged),
            behavior => behavior.NeighborChanged(new NeighborChangedContext
            {
                Level = this,
                Position = position,
                State = state,
                SourcePosition = sourcePosition,
                SourceBlock = source
            }));
    }

    /// <inheritdoc/>
    public IBlock? UpdateShape(
        Vector position,
        IBlock state,
        BlockFace direction,
        Vector neighborPosition,
        IBlock neighbor)
    {
        if (IsVanillaFalling(state))
        {
            // Vanilla FallingBlock.updateShape: checks below again soon, and keeps its state.
            this.ScheduleFall(position, state);
            return null;
        }

        if (VanillaRedstone.For(state) is RedstoneComponent component)
        {
            var updated = component.UpdateShape(this, position, state, direction, neighbor);

            // Vanilla's destroyBlock: what loses its support drops its loot as it goes.
            if (updated.IsAir)
                this.DropLoot(position, state);

            return updated;
        }

        if (!BlockBehaviors.TryEnter(state, out var behavior, out var call))
            return null;

        using (call)
        {
            try
            {
                var updated = behavior.UpdateShape(new ShapeUpdateContext
                {
                    Level = this,
                    Position = position,
                    State = state,
                    Direction = direction,
                    NeighborPosition = neighborPosition,
                    NeighborState = neighbor
                }) ?? state;

                if (updated.IsAir && !state.IsAir)
                {
                    this.DestroyBlock(position, dropItems: true);
                    return state;
                }

                return updated;
            }
            catch (Exception exception)
            {
                this.LogHookFailure(exception, nameof(IBlockBehavior.UpdateShape), state);
                return state;
            }
        }
    }

    /// <inheritdoc/>
    public void AfterNeighborsUpdated(Vector position, IBlock state)
    {
        if (this.HasAnalogOutput(state))
            this.UpdateComparatorsAround(position, state);
    }

    /// <inheritdoc/>
    public void UpdateIndirectNeighborShapes(
        Vector position,
        IBlock state,
        BlockUpdateFlags flags,
        int updateLimit) =>
        VanillaRedstone.For(state)?.UpdateIndirectNeighborShapes(this, position, state, flags, updateLimit);

    // Vanilla's falling blocks and suspicious sand and gravel; plugins' blocks never fall on their own.
    private static bool IsVanillaFalling(IBlock state) =>
        state.IsGravityAffected() && !BlocksRegistry.TryGetPluginBlock(state.GetHashCode(), out _);

    private void ScheduleFall(Vector position, IBlock state)
    {
        if (fluids.TryGetChunk(position, out var chunk))
            chunk.BlockTicks.Schedule(position, state.UnlocalizedName, FallDelay);
    }

    // Vanilla FallingBlock.tick and FallingBlockEntity.fall: with room below, the block leaves its fluid behind, with
    // updates, and falls as an entity.
    private void Fall(Vector position, IBlock state)
    {
        if (!this.GetBlock(position + Vector.Down).IsFreeForFallingBlock() || position.Y < level.MinY)
            return;

        updates.SetBlockOutsideFluids(position, FluidState.Of(state).CreateLegacyBlock(), BlockUpdateFlags.All);
        level.SpawnFallingBlock(position, BlocksRegistry.GetDefault(state.RegistryId));
    }

    // Runs a plugin block's tick in a call into its plugin, or holds it (false) while the plugin can't be called:
    // unloaded, or unloading with its blocks still bound to it. Blocks without a behaviour have nothing to run.
    private bool RunPluginTick(Vector position, IBlock state, BlockType pluginBlock)
    {
        if (pluginBlock.Binding is { Behavior: null })
            return true;

        if (!BlockBehaviors.TryEnter(state, out var behavior, out var call))
            return false;

        using (call)
        {
            try
            {
                behavior.Tick(this.Context(position, state));
            }
            catch (Exception exception)
            {
                this.LogHookFailure(exception, nameof(IBlockBehavior.Tick), state);
            }
        }

        return true;
    }

    private BlockContext Context(Vector position, IBlock state) => new()
    {
        Level = this,
        Position = position,
        State = state
    };

    // A hook runs while the level's block updates wait for it: one that throws is logged rather than stopping the
    // updates, or the tick, it runs in.
    private void RunHook(IBlock state, string hook, Action<IBlockBehavior> run)
    {
        if (!BlockBehaviors.TryEnter(state, out var behavior, out var call))
            return;

        using (call)
        {
            try
            {
                run(behavior);
            }
            catch (Exception exception)
            {
                this.LogHookFailure(exception, hook, state);
            }
        }
    }

    private void LogHookFailure(Exception exception, string hook, IBlock state) =>
        HookFailed(level.Logger, exception, state.UnlocalizedName, hook);

    [LoggerMessage(Level = LogLevel.Error, Message = "{Block}'s {Hook} hook failed")]
    private static partial void HookFailed(ILogger logger, Exception exception, string block, string hook);
}

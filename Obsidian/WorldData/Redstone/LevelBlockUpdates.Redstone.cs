using Obsidian.API.BlockEntities;
using Obsidian.API.Blocks;
using Obsidian.ChunkData;
using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Plugins;
using Obsidian.WorldData.Features;
using Obsidian.WorldData.Fluids;
using Obsidian.WorldData.Redstone;

namespace Obsidian.WorldData;

/// <summary>
/// Redstone signals, read through the blocks' outputs (vanilla's <c>SignalGetter</c>), and what vanilla's redstone
/// components (see <see cref="VanillaRedstone"/>) reach of the level.
/// </summary>
/// <remarks>
/// Like the rest of the class, only used under the level's tick lock: <see cref="ISignalReader"/> through the
/// <see cref="IBlockLevel"/> a hook gets, and the level's own signal methods take the lock first.
/// </remarks>
internal sealed partial class LevelBlockUpdates
{
    // Half of vanilla's item entity height, which popped drops are lowered by.
    private const double ItemHalfHeight = 0.125;

    // The flags and update limit of the shape updates a block changed with BlockUpdateFlags.All sends.
    private const BlockUpdateFlags RefreshShapeFlags = BlockUpdateFlags.Clients;
    private const int ShapeLimit = 511;

    // Vanilla's Direction.values(), the order SignalGetter asks the neighbours in.
    private static readonly BlockFace[] signalOrder =
        [BlockFace.Down, BlockFace.Up, BlockFace.North, BlockFace.South, BlockFace.West, BlockFace.East];

    // Vanilla's Direction.Plane.HORIZONTAL.
    private static readonly BlockFace[] horizontal = [BlockFace.North, BlockFace.East, BlockFace.South, BlockFace.West];

    /// <summary>
    /// Vanilla's <c>RedStoneWireBlock.shouldSignal</c>: off while dust works out its own power, so that it only
    /// counts the power of other blocks.
    /// </summary>
    internal bool WiresGiveSignal { get; set; } = true;

    /// <summary>
    /// The level's redstone torches that turned off lately, for their burning out.
    /// </summary>
    internal TorchBurnout TorchBurnout { get; } = new();

    /// <summary>
    /// The game time block ticks are scheduled against.
    /// </summary>
    internal long GameTime => this.Scheduler.GameTime;

    /// <inheritdoc/>
    public int GetSignal(Vector position, BlockFace direction)
    {
        var state = this.GetBlock(position);
        var signal = this.SignalOf(state, position, direction);

        return state.IsRedstoneConductor() ? Math.Max(signal, this.GetDirectSignalTo(position)) : signal;
    }

    /// <inheritdoc/>
    public int GetDirectSignal(Vector position, BlockFace direction) =>
        this.DirectSignalOf(this.GetBlock(position), position, direction);

    /// <inheritdoc/>
    public int GetDirectSignalTo(Vector position)
    {
        var best = 0;
        foreach (var direction in signalOrder)
        {
            best = Math.Max(best, this.GetDirectSignal(position.Offset(direction), direction));
            if (best >= 15)
                return best;
        }

        return best;
    }

    /// <inheritdoc/>
    public bool HasSignal(Vector position, BlockFace direction) => this.GetSignal(position, direction) > 0;

    /// <inheritdoc/>
    public bool HasNeighborSignal(Vector position)
    {
        foreach (var direction in signalOrder)
        {
            if (this.GetSignal(position.Offset(direction), direction) > 0)
                return true;
        }

        return false;
    }

    /// <inheritdoc/>
    public int GetBestNeighborSignal(Vector position)
    {
        var best = 0;
        foreach (var direction in signalOrder)
        {
            var signal = this.GetSignal(position.Offset(direction), direction);
            if (signal >= 15)
                return 15;

            best = Math.Max(best, signal);
        }

        return best;
    }

    /// <inheritdoc/>
    public void UpdateAnalogOutput(Vector position)
    {
        using var host = PluginCall.EnterHost();

        lock (fluids.TickLock)
            this.UpdateComparatorsAround(position, this.GetBlock(position));
    }

    /// <summary>
    /// Placed blocks of <paramref name="types"/>, in the level's complete chunks, lost or got back their plugin's
    /// behaviour (as it unloads or loads), so the power, connections and analog output they give may have changed:
    /// the blocks around each hear of it like of a change of the block. Their neighbours, and those of the blocks it
    /// may strongly power, get neighbour updates, its neighbours shape updates, and the comparators reading it hear.
    /// </summary>
    public void RefreshBlocksOf(IReadOnlyCollection<BlockType> types)
    {
        using var host = PluginCall.EnterHost();

        lock (fluids.TickLock)
        {
            foreach (var position in this.FindBlocksOf(types))
            {
                var state = this.GetBlock(position);
                updates.UpdateNeighborsAt(position, state);
                foreach (var direction in signalOrder)
                    updates.UpdateNeighborsAt(position.Offset(direction), state);

                foreach (var direction in signalOrder)
                    this.ShapeUpdateAt(position.Offset(direction), direction.Opposite(), RefreshShapeFlags, ShapeLimit);

                this.UpdateComparatorsAround(position, state);
            }
        }
    }

    /// <summary>
    /// A player uses the vanilla redstone component at <paramref name="position"/>: flips a lever, presses a button,
    /// cycles a repeater's delay or a comparator's mode, or turns dust between a dot and a cross.
    /// </summary>
    /// <param name="mayBuild">Whether the player may change blocks, which repeaters, comparators and dust need.</param>
    /// <returns>Whether the block reacted.</returns>
    public bool UseRedstone(Vector position, IPlayer player, bool mayBuild)
    {
        lock (fluids.TickLock)
        {
            var state = this.GetBlock(position);
            return VanillaRedstone.For(state)?.Use(this, position, state, player, mayBuild) == true;
        }
    }

    /// <summary>
    /// The state a player places a vanilla redstone component in (see
    /// <see cref="RedstoneComponent.GetStateForPlacement"/>), or the block as it is for other blocks.
    /// </summary>
    /// <returns>The state to place, or null when it can't go there.</returns>
    public IBlock? GetRedstonePlacement(IBlock block, Vector position, IPlayer player, BlockFace face, bool replaces)
    {
        if (VanillaRedstone.For(block) is not RedstoneComponent component)
            return block;

        var placement = new RedstonePlacement
        {
            Level = this,
            Block = block,
            Position = position,
            HorizontalDirection = PlacementDirections.Horizontal(player.Yaw.Degrees),
            NearestLookingDirections =
                PlacementDirections.NearestLooking(player.Yaw.Degrees, player.Pitch.Degrees, face, replaces),
            ClickedFace = face,
            NearestLookDirection = PlacementDirections.Nearest(player.Yaw.Degrees, player.Pitch.Degrees)
        };

        lock (fluids.TickLock)
            return component.GetStateForPlacement(placement);
    }

    /// <summary>
    /// Vanilla <c>setPlacedBy</c> for a vanilla redstone component a player placed: a repeater or comparator
    /// powered as it's placed ticks at once.
    /// </summary>
    public void OnPlacedByPlayer(Vector position, IBlock state)
    {
        lock (fluids.TickLock)
        {
            if (this.GetBlock(position).IsSameState(state))
                VanillaRedstone.For(state)?.OnPlacedByPlayer(this, position, state);
        }
    }

    /// <summary>
    /// An explosion that triggers blocks (a wind charge) reaches <paramref name="position"/>: a lever there flips
    /// and a button is pressed, like vanilla's <c>onExplosionHit</c>.
    /// </summary>
    /// <returns>Whether a block reacted.</returns>
    public bool TriggerRedstone(Vector position)
    {
        lock (fluids.TickLock)
        {
            var state = this.GetBlock(position);
            return VanillaRedstone.For(state)?.Trigger(this, position, state) == true;
        }
    }

    /// <summary>
    /// Vanilla <c>entityInside</c> for the pressure plate or wooden button at <paramref name="position"/>, whose
    /// cube <paramref name="entity"/>'s box overlaps.
    /// </summary>
    public void EntityInsideRedstone(Vector position, IEntity entity)
    {
        lock (fluids.TickLock)
        {
            var state = this.GetBlock(position);
            if (VanillaRedstone.IsPressedByEntities(state))
                VanillaRedstone.For(state)!.EntityInside(this, position, state, entity);
        }
    }

    /// <summary>
    /// A container's items changed, like vanilla's <c>BlockEntity.setChanged</c> for a block with an analog output:
    /// the comparators reading it hear of it.
    /// </summary>
    public void OnContainerChanged(Vector position)
    {
        lock (fluids.TickLock)
        {
            var state = this.GetBlock(position);
            if (this.HasAnalogOutput(state))
                this.UpdateComparatorsAround(position, state);
        }
    }

    /// <summary>
    /// Vanilla <c>isSignalSource</c>, for any block.
    /// </summary>
    internal bool IsSignalSource(IBlock state)
    {
        if (VanillaRedstone.For(state) is RedstoneComponent component)
            return component.IsSignalSource(this);

        if (!BlocksRegistry.TryGetPluginBlock(state.GetHashCode(), out _))
            return state.IsSignalSource();

        return this.AskPlugin(state, nameof(IBlockBehavior.IsSignalSource), false, b => b.IsSignalSource(state));
    }

    /// <summary>
    /// Vanilla <c>hasAnalogOutputSignal</c>, for any block.
    /// </summary>
    internal bool HasAnalogOutput(IBlock state)
    {
        if (!BlocksRegistry.TryGetPluginBlock(state.GetHashCode(), out _))
            return AnalogOutputs.Has(state);

        return this.AskPlugin(state, nameof(IBlockBehavior.HasAnalogOutput), false, b => b.HasAnalogOutput(state));
    }

    /// <summary>
    /// Vanilla <c>getAnalogOutputSignal</c>, for a block that has one.
    /// </summary>
    internal int AnalogOutputOf(IBlock state, Vector position, BlockFace direction)
    {
        if (!BlocksRegistry.TryGetPluginBlock(state.GetHashCode(), out _))
            return AnalogOutputs.Get(this, state, position);

        var context = this.SignalContext(state, position, direction);
        var output = this.AskPlugin(state, nameof(IBlockBehavior.GetAnalogOutput), 0, b => b.GetAnalogOutput(context));

        return Math.Clamp(output, 0, 15);
    }

    /// <summary>
    /// Vanilla <c>updateNeighbourForOutputSignal</c>: the comparators beside <paramref name="position"/>, or behind a
    /// conductor beside it, hear that the block there (<paramref name="source"/>) changed.
    /// </summary>
    internal void UpdateComparatorsAround(Vector position, IBlock source)
    {
        foreach (var direction in horizontal)
        {
            var beside = position.Offset(direction);
            if (!fluids.TryGetChunk(beside, out _))
                continue;

            var state = this.GetBlock(beside);
            if (state.Is(BlockIds.Comparator))
            {
                updates.QueueNeighborChanged(beside, source, position);
            }
            else if (state.IsRedstoneConductor())
            {
                var behind = beside.Offset(direction);
                if (this.GetBlock(behind).Is(BlockIds.Comparator))
                    updates.QueueNeighborChanged(behind, source, position);
            }
        }
    }

    /// <summary>
    /// Changes a block from a component, with vanilla's side effects as <paramref name="flags"/> ask.
    /// </summary>
    internal bool Set(Vector position, IBlock state, BlockUpdateFlags flags) =>
        updates.SetBlockOutsideFluids(position, state, flags);

    /// <summary>
    /// Vanilla <c>Level.neighborChanged</c>: the block at <paramref name="position"/> alone hears of a change at
    /// <paramref name="sourcePosition"/>.
    /// </summary>
    internal void NeighborChangedAt(Vector position, IBlock source, Vector sourcePosition) =>
        updates.QueueNeighborChanged(position, source, sourcePosition);

    /// <summary>
    /// Vanilla <c>updateNeighborsAtExceptFromFacing</c>.
    /// </summary>
    internal void UpdateNeighborsExcept(Vector position, IBlock source, BlockFace skip) =>
        updates.UpdateNeighborsAtExcept(position, source, skip);

    /// <summary>
    /// Vanilla <c>neighborShapeChanged</c>: the block at <paramref name="position"/> gets a shape update from its
    /// neighbour in <paramref name="direction"/>.
    /// </summary>
    internal void ShapeUpdateAt(Vector position, BlockFace direction, BlockUpdateFlags flags, int updateLimit)
    {
        var neighbor = this.GetBlock(position.Offset(direction));
        updates.QueueShapeUpdate(direction, neighbor, position, flags, updateLimit, VanillaShapes.Fluids);
    }

    /// <summary>
    /// Vanilla <c>willTickThisTick</c> for the block at <paramref name="position"/>.
    /// </summary>
    internal bool WillTickThisTick(Vector position, IBlock state) =>
        this.Scheduler.WillTickThisTick(position, state.UnlocalizedName);

    /// <summary>
    /// Vanilla <c>dropResources</c> and <c>removeBlock</c>: the block drops its loot, like a player breaking it by
    /// hand, and leaves the fluid it held.
    /// </summary>
    internal void DropAndRemove(Vector position, IBlock state)
    {
        this.DropLoot(position, state);
        this.RemoveBlock(position);
    }

    /// <summary>
    /// Vanilla <c>Block.dropResources</c> without a tool: the block's loot, each stack popped out of the block like
    /// vanilla's <c>popResource</c>.
    /// </summary>
    internal void DropLoot(Vector position, IBlock state)
    {
        if (!level.LevelData.GetBooleanRule("block_drops", true))
            return;

        foreach (var stack in BlockLoot.GetDrops(level, position, state, this.GetBlock))
        {
            // Vanilla's popResource: within a quarter block of the centre, lowered by half an item's height.
            var random = this.Random;
            var x = position.X + 0.5 + random.NextDouble() * 0.5 - 0.25;
            var y = position.Y + 0.5 + random.NextDouble() * 0.5 - 0.25 - ItemHalfHeight;
            var z = position.Z + 0.5 + random.NextDouble() * 0.5 - 0.25;
            var motion = new VectorD(random.NextDouble() * 0.2 - 0.1, 0.2, random.NextDouble() * 0.2 - 0.1);

            var item = new ItemEntity
            {
                Level = level,
                EntityId = Server.GetNextEntityId(),
                Position = new VectorD(x, y, z),
                Item = stack,
                Motion = motion
            };

            // The level's own drop, not one a plugin's code on the stack spawns.
            level.TrySpawnEntity(item, hostOwned: true);
        }
    }

    /// <summary>
    /// Plays a block sound at the centre of <paramref name="position"/> to the players near it, but
    /// <paramref name="except"/>, whose client plays it itself, like vanilla's <c>playSound</c> with a player.
    /// </summary>
    internal void PlaySound(
        Vector position,
        string sound,
        float volume = 1,
        float pitch = 1,
        IPlayer? except = null)
    {
        int[] excluded = except is null ? [] : [except.EntityId];
        var packet = new SoundPacket
        {
            SoundLocation = sound,
            SoundPosition = new SoundPosition(position.X + 0.5, position.Y + 0.5, position.Z + 0.5),
            Category = SoundCategory.Blocks,
            Volume = volume,
            Pitch = pitch,
            Seed = this.Random.NextLong()
        };

        level.PacketBroadcaster.BroadcastToLevelInRange(level, (VectorD)position + 0.5, packet, excluded);
    }

    /// <summary>
    /// Vanilla <c>levelEvent</c>: a sound or particles players near <paramref name="position"/> see.
    /// </summary>
    internal void LevelEvent(int type, Vector position, int data) => fluids.LevelEvent(type, position, data);

    /// <summary>
    /// The items of a container, its pending loot rolled first (with nobody opening it), as reading vanilla's
    /// containers' slots does.
    /// </summary>
    internal BaseContainer ItemsOf(ContainerBlockEntity container)
    {
        level.UnpackLootTable(container, opener: null);
        return container.Inventory;
    }

    /// <summary>
    /// The block entity at <paramref name="position"/>, if its chunk is loaded.
    /// </summary>
    internal IBlockEntity? GetBlockEntity(Vector position) =>
        fluids.TryGetChunk(position, out var chunk) ? chunk.GetBlockEntity(position.X, position.Y, position.Z) : null;

    /// <summary>
    /// The entities whose boxes overlap the box from <paramref name="min"/> to <paramref name="max"/>, like
    /// vanilla's <c>getEntitiesOfClass</c>.
    /// </summary>
    internal IEnumerable<IEntity> EntitiesIn(VectorD min, VectorD max)
    {
        var center = (min + max) / 2;
        foreach (var entity in level.GetEntitiesInRange(center, 4))
        {
            var box = entity.BoundingBox;
            if (box.Min.X < max.X && box.Max.X > min.X
                && box.Min.Y < max.Y && box.Max.Y > min.Y
                && box.Min.Z < max.Z && box.Max.Z > min.Z)
            {
                yield return entity;
            }
        }
    }

    // Where blocks of the types are in the complete chunks, found before any changes, which the updates make.
    private List<Vector> FindBlocksOf(IReadOnlyCollection<BlockType> types)
    {
        bool Matches(int stateId) => types.Any(type => (uint)(stateId - type.FirstStateId) < (uint)type.StateCount);

        var found = new List<Vector>();
        foreach (var chunk in fluids.TrackedChunks())
        {
            var sections = chunk.Sections;
            for (var index = 0; index < sections.Length; index++)
            {
                if (sections[index] is not ChunkSection section || !section.MayHold(Matches))
                    continue;

                for (var y = 0; y < 16; y++)
                {
                    for (var z = 0; z < 16; z++)
                    {
                        for (var x = 0; x < 16; x++)
                        {
                            if (Matches(section.GetStateId(x, y, z)))
                                found.Add(new Vector(chunk.X * 16 + x, chunk.MinY + index * 16 + y, chunk.Z * 16 + z));
                        }
                    }
                }
            }
        }

        return found;
    }

    // A block's weak signal: a vanilla component's, a plugin block's behaviour's, and none for other blocks.
    private int SignalOf(IBlock state, Vector position, BlockFace direction)
    {
        if (VanillaRedstone.For(state) is RedstoneComponent component)
            return component.GetSignal(this, position, state, direction);

        if (!BlocksRegistry.TryGetPluginBlock(state.GetHashCode(), out _))
            return 0;

        var context = this.SignalContext(state, position, direction);
        return Math.Clamp(this.AskPlugin(state, nameof(IBlockBehavior.GetSignal), 0, b => b.GetSignal(context)), 0, 15);
    }

    private int DirectSignalOf(IBlock state, Vector position, BlockFace direction)
    {
        if (VanillaRedstone.For(state) is RedstoneComponent component)
            return component.GetDirectSignal(this, position, state, direction);

        if (!BlocksRegistry.TryGetPluginBlock(state.GetHashCode(), out _))
            return 0;

        var context = this.SignalContext(state, position, direction);
        var signal = this.AskPlugin(state, nameof(IBlockBehavior.GetDirectSignal), 0, b => b.GetDirectSignal(context));

        return Math.Clamp(signal, 0, 15);
    }

    private SignalContext SignalContext(IBlock state, Vector position, BlockFace direction) => new()
    {
        Level = this,
        Position = position,
        State = state,
        Direction = direction
    };

    // Asks a plugin block's behaviour in a call into its plugin: a block without one, or whose plugin is unloading,
    // answers the default, and one that throws is logged and answers it too.
    private T AskPlugin<T>(IBlock state, string hook, T fallback, Func<IBlockBehavior, T> ask)
    {
        if (!BlockBehaviors.TryEnter(state, out var behavior, out var call))
            return fallback;

        using (call)
        {
            try
            {
                return ask(behavior);
            }
            catch (Exception exception)
            {
                this.LogHookFailure(exception, hook, state);
                return fallback;
            }
        }
    }
}

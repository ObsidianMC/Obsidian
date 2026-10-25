using Obsidian.API.Blocks;
using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.WorldData.Features;
using Obsidian.WorldData.Features.Tree;

namespace Obsidian.WorldData.Fluids;

/// <summary>
/// The part of vanilla's <c>ServerLevel</c> block updates run against: block changes with vanilla's update side
/// effects (<c>Level.setBlock</c> and its <c>CollectingNeighborUpdater</c>), <c>LiquidBlock</c>'s reactions to them,
/// and fluid ticks.
/// </summary>
/// <remarks>
/// Neighbor updates reach the blocks Obsidian models: liquids (which schedule their tick and turn lava to obsidian,
/// cobblestone or basalt), the other blocks' reactions the access gives (<see cref="IFluidLevelAccess.Reactions"/>:
/// plugin blocks, redstone components and falling blocks on a live level) and, through <see cref="ShapeUpdater"/>,
/// the shapes of the vanilla blocks Obsidian models, such as plants losing their support or the other half of a
/// double plant. Not thread-safe; the caller serializes access to the blocks.
/// </remarks>
internal sealed class FluidLevel
{
    // Vanilla Level.setBlock's default limit on chained shape updates.
    private const int UpdateLimit = 512;

    // Vanilla BlockBehaviour.UPDATE_SHAPE_ORDER (updateShape).
    private static readonly BlockFace[] updateShapeOrder = [BlockFace.West, BlockFace.East, BlockFace.North,
        BlockFace.South, BlockFace.Down, BlockFace.Up];

    // Vanilla LiquidBlock.POSSIBLE_FLOW_DIRECTIONS; lava checks the block each of them flows from.
    private static readonly BlockFace[] possibleFlowDirections = [BlockFace.Down, BlockFace.South, BlockFace.North, BlockFace.East, BlockFace.West];

    private static readonly IBlock basalt = BlocksRegistry.GetDefault(BlockIds.Basalt);

    private readonly IFluidLevelAccess access;
    private readonly NeighborUpdater neighborUpdater;

    public FluidLevel(IFluidLevelAccess access)
    {
        this.access = access;
        this.neighborUpdater = new NeighborUpdater(this);
        this.ShapeView = new ShapeUpdateView(this);
    }

    /// <summary>
    /// The dimension setting and game rules fluids follow.
    /// </summary>
    public FluidRules Rules => this.access.Rules;

    /// <summary>
    /// The level's random.
    /// </summary>
    public Random Random => this.access.Random;

    /// <summary>
    /// This level as <see cref="ShapeUpdater"/> sees it: fluid ticks it schedules get their fluid's real delay, like
    /// vanilla's <c>updateShape</c> on a live level.
    /// </summary>
    public IWorldGenLevel ShapeView { get; }

    /// <summary>
    /// The block at <paramref name="position"/>.
    /// </summary>
    public IBlock GetBlock(Vector position) => this.access.GetBlock(position);

    /// <summary>
    /// The fluid of the block at <paramref name="position"/>.
    /// </summary>
    public FluidState GetFluid(Vector position) => FluidState.Of(this.GetBlock(position));

    /// <summary>
    /// Vanilla <c>scheduleTick</c> for a fluid: ignored when that fluid already has a tick scheduled there.
    /// </summary>
    public void ScheduleTick(Vector position, FluidKind fluid, int delay) => this.access.ScheduleFluidTick(position, fluid, delay);

    /// <summary>
    /// Vanilla's lava fizz (level event 1501).
    /// </summary>
    public void Fizz(Vector position) => this.access.LevelEvent(1501, position, 0);

    /// <summary>
    /// Vanilla <c>ServerLevel.tickFluid</c>: runs a scheduled tick if the block still holds that exact fluid.
    /// </summary>
    public void RunScheduledTick(Vector position, FluidKind fluid)
    {
        var block = this.GetBlock(position);
        var state = FluidState.Of(block);
        if (state.Kind == fluid)
            state.Type!.Tick(this, position, block, state);
    }

    /// <summary>
    /// Vanilla <c>FluidState.tick</c>: runs the tick of the fluid at <paramref name="position"/> right away, like
    /// <c>LevelChunk.postProcessGeneration</c> does for marked fluids.
    /// </summary>
    public void TickFluid(Vector position)
    {
        var block = this.GetBlock(position);
        var state = FluidState.Of(block);
        if (!state.IsEmpty)
            state.Type!.Tick(this, position, block, state);
    }

    /// <summary>
    /// Vanilla <c>Level.setBlock</c>, as fluids change blocks: stores the block, runs its <c>onPlace</c>, then
    /// notifies its neighbors (<c>neighborChanged</c>, then <c>updateShape</c>) as the flags ask, with every vanilla
    /// block <see cref="ShapeUpdater"/> knows reacting to its shape updates.
    /// </summary>
    /// <returns>Whether the block changed.</returns>
    public bool SetBlock(Vector position, IBlock block, BlockUpdateFlags flags, int updateLimit = UpdateLimit) =>
        this.SetBlock(position, block, flags, updateLimit, VanillaShapes.All);

    /// <summary>
    /// <see cref="SetBlock(Vector, IBlock, BlockUpdateFlags, int)"/> for changes from outside fluid ticks (plugin
    /// blocks' hooks), including vanilla's support and connection rules.
    /// </summary>
    public bool SetBlockOutsideFluids(Vector position, IBlock block, BlockUpdateFlags flags) =>
        this.SetBlock(position, block, flags, UpdateLimit, VanillaShapes.All);

    /// <summary>
    /// Runs the side effects of a block something else stored (a player, a plugin or an explosion), as
    /// <c>Level.setBlock</c> runs them: players and portals hear of it (see
    /// <see cref="IFluidLevelAccess.OnBlockStored"/>), then <paramref name="old"/>'s removal,
    /// <paramref name="block"/>'s placement and its neighbors' updates run, as the flags ask.
    /// </summary>
    public void OnBlockChanged(Vector position, IBlock old, IBlock block, BlockUpdateFlags flags)
    {
        // Players are sent even an unchanged block, which corrects a client that predicted another.
        this.access.OnBlockStored(position, block, flags);

        if (!old.IsSameState(block))
            this.OnChanged(position, old, block, flags, UpdateLimit, VanillaShapes.All);
    }

    /// <summary>
    /// Vanilla <c>updateNeighborsAt</c>: the six neighbors of <paramref name="position"/> get <c>neighborChanged</c>
    /// from <paramref name="source"/>.
    /// </summary>
    public void UpdateNeighborsAt(Vector position, IBlock source) =>
        this.neighborUpdater.UpdateNeighborsAt(position, source);

    /// <summary>
    /// Vanilla <c>updateNeighborsAtExceptFromFacing</c>: <see cref="UpdateNeighborsAt"/> but for the neighbor in
    /// <paramref name="skip"/>.
    /// </summary>
    public void UpdateNeighborsAtExcept(Vector position, IBlock source, BlockFace skip) =>
        this.neighborUpdater.UpdateNeighborsAtExcept(position, source, skip);

    /// <summary>
    /// Vanilla <c>Level.neighborChanged</c>: the block at <paramref name="position"/> alone hears that
    /// <paramref name="source"/> at <paramref name="sourcePosition"/> changed.
    /// </summary>
    public void QueueNeighborChanged(Vector position, IBlock source, Vector sourcePosition) =>
        this.neighborUpdater.NeighborChanged(position, source, sourcePosition);

    /// <summary>
    /// Vanilla <c>neighborShapeChanged</c>: the block at <paramref name="position"/> gets a shape update from its
    /// neighbor in <paramref name="direction"/>, now <paramref name="neighbor"/>, like the direct ones a block change
    /// sends (redstone dust updates the dust diagonal to it this way).
    /// </summary>
    public void QueueShapeUpdate(
        BlockFace direction,
        IBlock neighbor,
        Vector position,
        BlockUpdateFlags flags,
        int updateLimit,
        VanillaShapes shapes) =>
        this.neighborUpdater.ShapeUpdate(direction, neighbor, position, flags, updateLimit, shapes);

    /// <summary>
    /// Lets fluids react to a block that something else changed (a feature growing at runtime): a placed liquid and
    /// neighboring liquids get vanilla's <c>onPlace</c> and <c>neighborChanged</c>, and fluid neighbors schedule the
    /// tick their <c>updateShape</c> would.
    /// </summary>
    public void UpdateFluidsAround(Vector position, IBlock block)
    {
        this.OnPlace(position, block);
        if (!this.GetBlock(position).IsSameState(block))
            return;

        this.neighborUpdater.UpdateNeighborsAt(position, block);
        foreach (var face in updateShapeOrder)
        {
            var neighborPosition = position.Offset(face);
            this.ScheduleFluidShapeTick(neighborPosition, this.GetBlock(neighborPosition), block);
        }
    }

    /// <summary>
    /// Vanilla <c>LiquidBlockContainer.placeLiquid</c>: a dry waterloggable block takes a water source (lit campfires and
    /// candles go out) and schedules the water's tick.
    /// </summary>
    public bool PlaceLiquid(Vector position, IBlock block, FluidState fluid)
    {
        if (block.GetProperty("waterlogged") != "false" || fluid.Kind != FluidKind.Water)
            return false;

        var waterlogged = block.WithProperty("waterlogged", true);
        if (waterlogged.GetProperty("lit") == "true")
            waterlogged = waterlogged.WithProperty("lit", false);

        this.SetBlock(position, waterlogged, BlockUpdateFlags.All);
        this.ScheduleTick(position, fluid.Kind, fluid.Type!.GetTickDelay(this));
        return true;
    }

    /// <summary>
    /// Vanilla <c>NeighborUpdater.executeUpdate</c>: the block at <paramref name="position"/> reacts to its neighbor
    /// at <paramref name="sourcePosition"/> changing from <paramref name="source"/>.
    /// </summary>
    internal void NeighborChanged(Vector position, IBlock source, Vector sourcePosition)
    {
        var block = this.GetBlock(position);
        this.NeighborChanged(position, block);
        this.access.Reactions?.NeighborChanged(position, block, source, sourcePosition);
    }

    /// <summary>
    /// Vanilla <c>NeighborUpdater.executeShapeUpdate</c> and <c>Block.updateOrDestroy</c>: the block at
    /// <paramref name="position"/> reacts to its neighbor in <paramref name="direction"/> becoming
    /// <paramref name="neighbor"/>, being destroyed when its new state is air.
    /// </summary>
    internal void ExecuteShapeUpdate(
        BlockFace direction,
        Vector position,
        IBlock neighbor,
        BlockUpdateFlags flags,
        int updateLimit,
        VanillaShapes shapes)
    {
        var block = this.GetBlock(position);
        var neighborPosition = position.Offset(direction);
        var updated = this.access.Reactions?.UpdateShape(position, block, direction, neighborPosition, neighbor)
            ?? this.UpdateVanillaShape(block, position, direction, neighbor, shapes);

        if (updated.IsSameState(block))
            return;

        if (updated.IsAir)
            this.DestroyBlock(position, (flags & BlockUpdateFlags.SuppressDrops) == 0, updateLimit, shapes);
        else
            this.SetBlock(position, updated, flags & ~BlockUpdateFlags.SuppressDrops, updateLimit, shapes);
    }

    private bool SetBlock(Vector position, IBlock block, BlockUpdateFlags flags, int updateLimit, VanillaShapes shapes)
    {
        var old = this.GetBlock(position);
        if (old.IsSameState(block) || !this.access.SetBlock(position, block))
            return false;

        this.access.OnBlockStored(position, block, flags);
        this.OnChanged(position, old, block, flags, updateLimit, shapes);
        this.access.OnBlockSet(position, old, block, flags);
        return true;
    }

    // The side effects of Level.setBlock after the block is stored, in vanilla's order: LevelChunk.setBlockState's
    // removal of the old block and onPlace of the new one, then neighborChanged and updateShape for the neighbors.
    private void OnChanged(
        Vector position,
        IBlock old,
        IBlock block,
        BlockUpdateFlags flags,
        int updateLimit,
        VanillaShapes shapes)
    {
        // Vanilla's affectNeighborsAfterRemoval: only when another block replaced it, with neighbor updates.
        if (old.RegistryId != block.RegistryId && (flags & BlockUpdateFlags.Neighbors) != 0)
            this.access.Reactions?.OnRemoved(position, old, block);

        // The removal may have replaced the block, and that change did its own updates.
        if (this.GetBlock(position).RegistryId != block.RegistryId)
            return;

        if ((flags & BlockUpdateFlags.SkipOnPlace) == 0)
        {
            this.OnPlace(position, block);
            this.access.Reactions?.OnPlaced(position, block, old);
        }

        // onPlace may have replaced the block (lava touching water), and that change did its own updates.
        if (!this.GetBlock(position).IsSameState(block))
            return;

        if ((flags & BlockUpdateFlags.Neighbors) != 0)
        {
            this.neighborUpdater.UpdateNeighborsAt(position, old);
            this.access.Reactions?.AfterNeighborsUpdated(position, block);
        }

        if ((flags & BlockUpdateFlags.KnownShape) == 0 && updateLimit > 0)
        {
            // Vanilla's order: the old block's indirect shape updates, the new one's direct ones, then its indirect.
            var shapeFlags = flags & ~(BlockUpdateFlags.Neighbors | BlockUpdateFlags.SuppressDrops);
            this.access.Reactions?.UpdateIndirectNeighborShapes(position, old, shapeFlags, updateLimit - 1);
            foreach (var face in updateShapeOrder)
            {
                this.neighborUpdater.ShapeUpdate(
                    face.Opposite(),
                    block,
                    position.Offset(face),
                    shapeFlags,
                    updateLimit - 1,
                    shapes);
            }

            this.access.Reactions?.UpdateIndirectNeighborShapes(position, block, shapeFlags, updateLimit - 1);
        }
    }

    // Vanilla's updateShape for its own blocks: every block ShapeUpdater knows, or only what blocks holding a fluid
    // do (schedule their fluid's tick), keeping the block's state.
    private IBlock UpdateVanillaShape(
        IBlock block,
        Vector position,
        BlockFace direction,
        IBlock neighbor,
        VanillaShapes shapes)
    {
        if (shapes == VanillaShapes.All)
        {
            var brightness = block.BlockClass() == "MushroomBlock" ? this.access.GetRawBrightness(position) : 15;
            return ShapeUpdater.UpdateShape(this.ShapeView, block, position, direction, neighbor, brightness);
        }

        this.ScheduleFluidShapeTick(position, block, neighbor);
        return block;
    }

    // The fluid tick updateShape schedules for a block holding a fluid next to a changed one: a liquid's when it or
    // the changed block is a source, and always for other blocks holding one (waterlogged blocks, seagrass).
    private void ScheduleFluidShapeTick(Vector position, IBlock block, IBlock changed)
    {
        var fluid = FluidState.Of(block);
        if (fluid.IsEmpty || (block.IsLiquidBlock() && !fluid.IsSource && !FluidState.Of(changed).IsSource))
            return;

        this.ScheduleTick(position, fluid.Kind, fluid.Type!.GetTickDelay(this));
    }

    /// <summary>
    /// Vanilla <c>Level.destroyBlock</c>: the block is replaced by its fluid (air, or the water a waterlogged block held).
    /// </summary>
    private bool DestroyBlock(Vector position, bool dropItems, int updateLimit, VanillaShapes shapes)
    {
        if (this.GetBlock(position).IsAir)
            return false;

        var state = this.GetBlock(position);
        var fluid = this.GetFluid(position).CreateLegacyBlock();
        // RedStoneWireBlock and BaseRailBlock use dropResources/removeBlock rather than Level.destroyBlock,
        // so their support-loss removal does not emit level event 2001.
        var playEffect = state.BlockClass() != "RedStoneWireBlock" && !Rails.IsRail(state);
        if (this.access.TryQueueDestruction(position, fluid, dropItems, playEffect,
            stillUnsupported: current => this.IsUnsupported(position, current)))
            return true;

        return this.SetBlock(position, fluid, BlockUpdateFlags.All, updateLimit, shapes);
    }

    // Re-evaluate shape survival against all current neighbours: the state's attachment face may have changed.
    internal bool IsUnsupported(Vector position, IBlock state)
    {
        foreach (var direction in updateShapeOrder)
        {
            var neighborPosition = position.Offset(direction);
            var neighbor = this.GetBlock(neighborPosition);
            var updated = this.access.Reactions?.UpdateShape(position, state, direction, neighborPosition, neighbor)
                ?? this.UpdateVanillaShape(state, position, direction, neighbor, VanillaShapes.All);
            if (updated.IsAir)
                return true;
        }

        return false;
    }

    /// <summary>
    /// WaterFluid.beforeDestroyingBlock drops resources with an empty tool, without Level.destroyBlock's effect.
    /// Keep the original block until its queued loot transaction can replace it with the incoming water.
    /// </summary>
    public bool TryWashAway(Vector position, IBlock replacement) =>
        this.access.TryQueueDestruction(position, replacement, dropItems: true, playEffect: false);

    // Block.onPlace: only liquids react, and they react like to a neighbor change.
    private void OnPlace(Vector position, IBlock block) => this.NeighborChanged(position, block);

    /// <summary>
    /// Vanilla <c>LiquidBlock.neighborChanged</c> (other blocks fluids touch don't react): a liquid schedules its tick
    /// unless lava just hardened.
    /// </summary>
    private void NeighborChanged(Vector position, IBlock block)
    {
        if (!block.IsLiquidBlock() || !this.ShouldSpreadLiquid(position, block))
            return;

        var fluid = FluidState.Of(block);
        this.ScheduleTick(position, fluid.Kind, fluid.Type!.GetTickDelay(this));
    }

    /// <summary>
    /// Vanilla <c>LiquidBlock.shouldSpreadLiquid</c>: lava touching water from above or the sides hardens into obsidian
    /// (a source) or cobblestone, and lava on soul soil touching blue ice into basalt.
    /// </summary>
    private bool ShouldSpreadLiquid(Vector position, IBlock block)
    {
        if (!FluidState.Of(block).IsLava)
            return true;

        var onSoulSoil = this.GetBlock(position + Vector.Down).RegistryId == BlockIds.SoulSoil;
        foreach (var direction in possibleFlowDirections)
        {
            var neighbor = position.Offset(direction.Opposite());
            if (this.GetFluid(neighbor).IsWater)
            {
                var hardened = this.GetFluid(position).IsSource ? BlocksRegistry.ObsidianBlock : BlocksRegistry.Cobblestone;
                this.SetBlock(position, hardened, BlockUpdateFlags.All);
                this.Fizz(position);
                return false;
            }

            if (onSoulSoil && this.GetBlock(neighbor).RegistryId == BlockIds.BlueIce)
            {
                this.SetBlock(position, basalt, BlockUpdateFlags.All);
                this.Fizz(position);
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The level as world generation code sees it, for <see cref="ShapeUpdater.UpdateShape"/>, which only reads blocks and
    /// schedules fluid ticks.
    /// </summary>
    private sealed class ShapeUpdateView(FluidLevel level) : IWorldGenLevel
    {
        public long Seed => throw new NotSupportedException();

        public int MinY => level.access.MinY;

        public int Height => level.access.Height;

        public int SeaLevel => throw new NotSupportedException();

        public IRandomSource Random => throw new NotSupportedException();

        public IBlock GetBlock(Vector position) => level.GetBlock(position);

        public bool SetBlock(Vector position, IBlock block) => level.access.SetBlock(position, block);

        public bool EnsureCanWrite(Vector position) => true;

        public int GetHeight(HeightmapType type, int x, int z) => throw new NotSupportedException();

        public BiomeCodec GetBiome(Vector position) => throw new NotSupportedException();

        public void SetBlockEntity(Vector position, IBlockEntity blockEntity) => throw new NotSupportedException();

        public IBlockEntity? GetBlockEntity(Vector position) => throw new NotSupportedException();

        public void AddEntity(GeneratedEntity entity) => throw new NotSupportedException();

        public void MarkForPostProcessing(Vector position) => throw new NotSupportedException();

        /// <remarks>
        /// Like vanilla's <c>updateShape</c>, the tick is for the block's own fluid, after that fluid's delay.
        /// </remarks>
        public void ScheduleFluidTick(Vector position)
        {
            var fluid = level.GetFluid(position);
            if (!fluid.IsEmpty)
                level.ScheduleTick(position, fluid.Kind, fluid.Type!.GetTickDelay(level));
        }
    }
}

/// <summary>
/// Which of vanilla's blocks react to the shape updates of a chain of block updates.
/// </summary>
internal enum VanillaShapes
{
    /// <summary>
    /// Every block <see cref="ShapeUpdater"/> knows, as in fluid ticks and generation.
    /// </summary>
    All,

    /// <summary>
    /// Only liquids and waterlogged blocks, which schedule their fluid's tick, as for changes players, plugins and
    /// explosions make.
    /// </summary>
    Fluids
}

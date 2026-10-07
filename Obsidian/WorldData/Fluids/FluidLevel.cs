using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.WorldData.Features;
using Obsidian.WorldData.Features.Tree;

namespace Obsidian.WorldData.Fluids;

/// <summary>
/// The part of vanilla's <c>ServerLevel</c> fluids run against: block changes with vanilla's update side effects
/// (<c>Level.setBlock</c> and its <c>CollectingNeighborUpdater</c>), <c>LiquidBlock</c>'s reactions to them, and fluid
/// ticks.
/// </summary>
/// <remarks>
/// Neighbor updates reach the blocks fluids care about: liquids (which schedule their tick and turn lava to obsidian,
/// cobblestone or basalt) and, through <see cref="ShapeUpdater"/>, the shapes of the blocks Obsidian models, such as plants
/// losing their support or the other half of a double plant. Not thread-safe; the caller serializes access to the blocks.
/// </remarks>
internal sealed class FluidLevel
{
    // Vanilla Level.setBlock's default limit on chained shape updates.
    private const int UpdateLimit = 512;

    // Vanilla NeighborUpdater.UPDATE_ORDER (neighborChanged) and BlockBehaviour.UPDATE_SHAPE_ORDER (updateShape).
    private static readonly BlockFace[] updateOrder = [BlockFace.West, BlockFace.East, BlockFace.Down, BlockFace.Up,
        BlockFace.North, BlockFace.South];
    private static readonly BlockFace[] updateShapeOrder = [BlockFace.West, BlockFace.East, BlockFace.North,
        BlockFace.South, BlockFace.Down, BlockFace.Up];

    // Vanilla LiquidBlock.POSSIBLE_FLOW_DIRECTIONS; lava checks the block each of them flows from.
    private static readonly BlockFace[] possibleFlowDirections = [BlockFace.Down, BlockFace.South, BlockFace.North, BlockFace.East, BlockFace.West];

    private static readonly IBlock basalt = BlockStateProperties.GetState("minecraft:basalt");

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
    /// Vanilla <c>Level.setBlock</c>: stores the block, runs its <c>onPlace</c>, then notifies its neighbors
    /// (<c>neighborChanged</c>, then <c>updateShape</c>) as the flags ask.
    /// </summary>
    /// <returns>Whether the block changed.</returns>
    public bool SetBlock(Vector position, IBlock block, BlockUpdateFlags flags, int updateLimit = UpdateLimit)
    {
        if (this.GetBlock(position).IsSameState(block) || !this.access.SetBlock(position, block))
            return false;

        if ((flags & BlockUpdateFlags.SkipOnPlace) == 0)
            this.OnPlace(position, block);

        // onPlace may have replaced the block (lava touching water), and that change did its own updates.
        if (!this.GetBlock(position).IsSameState(block))
            return true;

        if ((flags & BlockUpdateFlags.Neighbors) != 0)
            this.neighborUpdater.UpdateNeighborsAt(position);

        if ((flags & BlockUpdateFlags.KnownShape) == 0 && updateLimit > 0)
        {
            var shapeFlags = flags & ~(BlockUpdateFlags.Neighbors | BlockUpdateFlags.SuppressDrops);
            foreach (var face in updateShapeOrder)
                this.neighborUpdater.ShapeUpdate(face.Opposite(), block, position.Offset(face), shapeFlags, updateLimit - 1);
        }

        return true;
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
    /// Lets fluids react to a block something else changed with block updates (a player placing or breaking a block): a
    /// placed liquid and neighboring liquids get vanilla's <c>onPlace</c> and <c>neighborChanged</c>, and fluid neighbors
    /// schedule the tick their <c>updateShape</c> would.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="SetBlock"/>, neighbor shapes outside fluids are left alone, as they were before fluids were ported.
    /// </remarks>
    public void OnBlockChanged(Vector position, IBlock block)
    {
        this.OnPlace(position, block);
        if (!this.GetBlock(position).IsSameState(block))
            return;

        this.neighborUpdater.UpdateNeighborsAt(position);
        foreach (var face in updateShapeOrder)
        {
            var neighborPosition = position.Offset(face);
            var neighbor = this.GetBlock(neighborPosition);
            var fluid = FluidState.Of(neighbor);
            if (fluid.IsEmpty || (neighbor.IsLiquidBlock() && !fluid.IsSource && !FluidState.Of(block).IsSource))
                continue;

            this.ScheduleTick(neighborPosition, fluid.Kind, fluid.Type!.GetTickDelay(this));
        }
    }

    /// <summary>
    /// Vanilla <c>Level.destroyBlock</c>: the block is replaced by its fluid (air, or the water a waterlogged block held).
    /// </summary>
    /// <remarks>
    /// Vanilla also plays the break effect and drops the block's loot, which Obsidian doesn't do for these yet.
    /// </remarks>
    private bool DestroyBlock(Vector position, int updateLimit)
    {
        if (this.GetBlock(position).IsAir)
            return false;

        return this.SetBlock(position, this.GetFluid(position).CreateLegacyBlock(), BlockUpdateFlags.All, updateLimit);
    }

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

        var onSoulSoil = this.GetBlock(position + Vector.Down).Material == Material.SoulSoil;
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

            if (onSoulSoil && this.GetBlock(neighbor).Material == Material.BlueIce)
            {
                this.SetBlock(position, basalt, BlockUpdateFlags.All);
                this.Fizz(position);
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Vanilla <c>NeighborUpdater.executeShapeUpdate</c> and <c>Block.updateOrDestroy</c>: the block at
    /// <paramref name="position"/> reacts to its neighbor in <paramref name="direction"/> becoming
    /// <paramref name="neighbor"/>, being destroyed when its new state is air.
    /// </summary>
    private void ExecuteShapeUpdate(BlockFace direction, Vector position, IBlock neighbor, BlockUpdateFlags flags, int updateLimit)
    {
        var block = this.GetBlock(position);
        var updated = ShapeUpdater.UpdateShape(this.ShapeView, block, position, direction, neighbor);
        if (updated.IsSameState(block))
            return;

        if (updated.IsAir)
            this.DestroyBlock(position, updateLimit);
        else
            this.SetBlock(position, updated, flags & ~BlockUpdateFlags.SuppressDrops, updateLimit);
    }

    /// <summary>
    /// Vanilla's <c>CollectingNeighborUpdater</c>: updates triggered while others run are queued and run depth first
    /// after the current step, instead of recursing.
    /// </summary>
    private sealed class NeighborUpdater(FluidLevel level)
    {
        // Vanilla's max-chained-neighbor-updates server default.
        private const int MaxChainedUpdates = 1000000;

        private readonly Stack<IUpdate> stack = new();
        private readonly List<IUpdate> addedThisLayer = [];
        private int count;

        public void UpdateNeighborsAt(Vector position) => this.AddAndRun(new MultiNeighborUpdate(position));

        public void ShapeUpdate(BlockFace direction, IBlock neighbor, Vector position, BlockUpdateFlags flags, int updateLimit) =>
            this.AddAndRun(new ShapeUpdateStep(direction, neighbor, position, flags, updateLimit));

        private void AddAndRun(IUpdate update)
        {
            var running = this.count > 0;
            var overLimit = this.count >= MaxChainedUpdates;
            this.count++;
            if (!overLimit)
            {
                if (running)
                    this.addedThisLayer.Add(update);
                else
                    this.stack.Push(update);
            }

            if (!running)
                this.RunUpdates();
        }

        private void RunUpdates()
        {
            try
            {
                while (this.stack.Count > 0 || this.addedThisLayer.Count > 0)
                {
                    for (var i = this.addedThisLayer.Count - 1; i >= 0; i--)
                        this.stack.Push(this.addedThisLayer[i]);

                    this.addedThisLayer.Clear();
                    var update = this.stack.Peek();
                    while (this.addedThisLayer.Count == 0)
                    {
                        if (!update.RunNext(level))
                        {
                            this.stack.Pop();
                            break;
                        }
                    }
                }
            }
            finally
            {
                this.stack.Clear();
                this.addedThisLayer.Clear();
                this.count = 0;
            }
        }

        private interface IUpdate
        {
            /// <summary>Runs the next step; <c>false</c> once the update is done.</summary>
            public bool RunNext(FluidLevel level);
        }

        // neighborChanged for each neighbor of a changed block, one per step.
        private sealed class MultiNeighborUpdate(Vector source) : IUpdate
        {
            private int index;

            public bool RunNext(FluidLevel level)
            {
                var position = source.Offset(updateOrder[this.index++]);
                level.NeighborChanged(position, level.GetBlock(position));
                return this.index < updateOrder.Length;
            }
        }

        private sealed class ShapeUpdateStep(BlockFace direction, IBlock neighbor, Vector position, BlockUpdateFlags flags, int updateLimit)
            : IUpdate
        {
            public bool RunNext(FluidLevel level)
            {
                level.ExecuteShapeUpdate(direction, position, neighbor, flags, updateLimit);
                return false;
            }
        }
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

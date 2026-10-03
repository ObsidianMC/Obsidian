using Obsidian.WorldData.Features;

namespace Obsidian.WorldData.Fluids;

/// <summary>
/// Vanilla's <c>FlowingFluid</c>: how water and lava spread, fall and dry up when their scheduled tick runs.
/// </summary>
/// <remarks>
/// Each fluid is a stateless singleton; everything it reads or changes goes through the <see cref="FluidLevel"/> it's
/// given, which also reproduces vanilla's block update side effects.
/// </remarks>
internal abstract class FlowingFluid
{
    // Vanilla Direction.Plane.HORIZONTAL order, which every neighbor scan uses.
    private static readonly BlockFace[] horizontal = [BlockFace.North, BlockFace.East, BlockFace.South, BlockFace.West];

    // Vanilla spreads to the sides in Direction order (getSpread returns an EnumMap), whatever order they were found in.
    private static readonly BlockFace[] spreadOrder = [BlockFace.North, BlockFace.South, BlockFace.West, BlockFace.East];

    /// <summary>
    /// The source fluid (vanilla <c>getSource()</c>), e.g. <see cref="FluidKind.Water"/>.
    /// </summary>
    public abstract FluidKind SourceKind { get; }

    /// <summary>
    /// The flowing fluid (vanilla <c>getFlowing()</c>), e.g. <see cref="FluidKind.FlowingWater"/>.
    /// </summary>
    public abstract FluidKind FlowingKind { get; }

    /// <summary>
    /// Vanilla <c>isSame</c>: whether <paramref name="kind"/> is this fluid, source or flowing.
    /// </summary>
    public bool IsSame(FluidKind kind) => kind == this.SourceKind || kind == this.FlowingKind;

    /// <summary>
    /// Ticks between a change and the fluid's next scheduled tick.
    /// </summary>
    public abstract int GetTickDelay(FluidLevel level);

    /// <summary>
    /// Vanilla <c>canBeReplacedWith</c> for a block holding this fluid (<paramref name="state"/>).
    /// </summary>
    public abstract bool CanBeReplacedWith(FluidState state, FluidLevel level, Vector position, FluidKind fluid, BlockFace direction);

    /// <summary>
    /// How far sideways the fluid looks for a drop to flow toward.
    /// </summary>
    protected abstract int GetSlopeFindDistance(FluidLevel level);

    /// <summary>
    /// How much the amount drops per block flowed sideways.
    /// </summary>
    protected abstract int GetDropOff(FluidLevel level);

    /// <summary>
    /// Whether a flowing cell between two sources becomes a source (the source conversion game rules).
    /// </summary>
    protected abstract bool CanConvertToSource(FluidLevel level);

    /// <summary>
    /// Runs before the fluid replaces a block it flows into.
    /// </summary>
    protected abstract void BeforeDestroyingBlock(FluidLevel level, Vector position, IBlock block);

    /// <summary>
    /// Vanilla <c>getSpreadDelay</c>: the delay of the tick scheduled after the fluid's own amount changed.
    /// </summary>
    protected virtual int GetSpreadDelay(FluidLevel level, Vector position, FluidState current, FluidState next) => this.GetTickDelay(level);

    /// <summary>
    /// The fluid's source state (vanilla <c>getSource(false)</c>).
    /// </summary>
    public FluidState GetSource() => new(this.SourceKind, 8, false);

    /// <summary>
    /// A flowing state with <paramref name="amount"/> (1-8) of the fluid (vanilla <c>getFlowing</c>).
    /// </summary>
    public FluidState GetFlowing(int amount, bool falling) => new(this.FlowingKind, amount, falling);

    /// <summary>
    /// Vanilla <c>FlowingFluid.tick</c>: a flowing fluid recomputes its amount from its neighbors (drying up or
    /// rescheduling itself when it changed), then the fluid spreads.
    /// </summary>
    public void Tick(FluidLevel level, Vector position, IBlock block, FluidState fluid)
    {
        if (!fluid.IsSource)
        {
            var next = this.GetNewLiquid(level, position, level.GetBlock(position));
            var delay = this.GetSpreadDelay(level, position, fluid, next);
            if (next.IsEmpty)
            {
                fluid = next;
                block = BlocksRegistry.Air;
                level.SetBlock(position, block, BlockUpdateFlags.All);
            }
            else if (next != fluid)
            {
                fluid = next;
                block = fluid.CreateLegacyBlock();
                level.SetBlock(position, block, BlockUpdateFlags.All);
                level.ScheduleTick(position, fluid.Kind, delay);
            }
        }

        this.Spread(level, position, block, fluid);
    }

    /// <summary>
    /// Vanilla <c>spreadTo</c>: puts <paramref name="fluid"/> into the block at <paramref name="position"/>.
    /// </summary>
    protected virtual void SpreadTo(FluidLevel level, Vector position, IBlock block, BlockFace direction, FluidState fluid)
    {
        if (block.IsLiquidBlockContainer())
        {
            level.PlaceLiquid(position, block, fluid);
            return;
        }

        if (!block.IsAir)
            this.BeforeDestroyingBlock(level, position, block);

        level.SetBlock(position, fluid.CreateLegacyBlock(), BlockUpdateFlags.All);
    }

    /// <summary>
    /// Vanilla <c>getNewLiquid</c>: the fluid a block should hold given its neighbors: a source between two sources (when
    /// conversion is on and it rests on something solid or a source), a falling fluid under the same fluid, otherwise the
    /// strongest neighbor's amount minus the drop-off.
    /// </summary>
    protected FluidState GetNewLiquid(FluidLevel level, Vector position, IBlock block)
    {
        var maxAmount = 0;
        var sources = 0;
        foreach (var face in horizontal)
        {
            var neighborPosition = position.Offset(face);
            var neighbor = level.GetBlock(neighborPosition);
            var neighborFluid = FluidState.Of(neighbor);
            if (this.IsSame(neighborFluid.Kind) && CanPassThroughWall(face, block, neighbor))
            {
                if (neighborFluid.IsSource)
                    sources++;

                maxAmount = Math.Max(maxAmount, neighborFluid.Amount);
            }
        }

        if (sources >= 2 && this.CanConvertToSource(level))
        {
            var below = level.GetBlock(position + Vector.Down);
            if (below.IsSolid() || this.IsSourceOfThisType(FluidState.Of(below)))
                return this.GetSource();
        }

        var abovePosition = position + Vector.Up;
        var above = level.GetBlock(abovePosition);
        var aboveFluid = FluidState.Of(above);
        if (!aboveFluid.IsEmpty && this.IsSame(aboveFluid.Kind) && CanPassThroughWall(BlockFace.Up, block, above))
            return this.GetFlowing(8, true);

        var amount = maxAmount - this.GetDropOff(level);
        return amount <= 0 ? FluidState.Empty : this.GetFlowing(amount, false);
    }

    private void Spread(FluidLevel level, Vector position, IBlock block, FluidState fluid)
    {
        if (fluid.IsEmpty)
            return;

        var belowPosition = position + Vector.Down;
        var below = level.GetBlock(belowPosition);
        var belowFluid = FluidState.Of(below);
        if (this.CanMaybePassThrough(BlockFace.Down, block, below, belowFluid))
        {
            var next = this.GetNewLiquid(level, belowPosition, below);
            if (belowFluid.CanBeReplacedWith(level, belowPosition, next.Kind, BlockFace.Down) && below.CanHoldSpecificFluid(next.Kind))
            {
                this.SpreadTo(level, belowPosition, below, BlockFace.Down, next);

                // Falling between sources also spreads sideways.
                if (this.SourceNeighborCount(level, position) >= 3)
                    this.SpreadToSides(level, position, fluid, block);

                return;
            }
        }

        if (fluid.IsSource || !this.IsWaterHole(level, block, belowPosition, below))
            this.SpreadToSides(level, position, fluid, block);
    }

    private void SpreadToSides(FluidLevel level, Vector position, FluidState fluid, IBlock block)
    {
        var amount = fluid.Falling ? 7 : fluid.Amount - this.GetDropOff(level);
        if (amount <= 0)
            return;

        var spread = this.GetSpread(level, position, block);
        foreach (var face in spreadOrder)
        {
            var next = spread[(int)face];
            if (next is not null)
            {
                var target = position.Offset(face);
                this.SpreadTo(level, target, level.GetBlock(target), face, next.Value);
            }
        }
    }

    /// <summary>
    /// Vanilla <c>getSpread</c>: the fluid for each side the fluid flows to, indexed by <see cref="BlockFace"/>. Only the
    /// sides with the shortest way down (within the slope distance) are kept; on flat ground that's every open side.
    /// </summary>
    private FluidState?[] GetSpread(FluidLevel level, Vector position, IBlock block)
    {
        var best = 1000;
        var spread = new FluidState?[6];
        SpreadContext? context = null;

        foreach (var face in horizontal)
        {
            var neighborPosition = position.Offset(face);
            var neighbor = level.GetBlock(neighborPosition);
            var neighborFluid = FluidState.Of(neighbor);
            if (!this.CanMaybePassThrough(face, block, neighbor, neighborFluid))
                continue;

            var next = this.GetNewLiquid(level, neighborPosition, neighbor);
            if (!neighbor.CanHoldSpecificFluid(next.Kind))
                continue;

            context ??= new SpreadContext(this, level, position);
            var distance = context.IsHole(neighborPosition)
                ? 0
                : this.GetSlopeDistance(level, neighborPosition, 1, face.Opposite(), neighbor, context);

            if (distance < best)
                Array.Clear(spread);

            if (distance <= best)
            {
                if (neighborFluid.CanBeReplacedWith(level, neighborPosition, next.Kind, face))
                    spread[(int)face] = next;

                best = distance;
            }
        }

        return spread;
    }

    /// <summary>
    /// Vanilla <c>getSlopeDistance</c>: the fewest steps from <paramref name="position"/> to a hole the fluid could fall
    /// into, searching up to the slope distance, or 1000 when there's none.
    /// </summary>
    private int GetSlopeDistance(FluidLevel level, Vector position, int distance, BlockFace from, IBlock block, SpreadContext context)
    {
        var best = 1000;
        foreach (var face in horizontal)
        {
            if (face == from)
                continue;

            var neighborPosition = position.Offset(face);
            var neighbor = context.GetBlock(neighborPosition);
            if (!this.CanPassThrough(face, block, neighbor, FluidState.Of(neighbor)))
                continue;

            if (context.IsHole(neighborPosition))
                return distance;

            if (distance < this.GetSlopeFindDistance(level))
                best = Math.Min(best, this.GetSlopeDistance(level, neighborPosition, distance + 1, face.Opposite(), neighbor, context));
        }

        return best;
    }

    /// <summary>
    /// Vanilla <c>isWaterHole</c>: the fluid can fall from <paramref name="block"/> into the block below.
    /// </summary>
    private bool IsWaterHole(FluidLevel level, IBlock block, Vector belowPosition, IBlock below) =>
        CanPassThroughWall(BlockFace.Down, block, below)
            && (this.IsSame(FluidState.Of(below).Kind) || (below.CanHoldAnyFluid() && below.CanHoldSpecificFluid(this.FlowingKind)));

    private bool CanPassThrough(BlockFace direction, IBlock block, IBlock neighbor, FluidState neighborFluid) =>
        this.CanMaybePassThrough(direction, block, neighbor, neighborFluid) && neighbor.CanHoldSpecificFluid(this.FlowingKind);

    private bool CanMaybePassThrough(BlockFace direction, IBlock block, IBlock neighbor, FluidState neighborFluid) =>
        !this.IsSourceOfThisType(neighborFluid) && neighbor.CanHoldAnyFluid() && CanPassThroughWall(direction, block, neighbor);

    private bool IsSourceOfThisType(FluidState fluid) => fluid.IsSource && this.IsSame(fluid.Kind);

    private int SourceNeighborCount(FluidLevel level, Vector position)
    {
        var count = 0;
        foreach (var face in horizontal)
        {
            if (this.IsSourceOfThisType(level.GetFluid(position.Offset(face))))
                count++;
        }

        return count;
    }

    /// <summary>
    /// Vanilla <c>canPassThroughWall</c>: fluid can cross between two blocks unless either has a full collision shape or
    /// their touching collision faces close the gap together (a top slab next to a bottom slab, for instance).
    /// </summary>
    private static bool CanPassThroughWall(BlockFace direction, IBlock block, IBlock neighbor)
    {
        if (neighbor.HasBlockCollisionShape() || block.HasBlockCollisionShape())
            return false;

        if (block.HasEmptyCollisionShape() && neighbor.HasEmptyCollisionShape())
            return true;

        return !BlockPhysics.MergedFaceOccludes(block, neighbor, direction);
    }

    /// <summary>
    /// Vanilla's <c>FlowingFluid.SpreadContext</c>: block and hole lookups cached for one spread, all on the spreading
    /// fluid's layer.
    /// </summary>
    private sealed class SpreadContext(FlowingFluid fluid, FluidLevel level, Vector origin)
    {
        private readonly Dictionary<(int X, int Z), IBlock> blocks = [];
        private readonly Dictionary<(int X, int Z), bool> holes = [];

        public IBlock GetBlock(Vector position)
        {
            var key = (position.X - origin.X, position.Z - origin.Z);
            if (!this.blocks.TryGetValue(key, out var block))
                this.blocks[key] = block = level.GetBlock(position);

            return block;
        }

        public bool IsHole(Vector position)
        {
            var key = (position.X - origin.X, position.Z - origin.Z);
            if (!this.holes.TryGetValue(key, out var hole))
            {
                var belowPosition = position + Vector.Down;
                this.holes[key] = hole = fluid.IsWaterHole(level, this.GetBlock(position), belowPosition, level.GetBlock(belowPosition));
            }

            return hole;
        }
    }
}

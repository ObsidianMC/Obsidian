using Obsidian.API.Blocks;
using Obsidian.WorldData.Features;

namespace Obsidian.WorldData.Fluids;

/// <summary>
/// Vanilla's <c>CollectingNeighborUpdater</c> for a <see cref="FluidLevel"/>: updates triggered while others run are
/// queued and run depth first after the current step, instead of recursing.
/// </summary>
internal sealed class NeighborUpdater(FluidLevel level)
{
    // Vanilla's max-chained-neighbor-updates server default.
    private const int MaxChainedUpdates = 1000000;

    // Vanilla NeighborUpdater.UPDATE_ORDER (neighborChanged).
    private static readonly BlockFace[] updateOrder = [BlockFace.West, BlockFace.East, BlockFace.Down, BlockFace.Up,
        BlockFace.North, BlockFace.South];

    private readonly Stack<IUpdate> stack = new();
    private readonly List<IUpdate> addedThisLayer = [];
    private int count;

    /// <summary>
    /// <c>neighborChanged</c> for each neighbor of <paramref name="position"/>, from <paramref name="source"/>.
    /// </summary>
    public void UpdateNeighborsAt(Vector position, IBlock source) =>
        this.AddAndRun(new MultiNeighborUpdate(position, source, skip: null));

    /// <summary>
    /// Vanilla <c>updateNeighborsAtExceptFromFacing</c>: <see cref="UpdateNeighborsAt"/> but for the neighbor in
    /// <paramref name="skip"/>.
    /// </summary>
    public void UpdateNeighborsAtExcept(Vector position, IBlock source, BlockFace skip) =>
        this.AddAndRun(new MultiNeighborUpdate(position, source, skip));

    /// <summary>
    /// Vanilla <c>neighborChanged</c> for one block: the block at <paramref name="position"/> hears that
    /// <paramref name="source"/> at <paramref name="sourcePosition"/> changed.
    /// </summary>
    public void NeighborChanged(Vector position, IBlock source, Vector sourcePosition) =>
        this.AddAndRun(new SimpleNeighborUpdate(position, source, sourcePosition));

    /// <summary>
    /// <c>updateShape</c> for the block at <paramref name="position"/>, whose neighbor in
    /// <paramref name="direction"/> became <paramref name="neighbor"/>.
    /// </summary>
    public void ShapeUpdate(
        BlockFace direction,
        IBlock neighbor,
        Vector position,
        BlockUpdateFlags flags,
        int updateLimit,
        VanillaShapes shapes) =>
        this.AddAndRun(new ShapeUpdateStep(direction, neighbor, position, flags, updateLimit, shapes));

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

    // neighborChanged for each neighbor of a changed block but the skipped one, one per step.
    private sealed class MultiNeighborUpdate(Vector source, IBlock sourceBlock, BlockFace? skip) : IUpdate
    {
        private int index;

        public bool RunNext(FluidLevel level)
        {
            if (updateOrder[this.index] == skip)
                this.index++;

            var position = source.Offset(updateOrder[this.index++]);
            level.NeighborChanged(position, sourceBlock, source);

            if (this.index < updateOrder.Length && updateOrder[this.index] == skip)
                this.index++;

            return this.index < updateOrder.Length;
        }
    }

    private sealed class SimpleNeighborUpdate(Vector position, IBlock source, Vector sourcePosition) : IUpdate
    {
        public bool RunNext(FluidLevel level)
        {
            level.NeighborChanged(position, source, sourcePosition);
            return false;
        }
    }

    private sealed class ShapeUpdateStep(
        BlockFace direction,
        IBlock neighbor,
        Vector position,
        BlockUpdateFlags flags,
        int updateLimit,
        VanillaShapes shapes) : IUpdate
    {
        public bool RunNext(FluidLevel level)
        {
            level.ExecuteShapeUpdate(direction, position, neighbor, flags, updateLimit, shapes);
            return false;
        }
    }
}

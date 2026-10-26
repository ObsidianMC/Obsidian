using Obsidian.WorldData.BlockNotifications;

namespace Obsidian.WorldData.Pistons;

/// <summary>
/// Vanilla's <c>PistonStructureResolver</c>: plans which blocks a piston moves and which it breaks. Extending pushes the
/// line in front of the piston, and retracting pulls the sticky line behind it. A plan fails when it needs more than
/// <see cref="MaxPushDepth"/> blocks, meets an immovable block, or reads a chunk that isn't loaded.
/// </summary>
internal sealed class PistonStructureResolver
{
    public const int MaxPushDepth = 12;

    private readonly IBlockChangeWorld world;
    private readonly Vector piston;
    private readonly BlockFace facing;
    private readonly bool extending;
    private readonly Vector start;
    private readonly List<Vector> toPush = [];
    private readonly List<Vector> toDestroy = [];
    private bool unloaded;

    public PistonStructureResolver(IBlockChangeWorld world, Vector piston, BlockFace facing, bool extending)
    {
        this.world = world;
        this.piston = piston;
        this.facing = facing;
        this.extending = extending;
        if (extending)
        {
            this.MoveDirection = facing;
            this.start = PistonRules.Step(piston, facing, 1);
        }
        else
        {
            this.MoveDirection = facing.Opposite();
            this.start = PistonRules.Step(piston, facing, 2);
        }
    }

    /// <summary>The direction the blocks move in (vanilla <c>getMovementDirection</c>).</summary>
    public BlockFace MoveDirection { get; }

    /// <summary>The blocks that move, in the order vanilla's moves are applied.</summary>
    public IReadOnlyList<Vector> ToPush => this.toPush;

    /// <summary>The blocks that a push breaks (their push reaction is DESTROY).</summary>
    public IReadOnlyList<Vector> ToDestroy => this.toDestroy;

    /// <summary>Whether the plan succeeded. A plan that reads an unloaded chunk fails.</summary>
    public bool Resolve()
    {
        this.toPush.Clear();
        this.toDestroy.Clear();

        var startState = this.Read(this.start);
        if (!PistonRules.IsPushable(startState, this.world, this.start, this.MoveDirection, false, this.facing))
        {
            if (this.extending && PistonRules.PushReaction(startState) == "DESTROY")
            {
                this.toDestroy.Add(this.start);
                return !this.unloaded;
            }

            return false;
        }

        if (!this.AddBlockLine(this.start, this.MoveDirection))
            return false;

        for (var i = 0; i < this.toPush.Count; i++)
        {
            if (PistonRules.IsStickyBlock(this.Read(this.toPush[i])) && !this.AddBranchingBlocks(this.toPush[i]))
                return false;
        }

        return !this.unloaded;
    }

    private IBlock Read(Vector position)
    {
        var block = this.world.GetLoadedBlock(position);
        if (block is null)
        {
            this.unloaded = true;
            return BlocksRegistry.VoidAir;
        }

        return block;
    }

    // Vanilla addBlockLine: follows a line of blocks from a start position in direction 'side' (the piston's push line).
    private bool AddBlockLine(Vector position, BlockFace side)
    {
        var state = this.Read(position);
        if (state.IsAir)
            return true;

        if (!PistonRules.IsPushable(state, this.world, position, this.MoveDirection, false, side))
            return true;

        if (position == this.piston || this.toPush.Contains(position))
            return true;

        var count = 1;
        if (count + this.toPush.Count > MaxPushDepth)
            return false;

        // Sticky blocks pull the blocks behind them into the line.
        var opposite = this.MoveDirection.Opposite();
        while (PistonRules.IsStickyBlock(state))
        {
            var next = PistonRules.Step(position, opposite, count);
            var previous = state;
            state = this.Read(next);
            if (state.IsAir || !PistonRules.CanStickToEachOther(previous, state)
                || !PistonRules.IsPushable(state, this.world, next, this.MoveDirection, false, opposite)
                || next == this.piston)
            {
                break;
            }

            if (++count + this.toPush.Count > MaxPushDepth)
                return false;
        }

        var added = 0;
        for (var i = count - 1; i >= 0; i--)
        {
            this.toPush.Add(PistonRules.Step(position, opposite, i));
            added++;
        }

        var offset = 1;
        while (true)
        {
            var ahead = PistonRules.Step(position, this.MoveDirection, offset);
            var index = this.toPush.IndexOf(ahead);
            if (index > -1)
            {
                // A collision with the line: the blocks past the collision move to its front (vanilla reorderListAtCollision).
                this.ReorderAtCollision(added, index);
                for (var i = 0; i <= index + added; i++)
                {
                    if (PistonRules.IsStickyBlock(this.Read(this.toPush[i])) && !this.AddBranchingBlocks(this.toPush[i]))
                        return false;
                }

                return true;
            }

            state = this.Read(ahead);
            if (state.IsAir)
                return true;

            if (!PistonRules.IsPushable(state, this.world, ahead, this.MoveDirection, true, this.MoveDirection) || ahead == this.piston)
                return false;

            if (PistonRules.PushReaction(state) == "DESTROY")
            {
                this.toDestroy.Add(ahead);
                return true;
            }

            if (this.toPush.Count >= MaxPushDepth)
                return false;

            this.toPush.Add(ahead);
            added++;
            offset++;
        }
    }

    // Vanilla addBranchingBlocks: sticky blocks also pull on their neighbors across the line.
    private bool AddBranchingBlocks(Vector position)
    {
        var state = this.Read(position);
        foreach (var face in Enum.GetValues<BlockFace>())
        {
            if (PistonRules.Axis(face) == PistonRules.Axis(this.MoveDirection))
                continue;

            var neighbor = PistonRules.Step(position, face, 1);
            var neighborState = this.Read(neighbor);
            if (PistonRules.CanStickToEachOther(neighborState, state) && !this.AddBlockLine(neighbor, face))
                return false;
        }

        return true;
    }

    private void ReorderAtCollision(int added, int index)
    {
        var front = this.toPush.GetRange(0, index);
        var moved = this.toPush.GetRange(this.toPush.Count - added, added);
        var rest = this.toPush.GetRange(index, this.toPush.Count - added - index);
        this.toPush.Clear();
        this.toPush.AddRange(front);
        this.toPush.AddRange(moved);
        this.toPush.AddRange(rest);
    }
}

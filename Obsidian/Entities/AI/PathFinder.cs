using Obsidian.API.AI;
namespace Obsidian.Entities.AI;

internal sealed record MobPath(IReadOnlyList<VectorD> Nodes, bool ReachedTarget);

internal sealed class WalkNodeEvaluator(IPathBody body)
{
    private readonly Dictionary<(int X, int Z, double Y, bool AllowHazard), VectorD?> groundCache = [];
    private bool CanOpenDoors => body.CanOpenDoors;
    private static readonly (int X, int Z)[] directions =
    [(-1, 0), (1, 0), (0, -1), (0, 1), (-1, -1), (-1, 1), (1, -1), (1, 1)];

    public VectorD? FindGround(int x, int z, double currentY, bool allowHazard = false)
    {
        var key = (x, z, currentY, allowHazard);
        if (!groundCache.TryGetValue(key, out var ground))
        {
            ground = EvaluateGround(x, z, currentY, allowHazard);
            groundCache.Add(key, ground);
        }
        return ground;
    }

    // The mob's step height for the search under way, read once a search; NaN until it's read.
    private float maximumRise = float.NaN;

    // Vanilla's cW(): the fall distance a mob may drop, read once a search.
    private int maximumFall = -1;

    // The surfaces a column offers, reused by every evaluation on the thread: a search evaluates many columns.
    [ThreadStatic] private static List<double>? surfaces;

    // Terrain and door permissions can change between searches, including a previously blocked column.
    public void Reset()
    {
        groundCache.Clear();
        maximumRise = float.NaN;
        maximumFall = -1;
    }

    private VectorD? EvaluateGround(int x, int z, double currentY, bool allowHazard)
    {
        // Ground navigation follows the surface, including while the mob bobs above it.
        var waterY = (int)Math.Floor(currentY - 0.5f);
        if (!body.SwimmingNavigation && body.CanFloat && IsWater(x, waterY, z))
        {
            for (var depth = 0; depth < 16; depth++)
            {
                waterY++;
                if (IsWater(x, waterY, z))
                    continue;
                var surface = new VectorD(x + 0.5f, waterY, z + 0.5f);
                if (body.Terrain.IsFree(body.Dimension.CreateBBFromPosition(surface), CanOpenDoors) && Accepts(surface, allowHazard))
                    return surface;
                break;
            }
        }

        // Vanilla's WalkNodeEvaluator: a ground mob that cannot float and stands in water descends the water column and
        // takes the lowest contiguous water cell as its node (fall search through WATER), skipping floor candidates.
        var feetCell = (int)Math.Floor(currentY);
        if (!body.SwimmingNavigation && !body.CanFloat &&
            IsWater(x, feetCell, z))
        {
            var lowest = feetCell;
            while (IsWater(x, lowest - 1, z))
                lowest--;
            var bottom = new VectorD(x + 0.5f, lowest, z + 0.5f);
            return body.Terrain.IsFree(body.Dimension.CreateBBFromPosition(bottom), CanOpenDoors) && Accepts(bottom, allowHazard)
                ? bottom
                : null;
        }

        var candidates = surfaces ??= [];
        candidates.Clear();
        if (float.IsNaN(this.maximumRise))
            this.maximumRise = Math.Max(1.125f, body.StepHeight);
        var maximumRise = this.maximumRise;
        if (maximumFall < 0)
            maximumFall = body.MaxFall;
        for (var y = (int)Math.Floor(currentY + maximumRise); y >= (int)Math.Floor(currentY - maximumFall) - 1; y--)
        {
            var block = body.Terrain.GetBlock(new Vector(x, y, z));
            if (block == null)
                continue;
            if (CanOpenDoors && TagsRegistry.Block.WoodenDoors.Entries.Contains(block.RegistryId))
                continue;

            foreach (var shape in BlockCollisionShapes.Get(body.Level, new Vector(x, y, z), block))
            {
                if (shape.Min.X <= 0.5f && shape.Max.X >= 0.5f && shape.Min.Z <= 0.5f && shape.Max.Z >= 0.5f)
                    candidates.Add(y + shape.Max.Y);
            }
        }

        // Highest first, each height once, as Distinct().OrderByDescending() gave them, without their allocations.
        candidates.Sort(static (left, right) => right.CompareTo(left));
        for (var index = 0; index < candidates.Count; index++)
        {
            var y = candidates[index];
            if (index > 0 && candidates[index - 1] == y)
                continue;
            if (y > currentY + maximumRise + 0.00001f || y < currentY - maximumFall)
                continue;

            var position = new VectorD(x + 0.5f, StandingY(x, z, y), z + 0.5f);
            if (body.Terrain.IsFree(body.Dimension.CreateBBFromPosition(position), CanOpenDoors) && Accepts(position, allowHazard))
                return position;
        }

        return null;
    }

    // Vanilla classifies every water state, waterlogged ones included, as PathType.WATER; the generated state table holds that.
    private bool IsWater(int x, int y, int z) =>
        body.Terrain.GetBlock(new Vector(x, y, z)) is { } block && PathTypeClassifier.Base(block.GetHashCode()) == PathType.WATER;

    // Vanilla's WalkNodeEvaluator.a(node, side, side): a diagonal needs both orthogonal nodes, neither higher than the node
    // nor a closed wooden door, and a side level with the node needs a non-negative malus (fences excepted for narrow mobs).
    private bool CornerAllowed(VectorD current, VectorD? sideX, VectorD? sideZ)
    {
        if (sideX is not VectorD sx || sideZ is not VectorD sz)
            return false;
        if (sx.Y > current.Y || sz.Y > current.Y)
            return false;

        var sxType = PathTypeClassifier.ForNode(body, (Vector)sx.Floor());
        var szType = PathTypeClassifier.ForNode(body, (Vector)sz.Floor());
        if (sxType == PathType.WALKABLE_DOOR || szType == PathType.WALKABLE_DOOR)
            return false;

        var fence = sxType == PathType.FENCE && szType == PathType.FENCE && body.Dimension.Width < 0.5f;
        return (sz.Y < current.Y || GetCost(sz) >= 0 || fence) && (sx.Y < current.Y || GetCost(sx) >= 0 || fence);
    }

    // Vanilla's path nodes are whole cells: a partial block's top inside a BLOCKED cell (a bottom slab) stands in the cell above.
    private double StandingY(int x, int z, double top)
    {
        var cell = (int)Math.Floor(top);
        if (top == cell || body.Terrain.GetBlock(new Vector(x, cell, z)) is not { } block)
            return top;
        return PathTypeClassifier.Base(block.GetHashCode()) == PathType.BLOCKED ? cell + 1 : top;
    }

    public IEnumerable<VectorD> GetNeighbors(VectorD current)
    {
        // Vanilla's cardinal acceptance: a hazardous node may step to another hazardous node.
        var allowHazard = GetCost(current) < 0;
        foreach (var (x, z) in directions)
        {
            var next = FindGround((int)Math.Floor(current.X) + x, (int)Math.Floor(current.Z) + z, current.Y, allowHazard);
            if (next is not VectorD position)
                continue;

            if (x != 0 && z != 0)
            {
                var sideX = FindGround((int)Math.Floor(current.X) + x, (int)Math.Floor(current.Z), current.Y, allowHazard);
                var sideZ = FindGround((int)Math.Floor(current.X), (int)Math.Floor(current.Z) + z, current.Y, allowHazard);
                if (!CornerAllowed(current, sideX, sideZ))
                    continue;
            }

            var raisedCurrent = new VectorD(current.X, Math.Max(current.Y, position.Y), current.Z);
            var raisedNext = new VectorD(position.X, raisedCurrent.Y, position.Z);
            var currentBounds = body.Dimension.CreateBBFromPosition(raisedCurrent);
            var nextBounds = body.Dimension.CreateBBFromPosition(raisedNext);
            var passage = new BoundingBox(VectorD.Min(currentBounds.Min, nextBounds.Min), VectorD.Max(currentBounds.Max, nextBounds.Max));
            if (body.Terrain.IsFree(passage, CanOpenDoors) && HasStepUpClearance(current, position))
                yield return position;
        }
    }

    // Vanilla's jump search rejects a step up for a mob narrower than one block when its body would hit the cell above the start.
    private bool HasStepUpClearance(VectorD current, VectorD position)
    {
        if (position.Y <= current.Y)
            return true;

        // Vanilla's jump search: no jump from a fence, unpassable rail, trapdoor or powder snow, from sticky honey,
        // or when the cell above the start is unpassable.
        var start = new Vector((int)Math.Floor(current.X), (int)Math.Floor(current.Y), (int)Math.Floor(current.Z));
        var startType = PathTypeClassifier.ForNode(body, start);
        if (startType is PathType.FENCE or PathType.UNPASSABLE_RAIL or PathType.TRAPDOOR or PathType.POWDER_SNOW or PathType.STICKY_HONEY)
            return false;
        var aboveType = PathTypeClassifier.ForNode(body, new Vector(start.X, start.Y + 1, start.Z));
        if (body.Malus(PathNodeMapping.ToPublic(aboveType)) < 0)
            return false;

        if (body.Dimension.Width >= 1)
            return true;

        var halfWidth = body.Dimension.Width / 2;
        var headCell = Math.Floor(current.Y) + 1;
        var box = new BoundingBox(
            new VectorD(position.X - halfWidth, headCell + 0.001f, position.Z - halfWidth),
            new VectorD(position.X + halfWidth, position.Y + body.Dimension.Height - 0.002f, position.Z + halfWidth));
        return body.Terrain.IsFree(box, CanOpenDoors);
    }

    public double GetCost(VectorD position)
    {
        var feet = body.Terrain.GetBlock((Vector)position.Floor());
        var below = body.Terrain.GetBlock((Vector)(position - new VectorD(0, 0.01f, 0)).Floor());
        // Negative infinity marks a cell with no node; a hazardous node has a finite negative cost.
        return feet == null || below == null ? double.NegativeInfinity : body.NodeCost((Vector)position.Floor(), feet, below);
    }

    private bool Accepts(VectorD position, bool allowHazard)
    {
        var cost = GetCost(position);
        return cost >= 0 || allowHazard && !double.IsNegativeInfinity(cost);
    }
}

internal sealed class PathFinder(IPathBody body)
{
    private readonly WalkNodeEvaluator evaluator = new(body);

    // The search's open set and nodes, reused by every search on the thread: a search runs to its end without yielding,
    // and mobs search many times a tick, which allocated and grew new ones each time.
    [ThreadStatic] private static PriorityQueue<Node, (double Cost, int Order)>? searchOpen;
    [ThreadStatic] private static Dictionary<Vector, Node>? searchNodes;

    public MobPath? FindPath(VectorD target)
    {
        var open = searchOpen ??= new();
        var nodes = searchNodes ??= [];
        try
        {
            return this.FindPath(target, open, nodes);
        }
        finally
        {
            open.Clear();
            nodes.Clear();
        }
    }

    private MobPath? FindPath(VectorD target, PriorityQueue<Node, (double Cost, int Order)> open, Dictionary<Vector, Node> nodes)
    {
        evaluator.Reset();
        var volume = VolumeMovement.UsesVolume(body);
        var start = volume ? body.Position : evaluator.FindGround((int)Math.Floor(body.Position.X), (int)Math.Floor(body.Position.Z), body.Position.Y);
        if (start is not VectorD startPosition)
            return null;

        var targetKey = Key(target);
        var range = Math.Max(16, body.FollowRange);
        var first = new Node(startPosition, null, 0);
        nodes.Add(Key(startPosition), first);
        open.Enqueue(first, (Distance(startPosition, target), 0));
        var closest = first;
        var closestDistance = Distance(first.Position, target);
        var order = 1;

        for (var visited = 0; visited < 1024 && open.TryDequeue(out var current, out _); visited++)
        {
            if (!ReferenceEquals(nodes[Key(current.Position)], current) || current.Closed)
                continue;

            current.Closed = true;
            var distance = Distance(current.Position, target);
            if (distance < closestDistance)
            {
                closest = current;
                closestDistance = distance;
            }

            // Vanilla's path ends at the target's block, not at any node within a block of it.
            if (Key(current.Position) == targetKey)
                return BuildPath(current, true);

            foreach (var next in volume ? VolumeMovement.Neighbors(body, current.Position) : evaluator.GetNeighbors(current.Position))
            {
                if (Distance(next, startPosition) > range)
                    continue;

                var cost = current.Cost + Distance(current.Position, next) + (volume ? 0 : Math.Max(0, evaluator.GetCost(next)));
                var key = Key(next);
                if (nodes.TryGetValue(key, out var previous) && previous.Cost <= cost)
                    continue;

                var node = new Node(next, current, cost);
                nodes[key] = node;
                open.Enqueue(node, (cost + Distance(next, target) * 1.5f, order++));
            }
        }

        return closest == first ? null : BuildPath(closest, false);
    }

    private static double Distance(VectorD left, VectorD right) => (left - right).Magnitude;
    private static Vector Key(VectorD position) => new((int)Math.Floor(position.X), (int)Math.Round(position.Y * 16), (int)Math.Floor(position.Z));

    private static MobPath BuildPath(Node end, bool reached)
    {
        var path = new List<VectorD>();
        for (Node? node = end; node != null; node = node.Parent)
            path.Add(node.Position);

        path.Reverse();
        return new MobPath(path, reached);
    }

    private sealed class Node(VectorD position, Node? parent, double cost)
    {
        public VectorD Position { get; } = position;
        public Node? Parent { get; } = parent;
        public double Cost { get; } = cost;
        public bool Closed { get; set; }
    }
}

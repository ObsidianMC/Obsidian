namespace Obsidian.Entities.AI;

internal sealed record MobPath(IReadOnlyList<VectorD> Nodes, bool ReachedTarget);

internal sealed class WalkNodeEvaluator(PathfinderMob mob)
{
    private readonly Dictionary<(int X, int Z, double Y), VectorD?> groundCache = [];
    private bool CanOpenDoors => mob.Navigator is Navigator { CanOpenDoors: true };
    private static readonly (int X, int Z)[] directions =
    [(-1, 0), (1, 0), (0, -1), (0, 1), (-1, -1), (-1, 1), (1, -1), (1, 1)];

    public VectorD? FindGround(int x, int z, double currentY)
    {
        var key = (x, z, currentY);
        if (!groundCache.TryGetValue(key, out var ground))
        {
            ground = EvaluateGround(x, z, currentY);
            groundCache.Add(key, ground);
        }
        return ground;
    }

    // Terrain and door permissions can change between searches, including a previously blocked column.
    public void Reset() => groundCache.Clear();

    private VectorD? EvaluateGround(int x, int z, double currentY)
    {
        var candidates = new List<double>();
        for (var y = (int)Math.Floor(currentY + 1); y >= (int)Math.Floor(currentY - 3) - 1; y--)
        {
            var block = mob.Terrain.GetBlock(new Vector(x, y, z));
            if (block == null)
                continue;
            if (CanOpenDoors && TagsRegistry.Block.WoodenDoors.Entries.Contains(block.RegistryId))
                continue;

            foreach (var shape in BlockCollisionShapes.Get(block))
            {
                if (shape.Min.X <= 0.5f && shape.Max.X >= 0.5f && shape.Min.Z <= 0.5f && shape.Max.Z >= 0.5f)
                    candidates.Add(y + shape.Max.Y);
            }
        }

        foreach (var y in candidates.Distinct().OrderByDescending(value => value))
        {
            if (y > currentY + 1.00001f || y < currentY - 3)
                continue;

            var position = new VectorD(x + 0.5f, y, z + 0.5f);
            if (mob.Terrain.IsFree(mob.Dimension.CreateBBFromPosition(position), CanOpenDoors) && GetCost(position) >= 0)
                return position;
        }

        return null;
    }

    public IEnumerable<VectorD> GetNeighbors(VectorD current)
    {
        foreach (var (x, z) in directions)
        {
            var next = FindGround((int)Math.Floor(current.X) + x, (int)Math.Floor(current.Z) + z, current.Y);
            if (next is not VectorD position)
                continue;

            if (x != 0 && z != 0 &&
                (FindGround((int)Math.Floor(current.X) + x, (int)Math.Floor(current.Z), current.Y) is not VectorD sideX ||
                 FindGround((int)Math.Floor(current.X), (int)Math.Floor(current.Z) + z, current.Y) is not VectorD sideZ ||
                 Math.Abs(sideX.Y - position.Y) > 0.5f || Math.Abs(sideZ.Y - position.Y) > 0.5f))
                continue;

            var raisedCurrent = new VectorD(current.X, Math.Max(current.Y, position.Y), current.Z);
            var raisedNext = new VectorD(position.X, raisedCurrent.Y, position.Z);
            var currentBounds = mob.Dimension.CreateBBFromPosition(raisedCurrent);
            var nextBounds = mob.Dimension.CreateBBFromPosition(raisedNext);
            var passage = new BoundingBox(VectorD.Min(currentBounds.Min, nextBounds.Min), VectorD.Max(currentBounds.Max, nextBounds.Max));
            if (mob.Terrain.IsFree(passage, CanOpenDoors))
                yield return position;
        }
    }

    public double GetCost(VectorD position)
    {
        var feet = mob.Terrain.GetBlock((Vector)position.Floor());
        var below = mob.Terrain.GetBlock((Vector)(position - new VectorD(0, 0.01f, 0)).Floor());
        return feet == null || below == null ? -1 : mob.GetPathCost(feet, below);
    }
}

internal sealed class PathFinder(PathfinderMob mob)
{
    private readonly WalkNodeEvaluator evaluator = new(mob);

    public MobPath? FindPath(VectorD target)
    {
        evaluator.Reset();
        var volume = VolumeMovement.UsesVolume(mob);
        var start = volume ? mob.Position : evaluator.FindGround((int)Math.Floor(mob.Position.X), (int)Math.Floor(mob.Position.Z), mob.Position.Y);
        if (start is not VectorD startPosition)
            return null;

        var range = Math.Max(16, mob.FollowRange);
        var open = new PriorityQueue<Node, (double Cost, int Order)>();
        var nodes = new Dictionary<Vector, Node>();
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

            if (distance <= 1)
                return BuildPath(current, true);

            foreach (var next in volume ? VolumeMovement.Neighbors(mob, current.Position) : evaluator.GetNeighbors(current.Position))
            {
                if (Distance(next, startPosition) > range)
                    continue;

                var cost = current.Cost + Distance(current.Position, next) + (volume ? 0 : evaluator.GetCost(next));
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

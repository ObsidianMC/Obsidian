namespace Obsidian.Entities.AI;

internal sealed record MobPath(IReadOnlyList<VectorF> Nodes, bool ReachedTarget);

internal sealed class WalkNodeEvaluator(PathfinderMob mob)
{
    private bool CanOpenDoors => mob.Navigator is Navigator { CanOpenDoors: true };
    private static readonly (int X, int Z)[] directions =
    [(-1, 0), (1, 0), (0, -1), (0, 1), (-1, -1), (-1, 1), (1, -1), (1, 1)];

    public VectorF? FindGround(int x, int z, float currentY)
    {
        var candidates = new List<float>();
        for (var y = (int)MathF.Floor(currentY + 1); y >= (int)MathF.Floor(currentY - 3) - 1; y--)
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

            var position = new VectorF(x + 0.5f, y, z + 0.5f);
            if (mob.Terrain.IsFree(mob.Dimension.CreateBBFromPosition(position), CanOpenDoors) && GetCost(position) >= 0)
                return position;
        }

        return null;
    }

    public IEnumerable<VectorF> GetNeighbors(VectorF current)
    {
        foreach (var (x, z) in directions)
        {
            var next = FindGround((int)MathF.Floor(current.X) + x, (int)MathF.Floor(current.Z) + z, current.Y);
            if (next is not VectorF position)
                continue;

            if (x != 0 && z != 0 &&
                (FindGround((int)MathF.Floor(current.X) + x, (int)MathF.Floor(current.Z), current.Y) is not VectorF sideX ||
                 FindGround((int)MathF.Floor(current.X), (int)MathF.Floor(current.Z) + z, current.Y) is not VectorF sideZ ||
                 MathF.Abs(sideX.Y - position.Y) > 0.5f || MathF.Abs(sideZ.Y - position.Y) > 0.5f))
                continue;

            var raisedCurrent = new VectorF(current.X, MathF.Max(current.Y, position.Y), current.Z);
            var raisedNext = new VectorF(position.X, raisedCurrent.Y, position.Z);
            var currentBounds = mob.Dimension.CreateBBFromPosition(raisedCurrent);
            var nextBounds = mob.Dimension.CreateBBFromPosition(raisedNext);
            var passage = new BoundingBox(VectorF.Min(currentBounds.Min, nextBounds.Min), VectorF.Max(currentBounds.Max, nextBounds.Max));
            if (mob.Terrain.IsFree(passage, CanOpenDoors))
                yield return position;
        }
    }

    public float GetCost(VectorF position)
    {
        var feet = mob.Terrain.GetBlock((Vector)position.Floor());
        var below = mob.Terrain.GetBlock((Vector)(position - new VectorF(0, 0.01f, 0)).Floor());
        return feet == null || below == null ? -1 : mob.GetPathCost(feet, below);
    }
}

internal sealed class PathFinder(PathfinderMob mob)
{
    private readonly WalkNodeEvaluator evaluator = new(mob);

    public MobPath? FindPath(VectorF target)
    {
        var start = evaluator.FindGround((int)MathF.Floor(mob.Position.X), (int)MathF.Floor(mob.Position.Z), mob.Position.Y);
        if (start is not VectorF startPosition)
            return null;

        var range = MathF.Max(16, mob.FollowRange);
        var open = new PriorityQueue<Node, (float Cost, int Order)>();
        var nodes = new Dictionary<Vector, Node>();
        var first = new Node(startPosition, null, 0);
        nodes.Add(Key(startPosition), first);
        open.Enqueue(first, (Distance(startPosition, target), 0));
        var closest = first;
        var closestDistance = Distance(first.Position, target);
        var order = 1;

        // ponytail: bounded synchronous search; use snapshots and worker searches if profiling requires it.
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

            foreach (var next in evaluator.GetNeighbors(current.Position))
            {
                if (Distance(next, startPosition) > range)
                    continue;

                var cost = current.Cost + Distance(current.Position, next) + evaluator.GetCost(next);
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

    private static float Distance(VectorF left, VectorF right) => (left - right).Magnitude;
    private static Vector Key(VectorF position) => new((int)MathF.Floor(position.X), (int)MathF.Round(position.Y * 16), (int)MathF.Floor(position.Z));

    private static MobPath BuildPath(Node end, bool reached)
    {
        var path = new List<VectorF>();
        for (Node? node = end; node != null; node = node.Parent)
            path.Add(node.Position);

        path.Reverse();
        return new MobPath(path, reached);
    }

    private sealed class Node(VectorF position, Node? parent, float cost)
    {
        public VectorF Position { get; } = position;
        public Node? Parent { get; } = parent;
        public float Cost { get; } = cost;
        public bool Closed { get; set; }
    }
}

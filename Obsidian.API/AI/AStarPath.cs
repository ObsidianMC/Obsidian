namespace Obsidian.API.AI;

internal class AStarPath
{
    public int MaxFallHeight { get; set; } = 5;

    public int MaxClimbHeight { get; set; } = 1;

    public int MaxRange { get; set; } = 15;

    public int EntityHeight { get; set; } = 2;

    public bool EntityCanFly { get; set; } = false;

    public bool EntityCanSwim { get; set; } = false;

    public bool EntityCanClimbWalls { get; set; } = false;

    public bool EntityCanClimbLadders { get; set; } = false;

    private readonly IWorld world;

    private readonly List<Vector> validMoves = new()
    {
        Vector.Left,
        Vector.Right,
        Vector.Forwards,
        Vector.Backwards,
        Vector.Up,
        Vector.Down,

        Vector.Left + Vector.Forwards,
        Vector.Left + Vector.Backwards,
        Vector.Right + Vector.Forwards,
        Vector.Right + Vector.Backwards,

        Vector.Forwards + Vector.Up,
        Vector.Backwards + Vector.Up,
        Vector.Left + Vector.Up,
        Vector.Right + Vector.Up,

        Vector.Forwards + Vector.Down,
        Vector.Backwards + Vector.Down,
        Vector.Left + Vector.Down,
        Vector.Right + Vector.Down,
    };

    public AStarPath(IWorld world)
    {
        this.world = world;
    }

    public List<Vector> GetPath(Vector startPos, Vector targetPos)
    {
        var startNode = new Node(null, startPos);
        var endNode = new Node(null, targetPos);

        // Out of range?
        float distanceSquared = (targetPos - startPos).MagnitudeSquared();
        if (distanceSquared > MaxRange * MaxRange)
            return new List<Vector>();


        List<Node> openList = new();
        List<Node> closedList = new();

        openList.Add(startNode);

        while (openList.Count > 0)
        {
            var currentNode = openList[0];

            for (var index = 0; index < openList.Count; index++)
            {
                var item = openList[index];
                if (item.f < currentNode.f)
                {
                    currentNode = item;
                }
            }

            openList.Remove(currentNode);
            closedList.Add(currentNode);

            // Made it!
            if (currentNode == endNode)
            {
                var result = new List<Vector>();
                var current = currentNode;

                while (current is not null)
                {
                    result.Add(current.Position);
                    current = current.Parent;
                }

                result.Reverse();
                return result;
            }

            var children = new List<Node>();
            foreach (var newPosition in validMoves)
            {
                var nodePosition = currentNode.Position + newPosition;

                if (!IsValidMove(currentNode.Position, nodePosition))
                    continue;

                var newNode = new Node(currentNode, nodePosition);
                children.Add(newNode);
            }

            foreach (var child in children)
            {
                if (closedList.Contains(child))
                    continue;

                child.g = currentNode.g + 1;

                child.h = Math.Pow(child.Position.X - endNode.Position.X, 2);
                child.h += Math.Pow(child.Position.Y - endNode.Position.Y, 2);
                child.h += Math.Pow(child.Position.Z - endNode.Position.Z, 2);

                child.f = child.g + child.h;

                if (openList.Any(openNode => openNode == child && child.g > openNode.g))
                    continue;

                openList.Add(child);
            }
        }

        return new List<Vector>();
    }

    private bool IsValidMove(Vector curPos, Vector nextPos)
    {
        // Does the entity fit?
        /*for (int y = curPos.Y; y < Math.Max(curPos.Y, nextPos.Y) + EntityHeight; y++)
        {
            var b = world.GetBlock(nextPos.X, y, nextPos.Z);
            if (b.IsMotionBlocking)
            {
                return false;
            }
        }

        // Would the fall kill the entity?
        bool allAir = true;
        for (int y = nextPos.Y; y >= nextPos.Y - MaxFallHeight; y--)
        {
            var b = world.GetBlock(nextPos.X, y, nextPos.Z);
            if (!b.isAir)
            {
                allAir = false;
                break;
            }
        }

        if (allAir) 
            return false;

        // Can the entity jump/climb high enough?
        int height = nextPos.Y - curPos.Y;
        if (height > MaxClimbHeight)
            return false;

        // Can the entity swim?
        if (world.GetBlock(nextPos).isLiquid && !EntityCanSwim)
            return false;*/

        return true;
    }

    private class Node : IEquatable<Node>
    {
        public Node? Parent { get; }

        public Vector Position { get; }

        public double f = 0;

        public double g = 0;

        public double h = 0;


        public Node(Node? parent, Vector position)
        {
            Parent = parent;
            Position = position;
        }

        public bool Equals(Node? other) => other?.Position == Position;

        public override bool Equals(object? obj)
        {
            return Equals(obj as Node);
        }

        public static bool operator ==(Node? left, Node? right) { return left?.Position == right?.Position; }

        public static bool operator !=(Node? left, Node? right) { return !(left == right); }

        public override int GetHashCode() => Position.GetHashCode();
    }
}

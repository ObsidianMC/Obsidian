using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Obsidian.API.AI
{
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
            Vector.Down
        };

        public AStarPath(IWorld world)
        {
            this.world = world;
        }

        public List<Vector> GetPath(Vector startPos, Vector targetPos)
        {
            var startNode = new Node(null, startPos);
            var endNode = new Node(null, targetPos);

            List<Node> openList = new();
            List<Node> closedList = new();

            openList.Add(startNode);

            while (openList.Count > 0)
            {
                var currentNode = openList[0];
                var currentIndex = 0;

                for (var index = 0; index < openList.Count; index++)
                {
                    var item = openList[index];
                    if (item.f < currentNode.f)
                    {
                        currentNode = item;
                        currentIndex = index;
                    }
                }

                openList.Remove(currentNode);
                closedList.Append(currentNode);
                
                // Made it!
                if (currentNode == endNode)
                {
                    var result = new List<Vector>();
                    var current = currentNode;

                    while (current is not null)
                    {
                        result.Append(current.Position);
                        current = current.Parent;
                    }

                    result.Reverse();
                    return result;
                }

                var children = new List<Node>();
                foreach (var newPosition in validMoves)
                {
                    var nodePosition = currentNode.Position + newPosition;

                    if (!IsValidMove(nodePosition))
                        continue;

                    var newNode = new Node(currentNode, nodePosition);
                    children.Append(newNode);
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

                    foreach (var openNode in openList)
                    {
                        if (child == openNode && child.g > openNode.g)
                        {
                            continue;
                        }
                    }

                    openList.Append(child);
                }
            }

            return new List<Vector>();
        }

        private bool IsValidMove(Vector nextPos)
        {
            //var surfaceBlock =  world.GetBlock(nextPos);
            
            // Does the entity fit?

            // Would the fall kill the entity?

            // Can the entity jump/climb high enough?

            return true;
        }

        private class Node
        {
            public Node Parent { get; }

            public Vector Position { get; }

            public double f = 0;

            public double g = 0;

            public double h = 0;


            public Node(Node parent, Vector position)
            {
                Parent = parent;
                Position = position;
            }

            public bool IsEqual(Node other) => other.Position == this.Position;
        }
    }
}

using Obsidian.API;

namespace Obsidian.WorldData.Generators.Overworld.Features.Trees
{
    public abstract class BaseTree
    {
        protected readonly World world;

        protected readonly Material leaf, trunk;

        protected readonly int trunkHeight;

        protected BaseTree(World world, Material leaf, Material trunk, int trunkHeight)
        {
            this.world = world;
            this.leaf = leaf;
            this.trunk = trunk;
            this.trunkHeight = trunkHeight;
        }

        public virtual void GenerateTree(Vector origin, int heightOffset)
        {
            GenerateLeaves(origin, heightOffset);
            GenerateTrunk(origin, heightOffset);
        }

        protected virtual void GenerateLeaves(Vector origin, int heightOffset)
        {
            int topY = trunkHeight + heightOffset + 1;
            for (int y = topY; y >= topY - 4; y--)
            {
                for (int x = origin.X - 2; x <= origin.X + 2; x++)
                {
                    for (int z = origin.Z - 2; z <= origin.Z + 2; z++)
                    {
                        // Skip the top edges.
                        if (y == topY)
                        {
                            if (x != origin.X - 2 && x != origin.X + 2 && z != origin.Z - 2 && z != origin.Z + 2)
                            {
                                world.SetBlock(origin + (x, y, z), new Block(leaf));
                            }
                        }
                        else
                        {
                            world.SetBlock(origin + (x, y, z), new Block(leaf));
                        }
                    }
                }
            }
        }

        protected virtual void GenerateTrunk(Vector origin, int heightOffset)
        {
            int topY = trunkHeight + heightOffset;
            for (int y = topY; y >= origin.Y; y--)
            {
                world.SetBlock(origin + (0, y, 0), new Block(trunk));
            }
        }
    }
}

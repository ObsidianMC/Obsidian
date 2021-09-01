using Obsidian.API;

namespace Obsidian.WorldData.Generators.Overworld.Features.Trees
{
    public class LargeOakTree : BaseTree
    {
        public LargeOakTree(World world) : base(world, Material.OakLeaves, Material.OakLog, 12)
        {
        }

        private const bool V = false;
        private const bool X = true;
        private readonly bool[,] level0 = new bool[10, 10]
        {
            {V, V, V, V, V, V, V, V, V, V},
            {V, V, V, V, V, V, V, V, V, V},
            {V, V, V, V, V, V, V, V, V, V},
            {V, V, V, V, X, X, V, V, V, V},
            {V, V, V, X, X, X, X, V, V, V},
            {V, V, V, X, X, X, X, V, V, V},
            {V, V, V, V, X, X, V, V, V, V},
            {V, V, V, V, V, V, V, V, V, V},
            {V, V, V, V, V, V, V, V, V, V},
            {V, V, V, V, V, V, V, V, V, V}
        };

        private readonly bool[,] level1 = new bool[10, 10]
        {
            {V, V, V, V, V, V, V, V, V, V},
            {V, V, V, V, V, V, V, V, V, V},
            {V, V, V, V, X, X, V, V, V, V},
            {V, V, V, X, X, X, X, V, V, V},
            {V, V, X, X, X, X, X, X, V, V},
            {V, V, X, X, X, X, X, X, V, V},
            {V, V, V, X, X, X, X, V, V, V},
            {V, V, V, V, X, X, V, V, V, V},
            {V, V, V, V, V, V, V, V, V, V},
            {V, V, V, V, V, V, V, V, V, V}
        };

        private readonly bool[,] level2 = new bool[10, 10]
        {
            {V, V, V, V, V, V, V, V, V, V},
            {V, V, V, V, X, X, V, V, V, V},
            {V, V, V, X, X, X, X, V, V, V},
            {V, V, X, X, X, X, X, X, V, V},
            {V, X, X, X, X, X, X, X, X, V},
            {V, X, X, X, X, X, X, X, X, V},
            {V, V, X, X, X, X, X, X, V, V},
            {V, V, V, X, X, X, X, V, V, V},
            {V, V, V, V, X, X, V, V, V, V},
            {V, V, V, V, V, V, V, V, V, V}
        };

        private readonly bool[,] level3 = new bool[10, 10]
        {
            {V, V, V, V, X, X, V, V, V, V},
            {V, V, X, X, X, X, X, X, V, V},
            {V, X, X, X, X, X, X, X, X, V},
            {V, X, X, X, X, X, X, X, X, V},
            {X, X, X, X, X, X, X, X, X, X},
            {X, X, X, X, X, X, X, X, X, X},
            {V, X, X, X, X, X, X, X, X, V},
            {V, X, X, X, X, X, X, X, X, V},
            {V, V, X, X, X, X, X, X, V, V},
            {V, V, V, V, X, X, V, V, V, V}
        };

        protected override void GenerateTrunk(Vector origin, int heightOffset)
        {
            int topY = trunkHeight + heightOffset;
            for (int x = 0; x < 2; x++)
            {
                for (int z = 0; z < 2; z++)
                {
                    for (int y = topY; y > 0; y--)
                    {
                        world.SetBlockUntracked(origin + (x, y, z), new Block(trunk, 1));
                    }

                    // Fill in any air gaps under the trunk
                    if (world.GetBlock(origin+(x, -1, z)).Value.IsAir)
                    {
                        world.SetBlockUntracked(origin + (x, -1, z), new Block(trunk, 1));
                    }
                }
            }

            // Turn the ground around the trunk into podzol
            for (int x = 0; x < 10; x++)
            {
                for (int z = 0; z < 10; z++)
                {
                    if (level2[x, z])
                    {
                        for (int y = -2; y < 2; y++)
                        {
                            if ((Material)world.GetBlock(origin + (x - 4, y, z - 4)).Value.Id == Material.GrassBlock)
                                world.SetBlockUntracked(origin + (x - 4, y, z - 4), new Block(Material.Podzol, 1));
                        }
                    }
                }
            }
        }

        protected override void GenerateLeaves(Vector origin, int heightOffset)
        {
            int topY = trunkHeight + heightOffset;
            int y = topY + 1;
            for (int level = 0; level < 6; level++)
            {
                var leaves = level switch
                {
                    1 => level1,
                    2 => level2,
                    3 => level3,
                    4 => level3,
                    5 => level2,
                    _ => level0,
                };
                for (int x = 0; x < 10; x++)
                {
                    for (int z = 0; z < 10; z++)
                    {
                        if (leaves[x, z])
                        {
                            world.SetBlockUntracked(origin + (x - 4, y - level, z - 4), new Block(leaf));
                        }
                    }
                }
            }

        }
    }
}

namespace Obsidian.WorldData.Generators.Overworld.Features.Trees;

public class AcaciaTree : BaseTree
{
    public AcaciaTree(World world) : base(world, Material.AcaciaLeaves, Material.AcaciaLog, 7)
    {
    }

    protected override void GenerateLeaves(Vector origin, int heightOffset)
    {
        int topY = origin.Y + trunkHeight + heightOffset + 1;
        for (int y = topY; y >= topY - 1; y--)
        {
            for (int x = origin.X - 3; x <= origin.X + 3; x++)
            {
                for (int z = origin.Z - 3; z <= origin.Z + 3; z++)
                {
                    // Skip the top edges.
                    if (y == topY)
                    {
                        if (x != origin.X - 3 && x != origin.X + 3 && z != origin.Z - 3 && z != origin.Z + 3)
                        {
                            world.SetBlockUntracked(x, y, z, new Block(leaf));
                        }
                    }
                    else if (!(
                        (x == origin.X - 3 && z == origin.Z - 3) ||
                        (x == origin.X - 3 && z == origin.Z + 3) ||
                        (x == origin.X + 3 && z == origin.Z - 3) ||
                        (x == origin.X + 3 && z == origin.Z + 3)
                        ))
                    {
                        world.SetBlockUntracked(x, y, z, new Block(leaf));
                    }
                }
            }
        }
    }
}

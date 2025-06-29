using Obsidian.WorldData.Generators;

namespace Obsidian.WorldData.Features.Trees;

public class AcaciaTree(GenHelper helper, IChunk chunk) : BaseTree(helper, chunk, Material.AcaciaLeaves, Material.AcaciaLog, 7)
{
    protected async override Task GenerateLeavesAsync(Vector origin, int heightOffset)
    {
        int topY = origin.Y + TrunkHeight + heightOffset + 1;
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
                            await GenHelper.SetBlockAsync(x, y, z, this.LeafBlock, Chunk);
                        }
                    }
                    else if (!(
                        (x == origin.X - 3 && z == origin.Z - 3) ||
                        (x == origin.X - 3 && z == origin.Z + 3) ||
                        (x == origin.X + 3 && z == origin.Z - 3) ||
                        (x == origin.X + 3 && z == origin.Z + 3)
                        ))
                    {
                        await GenHelper.SetBlockAsync(x, y, z, this.LeafBlock, Chunk);
                    }
                }
            }
        }
    }
}

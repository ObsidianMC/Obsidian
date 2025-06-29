using Obsidian.WorldData.Generators;

namespace Obsidian.WorldData.Features.Trees;

public class LargeOakTree(GenHelper helper, IChunk chunk) : BaseTree(helper, chunk, Material.OakLeaves, Material.OakLog, 12)
{
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

    protected override async Task GenerateTrunkAsync(Vector origin, int heightOffset)
    {
        int topY = TrunkHeight + heightOffset;
        for (int x = 0; x < 2; x++)
        {
            for (int z = 0; z < 2; z++)
            {
                for (int y = topY; y > 0; y--)
                {
                    await GenHelper.SetBlockAsync(origin + (x, y, z), this.TrunkBlock, Chunk);
                }

                // Fill in any air gaps under the trunk
                var under = await GenHelper.GetBlockAsync(origin + (x, -1, z), Chunk);
                if (under.IsAir)
                {
                    await GenHelper.SetBlockAsync(origin + (x, -1, z), this.TrunkBlock, Chunk);
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
                        var b = await GenHelper.GetBlockAsync(origin + (x - 4, y, z - 4), Chunk);
                        if (b.Is(Material.GrassBlock))
                            await GenHelper.SetBlockAsync(origin + (x - 4, y, z - 4), BlocksRegistry.Podzol, Chunk);
                    }
                }
            }
        }
    }

    protected override async Task GenerateLeavesAsync(Vector origin, int heightOffset)
    {
        int topY = TrunkHeight + heightOffset;
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
                        await GenHelper.SetBlockAsync(origin + (x - 4, y - level, z - 4), this.LeafBlock, Chunk);
                    }
                }
            }
        }

    }
}

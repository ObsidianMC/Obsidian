using Obsidian.Utilities.Registry;

namespace Obsidian.WorldData.Generators.Overworld.Features.Trees;

public abstract class BaseTree
{
    protected readonly IBlock leafBlock;
    protected readonly IBlock trunkBlock;

    protected readonly GenHelper helper;

    protected readonly Chunk chunk;

    protected readonly Material leaf, trunk;

    protected int trunkHeight;

    protected readonly List<Material> ValidSourceBlocks = new()
    {
        Material.GrassBlock,
        Material.Dirt,
        Material.Podzol,
        Material.Farmland,
        Material.SnowBlock
    };

    protected BaseTree(GenHelper helper, Chunk chunk, Material leaf, Material trunk, int trunkHeight)
    {
        this.helper = helper;
        this.chunk = chunk;
        this.leaf = leaf;
        this.trunk = trunk;
        this.trunkHeight = trunkHeight;

        this.leafBlock = BlocksRegistry.Get(leaf);
        this.trunkBlock = BlocksRegistry.Get(trunk);
    }

    public virtual async Task<bool> TryGenerateTreeAsync(Vector origin, int heightOffset)
    {
        if (!await TreeCanGrowAsync(origin)) { return false; }
        await GenerateLeavesAsync(origin, heightOffset);
        await GenerateTrunkAsync(origin, heightOffset);
        return true;
    }

    protected virtual async Task GenerateLeavesAsync(Vector origin, int heightOffset)
    {
        int topY = origin.Y + trunkHeight + heightOffset + 1;
        for (int y = topY; y >= topY - 3; y--)
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
                            await helper.SetBlockAsync(x, y, z, this.leafBlock, chunk);
                        }
                    }
                    else
                    {
                        await helper.SetBlockAsync(x, y, z, this.leafBlock, chunk);
                    }
                }
            }
        }
    }

    protected virtual async Task GenerateTrunkAsync(Vector origin, int heightOffset)
    {
        int topY = trunkHeight + heightOffset;
        for (int y = topY; y > 0; y--)
        {
            await helper.SetBlockAsync(origin + (0, y, 0), this.trunkBlock, chunk);//TODO state == 1
        }
        await helper.SetBlockAsync(origin, BlocksRegistry.Dirt, chunk);
    }

    protected virtual async Task<bool> TreeCanGrowAsync(Vector origin)
    {
        var surfaceBlock = await helper.GetBlockAsync(origin, chunk);
        bool surfaceValid = ValidSourceBlocks.Contains(surfaceBlock.Material);

        bool plentyOfRoom =
            (await helper.GetBlockAsync(origin + (-1, 2, -1), chunk)).IsAir &&
            (await helper.GetBlockAsync(origin + (-1, 2, 0), chunk)).IsAir &&
            (await helper.GetBlockAsync(origin + (-1, 2, 1), chunk)).IsAir &&
            (await helper.GetBlockAsync(origin + (0, 2, -1), chunk)).IsAir &&
            (await helper.GetBlockAsync(origin + (0, 2, 0), chunk)).IsAir &&
            (await helper.GetBlockAsync(origin + (0, 2, 1), chunk)).IsAir &&
            (await helper.GetBlockAsync(origin + (1, 2, -1), chunk)).IsAir &&
            (await helper.GetBlockAsync(origin + (1, 2, 0), chunk)).IsAir &&
            (await helper.GetBlockAsync(origin + (1, 2, 1), chunk)).IsAir;

        return surfaceValid && plentyOfRoom;
    }
}

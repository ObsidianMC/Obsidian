namespace Obsidian.WorldData.Generators.Overworld.Features.Trees;

public abstract class BaseTree
{
    protected readonly World world;

    protected readonly Material leaf, trunk;

    protected readonly int trunkHeight;

    protected readonly List<Material> ValidSourceBlocks = new()
    {
        Material.GrassBlock,
        Material.Dirt,
        Material.Podzol,
        Material.Farmland,
        Material.SnowBlock
    };

    protected BaseTree(World world, Material leaf, Material trunk, int trunkHeight)
    {
        this.world = world;
        this.leaf = leaf;
        this.trunk = trunk;
        this.trunkHeight = trunkHeight;
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
                            await world.SetBlockUntrackedAsync(x, y, z, new Block(leaf));
                        }
                    }
                    else
                    {
                        await world.SetBlockUntrackedAsync(x, y, z, new Block(leaf));
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
            await world.SetBlockUntrackedAsync(origin + (0, y, 0), new Block(trunk, 1));
        }
        await world.SetBlockUntrackedAsync(origin, new Block(Material.Dirt));
    }

    protected virtual async Task<bool> TreeCanGrowAsync(Vector origin)
    {
        var surfaceBlock = (Block) await world.GetBlockAsync(origin);
        bool surfaceValid = ValidSourceBlocks.Contains(surfaceBlock.Material);

        bool plentyOfRoom =
            ((Block) await world.GetBlockAsync(origin + (-1, 2, -1))).IsAir &&
            ((Block) await world.GetBlockAsync(origin + (-1, 2, 0))).IsAir &&
            ((Block) await world.GetBlockAsync(origin + (-1, 2, 1))).IsAir &&
            ((Block) await world.GetBlockAsync(origin + (0, 2, -1))).IsAir &&
            ((Block) await world.GetBlockAsync(origin + (0, 2, 0))).IsAir &&
            ((Block) await world.GetBlockAsync(origin + (0, 2, 1))).IsAir &&
            ((Block) await world.GetBlockAsync(origin + (1, 2, -1))).IsAir &&
            ((Block) await world.GetBlockAsync(origin + (1, 2, 0))).IsAir &&
            ((Block) await world.GetBlockAsync(origin + (1, 2, 1))).IsAir;


        return surfaceValid && plentyOfRoom;
    }
}

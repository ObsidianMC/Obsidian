using Obsidian.API.BlockStates.Builders;
using Obsidian.WorldData.Generators;

namespace Obsidian.WorldData.Features.Trees;

public abstract class BaseTree
{
    protected IBlock LeafBlock { get; set; }
    protected IBlock TrunkBlock { get; set; }

    protected GenHelper GenHelper { get; }

    protected IChunk Chunk { get; }

    protected int TrunkHeight { get; set; }

    protected List<Material> ValidSourceBlocks { get; set; } =
    [
        Material.GrassBlock,
        Material.Dirt,
        Material.Podzol,
        Material.Farmland,
        Material.SnowBlock
    ];

    protected BaseTree(GenHelper helper, IChunk chunk, Material leaf, Material trunk, int trunkHeight)
    {
        this.GenHelper = helper;
        this.Chunk = chunk;
        this.TrunkHeight = trunkHeight;

        this.LeafBlock = BlocksRegistry.Get(leaf);

        IBlockState? state = trunk switch
        {
            Material.AcaciaLog => new AcaciaLogStateBuilder().WithAxis(Axis.Y).Build(),
            Material.JungleLog => new JungleLogStateBuilder().WithAxis(Axis.Y).Build(),
            Material.BirchLog => new BirchLogStateBuilder().WithAxis(Axis.Y).Build(),
            Material.DarkOakLog => new DarkOakLogStateBuilder().WithAxis(Axis.Y).Build(),
            Material.MangroveLog => new MangroveLogStateBuilder().WithAxis(Axis.Y).Build(),
            Material.OakLog => new OakLogStateBuilder().WithAxis(Axis.Y).Build(),
            Material.SpruceLog => new SpruceLogStateBuilder().WithAxis(Axis.Y).Build(),
            _ => null
        };

        this.TrunkBlock = BlocksRegistry.Get(trunk, state);
    }

    public async virtual Task<bool> TryGenerateTreeAsync(Vector origin, int heightOffset)
    {
        if (!await TreeCanGrowAsync(origin)) { return false; }

        await GenerateLeavesAsync(origin, heightOffset);
        await GenerateTrunkAsync(origin, heightOffset);
        return true;
    }

    protected async virtual Task GenerateLeavesAsync(Vector origin, int heightOffset)
    {
        // Make leaves
        for (int xx = -2; xx <= 2; xx++)
        {
            for (int zz = -2; zz <= 2; zz++)
            {
                await GenHelper.SetBlockAsync(origin.X + xx, TrunkHeight + origin.Y - 1 + heightOffset, origin.Z + zz,
                    this.LeafBlock,
                    Chunk);
                await GenHelper.SetBlockAsync(origin.X + xx, TrunkHeight + origin.Y + heightOffset, origin.Z + zz,
                    this.LeafBlock, Chunk);

                if (Math.Abs(xx) < 2 && Math.Abs(zz) < 2)
                {
                    await GenHelper.SetBlockAsync(origin.X + xx, TrunkHeight + origin.Y + 1 + heightOffset, origin.Z + zz,
                        this.LeafBlock,
                        Chunk);

                    if (xx == 0 || zz == 0)
                        await GenHelper.SetBlockAsync(origin.X + xx, TrunkHeight + origin.Y + heightOffset, origin.Z + zz,
                            this.LeafBlock, Chunk);
                }
            }
        }
    }

    protected async virtual Task GenerateTrunkAsync(Vector origin, int heightOffset)
    {
        int topY = TrunkHeight + heightOffset;
        for (int y = topY; y > 0; y--)
        {
            await GenHelper.SetBlockAsync(origin + (0, y, 0), this.TrunkBlock, Chunk);
        }

        await GenHelper.SetBlockAsync(origin, BlocksRegistry.Dirt, Chunk);
    }

    protected async virtual Task<bool> TreeCanGrowAsync(Vector origin)
    {
        var surfaceBlock = await GenHelper.GetBlockAsync(origin, Chunk);
        bool surfaceValid = ValidSourceBlocks.Contains(surfaceBlock.Material);

        bool plentyOfRoom =
            (await GenHelper.GetBlockAsync(origin + (-1, 2, -1), Chunk)).IsAir &&
            (await GenHelper.GetBlockAsync(origin + (-1, 2, 0), Chunk)).IsAir &&
            (await GenHelper.GetBlockAsync(origin + (-1, 2, 1), Chunk)).IsAir &&
            (await GenHelper.GetBlockAsync(origin + (0, 2, -1), Chunk)).IsAir &&
            (await GenHelper.GetBlockAsync(origin + (0, 2, 0), Chunk)).IsAir &&
            (await GenHelper.GetBlockAsync(origin + (0, 2, 1), Chunk)).IsAir &&
            (await GenHelper.GetBlockAsync(origin + (1, 2, -1), Chunk)).IsAir &&
            (await GenHelper.GetBlockAsync(origin + (1, 2, 0), Chunk)).IsAir &&
            (await GenHelper.GetBlockAsync(origin + (1, 2, 1), Chunk)).IsAir;

        return surfaceValid && plentyOfRoom;
    }
}

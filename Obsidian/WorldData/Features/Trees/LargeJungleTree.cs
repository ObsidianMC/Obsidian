using Obsidian.WorldData.Generators;

namespace Obsidian.WorldData.Features.Trees;

public class LargeJungleTree : JungleTree
{
    private readonly XorshiftRandom rand = new();

    public LargeJungleTree(GenHelper helper, IChunk chunk) : base(helper, chunk)
    {
        leavesRadius = 6;
        TrunkHeight = 20;
    }

    protected async override Task GenerateLeavesAsync(Vector origin, int heightOffset)
    {
        List<Vector> vineCandidates = new();
        int topY = origin.Y + TrunkHeight + heightOffset + 1;

        for (int y = topY - 3; y < topY + 1; y++)
        {
            for (int x = -leavesRadius; x <= leavesRadius + 1; x++)
            {
                for (int z = -leavesRadius; z <= leavesRadius + 1; z++)
                {
                    if (Math.Sqrt((x - 0.5) * (x - 0.5) + (z - 0.5) * (z - 0.5)) < leavesRadius)
                    {
                        if (await GenHelper.GetBlockAsync(x + origin.X, y, z + origin.Z, Chunk) is { IsAir: true })
                        {
                            await GenHelper.SetBlockAsync(x + origin.X, y, z + origin.Z, LeafBlock, Chunk);
                            if (rand.Next(4) == 0)
                            {
                                vineCandidates.Add(new Vector(x + origin.X, y, z + origin.Z));
                            }
                        }
                    }
                }
            }
            leavesRadius--;
        }
        await PlaceVinesAsync(vineCandidates);
    }

    protected override async Task GenerateTrunkAsync(Vector origin, int heightOffset)
    {
        List<Vector> vineCandidates = new();
        int topY = TrunkHeight + heightOffset;

        for (int x = 0; x < 2; x++)
        {
            for (int z = 0; z < 2; z++)
            {
                for (int y = topY; y > 0; y--)
                {
                    await GenHelper.SetBlockAsync(origin + (x, y, z), this.TrunkBlock, Chunk);

                    // Roll the dice to place a vine on this trunk block.
                    if (rand.Next(10) == 0)
                    {
                        vineCandidates.Add(origin + (x, y, z));
                    }
                }

                // Fill in any air gaps under the trunk
                var under = await GenHelper.GetBlockAsync(origin + (x, -1, z), Chunk);
                if (under.Material != Material.GrassBlock)
                {
                    await GenHelper.SetBlockAsync(origin + (x, -1, z), this.TrunkBlock, Chunk);
                }
            }
        }
        await PlaceVinesAsync(vineCandidates);
    }
}

namespace Obsidian.WorldData.Generators.Overworld.Features.Trees;

public class LargeJungleTree : JungleTree
{
    private readonly XorshiftRandom rand = new();

    public LargeJungleTree(GenHelper helper, Chunk chunk) : base(helper, chunk)
    {
        leavesRadius = 6;
        trunkHeight = 20;
    }

    protected override async Task GenerateLeavesAsync(Vector origin, int heightOffset)
    {
        List<Vector> vineCandidates = new();
        int topY = origin.Y + trunkHeight + heightOffset + 1;

        for (int y = topY - 3; y < topY + 1; y++)
        {
            for (int x = -leavesRadius; x <= leavesRadius + 1; x++)
            {
                for (int z = -leavesRadius; z <= leavesRadius + 1; z++)
                {
                    if (Math.Sqrt((x - 0.5) * (x - 0.5) + (z - 0.5) * (z - 0.5)) < leavesRadius)
                    {
                        if (await helper.GetBlockAsync(x + origin.X, y, z + origin.Z, chunk) is { IsAir: true })
                        {
                            await helper.SetBlockAsync(x + origin.X, y, z + origin.Z, leafBlock, chunk);
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
        int topY = trunkHeight + heightOffset;

        for (int x = 0; x < 2; x++)
        {
            for (int z = 0; z < 2; z++)
            {
                for (int y = topY; y > 0; y--)
                {
                    await helper.SetBlockAsync(origin + (x, y, z), this.trunkBlock, chunk);

                    // Roll the dice to place a vine on this trunk block.
                    if (rand.Next(10) == 0)
                    {
                        vineCandidates.Add(origin + (x, y, z));
                    }
                }

                // Fill in any air gaps under the trunk
                var under = await helper.GetBlockAsync(origin + (x, -1, z), chunk);
                if (under.Material != Material.GrassBlock)
                {
                    await helper.SetBlockAsync(origin + (x, -1, z), this.trunkBlock, chunk);
                }
            }
        }
        await PlaceVinesAsync(vineCandidates);
    }
}
